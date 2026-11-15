using System.Collections.Generic;
using Frontline.Bots;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Frontline
{
    /// <summary>
    /// Игровой HUD в стиле Polyfield (IMGUI, без префабов): счёт убийств команд и таймер, буквы точек с прогрессом,
    /// мини-карта (своя пехота, танки, точки), лента убийств с именами, здоровье, гранаты, оружие и патроны, слоты,
    /// динамический прицел, маркер попадания и убийства, индикаторы направления урона, прогресс захвата,
    /// подсказки действий, HUD танка (броня, скорость, перезарядка, два маркера, прицел наводчика),
    /// экран высадки (точка + класс), таблица на Tab, пауза и итог матча.
    /// Все размеры заданы для 1920×1080 и масштабируются по высоте экрана.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        GameModeManager _gm;
        bool _diag;
        PlayerController _pc; WeaponController _wc; TrowelTool _trowel; PlayerHealth _hp; GrenadeThrower _gt;

        struct Feed { public string a, b; public Color ca, cb; public bool head, tank; public float t; }
        readonly List<Feed> _feed = new();
        struct DmgMark { public Vector3 dir; public float t, amount; }
        readonly List<DmgMark> _dmg = new();
        float _hitT, _killT, _killMsgT; string _killMsg; bool _hitHead, _hitKill;
        bool _paused;
        public static GameHUD I { get; private set; }
        /// <summary>Пауза / экран высадки / конец матча — сенсорные кнопки в это время скрыты.</summary>
        public static bool BlocksTouch => I && (I._paused || !I._gm || I._gm.AwaitingDeploy || I._gm.Mode == null || I._gm.Mode.Over);

        /// <summary>Пауза с экранной кнопки (телефон).</summary>
        public static void TogglePause()
        {
            if (!I || !I._gm || I._gm.AwaitingDeploy || I._gm.Mode == null || I._gm.Mode.Over) return;
            I._paused = !I._paused; I._pauseTab = -1;
            GameInput.SetEnabled(!I._paused);
        }

        void OnEnable() => I = this;
        float _fps;
        int _deployClass = -1; bool _deployAlt; int _deploySpawn = -1;

        Texture2D _white, _circle, _ring, _arrow, _arc, _map, _scopeMask;
        GUIStyle _sBig, _sHuge, _sMid, _sSmall, _sTiny, _sCenter, _sRight, _sBtn, _sBtnSel, _sTitle,
                 _sMidR, _sBigR, _sTimer, _sL18, _sL24, _sL26, _sTinyC, _sTiny12C, _sBig44, _sBig52, _sMidC, _sKill, _sTinyWrap, _sTinyWrapMid, _sSmallC;
        float _scale, W, H;

        static readonly Color Friend = new(0.35f, 0.65f, 1f), Enemy = new(0.95f, 0.32f, 0.26f), Neutral = new(0.85f, 0.85f, 0.85f),
            Panel = new(0.05f, 0.06f, 0.05f, 0.62f), Gold = new(0.95f, 0.8f, 0.4f);

        byte MyTeam => _gm && _gm.Config != null ? (byte)_gm.Config.playerFaction : (byte)0;
        Color TeamCol(byte team) => team == 255 ? Neutral : team == MyTeam ? Friend : Enemy;

        // ======================= Жизненный цикл =======================

        void Start()
        {
            useGUILayout = false;          // HUD не использует GUILayout — пропускаем лишний проход Layout (заметно на телефонах)
            _gm = GameModeManager.I;
            _gm.KillFeed += OnKill;
            _gm.VehicleDestroyed += OnTankKilled;
            HitFeedback.Hit += OnAnyHit;
            HitFeedback.Ricocheted += OnRicochet;
            Aircraft.Destroyed += OnPlaneKilled;
            ArtilleryGun.Destroyed += OnGunKilled;
            BuildTextures();
            BuildMinimap();
        }

        void OnDestroy()
        {
            if (_gm) { _gm.KillFeed -= OnKill; _gm.VehicleDestroyed -= OnTankKilled; }
            Aircraft.Destroyed -= OnPlaneKilled;
            HitFeedback.Hit -= OnAnyHit;
            HitFeedback.Ricocheted -= OnRicochet;
            ArtilleryGun.Destroyed -= OnGunKilled;
            foreach (var t in new[] { _white, _circle, _ring, _arrow, _arc, _map }) if (t) Destroy(t);
        }

        void Bind()
        {
            if (_wc || !_gm || !_gm.Player) return;
            _pc = _gm.Player;
            _wc = _pc.GetComponent<WeaponController>();
            _trowel = _pc.GetComponent<TrowelTool>();
            _hp = _pc.GetComponent<PlayerHealth>();
            _gt = _pc.GetComponent<GrenadeThrower>();
            _wc.HitConfirmed += h => { _hitT = 0.18f; _hitHead = h.headshot; };
            _hp.Damaged += (dir, amount) => { _dmg.Add(new DmgMark { dir = dir, t = 1.6f, amount = amount }); if (_dmg.Count > 8) _dmg.RemoveAt(0); };
        }

        void OnKill(KillEvent k)
        {
            byte kt = TeamOf(k.killerActorId, (byte)(1 - k.victimTeam));
            _feed.Insert(0, new Feed
            {
                a = k.killerActorId == k.victimActorId ? "" : BotNames.Get(k.killerActorId, kt), b = BotNames.Get(k.victimActorId, k.victimTeam),
                ca = TeamCol(kt), cb = TeamCol(k.victimTeam), head = k.headshot, t = Time.time
            });
            if (_feed.Count > 7) _feed.RemoveAt(_feed.Count - 1);
            // «+1» у счёта команды, получившей убийство
            if (k.killerActorId != k.victimActorId && kt != k.victimTeam) _plus[kt] = Mathf.Min(_plus[kt] + 1, 99);
            if (_plus[kt] > 0) _plusT[kt] = 1.6f;
            if (k.victimActorId == 0 && k.killerActorId != 0)
            {
                byte kt2 = TeamOf(k.killerActorId, (byte)(1 - k.victimTeam));
                string veh = VehicleOf(k.killerActorId);
                _killedBy = BotNames.Get(k.killerActorId, kt2) + (veh != null ? "  ·  " + veh : "") + (k.headshot ? "  ·  в голову" : "");
            }
            else if (k.victimActorId == 0) _killedBy = null;
            if (k.killerActorId == CombatWorld.PlayerActorId && k.victimActorId != 0)
            {
                _myKills.Insert(0, new MyKill { victim = BotNames.Get(k.victimActorId, k.victimTeam), how = PlayerWeaponName() + (k.headshot ? " · в голову" : ""), t = Time.time });
                if (_myKills.Count > 6) _myKills.RemoveAt(_myKills.Count - 1);
                _hitT = 0.3f; _hitKill = true; _killT = 0.45f;
                _streak = Time.time - _lastKillTime < 6f ? _streak + 1 : 1; _lastKillTime = Time.time;
                _killMsg = (k.headshot ? "В ГОЛОВУ  " : "УБИТ  ") + BotNames.Get(k.victimActorId, k.victimTeam); _killMsgT = 2.6f;
                PlayKillSound(k.headshot);
            }
        }

        void OnTankKilled(Tank t, int killer)
        {
            byte kt = TeamOf(killer, (byte)(1 - t.Team));
            _feed.Insert(0, new Feed { a = BotNames.Get(killer, kt), b = t.DisplayName, ca = TeamCol(kt), cb = TeamCol(t.Team), tank = true, t = Time.time });
            if (_feed.Count > 7) _feed.RemoveAt(_feed.Count - 1);
            if (killer == CombatWorld.PlayerActorId && t.Team != MyTeam) { _killMsg = "ТЕХНИКА УНИЧТОЖЕНА  " + t.DisplayName; _killMsgT = 3f; _killT = 0.5f; _streak = 1; PlayKillSound(false); }
            if (t.Team <= 1) { _plus[1 - t.Team]++; _plusT[1 - t.Team] = 1.6f; }
        }

        struct MyKill { public string victim, how; public float t; }
        readonly List<MyKill> _myKills = new();
        string _killedBy;

        /// <summary>В какой технике сидит актор (для «Вас убил …»): танк, джип, самолёт или орудие.</summary>
        static string VehicleOf(int actor)
        {
            foreach (var t in Tank.All)
                if (t && (t.DriverActorId == actor || t.gunnerBot == actor || t.passengerBot == actor)) return t.DisplayName;
            foreach (var a in Aircraft.All) if (a && a.PilotActorId == actor) return a.DisplayName;
            foreach (var g in ArtilleryGun.All) if (g && g.CrewActorId == actor) return g.DisplayName;
            return null;
        }

        string PlayerWeaponName()
        {
            if (!_pc) return "";
            if (_pc.Flying) return _pc.Flying.DisplayName;
            if (_pc.Gunning) return _pc.Gunning.DisplayName;
            if (_pc.Vehicle && !_pc.Passenger) return _pc.Vehicle.DisplayName;
            return _wc && _wc.Current ? _wc.Current.displayName : "";
        }

        /// <summary>Свои убийства — отдельная вкладка слева под мини-картой.</summary>
        void DrawMyKills()
        {
            float x = W - 346, y = 230;          // справа, под лентой убийств
            int shown = 0;
            foreach (var k in _myKills) if (Time.time - k.t < 30f) shown++;
            if (shown == 0) return;
            Box(new Rect(x - 4, y - 4, 330, 28 + shown * 24), new Color(0, 0, 0, 0.45f));
            Text(new Rect(x + 4, y, 320, 22), $"МОИ УБИЙСТВА  ·  {_gm.PlayerKills}", _sTiny, Gold);
            y += 24;
            foreach (var k in _myKills)
            {
                float age = Time.time - k.t;
                if (age >= 30f) continue;
                float a = Mathf.Clamp01((30f - age) * 0.5f);
                Text(new Rect(x + 4, y, 160, 22), k.victim, _sTiny, new Color(Enemy.r, Enemy.g, Enemy.b, a));
                Text(new Rect(x + 168, y, 160, 22), k.how, _sTiny, new Color(1, 1, 1, 0.75f * a));
                y += 24;
            }
        }

        void OnAnyHit(bool kill) { _hitT = Mathf.Max(_hitT, kill ? 0.3f : 0.18f); if (kill) _hitKill = true; }
        void OnRicochet() => _ricoT = 1.2f;

        void OnGunKilled(ArtilleryGun g, int killer)
        {
            byte kt = TeamOf(killer, (byte)(1 - g.Team));
            _feed.Insert(0, new Feed { a = killer < 0 ? "—" : BotNames.Get(killer, kt), b = g.DisplayName, ca = TeamCol(kt), cb = TeamCol(g.Team), tank = true, t = Time.time });
            if (_feed.Count > 7) _feed.RemoveAt(_feed.Count - 1);
            if (killer == CombatWorld.PlayerActorId && g.Team != MyTeam) { _killMsg = "ОРУДИЕ УНИЧТОЖЕНО  " + g.DisplayName; _killMsgT = 3f; _killT = 0.5f; _streak = 1; PlayKillSound(false); }
            if (g.Team <= 1) { _plus[1 - g.Team]++; _plusT[1 - g.Team] = 1.6f; }
        }

        void OnPlaneKilled(Aircraft a, int killer)
        {
            byte kt = TeamOf(killer, (byte)(1 - a.Team));
            _feed.Insert(0, new Feed { a = killer < 0 ? "Крушение" : BotNames.Get(killer, kt), b = a.DisplayName, ca = TeamCol(kt), cb = TeamCol(a.Team), tank = true, t = Time.time });
            if (_feed.Count > 7) _feed.RemoveAt(_feed.Count - 1);
            if (killer == CombatWorld.PlayerActorId && a.Team != MyTeam) { _killMsg = "САМОЛЁТ СБИТ  " + a.DisplayName; _killMsgT = 3f; _killT = 0.5f; _streak = 1; PlayKillSound(false); }
            if (a.Team <= 1) { _plus[1 - a.Team]++; _plusT[1 - a.Team] = 1.6f; }
        }

        readonly int[] _plus = new int[2]; readonly float[] _plusT = new float[2];
        int _streak; float _lastKillTime = -99f;
        AudioSource _uiSrc;

        void PlayKillSound(bool head)
        {
            if (!_uiSrc) { _uiSrc = gameObject.AddComponent<AudioSource>(); _uiSrc.spatialBlend = 0; _uiSrc.playOnAwake = false; }
            _uiSrc.pitch = 1f + Mathf.Min(_streak - 1, 4) * 0.06f;      // серия — звук чуть выше
            _uiSrc.PlayOneShot(SoundSynth.KillConfirm(), 0.9f);
            if (head) _uiSrc.PlayOneShot(SoundSynth.Headshot(), 0.6f);
        }

        static byte TeamOf(int actor, byte fallback)
        {
            if (actor >= 0 && CombatWorld.Initialized && actor < CombatWorld.Actors.Length) return CombatWorld.Actors[actor].team;
            if (actor <= -10) foreach (var t in Tank.All) if (t && t.DriverActorId == actor) return t.Team;
            return fallback;
        }

        void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            Bind();
            MiniView.Tick();
            Spotting.Tick(MyTeam);
            // Сердцебиение тяжело раненого
            if (_hp && _hp.Downed && Time.time >= _beatAt)
            {
                _beatAt = Time.time + 0.62f;
                if (!_uiSrc) { _uiSrc = gameObject.AddComponent<AudioSource>(); _uiSrc.spatialBlend = 0; _uiSrc.playOnAwake = false; }
                _uiSrc.pitch = 1f; _uiSrc.PlayOneShot(SoundSynth.Heartbeat(), 1f);
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && _gm && !_gm.AwaitingDeploy && _gm.Mode != null && !_gm.Mode.Over)
            {
                if (_paused && _pauseTab >= 0) _pauseTab = -1;          // из настроек — назад в меню паузы
                else
                {
                    _paused = !_paused;
                    GameInput.SetEnabled(!_paused);
                }
            }
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) _diag = !_diag;
            float dt = Time.deltaTime;
            _hitT -= dt; _killT -= dt; _killMsgT -= dt; _ricoT -= dt;
            for (int tm = 0; tm < 2; tm++) if (_plusT[tm] > 0) { _plusT[tm] -= dt; if (_plusT[tm] <= 0) _plus[tm] = 0; }
            if (_hitT <= 0) _hitKill = false;
            for (int i = _dmg.Count - 1; i >= 0; i--) { var d = _dmg[i]; d.t -= dt; if (d.t <= 0) _dmg.RemoveAt(i); else _dmg[i] = d; }
            if (_gm && _gm.AwaitingDeploy && _deployClass < 0) { _deployClass = (int)_gm.Config.playerClass; _deployAlt = _gm.Config.useAltPrimary; _deploySpawn = _gm.DeploySpawn; }
            if (_gm && !_gm.AwaitingDeploy) _deployClass = -1;
        }

        // ======================= Текстуры =======================

        static Texture2D Tex(int w, int h, System.Func<float, float, float> alpha)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h)) * 255));
            t.SetPixels32(px); t.Apply();
            return t;
        }

        void BuildTextures()
        {
            _white = Tex(2, 2, (x, y) => 1);
            _circle = Tex(64, 64, (x, y) => { float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2; return (1 - d) * 12f; });
            _scopeMask = Tex(256, 256, (x, y) => { float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2; return (d - 0.955f) * 40f; });   // прозрачный круг, вокруг — сплошная чернота
            _ring = Tex(64, 64, (x, y) => { float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2; return (0.12f - Mathf.Abs(d - 0.86f)) * 30f; });
            _arrow = Tex(32, 32, (x, y) => { float w = (1 - y) * 0.5f; return (Mathf.Abs(x - 0.5f) < w * (y > 0.15f ? 1f : 0) ? 1f : 0f) * (y < 0.95f ? 1 : 0); });
            _arc = Tex(128, 48, (x, y) =>
            {
                float px = x - 0.5f, py = y * 0.375f + 1.2f;          // дуга окружности с центром ниже текстуры
                float r = Mathf.Sqrt(px * px + py * py);
                float band = 1f - Mathf.Abs(r - 1.45f) / 0.05f;
                return Mathf.Clamp01(band) * Mathf.Clamp01(1.2f - Mathf.Abs(px) * 2.4f);
            });
        }

        /// <summary>Мини-карта боевой зоны: рельеф с отмывкой, дороги, река, леса, постройки и изгороди (лучами сверху).</summary>
        void BuildMinimap()
        {
            const int N = 384;
            var terr = Terrain.activeTerrain;
            bool winter = _gm.Config.map && _gm.Config.map.theme == MapTheme.Winter;
            float Z = MapGenerator.Zone;
            _map = new Texture2D(N, N, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[N * N];
            Color field = winter ? new Color(0.86f, 0.88f, 0.9f) : new Color(0.42f, 0.5f, 0.3f);
            Color road = winter ? new Color(0.62f, 0.6f, 0.58f) : new Color(0.66f, 0.58f, 0.44f);
            Color water = winter ? new Color(0.65f, 0.75f, 0.82f) : new Color(0.26f, 0.42f, 0.55f);
            Color house = new(0.55f, 0.48f, 0.44f), hedge = winter ? new Color(0.52f, 0.58f, 0.55f) : new Color(0.2f, 0.32f, 0.16f);
            Color forest = winter ? new Color(0.45f, 0.52f, 0.5f) : new Color(0.24f, 0.34f, 0.2f);
            var td = terr ? terr.terrainData : null;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float wx = -Z + (x + 0.5f) / N * Z * 2, wz = -Z + (y + 0.5f) / N * Z * 2;
                    Color c = field;
                    if (td)
                    {
                        float u = (wx - terr.transform.position.x) / td.size.x, v = (wz - terr.transform.position.z) / td.size.z;
                        Vector3 n = td.GetInterpolatedNormal(u, v);
                        float shade = Mathf.Clamp(0.75f + Vector3.Dot(n, new Vector3(-0.5f, 0.75f, 0.45f).normalized) * 0.35f, 0.55f, 1.15f);
                        float h = td.GetInterpolatedHeight(u, v);
                        c = field * shade * (0.92f + Mathf.Clamp01(h / 120f) * 0.15f);
                    }
                    if (Mathf.Abs(wz - MapGenerator.RiverZ(wx)) < 8f) c = water;
                    else if (MapGenerator.RoadDist(new Vector2(wx, wz)) < 4f) c = road;
                    if (Physics.Raycast(new Vector3(wx, 500f, wz), Vector3.down, out var hit, 1000f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore) && !(hit.collider is TerrainCollider))
                    {
                        var nm = hit.collider.name;
                        if (nm.StartsWith("Hedge")) c = hedge;
                        else if (!nm.StartsWith("Fence")) c = house * (0.9f + (hit.point.y % 3f) * 0.05f);
                    }
                    c.a = 1; px[y * N + x] = c;
                }
            if (td)
                foreach (var t in td.treeInstances)
                {
                    float wx = t.position.x * td.size.x + terr.transform.position.x, wz = t.position.z * td.size.z + terr.transform.position.z;
                    int x = Mathf.FloorToInt((wx + Z) / (Z * 2) * N), y = Mathf.FloorToInt((wz + Z) / (Z * 2) * N);
                    if (x < 0 || y < 0 || x >= N || y >= N) continue;
                    px[y * N + x] = forest;
                }
            _map.SetPixels(px); _map.Apply();
            _mapPx = _map.GetPixels32(); _mapN = N;
        }

        // ---------- Круглая мини-карта с масштабами (клавиша M) ----------
        // 6 масштабов: сильно приближённая, затем 5 отдалений; после пятого — снова приближённая.
        static readonly float[] MiniRanges = { 70f, 170f, 260f, 380f, 520f, 700f };
        int _miniLevel = 1;
        Color32[] _mapPx; int _mapN;
        Texture2D _mini; Color32[] _miniPx;
        float _miniT; Vector3 _miniPos; int _miniBuiltLevel = -1;
        const int MiniRes = 160;

        /// <summary>Круглая подложка мини-карты: собирается на CPU 8 раз в секунду (дешевле, чем сотни draw call'ов маски).</summary>
        void UpdateMiniTexture(Vector3 pp, float range)
        {
            if (_mapPx == null) return;
            if (!_mini) { _mini = new Texture2D(MiniRes, MiniRes, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear }; _miniPx = new Color32[MiniRes * MiniRes]; }
            if ((_miniT -= Time.unscaledDeltaTime) > 0 && _miniBuiltLevel == _miniLevel && (pp - _miniPos).sqrMagnitude < 4f) return;
            _miniT = 0.12f; _miniPos = pp; _miniBuiltLevel = _miniLevel;
            float Z = MapGenerator.Zone, half = MiniRes * 0.5f;
            var outside = new Color32(38, 44, 32, 255);
            for (int y = 0; y < MiniRes; y++)
                for (int x = 0; x < MiniRes; x++)
                {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float d2 = dx * dx + dy * dy;
                    int i = y * MiniRes + x;
                    if (d2 > 1f) { _miniPx[i] = new Color32(0, 0, 0, 0); continue; }
                    float wx = pp.x + dx * range, wz = pp.z + dy * range;
                    Color32 c;
                    if (wx < -Z || wx > Z || wz < -Z || wz > Z) c = outside;
                    else
                    {
                        int mx = Mathf.Clamp((int)((wx + Z) / (2 * Z) * _mapN), 0, _mapN - 1), my = Mathf.Clamp((int)((wz + Z) / (2 * Z) * _mapN), 0, _mapN - 1);
                        c = _mapPx[my * _mapN + mx];
                    }
                    if (d2 > 0.93f) c = new Color32(20, 20, 18, 255);       // ободок
                    _miniPx[i] = c;
                }
            _mini.SetPixels32(_miniPx); _mini.Apply(false);
        }

        // ======================= Примитивы =======================

        void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }
        void Tex(Rect r, Texture t, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, t); GUI.color = o; }

        void Text(Rect r, string s, GUIStyle st, Color c, bool shadow = true)
        {
            if (string.IsNullOrEmpty(s)) return;
            s = Loc.T(s);
            var o = GUI.color;
            if (shadow) { GUI.color = new Color(0, 0, 0, 0.7f * c.a); GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), s, st); }
            GUI.color = c; GUI.Label(r, s, st); GUI.color = o;
        }

        void Line(Vector2 a, Vector2 b, float w, Color c)
        {
            var m = GUI.matrix;
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float len = Vector2.Distance(a, b);
            GUIUtility.RotateAroundPivot(ang, a * _scale);
            Box(new Rect(a.x, a.y - w * 0.5f, len, w), c);
            GUI.matrix = m;
        }

        void Bar(Rect r, float v, Color fill, Color back)
        {
            Box(r, back);
            Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), fill);
        }

        void Styles()
        {
            if (_sBig != null) return;
            GUIStyle S(int size, FontStyle fs = FontStyle.Normal, TextAnchor a = TextAnchor.UpperLeft) =>
                new(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a, richText = true, wordWrap = false, clipping = TextClipping.Overflow };
            _sHuge = S(64, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sTitle = S(40, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sBig = S(34, FontStyle.Bold);
            _sMid = S(22, FontStyle.Bold);
            _sSmall = S(18);
            _sTiny = S(14);
            _sCenter = S(22, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sRight = S(18, FontStyle.Normal, TextAnchor.UpperRight);
            _sMidR = S(22, FontStyle.Bold, TextAnchor.UpperRight);
            _sBigR = S(34, FontStyle.Bold, TextAnchor.UpperRight);
            _sTimer = S(30, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sL18 = S(18, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sL24 = S(24, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sL26 = S(26, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sTinyC = S(14, FontStyle.Normal, TextAnchor.UpperCenter);
            _sTiny12C = S(12, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sBig44 = S(44, FontStyle.Bold);
            _sBig52 = S(52, FontStyle.Bold);
            _sMidC = S(22, FontStyle.Bold, TextAnchor.UpperCenter);
            _sKill = S(24, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sTinyWrap = S(14); _sTinyWrap.wordWrap = true;
            _sTinyWrapMid = S(20, FontStyle.Normal, TextAnchor.MiddleLeft);
            _sSmallC = S(18, FontStyle.Normal, TextAnchor.UpperCenter);
            _sBtn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            _sBtn.normal.background = _white; _sBtn.hover.background = _white; _sBtn.active.background = _white;
            _sBtn.normal.textColor = Color.white; _sBtn.hover.textColor = Gold; _sBtn.active.textColor = Gold;
            _sBtnSel = new GUIStyle(_sBtn); _sBtnSel.normal.textColor = Gold;
        }

        bool Button(Rect r, string s, bool selected = false, bool enabled = true)
        {
            var o = GUI.color; var ob = GUI.backgroundColor;
            bool hover = r.Contains(Event.current.mousePosition);
            GUI.backgroundColor = !enabled ? new Color(0.2f, 0.2f, 0.2f, 0.7f) : selected ? new Color(0.45f, 0.38f, 0.18f, 0.95f) : hover ? new Color(0.28f, 0.3f, 0.26f, 0.95f) : new Color(0.14f, 0.15f, 0.13f, 0.9f);
            GUI.enabled = enabled;
            bool clicked = GUI.Button(r, Loc.T(s), selected ? _sBtnSel : _sBtn);
            GUI.enabled = true;
            GUI.color = o; GUI.backgroundColor = ob;
            if (clicked) { var src = _pc ? _pc.GetComponent<AudioSource>() : null; if (src) src.PlayOneShot(SoundSynth.UiClick(), 0.6f); }
            return clicked;
        }

        // ======================= Отрисовка =======================

        void OnGUI()
        {
            if (!_gm || _gm.Mode == null) return;
            Styles();
            _scale = Screen.height / 1080f;
            W = Screen.width / _scale; H = 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1));

            var m = _gm.Mode;
            bool inPlane = _pc && _pc.Flying;
            bool inGun = _pc && _pc.Gunning;
            bool inTank = (_pc && _pc.Vehicle && !_pc.Passenger) || inPlane || inGun;   // пассажир джипа воюет как пехотинец
            bool alive = _hp && _hp.Alive;

            if (!PostFx.Available && SaveSystem.Settings.vignette > 0.01f) DrawVignette(alive && !inTank);
            if (!m.Over && !_gm.AwaitingDeploy)
            {
                if (alive && !inTank) DrawScope();
                if (alive && !inTank) DrawCrosshair();
                if (inPlane) DrawPlaneHud(_pc.Flying);
                else if (inGun) DrawArtilleryHud(_pc.Gunning);
                else if (inTank) DrawTankHud(_pc.Vehicle);
                DrawDamageMarks();
                if (alive) DrawHitMarker();
                if (alive && _hp.Downed)
                {
                    // Тяжело ранен: кроваво-красные края экрана пульсируют в такт сердцу
                    float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.time * 3.2f)), 6f);
                    Color rc = new(0.55f, 0f, 0f, 0.35f + beat * 0.35f);
                    float e = 90f + beat * 30f;
                    for (int k = 0; k < 6; k++)
                    {
                        float ee = e * (1f - k / 6f); Color ck = new(rc.r, rc.g, rc.b, rc.a * (1f - k / 6f) * 0.5f);
                        Box(new Rect(0, 0, W, ee), ck); Box(new Rect(0, H - ee, W, ee), ck);
                        Box(new Rect(0, 0, ee, H), ck); Box(new Rect(W - ee, 0, ee, H), ck);
                    }
                    Text(new Rect(W / 2 - 400, H * 0.7f, 800, 34), "ТЯЖЕЛО РАНЕН — ползите в укрытие, ждите аптечку штурмовика", _sCenter, new Color(1f, 0.6f, 0.55f));
                }
                if (alive && _pc && _pc.CarryingShell)
                {
                    Box(new Rect(W / 2 - 260, H - 150, 520, 44), new Color(0, 0, 0, 0.6f));
                    Text(new Rect(W / 2 - 250, H - 144, 500, 34), "СНАРЯД В РУКАХ — отнесите к орудию и нажмите [R]", _sCenter, Gold);
                }
                DrawTickets(m);
                DrawMinimap();
                if (SaveSystem.Settings.bodycam && !inTank) DrawBodycam();
                DrawFeed();
                if (alive && !inTank) { DrawHealth(); DrawWeapon(); }
                else if (alive && inGun) DrawHealth();          // у орудия расчёт открыт — здоровье важно
                DrawCapture();
                DrawPrompts();
                if (!alive) Text(new Rect(W / 2 - 300, H * 0.62f, 600, 40), $"Высадка через {Mathf.Max(0, Mathf.CeilToInt(_gm.PlayerRespawnAt - Time.time))}", _sCenter, Color.white);
                DrawMyKills();
                if (Keyboard.current != null && Keyboard.current.tabKey.isPressed) DrawScoreboard(m);
            }
            Text(new Rect(W - 210, H - 26, 200, 24), $"{_fps:0} FPS · ботов {BotRegistry.BotCount}", _sRight, new Color(1, 1, 1, 0.5f), false);
            if (_diag && _pc)
            {
                var vma = _pc.GetComponent<ViewModelAnimator>();
                string d = vma ? vma.Diagnostics() : "ViewModelAnimator не найден";
                GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(10, 120, 1060, 150), Texture2D.whiteTexture); GUI.color = Color.white;
                Text(new Rect(20, 126, 1040, 140), "F3 · диагностика вида от первого лица\n" + d, _sTinyWrap, Color.yellow, false);
            }

            if (_hp && !_hp.Alive && !_gm.AwaitingDeploy && !m.Over)
            {
                DrawEyelids(Time.time - _hp.DeathTime);
                Text(new Rect(W / 2 - 300, H * 0.62f, 600, 40), $"Высадка через {Mathf.Max(0, Mathf.CeilToInt(_gm.PlayerRespawnAt - Time.time))}", _sCenter, new Color(1, 1, 1, 0.8f));
                // Кто убил — поверх закрытых глаз, первые 5 секунд после гибели
                float sinceDeath = Time.time - _hp.DeathTime;
                if (!string.IsNullOrEmpty(_killedBy) && sinceDeath < 5f)
                {
                    float ka = Mathf.Clamp01((5f - sinceDeath) * 2f);
                    Box(new Rect(W / 2 - 380, H * 0.44f - 8, 760, 56), new Color(0, 0, 0, 0.65f * ka));
                    Text(new Rect(W / 2 - 370, H * 0.44f, 740, 40), "ВАС УБИЛ:  " + _killedBy, _sCenter, new Color(Enemy.r, Enemy.g, Enemy.b, ka));
                }
            }
            if (_gm.AwaitingDeploy && !m.Over) DrawDeploy();
            if (_paused && !m.Over) DrawPause();
            if (m.Over) DrawEnd(m);
        }

        // ---------- Прицел и маркеры ----------

        void DrawCrosshair()
        {
            Vector2 c = new(W / 2, H / 2);
            if (_wc && _wc.Ads01 < 0.6f)
            {
                float a = 1f - _wc.Ads01 / 0.6f;
                // Зазор прицела = настоящий конус разброса пуль на экране (раньше — «на глаз» от скорости):
                // пули ложатся внутри креста, у пулемёта и от бедра крест честно шире
                float vfov = _pc && _pc.worldCamera ? _pc.worldCamera.fieldOfView : 70f;
                float gap = Mathf.Tan(_wc.SpreadDeg * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad) * (H * 0.5f);
                gap = Mathf.Clamp(gap, 4f, H * 0.25f);
                Color col = new(1, 1, 1, 0.85f * a);
                if (_wc.Current && _wc.Current.category == WeaponCategory.Knife) { Tex(new Rect(c.x - 3, c.y - 3, 6, 6), _circle, col); }
                else
                {
                    Box(new Rect(c.x - 1, c.y - gap - 9, 2, 9), col); Box(new Rect(c.x - 1, c.y + gap, 2, 9), col);
                    Box(new Rect(c.x - gap - 9, c.y - 1, 9, 2), col); Box(new Rect(c.x + gap, c.y - 1, 9, 2), col);
                    Box(new Rect(c.x - 1, c.y - 1, 2, 2), col);
                }
            }
            if (_killMsgT > 0)
            {
                // Плашка убийства сверху, под счётом: «УБИТ  Имя», серия — «×2», «×3»…
                float a = Mathf.Clamp01(_killMsgT * 2f);
                float pop = 1f + Mathf.Max(0, _killT) * 0.6f;
                float y = 150f - Mathf.Max(0, _killT) * 30f;
                Box(new Rect(W / 2 - 330, y - 4, 660, 52), new Color(0.05f, 0.05f, 0.04f, 0.72f * a));
                Box(new Rect(W / 2 - 330, y - 4, 660, 3), new Color(1f, 0.82f, 0.4f, a));
                _sKill.fontSize = Mathf.RoundToInt(26 * pop);
                string msg = _streak > 1 ? $"{_killMsg}   ×{_streak}" : _killMsg;
                Text(new Rect(W / 2 - 400, y + 2, 800, 40), msg, _sKill, new Color(1f, 0.85f, 0.5f, a));
            }
        }

        void DrawDamageMarks()
        {
            if (!_pc) return;
            var cam = _pc.worldCamera.transform;
            Vector2 c = new(W / 2, H / 2);
            foreach (var d in _dmg)
            {
                Vector3 local = cam.InverseTransformDirection(d.dir);
                float ang = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                var mtx = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang, c * _scale);
                float a = Mathf.Clamp01(d.t) * Mathf.Clamp(d.amount / 25f, 0.5f, 1f);
                Tex(new Rect(c.x - 130, c.y - 230, 260, 90), _arc, new Color(0.9f, 0.1f, 0.05f, a));
                GUI.matrix = mtx;
            }
        }

        // ---------- Флаги, точки, таймер ----------

        void DrawTickets(GameMode m)
        {
            float cx = W / 2, top = 14;
            int my = MyTeam, en = 1 - my;
            int sec = Mathf.Max(0, Mathf.CeilToInt(m.TimeLeft));
            float bw = 300, bh = 26;
            // Полосы — доля убийств каждой стороны (кто впереди — у того полоса длиннее)
            float killsTotal = Mathf.Max(1, m.Score[0] + m.Score[1]);
            // Своя команда слева, противник справа
            Box(new Rect(cx - 70 - bw - 6, top - 4, bw + 12, bh + 8), Panel);
            Box(new Rect(cx + 70 - 6, top - 4, bw + 12, bh + 8), Panel);
            float f0 = m.Score[my] / killsTotal, f1 = m.Score[en] / killsTotal;
            Bar(new Rect(cx - 70 - bw, top, bw, bh), f0, Friend * 0.9f, new Color(0, 0, 0, 0.4f));
            // противник — заполняется от центра
            Box(new Rect(cx + 70, top, bw, bh), new Color(0, 0, 0, 0.4f));
            Box(new Rect(cx + 70, top, bw * Mathf.Clamp01(f1), bh), Enemy * 0.9f);
            Text(new Rect(cx - 70 - bw + 8, top - 3, bw, bh), $"{(my == 0 ? "ГЕРМАНИЯ" : "СССР")}", _sMid, Color.white);
            Text(new Rect(cx + 70, top - 3, bw - 8, bh), $"{(en == 0 ? "ГЕРМАНИЯ" : "СССР")}", _sMidR, Color.white);
            Text(new Rect(cx - 70 - 90, top + bh + 4, 80, 40), m.Score[my].ToString(), _sBigR, m.Score[my] >= m.Score[en] ? Gold : Friend);
            Text(new Rect(cx + 70 + 10, top + bh + 4, 80, 40), m.Score[en].ToString(), _sBig, Enemy);
            // Всплывающие «+N» у счёта после каждого убийства
            for (int tm = 0; tm < 2; tm++)
            {
                if (_plusT[tm] <= 0) continue;
                float pa = Mathf.Clamp01(_plusT[tm] * 1.5f), rise = (1.6f - _plusT[tm]) * 14f;
                bool mine = tm == my;
                var pr = mine ? new Rect(cx - 70 - 170, top + bh + 8 - rise, 70, 36) : new Rect(cx + 70 + 90, top + bh + 8 - rise, 70, 36);
                Text(pr, "+" + _plus[tm], mine ? _sBigR : _sBig, new Color(mine ? 0.6f : 1f, mine ? 0.85f : 0.5f, mine ? 1f : 0.4f, pa));
            }
            Box(new Rect(cx - 64, top - 4, 128, 44), Panel);
            Text(new Rect(cx - 64, top, 128, 36), $"{sec / 60:0}:{sec % 60:00}", _sTimer, sec < 60 ? Enemy : Color.white);
            Text(new Rect(cx - 150, top + 40, 300, 24), m.Describe().ToUpper(), _sTinyC, new Color(1, 1, 1, 0.7f));
            Text(new Rect(cx - 150, top + 58, 300, 22), "убийства · победа по времени", _sTinyC, new Color(1, 1, 1, 0.45f));

            if (!WorldStores.CapturePoints.IsCreated || _gm.Config.mode != GameModeType.TerritoryControl) return;
            var pts = WorldStores.CapturePoints;
            float pw = 46, gapX = 10, total = pts.Length * pw + (pts.Length - 1) * gapX;
            float x0 = cx - total / 2, y0 = top + 66;
            for (int i = 0; i < pts.Length; i++)
            {
                var p = pts[i];
                var r = new Rect(x0 + i * (pw + gapX), y0, pw, pw);
                Color col = TeamCol(p.owner);
                bool blink = p.contested != 0 && Mathf.Repeat(Time.time, 0.6f) < 0.3f;
                Tex(r, _circle, new Color(0, 0, 0, 0.55f));
                Tex(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), _circle, blink ? Color.white : col * new Color(1, 1, 1, 0.85f));
                Text(r, ((char)('A' + i)).ToString(), _sL24, p.owner == 255 ? new Color(0.15f, 0.15f, 0.15f) : Color.white, false);
                // Прогресс захвата: полоска под буквой в цвет стороны, к которой склоняется точка
                float prog = Mathf.Abs(p.progress);
                byte towards = p.progress > 0 ? (byte)1 : (byte)0;
                if (prog > 0.01f && prog < 0.999f) Bar(new Rect(r.x, r.yMax + 3, r.width, 5), prog, TeamCol(towards), new Color(0, 0, 0, 0.5f));
            }
        }

        // ---------- Мини-карта ----------

        void DrawMinimap()
        {
            if (!_map || !_pc) return;
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame && !_paused) _miniLevel = (_miniLevel + 1) % MiniRanges.Length;
            const float size = 260;
            float range = MiniRanges[_miniLevel];     // радиус обзора, м
            var r = new Rect(16, 16, size, size);
            Vector3 pp = _pc.transform.position;
            UpdateMiniTexture(pp, range);
            Tex(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), _circle, new Color(0, 0, 0, 0.35f));
            if (_mini) GUI.DrawTexture(r, _mini);
            Vector2 ctr = r.center;
            float R = size * 0.5f - 6f;
            Vector2 ToMap(Vector3 w) => ctr + new Vector2(w.x - pp.x, -(w.z - pp.z)) * (size / 2 / range);
            bool InCircle(Vector2 m) => (m - ctr).sqrMagnitude < R * R;

            GUI.BeginClip(r);
            Vector2 off = new(-r.x, -r.y);
            // Точки захвата
            if (WorldStores.CapturePoints.IsCreated && _gm.Config.mode == GameModeType.TerritoryControl)
                for (int i = 0; i < WorldStores.CapturePoints.Length; i++)
                {
                    var cp = WorldStores.CapturePoints[i];
                    if (!InCircle(ToMap(cp.position))) continue;
                    var mp = ToMap(cp.position) + off;
                    float rr = cp.radius * (size / 2 / range) + 8;
                    Tex(new Rect(mp.x - rr, mp.y - rr, rr * 2, rr * 2), _ring, TeamCol(cp.owner));
                    Text(new Rect(mp.x - 20, mp.y - 14, 40, 28), ((char)('A' + i)).ToString(), _sL18, TeamCol(cp.owner));
                }
            // Свои бойцы
            var actors = CombatWorld.Actors;
            for (int i = 1; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team != MyTeam) continue;
                if (Mathf.Abs(a.position.x - pp.x) > range || Mathf.Abs(a.position.z - pp.z) > range) continue;   // вне окна карты — не рисуем
                if (!InCircle(ToMap(a.position))) continue;
                var mp = ToMap(a.position) + off;
                Tex(new Rect(mp.x - 3.5f, mp.y - 3.5f, 7, 7), _circle, Friend);
            }
            // Замеченные своими бойцами враги (засветка)
            for (int i = 1; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team == MyTeam || !Spotting.Spotted(a.actorId)) continue;
                if (!InCircle(ToMap(a.position))) continue;
                var mp = ToMap(a.position) + off;
                Tex(new Rect(mp.x - 4f, mp.y - 4f, 8, 8), _circle, Enemy);
            }
            // Танки: свои всегда, вражеские — если близко или стреляли (живые в радиусе 180 м)
            foreach (var t in Tank.All)
            {
                if (!t) continue;
                bool mine = t.Team == MyTeam;
                if (!mine && Vector3.Distance(t.transform.position, pp) > 180f) continue;
                if (!InCircle(ToMap(t.transform.position))) continue;
                var mp = ToMap(t.transform.position) + off;
                Color tc = t.Alive ? (mine ? Friend : Enemy) : new Color(0.3f, 0.3f, 0.3f);
                Box(new Rect(mp.x - 8, mp.y - 8, 16, 16), new Color(0, 0, 0, 0.6f));
                Box(new Rect(mp.x - 6, mp.y - 6, 12, 12), tc);
                Box(new Rect(mp.x - 1, mp.y - 6, 2, 6), Color.black);
            }
            // Свои орудия — серый квадратик со стволом
            foreach (var g in ArtilleryGun.All)
            {
                if (!g || !g.Alive || g.Team != MyTeam) continue;
                if (!InCircle(ToMap(g.transform.position))) continue;
                var mp = ToMap(g.transform.position) + off;
                Box(new Rect(mp.x - 5, mp.y - 5, 10, 10), new Color(0, 0, 0, 0.6f));
                Box(new Rect(mp.x - 3.5f, mp.y - 3.5f, 7, 7), Friend);
            }
            // Самолёты: свои всегда, вражеские — ближе 400 м (треугольник)
            foreach (var a in Aircraft.All)
            {
                if (!a || a.Wrecked) continue;
                bool mine = a.Team == MyTeam;
                if (!mine && Vector3.Distance(a.transform.position, pp) > 400f) continue;
                if (!InCircle(ToMap(a.transform.position))) continue;
                var mp = ToMap(a.transform.position) + off;
                Color ac = mine ? Friend : Enemy;
                Box(new Rect(mp.x - 7, mp.y - 2, 14, 4), ac);
                Box(new Rect(mp.x - 2, mp.y - 6, 4, 12), ac);
            }
            GUI.EndClip();

            // Игрок — стрелка по взгляду
            {
                float yaw = _pc.Vehicle || _pc.Flying ? _pc.worldCamera.transform.eulerAngles.y : _pc.transform.eulerAngles.y;
                var mtx = GUI.matrix;
                GUIUtility.RotateAroundPivot(yaw, ctr * _scale);
                Tex(new Rect(ctr.x - 9, ctr.y - 11, 18, 22), _arrow, Gold);
                GUI.matrix = mtx;
            }
            Text(new Rect(ctr.x - 10, r.y + 2, 20, 22), "С", _sTiny, new Color(1, 1, 1, 0.75f));
            Text(new Rect(r.x, r.yMax - 4, size, 20), $"[M] {range * 2:0} м", _sTiny, new Color(1, 1, 1, 0.5f));
        }

        // ---------- Оверлей нагрудной камеры ----------

        void DrawBodycam()
        {
            // Надпись REC с часами убрана — остаётся только «объектив» нагрудной камеры
        }

        // ---------- Лента убийств ----------

        void DrawFeed()
        {
            float y = 16, right = W - 16;
            foreach (var f in _feed)
            {
                float age = Time.time - f.t;
                if (age > 7f) continue;
                float a = Mathf.Clamp01((7f - age) * 1.5f);
                string mid = f.tank ? "  ► ТАНК ►  " : f.head ? "  ► ● ►  " : "  ►  ";
                var st = _sSmall;
                float wa = st.CalcSize(new GUIContent(f.a)).x, wm = st.CalcSize(new GUIContent(mid)).x, wb = st.CalcSize(new GUIContent(f.b)).x;
                float total = wa + wm + wb + 20;
                Box(new Rect(right - total, y, total, 28), new Color(0, 0, 0, 0.45f * a));
                float x = right - total + 10;
                Text(new Rect(x, y + 3, wa, 24), f.a, st, new Color(f.ca.r, f.ca.g, f.ca.b, a)); x += wa;
                Text(new Rect(x, y + 3, wm, 24), mid, st, new Color(1, 1, 1, 0.8f * a)); x += wm;
                Text(new Rect(x, y + 3, wb, 24), f.b, st, new Color(f.cb.r, f.cb.g, f.cb.b, a));
                y += 32;
            }
        }

        // ---------- Здоровье, оружие ----------

        void DrawHealth()
        {
            float x = 24, y = H - 92;
            Box(new Rect(x - 8, y - 10, 340, 84), Panel);
            float hpv = _hp.Health / _hp.maxHealth;
            Color hc = hpv > 0.6f ? new Color(0.85f, 0.9f, 0.85f) : hpv > 0.3f ? new Color(0.95f, 0.75f, 0.3f) : Enemy;
            Text(new Rect(x, y - 6, 120, 50), Mathf.CeilToInt(_hp.Health).ToString(), _sBig44, hc);
            Bar(new Rect(x + 100, y + 14, 210, 12), hpv, hc, new Color(0, 0, 0, 0.5f));
            if (_pc) Bar(new Rect(x + 100, y + 30, 210, 5), _pc.Stamina, _pc.Exhausted ? Enemy : new Color(0.75f, 0.8f, 0.55f), new Color(0, 0, 0, 0.45f));
            string cls = _gm.Config.playerClass switch { SoldierClass.Assault => "ШТУРМОВИК", SoldierClass.Engineer => "ИНЖЕНЕР", SoldierClass.Support => "ПОДДЕРЖКА", _ => "СНАЙПЕР" };
            Text(new Rect(x + 100, y - 8, 220, 22), cls, _sTiny, new Color(1, 1, 1, 0.7f));
            if (_gt) Text(new Rect(x, y + 44, 520, 24), $"Гранаты  {new string('●', _gt.Count)}{new string('○', Mathf.Max(0, _gt.Max - _gt.Count))}   [Q] · бросить оружие [G] · вид [F5]", _sSmall, Color.white);
        }

        void DrawWeapon()
        {
            float x = W - 380, y = H - 132;
            Box(new Rect(x - 10, y - 8, 376, 124), Panel);
            var ft = _pc ? _pc.GetComponent<FieldTools>() : null;
            if (ft && ft.Active)
            {
                Text(new Rect(x, y, 350, 30), ft.Kind == ToolKind.Medkit ? $"АПТЕЧКА  ·  {ft.Charges}" : $"МЕШКИ С ПЕСКОМ  ·  {ft.Charges}", _sMid, Gold);
                Text(new Rect(x, y + 34, 360, 26), ft.Status, _sTiny, Color.white);
                if (ft.Progress01 > 0.01f) Bar(new Rect(x, y + 64, 340, 8), ft.Progress01, Gold, new Color(0, 0, 0, 0.5f));
            }
            else if (_trowel && _trowel.Active)
            {
                Text(new Rect(x, y, 350, 30), "ШПАТЕЛЬ — СТРОИТЕЛЬСТВО", _sMid, Gold);
                Text(new Rect(x, y + 34, 350, 26), _trowel.BlockReason ?? $"Кирпич: {_trowel.Progress01 * 100:0}%", _sSmall, Color.white);
                if (_trowel.BlockReason == null) Bar(new Rect(x, y + 64, 340, 8), _trowel.Progress01, Gold, new Color(0, 0, 0, 0.5f));
            }
            else if (_wc && _wc.Current)
            {
                var w = _wc.Current;
                Text(new Rect(x, y - 2, 350, 30), w.displayName.ToUpper(), _sMid, Color.white);
                if (w.category != WeaponCategory.Knife)
                {
                    bool low = _wc.Ammo <= Mathf.Max(1, _wc.Stats.magazineSize / 4);
                    Text(new Rect(x, y + 26, 140, 60), _wc.Ammo.ToString(), _sBig52, low ? Enemy : Color.white);
                    Text(new Rect(x + 120, y + 48, 120, 30), "/ " + _wc.Reserve, _sMid, new Color(1, 1, 1, 0.7f));
                    string mode = w.fireMode switch { FireMode.Auto => "АВТО", FireMode.Bolt => "ЗАТВОР", _ => "ОДИН." };
                    Text(new Rect(x + 250, y + 34, 100, 24), mode, _sTiny, new Color(1, 1, 1, 0.6f));
                    if (_wc.Reloading) Text(new Rect(x + 230, y + 56, 130, 24), "перезарядка…", _sTiny, Gold);
                    else if (_wc.Ammo == 0 && _wc.Reserve == 0) Text(new Rect(x + 210, y + 56, 150, 24), "НЕТ ПАТРОНОВ", _sTiny, Enemy);
                }
            }
            // Слоты
            float sx = x, sy = y + 90;
            var s2 = _wc ? _wc.SlotWeapon(2) : null; var s3 = _wc ? _wc.SlotWeapon(3) : null;
            string[] names = { "1 ОСН", s2 && s2.category == WeaponCategory.SubmachineGun ? "2 ПП" : "2 ПИСТ", s3 && s3.id.StartsWith("wrench") ? "3 КЛЮЧ" : "3 НОЖ",
                               _wc && _wc.Tool == ToolKind.Medkit ? "4 АПТЕЧКА" : _wc && _wc.Tool == ToolKind.Sandbags ? "4 МЕШКИ" : _wc && !_wc.HasSlot(3) ? "3 СТРОЙ" : "4 СТРОЙ", "4 ПТ" };
            for (int i = 1; i <= 5; i++)
            {
                if (!_wc || !_wc.HasSlot(i)) continue;
                bool cur = _wc.CurrentSlot == i;
                Box(new Rect(sx, sy, 66, 20), cur ? new Color(0.9f, 0.75f, 0.35f, 0.85f) : new Color(1, 1, 1, 0.12f));
                Text(new Rect(sx, sy, 66, 20), names[i - 1], _sTiny12C, cur ? Color.black : Color.white, false);
                sx += 70;
            }
            if (Binoculars.Allowed)
            {
                bool on = Binoculars.Active;
                Box(new Rect(sx, sy, 76, 20), on ? new Color(0.9f, 0.75f, 0.35f, 0.85f) : new Color(1, 1, 1, 0.12f));
                Text(new Rect(sx, sy, 76, 20), "Z БИНОКЛЬ", _sTiny12C, on ? Color.black : Color.white, false);
            }
        }

        // ---------- Захват и подсказки ----------

        void DrawCapture()
        {
            if (!_pc || !WorldStores.CapturePoints.IsCreated || _gm.Config.mode != GameModeType.TerritoryControl) return;
            var pts = WorldStores.CapturePoints;
            for (int i = 0; i < pts.Length; i++)
            {
                var p = pts[i];
                if (math.distancesq(p.position, (float3)_pc.transform.position) > p.radius * p.radius) continue;
                float prog = p.progress;
                byte towards = prog > 0 ? (byte)1 : prog < 0 ? (byte)0 : (byte)255;
                string state = p.contested != 0 ? "ТОЧКА ОСПАРИВАЕТСЯ" : p.owner == MyTeam && Mathf.Abs(prog) >= 0.999f ? "ТОЧКА НАША" : towards == MyTeam ? "ЗАХВАТ" : "НЕЙТРАЛИЗАЦИЯ";
                var r = new Rect(W / 2 - 200, H * 0.23f, 400, 64);
                Box(r, Panel);
                Text(new Rect(r.x, r.y + 4, r.width, 30), $"{(char)('A' + i)} · {state}", _sCenter, p.contested != 0 ? Gold : Color.white);
                Bar(new Rect(r.x + 20, r.y + 42, r.width - 40, 10), Mathf.Abs(prog), TeamCol(towards), new Color(0, 0, 0, 0.5f));
                break;
            }
        }

        void DrawPrompts()
        {
            // Ремонт ключом инженера: что чиним и полоса прочности
            if (!string.IsNullOrEmpty(WeaponController.RepairInfo) && _wc && _wc.Current && !(_pc && _pc.Vehicle))
            {
                var rr = new Rect(W / 2 - 220, H * 0.6f, 440, 52);
                Box(rr, Panel);
                Text(new Rect(rr.x, rr.y + 2, rr.width, 26), WeaponController.RepairInfo, _sCenter, Color.white);
                if (WeaponController.Repair01 >= 0) Bar(new Rect(rr.x + 20, rr.y + 34, rr.width - 40, 8), WeaponController.Repair01, WeaponController.Repair01 > 0.5f ? Gold : Enemy, new Color(0, 0, 0, 0.5f));
            }
            if (PlayerBridge.Health && PlayerBridge.Health.Alive && PlayerBridge.Health.SpawnProtected)
                Text(new Rect(W / 2 - 250, H * 0.26f, 500, 30), $"ЗАЩИТА ПОСЛЕ ПОЯВЛЕНИЯ · {Mathf.CeilToInt(PlayerBridge.Health.ProtectedUntil - Time.time)} с", _sCenter, new Color(0.6f, 0.85f, 1f, 0.9f));
            string p = PlayerInteraction.Prompt;
            if (!string.IsNullOrEmpty(p))
            {
                var r = new Rect(W / 2 - 300, H * 0.68f, 600, 36);
                Box(r, Panel);
                Text(r, p, _sCenter, Color.white);
            }
            if (_wc && _wc.Current && _wc.Current.category != WeaponCategory.Knife && !_wc.Reloading && _wc.Ammo == 0 && _wc.Reserve > 0 && !(_pc && _pc.Vehicle))
                Text(new Rect(W / 2 - 200, H / 2 + 50, 400, 30), "[R] ПЕРЕЗАРЯДКА", _sCenter, Gold);
        }

        // ---------- Танк ----------

        void DrawTankHud(Tank t)
        {
            var cam = _pc.worldCamera;
            Vector2 c = new(W / 2, H / 2);
            if (t.Zoomed && !t.IsLight)
            {
                // Прицел наводчика: круглое поле, сетка с угольниками дальности
                float rr = H * 0.46f;
                Color dark = new(0, 0, 0, 0.92f);
                Box(new Rect(0, 0, W, c.y - rr), dark); Box(new Rect(0, c.y + rr, W, H - c.y - rr), dark);
                Box(new Rect(0, c.y - rr, c.x - rr, rr * 2), dark); Box(new Rect(c.x + rr, c.y - rr, W - c.x - rr, rr * 2), dark);
                Tex(new Rect(c.x - rr, c.y - rr, rr * 2, rr * 2), _scopeMask, dark);
                Color ret = new(0.05f, 0.05f, 0.05f, 0.9f);
                Line(c + new Vector2(-12, 22), c, 2.2f, ret); Line(c + new Vector2(12, 22), c, 2.2f, ret);
                for (int i = 1; i <= 3; i++)
                {
                    Line(c + new Vector2(-40 * i - 10, 0), c + new Vector2(-40 * i + 10, 0), 2f, ret);
                    Line(c + new Vector2(40 * i - 10, 0), c + new Vector2(40 * i + 10, 0), 2f, ret);
                    Line(c + new Vector2(-6, 22 + 26 * i), c + new Vector2(6, 22 + 26 * i), 2f, ret);
                    Text(new Rect(c.x + 10, c.y + 12 + 26 * i, 60, 20), (i * 400).ToString(), _sTiny, ret, false);
                }
                Box(new Rect(c.x - 1, c.y + 30, 2, H * 0.3f), ret);
            }
            else if (t.Zoomed && t.IsLight)
            {
                // Кольцевой зенитный прицел пулемёта: два кольца, перекрестие, мушка в центре
                Color ret = new(0.08f, 0.08f, 0.07f, 0.9f);
                Tex(new Rect(c.x - 70, c.y - 70, 140, 140), _ring, ret);
                Tex(new Rect(c.x - 34, c.y - 34, 68, 68), _ring, ret);
                Box(new Rect(c.x - 70, c.y - 1, 140, 2), ret); Box(new Rect(c.x - 1, c.y - 70, 2, 140), ret);
                Tex(new Rect(c.x - 3, c.y - 3, 6, 6), _circle, new Color(1f, 0.85f, 0.4f, 0.95f));
            }
            else
            {
                Tex(new Rect(c.x - 4, c.y - 4, 8, 8), _circle, new Color(1, 1, 1, 0.9f));
            }
            // Куда смотрит ствол
            Vector3 sp = cam.WorldToScreenPoint(t.GunHitPoint);
            if (sp.z > 0)
            {
                Vector2 g = new(sp.x / _scale, (Screen.height - sp.y) / _scale);
                bool aligned = Vector2.Distance(g, c) < 6f;
                Tex(new Rect(g.x - 16, g.y - 16, 32, 32), _ring, aligned ? Gold : new Color(1, 1, 1, 0.75f));
            }

            if (!t.IsLight)
            {
                float dist = Vector3.Distance(t.rig.muzzle.position, t.PredictedImpact);
                DrawMiniWindow(9f, $"куда упадёт снаряд · {dist:0} м");
            }
            float x = 24, y = H - 150;
            Box(new Rect(x - 8, y - 10, 400, 140), Panel);
            Text(new Rect(x, y - 6, 380, 30), t.DisplayName.ToUpper(), _sMid, Gold);
            float hpv = t.Health / t.maxHealth;
            Color hc = hpv > 0.5f ? new Color(0.8f, 0.9f, 0.8f) : hpv > 0.25f ? new Color(0.95f, 0.75f, 0.3f) : Enemy;
            Text(new Rect(x, y + 26, 200, 26), $"Броня  {Mathf.CeilToInt(hpv * 100)}%", _sSmall, hc);
            Bar(new Rect(x + 130, y + 34, 250, 10), hpv, hc, new Color(0, 0, 0, 0.5f));
            Text(new Rect(x, y + 54, 200, 26), $"Скорость  {Mathf.Abs(t.Speed) * 3.6f:0} км/ч", _sSmall, Color.white);
            string wpn = t.IsLight ? (t.PlayerSeat == JeepSeat.Gunner || t.gunnerBot < 0 ? "ПУЛЕМЁТ НА ТУРЕЛИ" : "ЗА ПУЛЕМЁТОМ — БОЕЦ") + $"  ·  место: {Tank.SeatName(t.PlayerSeat)}" : t.WeaponSlot == 0 ? $"[1] ПУШКА  ·  снарядов {t.shells}" : "[2] СПАРЕННЫЙ ПУЛЕМЁТ";
            Text(new Rect(x, y + 80, 380, 26), wpn, _sSmall, Color.white);
            if (t.WeaponSlot == 0) Bar(new Rect(x, y + 108, 380, 8), t.Reload01, t.Reload01 >= 1f ? Gold : new Color(0.7f, 0.7f, 0.7f), new Color(0, 0, 0, 0.5f));
            if (Time.time - t.LastHitTime < 0.6f) Box(new Rect(0, 0, W, H), new Color(1f, 0.2f, 0.1f, 0.18f * (1 - (Time.time - t.LastHitTime) / 0.6f)));
            string help = !t.IsLight ? "WASD — движение · мышь — башня · ЛКМ — огонь · ПКМ — прицел · 1/2 — оружие · E — выход"
                        : t.PlayerSeat == JeepSeat.Gunner ? "мышь — пулемёт · ЛКМ — огонь · ПКМ — прицел · X — пересесть · E — выход"
                        : "WASD — движение · мышь — обзор · ЛКМ — пулемёт (если нет стрелка) · X — пересесть · E — выход";
            Text(new Rect(W - 720, H - 40, 700, 26), help, _sRight, new Color(1, 1, 1, 0.55f));
        }

        // ---------- Самолёт ----------

        bool ToGui(Vector3 world, out Vector2 g)
        {
            var sp = _pc.worldCamera.WorldToScreenPoint(world);
            g = new Vector2(sp.x / _scale, (Screen.height - sp.y) / _scale);
            return sp.z > 0;
        }

        void DrawPlaneHud(Aircraft a)
        {
            // Прицел: точка сведения пулемётов, вектор скорости (куда реально летим)
            if (ToGui(a.GunAimPoint, out var g))
            {
                Tex(new Rect(g.x - 22, g.y - 22, 44, 44), _ring, new Color(1f, 0.85f, 0.4f, 0.9f));
                Tex(new Rect(g.x - 3, g.y - 3, 6, 6), _circle, new Color(1f, 0.85f, 0.4f, 0.95f));
            }
            if (a.Airborne && a.Velocity.sqrMagnitude > 4f && ToGui(a.transform.position + a.Velocity * 3f, out var fv))
            {
                Color fc = new(0.6f, 1f, 0.6f, 0.8f);
                Tex(new Rect(fv.x - 7, fv.y - 7, 14, 14), _ring, fc);
                Line(fv + new Vector2(-18, 0), fv + new Vector2(-8, 0), 2f, fc);
                Line(fv + new Vector2(8, 0), fv + new Vector2(18, 0), 2f, fc);
                Line(fv + new Vector2(0, -8), fv + new Vector2(0, -15), 2f, fc);
            }
            // Метки самолётов противника в поле зрения
            foreach (var o in Aircraft.All)
            {
                if (!o || o == a || !o.Alive || o.Team == a.Team) continue;
                float d = Vector3.Distance(o.transform.position, a.transform.position);
                if (d > 1000f || !ToGui(o.transform.position, out var og)) continue;
                Color ec = new(1f, 0.35f, 0.3f, 0.85f);
                Line(og + new Vector2(0, -12), og + new Vector2(12, 0), 2f, ec); Line(og + new Vector2(12, 0), og + new Vector2(0, 12), 2f, ec);
                Line(og + new Vector2(0, 12), og + new Vector2(-12, 0), 2f, ec); Line(og + new Vector2(-12, 0), og + new Vector2(0, -12), 2f, ec);
                Text(new Rect(og.x + 14, og.y - 10, 120, 20), $"{d:0} м", _sTiny, ec, false);
            }

            // Вражеская техника подсвечивается так же, как самолёты
            foreach (var t in Tank.All)
            {
                if (!t || !t.Alive || t.Team == a.Team) continue;
                float d = Vector3.Distance(t.transform.position, a.transform.position);
                if (d > 1000f || !ToGui(t.transform.position + Vector3.up * 1.5f, out var tg)) continue;
                Color ec = new(1f, 0.55f, 0.25f, 0.85f);
                Box(new Rect(tg.x - 10, tg.y - 10, 20, 2), ec); Box(new Rect(tg.x - 10, tg.y + 8, 20, 2), ec);
                Box(new Rect(tg.x - 10, tg.y - 10, 2, 20), ec); Box(new Rect(tg.x + 8, tg.y - 10, 2, 20), ec);
                Text(new Rect(tg.x + 14, tg.y - 10, 120, 20), $"{d:0} м", _sTiny, ec, false);
            }
            // Захват помощи прицеливания
            if (a.AssistTarget && ToGui(a.AssistTarget.position, out var lk))
            {
                Color lc = (Time.time % 0.4f) < 0.25f ? Gold : new Color(1f, 1f, 1f, 0.9f);
                float r = 22f;
                Line(lk + new Vector2(-r, -r), lk + new Vector2(-r + 10, -r), 2.5f, lc); Line(lk + new Vector2(-r, -r), lk + new Vector2(-r, -r + 10), 2.5f, lc);
                Line(lk + new Vector2(r, -r), lk + new Vector2(r - 10, -r), 2.5f, lc); Line(lk + new Vector2(r, -r), lk + new Vector2(r, -r + 10), 2.5f, lc);
                Line(lk + new Vector2(-r, r), lk + new Vector2(-r + 10, r), 2.5f, lc); Line(lk + new Vector2(-r, r), lk + new Vector2(-r, r - 10), 2.5f, lc);
                Line(lk + new Vector2(r, r), lk + new Vector2(r - 10, r), 2.5f, lc); Line(lk + new Vector2(r, r), lk + new Vector2(r, r - 10), 2.5f, lc);
                Text(new Rect(lk.x - 60, lk.y + r + 4, 120, 20), "ЗАХВАТ", _sTiny, lc, false);
            }

            float x = 24, y = H - 214;
            Box(new Rect(x - 8, y - 10, 420, 204), Panel);
            Text(new Rect(x, y - 6, 400, 30), a.DisplayName.ToUpper(), _sMid, Gold);
            float hpv = a.Health / a.maxHealth;
            Color hc = hpv > 0.5f ? new Color(0.8f, 0.9f, 0.8f) : hpv > 0.25f ? new Color(0.95f, 0.75f, 0.3f) : Enemy;
            Text(new Rect(x, y + 26, 200, 26), $"Прочность  {Mathf.CeilToInt(hpv * 100)}%", _sSmall, hc);
            Bar(new Rect(x + 160, y + 34, 240, 10), hpv, hc, new Color(0, 0, 0, 0.5f));
            Text(new Rect(x, y + 54, 200, 26), $"Скорость  {a.Speed * 3.6f:0} км/ч", _sSmall, a.Stalling ? Enemy : Color.white);
            Text(new Rect(x + 210, y + 54, 200, 26), $"Высота  {Mathf.Max(0, a.Agl):0} м", _sSmall, a.Airborne && a.Agl < 30f ? Enemy : Color.white);
            Text(new Rect(x, y + 82, 120, 26), $"Газ  {a.Throttle * 100f:0}%", _sSmall, Color.white);
            Bar(new Rect(x + 130, y + 90, 270, 10), a.Throttle, Gold, new Color(0, 0, 0, 0.5f));
            Text(new Rect(x, y + 110, 400, 26), $"ЛКМ  ПУЛЕМЁТЫ  {a.MgAmmo} / {Aircraft.MaxMg}", _sSmall, a.MgAmmo > 0 ? Color.white : Enemy);
            Text(new Rect(x, y + 136, 400, 26), $"ПКМ  РАКЕТЫ  {a.Rockets} / {Aircraft.MaxRockets}", _sSmall, a.Rockets > 0 ? Color.white : Enemy);
            for (int i = 0; i < Aircraft.MaxRockets; i++)
                Box(new Rect(x + 250 + i * 15, y + 142, 10, 16), i < a.Rockets ? Gold : new Color(1, 1, 1, 0.15f));
            Text(new Rect(x, y + 162, 400, 24), a.Airborne ? "у своей базы на земле — пополнение и ремонт" : "на стоянке у базы: пополнение и ремонт за 3 с", _sTiny, new Color(1, 1, 1, 0.5f));

            // Предупреждения
            string warn = null;
            if (!a.Airborne && a.Speed < 31f && a.Throttle > 0.5f) warn = "РАЗБЕГ… отрыв от земли с 110 км/ч — потяните мышь на себя";
            else if (a.Stalling) warn = "СВАЛИВАНИЕ — опустите нос и дайте газ";
            else if (a.Airborne && a.Agl < 30f && a.Velocity.y < -3f) warn = "ЗЕМЛЯ! ВЫРАВНИВАЙТЕ";
            else if (a.OutOfBounds) warn = "ВНЕ ЗОНЫ БОЯ — РАЗВОРАЧИВАЙТЕСЬ";
            if (warn != null) Text(new Rect(W / 2 - 400, H * 0.28f, 800, 40), warn, _sCenter, (Time.time % 0.6f) < 0.4f ? Enemy : Gold);
            if (Time.time - a.LastHitTime < 0.6f) Box(new Rect(0, 0, W, H), new Color(1f, 0.2f, 0.1f, 0.16f * (1 - (Time.time - a.LastHitTime) / 0.6f)));
            string help = "W/S — газ · A/D — руль · мышь — тангаж и крен · ЛКМ — пулемёты · ПКМ — ракеты · F5 — вид · E — " + (a.Airborne ? "парашют" : "выход");
            Text(new Rect(W - 900, H - 40, 880, 26), help, _sRight, new Color(1, 1, 1, 0.55f));
        }

        // ---------- Артиллерия ----------

        /// <summary>Зенитный пулемёт: кольцевой прицел в центре, метки самолётов противника с точкой упреждения.</summary>
        void DrawAAHud(ArtilleryGun g)
        {
            Vector2 c = new(W / 2, H / 2);
            Color rc = new(1f, 1f, 1f, 0.8f);
            Tex(new Rect(c.x - 60, c.y - 60, 120, 120), _ring, rc);
            Tex(new Rect(c.x - 26, c.y - 26, 52, 52), _ring, new Color(1f, 1f, 1f, 0.5f));
            Box(new Rect(c.x - 1, c.y - 1, 3, 3), Gold);
            foreach (var a in Aircraft.All)
            {
                if (!a || !a.Alive || a.Team == g.Team) continue;
                float d = Vector3.Distance(a.transform.position, g.transform.position);
                if (d > 1200f || !ToGui(a.transform.position, out var ag)) continue;
                Color ec = new(1f, 0.35f, 0.3f, 0.9f);
                Line(ag + new Vector2(0, -12), ag + new Vector2(12, 0), 2f, ec); Line(ag + new Vector2(12, 0), ag + new Vector2(0, 12), 2f, ec);
                Line(ag + new Vector2(0, 12), ag + new Vector2(-12, 0), 2f, ec); Line(ag + new Vector2(-12, 0), ag + new Vector2(0, -12), 2f, ec);
                Text(new Rect(ag.x + 14, ag.y - 10, 120, 20), $"{d:0} м", _sTiny, ec, false);
                if (ToGui(a.transform.position + a.Velocity * (d / 900f) * 2.2f, out var lg)) Tex(new Rect(lg.x - 6, lg.y - 6, 12, 12), _ring, Gold);   // упреждение
            }
            float x = 24, y = H - 260;
            Box(new Rect(x - 8, y - 10, 420, 80), Panel);
            Text(new Rect(x, y - 6, 400, 30), g.DisplayName.ToUpper(), _sMid, Gold);
            float hpv = g.Health / g.maxHealth;
            Text(new Rect(x, y + 26, 200, 26), $"Прочность  {Mathf.CeilToInt(hpv * 100)}%", _sSmall, hpv > 0.4f ? Color.white : Enemy);
            Text(new Rect(W - 900, H - 40, 880, 26), "мышь — наводка · ЛКМ — огонь (только по самолётам) · ПКМ — приближение · жёлтый круг — упреждение · E — отойти", _sRight, new Color(1, 1, 1, 0.55f));
        }

        void DrawArtilleryHud(ArtilleryGun g)
        {
            if (g.IsAA) { DrawAAHud(g); return; }
            // Метки вражеской техники в поле зрения
            foreach (var t in Tank.All)
            {
                if (!t || !t.Alive || t.Team == g.Team) continue;
                float d = Vector3.Distance(t.transform.position, g.transform.position);
                if (d > 1100f || !ToGui(t.transform.position + Vector3.up * 2.5f, out var tg)) continue;
                Color ec = new(1f, 0.35f, 0.3f, 0.85f);
                Box(new Rect(tg.x - 9, tg.y - 9, 18, 2), ec); Box(new Rect(tg.x - 9, tg.y + 7, 18, 2), ec);
                Box(new Rect(tg.x - 9, tg.y - 9, 2, 18), ec); Box(new Rect(tg.x + 7, tg.y - 9, 2, 18), ec);
                Text(new Rect(tg.x + 12, tg.y - 10, 120, 20), $"{d:0} м", _sTiny, ec, false);
            }
            if (g.Zoomed) DrawMiniWindow(ArtilleryGun.BlastRadius, $"{g.PredictedRange:0} м");
            // Состояние орудия — крупно сверху
            string st = !g.Loaded ? (g.Loading ? $"ЗАРЯЖАНИЕ  {g.LoadProgress * 100f:0}%" : _pc.CarryingShell ? "[R] ЗАРЯДИТЬ ОРУДИЕ" : "НЕТ СНАРЯДА — принесите из кучи за орудиями")
                      : g.Aligned ? "ГОТОВ К ВЫСТРЕЛУ" : "НАВОДИТСЯ…";
            Color stc = !g.Loaded ? Enemy : g.Aligned ? new Color(0.5f, 1f, 0.5f) : Gold;
            Text(new Rect(W / 2 - 400, 120, 800, 40), st, _sCenter, stc);
            if (g.Loading) Bar(new Rect(W / 2 - 200, 166, 400, 10), g.LoadProgress, Gold, new Color(0, 0, 0, 0.5f));

            float x = 24, y = H - 330;
            Box(new Rect(x - 8, y - 10, 420, 150), Panel);
            Text(new Rect(x, y - 6, 400, 30), g.DisplayName.ToUpper(), _sMid, Gold);
            float hpv = g.Health / g.maxHealth;
            Text(new Rect(x, y + 26, 200, 26), $"Орудие  {Mathf.CeilToInt(hpv * 100)}%", _sSmall, hpv > 0.4f ? Color.white : Enemy);
            Bar(new Rect(x + 160, y + 34, 240, 10), hpv, hpv > 0.4f ? new Color(0.8f, 0.9f, 0.8f) : Enemy, new Color(0, 0, 0, 0.5f));
            float miss = Shop.HasPerk(Shop.PerkArtillerist) ? 5f : 60f;
            Text(new Rect(x, y + 54, 400, 26), $"Ствол 60°  ·  дальность {g.PredictedRange:0} м  ·  промах {miss:0}%", _sSmall, Color.white);
            Text(new Rect(x, y + 82, 160, 26), g.Reload01 >= 1f ? "ГОТОВО" : "Заряжание…", _sSmall, g.Reload01 >= 1f ? Gold : Color.white);
            Bar(new Rect(x + 160, y + 90, 240, 10), g.Reload01, g.Reload01 >= 1f ? Gold : new Color(0.7f, 0.7f, 0.7f), new Color(0, 0, 0, 0.5f));
            Text(new Rect(x, y + 108, 400, 24), miss > 10f ? "специализация «Артиллерист» (8000) — промах 5%" : "специализация «Артиллерист»: промах 5%", _sTiny, new Color(1, 1, 1, 0.5f));
            Text(new Rect(W - 900, H - 40, 880, 26), g.Zoomed ? "мышь — курсор на карте · ЛКМ — огонь по точке · отпустите ПКМ — ручная наводка"
                                                               : "мышь ←→ — поворот, ↑↓ — дальность · ЛКМ — выстрел · ПКМ — карта · J — заряжающий (200) · E — отойти", _sRight, new Color(1, 1, 1, 0.55f));
        }

        Material _satMat; bool _satMatTried;

        /// <summary>Окно с 3D-видом сверху (MiniView) слева снизу; круг — зона поражения снаряда.</summary>
        void DrawMiniWindow(float blastRadius, string caption)
        {
            if (!MiniView.Active || !MiniView.Texture) return;
            const float S = 400f;
            var r = new Rect(16, H - 350 - S - 10, S, S);
            Box(new Rect(r.x - 4, r.y - 4, S + 8, S + 8), new Color(0, 0, 0, 0.8f));
            // Насыщеннее и контрастнее: шейдер насыщенности (если не найден — как есть)
            if (!_satMatTried) { _satMatTried = true; var sh = Resources.Load<Shader>("Shaders/MiniViewSat"); if (sh && sh.isSupported) { _satMat = new Material(sh); _satMat.SetFloat("_Sat", 1.05f); _satMat.SetFloat("_Contrast", 1.03f); _satMat.SetFloat("_Bright", 0.01f); } }
            if (_satMat && Event.current.type == EventType.Repaint)
            {
                var gm = GUI.matrix; GUI.matrix = Matrix4x4.identity;
                Graphics.DrawTexture(new Rect(r.x * _scale, r.y * _scale, r.width * _scale, r.height * _scale), MiniView.Texture, _satMat);
                GUI.matrix = gm;
            }
            else if (!_satMat) GUI.DrawTexture(r, MiniView.Texture, ScaleMode.StretchToFill, false);
            Vector2 c = r.center;
            // Метки всей техники и замеченной пехоты противника
            var mc = MiniView.Cam;
            if (mc)
            {
                bool V(Vector3 w, out Vector2 g)
                {
                    var v = mc.WorldToViewportPoint(w);
                    g = new Vector2(r.x + v.x * S, r.y + (1f - v.y) * S);
                    return v.z > 0 && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f;
                }
                foreach (var t in Tank.All)
                {
                    if (!t || !V(t.transform.position + Vector3.up, out var g)) continue;
                    Color tc = !t.Alive ? new Color(0.3f, 0.3f, 0.3f) : t.Team == MyTeam ? Friend : Enemy;
                    Box(new Rect(g.x - 7, g.y - 7, 14, 14), new Color(0, 0, 0, 0.7f)); Box(new Rect(g.x - 5, g.y - 5, 10, 10), tc);
                }
                foreach (var a in Aircraft.All)
                {
                    if (!a || a.Wrecked || !V(a.transform.position, out var g)) continue;
                    Color ac = a.Team == MyTeam ? Friend : Enemy;
                    Box(new Rect(g.x - 8, g.y - 2, 16, 4), ac); Box(new Rect(g.x - 2, g.y - 7, 4, 14), ac);
                }
                foreach (var gn in ArtilleryGun.All)
                {
                    if (!gn || !V(gn.transform.position, out var g)) continue;
                    Box(new Rect(g.x - 4, g.y - 4, 8, 8), gn.Team == MyTeam ? Friend : Enemy);
                }
                var acts = CombatWorld.Actors;
                for (int i = 1; i < acts.Length; i++)
                {
                    var ac = acts[i];
                    if (!ac.alive || ac.team == MyTeam || !Spotting.Spotted(ac.actorId)) continue;
                    if (V(ac.position, out var g)) Tex(new Rect(g.x - 4, g.y - 4, 8, 8), _circle, Enemy);
                }
            }
            float rad = Mathf.Max(5f, blastRadius / Mathf.Max(1f, MiniView.WorldWidth) * S);
            Color zc = new(1f, 0.85f, 0.3f, 0.95f);
            Tex(new Rect(c.x - rad, c.y - rad, rad * 2, rad * 2), _ring, zc);
            Line(c + new Vector2(-rad - 10, 0), c + new Vector2(-3, 0), 1.5f, zc); Line(c + new Vector2(3, 0), c + new Vector2(rad + 10, 0), 1.5f, zc);
            Line(c + new Vector2(0, -rad - 10), c + new Vector2(0, -3), 1.5f, zc); Line(c + new Vector2(0, 3), c + new Vector2(0, rad + 10), 1.5f, zc);
            if (!string.IsNullOrEmpty(caption)) Text(new Rect(r.x + 8, r.y + 6, S - 16, 22), caption, _sTiny, zc, true);
        }

        /// <summary>Окно карты слева снизу (ПКМ у орудия): сектор обстрела, курсор с зоной поражения, техника противника.</summary>
        void DrawArtilleryMap(ArtilleryGun g)
        {
            if (!_map) return;
            const float S = 400f;
            var r = new Rect(16, H - 350 - S - 10, S, S);
            float Z = MapGenerator.Zone;
            Box(new Rect(r.x - 4, r.y - 4, S + 8, S + 8), new Color(0, 0, 0, 0.75f));
            GUI.DrawTexture(r, _map);
            Vector2 M(Vector3 w) => new(r.x + (Mathf.Clamp(w.x, -Z, Z) + Z) / (2 * Z) * S, r.y + (1f - (Mathf.Clamp(w.z, -Z, Z) + Z) / (2 * Z)) * S);
            float mpm = S / (2 * Z);      // пикселей на метр
            Vector3 gp = g.transform.position;
            // Сектор обстрела
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 edge = gp + Quaternion.Euler(0, g.BaseYaw + s * ArtilleryGun.SectorDeg, 0) * Vector3.forward * ArtilleryGun.MaxRange;
                Line(M(gp), M(edge), 1.5f, new Color(1f, 1f, 1f, 0.35f));
            }
            // Точки захвата
            if (WorldStores.CapturePoints.IsCreated)
                for (int i = 0; i < WorldStores.CapturePoints.Length; i++)
                {
                    var cp = WorldStores.CapturePoints[i];
                    var mp = M(cp.position);
                    Tex(new Rect(mp.x - 9, mp.y - 9, 18, 18), _ring, TeamCol(cp.owner));
                }
            // Своя пехота (чтобы не накрыть своих) и вражеская техника
            var actors = CombatWorld.Actors;
            for (int i = 1; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team != g.Team) continue;
                var mp = M(a.position); Box(new Rect(mp.x - 1.5f, mp.y - 1.5f, 3, 3), Friend);
            }
            foreach (var t in Tank.All)
            {
                if (!t || !t.Alive) continue;
                var mp = M(t.transform.position);
                Box(new Rect(mp.x - 4, mp.y - 4, 8, 8), t.Team == g.Team ? Friend : Enemy);
            }
            var gpm = M(gp); Box(new Rect(gpm.x - 4, gpm.y - 4, 8, 8), Gold);
            // Курсор: круг — зона поражения фугаса
            var cm = M(g.AimCursor);
            float rad = Mathf.Max(6f, ArtilleryGun.BlastRadius * mpm);
            Color zc = g.Reload01 >= 1f ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(1f, 1f, 1f, 0.7f);
            Tex(new Rect(cm.x - rad, cm.y - rad, rad * 2, rad * 2), _ring, zc);
            Line(cm + new Vector2(-rad - 8, 0), cm + new Vector2(-3, 0), 1.5f, zc); Line(cm + new Vector2(3, 0), cm + new Vector2(rad + 8, 0), 1.5f, zc);
            Line(cm + new Vector2(0, -rad - 8), cm + new Vector2(0, -3), 1.5f, zc); Line(cm + new Vector2(0, 3), cm + new Vector2(0, rad + 8), 1.5f, zc);
            Text(new Rect(r.x + 8, r.y + 6, S - 16, 22), $"{g.PredictedRange:0} м  ·  {(g.Reload01 >= 1f ? "ЛКМ — ОГОНЬ" : "заряжание…")}", _sTiny, zc, true);
        }

        /// <summary>Отметка попадания — в любом режиме (пешком, в танке, самолёте, у орудия).</summary>
        void DrawHitMarker()
        {
            Vector2 c = new(W / 2, H / 2);
            if (_hitT > 0)
            {
                Color hc = _hitKill ? new Color(1f, 0.25f, 0.2f) : _hitHead ? Gold : Color.white;
                float s = _hitKill ? 16 : 11, g = 5;
                Line(c + new Vector2(-g, -g), c + new Vector2(-g - s, -g - s), 2.5f, hc);
                Line(c + new Vector2(g, -g), c + new Vector2(g + s, -g - s), 2.5f, hc);
                Line(c + new Vector2(-g, g), c + new Vector2(-g - s, g + s), 2.5f, hc);
                Line(c + new Vector2(g, g), c + new Vector2(g + s, g + s), 2.5f, hc);
            }
            if (_ricoT > 0)
                Text(new Rect(W / 2 - 200, H / 2 + 46, 400, 34), "РИКОШЕТ", _sCenter, new Color(1f, 0.85f, 0.4f, Mathf.Clamp01(_ricoT * 2f)));
        }
        float _ricoT, _beatAt;

        // ---------- Гибель: глаза закрываются ----------

        void DrawEyelids(float t)
        {
            // Красная пелена сразу, затем веки смыкаются с мягким краем, пару раз «моргнув»
            Box(new Rect(0, 0, W, H), new Color(0.35f, 0f, 0f, Mathf.Clamp01(0.45f - t * 0.2f)));
            float close = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 0.9f) / 1.6f));
            float flutter = t > 0.9f && t < 1.8f ? Mathf.Max(0, Mathf.Sin((t - 0.9f) * 9f)) * 0.12f * (1.8f - t) : 0f;
            float k = Mathf.Clamp01(close - flutter);
            float lid = H * 0.5f * k;
            if (lid <= 0.5f) return;
            Color black = new(0.01f, 0.005f, 0.005f, 1f);
            Box(new Rect(0, 0, W, lid), black);
            Box(new Rect(0, H - lid, W, lid), black);
            // мягкий край век
            for (int i = 0; i < 12; i++)
            {
                float a = (1f - i / 12f) * 0.85f;
                Box(new Rect(0, lid + i * 6f, W, 6f), new Color(0.01f, 0.005f, 0.005f, a * k));
                Box(new Rect(0, H - lid - (i + 1) * 6f, W, 6f), new Color(0.01f, 0.005f, 0.005f, a * k));
            }
        }

        // ---------- Экран высадки ----------

        void DrawDeploy()
        {
            Box(new Rect(0, 0, W, H), new Color(0.02f, 0.03f, 0.02f, _gm.FirstDeploy ? 0.55f : 0.8f));
            Text(new Rect(0, 30, W, 60), _gm.FirstDeploy ? "ВЫБОР СТОРОНЫ И КЛАССА" : "ВЫСАДКА", _sTitle, Gold);
            Text(new Rect(0, 82, W, 30), $"Убийств {_gm.PlayerKills}  ·  Смертей {_gm.PlayerDeaths}  ·  Танков {_gm.PlayerTankKills}", _sSmallC, new Color(1, 1, 1, 0.75f));

            // Карта со спаунами
            float ms = 640; var mr = new Rect(W / 2 - ms - 20, 130, ms, ms);
            Box(new Rect(mr.x - 5, mr.y - 5, mr.width + 10, mr.height + 10), new Color(0, 0, 0, 0.7f));
            if (_map) GUI.DrawTexture(mr, _map);
            float Z = MapGenerator.Zone;
            Vector2 ToMap(Vector3 w) => new(mr.x + (w.x + Z) / (2 * Z) * ms, mr.y + (1 - (w.z + Z) / (2 * Z)) * ms);
            // База
            var bp = ToMap(_gm.BaseCenter(MyTeam));
            var br = new Rect(bp.x - 44, bp.y - 20, 88, 40);
            if (Button(br, "БАЗА", _deploySpawn < 0)) _deploySpawn = -1;
            // Вражеская база (для ориентации)
            var ebp = ToMap(_gm.BaseCenter((byte)(1 - MyTeam)));
            Box(new Rect(ebp.x - 10, ebp.y - 10, 20, 20), Enemy * new Color(1, 1, 1, 0.8f));
            if (WorldStores.CapturePoints.IsCreated)
                for (int i = 0; i < WorldStores.CapturePoints.Length; i++)
                {
                    var cp = WorldStores.CapturePoints[i];
                    var p = ToMap(cp.position);
                    bool can = _gm.CanDeployAt(i);
                    var r = new Rect(p.x - 26, p.y - 26, 52, 52);
                    Tex(r, _circle, TeamCol(cp.owner) * new Color(1, 1, 1, 0.9f));
                    if (_deploySpawn == i) Tex(new Rect(r.x - 8, r.y - 8, r.width + 16, r.height + 16), _ring, Gold);
                    Text(r, ((char)('A' + i)).ToString(), _sL26, Color.white);
                    if (can && GUI.Button(r, GUIContent.none, GUIStyle.none)) _deploySpawn = i;
                }
            foreach (var t in Tank.All)
                if (t && t.Alive && t.Team == MyTeam) { var p = ToMap(t.transform.position); Box(new Rect(p.x - 6, p.y - 6, 12, 12), Friend); }
            if (_deploySpawn >= 0 && !_gm.CanDeployAt(_deploySpawn)) _deploySpawn = -1;

            // Классы
            float px = W / 2 + 20, py = 130;
            // Первая высадка — выбор стороны
            if (_gm.FirstDeploy)
            {
                Text(new Rect(px, py - 6, 560, 26), "СТОРОНА", _sTiny, new Color(1, 1, 1, 0.6f));
                bool de = _gm.Config.playerFaction == Faction.Germany;
                if (Button(new Rect(px, py + 20, 274, 52), "ГЕРМАНИЯ", de)) _gm.Config.playerFaction = Faction.Germany;
                if (Button(new Rect(px + 286, py + 20, 274, 52), "СССР", !de)) _gm.Config.playerFaction = Faction.USSR;
                py += 86;
            }
            var ars = GameDatabase.Instance.Arsenal(_gm.Config.playerFaction);
            string[] cn = { "ШТУРМОВИК", "ИНЖЕНЕР", "ПОДДЕРЖКА", "СНАЙПЕР" };
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(px, py + i * 100, 560, 90);
                bool owned = Shop.ClassOwned((SoldierClass)i);
                if (Button(r, "", _deployClass == i) && owned) _deployClass = i;
                Text(new Rect(r.x + 18, r.y + 8, 400, 34), owned ? cn[i] : cn[i] + "  · ЗАКРЫТ", _sMid, !owned ? new Color(1, 1, 1, 0.4f) : _deployClass == i ? Gold : Color.white);
                if (!owned)
                {
                    Text(new Rect(r.x + 18, r.y + 46, 540, 40), $"Не куплен · {Shop.ClassPrice((SoldierClass)i)} — купить в главном меню (Кастомизация)", _sSmall, new Color(1, 1, 1, 0.5f));
                    continue;
                }
                var lo = Shop.OwnedLoadout(_gm.Config.playerFaction, (SoldierClass)i);
                string kit = $"1 {(lo.primary ? lo.primary.displayName : "—")} · 2 {(lo.secondary ? lo.secondary.displayName : "—")} · 3 {(lo.hasTrowel ? "шпатель" : lo.melee ? lo.melee.displayName : "—")}" +
                             (lo.gadget ? $" · 4 {lo.gadget.displayName}" : "") +
                             ((SoldierClass)i == SoldierClass.Support && Shop.HasBinoculars ? " · бинокль" : "") + $" · гранаты ×{lo.grenades}";
                Text(new Rect(r.x + 18, r.y + 46, 540, 40), kit, _sSmall, new Color(1, 1, 1, 0.8f));
            }
            float by = py + 404;
            if (_deployClass >= 0)
            {
                // Основное оружие класса — по кругу из всех вариантов (выбор запоминается, как в кастомизации)
                var lo = ars.GetClass((SoldierClass)_deployClass);
                var owned = lo != null && lo.primaryOptions != null ? lo.primaryOptions.FindAll(Shop.WeaponOwned) : new System.Collections.Generic.List<WeaponDefinition>();
                if (owned.Count > 1)
                {
                    var cur = Shop.OwnedLoadout(_gm.Config.playerFaction, (SoldierClass)_deployClass).primary;
                    if (Button(new Rect(px, by, 560, 42), $"Оружие: {(cur ? cur.displayName : "—")}  ({owned.IndexOf(cur) + 1}/{owned.Count}) — сменить"))
                        LoadoutChoice.Set(_gm.Config.playerFaction, (SoldierClass)_deployClass, owned[(owned.IndexOf(cur) + 1) % owned.Count]);
                }
                else if (owned.Count == 0)
                    Text(new Rect(px, by + 8, 560, 30), "Основное оружие не куплено — купите в главном меню или подберите с поля боя", _sTinyC, new Color(1, 0.8f, 0.5f, 0.85f));
            }
            string where = _deploySpawn < 0 ? "на базе" : $"на точке {(char)('A' + _deploySpawn)}";
            if (Button(new Rect(px, by + 52, 560, 66), $"ВЫСАДИТЬСЯ {where.ToUpper()}"))
                _gm.Deploy((SoldierClass)Mathf.Max(0, _deployClass), _deployAlt, _deploySpawn);
            if (!float.IsInfinity(_gm.AutoDeployAt))
                Text(new Rect(px, by + 124, 560, 24), $"Автовысадка через {Mathf.Max(0, Mathf.CeilToInt(_gm.AutoDeployAt - Time.time))} с", _sTinyC, new Color(1, 1, 1, 0.6f));
            else
                Text(new Rect(px, by + 124, 560, 24), $"{_gm.Config.timeLimitMinutes:0} мин · ботов {_gm.Config.botCount} · {_gm.Mode.Describe()}", _sTinyC, new Color(1, 1, 1, 0.6f));
            Text(new Rect(px, by + 150, 560, 24), "Танки — у баз: подойдите и нажмите E. Ящики с патронами — на базах и точках.", _sTinyC, new Color(1, 1, 1, 0.5f));
            if (_gm.FirstDeploy && Button(new Rect(px, by + 186, 200, 40), "В МЕНЮ")) _gm.QuitToMenu();
        }

        // ---------- Таблица, пауза, итог ----------

        void DrawScoreboard(GameMode m)
        {
            var r = new Rect(W / 2 - 360, H / 2 - 170, 720, 300);
            Box(r, new Color(0, 0, 0, 0.75f));
            Text(new Rect(r.x, r.y + 12, r.width, 40), "СВОДКА БОЯ", _sCenter, Gold);
            int alive0 = 0, alive1 = 0;
            foreach (var a in CombatWorld.Actors) if (a.alive) { if (a.team == 0) alive0++; else alive1++; }
            Text(new Rect(r.x + 40, r.y + 70, 300, 30), $"Германия: убийств {m.Score[0]}", _sMid, TeamCol(0));
            Text(new Rect(r.x + 380, r.y + 70, 300, 30), $"СССР: убийств {m.Score[1]}", _sMid, TeamCol(1));
            Text(new Rect(r.x + 40, r.y + 104, 300, 30), $"в строю: {alive0}", _sSmall, Color.white);
            Text(new Rect(r.x + 380, r.y + 104, 300, 30), $"в строю: {alive1}", _sSmall, Color.white);
            Text(new Rect(r.x + 40, r.y + 160, 640, 30), $"Вы:  убийств {_gm.PlayerKills}  ·  смертей {_gm.PlayerDeaths}  ·  танков {_gm.PlayerTankKills}", _sMid, Color.white);
            Text(new Rect(r.x + 40, r.y + 210, 640, 60), "WASD · Shift бег · Ctrl присед · ЛКМ/ПКМ · R · 1–5 слоты · B стройка · F флип ножа · V удар · G граната · T осмотр · E действие/танк · Tab сводка · Esc пауза", _sTinyWrap, new Color(1, 1, 1, 0.6f));
        }

        // ======================= Виньетка (если постобработка URP недоступна) и оптика =======================

        Texture2D _vigTex;

        void DrawVignette(bool alive)
        {
            if (!_vigTex)
            {
                const int N = 256;
                _vigTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HudVignette" };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx * 0.85f + dy * dy);
                        float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.35f, d));
                        px[y * N + x] = new Color(1, 1, 1, a * a * 0.9f + a * 0.1f);
                    }
                _vigTex.SetPixels(px); _vigTex.Apply();
            }
            var st = SaveSystem.Settings;
            float k = st.vignette * (st.bodycam ? 1.15f : 1f) * (_wc ? 1f + _wc.Ads01 * 0.25f : 1f);
            float red = alive ? PostFx.DamageRed : 0f;
            Color c = Color.Lerp(new Color(0, 0, 0, Mathf.Clamp01(0.75f * k)), new Color(0.5f, 0f, 0f, Mathf.Clamp01(0.85f * k)), red);
            Tex(new Rect(0, 0, W, H), _vigTex, c);
        }

        /// <summary>Снайперский прицел: чёрное поле, оправа, сетка (немецкий ZF — три пенька, советский ПУ — пенёк с нитями).</summary>
        void DrawScope()
        {
            if (!_wc || !_wc.Current || !_wc.HasOptic || _wc.Ads01 < 0.85f) return;
            float a = Mathf.InverseLerp(0.85f, 1f, _wc.Ads01);
            Vector2 c = new(W / 2, H / 2);
            float rr = H * (_wc.Stats.scopeOverlay ? 0.46f : 0.6f);   // 4× — тесное поле, 2× — шире
            Color dark = new(0, 0, 0, a);
            Box(new Rect(0, 0, W, c.y - rr), dark); Box(new Rect(0, c.y + rr, W, H - c.y - rr), dark);
            Box(new Rect(0, c.y - rr, c.x - rr, rr * 2), dark); Box(new Rect(c.x + rr, c.y - rr, W - c.x - rr, rr * 2), dark);
            Tex(new Rect(c.x - rr, c.y - rr, rr * 2, rr * 2), _scopeMask, dark);
            Color ret = new(0.02f, 0.02f, 0.02f, 0.95f * a);
            if (_wc.Current.faction == Faction.Germany)
            {
                // ZF 39: толстые пеньки слева, справа и снизу, нижний — заострённый к центру
                Box(new Rect(c.x - rr, c.y - 3, rr - 40, 6), ret);
                Box(new Rect(c.x + 40, c.y - 3, rr - 40, 6), ret);
                Box(new Rect(c.x - 5, c.y + 26, 10, rr - 26), ret);
                Line(c + new Vector2(-5, 26), c, 2.5f, ret); Line(c + new Vector2(5, 26), c, 2.5f, ret);
            }
            else
            {
                // ПУ 3,5×: пенёк снизу и тонкие горизонтальные нити
                Box(new Rect(c.x - rr, c.y - 1, rr - 26, 2), ret);
                Box(new Rect(c.x + 26, c.y - 1, rr - 26, 2), ret);
                Box(new Rect(c.x - 3, c.y + 4, 6, rr - 4), ret);
                Line(c + new Vector2(-3, 4), c, 2f, ret); Line(c + new Vector2(3, 4), c, 2f, ret);
            }
            // Дыхание — едва заметное «плавание» тени по краю
            Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.06f * a * (0.5f + 0.5f * Mathf.Sin(Time.time * 1.1f))));
        }

        // ======================= Пауза и полные настройки =======================

        int _pauseTab = -1;          // -1 — главное меню паузы, 0..3 — вкладки настроек
        bool _settingsDirty; float _settingsSaveAt;
        float _rowY; Rect _pr;

        void MarkDirty() { _settingsDirty = true; _settingsSaveAt = Time.unscaledTime + 0.25f; }

        void LateUpdate()
        {
            // Настройки применяются и сохраняются с небольшой задержкой (ползунок не пишет файл каждый кадр)
            if (_settingsDirty && Time.unscaledTime >= _settingsSaveAt) { _settingsDirty = false; RuntimeMenu.ApplySettings(); }
        }

        void DrawPause()
        {
            Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.55f));
            if (_pauseTab < 0)
            {
                var r = new Rect(W / 2 - 260, H / 2 - 210, 520, 420);
                Box(r, new Color(0.08f, 0.09f, 0.07f, 0.95f));
                Text(new Rect(r.x, r.y + 16, r.width, 50), "ПАУЗА", _sTitle, Gold);
                if (Button(new Rect(r.x + 30, r.y + 90, r.width - 60, 54), "ПРОДОЛЖИТЬ")) { _paused = false; GameInput.SetEnabled(true); }
                if (Button(new Rect(r.x + 30, r.y + 154, r.width - 60, 54), "НАСТРОЙКИ")) _pauseTab = 0;
                // Строй армий — меняется прямо в бою, для обеих сторон
                string style = FrontPlanner.Style == FrontStyle.BroadLine ? "широкая полоса" : "отдельные полки";
                if (Button(new Rect(r.x + 30, r.y + 218, r.width - 60, 54), $"Строй армий: {style}"))
                    FrontPlanner.Style = FrontPlanner.Style == FrontStyle.BroadLine ? FrontStyle.Regiments : FrontStyle.BroadLine;
                Text(new Rect(r.x + 30, r.y + 276, r.width - 60, 40),
                     FrontPlanner.Style == FrontStyle.BroadLine ? "Сплошная линия по всей ширине поля, наступает вместе" : "Колонны по 4 в ряд, каждый полк — свой коридор и свой темп",
                     _sTinyC, new Color(1, 1, 1, 0.6f), false);
                if (Button(new Rect(r.x + 30, r.y + 334, r.width - 60, 54), "В ГЛАВНОЕ МЕНЮ")) _gm.QuitToMenu();
                return;
            }

            _pr = new Rect(W / 2 - 700, H / 2 - 450, 1400, 900);
            Box(_pr, new Color(0.08f, 0.09f, 0.07f, 0.96f));
            Text(new Rect(_pr.x, _pr.y + 10, _pr.width, 50), "НАСТРОЙКИ", _sTitle, Gold);
            string[] tabs = { "ГРАФИКА", "КАМЕРА И ЭФФЕКТЫ", "ЗВУК", "УПРАВЛЕНИЕ" };
            float tw = (_pr.width - 60) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
                if (Button(new Rect(_pr.x + 30 + i * tw, _pr.y + 70, tw - 8, 44), tabs[i], _pauseTab == i)) _pauseTab = i;

            var s = SaveSystem.Settings;
            _rowY = _pr.y + 136;
            switch (_pauseTab)
            {
                case 0:
                {
                    // Как в главном меню: автоподбор под желаемый FPS + ручные параметры
                    s.autoGraphics = Toggle("Автоподбор графики", s.autoGraphics);
                    int[] fps = { 40, 60, 80, 100, 120, 144, 165, 240, 0 };
                    int fi = 0; for (int i = 1; i < fps.Length; i++) if (Mathf.Abs(fps[i] - s.targetFps) < Mathf.Abs(fps[fi] - s.targetFps)) fi = i;
                    s.targetFps = fps[Cycle("Желаемый FPS", System.Array.ConvertAll(fps, x => x > 0 ? x.ToString() : "без ограничения"), fi)];
                    int pr = Cycle("Пресет", new[] { "Низкий", "Средний", "Высокий", "Ультра" }, (int)s.preset);
                    if (pr != (int)s.preset) { s.autoGraphics = false; RuntimeMenu.ApplyPreset(s, (GraphicsPreset)pr); MarkDirty(); }
                    s.renderScale = Slide("Масштаб рендера", s.renderScale, 0.5f, 1.5f, v => $"{v * 100:0}%");
                    s.dynamicResolution = Toggle("Динамическое разрешение", s.dynamicResolution);
                    s.vSync = Toggle("Вертикальная синхронизация", s.vSync);
                    s.antiAliasing = Cycle("Сглаживание", new[] { "Выкл", "FXAA", "SMAA", "MSAA 4x" }, Mathf.Clamp(s.antiAliasing, 0, 3));
                    s.shadowQuality = Cycle("Тени", new[] { "Выкл", "Низкие", "Средние", "Высокие" }, Mathf.Clamp(s.shadowQuality, 0, 3));
                    s.ssao = Toggle("Затенение (SSAO)", s.ssao);
                    s.viewDistance = Cycle("Дальность прорисовки", new[] { "Близко", "Средне", "Далеко" }, Mathf.Clamp(s.viewDistance, 0, 2));
                    s.grassDensity = Slide("Густота травы", s.grassDensity, 0f, 1f, v => $"{v * 100:0}%");
                    s.textureQuality = Cycle("Текстуры", new[] { "Полные", "Половина" }, Mathf.Clamp(s.textureQuality, 0, 1));
                    break;
                }
                case 1:
                    s.fieldOfView = Slide("Поле зрения (по горизонтали)", s.fieldOfView, 70f, 120f, v => $"{v:0}°");
                    s.bodycam = Toggle("Нагрудная камера (bodycam)", s.bodycam);
                    s.fisheye = Cycle("Рыбий глаз", new[] { "Выкл", "Слабый", "Сильный" }, Mathf.Clamp(s.fisheye, 0, 2));
                    s.heavyLook = Toggle("Инерция обзора", s.heavyLook);
                    s.motionBlur = Toggle("Размытие движения", s.motionBlur);
                    s.bloom = Toggle("Свечение (bloom)", s.bloom);
                    s.filmGrain = Toggle("Зерно плёнки", s.filmGrain);
                    s.chromatic = Toggle("Хроматическая аберрация", s.chromatic);
                    s.vignette = Slide("Виньетка", s.vignette, 0f, 1.5f, v => $"{v * 100:0}%");
                    s.shake = Slide("Тряска экрана", s.shake, 0f, 4f, v => v < 0.01f ? "выкл" : $"{v * 100:0}%");
                    s.maxWrecks = Mathf.RoundToInt(Slide("Обломки техники (макс.)", s.maxWrecks, 0, 40, v => $"{v:0}"));
                    break;
                case 2:
                    s.masterVolume = Slide("Общая громкость", s.masterVolume, 0f, 1f, v => $"{v * 100:0}%");
                    s.musicVolume = Slide("Музыка в меню", s.musicVolume, 0f, 1f, v => $"{v * 100:0}%");
                    break;
                case 3:
                    s.mouseSensitivity = Slide("Чувствительность мыши", s.mouseSensitivity, 0.1f, 5f, v => $"{v:0.00}");
                    s.adsSensitivity = Slide("Чувствительность в прицеле", s.adsSensitivity, 0.2f, 1.5f, v => $"{v * 100:0}%");
                    s.invertY = Toggle("Инверсия по вертикали", s.invertY);
                    s.touchLookSensitivity = Slide("Сенсор: чувствительность обзора", s.touchLookSensitivity, 0.2f, 3f, v => $"{v:0.00}");
                    Text(new Rect(_pr.x + 40, _rowY + 10, _pr.width - 80, 60),
                         "Клавиши переназначаются в главном меню → НАСТРОЙКИ.", _sTinyWrap, new Color(1, 1, 1, 0.6f), false);
                    break;
            }

            if (Button(new Rect(_pr.x + 30, _pr.yMax - 70, 260, 50), "← НАЗАД")) { _pauseTab = -1; if (_settingsDirty) { _settingsDirty = false; RuntimeMenu.ApplySettings(); } }
            if (Button(new Rect(_pr.xMax - 290, _pr.yMax - 70, 260, 50), "ПРОДОЛЖИТЬ"))
            {
                _pauseTab = -1; _paused = false; GameInput.SetEnabled(true);
                if (_settingsDirty) { _settingsDirty = false; RuntimeMenu.ApplySettings(); }
            }
        }

        Rect RowLabel(string label)
        {
            Text(new Rect(_pr.x + 40, _rowY, 420, 44), label, _sTinyWrapMid, Color.white, false);
            var r = new Rect(_pr.x + 480, _rowY + 4, _pr.width - 520, 38);
            _rowY += 50;
            return r;
        }

        int Cycle(string label, string[] opts, int idx)
        {
            var r = RowLabel(label);
            int n = opts.Length;
            if (Button(new Rect(r.x, r.y, 50, r.height), "<")) { idx = (idx - 1 + n) % n; MarkDirty(); }
            Text(new Rect(r.x + 56, r.y, r.width - 112, r.height), opts[Mathf.Clamp(idx, 0, n - 1)], _sL24, Gold, false);
            if (Button(new Rect(r.xMax - 50, r.y, 50, r.height), ">")) { idx = (idx + 1) % n; MarkDirty(); }
            return idx;
        }

        bool Toggle(string label, bool v)
        {
            var r = RowLabel(label);
            if (Button(r, v ? "ВКЛ" : "ВЫКЛ", v)) { v = !v; MarkDirty(); }
            return v;
        }

        float Slide(string label, float v, float min, float max, System.Func<float, string> fmt)
        {
            var r = RowLabel(label);
            float nv = GUI.HorizontalSlider(new Rect(r.x, r.y + 12, Mathf.Min(r.width - 130, 663f), 20), v, min, max);   // в 1,7 раза длиннее
            Text(new Rect(r.xMax - 120, r.y, 120, r.height), fmt(nv), _sL24, Gold, false);
            if (!Mathf.Approximately(nv, v)) MarkDirty();
            return nv;
        }

        void DrawEnd(GameMode m)
        {
            Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f));
            bool won = m.Winner == _gm.Config.playerFaction;
            string title = m.Winner == null ? "НИЧЬЯ" : won ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
            Text(new Rect(0, H * 0.22f, W, 90), title, _sHuge, m.Winner == null ? Color.white : won ? Gold : Enemy);
            Text(new Rect(0, H * 0.22f + 90, W, 40), $"Убийств за матч: Германия {m.Score[0]} · СССР {m.Score[1]}", _sMidC, Color.white);
            int reward = _gm.PlayerKills * 10 + _gm.PlayerTankKills * 60 + (won ? 250 : 75) + Mathf.RoundToInt(m.Score[(int)_gm.Config.playerFaction] * 0.1f);
            Text(new Rect(0, H * 0.22f + 140, W, 40), $"Убийств {_gm.PlayerKills}  ·  Смертей {_gm.PlayerDeaths}  ·  Танков {_gm.PlayerTankKills}  ·  Награда +{reward}", _sMidC, Gold);
            if (Button(new Rect(W / 2 - 180, H * 0.22f + 220, 360, 60), "В ГЛАВНОЕ МЕНЮ")) _gm.QuitToMenu();
        }
    }
}
