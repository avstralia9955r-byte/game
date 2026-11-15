using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.AI;

namespace Frontline.Bots
{
    /// <summary>
    /// Точечные пути по NavMesh (укрытие, фланг, звук, «застрял»). Бюджет — несколько путей за кадр,
    /// остальные ждут: так 128 ботов никогда не устраивают пик на главном потоке.
    /// Основная навигация к целям режима — flow field (BotMovementSystem), без NavMesh вообще.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BotDecisionSystem))]
    public partial class BotPathRequestSystem : SystemBase
    {
        public int pathsPerFrame = 6;
        NavMeshPath _path;
        readonly UnityEngine.Vector3[] _corners = new UnityEngine.Vector3[64];

        protected override void OnCreate() => _path = new NavMeshPath();

        protected override void OnUpdate()
        {
            int budget = pathsPerFrame;
            float now = (float)SystemAPI.Time.ElapsedTime;
            foreach (var (req, buf, cursor, brain, t) in SystemAPI
                         .Query<RefRW<BotPathRequest>, DynamicBuffer<BotPathPoint>, RefRW<BotPathCursor>, RefRW<BotBrain>, RefRO<LocalTransform>>()
                         .WithNone<BotDead>())
            {
                if (!req.ValueRO.pending) continue;
                if (budget-- <= 0) break;
                req.ValueRW.pending = false;
                buf.Clear();
                cursor.ValueRW.index = 0;

                UnityEngine.Vector3 from = t.ValueRO.Position, to = req.ValueRO.destination;
                if (NavMesh.SamplePosition(to, out var hit, 4f, NavMesh.AllAreas)) to = hit.position;
                if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _path) && _path.status != NavMeshPathStatus.PathInvalid)
                {
                    int n = _path.GetCornersNonAlloc(_corners);
                    for (int i = 1; i < n; i++) buf.Add(new BotPathPoint { position = _corners[i] });
                    brain.ValueRW.hasPath = buf.Length > 0;
                }
                else brain.ValueRW.hasPath = false;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BotPathRequestSystem))]
    public partial struct BotMovementSystem : ISystem
    {
        int _frame;
        public void OnUpdate(ref SystemState state)
        {
            if (!WorldStores.Ready || !CombatWorld.Initialized) return;
            float tnow = (float)SystemAPI.Time.ElapsedTime;
            FrontPlanner.Update(tnow);
            _frame++;
            var cam = UnityEngine.Camera.main;
            float3 viewer = cam ? (float3)cam.transform.position : (CombatWorld.Actors.Length > 0 ? CombatWorld.Actors[0].position : float3.zero);
            state.Dependency = new MoveJob
            {
                goals = FrontPlanner.Goals, frame = _frame, viewer = viewer, farDist = 220f * BotLod.Scale,
                capture = MatchBridge.Mode == GameModeType.TerritoryControl,
                dt = SystemAPI.Time.DeltaTime,
                now = (float)SystemAPI.Time.ElapsedTime,
                integration = WorldStores.Integration, walkable = WorldStores.Walkable, height = WorldStores.Height,
                gridSize = WorldStores.GridSize, origin = WorldStores.GridOrigin, cell = WorldStores.CellSize,
                points = WorldStores.CapturePoints, actors = CombatWorld.Actors.AsArray(),
                cells = WorldStores.GridSize.x * WorldStores.GridSize.y
            }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
        }

        [BurstCompile]
        [WithNone(typeof(BotDead), typeof(BotInVehicle))]   // в машине бот управляется ею
        partial struct MoveJob : IJobEntity
        {
            public float dt, now, cell;
            public int cells;
            public int2 gridSize;
            public float2 origin;
            [ReadOnly] public NativeArray<ushort> integration;
            [ReadOnly] public NativeArray<byte> walkable;
            [ReadOnly] public NativeArray<float> height;
            [ReadOnly] public NativeArray<CapturePointData> points;
            [ReadOnly] public NativeArray<ActorSnapshot> actors;
            [ReadOnly] public NativeArray<float4> goals;      // точки строя от FrontPlanner (индекс — actorId)
            public int frame;
            public float3 viewer;                             // камера игрока: дальние боты считаются через кадр
            public float farDist;
            public bool capture;                              // захват точек: точка важнее строя, огонь на ходу

            const float RunSpeed = 5.4f, WalkSpeed = 3.4f, CrouchSpeed = 1.9f, TurnRate = 6f;

            void Execute(ref LocalTransform t, ref BotMotion m, ref BotBrain b, ref BotPathCursor cursor, ref BotPathRequest req,
                         DynamicBuffer<BotPathPoint> path, in BotPerception p, in BotIdentity id, in BotWeapon w, in BotHealth hp)
            {
                // Оптимизация дальних (> 220 м от камеры): движение считается через кадр с двойным шагом,
                // без расталкивания соседей — на глаз не видно, а на 512 ботах экономит половину работы
                bool far = math.distancesq(t.Position, viewer) > farDist * farDist;
                if (far && ((frame + id.actorId) & 1) != 0) return;
                float dt = far ? this.dt * 2f : this.dt;
                float3 pos = t.Position;
                float2 desired = float2.zero;
                float speed = RunSpeed;

                // ---- 1. Куда идти ----
                if (b.hasPath && path.Length > 0)
                {
                    float3 wp = path[math.min(cursor.index, path.Length - 1)].position;
                    float2 to = (wp - pos).xz;
                    if (math.lengthsq(to) < 0.8f * 0.8f)
                    {
                        cursor.index++;
                        if (cursor.index >= path.Length) { b.hasPath = false; path.Clear(); cursor.index = 0; }
                    }
                    desired = math.normalizesafe(to);
                    if (b.state == BotState.Investigate) speed = WalkSpeed;
                }
                else if (b.hasPath) // путь ещё считается — идём напрямую
                {
                    desired = math.normalizesafe((b.moveTarget - pos).xz);
                }
                else
                {
                    switch (b.state)
                    {
                        case BotState.Engage:
                        {
                            // Покачивание влево-вправо + подход/отход к эффективной дистанции оружия.
                            float2 toT = math.normalizesafe((p.targetPos - pos).xz);
                            float2 side = new float2(-toT.y, toT.x);
                            float strafe = math.sin(now * 1.3f + id.seed % 100);
                            float dist = math.distance(pos, p.targetPos);
                            float approach = dist > w.range * 0.7f ? 1f : dist < w.range * 0.25f ? -0.5f : 0f;
                            desired = side * strafe * 0.7f + toT * approach;
                            speed = WalkSpeed;
                            if (capture && points.Length > 0)
                            {
                                // Захват: идём к своей точке и стреляем на ходу; на самой точке — держим её
                                int fi = math.clamp(b.objectiveIndex, 0, math.min(4, points.Length - 1));
                                var cp = points[fi];
                                bool onPoint = math.distancesq(pos.xz, cp.position.xz) < cp.radius * cp.radius * 0.6f;
                                if (!onPoint)
                                {
                                    float2 fd = FlowField.Sample(integration, walkable, fi, gridSize, origin, cell, pos);
                                    if (math.all(fd == float2.zero)) fd = math.normalizesafe((cp.position - pos).xz);
                                    desired = fd * 0.9f + side * strafe * 0.3f;
                                    speed = WalkSpeed * 1.15f;
                                }
                            }
                            break;
                        }
                        case BotState.InCover:
                            desired = float2.zero;
                            break;
                        case BotState.Defend:
                        {
                            // Блуждание внутри радиуса точки.
                            var cp = points.Length > 0 ? points[math.clamp(b.objectiveIndex, 0, points.Length - 1)] : default;
                            if (math.distancesq(b.moveTarget.xz, pos.xz) < 1f || math.distancesq(b.moveTarget.xz, cp.position.xz) > cp.radius * cp.radius)
                            {
                                var rng = Random.CreateFromIndex(id.seed ^ (uint)(now * 10));
                                b.moveTarget = cp.position + new float3(rng.NextFloat(-1, 1), 0, rng.NextFloat(-1, 1)) * cp.radius * 0.7f;
                            }
                            desired = math.normalizesafe((b.moveTarget - pos).xz) * 0.6f;
                            speed = WalkSpeed;
                            break;
                        }
                        default:
                        {
                            var forced = id.actorId < goals.Length ? goals[id.actorId] : float4.zero;
                            if (forced.w > 1.5f)
                            {
                                // Приказ «бегом сюда» (например, к машине игрока) — важнее строя и точек
                                float2 to = forced.xy - pos.xz;
                                float gd = math.length(to);
                                desired = gd > 0.3f ? to / gd : float2.zero;
                                speed = RunSpeed * 1.1f;
                                break;
                            }
                            int field = math.clamp(b.objectiveIndex, 0, 4);
                            // Чужая точка рядом (60 м) — строй ломается, бойцы идут её брать
                            int near = -1; float nd = 60f * 60f;
                            for (int k = 0; k < points.Length && k < 5; k++)
                            {
                                if (points[k].owner == id.team && points[k].contested == 0) continue;
                                float d2 = math.distancesq(points[k].position.xz, pos.xz);
                                if (d2 < nd) { nd = d2; near = k; }
                            }
                            var goal = id.actorId < goals.Length ? goals[id.actorId] : float4.zero;
                            if (near < 0 && goal.w > 0f && !capture)
                            {
                                // Своё место в строю (широкая полоса или колонна полка)
                                float2 to = goal.xy - pos.xz;
                                float gd = math.length(to);
                                desired = gd > 0.5f ? to / gd : float2.zero;
                                speed = goal.z > 40f ? RunSpeed * 1.08f : gd < 12f ? WalkSpeed : RunSpeed;   // отстал — бегом, у места — шагом
                                break;
                            }
                            if (near >= 0) field = near;
                            desired = FlowField.Sample(integration, walkable, field, gridSize, origin, cell, pos);
                            if (math.all(desired == float2.zero) && points.Length > field)
                                desired = math.normalizesafe((points[field].position - pos).xz) * 0.4f; // на самой точке
                            break;
                        }
                    }
                }

                // ---- 2. Разведение с соседями (локальное избегание толпы) ----
                float2 sep = float2.zero;
                for (int i = 0; i < actors.Length && !far; i++)
                {
                    var a = actors[i];
                    if (!a.alive || a.actorId == id.actorId) continue;
                    float2 d = pos.xz - a.position.xz;
                    float d2 = math.lengthsq(d);
                    if (d2 < 1.44f && d2 > 1e-4f) sep += d / d2 * 0.35f;
                }
                desired += sep;
                if (math.lengthsq(desired) > 1) desired = math.normalize(desired);

                if (m.crouched) speed = CrouchSpeed;
                // Тяжело ранен (меньше 10 HP): лежит и медленно ползёт
                if (hp.value > 0 && hp.value < 10f) { m.crouched = true; speed = CrouchSpeed * 0.35f; }
                m.speed = speed;

                // ---- 3. Интеграция с учётом проходимости (скольжение вдоль стен) ----
                float2 targetVel = desired * speed;
                float2 vel = math.lerp(m.velocity.xz, targetVel, math.saturate(dt * 8f));
                float2 next = pos.xz + vel * dt;
                if (!Walkable(new float3(next.x, 0, next.y)))
                {
                    if (Walkable(new float3(next.x, 0, pos.z))) { next.y = pos.z; vel.y = 0; }
                    else if (Walkable(new float3(pos.x, 0, next.y))) { next.x = pos.x; vel.x = 0; }
                    else
                    {
                        // Угол между стенами / край окопа: пробуем обойти под углом ±50° и ±100° к желаемому направлению
                        float sp = math.length(vel); bool found = false;
                        if (sp > 0.05f)
                        {
                            float2 v0 = vel / sp;
                            for (int k = 0; k < 4 && !found; k++)
                            {
                                float a = (k < 2 ? 0.87f : 1.75f) * ((k & 1) == 0 ? 1f : -1f) * ((id.seed & 1) == 0 ? 1f : -1f);
                                float2 rv = new float2(v0.x * math.cos(a) - v0.y * math.sin(a), v0.x * math.sin(a) + v0.y * math.cos(a));
                                float2 cand = pos.xz + rv * sp * 0.8f * dt;
                                if (Walkable(new float3(cand.x, 0, cand.y))) { next = cand; vel = rv * sp * 0.8f; found = true; }
                            }
                        }
                        if (!found) { next = pos.xz; vel = float2.zero; }
                    }
                }
                float y = FlowField.GroundHeight(height, gridSize, origin, cell, new float3(next.x, 0, next.y));
                float3 newPos = new float3(next.x, y, next.y);
                m.velocity = new float3(vel.x, (newPos.y - pos.y) / math.max(dt, 1e-4f), vel.y);
                t.Position = newPos;

                // ---- 4. Поворот: на цель в бою, иначе по ходу ----
                float targetYaw = m.yaw;
                bool glancing = false;
                if (p.targetVisible) { float3 d = p.targetPos - newPos; targetYaw = math.atan2(d.x, d.z); m.glanceUntil = 0; }
                else
                {
                    if (math.lengthsq(vel) > 0.2f) targetYaw = math.atan2(vel.x, vel.y);
                    // Осмотр: время от времени бот оглядывается влево, вправо или назад (на ходу — только по сторонам)
                    uint h = math.hash(new uint2(id.seed, (uint)(m.nextGlance * 10f)));
                    if (now >= m.nextGlance)
                    {
                        float r = (h & 1023) / 1023f;
                        bool moving = math.lengthsq(vel) > 1f;
                        m.glanceYaw = !moving && r < 0.3f ? math.PI * 0.9f * (((h >> 10) & 1) == 0 ? 1f : -1f)
                                    : (r < 0.65f ? 1f : -1f) * math.radians(60f + ((h >> 12) & 31));
                        m.glanceUntil = now + 0.9f + ((h >> 17) & 15) * 0.05f;
                        m.nextGlance = now + 4f + ((h >> 21) & 63) * 0.1f;
                    }
                    if (now < m.glanceUntil) { targetYaw += m.glanceYaw; glancing = true; }
                    // Подозрительный звук/движение — поворот туда
                    if (now - p.heardTime < 2.5f && now - p.heardTime >= 0f)
                    { float3 d = p.heardPos - newPos; if (math.lengthsq(d.xz) > 1f) { targetYaw = math.atan2(d.x, d.z); glancing = true; } }
                }
                float dy = math.atan2(math.sin(targetYaw - m.yaw), math.cos(targetYaw - m.yaw));
                float turn = glancing ? TurnRate * 0.6f : TurnRate;
                m.yaw += math.clamp(dy, -turn * dt, turn * dt);
                t.Rotation = quaternion.RotateY(m.yaw);

                // ---- 5. Застревание → NavMesh-путь к цели ----
                bool wantsMove = math.lengthsq(desired) > 0.25f;
                if (wantsMove && math.distancesq(newPos, m.lastPos) < 0.04f * dt * 60f) m.stuckTimer += dt;
                else { m.stuckTimer = math.max(0, m.stuckTimer - dt); m.stuckTotal = math.max(0, m.stuckTotal - dt * 0.25f); }
                if (m.stuckTotal >= 3f)
                {
                    // Застрял надолго (яма, угол между стенами, окоп): ближайшая проходимая клетка по кругу
                    m.stuckTotal = 0;
                    for (int k = 0; k < 24; k++)
                    {
                        float ang = k * 0.785f + (id.seed & 7) * 0.3f, rad = 2f + (k / 8) * 3.5f;
                        float2 c = newPos.xz + new float2(math.cos(ang), math.sin(ang)) * rad;
                        if (!Walkable(new float3(c.x, 0, c.y))) continue;
                        float cy = FlowField.GroundHeight(height, gridSize, origin, cell, new float3(c.x, 0, c.y));
                        newPos = new float3(c.x, cy, c.y); t.Position = newPos;
                        b.hasPath = false;
                        break;
                    }
                }
                m.lastPos = newPos;
                if (m.stuckTimer > 1.0f && !req.pending)
                {
                    m.stuckTimer = 0; m.stuckTotal += 1f;
                    float3 dest = b.hasPath ? b.moveTarget : (points.Length > 0 ? points[math.clamp(b.objectiveIndex, 0, points.Length - 1)].position : newPos);
                    b.moveTarget = dest; b.hasPath = true;
                    req.destination = dest; req.pending = true; req.requestTime = now;
                }
            }

            /// <summary>
            /// Отделение наступает развёрнутым фронтом, а не цепочкой: у каждого бойца своё место в линии (интервал ~7 м
            /// со случайным сдвигом и «рваной» глубиной). Боец держит боковое смещение относительно центра отделения
            /// поперёк направления движения и подстраивает темп: убежал вперёд — шагом, отстал — бегом.
            /// </summary>
            float2 Formation(float2 dir, float3 pos, in BotIdentity id, ref float speed)
            {
                if (math.lengthsq(dir) < 1e-4f) return dir;
                int squad = (id.actorId - 1) / 6, slot = (id.actorId - 1) % 6;
                float3 c = float3.zero; int n = 0;
                for (int i = 0; i < actors.Length; i++)
                {
                    var a = actors[i];
                    if (!a.alive || a.team != id.team || a.actorId <= 0 || (a.actorId - 1) / 6 != squad) continue;
                    c += a.position; n++;
                }
                if (n < 2) return dir;
                c /= n;
                float2 f = math.normalize(dir), side = new float2(-f.y, f.x);
                var rng = Random.CreateFromIndex(id.seed | 1u);
                float lateral = (slot - 2.5f) * 7f + rng.NextFloat(-2f, 2f);
                float depth = rng.NextFloat(-3.5f, 3.5f);
                float2 rel = pos.xz - c.xz;
                float latErr = lateral - math.dot(rel, side);
                float depErr = depth - math.dot(rel, f);
                float2 outDir = f + side * math.clamp(latErr / 6f, -0.9f, 0.9f);
                if (depErr < -4f) speed = WalkSpeed;            // впереди линии — шагом
                else if (depErr > 6f) speed = RunSpeed * 1.08f; // отстал — догоняет
                return math.normalizesafe(outDir);
            }

            bool Walkable(float3 p)
            {
                int2 c = math.clamp((int2)math.floor((p.xz - origin) / cell), 0, gridSize - 1);
                return walkable[c.y * gridSize.x + c.x] != 0;
            }
        }
    }
}
