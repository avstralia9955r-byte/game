using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Frontline.Bots
{
    public struct BotBuildRequest { public float3 position; public float yaw; public byte team; }

    public struct KillEvent { public int killerActorId, victimActorId; public byte victimTeam; public bool headshot; }

    /// <summary>Связь actorId → Entity и события убийств для режима игры/HUD (лента убийств).</summary>
    public static class BotRegistry
    {
        public const int MaxBots = 512;
        public static NativeArray<Entity> ActorToEntity;    // индекс = actorId (0 — игрок, Entity.Null)
        public static NativeQueue<KillEvent> Kills;
        public static NativeQueue<BotBuildRequest> BuildRequests;   // инженеры-боты (Hard+) → BrickSystem
        public static int BotCount;

        public static event Action<KillEvent> KillHappened;  // главный поток, после DamageSystem

        public static void Init()
        {
            if (ActorToEntity.IsCreated) return;
            ActorToEntity = new NativeArray<Entity>(MaxBots + 1, Allocator.Persistent);
            Kills = new NativeQueue<KillEvent>(Allocator.Persistent);
            BuildRequests = new NativeQueue<BotBuildRequest>(Allocator.Persistent);
            UnityEngine.Application.quitting += Dispose;
        }

        public static void Dispose()
        {
            if (ActorToEntity.IsCreated) ActorToEntity.Dispose();
            if (Kills.IsCreated) Kills.Dispose();
            if (BuildRequests.IsCreated) BuildRequests.Dispose();
        }

        public static void FlushKills()
        {
            while (Kills.TryDequeue(out var k)) KillHappened?.Invoke(k);
        }
    }

    /// <summary>Выбор точки возрождения: база команды или (в TC) захваченная точка, не занятая врагом.</summary>
    public static class SpawnPicker
    {
        public static float3 Pick(byte team, ref Random rng, bool allowCapturePoints)
        {
            if (allowCapturePoints && WorldStores.CapturePoints.IsCreated && rng.NextFloat() < 0.6f)
            {
                int start = rng.NextInt(WorldStores.CapturePoints.Length);
                for (int k = 0; k < WorldStores.CapturePoints.Length; k++)
                {
                    var cp = WorldStores.CapturePoints[(start + k) % WorldStores.CapturePoints.Length];
                    if (cp.owner == team && cp.contested == 0)
                    {
                        float2 off = rng.NextFloat2Direction() * rng.NextFloat(cp.radius * 0.5f, cp.radius * 1.3f);
                        return SnapToGround(cp.position + new float3(off.x, 0, off.y));
                    }
                }
            }
            var spawns = team == (byte)Faction.Germany ? WorldStores.SpawnsGermany : WorldStores.SpawnsUSSR;
            if (!spawns.IsCreated || spawns.Length == 0) return float3.zero;
            var s = spawns[rng.NextInt(spawns.Length)];
            float2 o = rng.NextFloat2Direction() * rng.NextFloat(0f, 4f);
            return SnapToGround(s + new float3(o.x, 0, o.y));
        }

        public static float3 SnapToGround(float3 p)
        {
            if (!WorldStores.Height.IsCreated) return p;
            p.y = FlowField.GroundHeight(WorldStores.Height, WorldStores.GridSize, WorldStores.GridOrigin, WorldStores.CellSize, p);
            return p;
        }
    }
}
