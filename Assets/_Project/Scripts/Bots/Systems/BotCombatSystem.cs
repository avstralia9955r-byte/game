using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Frontline.Bots
{
    public struct BotShot
    {
        public float3 origin, direction;
        public float damage, headMul, range, muzzleVelocity, loudness;
        public int shooterActorId, intendedTargetId;
        public byte team;
        public bool suppressed;
    }

    /// <summary>
    /// Стрельба ботов: время реакции, ошибка прицела (сходится со временем, растёт от подавления и скорости цели),
    /// упреждение по скорости пули, очереди с подбросом ствола, перезарядка. Выстрелы складываются в очередь,
    /// которую разрешает BotShotResolveSystem пакетными лучами.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BotMovementSystem))]
    public partial struct BotCombatSystem : ISystem
    {
        public static NativeQueue<BotShot> Shots;

        public void OnCreate(ref SystemState state)
        {
            Shots = new NativeQueue<BotShot>(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state) { if (Shots.IsCreated) Shots.Dispose(); }

        public void OnUpdate(ref SystemState state)
        {
            if (!MatchBridge.MatchRunning) return;
            state.Dependency = new FireJob
            {
                now = (float)SystemAPI.Time.ElapsedTime,
                shots = Shots.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
        }

        [BurstCompile]
        [WithNone(typeof(BotDead), typeof(BotInVehicle))]   // в машине бот управляется ею
        partial struct FireJob : IJobEntity
        {
            public float now;
            public NativeQueue<BotShot>.ParallelWriter shots;

            void Execute(ref BotWeapon w, ref BotMotion m, in BotPerception p, in BotSkill s, in BotIdentity id,
                         in BotBrain b, in LocalTransform t, in BotHealth hp)
            {
                // Перезарядка
                if (w.ammo <= 0)
                {
                    if (w.reloadEnd <= 0) { w.reloadEnd = now + w.reloadTime; return; }
                    if (now < w.reloadEnd) return;
                    w.ammo = w.magazine; w.reloadEnd = 0;
                }

                bool canShoot = p.targetVisible && b.state != BotState.SeekCover && now - p.firstSeenTime >= s.reactionTime;
                if (!canShoot)
                {
                    // Тактическая перезарядка в укрытии/без цели.
                    if (!p.targetVisible && w.ammo < w.magazine / 3 && w.reloadEnd <= 0) { w.ammo = 0; w.reloadEnd = now + w.reloadTime; }
                    w.burstLeft = 0;
                    return;
                }
                if (now < w.nextFire) return;

                float3 eye = t.Position + new float3(0, m.crouched ? 1.1f : 1.6f, 0);
                float dist = math.distance(eye, p.targetPos);
                if (dist > w.range * 1.2f) return;

                var rng = Random.CreateFromIndex(id.seed ^ (uint)(now * 977f));

                if (w.burstLeft <= 0)
                {
                    w.burstLeft = w.isAuto ? rng.NextInt(s.burstMin, s.burstMax + 1) : 1;
                    // Пауза между очередями (для самозарядных — «прицеливание» между выстрелами).
                    w.nextFire = now + (w.isAuto ? rng.NextFloat(0.15f, 0.45f) : rng.NextFloat(0.05f, 0.3f));
                    return;
                }

                // Упреждение + прицельная точка (грудь, иногда голова на Realistic).
                float tFlight = dist / math.max(50f, w.muzzleVelocity);
                float aimHeight = (s.aimErrorDeg < 1.5f && rng.NextFloat() < 0.25f) ? 1.62f : 1.25f;
                float3 aimPoint = p.targetPos + p.targetVel * tFlight + new float3(0, aimHeight, 0);

                float settle = math.saturate((now - p.firstSeenTime) / math.max(0.1f, s.aimSettleTime));
                float targetSpeed = math.length(p.targetVel.xz);
                float err = s.aimErrorDeg * math.lerp(1.6f, 0.35f, settle) * (1f + p.suppression * 1.5f + targetSpeed * 0.08f)
                            + w.spreadDeg * (m.crouched ? 0.6f : 1f);
                // Срыв очереди: ~5% времени (окнами по 2 с) пули бота уходят мимо — «десяток пуль в молоко»
                if ((math.hash(new uint2((uint)id.actorId, (uint)(now * 0.5f))) % 20u) == 0u) err += 5f;
                if (hp.value > 0 && hp.value < 10f) err = err * 2.5f + 3f;      // тяжело ранен — стреляет почти наугад
                int shotInBurst = math.max(0, s.burstMax - w.burstLeft);
                float climb = w.recoilClimbDeg * shotInBurst;

                float3 dir = math.normalize(aimPoint - eye);
                dir = Perturb(dir, err, climb, ref rng);

                shots.Enqueue(new BotShot
                {
                    origin = eye, direction = dir, damage = w.damage, headMul = w.headMul, range = w.range,
                    muzzleVelocity = w.muzzleVelocity, loudness = w.suppressed ? 0 : w.loudness,
                    shooterActorId = id.actorId, intendedTargetId = p.targetActorId, team = id.team, suppressed = w.suppressed
                });

                w.ammo--; w.burstLeft--;
                w.nextFire = now + w.fireInterval;
            }

            static float3 Perturb(float3 dir, float errDeg, float climbDeg, ref Random rng)
            {
                float3 up = new float3(0, 1, 0);
                float3 right = math.normalizesafe(math.cross(up, dir));
                float3 realUp = math.cross(dir, right);
                float r = math.tan(math.radians(errDeg) * 0.5f) * math.sqrt(rng.NextFloat());
                float a = rng.NextFloat(math.PI * 2);
                float c = math.tan(math.radians(climbDeg));
                return math.normalize(dir + right * (math.cos(a) * r) + realUp * (math.sin(a) * r + c));
            }
        }
    }

    /// <summary>
    /// Разрешение выстрелов ботов: пакет RaycastCommand по миру → аналитический тест капсул акторов до точки попадания.
    /// Урон в CombatWorld.Damage, подавление промахами, шум для слуха, трассеры только рядом с камерой.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BotCombatSystem))]
    public partial class BotShotResolveSystem : SystemBase
    {
        public float tracerMaxDistance = 120f;
        NativeList<BotShot> _batch;

        protected override void OnCreate() => _batch = new NativeList<BotShot>(1024, Allocator.Persistent);
        protected override void OnDestroy() { if (_batch.IsCreated) _batch.Dispose(); }

        protected override void OnUpdate()
        {
            var queue = BotCombatSystem.Shots;
            if (!queue.IsCreated || queue.Count == 0 || !CombatWorld.Initialized) return;
            _batch.Clear();
            while (queue.TryDequeue(out var s)) _batch.Add(s);
            int n = _batch.Length;

            var cmds = new NativeArray<RaycastCommand>(n, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(n, Allocator.TempJob);
            var qp = new QueryParameters(CombatWorld.WorldMask, false, QueryTriggerInteraction.Ignore, false);
            for (int i = 0; i < n; i++)
                cmds[i] = new RaycastCommand(_batch[i].origin, _batch[i].direction, qp, _batch[i].range);
            RaycastCommand.ScheduleBatch(cmds, hits, 32, 1).Complete();

            var cam = Camera.main ? Camera.main.transform.position : Vector3.zero;
            var theme = MatchSession.Current.map ? MatchSession.Current.map.theme : MapTheme.Summer;
            var perceptionLookup = GetComponentLookup<BotPerception>(false);

            for (int i = 0; i < n; i++)
            {
                var s = _batch[i];
                var h = hits[i];
                bool world = h.HasHit();
                float worldDist = world ? h.distance : s.range;
                float3 end = s.origin + s.direction * worldDist;

                if (CombatWorld.RaycastActors(s.origin, s.direction, worldDist, s.team, s.shooterActorId, out var ah))
                {
                    float falloff = math.lerp(1f, 0.6f, math.saturate(ah.distance / s.range));
                    CombatWorld.Damage.Enqueue(new DamageEvent
                    {
                        targetActorId = ah.actorId, sourceActorId = s.shooterActorId,
                        amount = s.damage * falloff * (ah.headshot ? s.headMul : 1f) * Sandbag.Factor(s.origin, ah.point),
                        hitPoint = ah.point, direction = s.direction, headshot = ah.headshot
                    });
                    end = ah.point;
                    if (FxPool.I && math.distancesq(end, (float3)cam) < tracerMaxDistance * tracerMaxDistance) FxPool.I.Blood(end, s.direction);
                }
                else
                {
                    if (world)
                    {
                        var brick = h.collider ? h.collider.GetComponent<Brick>() : null;
                        if (brick) brick.Damage(s.damage * 0.5f);
                        // Лёгкую машину пули пробивают
                        var veh = h.collider ? h.collider.GetComponentInParent<Tank>() : null;
                        if (veh && veh.IsLight && veh.Alive && veh.Team != s.team) veh.TakeDamage(s.damage * 0.5f, s.shooterActorId, h.point);
                        var plane = !veh && h.collider ? h.collider.GetComponentInParent<Aircraft>() : null;
                        if (plane && plane.Alive && plane.Team != s.team) plane.TakeDamage(s.damage * 0.35f, s.shooterActorId, h.point);
                        if (FxPool.I && math.distancesq(h.point, cam) < tracerMaxDistance * tracerMaxDistance)
                        {
                            if (brick) FxPool.I.BrickChips(h.point, h.normal); else FxPool.I.Impact(h.point, h.normal, theme);
                        }
                    }
                    // Промах по боту → подавление (бот на Hard+ уйдёт в укрытие).
                    if (s.intendedTargetId > 0 && s.intendedTargetId < BotRegistry.ActorToEntity.Length)
                    {
                        var e = BotRegistry.ActorToEntity[s.intendedTargetId];
                        if (e != Entity.Null && perceptionLookup.HasComponent(e))
                        {
                            var p = perceptionLookup[e];
                            p.suppression = math.min(1f, p.suppression + 0.12f);
                            // Обстреливаемый бот узнаёт направление на стрелка.
                            p.heardPos = s.origin; p.heardTime = (float)SystemAPI.Time.ElapsedTime;
                            perceptionLookup[e] = p;
                        }
                    }
                }

                if (FxPool.I && (i & 1) == 0 && math.distancesq(s.origin, (float3)cam) < tracerMaxDistance * tracerMaxDistance * 4f)
                    FxPool.I.Tracer(s.origin + s.direction * 0.8f, end, s.muzzleVelocity);

                // Пролёт пули рядом с игроком — свист/щелчок.
                if (CombatWorld.Actors.Length > 0 && CombatWorld.Actors[0].alive && CombatWorld.Actors[0].team != s.team)
                {
                    float3 head = CombatWorld.Actors[0].position + new float3(0, 1.6f, 0);
                    float3 seg = end - s.origin; float len2 = math.lengthsq(seg);
                    float k = len2 > 0 ? math.saturate(math.dot(head - s.origin, seg) / len2) : 0;
                    float3 closest = s.origin + seg * k;
                    float dd = math.distance(closest, head);
                    if (dd < 3f && dd > 0.4f && k > 0.05f && k < 0.98f) BotAudio.Whiz(closest);
                }
                if (FxPool.I && math.distancesq(s.origin, (float3)cam) < tracerMaxDistance * tracerMaxDistance * 4f)
                    FxPool.I.MuzzleFlashAt(s.origin + s.direction * 0.9f, s.direction);
                CombatWorld.EmitNoise(s.origin, s.loudness, s.shooterActorId, s.team, 0);
                BotAudio.Shot(s.origin, s.team, s.suppressed);
                if (Net.NetSession.IsHost) Net.NetDriver.RecordShot(s.shooterActorId, s.origin, end, s.team, s.suppressed);
            }
            cmds.Dispose(); hits.Dispose();
        }
    }
}
