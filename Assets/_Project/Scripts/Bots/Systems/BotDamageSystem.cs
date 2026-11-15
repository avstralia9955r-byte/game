using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Frontline.Bots
{
    /// <summary>Ссылка ECS-мира на игрока (GameObject).</summary>
    public static class PlayerBridge
    {
        public static PlayerHealth Health;
        public static PlayerController Controller;
    }

    /// <summary>
    /// Применяет урон из CombatWorld.Damage (его пишут оружие игрока, нож, выстрелы ботов, взрывы),
    /// убивает и возрождает ботов, публикует события убийств для режима и HUD.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BotShotResolveSystem))]
    public partial class BotDamageSystem : SystemBase
    {
        Random _rng;
        float _regenAt;

        protected override void OnCreate() => _rng = Random.CreateFromIndex(1234);

        protected override void OnUpdate()
        {
            if (!CombatWorld.Initialized) return;
            // Клиент сетевой игры: урон не применяется локально — то, что нанёс сам игрок, уходит хосту
            if (Net.NetSession.IsClient)
            {
                while (CombatWorld.Damage.TryDequeue(out var ce))
                    if (ce.sourceActorId == CombatWorld.PlayerActorId) Net.NetDriver.QueueDamage(ce);
                BotRegistry.FlushKills();
                return;
            }
            float now = (float)SystemAPI.Time.ElapsedTime;
            var actors = CombatWorld.Actors;

            while (CombatWorld.Damage.TryDequeue(out var e))
            {
                // Лётчик в кабине (игрок или бот): попадания принимает самолёт
                if (e.amount < 9000f)
                {
                    var plane = Aircraft.OfPilot(e.targetActorId);
                    if (plane) { plane.TakeDamage(e.amount * 0.4f, e.sourceActorId, e.hitPoint); continue; }
                }
                if (e.targetActorId == CombatWorld.PlayerActorId)
                {
                    bool wasAlive = PlayerBridge.Health && PlayerBridge.Health.Alive;
                    if (PlayerBridge.Health) PlayerBridge.Health.ApplyDamage(e);
                    if (wasAlive && !PlayerBridge.Health.Alive)
                        BotRegistry.Kills.Enqueue(new KillEvent { killerActorId = e.sourceActorId, victimActorId = 0,
                            victimTeam = PlayerBridge.Controller ? PlayerBridge.Controller.Team : (byte)0, headshot = e.headshot });
                    continue;
                }
                if (Net.NetDriver.ApplyRemoteDamage(e)) continue;          // по подключённому игроку — здоровье считает хост
                if (e.targetActorId <= 0 || e.targetActorId >= BotRegistry.ActorToEntity.Length) continue;
                var ent = BotRegistry.ActorToEntity[e.targetActorId];
                if (ent == Entity.Null || !EntityManager.Exists(ent)) continue;

                var hp = EntityManager.GetComponentData<BotHealth>(ent);
                if (hp.value <= 0) continue;
                if (now < hp.protectUntil && e.amount < 9000f) continue;      // только что появился — неуязвим 10 с
                hp.value -= e.amount;
                hp.lastHitTime = now;
                HitFeedback.Report(e.sourceActorId, hp.value <= 0);      // отметка попадания игрока из чего угодно
                EntityManager.SetComponentData(ent, hp);

                // Раненый бот «чувствует» направление на стрелка, даже если не видел его.
                var p = EntityManager.GetComponentData<BotPerception>(ent);
                if (e.sourceActorId >= 0 && e.sourceActorId < actors.Length)
                {
                    p.heardPos = actors[e.sourceActorId].position; p.heardTime = now;
                    if (p.targetActorId < 0) { p.targetActorId = e.sourceActorId; p.targetPos = p.heardPos; p.lastSeenTime = now - 1f; }
                }
                p.suppression = math.min(1f, p.suppression + 0.3f);
                EntityManager.SetComponentData(ent, p);

                if (hp.value <= 0)
                {
                    EntityManager.SetComponentData(ent, new BotDead { respawnAt = now + MatchBridge.RespawnDelay, killerActorId = e.sourceActorId });
                    EntityManager.SetComponentEnabled<BotDead>(ent, true);
                    if (EntityManager.HasComponent<VatClip>(ent))
                    {
                        EntityManager.SetComponentData(ent, new VatClip { value = 5 });
                        EntityManager.SetComponentData(ent, new VatTime { value = 0 });
                    }
                    else
                    {
                        // Процедурная смерть: BotDeathFallSystem заваливает тело вперёд/назад по направлению попадания.
                        var mo = EntityManager.GetComponentData<BotMotion>(ent);
                        float3 fwd = new float3(math.sin(mo.yaw), 0, math.cos(mo.yaw));
                        mo.pitch = math.dot(fwd, e.direction) > 0 ? 1f : -1f;
                        mo.velocity = float3.zero;
                        EntityManager.SetComponentData(ent, mo);
                        VehicleSeats.Unboard(EntityManager, ent);   // погиб в машине — выпадает из неё
                        // Импульс «тряпичной кукле»: пуля толкает и валит от стрелка, взрыв подбрасывает в воздух
                        if (EntityManager.HasComponent<BotRagdoll>(ent))
                        {
                            var lt = EntityManager.GetComponentData<LocalTransform>(ent);
                            float3 d = e.direction; d.y = 0;
                            d = math.normalizesafe(d, -fwd);
                            float force = e.explosive ? math.clamp(e.amount / 30f, 3f, 6.5f)
                                                      : math.clamp(e.amount / 40f, 0.7f, 2.2f) * (e.headshot ? 1.25f : 1f);
                            float up = e.explosive ? force * 1.1f + _rng.NextFloat(2f) : 0.35f + _rng.NextFloat(0.3f);
                            EntityManager.SetComponentData(ent, new BotRagdoll
                            {
                                active = 1, pos = lt.Position, groundY = lt.Position.y,
                                vel = d * force + new float3(0, up, 0),
                                axis = math.normalizesafe(math.cross(new float3(0, 1, 0), d), new float3(1, 0, 0)),
                                angle = 0.03f,
                                angVel = e.explosive ? 4f + _rng.NextFloat(4f) : 0.8f + force * 0.6f + (e.headshot ? 1.2f : 0f)
                            });
                        }
                    }
                    var id = EntityManager.GetComponentData<BotIdentity>(ent);
                    // Погибший роняет оружие — его можно подобрать
                    if (EntityManager.HasComponent<LocalTransform>(ent))
                        WeaponPickups.BotDrop(EntityManager.GetComponentData<LocalTransform>(ent).Position, id.team, id.soldierClass);
                    BotRegistry.Kills.Enqueue(new KillEvent { killerActorId = e.sourceActorId, victimActorId = e.targetActorId, victimTeam = id.team, headshot = e.headshot });
                }
            }

            // ---- Своё восстановление: 1 HP раз в 10 секунд ----
            if (now >= _regenAt)
            {
                _regenAt = now + 10f;
                foreach (var hpr in SystemAPI.Query<RefRW<BotHealth>>().WithNone<BotDead>())
                    if (hpr.ValueRO.value > 0 && hpr.ValueRO.value < hpr.ValueRO.max) hpr.ValueRW.value = math.min(hpr.ValueRO.max, hpr.ValueRO.value + 1f);
            }

            // ---- Возрождение ----
            if (MatchBridge.MatchRunning)
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                foreach (var (hp, t, brain, perc, w, id, ent) in SystemAPI
                             .Query<RefRW<BotHealth>, RefRW<LocalTransform>, RefRW<BotBrain>, RefRW<BotPerception>,
                                    RefRW<BotWeapon>, RefRO<BotIdentity>>()
                             .WithAll<BotDead>().WithEntityAccess())
                {
                    if (now < SystemAPI.GetComponent<BotDead>(ent).respawnAt) continue;
                    var pos = SpawnPicker.Pick(id.ValueRO.team, ref _rng, MatchBridge.PointSpawns);
                    t.ValueRW.Position = pos;
                    hp.ValueRW.value = hp.ValueRO.max;
                    hp.ValueRW.protectUntil = now + 10f;
                    brain.ValueRW = new BotBrain { state = BotState.Advance, objectiveIndex = _rng.NextInt(5), nextThink = now + 0.2f };
                    perc.ValueRW = new BotPerception { targetActorId = -1, candidateActorId = -1 };
                    w.ValueRW.ammo = w.ValueRO.magazine; w.ValueRW.reloadEnd = 0; w.ValueRW.burstLeft = 0;
                    ecb.SetComponent(ent, new BotMotion { lastPos = pos, yaw = _rng.NextFloat(math.PI * 2) });
                    ecb.SetComponentEnabled<BotDead>(ent, false);
                    if (SystemAPI.HasComponent<VatClip>(ent)) ecb.SetComponent(ent, new VatClip { value = 0 });
                    if (SystemAPI.HasComponent<BotRagdoll>(ent)) ecb.SetComponent(ent, new BotRagdoll());
                    t.ValueRW.Rotation = quaternion.identity;
                }
                ecb.Playback(EntityManager);
                ecb.Dispose();
            }

            BotRegistry.FlushKills();
        }
    }

    /// <summary>
    /// VAT-анимация: клип по состоянию движения, время клипа — per-instance свойство материала.
    /// Дальние боты (LOD2) получают тот же поток данных — шейдер сам упрощает выборку.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct BotAnimationSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new AnimJob { dt = SystemAPI.Time.DeltaTime }.ScheduleParallel();
        }

        [BurstCompile]
        partial struct AnimJob : IJobEntity
        {
            public float dt;

            void Execute(ref VatClip clip, ref VatTime time, in BotMotion m, in BotPerception p, in BotHealth hp)
            {
                if (hp.value <= 0) { time.value += dt; return; } // клип смерти (5) проигрывается один раз — clamp в шейдере
                float speed = math.length(m.velocity.xz);
                float next = m.crouched ? (speed > 0.3f ? 3 : 2) : speed > 0.3f ? 1 : (p.targetVisible ? 4 : 0);
                if (next != clip.value) { clip.value = next; time.value = 0; }
                // Скорость проигрывания бега — по фактической скорости, чтобы ноги не «скользили».
                time.value += dt * (next == 1 || next == 3 ? math.max(0.5f, speed / 5f) : 1f);
            }
        }
    }
}
