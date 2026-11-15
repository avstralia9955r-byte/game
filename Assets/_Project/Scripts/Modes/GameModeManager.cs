using System;
using System.Collections;
using Frontline.Bots;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Frontline
{
    /// <summary>
    /// Точка входа игровой сцены: читает MatchSession, настраивает ECS-ботов, точки, спавны, камуфляж,
    /// погоду, игрока; тикает режим; выдаёт награды (локальная валюта) по окончании.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameModeManager : MonoBehaviour
    {
        public static GameModeManager I { get; private set; }

        [Header("Сцена карты")]
        public CapturePoint[] capturePoints = new CapturePoint[5];
        public Transform[] spawnsGermany, spawnsUSSR;
        public PlayerController playerPrefab;
        public CamoController camo;
        public EnvironmentController environment;
        public float playerRespawnDelay = 5f;

        public GameMode Mode { get; private set; }
        public PlayerController Player { get; private set; }
        public MatchConfig Config { get; private set; }
        public int PlayerKills { get; private set; }
        public int PlayerDeaths { get; private set; }
        public float PlayerRespawnAt { get; private set; }
        public int PlayerTankKills { get; private set; }
        /// <summary>Игрок мёртв и таймер вышел — показываем экран высадки (выбор точки и класса).</summary>
        public bool AwaitingDeploy { get; private set; }
        public float AutoDeployAt { get; private set; }
        /// <summary>-1 — база, 0..4 — точка захвата.</summary>
        public int DeploySpawn { get; set; } = -1;
        public event Action<Tank, int> VehicleDestroyed;

        public event Action<KillEvent> KillFeed;
        public event Action<GameMode> MatchEnded;

        bool _ended;

        void Awake()
        {
            I = this;
            if (SaveSystem.Settings == null) SaveSystem.LoadAll();
            GameInput.Init();
            CombatWorld.Init();
            BotRegistry.Init();
            Config = MatchSession.Current;
            if (!Config.map) Config.map = GameDatabase.Instance ? GameDatabase.Instance.maps.Find(m => m && m.sceneName == SceneManager.GetActiveScene().name) : null;

            SetupStores();
            Mode = Config.mode == GameModeType.TerritoryControl
                ? new TerritoryControlMode(Config, capturePoints)
                : new TeamDeathmatchMode(Config);

            var theme = Config.map ? Config.map.theme : MapTheme.Summer;
            if (camo && Config.map) camo.Apply(Config.map);
            if (environment) environment.Apply(theme, Config.timeOfDay, Config.weather);

            MatchBridge.BotCount = Config.botCount;
            MatchBridge.Difficulty = Config.difficulty;
            MatchBridge.PlayerFaction = Config.playerFaction;
            MatchBridge.Mode = Config.mode;
            MatchBridge.PointSpawns = Config.mode == GameModeType.TerritoryControl;
            // Боты появляются после первой высадки игрока — когда известна его сторона
            MatchBridge.SpawnRequested = false;

            BotRegistry.KillHappened += OnKill;
        }

        void OnDestroy()
        {
            BotRegistry.KillHappened -= OnKill;
            MatchBridge.MatchRunning = false;
            if (WorldStores.CapturePoints.IsCreated) WorldStores.CapturePoints.Dispose();
            if (WorldStores.SpawnsGermany.IsCreated) WorldStores.SpawnsGermany.Dispose();
            if (WorldStores.SpawnsUSSR.IsCreated) WorldStores.SpawnsUSSR.Dispose();
        }

        void SetupStores()
        {
            if (WorldStores.CapturePoints.IsCreated) WorldStores.CapturePoints.Dispose();
            int n = 0; foreach (var c in capturePoints) if (c) n++;
            WorldStores.CapturePoints = new NativeArray<CapturePointData>(n, Allocator.Persistent);
            int k = 0;
            foreach (var c in capturePoints)
                if (c) WorldStores.CapturePoints[k++] = new CapturePointData { position = c.transform.position, radius = c.radius, owner = 255 };

            WorldStores.SpawnsGermany = ToNative(spawnsGermany);
            WorldStores.SpawnsUSSR = ToNative(spawnsUSSR);
        }

        static NativeArray<float3> ToNative(Transform[] t)
        {
            var a = new NativeArray<float3>(t?.Length ?? 0, Allocator.Persistent);
            for (int i = 0; i < a.Length; i++) a[i] = t[i].position;
            return a;
        }

        /// <summary>Матч начался (первая высадка): идёт время, появились боты.</summary>
        public bool Started { get; private set; }
        /// <summary>Первая высадка: на экране выбирается сторона.</summary>
        public bool FirstDeploy => !Started;
        Camera _deployCam;

        IEnumerator Start()
        {
            yield return null; // дать FlowFieldBaker/CoverPointBaker отработать
            // Обзорная камера над полем боя, пока игрок выбирает сторону и класс
            var go = new GameObject("DeployCamera");
            go.transform.SetParent(transform, false);
            _deployCam = go.AddComponent<Camera>();
            _deployCam.farClipPlane = 1500f; _deployCam.fieldOfView = 50f;
            go.AddComponent<AudioListener>();
            go.transform.SetPositionAndRotation(GroundAt(new Vector3(0, 0, -260f)) + Vector3.up * 160f, Quaternion.Euler(38f, 0f, 0f));
            AwaitingDeploy = true;
            AutoDeployAt = float.PositiveInfinity;
            GameInput.SetEnabled(false);
        }

        void LateUpdate()
        {
            if (_deployCam) _deployCam.transform.RotateAround(new Vector3(0, _deployCam.transform.position.y, 0), Vector3.up, Time.deltaTime * 2.5f);
        }

        public Vector3 BaseCenter(byte team)
        {
            var t = team == 0 ? spawnsGermany : spawnsUSSR;
            if (t == null || t.Length == 0) return Vector3.zero;
            Vector3 c = Vector3.zero; foreach (var s in t) c += s.position;
            return c / t.Length;
        }

        /// <summary>Можно ли высадиться на точке: своя и не оспаривается.</summary>
        public bool CanDeployAt(int point)
        {
            if (point < 0) return true;
            if (Config.mode != GameModeType.TerritoryControl || !WorldStores.CapturePoints.IsCreated || point >= WorldStores.CapturePoints.Length) return false;
            var cp = WorldStores.CapturePoints[point];
            return cp.owner == (byte)Config.playerFaction && cp.contested == 0;
        }

        /// <summary>Экран высадки: класс, альтернативное оружие, точка.</summary>
        public void Deploy(SoldierClass cls, bool alt, int point)
        {
            if (!AwaitingDeploy || Mode.Over) return;
            if (!Started)
            {
                Started = true;
                MatchBridge.PlayerFaction = Config.playerFaction;
                MatchBridge.SpawnRequested = true;
                if (_deployCam) Destroy(_deployCam.gameObject);
            }
            Config.playerClass = cls; Config.useAltPrimary = alt;
            DeploySpawn = CanDeployAt(point) ? point : -1;
            AwaitingDeploy = false;
            SpawnPlayer();
            GameInput.SetEnabled(true);
        }

        Vector3 PickPlayerSpawn(ref Unity.Mathematics.Random rng)
        {
            if (DeploySpawn >= 0 && CanDeployAt(DeploySpawn))
            {
                var cp = WorldStores.CapturePoints[DeploySpawn];
                float2 off = rng.NextFloat2Direction() * rng.NextFloat(cp.radius * 0.4f, cp.radius * 1.1f);
                return (Vector3)SpawnPicker.SnapToGround(cp.position + new float3(off.x, 0, off.y));
            }
            var spawns = Config.playerFaction == Faction.Germany ? spawnsGermany : spawnsUSSR;
            if (spawns != null && spawns.Length > 0)
            {
                var s = spawns[rng.NextInt(spawns.Length)].position;
                float2 o = rng.NextFloat2Direction() * rng.NextFloat(0f, 3f);
                return s + new Vector3(o.x, 0, o.y);
            }
            return SpawnPicker.Pick((byte)Config.playerFaction, ref rng, false);
        }

        public void OnVehicleDestroyed(Tank t, int killer)
        {
            Mode?.OnVehicleLost(t.Team);   // уничтоженная техника — убийство для противника
            if (killer == CombatWorld.PlayerActorId && t.Team != (byte)Config.playerFaction) PlayerTankKills++;
            VehicleDestroyed?.Invoke(t, killer);
        }

        /// <summary>Разбито орудие — как уничтоженная техника.</summary>
        public void OnArtilleryDestroyed(ArtilleryGun g, int killer)
        {
            Mode?.OnVehicleLost(g.Team);
            if (killer == CombatWorld.PlayerActorId && g.Team != (byte)Config.playerFaction) PlayerTankKills++;
        }

        /// <summary>Сбит самолёт — как уничтоженная техника: очко противнику.</summary>
        public void OnAircraftDestroyed(Aircraft a, int killer)
        {
            Mode?.OnVehicleLost(a.Team);
            if (killer == CombatWorld.PlayerActorId && a.Team != (byte)Config.playerFaction) PlayerTankKills++;
        }

        void SpawnPlayer()
        {
            var arsenal = GameDatabase.Instance.Arsenal(Config.playerFaction);
            if (!Player)
            {
                Player = Instantiate(playerPrefab);
                Player.Faction = Config.playerFaction;
                var theme = Config.map ? Config.map.theme : MapTheme.Summer;
                var pb = Player.GetComponent<PlayerBody>(); if (pb) pb.SetFaction(Config.playerFaction, theme);
                var arms = Player.GetComponentInChildren<ViewModelArms>(true); if (arms) arms.SetFaction(Config.playerFaction, theme);
                PlayerBridge.Controller = Player;
                PlayerBridge.Health = Player.GetComponent<PlayerHealth>();
                PlayerBridge.Health.Died += OnPlayerDied;
            }
            var rng = Unity.Mathematics.Random.CreateFromIndex((uint)Time.frameCount + 7);
            var pos = PickPlayerSpawn(ref rng);
            if (Player.Vehicle && Player.RideSeat) Player.ExitVehicle(pos, 0);      // второй игрок погиб в машине
            if (Player.Flying) Player.Flying.ExitPlayer(false, -1);
            if (Player.Gunning) Player.Gunning.ExitPlayer(false, -1);
            Player.CarryingShell = false;                                            // снаряд погибшего пропадает
            Player.Teleport(GroundAt(pos) + Vector3.up * 0.15f, LookYawTowardsFront(pos));
            _lastSafe = Player.transform.position;
            PlayerBridge.Health.ResetHealth();
            Player.gameObject.SetActive(true);
            Player.InputEnabled = true;
            if (!Shop.ClassOwned(Config.playerClass)) Config.playerClass = SoldierClass.Assault;   // некупленный класс недоступен
            // Только купленное оружие; основное — выбранное в кастомизации/на экране высадки
            var loadout = Shop.OwnedLoadout(Config.playerFaction, Config.playerClass);
            Binoculars.Allowed = Shop.HasBinoculars;   // бинокль — отдельный слот любого класса (клавиша Z), если куплен
            if (!Player.GetComponent<Binoculars>()) Player.gameObject.AddComponent<Binoculars>();
            var body = Player.GetComponent<PlayerBody>();
            if (body) body.SetClass(loadout.soldierClass);
            Player.GetComponentInChildren<WeaponController>().SetupLoadout(loadout, Config.map ? Config.map.theme : MapTheme.Summer);
            var gt = Player.GetComponentInChildren<GrenadeThrower>();
            if (gt) gt.Refill(loadout.grenades);
        }

        Vector3 _lastSafe;
        float _safeTimer;

        /// <summary>Высота поверхности в точке: луч сверху по миру, затем ландшафт, затем сетка навигации.</summary>
        public static Vector3 GroundAt(Vector3 p)
        {
            Physics.SyncTransforms();
            var top = new Vector3(p.x, 1500f, p.z);
            if (Physics.Raycast(top, Vector3.down, out var hit, 3000f, CombatWorld.WorldMask | 1, QueryTriggerInteraction.Ignore))
                return hit.point;
            var t = Terrain.activeTerrain;
            if (t) return new Vector3(p.x, t.SampleHeight(p) + t.transform.position.y, p.z);
            return p;
        }

        /// <summary>Если игрок провалился под ландшафт — возвращаем на последнее безопасное место.</summary>
        void FallGuard()
        {
            if (!Player || !Player.gameObject.activeInHierarchy) return;
            if (Player.Flying || Player.Gunning) { _safeTimer = 0; return; }              // в самолёте — высоко над землёй, это нормально
            var pos = Player.transform.position;
            var t = Terrain.activeTerrain;
            float ground = t ? t.SampleHeight(pos) + t.transform.position.y : float.MinValue;
            bool under = (t && pos.y < ground - 3f) || pos.y < -100f;
            if (under)
            {
                var back = _lastSafe != Vector3.zero ? _lastSafe : GroundAt(pos) + Vector3.up * 0.2f;
                Player.Teleport(back + Vector3.up * 0.2f, Player.transform.eulerAngles.y);
                Debug.LogWarning("[Frontline] Игрок оказался под картой — возвращён на поверхность.");
                return;
            }
            _safeTimer += Time.deltaTime;
            if (_safeTimer > 0.5f && Player.IsGrounded) { _safeTimer = 0; _lastSafe = pos; }
        }

        float LookYawTowardsFront(float3 pos)
        {
            // Смотрим на центр карты (среднее точек захвата).
            float3 c = float3.zero; var cps = WorldStores.CapturePoints;
            for (int i = 0; i < cps.Length; i++) c += cps[i].position;
            if (cps.Length > 0) c /= cps.Length;
            float3 d = c - pos;
            return math.degrees(math.atan2(d.x, d.z));
        }

        void OnPlayerDied(int killer)
        {
            PlayerDeaths++;
            Player.InputEnabled = false;
            PlayerRespawnAt = Time.time + playerRespawnDelay;
            StartCoroutine(OpenDeploy());
        }

        IEnumerator OpenDeploy()
        {
            yield return new WaitForSeconds(playerRespawnDelay);
            if (Mode.Over) yield break;
            AwaitingDeploy = true;
            AutoDeployAt = Time.time + 25f;
            GameInput.SetEnabled(false);       // курсор свободен — выбор мышью
        }

        void OnKill(KillEvent k)
        {
            Mode.OnKill(k);
            if (k.killerActorId == CombatWorld.PlayerActorId && k.victimActorId != 0) PlayerKills++;
            KillFeed?.Invoke(k);
        }

        void Update()
        {
            FallGuard();
            if (AwaitingDeploy && Time.time >= AutoDeployAt) Deploy(Config.playerClass, Config.useAltPrimary, DeploySpawn);
            if (Mode == null || _ended || !Started) return;
            if (!Net.NetSession.IsClient) Mode.Tick(Time.deltaTime);   // у клиента время и счёт приходят с хоста
            if (Mode.Over) EndMatch();
        }

        void EndMatch()
        {
            _ended = true;
            MatchBridge.MatchRunning = false;
            GameInput.SetEnabled(false);

            // Награды — только локальная валюта, без микротранзакций.
            var p = SaveSystem.Progress;
            bool won = Mode.Winner == Config.playerFaction;
            int reward = PlayerKills * 25 + PlayerTankKills * 100 + (won ? 300 : 100) + Mathf.RoundToInt(Mode.Score[(int)Config.playerFaction] * 0.1f);
            p.currency += reward;
            p.totalKills += PlayerKills; p.matchesPlayed++; if (won) p.matchesWon++;
            Achievements.Evaluate(p, PlayerKills, won, Config);
            SaveSystem.SaveProgress();
            MatchEnded?.Invoke(Mode);
        }

        public void QuitToMenu()
        {
            MatchBridge.MatchRunning = false;
            if (GameFlow.I) GameFlow.I.ReturnToMenu();
            else SceneManager.LoadScene("MainMenu");
        }
    }

    /// <summary>Локальные достижения — часть обвесов открывается ими, а не валютой.</summary>
    public static class Achievements
    {
        public static void Evaluate(ProgressData p, int kills, bool won, MatchConfig cfg)
        {
            void Give(string id) { if (!p.achievementIds.Contains(id)) p.achievementIds.Add(id); }
            if (kills >= 1) Give("first_blood");
            if (kills >= 25) Give("kills_25_match");
            if (p.totalKills >= 500) Give("veteran_500");
            if (won && cfg.difficulty == BotDifficulty.Realistic) Give("realistic_win");
            if (p.bricksPlaced >= 200) Give("builder_200");
            if (p.matchesWon >= 10) Give("wins_10");
        }
    }
}
