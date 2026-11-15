using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline.Net
{
    /// <summary>
    /// Другой живой игрок в сетевой игре: та же модель и та же процедурная поза (SoldierPose), что у ботов.
    /// Положение приходит по сети ~20–30 раз в секунду и сглаживается; над головой — подпись-маркер союзника.
    /// </summary>
    public class RemoteSoldier : MonoBehaviour
    {
        struct Part { public Transform t; public LimbKind kind; public float side; }
        Part[] _parts;
        Faction _faction; SoldierClass _cls; bool _built;
        Vector3 _target, _vel, _lastPos; float _yaw, _targetYaw, _gait, _deathT, _recoil;
        bool _alive = true, _crouched, _aiming;
        public string Label;

        public static RemoteSoldier Create(Transform parent, string label)
        {
            var go = new GameObject("RemotePlayer_" + label);
            go.transform.SetParent(parent, false);
            var rs = go.AddComponent<RemoteSoldier>();
            rs.Label = label;
            return rs;
        }

        void Build(Faction f, SoldierClass cls)
        {
            if (_built && f == _faction && cls == _cls) return;
            foreach (Transform c in transform) Destroy(c.gameObject);
            _faction = f; _cls = cls; _built = true;
            var theme = ProceduralModels.Theme;
            BotBody.Palette(f, theme, out var uni, out var trousers, out var helm, out var gear, out var skin);
            var list = new System.Collections.Generic.List<Part>();
            void Add(string n, LimbKind k, float side, Mesh mesh, Color c)
            {
                var go = new GameObject(n);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = ProceduralModels.Mat(c, 0.08f);
                r.shadowCastingMode = ShadowCastingMode.On;
                list.Add(new Part { t = go.transform, kind = k, side = side });
            }
            Add("Torso", LimbKind.Torso, 0, BotBody.TorsoMesh(cls), uni);
            Add("Head", LimbKind.Head, 0, BotBody.HeadMesh(), skin);
            Add("Helmet", LimbKind.Helmet, 0, BotBody.HelmetMesh(f), helm);
            Add("ThighL", LimbKind.Thigh, -1, BotBody.LimbMesh("thigh", 0.17f, BotBody.Thigh, 0), trousers);
            Add("ThighR", LimbKind.Thigh, 1, BotBody.LimbMesh("thigh", 0.17f, BotBody.Thigh, 0), trousers);
            Add("ShinL", LimbKind.Shin, -1, BotBody.LimbMesh("shin", 0.13f, BotBody.Shin, 1), trousers);
            Add("ShinR", LimbKind.Shin, 1, BotBody.LimbMesh("shin", 0.13f, BotBody.Shin, 1), trousers);
            Add("UpperArmL", LimbKind.UpperArm, -1, BotBody.LimbMesh("upper", 0.115f, BotBody.Upper, 0), uni);
            Add("UpperArmR", LimbKind.UpperArm, 1, BotBody.LimbMesh("upper", 0.115f, BotBody.Upper, 0), uni);
            Add("ForearmL", LimbKind.Forearm, -1, BotBody.LimbMesh("fore", 0.095f, BotBody.Fore, 2), uni);
            Add("ForearmR", LimbKind.Forearm, 1, BotBody.LimbMesh("fore", 0.095f, BotBody.Fore, 2), uni);
            Add("Pack", LimbKind.Pack, 0, BotBody.PackMesh(cls), gear);
            // Оружие — та же запечённая детальная модель, что у ботов этого класса
            var gunGo = new GameObject("Gun"); gunGo.transform.SetParent(transform, false);
            var mf = gunGo.AddComponent<MeshFilter>(); var mr = gunGo.AddComponent<MeshRenderer>();
            if (GunBaker.TryGet(f, cls, false, out var gm, out var gmat)) { mf.sharedMesh = gm; mr.sharedMaterial = gmat; }
            else { mf.sharedMesh = BotBody.GunMesh(f, cls); mr.sharedMaterial = ProceduralModels.Mat(ProceduralModels.Wood); }
            list.Add(new Part { t = gunGo.transform, kind = LimbKind.Gun, side = 0 });
            _parts = list.ToArray();
        }

        /// <summary>Новое сетевое состояние.</summary>
        public void SetState(Vector3 pos, float yawDeg, bool alive, bool crouched, bool aiming, byte team, SoldierClass cls)
        {
            Build(team == 0 ? Faction.Germany : Faction.USSR, cls);
            if (!_alive && alive) { transform.position = pos; _lastPos = pos; }   // возродился — без «проезда» через карту
            _target = pos; _targetYaw = yawDeg; _alive = alive; _crouched = crouched; _aiming = aiming;
        }

        public void Shot() => _recoil = 1f;
        /// <summary>Перезаряжает или достаёт оружие — руки опускают ствол и тянутся к магазину (видно всем).</summary>
        public bool Reloading { get; set; }
        /// <summary>Несёт артиллерийский снаряд — он виден в руках.</summary>
        public bool Carrying
        {
            get => _carry;
            set
            {
                if (_carry == value) return;
                _carry = value;
                if (value && !_shell) _shell = LogiModels.Shell(transform, new Vector3(0, 1.05f, 0.4f), new Vector3(0, 90, 0));
                if (_shell) _shell.gameObject.SetActive(value);
            }
        }
        bool _carry; Transform _shell;
        float _reloadK;

        void LateUpdate()
        {
            if (_parts == null) return;
            float dt = Time.deltaTime;
            // Сглаживание: догоняем последнее известное положение (сетевой шум — до ~50 мс)
            if ((transform.position - _target).sqrMagnitude > 25f) transform.position = _target;
            transform.position = Vector3.Lerp(transform.position, _target, Mathf.Min(1f, dt * 14f));
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, Mathf.Min(1f, dt * 14f));
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
            Vector3 v = (transform.position - _lastPos) / Mathf.Max(dt, 1e-4f); v.y = 0;
            _lastPos = transform.position;
            _vel = Vector3.Lerp(_vel, v, Mathf.Min(1f, dt * 8f));
            Vector3 local = transform.InverseTransformDirection(_vel);
            _gait += SoldierPose.Cadence(_vel.magnitude) * dt;
            if (_gait > 1000f) _gait -= 2f * Mathf.PI * 159f;
            _deathT = _alive ? 0f : _deathT + dt;
            _recoil = Mathf.MoveTowards(_recoil, 0, dt * 11f);
            _reloadK = Mathf.MoveTowards(_reloadK, Reloading ? 1f : 0f, dt * 6f);
            var input = new PoseInput
            {
                localVel = new float2(local.x, local.z), speed = _vel.magnitude, crouch = _crouched ? 1 : 0,
                sprint = Mathf.Clamp01((_vel.magnitude - 4.2f) / 1.2f), aim = _aiming ? 1 : 0.3f, recoil = _recoil, reload = _reloadK,
                deadK = _alive ? 0 : Mathf.Clamp01(_deathT / 0.5f), deadT = _deathT, time = Time.time, gait = _gait, seed = 11
            };
            foreach (var p in _parts)
            {
                SoldierPose.Pose(p.kind, p.side, input, out var pos, out var rot);
                p.t.localPosition = pos; p.t.localRotation = rot;
            }
            // Мёртвый игрок лежит: тело заваливается назад
            if (!_alive)
            {
                float k = Mathf.Clamp01(_deathT / 0.6f); k *= k;
                transform.rotation = Quaternion.Euler(0, _yaw, 0) * Quaternion.Euler(-86f * k, 0, 0);
            }
        }
    }
}
