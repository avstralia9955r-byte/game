using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Frontline
{
    public struct ActorSnapshot
    {
        public float3 position;     // ступни
        public float3 velocity;
        public int actorId;         // 0 = игрок, 1..N = боты (индекс в BotIdentity)
        public byte team;
        public bool alive;
        public bool crouched;
    }

    public struct DamageEvent
    {
        public int targetActorId;
        public int sourceActorId;
        public float amount;
        public float3 hitPoint;
        public float3 direction;
        public bool headshot;
        public bool explosive;
    }

    /// <summary>
    /// Шумовое событие для «слуха» ботов. Выстрел с глушителем публикуется с радиусом ≈ 0,
    /// поэтому боты его не слышат (видеть стрелка они по-прежнему могут).
    /// </summary>
    public struct NoiseEvent
    {
        public float3 position;
        public float radius;
        public int sourceActorId;
        public byte team;
        public byte kind;           // 0 = выстрел, 1 = взрыв, 2 = шаги, 3 = строительство
    }

    public struct ActorHit
    {
        public int actorId;
        public float distance;
        public bool headshot;
        public float3 point;
    }

    /// <summary>
    /// Общий «мост» между GameObject-миром (игрок, оружие, кирпичи) и ECS-миром (боты).
    /// Мир (рельеф, здания, кирпичи) — коллайдеры PhysX; акторы — аналитические капсулы
    /// (дешевле, чем 128 кинематических тел, и считается в Burst).
    /// </summary>
    public static class CombatWorld
    {
        public const int PlayerActorId = 0;
        public const float BodyRadius = 0.3f, StandHeight = 1.8f, CrouchHeight = 1.2f, HeadRadius = 0.14f;

        public static NativeList<ActorSnapshot> Actors;
        public static NativeQueue<DamageEvent> Damage;
        public static NativeList<NoiseEvent> Noise;       // заполняется за кадр, очищается BotHearingSystem
        public static NativeList<NoiseEvent> NoiseWrite;  // двойная буферизация: пишем сюда, система меняет местами

        public static int WorldMask, BrickMask;

        public static bool Initialized => Actors.IsCreated;

        public static void Init()
        {
            if (Initialized) return;
            Actors = new NativeList<ActorSnapshot>(520, Allocator.Persistent);   // до 512 ботов + игрок
            Damage = new NativeQueue<DamageEvent>(Allocator.Persistent);
            Noise = new NativeList<NoiseEvent>(64, Allocator.Persistent);
            NoiseWrite = new NativeList<NoiseEvent>(64, Allocator.Persistent);
            WorldMask = LayerMask.GetMask("World", "Bricks");
            BrickMask = LayerMask.GetMask("Bricks");
            // Слои ещё не заведены (Frontline → Setup Project не запускали) — работаем по слоям по умолчанию.
            if (WorldMask == 0) WorldMask = Physics.DefaultRaycastLayers;
            if (BrickMask == 0) BrickMask = WorldMask;
            Application.quitting += Dispose;
        }

        public static void Dispose()
        {
            if (!Initialized) return;
            Actors.Dispose(); Damage.Dispose(); Noise.Dispose(); NoiseWrite.Dispose();
        }

        public static void EmitNoise(Vector3 pos, float radius, int source, byte team, byte kind = 0)
        {
            if (!Initialized || radius <= 0.5f) return;
            NoiseWrite.Add(new NoiseEvent { position = pos, radius = radius, sourceActorId = source, team = team, kind = kind });
        }

        /// <summary>Вызывается системой слуха раз в кадр: то, что накопилось, становится читаемым.</summary>
        public static void SwapNoiseBuffers()
        {
            (Noise, NoiseWrite) = (NoiseWrite, Noise);
            NoiseWrite.Clear();
        }

        /// <summary>Луч по акторам (кроме своей команды). Для игрока — 1 луч × 128 капсул, микросекунды.</summary>
        public static bool RaycastActors(float3 origin, float3 dir, float maxDist, byte ignoreTeam, int ignoreActor, out ActorHit hit)
        {
            return RaycastActorsBurst(Actors.AsArray(), origin, dir, maxDist, ignoreTeam, ignoreActor, out hit);
        }

        // Без [BurstCompile]: Burst не поддерживает прямой вызов с out-структурой (BC1063). 1 луч × 129 капсул — микросекунды.
        public static bool RaycastActorsBurst(in NativeArray<ActorSnapshot> actors, in float3 origin, in float3 dir,
                                              float maxDist, byte ignoreTeam, int ignoreActor, out ActorHit hit)
        {
            hit = default;
            hit.distance = maxDist;
            bool any = false;
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team == ignoreTeam || a.actorId == ignoreActor) continue;

                float h = a.crouched ? CrouchHeight : StandHeight;
                float3 headC = a.position + new float3(0, h - HeadRadius, 0);
                if (RaySphere(origin, dir, headC, HeadRadius, out float tHead) && tHead < hit.distance)
                {
                    hit = new ActorHit { actorId = a.actorId, distance = tHead, headshot = true, point = origin + dir * tHead };
                    any = true;
                    continue;
                }
                float3 p0 = a.position + new float3(0, BodyRadius, 0);
                float3 p1 = a.position + new float3(0, h - HeadRadius * 2f - 0.02f, 0);
                if (RayCapsule(origin, dir, p0, p1, BodyRadius, out float tBody) && tBody < hit.distance)
                {
                    hit = new ActorHit { actorId = a.actorId, distance = tBody, headshot = false, point = origin + dir * tBody };
                    any = true;
                }
            }
            return any;
        }

        public static bool RaySphere(float3 ro, float3 rd, float3 c, float r, out float t)
        {
            float3 oc = ro - c;
            float b = math.dot(oc, rd);
            float cc = math.dot(oc, oc) - r * r;
            float disc = b * b - cc;
            t = 0;
            if (disc < 0) return false;
            t = -b - math.sqrt(disc);
            if (t < 0) t = -b + math.sqrt(disc);
            return t >= 0;
        }

        /// <summary>Луч–капсула (Íñigo Quílez). rd нормализован.</summary>
        public static bool RayCapsule(float3 ro, float3 rd, float3 pa, float3 pb, float r, out float t)
        {
            t = 0;
            float3 ba = pb - pa, oa = ro - pa;
            float baba = math.dot(ba, ba), bard = math.dot(ba, rd), baoa = math.dot(ba, oa);
            float rdoa = math.dot(rd, oa), oaoa = math.dot(oa, oa);
            float a = baba - bard * bard;
            float b = baba * rdoa - baoa * bard;
            float c = baba * oaoa - baoa * baoa - r * r * baba;
            float h = b * b - a * c;
            if (h >= 0)
            {
                float tt = (-b - math.sqrt(h)) / a;
                float y = baoa + tt * bard;
                if (y > 0 && y < baba && tt >= 0) { t = tt; return true; }
                float3 oc = y <= 0 ? oa : ro - pb;
                b = math.dot(rd, oc);
                c = math.dot(oc, oc) - r * r;
                h = b * b - c;
                if (h > 0) { t = -b - math.sqrt(h); return t >= 0; }
            }
            return false;
        }
    }
}
