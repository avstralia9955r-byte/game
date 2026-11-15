using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Frontline.Bots
{
    /// <summary>
    /// Слух: боты реагируют на шумовые события врага (выстрелы, взрывы, шаги, стройка) в радиусе события × слух.
    /// Выстрел с глушителем публикуется с радиусом ≈ 0 и до ботов не доходит вовсе.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BotSnapshotSystem))]
    public partial struct BotHearingSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            if (!CombatWorld.Initialized) return;
            CombatWorld.SwapNoiseBuffers();
            if (CombatWorld.Noise.Length == 0) return;
            state.Dependency = new HearJob
            {
                noise = CombatWorld.Noise.AsArray(),
                now = (float)SystemAPI.Time.ElapsedTime
            }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
        }

        [BurstCompile]
        [WithNone(typeof(BotDead))]
        partial struct HearJob : IJobEntity
        {
            [ReadOnly] public NativeArray<NoiseEvent> noise;
            public float now;

            void Execute(ref BotPerception p, in BotIdentity id, in BotSkill skill, in LocalTransform t)
            {
                float bestScore = float.MaxValue;
                bool heard = false;
                for (int i = 0; i < noise.Length; i++)
                {
                    var n = noise[i];
                    if (n.team == id.team) continue;
                    // Шаги/стройку слышат только боты со «слухом» (Medium+); выстрелы и взрывы слышат все.
                    if ((n.kind == 2 || n.kind == 3) && !skill.reactsToSound) continue;
                    float r = n.radius * skill.hearingMul;
                    float d2 = math.distancesq(n.position, t.Position);
                    if (d2 > r * r) continue;
                    float score = d2 / (r * r) - (n.kind == 1 ? 0.5f : 0); // ближе и громче — приоритетнее
                    if (score < bestScore) { bestScore = score; p.heardPos = n.position; heard = true; }
                }
                if (heard) p.heardTime = now;
            }
        }
    }

    /// <summary>
    /// Зрение: угол обзора 70° (±35°), вне конуса — только вплотную (2,5 м). Увиденного противника бот замечает
    /// не сразу: шкала внимания растёт тем быстрее, чем ближе цель, чем она быстрее движется и чем ближе к центру
    /// взгляда; присевшего и неподвижного разглядеть трудно — к врагу можно подкрасться. Уже замеченную цель бот
    /// отслеживает сразу. Три этапа, все в Burst/джобах:
    /// A) выбор кандидата по дистанции и конусу обзора (каждый бот — раз в 3 кадра, со сдвигом по actorId);
    /// B) пакетные лучи PhysX (RaycastCommand) глаза → грудь цели на слой мира (здания, кирпичи, рельеф);
    /// C) обновление восприятия по результатам.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BotHearingSystem))]
    public partial struct BotVisionSystem : ISystem
    {
        int _frame;

        public void OnUpdate(ref SystemState state)
        {
            int n = BotRegistry.BotCount;
            if (!CombatWorld.Initialized || n == 0) return;
            _frame++;

            var cmds = new NativeArray<RaycastCommand>(n, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(n, Allocator.TempJob);
            float now = (float)SystemAPI.Time.ElapsedTime;

            var pick = new PickCandidateJob
            {
                actors = CombatWorld.Actors.AsArray(), commands = cmds, frame = _frame, now = now,
                viewer = UnityEngine.Camera.main ? (float3)UnityEngine.Camera.main.transform.position : float3.zero,
                lod = BotLod.Scale,
                scene = Physics.defaultPhysicsScene,
                // Луч зрения упирается и в листву (триггеры кустов и изгородей на слое заслонов) — за кустом не видно
                qp = new QueryParameters(CombatWorld.WorldMask | (1 << MapGenerator.VisionOccluderLayer), false, QueryTriggerInteraction.Collide, false)
            }.ScheduleParallel(state.Dependency);

            var ray = RaycastCommand.ScheduleBatch(cmds, hits, 16, 1, pick);

            state.Dependency = new ApplyVisibilityJob
            {
                actors = CombatWorld.Actors.AsArray(), hits = hits, now = now
            }.ScheduleParallel(ray);
            state.Dependency.Complete();

            cmds.Dispose(); hits.Dispose();
        }

        [BurstCompile]
        [WithNone(typeof(BotDead))]
        partial struct PickCandidateJob : IJobEntity
        {
            [ReadOnly] public NativeArray<ActorSnapshot> actors;
            [NativeDisableParallelForRestriction] public NativeArray<RaycastCommand> commands;
            public PhysicsScene scene;
            public QueryParameters qp;
            public int frame;
            public float now;
            public float3 viewer;
            public float lod;

            void Execute(ref BotPerception p, in BotIdentity id, in BotSkill skill, in LocalTransform t, in BotMotion m)
            {
                int slot = id.actorId - 1;
                if (slot < 0 || slot >= commands.Length) return;

                // Зрение — раз в 3 кадра; у дальних от камеры (> 250 м) — раз в 6
                float fd = 250f * lod;
                int every = math.distancesq(t.Position, viewer) > fd * fd ? 6 : 3;
                if ((frame + id.actorId) % every != 0) { p.candidateActorId = -2; commands[slot] = new RaycastCommand(scene, t.Position, new float3(0, 1, 0), qp, 0f); return; }

                float3 eye = t.Position + new float3(0, m.crouched ? 1.1f : 1.6f, 0);
                float3 fwd = math.mul(t.Rotation, new float3(0, 0, 1));
                int best = -1; float bestScore = float.MaxValue;
                float range2 = skill.visionRange * skill.visionRange;

                for (int i = 0; i < actors.Length; i++)
                {
                    var a = actors[i];
                    if (!a.alive || a.team == id.team) continue;
                    float3 to = a.position - t.Position;
                    float d2 = math.lengthsq(to);
                    if (d2 > range2) continue;
                    float d = math.sqrt(d2);
                    // Вне конуса 70° — заметит только вплотную (2,5 м), либо если цель уже отслеживается.
                    bool inCone = math.dot(fwd, to / math.max(d, 0.001f)) >= skill.fovCos;
                    bool tracked = a.actorId == p.targetActorId && now - p.lastSeenTime < 2f;
                    if (!inCone && d > 2.5f && !tracked) continue;
                    // Присевшие и далёкие замечаются хуже.
                    float score = d * (a.crouched ? 1.5f : 1f) * (tracked ? 0.6f : 1f);
                    if (score < bestScore) { bestScore = score; best = i; }
                }

                if (best < 0) { p.candidateActorId = -1; commands[slot] = new RaycastCommand(scene, t.Position, new float3(0, 1, 0), qp, 0f); return; }

                var target = actors[best];
                float3 chest = target.position + new float3(0, target.crouched ? 0.9f : 1.3f, 0);
                float3 dir = chest - eye;
                float len = math.length(dir);
                p.candidateActorId = target.actorId;
                commands[slot] = new RaycastCommand(scene, eye, dir / len, qp, math.max(0, len - 0.3f));
            }
        }

        [BurstCompile]
        [WithNone(typeof(BotDead))]
        partial struct ApplyVisibilityJob : IJobEntity
        {
            [ReadOnly] public NativeArray<ActorSnapshot> actors;
            [ReadOnly] public NativeArray<RaycastHit> hits;
            public float now;

            void Execute(ref BotPerception p, in BotIdentity id, in LocalTransform t, in BotSkill skill)
            {
                // Цель умерла — забываем.
                if (p.targetActorId >= 0 && (p.targetActorId >= actors.Length || !actors[p.targetActorId].alive))
                { p.targetActorId = -1; p.targetVisible = false; }

                if (p.candidateActorId == -2) return;          // не наш кадр
                int slot = id.actorId - 1;
                if (slot < 0 || slot >= hits.Length) return;

                if (p.candidateActorId < 0)
                {
                    p.targetVisible = false;
                    p.awareness = math.max(0f, p.awareness - 0.1f);
                }
                else
                {
                    bool visible = !hits[slot].HasHit();
                    float dtv = math.clamp(now - p.lastVisionCheck, 0f, 0.25f);
                    p.lastVisionCheck = now;
                    bool tracked = p.candidateActorId == p.targetActorId && now - p.lastSeenTime < 3f;
                    if (visible && !tracked)
                    {
                        // Шкала внимания: сначала бот лишь «что-то видит», потом понимает, что это враг
                        if (p.awareOf != p.candidateActorId) { p.awareOf = p.candidateActorId; p.awareness = 0f; }
                        var c = actors[p.candidateActorId];
                        float3 to = c.position - t.Position;
                        float d = math.length(to);
                        float3 fwd = math.mul(t.Rotation, new float3(0, 0, 1));
                        float center = math.saturate((math.dot(fwd, to / math.max(d, 0.01f)) - skill.fovCos) / (1f - skill.fovCos));
                        float spd = math.length(c.velocity.xz);
                        float rate = 1.7f / (1f + d / 11f)
                                   * (c.crouched ? 0.4f : 1f)
                                   * (spd < 0.3f ? 0.55f : spd > 4.2f ? 1.6f : 1f)
                                   * math.lerp(0.55f, 1.45f, center)
                                   / math.max(0.3f, skill.reactionTime * 1.6f);
                        if (p.candidateActorId != 0) rate *= 3f;                 // боты друг друга замечают быстрее — бой не затягивается
                        if (d < 2.5f) rate += 3f;                                // вплотную — заметит почти сразу
                        p.awareness = math.saturate(p.awareness + rate * dtv);
                        if (p.awareness < 1f)
                        {
                            // Ещё не понял: поворачивается на подозрительное движение (как на звук)
                            if (p.awareness > 0.45f) { p.heardPos = c.position; p.heardTime = now; }
                            visible = false;
                        }
                    }
                    else if (!visible && p.awareOf == p.candidateActorId) p.awareness = math.max(0f, p.awareness - 0.35f * dtv);
                    if (visible)
                    {
                        if (p.targetActorId != p.candidateActorId || !p.targetVisible) p.firstSeenTime = now;
                        p.targetActorId = p.candidateActorId;
                        var a = actors[p.candidateActorId];
                        p.targetPos = a.position; p.targetVel = a.velocity;
                        p.lastSeenTime = now; p.targetVisible = true;
                    }
                    else if (p.candidateActorId == p.targetActorId) p.targetVisible = false;
                }

                if (p.targetActorId >= 0 && !p.targetVisible && now - p.lastSeenTime > 8f) p.targetActorId = -1;
                p.suppression = math.max(0, p.suppression - 0.02f);
            }
        }
    }
}
