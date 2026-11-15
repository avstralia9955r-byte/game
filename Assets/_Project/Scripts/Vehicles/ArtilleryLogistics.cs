using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>Модели для логистики батареи: снаряд, фигурка бойца, грузовик с прицепом.</summary>
    public static class LogiModels
    {
        static readonly Color Brass = new(0.72f, 0.56f, 0.24f), Steel = new(0.32f, 0.33f, 0.3f), Ogive = new(0.28f, 0.3f, 0.24f);

        /// <summary>Снаряд с гильзой (≈0,9 м), ось — вдоль Z.</summary>
        public static Transform Shell(Transform parent, Vector3 pos, Vector3 euler, float scale = 1f)
        {
            var root = new GameObject("Shell").transform; root.SetParent(parent, false);
            root.localPosition = pos; root.localRotation = Quaternion.Euler(euler); root.localScale = Vector3.one * scale;
            ProceduralModels.Part(root, "Case", MeshUtil.Cylinder(10), new Vector3(0, 0, -0.18f), new Vector3(0.16f, 0.5f, 0.16f), Brass, new Vector3(90, 0, 0), false, 0.6f, 0.8f);
            ProceduralModels.Part(root, "Body", MeshUtil.Cylinder(10), new Vector3(0, 0, 0.22f), new Vector3(0.15f, 0.32f, 0.15f), Ogive, new Vector3(90, 0, 0), false, 0.3f, 0.3f);
            ProceduralModels.Part(root, "Nose", MeshUtil.Cone(10), new Vector3(0, 0, 0.38f), new Vector3(0.15f, 0.16f, 0.15f), Steel, new Vector3(90, 0, 0), false, 0.4f, 0.5f);
            return root;
        }

        /// <summary>Простой боец (ноги, корпус, руки вперёд, голова в каске) — для анимации переноски.</summary>
        public static Transform Soldier(Transform parent, Faction f, out Transform hands)
        {
            bool de = f == Faction.Germany;
            Color uni = de ? new Color(0.36f, 0.37f, 0.32f) : new Color(0.42f, 0.4f, 0.28f);
            var root = new GameObject("Carrier").transform; root.SetParent(parent, false);
            for (int s = -1; s <= 1; s += 2)
            {
                var leg = new GameObject(s < 0 ? "LegL" : "LegR").transform; leg.SetParent(root, false); leg.localPosition = new Vector3(s * 0.1f, 0.9f, 0);
                ProceduralModels.Part(leg, "Mesh", MeshUtil.Bevel(0.04f), new Vector3(0, -0.45f, 0), new Vector3(0.15f, 0.9f, 0.18f), uni, default, false, 0.1f, 0f);
            }
            ProceduralModels.Part(root, "Body", MeshUtil.Bevel(0.06f), new Vector3(0, 1.2f, 0), new Vector3(0.42f, 0.6f, 0.26f), uni, default, false, 0.1f, 0f);
            ProceduralModels.Part(root, "Head", MeshUtil.Ico(), new Vector3(0, 1.62f, 0.02f), new Vector3(0.22f, 0.26f, 0.24f), new Color(0.75f, 0.6f, 0.5f), default, false, 0.2f, 0f);
            ProceduralModels.Part(root, "Helmet", MeshUtil.Ico(), new Vector3(0, 1.7f, 0.02f), new Vector3(0.3f, 0.18f, 0.32f), de ? new Color(0.3f, 0.32f, 0.28f) : new Color(0.28f, 0.33f, 0.22f), default, false, 0.3f, 0f);
            for (int s = -1; s <= 1; s += 2)
                ProceduralModels.Part(root, "Arm", MeshUtil.Bevel(0.03f), new Vector3(s * 0.2f, 1.2f, 0.22f), new Vector3(0.1f, 0.1f, 0.48f), uni, new Vector3(20, 0, 0), false, 0.1f, 0f);
            hands = new GameObject("Hands").transform; hands.SetParent(root, false); hands.localPosition = new Vector3(0, 1.05f, 0.42f);
            return root;
        }

        /// <summary>Шагающая анимация: ноги ножницами, лёгкое покачивание корпуса.</summary>
        public static void Walk(Transform fig, float phase, float amount)
        {
            if (!fig) return;
            var l = fig.Find("LegL"); var r = fig.Find("LegR");
            float a = Mathf.Sin(phase) * 32f * amount;
            if (l) l.localRotation = Quaternion.Euler(a, 0, 0);
            if (r) r.localRotation = Quaternion.Euler(-a, 0, 0);
        }
    }

    /// <summary>
    /// Куча снарядов за рядом орудий: до 14 штук на земляном пригорке, снаряды исчезают по одному при подборе.
    /// Берут её наводчики-боты и игрок (клавиша E); пополняет грузовик снабжения.
    /// </summary>
    public class AmmoPile : MonoBehaviour
    {
        public const int Capacity = 40;
        public static readonly List<AmmoPile> All = new();
        public byte team;
        public int Count { get; private set; }
        readonly Transform[] _shells = new Transform[Capacity];

        public static AmmoPile Of(byte team) { foreach (var p in All) if (p && p.team == team) return p; return null; }

        public static AmmoPile Create(byte team, Vector3 pos, float yaw, Transform parent, MapTheme theme)
        {
            var go = new GameObject("AmmoPile");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var p = go.AddComponent<AmmoPile>(); p.team = team;
            Color dirt = theme == MapTheme.Winter ? new Color(0.78f, 0.8f, 0.82f) : new Color(0.38f, 0.3f, 0.2f);
            ProceduralModels.Part(go.transform, "Mound", MeshUtil.Ico(), new Vector3(0, 0.05f, 0), new Vector3(5.2f, 0.7f, 3.6f), dirt, default, false, 0.05f, 0f);
            // Штабель: два ряда по 8 × 3 яруса снизу вверх (8+8, 7+7, 5+5) = 40, носами вбок
            int[] perLayer = { 8, 7, 5 };
            int n = 0;
            for (int layer = 0; layer < 3 && n < Capacity; layer++)
                for (int rowZ = 0; rowZ < 2; rowZ++)
                    for (int k = 0; k < perLayer[layer] && n < Capacity; k++)
                    {
                        float x = (k - (perLayer[layer] - 1) * 0.5f) * 0.19f;
                        p._shells[n++] = LogiModels.Shell(go.transform, new Vector3(x, 0.36f + layer * 0.16f, (rowZ - 0.5f) * 0.95f), new Vector3(0, 90, 0));
                    }
            p.Count = Capacity;
            All.Add(p);
            return p;
        }

        void OnDestroy() => All.Remove(this);

        void Refresh() { for (int i = 0; i < Capacity; i++) if (_shells[i]) _shells[i].gameObject.SetActive(i < Count); }

        public bool Take() { if (Count <= 0) return false; Count--; Refresh(); return true; }
        public bool Add() { if (Count >= Capacity) return false; Count++; Refresh(); return true; }
        public void SetCount(int n) { Count = Mathf.Clamp(n, 0, Capacity); Refresh(); }
    }

    /// <summary>
    /// Грузовик снабжения: раз в минуту приезжает к куче своей батареи, водитель переносит снаряды
    /// из кузова (30 шт.) в кучу по одному — с ходьбой и снарядом в руках, затем садится и уезжает.
    /// </summary>
    public class SupplyTruck : MonoBehaviour
    {
        enum Phase { Arrive, Unload, Leave }
        Phase _phase;
        AmmoPile _pile;
        Vector3 _from, _stop;
        float _speed, _wheelAng, _walkPhase;
        public const int Load = 30;
        int _cargo = Load;
        Transform _trailer, _driver, _driverHands, _carried;
        readonly List<Transform> _cargoShells = new();
        readonly List<Transform> _wheels = new();
        int _step; float _wait;
        AudioSource _engine;

        public static SupplyTruck Create(AmmoPile pile, Vector3 from, Faction f, MapTheme theme, Transform parent)
        {
            var go = new GameObject("SupplyTruck");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<SupplyTruck>();
            t._pile = pile; t._from = from;
            Vector3 away = from - pile.transform.position; away.y = 0; away.Normalize();
            t._stop = pile.transform.position + away * 9f + Vector3.Cross(Vector3.up, away) * 3.5f;
            t.Build(f, theme);
            t.Place(from, Quaternion.LookRotation(-away));
            return t;
        }

        void Build(Faction f, MapTheme theme)
        {
            bool de = f == Faction.Germany;
            Color paint = theme == MapTheme.Winter ? new Color(0.8f, 0.81f, 0.8f) : de ? new Color(0.42f, 0.42f, 0.38f) : new Color(0.32f, 0.37f, 0.25f);
            Color dark = Color.Lerp(paint, Color.black, 0.5f), tyre = new(0.08f, 0.08f, 0.08f), wood = new(0.42f, 0.32f, 0.2f), glass = new(0.4f, 0.48f, 0.52f);
            var r = transform;
            GameObject P(Transform p, string n, Mesh m, Vector3 at, Vector3 sc, Color c, Vector3 eu = default) => ProceduralModels.Part(p, n, m, at, sc, c, eu, false, 0.25f, 0.15f);
            // Грузовик (Opel Blitz / ЗИС-5): кабина, капот, кузов, 6 колёс
            P(r, "Chassis", MeshUtil.Bevel(0.03f), new Vector3(0, 0.75f, 0), new Vector3(1.6f, 0.25f, 5.6f), dark);
            P(r, "Hood", MeshUtil.Bevel(0.08f), new Vector3(0, 1.25f, 2.15f), new Vector3(1.2f, 0.8f, 1.3f), paint);
            P(r, "Cab", MeshUtil.Bevel(0.08f), new Vector3(0, 1.7f, 1.05f), new Vector3(1.9f, 1.4f, 1.1f), paint);
            P(r, "Windshield", MeshUtil.Bevel(0.02f), new Vector3(0, 2.0f, 1.62f), new Vector3(1.6f, 0.5f, 0.04f), glass);
            P(r, "Bed", MeshUtil.Bevel(0.03f), new Vector3(0, 1.25f, -1.0f), new Vector3(2.1f, 0.15f, 3.0f), wood);
            for (int s = -1; s <= 1; s += 2) P(r, "Side", MeshUtil.Bevel(0.02f), new Vector3(s * 1.03f, 1.55f, -1.0f), new Vector3(0.06f, 0.5f, 3.0f), wood);
            P(r, "Tail", MeshUtil.Bevel(0.02f), new Vector3(0, 1.55f, -2.48f), new Vector3(2.1f, 0.5f, 0.06f), wood);
            foreach (var z in new[] { 2.0f, -1.2f, -2.2f })
                for (int s = -1; s <= 1; s += 2)
                {
                    var w = P(r, "Wheel", MeshUtil.Cylinder(14), new Vector3(s * 0.85f, 0.48f, z), new Vector3(0.96f, 0.28f, 0.96f), tyre, new Vector3(0, 0, 90)).transform;
                    _wheels.Add(w);
                }
            // 30 снарядов в кузове: 3 ряда по 10, видны с бортов
            _trailer = r;
            for (int i = 0; i < Load; i++)
            {
                int row = i / 10, k = i % 10;
                _cargoShells.Add(LogiModels.Shell(r, new Vector3((k - 4.5f) * 0.19f, 1.42f + (row == 2 ? 0.17f : 0f), -0.3f - (row == 1 ? 0.95f : row == 2 ? 0.48f : 0f) - 0.4f), Vector3.zero));
            }
            _driver = LogiModels.Soldier(r, f, out _driverHands);
            _driver.gameObject.SetActive(false);
            var a = new GameObject("Engine").AddComponent<AudioSource>(); a.transform.SetParent(r, false);
            a.clip = SoundSynth.Engine(!de); a.loop = true; a.spatialBlend = 1; a.minDistance = 6; a.maxDistance = 120; a.volume = 0.55f; a.pitch = 1.15f; a.rolloffMode = AudioRolloffMode.Linear; a.Play();
            _engine = a;
        }

        static float TerrainH(Vector3 p) { var t = Terrain.activeTerrain; return t ? t.SampleHeight(p) + t.transform.position.y : 0f; }

        void Place(Vector3 p, Quaternion rot)
        {
            p.y = TerrainH(p);
            transform.SetPositionAndRotation(p, rot);
        }

        bool DriveTo(Vector3 target, float dt)
        {
            Vector3 d = target - transform.position; d.y = 0;
            float dist = d.magnitude;
            _speed = Mathf.MoveTowards(_speed, dist < 8f ? Mathf.Max(1f, dist) : 8f, dt * 4f);
            if (dist < 0.4f) { _speed = 0; return true; }
            Quaternion want = Quaternion.LookRotation(d / dist);
            var rot = Quaternion.RotateTowards(transform.rotation, want, 40f * dt);
            Place(transform.position + rot * Vector3.forward * Mathf.Min(_speed * dt, dist), rot);
            _wheelAng += _speed * dt / 0.48f * Mathf.Rad2Deg;
            foreach (var w in _wheels) if (w) w.localRotation = Quaternion.Euler(_wheelAng, 0, 90);
            return false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!_pile) { Destroy(gameObject); return; }
            if (_engine) _engine.pitch = 0.9f + _speed * 0.06f;
            switch (_phase)
            {
                case Phase.Arrive:
                    if (DriveTo(_stop, dt)) { _phase = Phase.Unload; _driver.gameObject.SetActive(true); _driver.position = CabSide(); _step = 0; _wait = 0.5f; }
                    break;
                case Phase.Unload:
                    Unload(dt);
                    break;
                case Phase.Leave:
                    if (DriveTo(_from, dt)) Destroy(gameObject);
                    break;
            }
        }

        Vector3 CabSide() { var p = transform.TransformPoint(new Vector3(-1.4f, 0, 1.0f)); p.y = TerrainH(p); return p; }
        Vector3 TrailerBack() { var p = transform.TransformPoint(new Vector3(0f, 0, -3.3f)); p.y = TerrainH(p); return p; }
        Vector3 PileFront() { var d = transform.position - _pile.transform.position; d.y = 0; var p = _pile.transform.position + d.normalized * 2.2f; p.y = TerrainH(p); return p; }

        /// <summary>Шаги разгрузки: к прицепу → взять снаряд → к куче (медленнее, со снарядом) → положить → назад.</summary>
        void Unload(float dt)
        {
            if (_wait > 0) { _wait -= dt; return; }
            bool carrying = _carried;
            Vector3 target = _step switch { 0 => TrailerBack(), 1 => PileFront(), _ => CabSide() };
            float sp = carrying ? 1.2f : 2.2f;
            Vector3 d = target - _driver.position; d.y = 0;
            if (d.magnitude > 0.3f)
            {
                _driver.rotation = Quaternion.LookRotation(d.normalized);
                Vector3 np = _driver.position + d.normalized * Mathf.Min(sp * dt, d.magnitude);
                np.y = TerrainH(np);
                _driver.position = np;
                _walkPhase += dt * sp * 4f;
                LogiModels.Walk(_driver, _walkPhase, 1f);
                return;
            }
            LogiModels.Walk(_driver, 0, 0);
            if (_step == 0)
            {
                if (_cargo <= 0 || _pile.Count >= AmmoPile.Capacity) { _step = 2; return; }
                _cargo--;
                var src = _cargoShells[_cargo]; if (src) src.gameObject.SetActive(false);
                _carried = LogiModels.Shell(_driverHands, Vector3.zero, new Vector3(0, 90, 0));
                _step = 1; _wait = 0.4f;
            }
            else if (_step == 1)
            {
                if (_carried) Destroy(_carried.gameObject);
                _carried = null;
                _pile.Add();
                _step = 0; _wait = 0.35f;
            }
            else
            {
                _driver.gameObject.SetActive(false);
                _phase = Phase.Leave;
            }
        }

        // ---- Помощь игрока в разгрузке ----
        public static readonly List<SupplyTruck> All = new();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);
        public byte Team => _pile ? _pile.team : (byte)255;
        public Vector3 BackPos => TrailerBack();
        /// <summary>Грузовик стоит у кучи и в кузове ещё есть снаряды — игрок может взять один и отнести сам.</summary>
        public bool CanPlayerTake => _phase == Phase.Unload && _cargo > 0;
        public int Cargo => _cargo;
        public bool PlayerTake()
        {
            if (!CanPlayerTake) return false;
            _cargo--;
            var src = _cargoShells[_cargo]; if (src) src.gameObject.SetActive(false);
            return true;
        }

        /// <summary>Пока грузовик в пути или разгружается, следующий не выезжает.</summary>
        public bool Busy => this && gameObject;
    }

    /// <summary>
    /// Заряжающий, нанятый игроком за 200 монет: стоит у орудия, а как только оно разряжено — идёт к куче,
    /// берёт снаряд, несёт его (медленнее) и 5 с заряжает. Работает, пока жива пушка; бесплатно перезаряжает
    /// и при игроке-наводчике, и при боте.
    /// </summary>
    public class ArtilleryLoader : MonoBehaviour
    {
        ArtilleryGun _gun; Transform _fig, _hands, _shell;
        enum St : byte { Idle, ToPile, ToGun, Wait }
        St _st; float _ph, _wait;

        public static ArtilleryLoader Create(ArtilleryGun gun, Faction f)
        {
            var go = new GameObject("HiredLoader");
            go.transform.SetParent(gun.transform.parent, false);
            var l = go.AddComponent<ArtilleryLoader>(); l._gun = gun;
            l._fig = LogiModels.Soldier(go.transform, f, out l._hands);
            l._fig.position = l.Home(); l._fig.rotation = gun.transform.rotation;
            BotAudio.PlayAt(SoundSynth.AmmoPickup(), l._fig.position + Vector3.up, 0.6f);
            return l;
        }

        static float H(Vector3 p) { var t = Terrain.activeTerrain; return t ? t.SampleHeight(p) + t.transform.position.y : p.y; }
        Vector3 Home() { var p = _gun.transform.TransformPoint(new Vector3(1.4f, 0, -1.6f)); p.y = H(p); return p; }

        void Update()
        {
            if (!_gun || !_gun.Alive) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            var pile = AmmoPile.Of(_gun.Team);
            switch (_st)
            {
                case St.Idle:
                    LogiModels.Walk(_fig, 0, 0);
                    if (!Walk(Home(), 2.4f, dt)) return;
                    _fig.rotation = Quaternion.Slerp(_fig.rotation, _gun.transform.rotation, dt * 3f);
                    if (!_gun.Loaded && !_gun.Loading && pile && pile.Count > 0) _st = St.ToPile;
                    break;
                case St.ToPile:
                    if (!pile) { _st = St.Idle; return; }
                    Vector3 pp = pile.transform.position + (_gun.transform.position - pile.transform.position).normalized * 1.7f;
                    if (!Walk(pp, 2.6f, dt)) return;
                    if (_gun.Loaded || _gun.Loading) { _st = St.Idle; return; }
                    if (!pile.Take()) { _st = St.Idle; return; }
                    _shell = LogiModels.Shell(_hands, Vector3.zero, new Vector3(0, 90, 0));
                    _st = St.ToGun;
                    break;
                case St.ToGun:
                    if (!Walk(Home(), 1.3f, dt)) return;
                    if (_shell) Destroy(_shell.gameObject); _shell = null;
                    if (!_gun.StartHelperLoad() && pile) pile.Add();   // уже зарядили — вернуть снаряд в кучу
                    _st = St.Wait; _wait = 0.5f;
                    break;
                case St.Wait:
                    LogiModels.Walk(_fig, 0, 0);
                    _wait -= dt;
                    if (_wait <= 0 && !_gun.Loading) _st = St.Idle;
                    break;
            }
        }

        bool Walk(Vector3 target, float sp, float dt)
        {
            Vector3 d = target - _fig.position; d.y = 0;
            if (d.magnitude < 0.35f) { LogiModels.Walk(_fig, 0, 0); return true; }
            _fig.rotation = Quaternion.LookRotation(d.normalized);
            _ph += dt * sp * 4f;
            Vector3 np = _fig.position + d.normalized * Mathf.Min(sp * dt, d.magnitude); np.y = H(np);
            _fig.position = np;
            LogiModels.Walk(_fig, _ph, 1f);
            return false;
        }
    }
}
