using System;
using System.Collections;
using System.Collections.Generic;
using Frontline.Bots;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Frontline
{
    /// <summary>
    /// Полевая гаубица (Германия — 10,5 cm leFH 18, СССР — 122-мм М-30) у базы. Расчёт — бот или игрок.
    /// Бот бьёт навесом по вражеской технике и скоплениям пехоты (не ближе 15 м от своих), с пристрелкой:
    /// каждый следующий выстрел по той же цели точнее. Игрок: мышь — наводка (±40° по горизонту, 0–65° по
    /// вертикали), 1/2/3 или колесо — заряд, ЛКМ — выстрел, ПКМ — бинокль (приближение к точке падения), E — отойти.
    /// HUD показывает расчётную точку падения снаряда. Орудие разбивают снаряды, ракеты и гранаты; пули держит щит.
    /// </summary>
    public class ArtilleryGun : MonoBehaviour
    {
        public static readonly List<ArtilleryGun> All = new();
        public static event Action<ArtilleryGun, int> Destroyed;

        public Faction faction;
        public byte Team => (byte)faction;
        public int index;
        public string DisplayName => IsAA ? (faction == Faction.Germany ? "2 cm Flak 38" : "ДШК (зенитный)")
                                          : faction == Faction.Germany ? "10,5 cm leFH 18" : "122-мм гаубица М-30";
        /// <summary>Зенитный пулемёт: стреляет только по самолётам, вращается на 360°.</summary>
        public bool IsAA { get; private set; }
        public float maxHealth = 320f;
        public float Health { get; private set; }
        public bool Alive => Health > 0;
        public bool PlayerAboard { get; private set; }
        public bool HasCrew => PlayerAboard || crewBot > 0;
        public float Elevation => _elev;
        public float Reload01 => 1f - Mathf.Clamp01(_reload / ReloadTime);
        public bool Zoomed { get; private set; }
        public float PredictedRange { get; private set; }
        public Vector3 BasePosition => _basePos != Vector3.zero ? _basePos : transform.position;
        public float YawOffset => _yawOff;
        public float Recoil01 => _recoil;

        /// <summary>Сетевая кукла у клиента: наводка, откат, наличие снаряда и расчёта — с хоста.</summary>
        public void ApplyNetPose(float yawOff, float elev, float recoil, bool loaded, bool crew)
        {
            if (recoil > _recoil + 0.5f) { _slide = 1f; if (FxPool.I && _muzzle) { FxPool.I.MuzzleFlashAt(_muzzle.position, _muzzle.forward); FxPool.I.Smoke(_muzzle.position); } }
            _yawOff = yawOff; _elev = elev; _recoil = recoil; Loaded = loaded;
            _slide = Mathf.MoveTowards(_slide, 0f, Time.deltaTime * 0.7f);
            ApplyAim();
            if (_crewFig && _crewFig.gameObject.activeSelf != crew) _crewFig.gameObject.SetActive(crew);
        }
        public int CrewActorId => PlayerAboard ? CombatWorld.PlayerActorId : crewBot > 0 ? crewBot : -2000 - index;
        public float LastHitTime { get; private set; } = -99f;
        [NonSerialized] public int crewBot = -1;

        const float ReloadTime = 6f, MaxTraverse = 40f, MinElev = 60f, MaxElev = 60f, Scale = 1.3f;
        // Фугас: радиус больше двух корпусов танка, близкий разрыв уничтожает танк с одного попадания
        public const float BlastRadius = 18f;
        const float BlastDamage = 230f, ArmorDirect = 2000f, ArmorSplash = 1700f;

        Transform _mount, _barrel, _muzzle, _crewFig, _tube;
        AudioSource _loud;
        Vector3 _basePos;
        float _baseYaw, _yawOff, _elev = 60f, _reload, _recoil, _think;
        MapTheme _theme;
        AudioClip _shot;
        bool _dead; float _wreckUntil;

        // ---------------- Создание и модель ----------------

        public static ArtilleryGun Create(Faction f, MapTheme theme, Vector3 pos, float yaw, int index, Transform parent, int layer)
        {
            bool de = f == Faction.Germany, winter = theme == MapTheme.Winter;
            Color paint = de ? new Color(0.4f, 0.42f, 0.37f) : new Color(0.3f, 0.36f, 0.23f);
            if (winter) paint = new Color(0.82f, 0.83f, 0.82f);
            Color dark = Color.Lerp(paint, Color.black, 0.45f), steel = new(0.24f, 0.24f, 0.25f), tyre = new(0.08f, 0.08f, 0.08f), wood = new(0.35f, 0.25f, 0.15f);

            var root = new GameObject(de ? "Artillery_leFH18" : "Artillery_M30");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var g = root.AddComponent<ArtilleryGun>();
            g.faction = f; g.index = index; g._theme = theme; g._baseYaw = yaw; g.Health = g.maxHealth;
            Transform r = root.transform;
            GameObject P(Transform p, string n, Mesh m, Vector3 at, Vector3 sc, Color c, Vector3 eu = default, float sm = 0.3f, float me = 0.25f)
                => ProceduralModels.Part(p, n, m, at, sc, c, eu, false, sm, me);

            // Как 152-мм 2А36 / 2А65: большие колёса, длинные раздвижные станины с сошниками, низкий щит,
            // люлька с цилиндрами противооткатных устройств над стволом, очень длинный ствол с многощелевым тормозом.
            for (int s = -1; s <= 1; s += 2)
            {
                P(r, "Wheel", MeshUtil.Cylinder(18), new Vector3(s * 1.05f, 0.62f, 0.15f), new Vector3(1.24f, 0.3f, 1.24f), tyre, new Vector3(0, 0, 90), 0.15f, 0f);
                P(r, "Rim", MeshUtil.Cylinder(12), new Vector3(s * 1.1f, 0.62f, 0.15f), new Vector3(0.75f, 0.31f, 0.75f), paint, new Vector3(0, 0, 90));
                P(r, "Hub", MeshUtil.Cylinder(8), new Vector3(s * 1.27f, 0.62f, 0.15f), new Vector3(0.25f, 0.06f, 0.25f), dark, new Vector3(0, 0, 90));
                P(r, "Trail", MeshUtil.Bevel(0.04f), new Vector3(s * 0.62f, 0.32f, -2.3f), new Vector3(0.24f, 0.26f, 4.4f), paint, new Vector3(-5f, s * -13f, 0));
                P(r, "TrailBox", MeshUtil.Bevel(0.02f), new Vector3(s * 0.95f, 0.25f, -3.6f), new Vector3(0.3f, 0.22f, 0.8f), dark, new Vector3(-5f, s * -13f, 0));
                P(r, "Spade", MeshUtil.Bevel(0.02f), new Vector3(s * 1.15f, 0.12f, -4.4f), new Vector3(0.65f, 0.45f, 0.07f), dark, new Vector3(-25f, s * -13f, 0));
            }
            P(r, "Axle", MeshUtil.Bevel(0.03f), new Vector3(0, 0.62f, 0.15f), new Vector3(2.2f, 0.16f, 0.2f), dark);
            P(r, "Carriage", MeshUtil.Bevel(0.05f), new Vector3(0, 0.72f, 0f), new Vector3(1.1f, 0.3f, 1.1f), paint);

            var mount = new GameObject("Mount").transform; mount.SetParent(r, false); mount.localPosition = new Vector3(0, 0.9f, 0.05f);
            g._mount = mount;
            P(mount, "Pivot", MeshUtil.Cylinder(12), new Vector3(0, 0.0f, 0), new Vector3(0.9f, 0.12f, 0.9f), dark);
            P(mount, "Cheek", MeshUtil.Bevel(0.04f), new Vector3(-0.32f, 0.35f, 0), new Vector3(0.1f, 0.6f, 0.8f), paint);
            P(mount, "Cheek", MeshUtil.Bevel(0.04f), new Vector3(0.32f, 0.35f, 0), new Vector3(0.1f, 0.6f, 0.8f), paint);
            P(mount, "Shield", MeshUtil.Bevel(0.02f), new Vector3(0, 0.35f, 0.6f), new Vector3(2.0f, 0.75f, 0.05f), paint, new Vector3(-8, 0, 0));
            for (int s = -1; s <= 1; s += 2)
                P(mount, "ShieldWing", MeshUtil.Bevel(0.02f), new Vector3(s * 1.1f, 0.3f, 0.5f), new Vector3(0.35f, 0.65f, 0.05f), paint, new Vector3(-8, s * 22f, 0));
            P(mount, "Sight", MeshUtil.Bevel(0.02f), new Vector3(-0.45f, 0.7f, 0.15f), new Vector3(0.1f, 0.18f, 0.28f), new Color(0.15f, 0.15f, 0.14f));
            P(mount, "ElevWheel", MeshUtil.Cylinder(12), new Vector3(0.45f, 0.4f, -0.25f), new Vector3(0.3f, 0.03f, 0.3f), dark, new Vector3(0, 0, 90));

            var barrel = new GameObject("Barrel").transform; barrel.SetParent(mount, false); barrel.localPosition = new Vector3(0, 0.45f, 0.1f);
            g._barrel = barrel;
            // Люлька и противооткатные цилиндры над стволом
            P(barrel, "Cradle", MeshUtil.Bevel(0.05f), new Vector3(0, -0.02f, 0.3f), new Vector3(0.42f, 0.36f, 2.2f), paint);
            P(barrel, "RecoilCyl", MeshUtil.Cylinder(10), new Vector3(-0.1f, 0.26f, 0.5f), new Vector3(0.15f, 2.0f, 0.15f), paint, new Vector3(90, 0, 0));
            P(barrel, "RecoilCyl", MeshUtil.Cylinder(10), new Vector3(0.1f, 0.26f, 0.5f), new Vector3(0.15f, 2.0f, 0.15f), paint, new Vector3(90, 0, 0));
            // Подвижная часть (ствол с казёнником) — откатывается назад при выстреле
            var tube = new GameObject("Tube").transform; tube.SetParent(barrel, false);
            g._tube = tube;
            P(tube, "Breech", MeshUtil.Bevel(0.04f), new Vector3(0, 0, -0.9f), new Vector3(0.44f, 0.44f, 0.6f), dark);
            P(tube, "Jacket", MeshUtil.Frustum(14, 0.8f), new Vector3(0, 0, 0.6f), new Vector3(0.34f, 2.6f, 0.34f), steel, new Vector3(90, 0, 0));
            P(tube, "Barrel", MeshUtil.Frustum(14, 0.82f), new Vector3(0, 0, 4.4f), new Vector3(0.26f, 5.2f, 0.26f), steel, new Vector3(90, 0, 0));
            // Многощелевой дульный тормоз
            P(tube, "BrakeBody", MeshUtil.Cylinder(12), new Vector3(0, 0, 7.25f), new Vector3(0.36f, 0.75f, 0.36f), dark, new Vector3(90, 0, 0));
            for (int k = 0; k < 4; k++)
                for (int s = -1; s <= 1; s += 2)
                    P(tube, "BrakeSlot", MeshUtil.Bevel(0.01f), new Vector3(s * 0.17f, 0, 6.97f + k * 0.17f), new Vector3(0.05f, 0.22f, 0.07f), new Color(0.04f, 0.04f, 0.04f));
            var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(tube, false); muzzle.localPosition = new Vector3(0, 0, 7.7f);
            g._muzzle = muzzle;

            // Фигурка наводчика за щитом (видна, когда у орудия бот)
            var fig = new GameObject("Crew").transform; fig.SetParent(r, false); fig.localPosition = new Vector3(-0.6f, 0, -1.1f);
            Color uni = de ? new Color(0.36f, 0.37f, 0.32f) : new Color(0.42f, 0.4f, 0.28f);
            P(fig, "Legs", MeshUtil.Bevel(0.05f), new Vector3(0, 0.45f, 0), new Vector3(0.34f, 0.9f, 0.24f), uni, default, 0.1f, 0f);
            P(fig, "Body", MeshUtil.Bevel(0.06f), new Vector3(0, 1.15f, 0.05f), new Vector3(0.42f, 0.55f, 0.28f), uni, new Vector3(15, 0, 0), 0.1f, 0f);
            P(fig, "Head", MeshUtil.Ico(), new Vector3(0, 1.58f, 0.12f), new Vector3(0.22f, 0.26f, 0.24f), new Color(0.75f, 0.6f, 0.5f), default, 0.2f, 0f);
            P(fig, "Helmet", MeshUtil.Ico(), new Vector3(0, 1.66f, 0.12f), new Vector3(0.3f, 0.18f, 0.32f), de ? new Color(0.3f, 0.32f, 0.28f) : new Color(0.28f, 0.33f, 0.22f), default, 0.3f, 0f);
            fig.gameObject.SetActive(false);
            g._crewFig = fig;

            // Крупнее в 1,6 раза (фигурка расчёта — обычного роста); детали склеены в один меш на узел — меньше draw calls
            root.transform.localScale = Vector3.one * Scale;
            fig.localScale = Vector3.one / Scale;
            var groups = new HashSet<Transform> { r, mount, barrel, tube, fig };
            foreach (var gr in new[] { tube, barrel, mount, fig, r }) MeshMerge.Merge(gr, groups);
            AircraftModels.SetupLod(root);      // вдали — только крупные части
            g._basePos = pos;
            var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.95f, 0.2f); bc.size = new Vector3(2.2f, 1.6f, 1.2f);
            var bc2 = root.AddComponent<BoxCollider>(); bc2.center = new Vector3(0, 0.4f, -1.7f); bc2.size = new Vector3(1.6f, 0.6f, 3.2f);
            foreach (var c in root.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
            if (Application.isMobilePlatform)
                foreach (var rr in root.GetComponentsInChildren<Renderer>(true)) if (rr.bounds.size.magnitude < 1f) rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g._shot = SoundSynth.TankCannon(de);
            g._think = Random.Range(2f, 5f);
            g.ApplyAim();
            All.Add(g);
            return g;
        }

        // ================= Зенитный пулемёт =================

        public static ArtilleryGun CreateAA(Faction f, MapTheme theme, Vector3 pos, float yaw, int index, Transform parent, int layer)
        {
            bool de = f == Faction.Germany, winter = theme == MapTheme.Winter;
            Color paint = de ? new Color(0.4f, 0.42f, 0.37f) : new Color(0.3f, 0.36f, 0.23f);
            if (winter) paint = new Color(0.82f, 0.83f, 0.82f);
            Color dark = Color.Lerp(paint, Color.black, 0.45f), steel = new(0.22f, 0.22f, 0.23f);
            var root = new GameObject(de ? "AA_Flak38" : "AA_DShK");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var g = root.AddComponent<ArtilleryGun>();
            g.IsAA = true; g.faction = f; g.index = index; g._theme = theme; g._baseYaw = yaw; g.maxHealth = 220f; g.Health = g.maxHealth; g._elev = 20f;
            Transform r = root.transform;
            GameObject P(Transform p, string n, Mesh m, Vector3 at, Vector3 sc, Color c, Vector3 eu = default, float sm = 0.3f, float me = 0.25f)
                => ProceduralModels.Part(p, n, m, at, sc, c, eu, false, sm, me);
            // Крестовина-основание, мешки по кругу, тумба
            for (int k = 0; k < 4; k++) P(r, "Leg", MeshUtil.Bevel(0.02f), Quaternion.Euler(0, 45 + k * 90, 0) * new Vector3(0, 0.1f, 0.8f), new Vector3(0.18f, 0.14f, 1.6f), dark, new Vector3(0, 45 + k * 90, 0));
            for (int k = 0; k < 10; k++)
            {
                float a = k * 36f;
                P(r, "Sandbag", MeshUtil.Bevel(0.12f), Quaternion.Euler(0, a, 0) * new Vector3(0, 0.25f, 2.3f), new Vector3(1.4f, 0.5f, 0.6f), new Color(0.5f, 0.45f, 0.33f), new Vector3(0, a, 0), 0.05f, 0f);
            }
            P(r, "Pedestal", MeshUtil.Cylinder(10), new Vector3(0, 0.45f, 0), new Vector3(0.35f, 0.8f, 0.35f), paint);
            var mount = new GameObject("Mount").transform; mount.SetParent(r, false); mount.localPosition = new Vector3(0, 0.9f, 0);
            g._mount = mount;
            P(mount, "Cradle", MeshUtil.Bevel(0.04f), new Vector3(0, 0.15f, 0), new Vector3(0.5f, 0.3f, 0.5f), paint);
            if (de) P(mount, "Shield", MeshUtil.Bevel(0.02f), new Vector3(0, 0.5f, 0.35f), new Vector3(1.3f, 0.8f, 0.04f), paint);
            P(mount, "Seat", MeshUtil.Bevel(0.03f), new Vector3(0, -0.1f, -0.75f), new Vector3(0.4f, 0.08f, 0.35f), dark);
            var barrel = new GameObject("Barrel").transform; barrel.SetParent(mount, false); barrel.localPosition = new Vector3(0, 0.45f, 0.1f);
            g._barrel = barrel;
            P(barrel, "Receiver", MeshUtil.Bevel(0.04f), new Vector3(0, 0, -0.15f), new Vector3(0.28f, 0.26f, 0.9f), steel);
            P(barrel, "Tube", MeshUtil.Cylinder(8), new Vector3(0, 0, 0.95f), new Vector3(0.07f, 1.5f, 0.07f), steel, new Vector3(90, 0, 0), 0.4f, 0.6f);
            P(barrel, "Jacket", MeshUtil.Cylinder(10), new Vector3(0, 0, 0.55f), new Vector3(0.12f, 0.6f, 0.12f), dark, new Vector3(90, 0, 0));
            P(barrel, "Brake", MeshUtil.Cylinder(8), new Vector3(0, 0, 1.7f), new Vector3(0.11f, 0.16f, 0.11f), dark, new Vector3(90, 0, 0));
            P(barrel, "Drum", MeshUtil.Cylinder(12), new Vector3(0.22f, 0.05f, -0.1f), new Vector3(0.3f, 0.1f, 0.3f), dark, new Vector3(0, 0, 90));
            P(barrel, "RingSight", MeshUtil.Cylinder(16), new Vector3(0, 0.3f, 0.2f), new Vector3(0.36f, 0.02f, 0.36f), steel, new Vector3(90, 0, 0));
            var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(barrel, false); muzzle.localPosition = new Vector3(0, 0, 1.8f);
            g._muzzle = muzzle;
            var fig = new GameObject("Crew").transform; fig.SetParent(mount, false); fig.localPosition = new Vector3(0, -0.9f, -1.05f);
            Color uni = de ? new Color(0.36f, 0.37f, 0.32f) : new Color(0.42f, 0.4f, 0.28f);
            P(fig, "Legs", MeshUtil.Bevel(0.05f), new Vector3(0, 0.45f, 0), new Vector3(0.34f, 0.9f, 0.24f), uni, default, 0.1f, 0f);
            P(fig, "Body", MeshUtil.Bevel(0.06f), new Vector3(0, 1.15f, 0.05f), new Vector3(0.42f, 0.55f, 0.28f), uni, new Vector3(10, 0, 0), 0.1f, 0f);
            P(fig, "Head", MeshUtil.Ico(), new Vector3(0, 1.58f, 0.1f), new Vector3(0.22f, 0.26f, 0.24f), new Color(0.75f, 0.6f, 0.5f), default, 0.2f, 0f);
            P(fig, "Helmet", MeshUtil.Ico(), new Vector3(0, 1.66f, 0.1f), new Vector3(0.3f, 0.18f, 0.32f), de ? new Color(0.3f, 0.32f, 0.28f) : new Color(0.28f, 0.33f, 0.22f), default, 0.3f, 0f);
            fig.gameObject.SetActive(false);
            g._crewFig = fig;
            var groups = new HashSet<Transform> { r, mount, barrel, fig };
            foreach (var gr in new[] { barrel, mount, fig, r }) MeshMerge.Merge(gr, groups);
            AircraftModels.SetupLod(root);
            var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.9f, 0); bc.size = new Vector3(1.2f, 1.6f, 1.2f);
            foreach (var c in root.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
            g._shot = SoundSynth.Gunshot(de ? "flak38" : "dshk", WeaponCategory.MachineGun);
            g._think = Random.Range(0.5f, 2f);
            g.ApplyAim();
            All.Add(g);
            return g;
        }

        const float AAInterval = 0.09f, AARange = 800f, AABullet = 900f;
        Aircraft _aaTgt;
        int _aaShots;

        /// <summary>Очередь зенитки: пуля мгновенная, урон только самолётам (по земле не стреляет).</summary>
        void FireAA(Vector3 dir)
        {
            if (_reload > 0) return;
            _reload = AAInterval; _recoil = Mathf.Min(1f, _recoil + 0.15f); _aaShots++;
            Vector3 o = _muzzle.position;
            dir = Quaternion.AngleAxis(Random.Range(-0.5f, 0.5f), Vector3.up) * Quaternion.AngleAxis(Random.Range(-0.5f, 0.5f), _barrel.right) * dir;
            Vector3 end = o + dir * AARange;
            if (Physics.Raycast(o, dir, out var h, AARange, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore))
            {
                end = h.point;
                var ac = h.collider.GetComponentInParent<Aircraft>();
                if (ac && ac.Alive && ac.Team != Team) { ac.TakeDamage(15f, CrewActorId, h.point); if (FxPool.I) FxPool.I.BrickChips(h.point, h.normal); }
            }
            if (FxPool.I) { FxPool.I.Tracer(o, end, AABullet); if ((_aaShots & 1) == 0) FxPool.I.MuzzleFlashAt(o, dir); }
            BotAudio.ShotAt(_shot, o, PlayerAboard ? 0.8f : 1f, 0.05f);
            if ((_aaShots & 3) == 0) CombatWorld.EmitNoise(o, 300f, CrewActorId, Team, 0);
            if (PlayerAboard) Shake(0.03f);
        }

        void PlayerInputAA(float dt)
        {
            var pc = PlayerBridge.Controller;
            if (!pc || pc.Gunning != this) { PlayerAboard = false; return; }
            var inp = pc.Input;
            if (inp.interactPressed && Time.frameCount > _enterFrame + 1) { ExitPlayer(false, -1); return; }
            Zoomed = inp.aimHeld;
            float sens = Zoomed ? 0.35f : 0.8f;
            _yawOff = Mathf.Repeat(_yawOff + inp.lookDelta.x * sens + 180f, 360f) - 180f;
            _elev = Mathf.Clamp(_elev + inp.lookDelta.y * sens, -5f, 85f);
            ApplyAim();
            // Пули сходятся в точке под центром прицела (камера чуть выше ствола) — кольцо прицела показывает честно
            Quaternion aim = _barrel.rotation;
            Vector3 camPos = _barrel.position - aim * Vector3.forward * (Zoomed ? 0.9f : 2.6f) + Vector3.up * (Zoomed ? 0.35f : 0.8f);
            Vector3 conv = camPos + aim * Vector3.forward * 350f;
            if (inp.fireHeld) FireAA((conv - _muzzle.position).normalized);
        }

        /// <summary>Бот-зенитчик: ближайший вражеский самолёт в воздухе, упреждение по скорости пули.</summary>
        void ThinkAA(float dt)
        {
            _think -= dt;
            if (_think <= 0)
            {
                _think = 0.5f; _aaTgt = null; float bd = AARange;
                foreach (var a in Aircraft.All)
                {
                    if (!a || !a.Alive || !a.Airborne || a.Team == Team) continue;
                    float d = Vector3.Distance(a.transform.position, _muzzle.position);
                    if (d < bd) { bd = d; _aaTgt = a; }
                }
            }
            Vector3 want;
            if (_aaTgt && _aaTgt.Alive)
            {
                Vector3 tp = _aaTgt.transform.position;
                float t = Vector3.Distance(tp, _muzzle.position) / AABullet;
                want = tp + _aaTgt.Velocity * t * 2.2f - _mount.position;    // пуля мгновенная — упреждение «на глаз», с ошибкой
            }
            else want = Quaternion.Euler(0, _baseYaw, 0) * new Vector3(0, 0.4f, 1f);
            Vector3 l = Quaternion.Inverse(transform.rotation) * want;
            float yawT = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg, elT = Mathf.Clamp(Mathf.Atan2(l.y, new Vector2(l.x, l.z).magnitude) * Mathf.Rad2Deg, -5f, 85f);
            _yawOff = Mathf.MoveTowardsAngle(_yawOff, yawT, 70f * dt);
            _elev = Mathf.MoveTowards(_elev, elT, 50f * dt);
            if (_aaTgt && Mathf.Abs(Mathf.DeltaAngle(_yawOff, yawT)) < 4f && Mathf.Abs(_elev - elT) < 4f)
            {
                ApplyAim();
                FireAA(MuzzleDir);
            }
        }

        void LateAA(PlayerController pc)
        {
            pc.transform.SetPositionAndRotation(SeatPos, Quaternion.Euler(0, transform.eulerAngles.y + _yawOff, 0));
            Quaternion aim = _barrel.rotation;
            Vector3 camPos = _barrel.position - aim * Vector3.forward * (Zoomed ? 0.9f : 2.6f) + Vector3.up * (Zoomed ? 0.35f : 0.8f);
            pc.cameraPivot.SetPositionAndRotation(camPos, aim);
            pc.worldCamera.fieldOfView = Mathf.Lerp(pc.worldCamera.fieldOfView, Zoomed ? 40f : pc.BaseFov, Time.deltaTime * 10f);
        }

        void OnDestroy()
        {
            All.Remove(this);
            ReleaseBuddy();
            if (PlayerAboard) ExitPlayer(false, -1);
        }

        void ApplyAim()
        {
            _mount.localRotation = Quaternion.Euler(0, _yawOff, 0);
            _barrel.localRotation = Quaternion.Euler(-_elev, 0, 0);
            if (_tube)
            {
                // Откат ствола в люльке (противооткатные устройства): резко назад на 1,1 м, плавный накат
                float back = _recoil > 0.75f ? Mathf.InverseLerp(1f, 0.75f, _recoil) : Mathf.SmoothStep(0f, 1f, _recoil / 0.75f);
                _tube.localPosition = -Vector3.forward * back * 1.1f;
                // Всё орудие немного отъезжает назад и подкатывается обратно
                if (!IsAA && _basePos != Vector3.zero)
                {
                    Vector3 fwd = _mount.forward; fwd.y = 0; fwd.Normalize();
                    float slide = Mathf.Sin(Mathf.Clamp01(_slide) * Mathf.PI * 0.5f) * 0.55f;
                    Vector3 p = _basePos - fwd * slide; p.y = TerrainH(p);
                    transform.position = p;
                }
            }
            else _barrel.localPosition = new Vector3(0, 0.45f, 0.1f) - Vector3.forward * Mathf.Sin(Mathf.Clamp01(_recoil) * Mathf.PI * 0.5f) * 0.12f;
        }
        float _slide;

        static float TerrainH(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t ? t.SampleHeight(p) + t.transform.position.y : 0f;
        }

        // ---------------- Цикл ----------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (_dead) { if (Time.time > _wreckUntil) Destroy(gameObject); return; }
            _reload -= dt;
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * (IsAA ? 6f : 0.6f));
            _slide = Mathf.MoveTowards(_slide, 0f, dt * 0.7f);
            LoadUpdate(dt);
            KeepCrew();
            if (PlayerAboard) { if (IsAA) PlayerInputAA(dt); else PlayerInput(dt); }
            else if (crewBot > 0) { if (IsAA) ThinkAA(dt); else ThinkAI(dt); }
            else Recruit();
            ApplyAim();
            if (_crewFig) { bool show = crewBot > 0; if (_crewFig.gameObject.activeSelf != show) _crewFig.gameObject.SetActive(show); }
            if (!IsAA && _crewFig && crewBot <= 0 && _figAway) ResetFigure();
        }

        // ================= Снаряды и заряжание =================
        // Боекомплекта нет: каждый выстрел — снаряд из кучи за орудиями. Игрок приносит снаряд (E у кучи), у орудия
        // нажимает R — 5 с заряжания. Бот-наводчик сам ходит за снарядом (медленнее со снарядом в руках).

        public bool Loaded { get; private set; } = true;
        public float LoadProgress => _loading ? Mathf.Clamp01(1f - _loadT / LoadTime) : 0f;
        public bool Loading => _loading;
        const float LoadTime = 5f;
        bool _loading; float _loadT; bool _loadByPlayer;

        /// <summary>Игрок со снарядом рядом с орудием нажал R.</summary>
        public bool StartPlayerLoad(PlayerController pc)
        {
            if (IsAA || Loaded || _loading || !pc || !pc.CarryingShell || !Alive) return false;
            pc.CarryingShell = false;
            _loading = true; _loadT = LoadTime; _loadByPlayer = true;
            BotAudio.PlayAt(SoundSynth.Hatch(), transform.position + Vector3.up, 0.7f);
            return true;
        }

        /// <summary>Нанятый заряжающий принёс снаряд: 5 с заряжания без участия игрока.</summary>
        public bool StartHelperLoad()
        {
            if (IsAA || Loaded || _loading || !Alive) return false;
            _loading = true; _loadT = LoadTime; _loadByPlayer = false;
            BotAudio.PlayAt(SoundSynth.Hatch(), transform.position + Vector3.up, 0.7f);
            return true;
        }

        /// <summary>Нанятый за монеты заряжающий (носит снаряды из кучи и заряжает орудие).</summary>
        public ArtilleryLoader Loader { get; private set; }
        public bool HireLoader()
        {
            if (IsAA || Loader || !Alive) return false;
            if (!Shop.Spend(Shop.LoaderPrice)) return false;
            Loader = ArtilleryLoader.Create(this, faction);
            return true;
        }

        void LoadUpdate(float dt)
        {
            if (!_loading) return;
            if (_loadByPlayer)
            {
                var pc = PlayerBridge.Controller;
                // Отошёл дальше 6 м — снаряд остаётся у орудия, заряжание продолжится, когда вернётся
                if (!pc || (pc.Gunning != this && Vector3.Distance(pc.transform.position, transform.position) > 6f * Scale)) return;
            }
            _loadT -= dt;
            if (_loadT <= 0) { _loading = false; Loaded = true; BotAudio.PlayAt(SoundSynth.BoltForward(), transform.position + Vector3.up, 0.9f); }
        }

        // ---- Бот носит снаряды ----
        enum Fetch : byte { None, ToPile, ToGun }
        Fetch _fetch; bool _figAway; float _walkPh; Transform _figShell, _figHands;

        Vector3 SeatWorld => transform.TransformPoint(new Vector3(-0.6f, 0.02f, -1.2f));

        void ResetFigure()
        {
            _figAway = false; _fetch = Fetch.None;
            _crewFig.localPosition = new Vector3(-0.6f, 0, -1.1f); _crewFig.localRotation = Quaternion.identity;
            if (_figShell) Destroy(_figShell.gameObject);
        }

        /// <summary>Шаг ИИ-логистики: нет снаряда → идём к куче → берём → несём → 5 с заряжания.</summary>
        bool BotAmmo(float dt)
        {
            if (IsAA || Loaded) return false;
            if (_loading) return true;
            if (Loader && _fetch == Fetch.None) return true;      // снаряды носит нанятый заряжающий
            var pile = AmmoPile.Of(Team);
            if (_fetch == Fetch.None)
            {
                if (!pile || pile.Count <= 0) return true;          // ждём грузовик
                _fetch = Fetch.ToPile; _figAway = true;
            }
            Vector3 target = _fetch == Fetch.ToPile ? (pile ? pile.transform.position + (transform.position - pile.transform.position).normalized * 1.6f : SeatWorld) : SeatWorld;
            float sp = _fetch == Fetch.ToGun ? 1.3f : 2.6f;              // со снарядом — медленнее
            Vector3 fp = _crewFig.position, d = target - fp; d.y = 0;
            if (d.magnitude > 0.4f)
            {
                _crewFig.rotation = Quaternion.LookRotation(d.normalized);
                _walkPh += dt * sp * 4f;
                Vector3 np = fp + d.normalized * Mathf.Min(sp * dt, d.magnitude); np.y = TerrainH(np) + Mathf.Abs(Mathf.Sin(_walkPh)) * 0.05f;
                _crewFig.position = np;
                return true;
            }
            if (_fetch == Fetch.ToPile)
            {
                if (!pile || !pile.Take()) { _fetch = Fetch.ToGun; return true; }     // кучу растащили — назад ни с чем
                if (!_figHands) { _figHands = new GameObject("Hands").transform; _figHands.SetParent(_crewFig, false); _figHands.localPosition = new Vector3(0, 1.0f, 0.4f); }
                _figShell = LogiModels.Shell(_figHands, Vector3.zero, new Vector3(0, 90, 0));
                _fetch = Fetch.ToGun;
                return true;
            }
            // Принёс: снаряд в казённик, 5 с
            bool had = _figShell;
            ResetFigure();
            if (had && !Loaded && !_loading) { _loading = true; _loadT = LoadTime; _loadByPlayer = false; }
            else if (had) { var bp = AmmoPile.Of(Team); if (bp) bp.Add(); }   // заряжающий успел раньше — снаряд обратно в кучу
            return true;
        }

        // ---------------- Стрельба ----------------

        Vector3 MuzzleDir => _barrel.forward;

        // Баллистика «как у гаубицы, но быстро»: у всех (боты и игрок) одинаковая скорость снаряда и усиленная
        // гравитация — снаряд уходит почти вертикально вверх и падает на цель почти отвесно за ~4 с.
        public const float ShellSpeed = 570f, ShellGravity = 30f;
        static float G => 9.81f * ShellGravity;

        /// <summary>Навесное решение: угол возвышения (высокая траектория) для точки на расстоянии x и разнице высот y.</summary>
        /// <summary>
        /// Ствол всегда задран на 60° (наклонён вперёд на 30° от вертикали); дальность задаёт скорость снаряда
        /// (переменный заряд) — у ботов и у игрока по одной и той же баллистике.
        /// </summary>
        public const float FixedElev = 60f;
        float _shotSpeed = 300f;

        static bool SolveSpeed(float x, float y, out float speed)
        {
            float th = FixedElev * Mathf.Deg2Rad, c = Mathf.Cos(th);
            float den = 2f * c * c * (x * Mathf.Tan(th) - y);
            speed = 300f;
            if (den <= 0.01f || x < 1f) return false;
            speed = Mathf.Sqrt(G * x * x / den);
            return speed > 20f && speed < 1500f;
        }

        /// <summary>Повернуть ствол на точку (мгновенно — для выстрела) и вернуть направление ствола.</summary>
        bool AimAt(Vector3 target, out float yawOff, out float elev) => AimAt(target, out yawOff, out elev, out _);

        bool AimAt(Vector3 target, out float yawOff, out float elev, out float speed)
        {
            Vector3 o = _muzzle ? _muzzle.position : _mount.position;
            Vector3 d = target - o; float x = new Vector2(d.x, d.z).magnitude;
            yawOff = Mathf.Clamp(Mathf.DeltaAngle(_baseYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg), -MaxTraverse, MaxTraverse);
            elev = FixedElev;
            return SolveSpeed(x, d.y, out speed);
        }

        /// <summary>Выстрел в точку с шансом промаха (промах — снаряд ложится в 30–90 м от точки).</summary>
        void FireAt(Vector3 target, float missChance)
        {
            if (_reload > 0) return;
            Vector3 aim = target + new Vector3(Random.Range(-2.5f, 2.5f), 0, Random.Range(-2.5f, 2.5f));
            if (Random.value < missChance) { var rc = Random.insideUnitCircle.normalized * Random.Range(30f, 90f); aim += new Vector3(rc.x, 0, rc.y); }
            aim.y = TerrainH(aim);
            if (!AimAt(aim, out var yo, out var el, out var sp)) return;
            _yawOff = yo; _elev = el; _shotSpeed = sp; ApplyAim();
            FireBarrel();
        }

        void FireBarrel()
        {
            if (_reload > 0 || !Loaded) return;
            Loaded = false;
            _reload = ReloadTime; _recoil = 1f; _slide = 1f;
            Vector3 o = _muzzle.position, dir = MuzzleDir;
            var p = Projectile.Fire(Projectile.Type.Shell, o + dir * 0.3f, dir, _shotSpeed, ShellGravity, CrewActorId, Team, BlastRadius, BlastDamage, ArmorDirect, ArmorSplash, transform);
            p.Life = 30f;
            p.KindOverride = Explosions.Kind.Artillery;
            if (FxPool.I)
            {
                FxPool.I.MuzzleFlashAt(o, dir); FxPool.I.MuzzleFlashAt(o + dir * 0.8f, dir); FxPool.I.Explosion(o + dir * 1.2f);
                // Дымок у дула и пыль под орудием
                for (int k = 0; k < 4; k++) FxPool.I.Smoke(o + dir * (0.8f + k * 1.2f) + Random.insideUnitSphere * 0.6f);
                FxPool.I.Smoke(o - dir * 0.5f);
                FxPool.I.Smoke(transform.position + Vector3.up * 0.4f); FxPool.I.Smoke(transform.position + transform.right * 2f);
            }
            // Громко: выстрел пушки + раскат, сильная тряска у расчёта и тех, кто рядом
            // Вдвое громче обычного: свой источник с большой «зоной полной громкости» и слышимостью на всю карту
            if (!_loud)
            {
                _loud = gameObject.AddComponent<AudioSource>();
                _loud.spatialBlend = 1f; _loud.minDistance = 45f; _loud.maxDistance = 1400f; _loud.rolloffMode = AudioRolloffMode.Logarithmic; _loud.dopplerLevel = 0f; _loud.playOnAwake = false;
            }
            _loud.transform.position = o;
            _loud.PlayOneShot(_shot, 1f); _loud.PlayOneShot(SoundSynth.ShellExplosion(), 1f);
            BotAudio.PlayAt(SoundSynth.ShellExplosion(), o, 1f, 0.05f);
            CombatWorld.EmitNoise(o, 600f, CrewActorId, Team, 1);
            CameraEffects.Explosion(o, 20f);             // тряска на ~120 м вокруг
            if (PlayerAboard) Shake(1f);
        }

        void Shake(float t)
        {
            var pc = PlayerBridge.Controller; if (!pc || !pc.worldCamera) return;
            var fx = pc.worldCamera.GetComponent<CameraEffects>();
            if (fx) fx.AddTrauma(t);
        }

        /// <summary>Шанс промаха игрока: 60%, со специализацией «Артиллерист» — 5%.</summary>
        static float PlayerMissChance => Shop.HasPerk(Shop.PerkArtillerist) ? 0.05f : 0.6f;

        // ---------------- Урон ----------------

        public void TakeDamage(float amount, int source, Vector3 point)
        {
            // Орудия и зенитки неуничтожимы: снаряды лишь высекают искры (расчёт при этом уязвим).
            // Исключение — сетевая кукла у клиента, которую «добивает» сообщение хоста (amount ≥ 1e6).
            if (amount < 1e6f) { if (FxPool.I && Random.value < 0.3f) FxPool.I.BrickChips(point, (point - transform.position).normalized); return; }
            if (!Alive || amount <= 0) return;
            if (source >= 0 && CombatWorld.Initialized && source < CombatWorld.Actors.Length && CombatWorld.Actors[source].team == Team) return;
            Health -= amount; LastHitTime = Time.time;
            HitFeedback.Report(source, Health <= 0);
            if (PlayerAboard) Shake(0.2f);
            if (FxPool.I) FxPool.I.BrickChips(point, (point - transform.position).normalized);
            if (Health <= 0) Kill(source);
        }

        void Kill(int source)
        {
            Health = 0; _dead = true;
            if (PlayerAboard) ExitPlayer(true, source);
            KillCrew(source);
            Vector3 c = transform.position + Vector3.up;
            if (FxPool.I) { FxPool.I.Explosion(c); FxPool.I.Explosion(c + Vector3.up); }
            BotAudio.PlayAt(SoundSynth.ShellExplosion(), c, 1f, 0.06f);
            _barrel.localRotation = Quaternion.Euler(Random.Range(5f, 15f), Random.Range(-20f, 20f), Random.Range(-10f, 10f));
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i]) mats[i] = ProceduralModels.Mat(Color.Lerp(mats[i].color, new Color(0.06f, 0.055f, 0.05f), 0.8f), 0.05f, 0.1f);
                r.sharedMaterials = mats;
            }
            if (_crewFig) _crewFig.gameObject.SetActive(false);
            MapProps.SmokeColumn(transform, c);
            _wreckUntil = Time.time + 60f;          // обломки исчезают через минуту
            Destroyed?.Invoke(this, source);
        }

        // ---------------- Игрок ----------------

        int _enterFrame;
        public bool CanEnter(PlayerController pc) => Alive && !PlayerAboard && pc && pc.Team == Team;
        public Vector3 SeatPos => IsAA ? _mount.TransformPoint(new Vector3(0, -_mount.localPosition.y + 0.02f, -1.05f)) : transform.TransformPoint(new Vector3(-0.6f, 0.02f, -1.2f));

        public void EnterPlayer(PlayerController pc)
        {
            if (!CanEnter(pc)) return;
            if (crewBot > 0) DismountCrew();
            ReleaseBuddy();
            PlayerAboard = true; _enterFrame = Time.frameCount; Zoomed = false;
            _wantYaw = _yawOff; _wantElev = _elev;
            pc.EnterArtillery(this);
            BotAudio.PlayAt(SoundSynth.Hatch(), transform.position + Vector3.up, 0.5f);
        }

        public void ExitPlayer(bool killed, int killer)
        {
            if (!PlayerAboard) return;
            PlayerAboard = false; Zoomed = false;
            var pc = PlayerBridge.Controller;
            if (!pc || pc.Gunning != this) return;
            Vector3 exit = transform.TransformPoint(new Vector3(-1.6f, 0, -1.4f)); exit.y = TerrainH(exit) + 0.1f;
            pc.ExitArtillery(exit, transform.eulerAngles.y);
            if (killed)
                CombatWorld.Damage.Enqueue(new DamageEvent { targetActorId = CombatWorld.PlayerActorId, sourceActorId = killer, amount = 9999f, explosive = true, hitPoint = exit, direction = Vector3.up });
        }

        void PlayerInput(float dt)
        {
            var pc = PlayerBridge.Controller;
            if (!pc || pc.Gunning != this) { PlayerAboard = false; return; }
            var inp = pc.Input;
            if (inp.interactPressed && Time.frameCount > _enterFrame + 1) { ExitPlayer(false, -1); return; }
            if (inp.reloadPressed && !Loaded && pc.CarryingShell) StartPlayerLoad(pc);
            bool wasZoomed = Zoomed;
            Zoomed = inp.aimHeld;
            if (Zoomed)
            {
                // ПКМ: слева снизу — 3D-вид сверху на курсор; мышь двигает курсор, орудие медленно доворачивается за ним,
                // ЛКМ — выстрел, когда навелось и заряжено
                if (!wasZoomed) AimCursor = ClampToSector(transform.position + Quaternion.Euler(0, _baseYaw + _yawOff, 0) * Vector3.forward * _manualRange);
                Quaternion yr = Quaternion.Euler(0, _baseYaw, 0);
                Vector3 c = AimCursor + (yr * Vector3.right * inp.lookDelta.x + yr * Vector3.forward * inp.lookDelta.y) * 2.6f;
                c = ClampToSector(c); c.y = TerrainH(c);
                AimCursor = c;
                if (AimAt(AimCursor, out var yo, out var el, out var csp)) { _wantYaw = yo; _wantElev = el; _shotSpeed = csp; }
                Vector3 d = AimCursor - transform.position; d.y = 0; PredictedRange = d.magnitude; _manualRange = Mathf.Clamp(PredictedRange, MinRange, MaxRange);
            }
            else
            {
                // Без ПКМ: ручная наводка мышью (цель наводки), орудие догоняет её с той же медленной скоростью
                // Без ПКМ: мышь X — поворот, мышь Y — дальность (заряд); ствол всегда 60°
                _wantYaw = Mathf.Clamp(_wantYaw + inp.lookDelta.x * 0.45f, -MaxTraverse, MaxTraverse);
                _manualRange = Mathf.Clamp(_manualRange + inp.lookDelta.y * 6f, MinRange, MaxRange);
                _wantElev = FixedElev;
                AimCursor = ClampToSector(transform.position + Quaternion.Euler(0, _baseYaw + _wantYaw, 0) * Vector3.forward * _manualRange);
                if (AimAt(AimCursor, out _, out _, out var msp)) _shotSpeed = msp;
                PredictedRange = _manualRange;
            }
            // Маховики наводки — медленно
            _yawOff = Mathf.MoveTowardsAngle(_yawOff, _wantYaw, TraverseSpeed * dt);
            _elev = Mathf.MoveTowards(_elev, _wantElev, ElevSpeed * dt);
            Aligned = Mathf.Abs(Mathf.DeltaAngle(_yawOff, _wantYaw)) < 0.3f && Mathf.Abs(_elev - _wantElev) < 0.3f;
            if (inp.fireHeld && _reload <= 0 && Loaded && Aligned)
            {
                if (Zoomed) FireAt(AimCursor, PlayerMissChance);
                else if (Random.value < PlayerMissChance)
                {
                    float keepY = _yawOff, keepS = _shotSpeed;
                    _yawOff += Random.Range(-4f, 4f); _shotSpeed *= Random.Range(0.9f, 1.1f);
                    ApplyAim(); FireBarrel(); _yawOff = keepY; _shotSpeed = keepS;
                }
                else FireBarrel();
            }
        }

        /// <summary>Орудие навелось на заданную точку (для надписи «Готов к выстрелу»).</summary>
        public bool Aligned { get; private set; }
        float _manualRange = 350f;
        const float TraverseSpeed = 7f, ElevSpeed = 5f;

        /// <summary>Курсор огня по карте (режим ПКМ) — мировая точка на земле.</summary>
        public Vector3 AimCursor { get; private set; }
        public float BaseYaw => _baseYaw;
        public const float SectorDeg = MaxTraverse, MaxRange = 1100f, MinRange = 40f;

        Vector3 ClampToSector(Vector3 p)
        {
            Vector3 o = transform.position;
            Vector3 d = p - o; d.y = 0;
            float dist = Mathf.Clamp(d.magnitude, MinRange, MaxRange);
            float ang = Mathf.Clamp(Mathf.DeltaAngle(_baseYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg), -MaxTraverse, MaxTraverse);
            Vector3 r = o + Quaternion.Euler(0, _baseYaw + ang, 0) * Vector3.forward * dist;
            float lim = MapGenerator.Half - 5f;
            r.x = Mathf.Clamp(r.x, -lim, lim); r.z = Mathf.Clamp(r.z, -lim, lim);
            return r;
        }

        void LateUpdate()
        {
            if (!PlayerAboard) return;
            var pc = PlayerBridge.Controller; if (!pc || pc.Gunning != this) return;
            if (IsAA) { LateAA(pc); return; }
            if (Zoomed) MiniView.Request(AimCursor, 150f, 38f, Quaternion.Euler(0, _baseYaw, 0) * Vector3.forward);
            pc.transform.SetPositionAndRotation(SeatPos, Quaternion.Euler(0, _baseYaw + _yawOff, 0));
            Quaternion yawRot = Quaternion.Euler(0, _baseYaw + _yawOff, 0);
            // Камера всегда у орудия (сверху сзади) — слушатель звука не «летает» над картой
            Vector3 camPos = _mount.position + Vector3.up * 2.6f - yawRot * Vector3.forward * 7.5f;
            Quaternion rot = yawRot * Quaternion.Euler(6f, 0, 0);
            pc.worldCamera.fieldOfView = Mathf.Lerp(pc.worldCamera.fieldOfView, pc.BaseFov, Time.deltaTime * 10f);
            pc.cameraPivot.SetPositionAndRotation(camPos, rot);
        }

        // ---------------- Расчёт-бот ----------------

        static EntityManager? Em()
        {
            var w = World.DefaultGameObjectInjectionWorld;
            return w != null && w.IsCreated ? w.EntityManager : null;
        }

        static Entity BotEntity(int actorId) =>
            BotRegistry.ActorToEntity.IsCreated && actorId > 0 && actorId < BotRegistry.ActorToEntity.Length ? BotRegistry.ActorToEntity[actorId] : Entity.Null;

        int _buddy = -1; float _buddyUntil, _buddyRetry, _buddyRefresh;

        void ReleaseBuddy()
        {
            if (_buddy >= 0) { FrontPlanner.Release(_buddy); Tank.Claimed.Remove(_buddy); }
            _buddy = -1;
        }

        void Recruit()
        {
            if (Net.NetSession.IsClient || !Alive || !CombatWorld.Initialized) { ReleaseBuddy(); return; }
            var pc = PlayerBridge.Controller;
            if (pc && pc.Team == Team && !pc.Vehicle && !pc.Flying && !pc.Gunning && PlayerBridge.Health && PlayerBridge.Health.Alive &&
                Vector3.Distance(pc.transform.position, transform.position) < 18f) { ReleaseBuddy(); return; }
            float now = Time.time;
            var actors = CombatWorld.Actors;
            var em = Em(); if (em == null) return;
            if (_buddy >= 0)
            {
                bool ok = _buddy < actors.Length && actors[_buddy].alive && now < _buddyUntil;
                if (!ok) { ReleaseBuddy(); _buddyRetry = now + 2f; return; }
                if (Vector3.Distance(actors[_buddy].position, SeatPos) < 3.5f)
                {
                    int id = _buddy; ReleaseBuddy();
                    var e = BotEntity(id);
                    if (e != Entity.Null && em.Value.Exists(e) && !VehicleSeats.IsBoarded(em.Value, e))
                    {
                        VehicleSeats.Board(em.Value, e, 7000 + index, 1);
                        crewBot = id; _think = 1.5f;
                    }
                    return;
                }
                if (now > _buddyRefresh) { _buddyRefresh = now + 0.8f; var sp = SeatPos; FrontPlanner.Force(_buddy, new float2(sp.x, sp.z)); }
                return;
            }
            if (now < _buddyRetry) return;
            _buddyRetry = now + 1.5f;
            float best = 100f; int pick = -1;
            for (int i = 1; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team != Team || Net.NetSession.IsRemoteSlot(a.actorId) || Tank.Claimed.Contains(a.actorId)) continue;
                float d = Vector3.Distance(a.position, transform.position);
                if (d >= best) continue;
                var e = BotEntity(a.actorId);
                if (e == Entity.Null || !em.Value.Exists(e) || VehicleSeats.IsBoarded(em.Value, e)) continue;
                best = d; pick = a.actorId;
            }
            if (pick < 0) return;
            _buddy = pick; _buddyUntil = now + 30f; _buddyRefresh = now + 0.8f; Tank.Claimed.Add(pick);
            var s = SeatPos; FrontPlanner.Force(pick, new float2(s.x, s.z));
        }

        /// <summary>Бот стоит у орудия открыто: его видят и могут подстрелить (в отличие от экипажа танка).</summary>
        void KeepCrew()
        {
            if (crewBot <= 0) return;
            var em = Em(); if (em == null) return;
            var m = em.Value; var e = BotEntity(crewBot);
            var actors = CombatWorld.Actors;
            bool alive = actors.IsCreated && crewBot < actors.Length && actors[crewBot].alive;
            if (e == Entity.Null || !m.Exists(e) || !VehicleSeats.IsBoarded(m, e) || !alive)
            {
                if (e != Entity.Null && m.Exists(e)) VehicleSeats.Unboard(m, e);
                crewBot = -1; return;
            }
            if (m.HasComponent<LocalTransform>(e))
            {
                var lt = m.GetComponentData<LocalTransform>(e);
                lt.Position = IsAA || !_crewFig ? SeatPos : _crewFig.position;
                lt.Rotation = quaternion.RotateY(math.radians(_baseYaw + _yawOff));
                m.SetComponentData(e, lt);
            }
            if (m.HasComponent<BotMotion>(e)) { var mo = m.GetComponentData<BotMotion>(e); mo.velocity = float3.zero; mo.speed = 0; m.SetComponentData(e, mo); }
        }

        void DismountCrew()
        {
            var em = Em(); int id = crewBot; crewBot = -1;
            if (em == null) return;
            var e = BotEntity(id);
            if (e == Entity.Null || !em.Value.Exists(e)) return;
            if (em.Value.HasComponent<LocalTransform>(e))
            {
                var lt = em.Value.GetComponentData<LocalTransform>(e);
                Vector3 p = transform.TransformPoint(new Vector3(1.6f, 0, -1.4f)); p.y = TerrainH(p) + 0.05f;
                lt.Position = p; em.Value.SetComponentData(e, lt);
            }
            VehicleSeats.Unboard(em.Value, e);
        }

        void KillCrew(int killer)
        {
            if (crewBot <= 0) return;
            int id = crewBot;
            DismountCrew();
            CombatWorld.Damage.Enqueue(new DamageEvent { targetActorId = id, sourceActorId = killer, amount = 9999f, explosive = true, hitPoint = transform.position, direction = Vector3.up });
        }

        // ---------------- ИИ: выбор цели и пристрелка ----------------

        Tank _tgtTank; int _tgtActor = -1; Vector3 _tgtPos, _aimPoint;
        float _wantYaw, _wantElev; bool _hasSolution;

        void ThinkAI(float dt)
        {
            if (BotAmmo(dt)) return;                 // нет снаряда — сходить к куче и зарядить
            _think -= dt;
            if (_think <= 0)
            {
                _think = 1.2f;
                PickTarget();
                _hasSolution = false;
                if (_tgtTank || _tgtActor >= 0)
                {
                    _aimPoint = _tgtPos;
                    if (_tgtTank) _aimPoint += _tgtTank.transform.forward * _tgtTank.Speed * 3.5f;   // упреждение на время полёта
                    if (AimAt(_aimPoint, out var yo, out var el, out var asp) && Mathf.Abs(Mathf.DeltaAngle(_baseYaw, _baseYaw + yo)) <= MaxTraverse)
                    { _wantYaw = yo; _wantElev = el; _shotSpeed = asp; _hasSolution = true; }
                }
            }
            // Наводка с ограниченной скоростью (маховики)
            _yawOff = Mathf.MoveTowardsAngle(_yawOff, _hasSolution ? _wantYaw : 0f, 14f * dt);
            _elev = FixedElev;
            if (_hasSolution && _reload <= 0 && Mathf.Abs(Mathf.DeltaAngle(_yawOff, _wantYaw)) < 0.6f && Mathf.Abs(_elev - _wantElev) < 0.6f)
            {
                FireAt(_aimPoint, 0.7f);          // боты-артиллеристы промахиваются в 70% случаев
                _think = Mathf.Min(_think, 0.5f);
            }
        }

        /// <summary>
        /// Цель — вражеская техника (кроме самолётов) или скопление пехоты противника (3+ бойца в 14 м),
        /// дальность 120–900 м, в секторе обстрела; рядом с целью не должно быть своих.
        /// </summary>
        void PickTarget()
        {
            Vector3 pos = transform.position;
            var actors = CombatWorld.Actors;
            if (_tgtTank && (!_tgtTank.Alive || !InSector(_tgtTank.transform.position) || FriendsNear(_tgtTank.transform.position))) _tgtTank = null;
            if (_tgtTank) { _tgtPos = _tgtTank.transform.position; return; }
            float bt = 900f;
            foreach (var t in Tank.All)
            {
                if (!t || !t.Alive || t.Team == Team) continue;
                Vector3 tp = t.transform.position;
                float d = Vector3.Distance(tp, pos);
                if (d < 120f || d > bt || !InSector(tp) || FriendsNear(tp)) continue;
                bt = d; _tgtTank = t;
            }
            if (_tgtTank) { _tgtActor = -1; _tgtPos = _tgtTank.transform.position; return; }
            if (!actors.IsCreated || actors.Length == 0) { _tgtActor = -1; return; }
            // Держим прежнюю цель, если она жива и всё ещё в скоплении
            if (_tgtActor >= 0 && _tgtActor < actors.Length && actors[_tgtActor].alive && Random.value < 0.75f)
            {
                Vector3 tp = actors[_tgtActor].position;
                if (InSector(tp) && !FriendsNear(tp) && EnemiesNear(tp) >= 3) { _tgtPos = tp; return; }
            }
            _tgtActor = -1; int bestCount = 2;
            for (int k = 0; k < 14; k++)
            {
                var a = actors[Random.Range(0, actors.Length)];
                if (!a.alive || a.team == Team || Aircraft.IsAirCrew(a.actorId) || BaseZone.Near(a.position, a.team, 90f)) continue;
                Vector3 tp = a.position;
                float d = Vector3.Distance(tp, pos);
                if (d < 120f || d > 900f || !InSector(tp)) continue;
                int n = EnemiesNear(tp);
                if (n <= bestCount || FriendsNear(tp)) continue;
                bestCount = n; _tgtActor = a.actorId; _tgtPos = tp;
            }
        }

        bool InSector(Vector3 p)
        {
            Vector3 d = p - transform.position;
            return Mathf.Abs(Mathf.DeltaAngle(_baseYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg)) <= MaxTraverse;
        }

        bool FriendsNear(Vector3 p)
        {
            var actors = CombatWorld.Actors;
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team != Team) continue;
                if (((Vector3)a.position - p).sqrMagnitude < 16f * 16f) return true;
            }
            return false;
        }

        int EnemiesNear(Vector3 p)
        {
            var actors = CombatWorld.Actors; int n = 0;
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team == Team) continue;
                if (((Vector3)a.position - p).sqrMagnitude < 14f * 14f) n++;
            }
            return n;
        }
    }

    /// <summary>
    /// Орудия матча: батарея за базой каждой стороны (рядами по 6, интервал 14 м), стволом к центру карты.
    /// Количество — ползунок «Количество орудий» в «Новой игре». Разбитое орудие возвращается через 45 с.
    /// </summary>
    public class ArtilleryManager : MonoBehaviour
    {
        public int gunsGermany = 1, gunsUssr = 1;
        public int aaPerTeam = 0;                 // зенитные пулемёты у базы (если в матче есть самолёты)
        ArtilleryGun[,] _aa; float[,] _aaRespawn;
        public float respawnDelay = 45f, firstSpawnDelay = 4.5f;
        public MapTheme theme;
        public int worldLayer;
        public Vector3 baseGermany, baseUSSR;

        ArtilleryGun[,] _guns; float[,] _respawnAt; int _next;
        readonly Collider[] _ov = new Collider[16];

        Vector3 BaseOf(int team) => team == 0 ? baseGermany : baseUSSR;
        int CountOf(int team) => team == 0 ? gunsGermany : gunsUssr;

        void OnEnable() => ArtilleryGun.Destroyed += OnGunDestroyed;
        void OnDisable() => ArtilleryGun.Destroyed -= OnGunDestroyed;

        IEnumerator Start()
        {
            if (Net.NetSession.IsClient) { enabled = false; yield break; }
            int slots = Mathf.Max(1, Mathf.Max(gunsGermany, gunsUssr));
            _guns = new ArtilleryGun[2, slots]; _respawnAt = new float[2, slots];
            _aa = new ArtilleryGun[2, Mathf.Max(1, aaPerTeam)]; _aaRespawn = new float[2, Mathf.Max(1, aaPerTeam)];
            yield return new WaitForSeconds(firstSpawnDelay);
            for (int team = 0; team < 2; team++)
            {
                for (int s = 0; s < CountOf(team); s++) Spawn(team, s);
                for (int s = 0; s < aaPerTeam; s++) SpawnAA(team, s);
                if (CountOf(team) > 0) SpawnPile(team);
            }
        }

        // ---- Снабжение батареи ----
        readonly AmmoPile[] _piles = new AmmoPile[2];
        readonly SupplyTruck[] _trucks = new SupplyTruck[2];
        readonly float[] _truckT = { 60f, 60f };

        /// <summary>Куча снарядов — позади рядов орудий.</summary>
        void SpawnPile(int team)
        {
            Vector3 b = BaseOf(team);
            Vector3 toC = -b; toC.y = 0; toC = toC.sqrMagnitude > 1 ? toC.normalized : Vector3.forward;
            int rows = (CountOf(team) + 7) / 8;
            // Сбоку от рядов орудий, посередине глубины вершины — до склона не меньше 16 м в любую сторону
            Vector3 p = MapGenerator.BatteryPoint(team, MapGenerator.HillHalfW - 18f, MapGenerator.HillHalfD);
            _piles[team] = AmmoPile.Create((byte)team, p, Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg, transform, theme);
        }

        void SupplyUpdate()
        {
            for (int team = 0; team < 2; team++)
            {
                var pile = _piles[team];
                if (!pile) continue;
                if (_trucks[team]) continue;
                if ((_truckT[team] -= Time.deltaTime) > 0) continue;
                _truckT[team] = 60f;                                    // раз в минуту
                // Подъезд — сбоку, со стороны кучи (не через ряды орудий)
                Vector3 hc = MapGenerator.BatteryPoint(team, 0f, MapGenerator.HillHalfD);
                Vector3 outDir = pile.transform.position - hc; outDir.y = 0; outDir = outDir.sqrMagnitude > 1 ? outDir.normalized : Vector3.right;
                Vector3 from = pile.transform.position + outDir * 120f;
                float lim = MapGenerator.Half - 15f;
                from.x = Mathf.Clamp(from.x, -lim, lim); from.z = Mathf.Clamp(from.z, -lim, lim);
                _trucks[team] = SupplyTruck.Create(pile, from, (Faction)team, theme, transform);
            }
        }

        /// <summary>Зенитки — по бокам базы, в окопчике из мешков.</summary>
        void SpawnAA(int team, int slot)
        {
            Vector3 b = BaseOf(team);
            Vector3 toC = -b; toC.y = 0; toC = toC.sqrMagnitude > 1 ? toC.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toC);
            float sgn = slot % 2 == 0 ? -1f : 1f;
            Vector3 p = b + toC * (10f + (slot / 2) * 25f) + side * sgn * (45f + (slot / 2) * 10f);
            float lim = MapGenerator.Zone - 10f;
            p.x = Mathf.Clamp(p.x, -lim, lim); p.z = Mathf.Clamp(p.z, -lim, lim);
            p = Free(p);
            _aa[team, slot] = ArtilleryGun.CreateAA((Faction)team, theme, p, Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg, 500 + 100 * team + _next++ % 100, transform, worldLayer);
        }

        void Spawn(int team, int slot)
        {
            Vector3 b = BaseOf(team);
            Vector3 toC = -b; toC.y = 0; toC = toC.sqrMagnitude > 1 ? toC.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toC);
            // На ровной вершине холма батареи: ряды по 8 через 16 м, первый ряд у передней кромки
            const int perRow = 8;
            int count = CountOf(team);
            int row = slot / perRow, col = slot % perRow, inRow = Mathf.Min(perRow, count - row * perRow);
            Vector3 p = MapGenerator.BatteryPoint(team, (col - (inRow - 1) * 0.5f) * 16f, 12f + row * 22f);
            float yaw = Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg;
            _guns[team, slot] = ArtilleryGun.Create((Faction)team, theme, p, yaw, 100 * team + _next++ % 100, transform, worldLayer);
        }

        /// <summary>Сдвигаем орудие с домов и заборов на ближайшее свободное место.</summary>
        Vector3 Free(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            for (int ring = 0; ring < 5; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI * 0.25f, r = ring * 5f;
                    Vector3 q = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                    q.y = t ? t.SampleHeight(q) + t.transform.position.y : 0f;
                    int n = Physics.OverlapBoxNonAlloc(q + Vector3.up * 1.2f, new Vector3(1.4f, 1f, 2.4f), _ov, Quaternion.identity, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
                    bool busy = false;
                    for (int i = 0; i < n; i++) if (!(_ov[i] is TerrainCollider)) { busy = true; break; }
                    if (!busy) return q;
                    if (ring == 0) break;
                }
            p.y = t ? t.SampleHeight(p) + t.transform.position.y : 0f;
            return p;
        }

        void Update()
        {
            if (_guns == null) return;
            SupplyUpdate();
            for (int team = 0; team < 2; team++)
                for (int s = 0; s < CountOf(team); s++)
                    if (!_guns[team, s] && _respawnAt[team, s] > 0 && Time.time >= _respawnAt[team, s]) { _respawnAt[team, s] = 0; Spawn(team, s); }
            if (_aa != null)
                for (int team = 0; team < 2; team++)
                    for (int s = 0; s < aaPerTeam; s++)
                        if (!_aa[team, s] && _aaRespawn[team, s] > 0 && Time.time >= _aaRespawn[team, s]) { _aaRespawn[team, s] = 0; SpawnAA(team, s); }
        }

        void OnGunDestroyed(ArtilleryGun g, int killer)
        {
            if (_guns != null)
                for (int team = 0; team < 2; team++)
                    for (int s = 0; s < _guns.GetLength(1); s++)
                        if (_guns[team, s] == g) { _guns[team, s] = null; _respawnAt[team, s] = Time.time + respawnDelay; }
            if (_aa != null)
                for (int team = 0; team < 2; team++)
                    for (int s = 0; s < _aa.GetLength(1); s++)
                        if (_aa[team, s] == g) { _aa[team, s] = null; _aaRespawn[team, s] = Time.time + respawnDelay; }
            if (GameModeManager.I) GameModeManager.I.OnArtilleryDestroyed(g, killer);
        }
    }
}
