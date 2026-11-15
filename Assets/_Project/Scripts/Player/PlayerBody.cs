using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>
    /// Тело игрока (та же модель и та же поза, что у ботов — SoldierPose):
    /// • от первого лица (бодикам) тело НЕ рисуется — видны только руки с оружием; тело отбрасывает лишь тень
    ///   (бег, присед, прицел, перезарядка видны по тени на земле);
    /// • оружие в руках тела соответствует классу и текущему слоту (основное / пистолет / нож / шпатель).
    /// </summary>
    public class PlayerBody : MonoBehaviour
    {
        public PlayerController player;
        public WeaponController weapon;
        public PlayerHealth health;
        public Faction faction;
        public MapTheme theme;
        public SoldierClass soldierClass;

        struct Part { public Transform t; public LimbKind kind; public float side; public MeshRenderer r; public bool legs; }
        Part[] _parts;
        Transform _gun, _root;
        bool _tpView, _hidden, _viewInit;

        /// <summary>Дуло оружия в руках тела (вспышка от третьего лица).</summary>
        public Vector3 GunMuzzle => _gun ? _gun.position + _gun.forward * 0.6f + _gun.up * 0.03f : transform.position + Vector3.up * 1.4f;

        /// <summary>Сторона выбирается в матче — пересобираем форму, каску и снаряжение.</summary>
        public void SetFaction(Faction f, MapTheme t)
        {
            if (f == faction && t == theme && _parts != null) return;
            faction = f; theme = t;
            if (_parts == null) return;
            if (_root) Destroy(_root.gameObject);
            _parts = null; _viewInit = false;
            Build();
            if (weapon) OnWeaponChanged(weapon);
        }

        /// <summary>Смена класса на экране высадки — пересобираем снаряжение и оружие в руках.</summary>
        public void SetClass(SoldierClass c)
        {
            if (c == soldierClass && _parts != null) return;
            soldierClass = c;
            if (_parts == null) return;          // Start ещё не был — соберётся с новым классом
            if (_root) Destroy(_root.gameObject);
            _parts = null; _viewInit = false;
            Build();
            if (weapon) OnWeaponChanged(weapon);
        }

        /// <summary>thirdPerson — всё тело видно; hidden — тело скрыто целиком (в танке).</summary>
        public void SetView(bool thirdPerson, bool hidden)
        {
            if (_parts == null || (_viewInit && thirdPerson == _tpView && hidden == _hidden)) return;
            _viewInit = true; _tpView = thirdPerson; _hidden = hidden;
            foreach (var p in _parts)
            {
                if (!p.r) continue;
                p.r.enabled = !hidden;
                p.r.shadowCastingMode = thirdPerson ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            }
            if (_root) _root.localPosition = new Vector3(0, 0, thirdPerson ? 0f : -0.12f);
        }
        MeshFilter _gunMesh;
        float _recoil, _reload, _aim, _crouch, _sprint, _air, _deathT, _gait;
        int _lastAmmo;
        Vector3 _lastPos;

        void Start()
        {
            Build();
            if (weapon) { weapon.AmmoChanged += OnAmmo; weapon.WeaponChanged += OnWeaponChanged; }
            _lastPos = transform.position;
        }

        void OnDestroy() { if (weapon) { weapon.AmmoChanged -= OnAmmo; weapon.WeaponChanged -= OnWeaponChanged; } }

        void OnAmmo(WeaponController w) { if (w.Ammo < _lastAmmo && !w.Reloading) _recoil = 1f; _lastAmmo = w.Ammo; }

        public static bool LocalReloadOrDraw;
        float _drawT;

        void OnWeaponChanged(WeaponController w)
        {
            _drawT = 0.45f;          // достаёт оружие: стволом вниз, затем вскидывает
            if (!_gunMesh) return;
            // Пистолет / нож / шпатель — короче основного оружия
            var r = _gunMesh.GetComponent<MeshRenderer>();
            // То же оружие, что от первого лица: запечённая детальная модель без кистей
            if (w.Current && w.Current.viewModelPrefab && GunBaker.TryBake(w.Current.viewModelPrefab, out var gm, out var gmat))
            {
                _gunMesh.sharedMesh = gm; r.sharedMaterial = gmat;
            }
            else
            {
                bool small = !w.Current || w.Current.category is WeaponCategory.Pistol or WeaponCategory.Pistol2 or WeaponCategory.Knife;
                _gunMesh.sharedMesh = small ? SmallMesh() : BotBody.GunMesh(faction, soldierClass);
                r.sharedMaterial = ProceduralModels.Mat(ProceduralModels.Wood);
            }
            _lastAmmo = w.Ammo;
        }

        static Mesh _small;
        static Mesh SmallMesh()
        {
            if (_small) return _small;
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var m = Object.Instantiate(g.GetComponent<MeshFilter>().sharedMesh);
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], new Vector3(0.04f, 0.12f, 0.2f)) + new Vector3(0, -0.03f, 0.05f);
            m.vertices = v; m.RecalculateBounds();
            Destroy(g);
            return _small = m;
        }

        void Build()
        {
            BotBody.Palette(faction, theme, out var uniform, out var trousers, out var helmet, out var gear, out var skin);

            var root = new GameObject("Body").transform;
            root.SetParent(transform, false);
            var list = new System.Collections.Generic.List<Part>();

            void Add(string n, LimbKind k, float side, Mesh mesh, Color c, bool visible)
            {
                var go = new GameObject(n);
                go.transform.SetParent(root, false);
                go.layer = gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = ProceduralModels.Mat(c, 0.08f);
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;   // от первого лица — только тень
                list.Add(new Part { t = go.transform, kind = k, side = side, r = r, legs = visible });
                if (k == LimbKind.Gun) { _gun = go.transform; _gunMesh = go.GetComponent<MeshFilter>(); }
            }

            Add("Torso", LimbKind.Torso, 0, BotBody.TorsoMesh(soldierClass), uniform, true);
            Add("Head", LimbKind.Head, 0, BotBody.HeadMesh(), skin, false);
            Add("Helmet", LimbKind.Helmet, 0, BotBody.HelmetMesh(faction), helmet, false);
            Add("ThighL", LimbKind.Thigh, -1, BotBody.LimbMesh("thigh", 0.17f, BotBody.Thigh, 0), trousers, true);
            Add("ThighR", LimbKind.Thigh, 1, BotBody.LimbMesh("thigh", 0.17f, BotBody.Thigh, 0), trousers, true);
            Add("ShinL", LimbKind.Shin, -1, BotBody.LimbMesh("shin", 0.13f, BotBody.Shin, 1), trousers, true);
            Add("ShinR", LimbKind.Shin, 1, BotBody.LimbMesh("shin", 0.13f, BotBody.Shin, 1), trousers, true);
            Add("UpperArmL", LimbKind.UpperArm, -1, BotBody.LimbMesh("upper", 0.115f, BotBody.Upper, 0), uniform, false);
            Add("UpperArmR", LimbKind.UpperArm, 1, BotBody.LimbMesh("upper", 0.115f, BotBody.Upper, 0), uniform, false);
            Add("ForearmL", LimbKind.Forearm, -1, BotBody.LimbMesh("fore", 0.095f, BotBody.Fore, 2), uniform, false);
            Add("ForearmR", LimbKind.Forearm, 1, BotBody.LimbMesh("fore", 0.095f, BotBody.Fore, 2), uniform, false);
            Add("Gun", LimbKind.Gun, 0, BotBody.GunMesh(faction, soldierClass), ProceduralModels.Wood, false);
            Add("Pack", LimbKind.Pack, 0, BotBody.PackMesh(soldierClass), gear, false);
            _parts = list.ToArray();
            _root = root;
            // Видимые ноги чуть сдвинуты назад, чтобы при взгляде вниз камера не оказывалась «внутри» таза
            root.localPosition = new Vector3(0, 0, -0.12f);
        }

        void LateUpdate()
        {
            if (_parts == null || !player) return;
            float dt = Time.deltaTime;
            Vector3 vel = (transform.position - _lastPos) / Mathf.Max(dt, 1e-4f);
            _lastPos = transform.position;
            vel.y = 0;
            Vector3 local = transform.InverseTransformDirection(vel);

            _recoil = Mathf.MoveTowards(_recoil, 0, dt * 11f);
            // Перезарядка и доставание оружия видны от третьего лица (и другим игрокам по сети)
            _drawT = Mathf.Max(0, _drawT - dt);
            bool rel = (weapon && weapon.Reloading) || _drawT > 0;
            LocalReloadOrDraw = rel;
            _reload = Mathf.MoveTowards(_reload, rel ? 1 : 0, dt * 6f);
            _aim = Mathf.MoveTowards(_aim, player.Aiming || (weapon && weapon.Current && player.Input.fireHeld) ? 1 : 0.35f, dt * 6f);
            _crouch = Mathf.MoveTowards(_crouch, player.IsCrouched ? 1 : 0, dt * 6f);
            _sprint = Mathf.MoveTowards(_sprint, player.IsSprinting ? 1 : 0, dt * 5f);
            _air = Mathf.MoveTowards(_air, player.IsGrounded ? 0 : 1, dt * 6f);
            bool dead = health && !health.Alive;
            _deathT = dead ? _deathT + dt : 0;
            _gait += SoldierPose.Cadence(vel.magnitude) * dt;
            if (_gait > 1000f) _gait -= 2f * Mathf.PI * 159f;

            float pitch = player.cameraPivot ? -NormalizeAngle(player.cameraPivot.localEulerAngles.x) * Mathf.Deg2Rad : 0;
            var input = new PoseInput
            {
                localVel = new float2(local.x, local.z), speed = vel.magnitude, crouch = _crouch, sprint = _sprint,
                aim = _aim, aimPitch = Mathf.Clamp(pitch, -0.9f, 0.9f), recoil = _recoil, reload = _reload,
                flinch = 0, deadK = dead ? Mathf.Clamp01(_deathT / 0.5f) : 0, deadT = _deathT, time = Time.time, airborne = _air,
                lookIdle = 0, seed = 7, gait = _gait
            };
            foreach (var p in _parts)
            {
                SoldierPose.Pose(p.kind, p.side, input, out var pos, out var rot);
                p.t.localPosition = pos;
                p.t.localRotation = rot;
            }
        }

        static float NormalizeAngle(float a) => a > 180 ? a - 360 : a;
    }
}
