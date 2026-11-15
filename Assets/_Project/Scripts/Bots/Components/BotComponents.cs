using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Frontline.Bots
{
    public enum BotState : byte
    {
        Advance,        // к цели режима (точка захвата / ближайший враг в TDM) по flow field
        Engage,         // бой с видимой целью
        SeekCover,      // перебежка к укрытию (Hard+)
        InCover,        // стрельба/перезарядка из укрытия
        Flank,          // обход цели сбоку (Realistic, частично Hard)
        Investigate,    // идёт на звук (Hard+)
        Defend,         // удерживает захваченную точку
        Build           // инженер строит стену на точке (Hard+)
    }

    public struct BotIdentity : IComponentData
    {
        public int actorId;            // 1..N, индекс в CombatWorld.Actors
        public byte team;              // = (byte)Faction
        public SoldierClass soldierClass;
        public uint seed;
    }

    public struct BotHealth : IComponentData { public float value, max; public float lastHitTime; public float protectUntil; }   // protectUntil — защита после появления

    /// <summary>Включается при смерти. Боты с включённым BotDead не думают, не двигаются и не видны.</summary>
    public struct BotDead : IComponentData, IEnableableComponent { public float respawnAt; public int killerActorId; }

    /// <summary>
    /// Бот сидит в машине (джип): не думает, не ходит и не стреляет сам — положение задаёт машина, стрельбу ведёт она же
    /// (водитель рулит, стрелок — за пулемётом, пассажир — из своего оружия). Части тела скрыты (DisableRendering).
    /// </summary>
    public struct BotInVehicle : IComponentData, IEnableableComponent { public int vehicleIndex; public byte seat; }

    public struct BotMotion : IComponentData
    {
        public float3 velocity;
        public float yaw, pitch;
        public float speed;            // желаемая скорость (м/с)
        public bool crouched;
        public float stuckTimer;
        public float stuckTotal;       // сколько раз подряд не помог обход по NavMesh — после 4 бойца переставляем
        public float3 lastPos;
        public float gait;             // накопленная фаза шага (рад) — ноги не «дёргаются» при смене скорости
        public float glanceYaw, glanceUntil, nextGlance;   // осмотр по сторонам и назад
    }

    public struct BotBrain : IComponentData
    {
        public BotState state;
        public float stateTime;
        public float nextThink;
        public int objectiveIndex;     // индекс flow field (0..4 — точки A–E, 5/6 — базы команд)
        public float3 moveTarget;      // для Cover/Flank/Investigate (по NavMesh-пути)
        public float3 coverNormal;
        public bool hasPath;
        public float nextBuild;
    }

    public struct BotPerception : IComponentData
    {
        public int targetActorId;      // -1 = нет
        public float3 targetPos;       // последняя известная позиция
        public float3 targetVel;
        public float lastSeenTime;
        public float firstSeenTime;    // для времени реакции
        public bool targetVisible;
        public float3 heardPos;
        public float heardTime;
        public float suppression;      // 0..1 — пули рядом, растёт от промахов по боту
        public int candidateActorId;   // кандидат от этапа поиска, проверяется лучом
        public float awareness;        // 0..1 — насколько бот «разглядел» кандидата (1 — заметил)
        public int awareOf;            // кого именно разглядывает
        public float lastVisionCheck;
    }

    public struct BotWeapon : IComponentData
    {
        public byte weaponIndex;       // индекс в BotWeaponTable (категория фракции)
        public float damage, headMul, fireInterval, range, muzzleVelocity, loudness;
        public float spreadDeg, recoilClimbDeg;
        public int magazine, ammo;
        public float reloadTime, reloadEnd, nextFire;
        public int burstLeft;
        public bool suppressed, isAuto;
    }

    /// <summary>Параметры сложности (заполняются из BotSkillTable по BotDifficulty).</summary>
    public struct BotSkill : IComponentData
    {
        public float reactionTime;     // с, от обнаружения до первого выстрела
        public float aimErrorDeg;      // базовая ошибка прицела
        public float aimSettleTime;    // за сколько ошибка сходится к минимуму
        public float visionRange, fovCos, hearingMul;
        public int burstMin, burstMax;
        public bool usesCover, flanks, reactsToSound, builds;
        public float thinkInterval;
    }

    public struct BotPathPoint : IBufferElementData { public float3 position; }
    public struct BotPathCursor : IComponentData { public int index; }

    /// <summary>Запрос пути по NavMesh (выполняет BotPathRequestSystem с бюджетом N путей/кадр).</summary>
    public struct BotPathRequest : IComponentData { public float3 destination; public bool pending; public float requestTime; }

    // ---- Рендер: VAT-анимация (Vertex Animation Texture) через per-instance свойства Entities Graphics ----

    [MaterialProperty("_VatClip")]  public struct VatClip : IComponentData { public float value; }   // 0 idle,1 run,2 crouch,3 crouchwalk,4 shoot,5 death
    [MaterialProperty("_VatTime")]  public struct VatTime : IComponentData { public float value; }
    [MaterialProperty("_TeamTint")] public struct TeamTint : IComponentData { public float4 value; }

    // ---- Синглтоны ----

    public struct BotPrefabs : IComponentData { public Entity germany, ussr; }

    public struct BotSpawnRequest : IComponentData
    {
        public int count;
        public BotDifficulty difficulty;
        public Faction playerFaction;
        public bool processed;
    }
}
