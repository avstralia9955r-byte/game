using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Frontline.Bots
{
    /// <summary>
    /// Utility-AI: каждый бот «думает» раз в skill.thinkInterval (0.2–0.6 с, разнесено по кадрам), выбирая состояние.
    /// Easy/Medium — прямолинейны (идут к точке, стреляют). Hard/Realistic — укрытия при низком HP/перезарядке/подавлении,
    /// фланг при затяжной перестрелке, проверка услышанных звуков, инженеры строят стены на удержанных точках.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BotVisionSystem))]
    public partial struct BotDecisionSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            if (!WorldStores.Ready || !MatchBridge.MatchRunning) return;
            WorldStores.EnsureOptional();
            state.Dependency = new ThinkJob
            {
                now = (float)SystemAPI.Time.ElapsedTime,
                points = WorldStores.CapturePoints,
                covers = WorldStores.Covers,
                hasCovers = WorldStores.Covers.Length > 0,
                tdm = MatchBridge.Mode == GameModeType.TeamDeathmatch,
                builds = BotRegistry.BuildRequests.AsParallelWriter(),
            }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
        }

        [BurstCompile]
        [WithNone(typeof(BotDead), typeof(BotInVehicle))]   // в машине бот управляется ею
        partial struct ThinkJob : IJobEntity
        {
            public float now;
            [ReadOnly] public NativeArray<CapturePointData> points;
            [ReadOnly] public NativeArray<CoverPoint> covers;
            public bool hasCovers, tdm;
            public NativeQueue<BotBuildRequest>.ParallelWriter builds;

            void Execute(ref BotBrain b, ref BotPathRequest req, ref BotMotion m, in BotPerception p, in BotHealth hp,
                         in BotWeapon w, in BotSkill s, in BotIdentity id, in LocalTransform t)
            {
                if (now < b.nextThink) return;
                b.nextThink = now + s.thinkInterval;
                var rng = Random.CreateFromIndex(id.seed ^ (uint)(now * 1000f));

                float3 pos = t.Position;
                bool hasTarget = p.targetActorId >= 0;
                bool visible = hasTarget && p.targetVisible;
                float distToTarget = hasTarget ? math.distance(pos, p.targetPos) : 0;
                bool reloading = w.reloadEnd > now;
                BotState next = b.state;

                if (visible)
                {
                    // Захват точек: главная цель — точка, в укрытие только раненым (огонь ведут на ходу)
                    bool wantsCover = s.usesCover && hasCovers &&
                                      (tdm ? (hp.value < hp.max * 0.55f || reloading || p.suppression > 0.6f) : hp.value < hp.max * 0.3f) &&
                                      b.state != BotState.InCover && b.state != BotState.SeekCover;
                    if (wantsCover && FindCover(pos, p.targetPos, out var cover))
                    {
                        next = BotState.SeekCover;
                        RequestPath(ref b, ref req, cover.position);
                        b.coverNormal = cover.normal;
                        m.crouched = false;
                    }
                    else if (b.state == BotState.SeekCover && b.hasPath)
                    {
                        // продолжаем бежать в укрытие
                    }
                    else if (tdm && s.flanks && b.state == BotState.Engage && now - b.stateTime > 5f && distToTarget > 20f && rng.NextFloat() < 0.3f)
                    {
                        // Обход: точка сбоку от цели на 35–60% дистанции.
                        float3 to = math.normalizesafe(p.targetPos - pos);
                        float3 side = new float3(-to.z, 0, to.x) * (rng.NextBool() ? 1 : -1);
                        float3 flank = math.lerp(pos, p.targetPos, 0.5f) + side * math.min(distToTarget * 0.5f, 22f);
                        next = BotState.Flank;
                        RequestPath(ref b, ref req, flank);
                    }
                    else if (b.state != BotState.InCover && b.state != BotState.Flank)
                    {
                        next = BotState.Engage;
                        b.hasPath = false;
                        // В открытом бою опытные боты приседают на дальней дистанции.
                        m.crouched = s.usesCover && distToTarget > 35f;
                    }

                    if (b.state == BotState.InCover)
                    {
                        // Из укрытия: низкое — сидим/встаём для выстрела; выходим, когда восстановились.
                        if (!reloading && hp.value > hp.max * 0.6f && p.suppression < 0.3f && now - b.stateTime > 4f) next = BotState.Engage;
                    }
                }
                else if (tdm && hasTarget && s.flanks && now - p.lastSeenTime < 6f)
                {
                    if (b.state != BotState.Investigate) { next = BotState.Investigate; RequestPath(ref b, ref req, p.targetPos); }
                }
                else if (tdm && s.reactsToSound && now - p.heardTime < 5f && math.distancesq(pos, p.heardPos) > 9f)
                {
                    if (b.state != BotState.Investigate || math.distancesq(b.moveTarget, p.heardPos) > 100f)
                    { next = BotState.Investigate; RequestPath(ref b, ref req, p.heardPos); }
                }
                else
                {
                    // Цель режима.
                    if (b.state == BotState.Investigate && b.hasPath) { /* дойти до места звука */ }
                    else if (b.state == BotState.SeekCover && b.hasPath) { }
                    else
                    {
                        next = ChooseObjective(ref b, ref rng, pos, id.team, id.actorId);
                        m.crouched = false;
                    }
                }

                // Дошёл до укрытия.
                if (b.state == BotState.SeekCover && !b.hasPath) { next = BotState.InCover; m.crouched = true; }
                if ((b.state == BotState.Flank || b.state == BotState.Investigate) && !b.hasPath && !visible) next = BotState.Advance;

                // Инженер на своей точке строит стенку в сторону ближайшей вражеской активности.
                if (next == BotState.Defend && s.builds && id.soldierClass == SoldierClass.Engineer && now >= b.nextBuild)
                {
                    b.nextBuild = now + 2f; // 1 кирпич / 2 с — как у игрока
                    float yaw = m.yaw;
                    if (now - p.heardTime < 30f) { float3 d = p.heardPos - pos; yaw = math.atan2(d.x, d.z); }
                    float3 fwd = new float3(math.sin(yaw), 0, math.cos(yaw));
                    builds.Enqueue(new BotBuildRequest { position = pos + fwd * 1.4f, yaw = math.degrees(yaw), team = id.team });
                }

                if (next != b.state) { b.state = next; b.stateTime = now; }
            }

            /// <summary>Отделение (6 бойцов подряд по actorId) — общая цель, поэтому идут вместе.</summary>
            static float SquadNoise(int squad, int i, int epoch)
            {
                uint h = math.hash(new int3(squad * 7919 + 13, i * 104729 + 7, epoch * 31 + 3));
                return (h & 0xFFFF) / 65535f;
            }

            BotState ChooseObjective(ref BotBrain b, ref Random rng, float3 pos, byte team, int actorId)
            {
                int squad = (actorId - 1) / 6;
                int epoch = (int)(now / 40f);
                if (tdm || !points.IsCreated || points.Length == 0)
                {
                    // TDM: отделение патрулирует между точками карты (там сходятся линии фронта), цель меняется раз в 40 с.
                    b.objectiveIndex = (int)(SquadNoise(squad, 0, epoch) * 0.999f * math.max(1, points.Length));
                    b.hasPath = false;
                    return BotState.Advance;
                }

                var cur = points[b.objectiveIndex];
                bool onPoint = math.distancesq(pos, cur.position) < cur.radius * cur.radius;
                if (onPoint && cur.owner == team && cur.contested == 0)
                {
                    // Точка наша: часть ботов остаётся оборонять, остальные идут дальше.
                    if (rng.NextFloat() < 0.65f) return BotState.Defend;
                }
                if (cur.owner != team || cur.contested != 0) { b.hasPath = false; return BotState.Advance; }

                // Выбор новой точки: ближайшая не наша, со случайным разбросом, чтобы не шли толпой.
                int best = b.objectiveIndex; float bestScore = float.MaxValue;
                for (int i = 0; i < points.Length; i++)
                {
                    var cp = points[i];
                    float score = math.distance(pos, cp.position) * (cp.owner == team ? 3f : 1f) * math.lerp(0.75f, 1.25f, SquadNoise(squad, i, epoch));
                    if (score < bestScore) { bestScore = score; best = i; }
                }
                b.objectiveIndex = best;
                b.hasPath = false;
                return BotState.Advance;
            }

            bool FindCover(float3 pos, float3 threat, out CoverPoint result)
            {
                result = default;
                float best = float.MaxValue;
                for (int i = 0; i < covers.Length; i++)
                {
                    var c = covers[i];
                    float d2 = math.distancesq(pos, c.position);
                    if (d2 > 18f * 18f || d2 < 1f) continue;
                    // Укрытие должно быть между нами и угрозой: нормаль стены смотрит от угрозы.
                    float3 toThreat = math.normalizesafe(threat - c.position);
                    if (math.dot(c.normal, toThreat) > -0.5f) continue;
                    float score = d2 - (c.crouchOnly ? 0 : 20f);
                    if (score < best) { best = score; result = c; }
                }
                return best < float.MaxValue;
            }

            void RequestPath(ref BotBrain b, ref BotPathRequest req, float3 dest)
            {
                b.moveTarget = dest;
                b.hasPath = true;              // движение ждёт путь; пока его нет — идём напрямую
                req.destination = dest;
                req.pending = true;
                req.requestTime = now;
            }
        }
    }
}
