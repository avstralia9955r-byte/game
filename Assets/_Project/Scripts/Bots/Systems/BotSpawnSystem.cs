using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Frontline.Bots
{
    /// <summary>Параметры матча, которые MonoBehaviour-часть (GameModeManager) передаёт в ECS.</summary>
    public static class MatchBridge
    {
        public static bool SpawnRequested;
        public static int BotCount = 128;
        public static BotDifficulty Difficulty = BotDifficulty.Medium;
        public static Faction PlayerFaction = Faction.USSR;
        public static bool PointSpawns = true;      // TC: можно возрождаться на своих точках
        public static float RespawnDelay = 6f;
        public static bool MatchRunning;
        public static GameModeType Mode = GameModeType.TerritoryControl;
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class BotSpawnSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<BotPrefabs>();
            BotRegistry.Init();
            CombatWorld.Init();
        }

        protected override void OnUpdate()
        {
            if (!MatchBridge.SpawnRequested || !WorldStores.Ready) return;
            if (Net.NetSession.IsClient && !Net.NetDriver.HostStarted) return;   // ждём, пока хост создаст своих ботов
            MatchBridge.SpawnRequested = false;

            // Удалить ботов прошлого матча.
            EntityManager.DestroyEntity(GetEntityQuery(typeof(BotIdentity)));

            var prefabs = SystemAPI.GetSingleton<BotPrefabs>();
            // Варианты по классам (оружие и снаряжение) — если заданы.
            var prefabEntity = SystemAPI.GetSingletonEntity<BotPrefabs>();
            var variants = EntityManager.HasBuffer<Frontline.BotPrefabVariant>(prefabEntity)
                ? EntityManager.GetBuffer<Frontline.BotPrefabVariant>(prefabEntity).ToNativeArray(Unity.Collections.Allocator.Temp)
                : default;
            int n = math.clamp(MatchBridge.BotCount, 0, BotRegistry.MaxBots);
            BotRegistry.BotCount = n;
            // + слоты сетевых игроков после ботов
            CombatWorld.Actors.Resize(n + 1 + (Net.NetSession.Active ? Net.NetSession.MaxRemote : 0), NativeArrayOptions.ClearMemory);

            // В сетевой игре раскладка команд и классов одинакова у хоста и клиентов: общая сторона хоста и общее зерно
            byte playerTeam = (byte)(Net.NetSession.IsClient ? Net.NetSession.HostFaction : MatchBridge.PlayerFaction);
            var skill = BotSkillTable.For(MatchBridge.Difficulty);
            var rng = Random.CreateFromIndex(Net.NetSession.Active ? Net.NetSession.Seed : (uint)System.Environment.TickCount);

            // Союзников на одного меньше — игрок занимает слот своей команды (64 на 64 = 63 бота + игрок против 64).
            int allies = n / 2 - (n % 2 == 0 ? 1 : 0);
            allies = math.max(0, allies);

            for (int i = 0; i < n; i++)
            {
                int actorId = i + 1;
                byte team = i < allies ? playerTeam : (byte)(1 - playerTeam);
                var prefab = team == (byte)Faction.Germany ? prefabs.germany : prefabs.ussr;
                var cls = PickClass(ref rng);
                if (variants.IsCreated)
                    for (int v = 0; v < variants.Length; v++)
                        if (variants[v].team == team && variants[v].soldierClass == cls) { prefab = variants[v].prefab; break; }
                var e = EntityManager.Instantiate(prefab);

                var pos = SpawnPicker.Pick(team, ref rng, false);
                EntityManager.SetComponentData(e, LocalTransform.FromPositionRotation(pos, quaternion.RotateY(rng.NextFloat(math.PI * 2))));
                EntityManager.SetComponentData(e, new BotIdentity { actorId = actorId, team = team, soldierClass = cls, seed = rng.NextUInt() | 1u });
                EntityManager.SetComponentData(e, skill);
                var w = BotWeaponTable.Default((Faction)team, cls);
                w.burstLeft = 0;
                EntityManager.SetComponentData(e, w);
                EntityManager.SetComponentData(e, new BotBrain
                {
                    state = BotState.Advance,
                    nextThink = rng.NextFloat(0, skill.thinkInterval), // разнос по кадрам
                    objectiveIndex = rng.NextInt(5)
                });
                EntityManager.SetComponentData(e, new BotMotion { lastPos = pos });
                var bh = EntityManager.GetComponentData<BotHealth>(e);
                bh.protectUntil = (float)World.Time.ElapsedTime + 10f;        // 10 с неуязвимости после появления
                EntityManager.SetComponentData(e, bh);
                if (EntityManager.HasComponent<TeamTint>(e))
                    EntityManager.SetComponentData(e, new TeamTint
                    {
                        value = team == (byte)Faction.Germany ? new float4(0.85f, 0.88f, 0.9f, 1) : new float4(0.9f, 0.88f, 0.8f, 1)
                    });
                BotRegistry.ActorToEntity[actorId] = e;
            }
            MatchBridge.MatchRunning = true;
            UnityEngine.Debug.Log($"[Bots] Создано {n} ботов ({MatchBridge.Difficulty})");
        }

        static SoldierClass PickClass(ref Random rng)
        {
            float r = rng.NextFloat();
            return r < 0.40f ? SoldierClass.Assault : r < 0.65f ? SoldierClass.Engineer : r < 0.85f ? SoldierClass.Support : SoldierClass.Recon;
        }
    }

    /// <summary>
    /// Публикует позиции ботов в CombatWorld.Actors (индекс = actorId) — общий источник для слуха, зрения,
    /// попаданий игрока и проверки строительства.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct BotSnapshotSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            if (!CombatWorld.Initialized || CombatWorld.Actors.Length <= 1) return;
            state.Dependency = new SnapshotJob { actors = CombatWorld.Actors.AsArray() }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
        }

        [BurstCompile]
        partial struct SnapshotJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public NativeArray<ActorSnapshot> actors;

            void Execute(in BotIdentity id, in LocalTransform t, in BotMotion m, in BotHealth hp)
            {
                if (id.actorId <= 0 || id.actorId >= actors.Length) return;
                actors[id.actorId] = new ActorSnapshot
                {
                    position = t.Position, velocity = m.velocity, actorId = id.actorId,
                    team = id.team, alive = hp.value > 0, crouched = m.crouched
                };
            }
        }
    }
}
