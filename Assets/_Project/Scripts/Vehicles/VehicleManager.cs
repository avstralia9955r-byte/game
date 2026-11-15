using System.Collections;
using Frontline.Bots;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Техника матча: по 1–2 танка на команду у баз, экипаж ботов, возрождение уничтоженных через respawnDelay,
    /// посадка/высадка игрока. Уничтоженный вражеский танк или джип засчитывается противнику как убийство (GameMode.OnVehicleLost).
    /// </summary>
    /// <summary>
    /// Зона высадки у базы: ИИ техники и артиллерии не обстреливает пехоту противника в 75 м от её базы — иначе танки
    /// «спамили» по точке появления и выкашивали сотни только что высадившихся бойцов.
    /// </summary>
    public static class BaseZone
    {
        public static bool Near(Vector3 p, byte team, float r = 75f)
        {
            var vm = VehicleManager.I;
            if (!vm || team > 1) return false;
            Vector3 b = vm.BaseOf(team);
            float dx = b.x - p.x, dz = b.z - p.z;
            return dx * dx + dz * dz < r * r;
        }
    }

    public class VehicleManager : MonoBehaviour
    {
        public static VehicleManager I { get; private set; }

        public int tanksPerTeam = 1;              // больше из двух сторон (размер массива слотов)
        public int tanksGermany = 1, tanksUssr = 1;
        public int jeepsPerTeam = 2;          // машины с пулемётом (как джипы Ravenfield)
        int Slots => tanksPerTeam + jeepsPerTeam;
        public float respawnDelay = 30f, firstSpawnDelay = 4f;   // уничтоженная техника появляется у базы через 30 с
        public MapTheme theme;
        public int worldLayer;
        public Vector3 baseGermany, baseUSSR;

        Tank[,] _tanks;
        float[,] _respawnAt, _boardNext;
        int _nextIndex;

        public Vector3 BaseOf(byte team) => team == 0 ? baseGermany : baseUSSR;

        void Awake() => I = this;
        void OnEnable() => Tank.Destroyed += OnTankDestroyed;
        void OnDisable() => Tank.Destroyed -= OnTankDestroyed;
        void OnDestroy() { if (I == this) I = null; }

        IEnumerator Start()
        {
            if (Net.NetSession.IsClient) { enabled = false; yield break; }   // у клиента техника — куклы с хоста
            _tanks = new Tank[2, Slots];
            _respawnAt = new float[2, Slots];
            _boardNext = new float[2, Slots];
            yield return new WaitForSeconds(firstSpawnDelay);     // после запекания flow field — чтобы танки не попали в препятствия
            for (int team = 0; team < 2; team++)
                for (int s = 0; s < Slots; s++) Spawn(team, s);
        }

        void Spawn(int team, int slot)
        {
            Vector3 b = BaseOf((byte)team);
            Vector3 toC = -b; toC.y = 0; toC = toC.sqrMagnitude > 1 ? toC.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toC);
            bool light = slot >= tanksPerTeam;
            if (!light && slot >= (team == 0 ? tanksGermany : tanksUssr)) return;     // у этой стороны танков меньше
            // Танки — ровными рядами по 12 (интервал 10 м, между рядами 14 м) перед базой
            const int perRow = 12;
            int count = team == 0 ? tanksGermany : tanksUssr;
            int row = slot / perRow, col = slot % perRow, inRow = Mathf.Min(perRow, count - row * perRow);
            Vector3 p = light
                ? b + toC * 24f + side * ((slot - tanksPerTeam - (jeepsPerTeam - 1) * 0.5f) * 8f + 70f)
                : b + toC * (40f + row * 14f) + side * ((col - (inRow - 1) * 0.5f) * 10f);
            float lim = MapGenerator.Zone - 10f;
            p.x = Mathf.Clamp(p.x, -lim, lim); p.z = Mathf.Clamp(p.z, -lim, lim);
            var t = Terrain.activeTerrain;
            p.y = t ? t.SampleHeight(p) + t.transform.position.y : GameModeManager.GroundAt(p).y;
            float yaw = Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg;
            var tank = Tank.Create((Faction)team, theme, p, yaw, _nextIndex++, transform, worldLayer, light);
            // Техника появляется пустой: к танку подбегает ближайший свой боец и садится за рычаги;
            // джип ждёт игроков (бойца на свободное место игрок зовёт клавишей 3)
            tank.crew = Tank.Crew.None;
            tank.EmptySince = Time.time;
            _tanks[team, slot] = tank;
        }

        // ---------------- Танк на каждой точке ----------------
        // На каждой точке захвата стоит по танку стороны, которая её держит (нейтральная — танк появится после захвата;
        // в «Командном бою» точки поделены между сторонами поочерёдно). Уничтоженный — снова через respawnDelay.
        Tank[] _pointTanks; float[] _pointRespawn; float _pointCheck;
        static readonly Collider[] _ov = new Collider[16];

        void PointTanks()
        {
            if ((_pointCheck -= Time.deltaTime) > 0) return;
            _pointCheck = 1f;
            var cps = WorldStores.CapturePoints;
            if (!cps.IsCreated || cps.Length == 0) return;
            if (_pointTanks == null || _pointTanks.Length != cps.Length) { _pointTanks = new Tank[cps.Length]; _pointRespawn = new float[cps.Length]; }
            bool capture = MatchBridge.Mode == GameModeType.TerritoryControl;
            for (int i = 0; i < cps.Length; i++)
            {
                if (_pointTanks[i] && _pointTanks[i].Alive) continue;
                if (_pointTanks[i] && !_pointTanks[i].Alive) { _pointTanks[i] = null; _pointRespawn[i] = Time.time + respawnDelay; }
                if (Time.time < _pointRespawn[i]) continue;
                byte owner = capture ? cps[i].owner : (byte)(i % 2);
                if (owner > 1) continue;
                Vector3 c = cps[i].position;
                Vector3 toBase = BaseOf(owner) - c; toBase.y = 0; toBase = toBase.sqrMagnitude > 1 ? toBase.normalized : Vector3.forward;
                if (!FreeSpot(c + toBase * (cps[i].radius + 8f), out var p)) continue;
                float yaw = Mathf.Atan2(-toBase.x, -toBase.z) * Mathf.Rad2Deg;
                var t = Tank.Create((Faction)owner, theme, p, yaw, _nextIndex++, transform, worldLayer, false);
                t.crew = Tank.Crew.None; t.EmptySince = Time.time;
                _pointTanks[i] = t;
            }
        }

        /// <summary>Свободное ровное место под танк рядом с точкой (по кругу, без домов и стен).</summary>
        static bool FreeSpot(Vector3 near, out Vector3 spot)
        {
            var terr = Terrain.activeTerrain;
            float lim = MapGenerator.Zone - 12f;
            for (int ring = 0; ring < 5; ring++)
                for (int k = 0; k < 10; k++)
                {
                    float a = k * Mathf.PI * 0.2f + ring * 0.3f, r = ring * 6f;
                    Vector3 p = near + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                    if (Mathf.Abs(p.x) > lim || Mathf.Abs(p.z) > lim) continue;
                    p.y = terr ? terr.SampleHeight(p) + terr.transform.position.y : GameModeManager.GroundAt(p).y;
                    int n = Physics.OverlapBoxNonAlloc(p + Vector3.up * 1.6f, new Vector3(2.2f, 1.2f, 3.6f), _ov, Quaternion.identity, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
                    bool busy = false;
                    for (int q = 0; q < n; q++) if (!(_ov[q] is TerrainCollider)) { busy = true; break; }
                    if (busy) continue;
                    spot = p; return true;
                }
            spot = near; return false;
        }

        void Update()
        {
            if (_tanks == null) return;
            PointTanks();
            var pc = PlayerBridge.Controller;
            for (int team = 0; team < 2; team++)
                for (int s = 0; s < Slots; s++)
                {
                    var t = _tanks[team, s];
                    if (!t)
                    {
                        if (_respawnAt[team, s] > 0 && Time.time >= _respawnAt[team, s]) { _respawnAt[team, s] = 0; Spawn(team, s); }
                        continue;
                    }
                    // Джипы: ближайшие свои бойцы садятся на свободные места (водитель → пулемёт → пассажир)
                    if (t.IsLight) t.RemoteRider = RemoteNear(t);
                    // Экипаж набирается сам: Tank.CrewRecruiting (боец бежит к технике и садится)
                }
        }

        /// <summary>
        /// Посадка ботов в джип: живые свои бойцы в радиусе 16 м (не игрок, не сетевые). Если игрок стоит рядом
        /// у пустой машины — место водителя ему оставляем (садятся только стрелок и пассажир).
        /// </summary>
        static bool RemoteNear(Tank t)
        {
            if (!Net.NetSession.IsHost || !CombatWorld.Initialized) return false;
            var actors = CombatWorld.Actors;
            Vector3 g = t.SeatTransform(JeepSeat.Gunner).position;
            for (int i = 0; i < actors.Length; i++)
                if (Net.NetSession.IsRemoteSlot(actors[i].actorId) && actors[i].alive && Vector3.Distance(actors[i].position, g) < 1.8f) return true;
            return false;
        }

        static void BoardNearby(Tank t, PlayerController pc)
        {
            if (!t.AnySeatFree) return;
            var actors = CombatWorld.Actors;
            if (!actors.IsCreated) return;
            bool playerNear = pc && !pc.Vehicle && pc.Team == t.Team && Vector3.Distance(pc.transform.position, t.transform.position) < 12f;
            Vector3 c = t.transform.position;
            for (int i = 1; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team != t.Team || Net.NetSession.IsRemoteSlot(a.actorId)) continue;
                if (Vector3.Distance(a.position, c) > 16f) continue;
                if (playerNear && t.FirstFreeSeat() == JeepSeat.Driver && !t.SeatFree(JeepSeat.Gunner) && !t.SeatFree(JeepSeat.Passenger)) break;
                if (playerNear && t.FirstFreeSeat() == JeepSeat.Driver)
                {
                    // водительское место оставляем игроку: сажаем на пулемёт/пассажиром
                    if (!t.TryBoardBotAt(a.actorId, t.SeatFree(JeepSeat.Gunner) ? JeepSeat.Gunner : JeepSeat.Passenger)) continue;
                }
                else if (!t.TryBoardBot(a.actorId)) continue;
                if (!t.AnySeatFree) break;
            }
        }

        void OnTankDestroyed(Tank t, int killer)
        {
            if (_tanks != null)
                for (int team = 0; team < 2; team++)
                    for (int s = 0; s < Slots; s++)
                        if (_tanks[team, s] == t) { _tanks[team, s] = null; _respawnAt[team, s] = Time.time + respawnDelay; }
            if (GameModeManager.I) GameModeManager.I.OnVehicleDestroyed(t, killer);
        }

        public static void EnterPlayer(Tank t, PlayerController pc)
        {
            EnterPlayer(t, pc, JeepSeat.None);
        }

        /// <summary>Посадка с выбором места (джип: водитель или пулемётчик; None — первое свободное).</summary>
        public static void EnterPlayer(Tank t, PlayerController pc, JeepSeat want)
        {
            if (!t || !t.Alive || !pc || t.PlayerAboard) return;
            var seat = t.IsLight ? (want != JeepSeat.None && t.SeatFree(want) ? want : t.FirstFreeSeat()) : (t.crew == Tank.Crew.Player ? JeepSeat.None : JeepSeat.Driver);
            if (seat == JeepSeat.None) return;
            t.OnPlayerEnter(pc, seat);
            pc.EnterVehicle(t);
            if (seat == JeepSeat.Passenger) pc.SetPassenger(true);
            BotAudio.PlayAt(SoundSynth.Hatch(), t.transform.position + Vector3.up * 2f, 0.8f);
        }

        public static void EjectPlayer(Tank t, bool killed, int killer)
        {
            var pc = PlayerBridge.Controller;
            if (!pc || pc.Vehicle != t) return;
            Vector3 exit = FindExit(t);
            t.OnPlayerExit();
            pc.ExitVehicle(exit, t.transform.eulerAngles.y);
            BotAudio.PlayAt(SoundSynth.Hatch(), exit + Vector3.up, 0.8f);
            if (killed)
                CombatWorld.Damage.Enqueue(new DamageEvent { targetActorId = CombatWorld.PlayerActorId, sourceActorId = killer, amount = 9999f, explosive = true, hitPoint = exit, direction = Vector3.up });
        }

        static Vector3 FindExit(Tank t)
        {
            var tr = t.transform;
            Vector3[] cands = { -tr.right * 2.9f, tr.right * 2.9f, -tr.forward * 4.3f, tr.forward * 4.5f, Vector3.up * 2.6f };
            foreach (var off in cands)
            {
                Vector3 p = tr.position + new Vector3(off.x, 0, off.z);
                if (off.y > 0) return tr.position + off;
                var terr = Terrain.activeTerrain;
                p.y = terr ? Mathf.Max(terr.SampleHeight(p) + terr.transform.position.y, tr.position.y - 1.5f) : tr.position.y;
                if (!Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.34f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore))
                    return p + Vector3.up * 0.1f;
            }
            return tr.position + Vector3.up * 2.6f;
        }
    }
}
