using System;
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
    /// Истребитель (Bf 109 / Як-9) с аркадной моделью полёта в духе Ravenfield.
    /// Управление игрока: W/S — газ, A/D — руль направления, мышь вверх/вниз — тангаж, влево/вправо — крен,
    /// ЛКМ — пулемёты (1000 патронов, сбивают самолёты), ПКМ — ракеты (10), F5 — вид из кабины / сзади,
    /// E — выйти (на земле) или выпрыгнуть с парашютом (в воздухе).
    /// Разбег по земле, отрыв на скорости ~110 км/ч, сваливание на малой скорости, посадка на ровное место.
    /// Пилотом может быть бот: он сам бежит к самолёту, взлетает, ведёт воздушный бой и штурмует технику и пехоту.
    /// </summary>
    public class Aircraft : MonoBehaviour
    {
        public static readonly List<Aircraft> All = new();
        public static event Action<Aircraft, int> Destroyed;     // самолёт, actorId сбившего

        public const int MaxRockets = 10, MaxMg = 1000;
        const float GearH = 1.3f, TakeoffSpeed = 31f, StallSpeed = 27f, ThrustAcc = 14f, Drag = 0.0021f;
        const float CeilingY = 700f, Bound = 620f, Cruise = 120f, Converge = 280f;

        public Faction faction;
        public byte Team => (byte)faction;
        public int index;
        public string DisplayName => faction == Faction.Germany ? "Bf 109 G" : "Як-9";
        public float maxHealth = 450f;
        public float Health { get; private set; }
        public bool Alive => Health > 0;
        public int Rockets { get; private set; } = MaxRockets;
        public int MgAmmo { get; private set; } = MaxMg;
        public bool Airborne { get; private set; }
        public bool PlayerAboard { get; private set; }
        public bool Cockpit { get; private set; }
        public float Throttle => _thr;
        public float Speed => _spd;
        public Vector3 Velocity => _dir * _spd + Vector3.down * _sink;
        public float Agl { get; private set; }
        public bool Stalling => Airborne && _lift < 0.8f;
        public bool OutOfBounds { get; private set; }
        public float LastHitTime { get; private set; } = -99f;
        public bool Wrecked => _crashed;
        public bool HasPilot => PlayerAboard || pilotBot > 0;
        public int PilotActorId => PlayerAboard ? CombatWorld.PlayerActorId : pilotBot > 0 ? pilotBot : -1000 - index;
        public Vector3 Home { get; set; }
        [NonSerialized] public int pilotBot = -1;

        AircraftModels.Rig _rig;
        MapTheme _theme;
        AudioSource _engine;
        AudioClip _mgClip;
        bool _audioOn = true;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        // Полёт
        float _spd, _sink, _lift = 1f, _thr, _thrWant, _yaw;
        Vector3 _dir = Vector3.forward;
        float _pIn, _rIn, _yIn;
        bool _brake, _fireMg, _fireRk;
        float _mgCd, _rkCd, _propAngle, _smokeT;
        int _gunSide, _shotN;
        bool _crashed; float _wreckUntil;
        int _lastAttacker = -1;
        float _spawnTime;

        // ---------------- Создание ----------------

        public static Aircraft Create(Faction f, MapTheme theme, Vector3 groundPos, float yaw, int index, Transform parent, int layer)
        {
            var rig = AircraftModels.Build(f, theme, parent);
            var a = rig.root.AddComponent<Aircraft>();
            a._rig = rig; a.faction = f; a.index = index; a._theme = theme;
            a.Health = a.maxHealth; a._yaw = yaw; a._spawnTime = Time.time;
            a.transform.SetPositionAndRotation(groundPos + Vector3.up * GearH, Quaternion.Euler(-9f, yaw, 0));
            a._dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            a.Home = groundPos;
            // Коллайдеры: фюзеляж, крыло, хвост — на слое мира (в них попадают пули, ракеты и другие самолёты)
            void Box(Vector3 c, Vector3 s) { var b = rig.root.AddComponent<BoxCollider>(); b.center = c; b.size = s; }
            Box(new Vector3(0, 0.1f, 0.2f), new Vector3(1.15f, 1.3f, 8.4f));
            Box(new Vector3(0, -0.15f, 0.75f), new Vector3(10f, 0.35f, 1.8f));
            Box(new Vector3(0, 0.6f, -3.85f), new Vector3(3.3f, 1.5f, 1.1f));
            foreach (var c in rig.root.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;

            var go = new GameObject("Audio"); go.transform.SetParent(rig.root.transform, false); go.transform.localPosition = new Vector3(0, 0, 3f);
            var s = go.AddComponent<AudioSource>();
            var clip = SoundSynth.Engine(false);
            s.clip = clip; s.loop = true; s.volume = 0.3f; s.spatialBlend = 1f; s.minDistance = 12f; s.maxDistance = 650f;
            s.rolloffMode = AudioRolloffMode.Linear; s.dopplerLevel = 0.8f; s.pitch = 1.1f; s.time = Random.Range(0f, clip.length * 0.9f);
            s.Play();
            a._engine = s;
            a._mgClip = SoundSynth.Gunshot("aircraft_mg", WeaponCategory.MachineGun);
            AircraftModels.SetupLod(rig.root);
            All.Add(a);
            return a;
        }

        void OnDestroy()
        {
            All.Remove(this);
            ReleaseBuddy();
            if (PlayerAboard) ExitPlayer(false, -1);
        }

        bool Mine(Collider c) => c && c.transform.IsChildOf(transform);

        static float TerrainH(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t ? t.SampleHeight(p) + t.transform.position.y : 0f;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        // ---------------- Цикл ----------------

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0) return;
            if (_crashed) { if (Time.time > _wreckUntil) Destroy(gameObject); return; }

            KeepPilot();
            // Пустой самолёт на стоянке: только набор пилота, без расчёта движения
            if (Alive && !Airborne && !HasPilot && _spd < 0.01f && _thr < 0.01f)
            {
                _thrWant = 0; _fireMg = _fireRk = false;
                Recruit();
                if (_engine && _engine.volume > 0.01f) _engine.volume = Mathf.MoveTowards(_engine.volume, 0f, dt);
                return;
            }
            if (!Alive) { _pIn = 0.2f; _rIn = 0.7f; _yIn = 0.3f; _thrWant = 0f; _fireMg = _fireRk = false; }
            else if (PlayerAboard) PlayerInput(dt);
            else if (pilotBot > 0) ThinkAI(dt);
            else { _pIn = _rIn = _yIn = 0; _thrWant = 0; _fireMg = _fireRk = false; _brake = true; Recruit(); }

            _thr = Mathf.MoveTowards(_thr, _thrWant, dt * 1.2f);
            Fly(dt);
            if (_crashed) return;
            if (Alive) Weapons(dt);
            Visuals(dt);
            if (Alive && !Airborne && _spd < 4f) RearmAtHome(dt);
        }

        // ---------------- Модель полёта ----------------

        void Fly(float dt)
        {
            Vector3 pos = transform.position;
            if (!Airborne)
            {
                // Разбег / руление по земле: препятствия на земле не мешают (иначе самолёт застревал бы у базы)
                float acc = ThrustAcc * _thr - 0.5f - Drag * _spd * _spd;
                if (_thr < 0.05f) acc -= _brake ? 9f : 2f;
                _spd = Mathf.Clamp(_spd + acc * dt, 0f, 60f);
                float yawRate = Mathf.Lerp(50f, 15f, _spd / TakeoffSpeed);
                _yaw += _yIn * yawRate * dt;
                float pitch = Mathf.Lerp(-9f, -2.5f, Mathf.Clamp01(_spd / TakeoffSpeed));   // хвостовое колесо: на разбеге хвост поднимается
                Vector3 flat = Quaternion.Euler(0, _yaw, 0) * Vector3.forward;
                Vector3 p = pos + flat * (_spd * dt);
                float lim = MapGenerator.Half - 8f;
                p.x = Mathf.Clamp(p.x, -lim, lim); p.z = Mathf.Clamp(p.z, -lim, lim);
                p.y = TerrainH(p) + GearH;
                _dir = flat; _sink = 0; _lift = 1f;
                transform.SetPositionAndRotation(p, Quaternion.Euler(pitch, _yaw, 0));
                Agl = 0;
                OutOfBounds = false;
                if (Alive && _spd >= TakeoffSpeed && (_pIn > 0.1f || _spd > TakeoffSpeed + 8f))
                {
                    Airborne = true;
                    transform.SetPositionAndRotation(p + Vector3.up * 0.5f, Quaternion.Euler(-7f, _yaw, 0));
                    _dir = transform.forward;
                }
                return;
            }

            Quaternion rot = transform.rotation;
            float eff = Mathf.Clamp01((_spd - 8f) / 30f);
            rot *= Quaternion.Euler(-_pIn * 72f * eff * dt, _yIn * 28f * eff * dt, -_rIn * 150f * eff * dt);
            // Согласованный разворот: накренённый самолёт сам уходит в сторону крена
            Vector3 right = rot * Vector3.right;
            rot = Quaternion.AngleAxis(-right.y * 30f * eff * dt, Vector3.up) * rot;
            Vector3 fwd = rot * Vector3.forward;

            float thrust = ThrustAcc * _thr * Mathf.Clamp01((CeilingY - pos.y) / 150f + 0.15f);
            _spd += (thrust - Drag * _spd * _spd - 9.81f * _dir.y) * dt;
            _spd = Mathf.Clamp(_spd, 0f, 150f);
            _lift = Mathf.Clamp01((_spd - 16f) / (StallSpeed + 6f - 16f));
            // Подъёмная сила тянет вектор скорости к носу
            _dir = Vector3.Slerp(_dir, fwd, 1f - Mathf.Exp(-(2.6f * _lift + 0.25f) * dt)).normalized;
            if (_lift < 1f) _sink += 9.81f * (1f - _lift) * dt;
            else _sink = Mathf.MoveTowards(_sink, 0f, 7f * dt);
            _sink = Mathf.Clamp(_sink, 0f, 60f);
            Vector3 v = Velocity;
            // Сваливание: нос опускается по потоку
            if (_lift < 0.95f && v.sqrMagnitude > 1f)
                rot = Quaternion.RotateTowards(rot, Quaternion.LookRotation(v, rot * Vector3.up), (1f - _lift) * 70f * dt);

            Vector3 np = pos + v * dt;
            Vector3 flatP = Flat(np);
            OutOfBounds = Mathf.Max(Mathf.Abs(np.x), Mathf.Abs(np.z)) > Bound;

            // Столкновения: препятствия (дома, деревья с коллайдерами, другие самолёты) и земля
            Vector3 d = np - pos; float len = d.magnitude;
            if (len > 0.001f)
            {
                int n = Physics.SphereCastNonAlloc(pos, 1.1f, d / len, _hits, len + 0.6f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = _hits[i].collider;
                    if (!c || Mine(c) || c is TerrainCollider || c.attachedRigidbody) continue;
                    if (_hits[i].distance <= 0f) continue;     // уже внутри (например, взлёт сквозь куст) — пропускаем
                    var other = c.GetComponentInParent<Aircraft>();
                    if (other && other.Alive) other.TakeDamage(9999f, PilotActorId, _hits[i].point);
                    transform.position = _hits[i].point;
                    Crash();
                    return;
                }
            }
            float gh = TerrainH(np);
            Agl = np.y - gh;
            if (np.y - GearH * 0.8f < gh)
            {
                Vector3 up = rot * Vector3.up;
                bool gentle = Alive && up.y > 0.9f && fwd.y > -0.25f && _spd < 44f && v.y > -9f;
                if (gentle)
                {
                    Airborne = false; _sink = 0;
                    _yaw = rot.eulerAngles.y;
                    transform.SetPositionAndRotation(new Vector3(np.x, gh + GearH, np.z), Quaternion.Euler(-4f, _yaw, 0));
                    BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Metal, 0), np, 0.6f);
                    return;
                }
                transform.position = new Vector3(np.x, gh + 0.5f, np.z);
                Crash();
                return;
            }
            transform.SetPositionAndRotation(np, rot);
        }

        // ---------------- Игрок ----------------

        int _enterFrame;
        Vector2 _mouse;
        Quaternion _camRot = Quaternion.identity;

        public bool CanEnter(PlayerController pc) =>
            Alive && !_crashed && !PlayerAboard && !Airborne && pc && pc.Team == Team;

        public void EnterPlayer(PlayerController pc)
        {
            if (!CanEnter(pc)) return;
            if (pilotBot > 0) DismountPilot();
            ReleaseBuddy();
            PlayerAboard = true; _enterFrame = Time.frameCount;
            _thrWant = 0f; _mouse = Vector2.zero; _camRot = transform.rotation;
            pc.EnterAircraft(this);
            BotAudio.PlayAt(SoundSynth.Hatch(), transform.position + Vector3.up, 0.7f);
        }

        /// <summary>Выход: на земле — у крыла, в воздухе — прыжок с парашютом (самолёт без пилота падает).</summary>
        public void ExitPlayer(bool killed, int killer)
        {
            if (!PlayerAboard) return;
            PlayerAboard = false;
            Cockpit = false;
            if (_rig != null && _rig.prop && !_crashed) _rig.prop.gameObject.SetActive(true);
            var pc = PlayerBridge.Controller;
            if (!pc || pc.Flying != this) return;
            Vector3 exit; bool chute = false;
            if (!Airborne || Agl < 4f)
            {
                exit = transform.position - transform.right * 5.6f;
                exit.y = TerrainH(exit) + 0.15f;
            }
            else { exit = transform.position - transform.up * 2.5f - Velocity.normalized * 3f; chute = true; }
            if (exit.y < TerrainH(exit) + 0.1f) exit.y = TerrainH(exit) + 0.15f;
            pc.ExitAircraft(exit, transform.eulerAngles.y, chute);
            _thrWant = Airborne ? _thrWant : 0f;
            if (killed)
                CombatWorld.Damage.Enqueue(new DamageEvent { targetActorId = CombatWorld.PlayerActorId, sourceActorId = killer, amount = 9999f, explosive = true, hitPoint = exit, direction = Vector3.up });
        }

        void PlayerInput(float dt)
        {
            var pc = PlayerBridge.Controller;
            if (!pc || pc.Flying != this) { PlayerAboard = false; return; }
            var inp = pc.Input;
            if (inp.interactPressed && Time.frameCount > _enterFrame + 1) { ExitPlayer(false, -1); return; }
            if (inp.cameraViewPressed) Cockpit = !Cockpit;
            _thrWant = Mathf.Clamp01(_thrWant + inp.move.y * dt * 0.8f);
            _brake = !Airborne && inp.move.y < -0.1f && _thrWant <= 0.001f;
            _yIn = Mathf.Clamp(inp.move.x, -1f, 1f);
            // Мышь — как в Ravenfield: скорость движения мыши = скорость поворота по тангажу и крену
            Vector2 m = inp.lookDelta / Mathf.Max(dt, 1e-4f);
            _mouse = Vector2.Lerp(_mouse, m, 1f - Mathf.Exp(-22f * dt));
            _pIn = Mathf.Clamp(_mouse.y * 0.012f, -1f, 1f);
            _rIn = Airborne ? Mathf.Clamp(_mouse.x * 0.012f, -1f, 1f) : 0f;
            _fireMg = inp.fireHeld; _fireRk = inp.aimHeld;
            // Далеко за краем карты — автопилот разворачивает к центру
            if (Airborne && Flat(transform.position).magnitude > Bound + 90f)
            {
                var keep = (_fireMg, _fireRk);
                Steer((-Flat(transform.position).normalized + Vector3.up * 0.15f).normalized);
                (_fireMg, _fireRk) = keep;
            }
            if (Airborne && Agl < 2f) _pIn = Mathf.Max(_pIn, 0f);
        }

        void LateUpdate()
        {
            if (!PlayerAboard || _crashed) return;
            var pc = PlayerBridge.Controller;
            if (!pc || pc.Flying != this) return;
            float dt = Time.deltaTime;
            pc.transform.SetPositionAndRotation(transform.position, Quaternion.Euler(0, transform.eulerAngles.y, 0));
            Vector3 camPos; Quaternion rot;
            if (Cockpit)
            {
                // Глаз пилота — внутри фонаря, над приборной доской; винт на оборотах не мерцает (лопасти скрыты)
                camPos = transform.TransformPoint(new Vector3(0, 0.8f, -0.05f));
                rot = transform.rotation;
            }
            else
            {
                _camRot = Quaternion.Slerp(_camRot, transform.rotation, 1f - Mathf.Exp(-5f * dt));
                camPos = transform.position + _camRot * new Vector3(0, 3.4f, -15.5f);
                float g = TerrainH(camPos) + 1.2f;
                if (camPos.y < g) camPos.y = g;
                rot = Quaternion.LookRotation(transform.position + transform.forward * 30f - camPos, _camRot * Vector3.up);
            }
            pc.cameraPivot.SetPositionAndRotation(camPos, rot);
            float fov = pc.BaseFov + Mathf.Clamp(_spd - 40f, 0f, 60f) * 0.12f;
            pc.worldCamera.fieldOfView = Mathf.Lerp(pc.worldCamera.fieldOfView, fov, dt * 4f);
            if (_rig.pilot) _rig.pilot.gameObject.SetActive(!Cockpit);
            if (_rig.prop) _rig.prop.gameObject.SetActive(!(Cockpit && _thr > 0.2f));
        }

        void Shake(float t)
        {
            var pc = PlayerBridge.Controller; if (!pc || !pc.worldCamera) return;
            var fx = pc.worldCamera.GetComponent<CameraEffects>();
            if (fx) fx.AddTrauma(t);
        }

        /// <summary>Точка сведения пулемётов — для прицела HUD.</summary>
        public Vector3 GunAimPoint => transform.position + transform.forward * Converge;

        /// <summary>Сетевая кукла у клиента: поза, газ (винт и мотор), пилот — с хоста.</summary>
        public void ApplyNetPose(Vector3 pos, Quaternion rot, float thr, bool pilot, bool airborne)
        {
            transform.SetPositionAndRotation(pos, rot);
            _thr = thr; Airborne = airborne;
            _propAngle = (_propAngle + (thr * 2200f + 300f) * Time.deltaTime) % 360f;
            if (_rig.prop) _rig.prop.localRotation = Quaternion.Euler(0, 0, -_propAngle);
            if (_rig.pilot && _rig.pilot.gameObject.activeSelf != pilot) _rig.pilot.gameObject.SetActive(pilot);
            if (_engine) { _engine.pitch = 0.85f + thr * 0.7f; _engine.volume = pilot || airborne ? 0.35f + thr * 0.55f : 0f; }
        }

        // ---------------- Помощь в прицеливании (игроку) ----------------
        // Небольшой «аимбот»: если вражеский самолёт или танк в узком конусе перед носом — очереди и ракеты
        // доворачиваются на него (ракетам — с упреждением). Конус маленький: навестись всё равно нужно самому.
        public Transform AssistTarget { get; private set; }
        public bool AssistLocked => AssistTarget;
        float _assistT;

        void UpdateAssist(float dt)
        {
            if ((_assistT -= dt) > 0) return;
            _assistT = 0.1f;
            AssistTarget = null;
            Vector3 pos = transform.position, fwd = transform.forward;
            float best = 7f;     // градусов
            foreach (var a in All)
            {
                if (!a || a == this || !a.Alive || a.Team == Team) continue;
                Vector3 to = a.transform.position - pos; float d = to.magnitude;
                if (d > 800f || d < 5f) continue;
                float ang = Vector3.Angle(fwd, to);
                if (ang < best) { best = ang; AssistTarget = a.transform; }
            }
            if (AssistTarget) return;
            best = 5f;
            foreach (var t in Tank.All)
            {
                if (!t || !t.Alive || t.Team == Team) continue;
                Vector3 to = t.transform.position + Vector3.up * 1.2f - pos; float d = to.magnitude;
                if (d > 700f) continue;
                float ang = Vector3.Angle(fwd, to);
                if (ang < best) { best = ang; AssistTarget = t.transform; }
            }
        }

        /// <summary>Куда стрелять по цели помощи: для пулемёта (мгновенная пуля) — в центр, для ракеты — с упреждением.</summary>
        bool AssistAim(Vector3 from, float projSpeed, out Vector3 point)
        {
            point = default;
            if (!PlayerAboard || !AssistTarget) return false;
            Vector3 tp; Vector3 tv;
            var a = AssistTarget.GetComponent<Aircraft>();
            if (a) { if (!a.Alive) return false; tp = a.transform.position; tv = a.Velocity; }
            else
            {
                var t = AssistTarget.GetComponent<Tank>();
                if (!t || !t.Alive) return false;
                tp = t.transform.position + Vector3.up * 1.2f; tv = t.transform.forward * t.Speed;
            }
            if (projSpeed > 0)
            {
                float tt = Vector3.Distance(from, tp) / projSpeed;
                tp += tv * tt + Vector3.up * (0.5f * 9.81f * 0.12f * tt * tt);
            }
            point = tp;
            return true;
        }

        // ---------------- Оружие ----------------

        void Weapons(float dt)
        {
            _mgCd -= dt; _rkCd -= dt;
            if (PlayerAboard) UpdateAssist(dt); else AssistTarget = null;
            if (_fireMg && MgAmmo > 0 && _mgCd <= 0) { _mgCd = 0.055f; FireMg(); }
            if (_fireRk && Rockets > 0 && _rkCd <= 0) { _rkCd = 0.28f; FireRocket(); }
        }

        void FireMg()
        {
            MgAmmo--; _shotN++;
            _gunSide ^= 1;
            var g = _rig.guns[_gunSide];
            Vector3 o = g ? g.position : transform.position + transform.forward * 2f;
            Vector3 aim = ((AssistAim(o, 0f, out var ap) ? ap : GunAimPoint) - o).normalized;
            float sp = PlayerAboard ? 0.45f : 0.9f;
            Vector3 dir = Quaternion.AngleAxis(Random.Range(-sp, sp), transform.up) * Quaternion.AngleAxis(Random.Range(-sp, sp), transform.right) * aim;
            const float range = 700f;
            float wd = range; RaycastHit wh = default; bool world = false;
            int n = Physics.RaycastNonAlloc(o, dir, _hits, range, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!Mine(_hits[i].collider) && _hits[i].distance < wd) { wd = _hits[i].distance; wh = _hits[i]; world = true; }
            Vector3 end = o + dir * wd;
            int shooter = PilotActorId;
            if (CombatWorld.Initialized && CombatWorld.RaycastActors(o, dir, wd, Team, shooter, out var ah))
            {
                float dmg = (ah.headshot ? 60f : 36f) * Mathf.Lerp(1f, 0.6f, ah.distance / range);
                CombatWorld.Damage.Enqueue(new DamageEvent { targetActorId = ah.actorId, sourceActorId = shooter, amount = dmg, hitPoint = ah.point, direction = dir, headshot = ah.headshot });
                if (FxPool.I) FxPool.I.Blood(ah.point, dir);
                end = ah.point;
            }
            else if (world)
            {
                var ac = wh.collider.GetComponentInParent<Aircraft>();
                var tk = ac ? null : wh.collider.GetComponentInParent<Tank>();
                if (ac)
                {
                    if (ac.Team != Team) ac.TakeDamage(13f, shooter, wh.point);
                    if (FxPool.I) FxPool.I.BrickChips(wh.point, wh.normal);
                }
                else if (tk)
                {
                    if (tk.Team != Team) tk.TakeDamage(tk.IsLight ? 10f : 1.2f, shooter, wh.point);
                    if (FxPool.I) FxPool.I.BrickChips(wh.point, wh.normal);
                }
                else
                {
                    var fence = wh.collider.GetComponent<Fence>(); if (fence) fence.Damage(25f, wh.point, dir);
                    var brick = wh.collider.GetComponent<Brick>(); if (brick) brick.Damage(15f);
                    if (FxPool.I && (_shotN & 1) == 0) FxPool.I.Impact(wh.point, wh.normal, _theme);
                }
            }
            if (FxPool.I)
            {
                if ((_shotN & 1) == 0) FxPool.I.Tracer(o, end, 950f);
                if (PlayerAboard || (_shotN & 3) == 0) FxPool.I.MuzzleFlashAt(o, dir);
            }
            if (PlayerAboard || (_shotN & 1) == 0) BotAudio.ShotAt(_mgClip, o, PlayerAboard ? 0.65f : 1f, 0.06f);
            if ((_shotN & 3) == 0) CombatWorld.EmitNoise(o, 260f, shooter, Team, 0);
            if (PlayerAboard) Shake(0.02f);
        }

        void FireRocket()
        {
            int i = MaxRockets - Rockets;
            // Поочерёдно с левого и правого крыла: 0,5,1,6,...
            int slot = (i % 2 == 0 ? 0 : 5) + i / 2;
            Rockets--;
            var r = _rig.rockets[Mathf.Clamp(slot, 0, 9)];
            Vector3 o = (r ? r.position : transform.position) + transform.forward * 0.9f;
            if (r) r.gameObject.SetActive(false);
            Vector3 baseDir = AssistAim(o, _spd + 150f, out var ap) ? (ap - o).normalized : transform.forward;
            float rs = PlayerAboard && AssistTarget ? 0.25f : 0.6f;
            Vector3 dir = Quaternion.AngleAxis(Random.Range(-rs, rs), transform.up) * Quaternion.AngleAxis(Random.Range(-rs, rs) + (AssistTarget ? 0f : 0.3f), transform.right) * baseDir;
            Projectile.Fire(Projectile.Type.Rocket, o, dir, _spd + 150f, 0.12f, PilotActorId, Team, 9f, 190f, 330f, 110f, transform);
            if (FxPool.I) { FxPool.I.MuzzleFlashAt(o, dir); FxPool.I.Smoke(o - dir * 1.5f); }
            BotAudio.PlayAt(SoundSynth.RocketLaunch(), o, PlayerAboard ? 0.8f : 1f, 0.06f);
            CombatWorld.EmitNoise(o, 320f, PilotActorId, Team, 1);
            if (PlayerAboard) Shake(0.08f);
        }

        void Rearm()
        {
            Rockets = MaxRockets; MgAmmo = MaxMg;
            foreach (var r in _rig.rockets) if (r) r.gameObject.SetActive(true);
        }

        float _rearmT;
        /// <summary>Стоит у своей базы — пополняет боекомплект и чинится.</summary>
        void RearmAtHome(float dt)
        {
            if ((Flat(transform.position - Home)).magnitude > 140f) { _rearmT = 0; return; }
            if (Rockets == MaxRockets && MgAmmo == MaxMg && Health >= maxHealth) return;
            _rearmT += dt;
            if (_rearmT < 3f) return;
            _rearmT = 0;
            Rearm();
            Health = Mathf.Min(maxHealth, Health + maxHealth * 0.5f);
            BotAudio.PlayAt(SoundSynth.AmmoPickup(), transform.position, 0.6f);
        }

        // ---------------- Урон ----------------

        static bool FriendlySource(int source, byte team)
        {
            if (source >= 0 && CombatWorld.Initialized && source < CombatWorld.Actors.Length) return CombatWorld.Actors[source].team == team;
            if (source <= -1000) { int ai = -1000 - source; foreach (var a in All) if (a && a.index == ai) return a.Team == team; return false; }
            if (source <= -10) { int ti = -10 - source; foreach (var t in Tank.All) if (t && t.index == ti) return t.Team == team; }
            return false;
        }

        public void TakeDamage(float amount, int source, Vector3 point)
        {
            if (!Alive || _crashed || amount <= 0) return;
            if (FriendlySource(source, Team) && amount < 9000f) return;
            Health -= amount;
            LastHitTime = Time.time;
            HitFeedback.Report(source, Health <= 0);
            if (source != PilotActorId) _lastAttacker = source;
            if (PlayerAboard) Shake(Mathf.Clamp(amount / 200f, 0.05f, 0.4f));
            if (FxPool.I && Random.value < 0.35f) FxPool.I.BrickChips(point, (point - transform.position).normalized);
            if (Random.value < 0.3f) BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Metal, Random.Range(0, 3)), point, 0.6f);
            if (Health <= 0) Kill(source);
        }

        void Kill(int source)
        {
            if (Health > 0) Health = 0;
            if (_killed) return;
            _killed = true; _killer = source;
            if (PlayerAboard) ExitPlayer(true, source);
            KillPilot(source);
            Destroyed?.Invoke(this, source);
            if (FxPool.I) FxPool.I.Explosion(transform.position);
            if (!Airborne) Crash();
        }
        bool _killed; int _killer = -1;

        void Crash()
        {
            if (_crashed) return;
            if (!_killed)
            {
                Health = 0;
                Kill(Time.time - LastHitTime < 12f ? _lastAttacker : -1);
                if (_crashed) return;
            }
            _crashed = true;
            Vector3 p = transform.position;
            float h = TerrainH(p);
            Vector3 c = new(p.x, Mathf.Max(p.y, h + 0.6f), p.z);
            Explosions.Detonate(c, 9f, 150f, 140f, _killer, Team, Explosions.Kind.TankWreck, true, 300f, transform);
            if (FxPool.I) for (int i = 0; i < 3; i++) FxPool.I.Explosion(c + Random.insideUnitSphere * 2.5f + Vector3.up);
            // Обломки — на землю
            transform.SetPositionAndRotation(new Vector3(p.x, h + 0.7f, p.z), Quaternion.Euler(Random.Range(-8f, 4f), transform.eulerAngles.y, Random.Range(-25f, 25f)));
            if (_rig.prop) _rig.prop.gameObject.SetActive(false);
            if (_rig.pilot) _rig.pilot.gameObject.SetActive(false);
            foreach (var r in _rig.rockets) if (r) r.gameObject.SetActive(false);
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i]) mats[i] = ProceduralModels.Mat(Color.Lerp(mats[i].color, new Color(0.06f, 0.055f, 0.05f), 0.8f), 0.05f, 0.1f);
                r.sharedMaterials = mats;
            }
            MapProps.SmokeColumn(transform, transform.position + Vector3.up * 1.2f);
            WreckLimiter.Add(gameObject);
            if (_engine) _engine.Stop();
            _wreckUntil = Time.time + 60f;          // обломки исчезают через минуту
        }

        // ---------------- Визуал и звук ----------------

        void Visuals(float dt)
        {
            float rpm = (_thr * 2200f + (Airborne ? _spd * 8f : 0f) + 300f) * (Alive ? 1f : 0.3f);
            _propAngle = (_propAngle + rpm * dt) % 360f;
            if (_rig.prop) _rig.prop.localRotation = Quaternion.Euler(0, 0, -_propAngle);
            if (_rig.pilot && !PlayerAboard) { bool show = HasPilot; if (_rig.pilot.gameObject.activeSelf != show) _rig.pilot.gameObject.SetActive(show); }
            if (_engine && _audioOn)
            {
                float on = HasPilot || Airborne ? 1f : 0f;
                _engine.pitch = Mathf.Lerp(_engine.pitch, 0.85f + _thr * 0.7f + _spd / 160f, dt * 3f);
                _engine.volume = Mathf.Lerp(_engine.volume, on * (0.35f + _thr * 0.55f), dt * 3f);
            }
            // Дым из повреждённого мотора, горящий самолёт — сплошной шлейф
            if (FxPool.I && (Health < maxHealth * 0.4f || !Alive))
            {
                _smokeT -= dt;
                if (_smokeT <= 0)
                {
                    _smokeT = Alive ? 0.18f : 0.06f;
                    FxPool.I.Smoke(transform.position + transform.forward * 2.5f);
                    if (!Alive && Random.value < 0.25f) FxPool.I.Explosion(transform.position + Random.insideUnitSphere);
                }
            }
        }

        static readonly List<(float d, Aircraft a)> _byDist = new();

        /// <summary>Звучат только 6 ближайших моторов (в пределах 650 м) — без «каши» голосов.</summary>
        public static void AudioBudget(Vector3 listener)
        {
            _byDist.Clear();
            foreach (var a in All) if (a && a._engine) _byDist.Add((a.HasPilot || a.Airborne ? (a.transform.position - listener).sqrMagnitude : 1e12f, a));
            _byDist.Sort((x, y) => x.d.CompareTo(y.d));
            for (int i = 0; i < _byDist.Count; i++)
            {
                var a = _byDist[i].a;
                bool on = !a._crashed && (a.HasPilot || a.Airborne) && i < 6 && _byDist[i].d < 650f * 650f;
                if (on == a._audioOn) continue;
                a._audioOn = on;
                if (on) { if (!a._engine.isPlaying) a._engine.Play(); }
                else a._engine.Stop();
            }
        }

        // ---------------- Экипаж (бот-пилот) ----------------

        static EntityManager? Em()
        {
            var w = World.DefaultGameObjectInjectionWorld;
            return w != null && w.IsCreated ? w.EntityManager : null;
        }

        static Entity BotEntity(int actorId) =>
            BotRegistry.ActorToEntity.IsCreated && actorId > 0 && actorId < BotRegistry.ActorToEntity.Length ? BotRegistry.ActorToEntity[actorId] : Entity.Null;

        /// <summary>Этот актор — лётчик (игрок или бот) в самолёте: урон ему получает самолёт.</summary>
        public static Aircraft OfPilot(int actorId)
        {
            if (All.Count == 0) return null;
            if (actorId == CombatWorld.PlayerActorId)
            {
                var pc = PlayerBridge.Controller;
                return pc && pc.Flying && pc.Flying.Alive ? pc.Flying : null;
            }
            if (actorId <= 0) return null;
            foreach (var a in All) if (a && a.pilotBot == actorId && a.Alive) return a;
            return null;
        }

        public static bool IsAirCrew(int actorId) => OfPilot(actorId) != null;

        int _buddy = -1; float _buddyUntil, _buddyRetry, _buddyRefresh;

        void ReleaseBuddy()
        {
            if (_buddy >= 0) { FrontPlanner.Release(_buddy); Tank.Claimed.Remove(_buddy); }
            _buddy = -1;
        }

        /// <summary>Пустой самолёт у базы: ближайший свой боец бежит к нему и садится в кабину (если рядом нет игрока).</summary>
        void Recruit()
        {
            if (Net.NetSession.IsClient || Airborne || !Alive || !CombatWorld.Initialized) { ReleaseBuddy(); return; }
            var pc = PlayerBridge.Controller;
            if (pc && pc.Team == Team && !pc.Vehicle && !pc.Flying && PlayerBridge.Health && PlayerBridge.Health.Alive &&
                Vector3.Distance(pc.transform.position, transform.position) < 30f) { ReleaseBuddy(); return; }   // место ждёт игрока
            float now = Time.time;
            if (now < _spawnTime + 3f) return;
            var actors = CombatWorld.Actors;
            var em = Em();
            if (em == null) return;
            if (_buddy >= 0)
            {
                bool ok = _buddy < actors.Length && actors[_buddy].alive && now < _buddyUntil;
                if (!ok) { ReleaseBuddy(); _buddyRetry = now + 2f; return; }
                if (Vector3.Distance(actors[_buddy].position, transform.position) < 5.5f)
                {
                    int id = _buddy; ReleaseBuddy();
                    var e = BotEntity(id);
                    if (e != Entity.Null && em.Value.Exists(e) && !VehicleSeats.IsBoarded(em.Value, e))
                    {
                        VehicleSeats.Board(em.Value, e, 5000 + index, 1);
                        pilotBot = id;
                        _state = AiState.Takeoff;
                        BotAudio.PlayAt(SoundSynth.Hatch(), transform.position + Vector3.up, 0.5f);
                    }
                    return;
                }
                if (now > _buddyRefresh) { _buddyRefresh = now + 0.8f; FrontPlanner.Force(_buddy, new float2(transform.position.x, transform.position.z)); }
                return;
            }
            if (now < _buddyRetry) return;
            _buddyRetry = now + 1.5f;
            float best = 130f; int pick = -1;
            for (int i = 1; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team != Team || Net.NetSession.IsRemoteSlot(a.actorId) || Tank.Claimed.Contains(a.actorId)) continue;
                float dd = Vector3.Distance(a.position, transform.position);
                if (dd >= best) continue;
                var e = BotEntity(a.actorId);
                if (e == Entity.Null || !em.Value.Exists(e) || VehicleSeats.IsBoarded(em.Value, e)) continue;
                best = dd; pick = a.actorId;
            }
            if (pick < 0) return;
            _buddy = pick; _buddyUntil = now + 30f; _buddyRefresh = now + 0.8f; Tank.Claimed.Add(pick);
            FrontPlanner.Force(pick, new float2(transform.position.x, transform.position.z));
        }

        /// <summary>Тело бота-пилота держим в кабине: по его позиции стреляют и видят самолёт боты.</summary>
        void KeepPilot()
        {
            if (pilotBot <= 0) return;
            var em = Em(); if (em == null) return;
            var m = em.Value;
            var e = BotEntity(pilotBot);
            var actors = CombatWorld.Actors;
            bool alive = actors.IsCreated && pilotBot < actors.Length && actors[pilotBot].alive;
            if (e == Entity.Null || !m.Exists(e) || !VehicleSeats.IsBoarded(m, e) || !alive)
            {
                if (e != Entity.Null && m.Exists(e)) VehicleSeats.Unboard(m, e);
                pilotBot = -1; return;
            }
            if (m.HasComponent<LocalTransform>(e))
            {
                var lt = m.GetComponentData<LocalTransform>(e);
                lt.Position = transform.position - Vector3.up * 0.9f;
                lt.Rotation = quaternion.RotateY(math.radians(transform.eulerAngles.y));
                m.SetComponentData(e, lt);
            }
            if (m.HasComponent<BotMotion>(e))
            {
                var mo = m.GetComponentData<BotMotion>(e);
                mo.velocity = Velocity; mo.speed = 0;
                m.SetComponentData(e, mo);
            }
        }

        Vector3 GroundBeside()
        {
            Vector3 p = transform.position - transform.right * 5.5f;
            p.y = TerrainH(p) + 0.05f;
            return p;
        }

        void DismountPilot()
        {
            var em = Em(); int id = pilotBot; pilotBot = -1;
            if (em == null) return;
            var e = BotEntity(id);
            if (e == Entity.Null || !em.Value.Exists(e)) return;
            if (em.Value.HasComponent<LocalTransform>(e))
            {
                var lt = em.Value.GetComponentData<LocalTransform>(e);
                lt.Position = GroundBeside(); em.Value.SetComponentData(e, lt);
            }
            VehicleSeats.Unboard(em.Value, e);
        }

        void KillPilot(int killer)
        {
            if (pilotBot <= 0) return;
            int id = pilotBot;
            Vector3 p = transform.position; p.y = TerrainH(p) + 0.05f;
            DismountPilot();
            var em = Em(); var e = BotEntity(id);
            if (em != null && e != Entity.Null && em.Value.Exists(e) && em.Value.HasComponent<LocalTransform>(e))
            {
                var lt = em.Value.GetComponentData<LocalTransform>(e);
                lt.Position = p; em.Value.SetComponentData(e, lt);
            }
            CombatWorld.Damage.Enqueue(new DamageEvent { targetActorId = id, sourceActorId = killer, amount = 9999f, explosive = true, hitPoint = p, direction = Vector3.up });
        }

        // ---------------- ИИ пилота ----------------

        enum AiState : byte { Takeoff, Patrol, Rearm }
        AiState _state;
        float _think, _extendUntil;
        Aircraft _air;
        Tank _tankTgt;
        int _actorTgt = -1;
        bool _attackRun;
        Vector3 _aimErr;

        void ThinkAI(float dt)
        {
            _fireMg = _fireRk = false; _brake = false;
            if (!Airborne)
            {
                _thrWant = 1f; _yIn = 0; _rIn = 0;
                _pIn = _spd > TakeoffSpeed - 2f ? 0.6f : 0f;
                _state = AiState.Takeoff;
                return;
            }
            Vector3 pos = transform.position;
            _think -= dt;
            if (_think <= 0) { _think = 0.4f + Random.value * 0.25f; PickTarget(); }

            Vector3 want; float thr = 1f;
            if (_state == AiState.Takeoff)
            {
                want = Flat(transform.forward).normalized + Vector3.up * 0.45f;
                if (Agl > 70f) _state = AiState.Patrol;
            }
            else if (_state == AiState.Rearm)
            {
                Vector3 aim = Home; aim.y = TerrainH(Home) + Cruise + 30f;
                want = (aim - pos).normalized;
                if (Flat(pos - Home).magnitude < 140f) { Rearm(); _state = AiState.Patrol; }
            }
            else if (Time.time < _extendUntil)
                want = Flat(transform.forward).normalized + Vector3.up * 0.4f;
            else if (_air && _air.Alive && _air.Airborne)
            {
                // Воздушный бой: заход в хвост, огонь при совмещении
                Vector3 tp = _air.transform.position + _aimErr;
                Vector3 to = tp - pos; float d = to.magnitude;
                want = to / Mathf.Max(d, 0.1f);
                float ang = Vector3.Angle(transform.forward, to);
                if (ang < 4f && d < 450f) _fireMg = true;
                if (d < 45f && Vector3.Dot(transform.forward, to) > 0) _extendUntil = Time.time + 2.5f;   // не таранить
            }
            else if (_tankTgt || _actorTgt >= 0)
            {
                Vector3 tp = TargetPos();
                Vector3 to = tp - pos; float hd = Flat(to).magnitude;
                if (!_attackRun)
                {
                    Vector3 aim = new(tp.x, TerrainH(tp) + Cruise, tp.z);
                    want = (aim - pos).normalized;
                    if (hd < 480f && hd > 250f && Agl > 70f) _attackRun = true;
                    else if (hd < 250f) want = Flat(transform.forward).normalized + Vector3.up * 0.2f;   // проскочили — заходим снова
                }
                else
                {
                    // Пикирование на цель: ракеты по технике (и иногда по пехоте), пулемёт по всем
                    want = to.normalized; thr = 0.65f;
                    float ang = Vector3.Angle(transform.forward, to), d = to.magnitude;
                    if (ang < 5f && d < 420f && Rockets > 0 && (_tankTgt || (_shotN & 7) == 0)) _fireRk = true;
                    if (ang < 4f && d < 360f) _fireMg = true;
                    if (d < 130f || Agl < 50f) { _attackRun = false; _extendUntil = Time.time + 5f; _tankTgt = null; _actorTgt = -1; }
                }
            }
            else
            {
                // Барражирование над полем боя
                Vector3 c = Vector3.zero;
                Vector3 r = Flat(pos - c); if (r.sqrMagnitude < 1f) r = Vector3.forward;
                Vector3 tan = Vector3.Cross(Vector3.up, r.normalized) * (index % 2 == 0 ? 1f : -1f);
                Vector3 aim = c + r.normalized * 260f + tan * 220f; aim.y = TerrainH(aim) + Cruise;
                want = (aim - pos).normalized;
            }
            // Попутно: пехота противника прямо по курсу — короткая очередь из пулемётов (если не идёт воздушный бой)
            if (!_fireMg && !(_air && _air.Alive) && MgAmmo > 150 && _oppTgt >= 0)
            {
                var acts = CombatWorld.Actors;
                if (_oppTgt < acts.Length && acts[_oppTgt].alive)
                {
                    Vector3 to = (Vector3)acts[_oppTgt].position + Vector3.up - pos;
                    if (to.magnitude < 380f && Vector3.Angle(transform.forward, to) < 6f) _fireMg = true;
                }
                else _oppTgt = -1;
            }
            if (Rockets == 0 && MgAmmo < 100 && _state == AiState.Patrol) _state = AiState.Rearm;

            // Безопасность: земля, рельеф впереди, потолок, край карты
            Vector3 v = Velocity;
            float tti = v.y < -1f ? (Agl - 25f) / -v.y : 99f;
            Vector3 ahead = pos + v * 2.5f;
            if (Agl < 40f || tti < 3f || ahead.y < TerrainH(ahead) + 30f)
            {
                want = Flat(transform.forward).normalized + Vector3.up * 1.3f; thr = 1f; _fireRk = false;
                if (_attackRun) { _attackRun = false; _extendUntil = Time.time + 3f; }
            }
            if (Stalling) { want = Flat(transform.forward).normalized - Vector3.up * 0.2f; thr = 1f; }
            Vector3 fp = Flat(pos);
            if (fp.magnitude > 540f) want = Vector3.Lerp(want, (-fp.normalized + Vector3.up * 0.1f).normalized, 0.8f);
            if (pos.y > CeilingY - 120f) want.y = Mathf.Min(want.y, -0.1f);
            Steer(want.normalized);
            _thrWant = thr;
        }

        Vector3 TargetPos()
        {
            if (_tankTgt) return _tankTgt.transform.position + Vector3.up * 1.5f;
            var actors = CombatWorld.Actors;
            if (_actorTgt >= 0 && _actorTgt < actors.Length) return (Vector3)actors[_actorTgt].position + Vector3.up;
            return Vector3.zero;
        }

        int _oppTgt = -1;

        /// <summary>Пехотинец противника в конусе перед носом — цель для попутной очереди.</summary>
        void ScanOpportunity()
        {
            _oppTgt = -1;
            var actors = CombatWorld.Actors;
            if (!actors.IsCreated || actors.Length == 0) return;
            Vector3 pos = transform.position, fwd = transform.forward;
            float best = 8f;
            for (int k = 0; k < 24; k++)
            {
                int i = Random.Range(0, actors.Length);
                var a = actors[i];
                if (!a.alive || a.team == Team) continue;
                Vector3 to = (Vector3)a.position - pos;
                if (to.sqrMagnitude > 380f * 380f) continue;
                float ang = Vector3.Angle(fwd, to);
                if (ang < best && !IsAirCrew(a.actorId)) { best = ang; _oppTgt = a.actorId; }
            }
        }

        void PickTarget()
        {
            Vector3 pos = transform.position;
            ScanOpportunity();
            // Приоритет: 1) самолёты противника, 2) танки и машины, 3) пехота
            // 1) Вражеские самолёты в воздухе
            _air = null; float bd = 1600f;     // вражеские самолёты ищем по всей карте — это главная цель
            foreach (var a in All)
            {
                if (!a || a == this || !a.Alive || !a.Airborne || a.Team == Team) continue;
                float d = Vector3.Distance(a.transform.position, pos);
                if (d < bd) { bd = d; _air = a; }
            }
            if (_air) { _aimErr = Random.insideUnitSphere * 3f; _attackRun = false; return; }
            if (_attackRun) return;      // уже пикируем — цель не меняем
            // 2) Вражеская техника
            if (_tankTgt && (!_tankTgt.Alive || Random.value < 0.15f)) _tankTgt = null;
            if (!_tankTgt)
            {
                float bt = 900f;
                foreach (var t in Tank.All)
                {
                    if (!t || !t.Alive || t.Team == Team) continue;
                    float d = Vector3.Distance(t.transform.position, pos) * Random.Range(0.8f, 1.2f);
                    if (d < bt) { bt = d; _tankTgt = t; }
                }
            }
            if (_tankTgt) { _actorTgt = -1; return; }
            // 3) Пехота противника (случайная выборка)
            var actors = CombatWorld.Actors;
            if (!actors.IsCreated || actors.Length == 0) return;
            if (_actorTgt >= 0 && (_actorTgt >= actors.Length || !actors[_actorTgt].alive)) _actorTgt = -1;
            if (_actorTgt >= 0) return;
            float ba = 900f;
            for (int k = 0; k < 16; k++)
            {
                int i = Random.Range(0, actors.Length);
                var a = actors[i];
                if (!a.alive || a.team == Team || IsAirCrew(a.actorId) || BaseZone.Near(a.position, a.team)) continue;
                float d = Vector3.Distance(a.position, pos);
                if (d < ba) { ba = d; _actorTgt = a.actorId; }
            }
        }

        /// <summary>Перевод желаемого направления в рули: крен в сторону цели + взятие ручки на себя (как пилот Ravenfield).</summary>
        void Steer(Vector3 want)
        {
            Vector3 l = transform.InverseTransformDirection(want);
            float off = Vector3.Angle(Vector3.forward, l);
            float bank = Mathf.Atan2(-transform.right.y, transform.up.y) * Mathf.Rad2Deg;     // + — крен вправо, ±180 — вверх ногами
            float rollErr = off < 20f ? Mathf.Clamp(l.x * 120f, -30f, 30f) - bank : Mathf.Atan2(l.x, l.y) * Mathf.Rad2Deg;
            _rIn = Mathf.Clamp(rollErr / 35f, -1f, 1f);
            float pitchErr = Mathf.Atan2(l.y, Mathf.Max(0.1f, l.z)) * Mathf.Rad2Deg;
            float pc = Mathf.Clamp(pitchErr / 15f, -0.6f, 1f);
            if (off >= 20f && Mathf.Abs(rollErr) > 45f) pc *= 0.3f;
            _pIn = pc;
            _yIn = Mathf.Clamp(Mathf.Atan2(l.x, Mathf.Max(0.1f, l.z)) * Mathf.Rad2Deg / 10f, -1f, 1f) * 0.6f;
        }
    }
}
