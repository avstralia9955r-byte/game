using System;
using System.Collections.Generic;
using Frontline.Bots;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Frontline
{
    /// <summary>
    /// Управляемый танк (Pz.IV / Т-34-85). Кинематическое движение по рельефу (4 точки опоры → тангаж/крен),
    /// упор в препятствия, давит заборы и кирпичные стены, давит пехоту. Башня и пушка доворачиваются к точке
    /// прицеливания с ограниченной скоростью. Пушка (фугас, баллистика) и спаренный пулемёт.
    /// Экипаж: игрок (W/S газ, A/D поворот, мышь — башня, ЛКМ огонь, ПКМ прицел наводчика, 1/2 пушка/пулемёт, E выход)
    /// или боты (едут к точкам, ищут цели, упреждают, объезжают препятствия, сдают назад, если застряли).
    /// </summary>
    public partial class Tank : MonoBehaviour
    {
        public static readonly List<Tank> All = new();
        public static event Action<Tank, int> Destroyed;     // танк, actorId уничтожившего

        public enum Crew : byte { None, Bot, Player }

        public Faction faction;
        public byte Team => (byte)faction;
        /// <summary>Лёгкая машина с пулемётом (джип): быстрая, без пушки, пробивается обычными пулями.</summary>
        public bool IsLight { get; private set; }
        public string DisplayName => IsLight ? (faction == Faction.Germany ? "Kübelwagen с MG-34" : "ГАЗ-64 с ДП")
                                             : faction == Faction.Germany ? "Pz.Kpfw. IV" : "Т-34-85";
        public int index;
        public Crew crew;
        public int DriverActorId => crew == Crew.Player ? CombatWorld.PlayerActorId : crew == Crew.Bot && driverBot > 0 ? driverBot : -10 - index;
        public TankModels.Rig rig;

        public float maxHealth = 1500f, maxSpeed = 10.5f, reverseSpeed = 4f, turnRate = 34f, traverseRate = 30f, elevateRate = 18f;
        public float reloadTime = 4.5f, shellSpeed = 260f;
        public int shells = 40;

        public float Health { get; private set; }
        /// <summary>Шанс, что снаряд/ракета не отрикошетит: 95%, у танка игрока со специализацией «Танкист» — 70%.</summary>
        public float HitChance => PlayerAboard && Shop.HasPerk(Shop.PerkTanker) ? 0.7f : 0.95f;
        public bool Alive => Health > 0;
        public float Speed => _speed;
        public float TurretYaw => _turretYaw;
        public float GunPitch => _gunPitch;

        /// <summary>Сетевая «кукла» у клиента: поза приходит с хоста, своя логика выключена.</summary>
        public void ApplyNetPose(Vector3 pos, float yaw, float turretYaw, float gunPitch)
        {
            _yaw = yaw; _turretYaw = turretYaw; _gunPitch = gunPitch;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            rig.turret.localRotation = Quaternion.Euler(0, turretYaw, 0);
            rig.gun.localRotation = Quaternion.Euler(gunPitch, 0, 0);
            if (_crewVis && _crewVis.gameObject.activeSelf != (crew != Crew.None)) _crewVis.gameObject.SetActive(crew != Crew.None);
        }
        public float Reload01 => 1f - Mathf.Clamp01(_reload / reloadTime);
        public int WeaponSlot { get; private set; }            // 0 — пушка, 1 — пулемёт
        public bool Zoomed { get; private set; }
        public float LastHitTime { get; private set; } = -99f;
        public Vector3 AimPoint => _aimPoint;
        public Vector3 GunHitPoint { get; private set; }       // куда реально смотрит ствол (для второго маркера)
        public float WreckTime { get; private set; }
        public float EmptySince { get; set; }

        // Управление (пишет игрок или ИИ)
        [NonSerialized] public float throttle, steer;
        [NonSerialized] public bool fireCannon, fireMg;
        Vector3 _aimPoint;

        BoxCollider _hullCol, _turretCol;
        Transform _crewVis;
        AudioSource _engine, _tracks, _turretSrc;
        float _speed, _yaw, _turretYaw, _gunPitch, _pitchVis, _rollVis, _reload, _mgNext, _recoil, _noiseTimer, _lastTurretYaw;
        float _groundY;
        readonly RaycastHit[] _hits = new RaycastHit[24];
        readonly Dictionary<int, float> _runOver = new();
        AudioClip _cannonClip, _mgClip;
        MapTheme _theme;

        // ---------------- Создание ----------------

        public static Tank Create(Faction f, MapTheme theme, Vector3 pos, float yaw, int index, Transform parent, int layer, bool light = false)
        {
            var rig = light ? JeepModels.Build(f, theme, parent) : TankModels.Build(f, theme, parent);
            var t = rig.root.AddComponent<Tank>();
            t.rig = rig; t.faction = f; t.index = index; t._theme = theme; t.IsLight = light;
            if (light)
            {
                t.maxHealth = 320f; t.maxSpeed = 17f; t.reverseSpeed = 6f; t.turnRate = 75f;
                t.traverseRate = 240f; t.elevateRate = 160f; t.shells = 0;
                t._crewVis = rig.hull.Find("Crew");
                t.InitSeats();
            }
            t.Health = t.maxHealth; t._yaw = yaw;
            t.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            t._groundY = pos.y;
            if (f == Faction.USSR && !light) { t.maxSpeed = 11.5f; t.turnRate = 36f; t.traverseRate = 26f; t.reloadTime = 5f; }
            t._hullCol = rig.hull.gameObject.AddComponent<BoxCollider>();
            t._hullCol.center = new Vector3(0, rig.hullCenterY, 0); t._hullCol.size = rig.hullSize;
            t._turretCol = rig.turret.gameObject.AddComponent<BoxCollider>();
            t._turretCol.center = new Vector3(0, 0.42f, -0.1f); t._turretCol.size = rig.turretSize;
            foreach (var c in rig.root.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;

            t._engine = t.Loop(SoundSynth.Engine(f == Faction.USSR && !light), 0.75f, 8f, light ? 180f : 260f);
            if (light) t._engine.pitch = 1.3f;
            t._tracks = t.Loop(SoundSynth.Tracks(), 0f, 6f, 160f);
            t._turretSrc = t.Loop(SoundSynth.TurretTraverse(), 0f, 3f, 40f);
            t._cannonClip = SoundSynth.TankCannon(f == Faction.Germany);
            var db = GameDatabase.Instance;
            var mg = db ? db.Arsenal(f).Get(WeaponCategory.MachineGun) : null;
            t._mgClip = mg ? mg.fireSound : SoundSynth.Gunshot("coax", WeaponCategory.MachineGun);
            t.EmptySince = Time.time;
            t.SetupLod();
            All.Add(t);
            return t;
        }

        AudioSource Loop(AudioClip clip, float vol, float min, float max)
        {
            var go = new GameObject("Audio"); go.transform.SetParent(rig.hull, false); go.transform.localPosition = new Vector3(0, 1.2f, -1.5f);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = vol; s.spatialBlend = 1f; s.minDistance = min; s.maxDistance = max;
            s.rolloffMode = AudioRolloffMode.Linear; s.dopplerLevel = 0.5f; s.time = Random.Range(0f, clip.length * 0.9f);
            s.Play();
            return s;
        }

        void OnDestroy() => All.Remove(this);

        // ---------------- Звуковой бюджет техники ----------------
        // 128 танков × 3 зацикленных источника — сотни голосов: движок «съедал» их и звуки рвались.
        // Раз в секунду: звучат только 8 ближайших к слушателю машин (в пределах 280 м), у остальных источники выключены.
        bool _audioOn = true;
        static readonly List<(float d, Tank t)> _byDist = new();

        public static void AudioBudget(Vector3 listener)
        {
            _byDist.Clear();
            foreach (var t in All) if (t && t._engine) _byDist.Add(((t.transform.position - listener).sqrMagnitude, t));
            _byDist.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < _byDist.Count; i++)
            {
                var t = _byDist[i].t;
                bool on = i < 8 && _byDist[i].d < 280f * 280f && t.Alive;
                if (on == t._audioOn) continue;
                t._audioOn = on;
                foreach (var s in new[] { t._engine, t._tracks, t._turretSrc })
                    if (s) { s.enabled = on; if (on && !s.isPlaying) s.Play(); }
            }
        }

        // ---------------- Геометрия ----------------

        public Vector3 Center => transform.position + transform.up * rig.hullCenterY;

        public float DistanceTo(Vector3 p)
        {
            float d = _hullCol ? Vector3.Distance(_hullCol.ClosestPoint(p), p) : Vector3.Distance(Center, p);
            if (_turretCol) d = Mathf.Min(d, Vector3.Distance(_turretCol.ClosestPoint(p), p));
            return d;
        }

        bool Mine(Collider c) => c && c.transform.IsChildOf(transform);

        // ---------------- Цикл ----------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (!Alive) { WreckUpdate(dt); return; }
            // Дальняя техника (>220 м от камеры, без игрока, не в прицеле) обновляется через кадр с двойным шагом
            var cam = Camera.main;
            float camD2 = cam ? (cam.transform.position - transform.position).sqrMagnitude : 0f;
            float near = Application.isMobilePlatform ? 70f : 120f;
            if (!PlayerAboard && cam && cam.fieldOfView > 35f && camD2 > near * near)
            {
                // 120–300 м — раз в 2 кадра, дальше — раз в 4 (с накопленным шагом)
                int every = camD2 > 300f * 300f ? 4 : 2;
                if (Application.isMobilePlatform) every *= 2;      // телефон: дальние танки ещё реже
                _simAcc += dt;
                if (((Time.frameCount + index) % every) != 0) return;
                dt = Mathf.Min(_simAcc, 0.15f); _simAcc = 0;
            }
            else _simAcc = 0;

            // Пустая стоящая техника «спит»: без лучей опоры, наводки и звука (на картах с сотней танков — главная экономия)
            bool empty = crew == Crew.None && !PlayerAboard && driverBot < 0 && gunnerBot < 0 && passengerBot < 0 && !RemoteRider;
            if (empty && Mathf.Abs(_speed) < 0.05f)
            {
                _idleT += dt;
                if (_idleT > 1.5f)
                {
                    if (!IsLight) TankCrewUpdate(dt);
                    fireCannon = fireMg = false; throttle = steer = 0;
                    if (_audioOn && _engine && _engine.volume > 0.01f) { _engine.volume = 0; _tracks.volume = 0; _turretSrc.volume = 0; }
                    return;
                }
            }
            else _idleT = 0;

            if (!IsLight) TankCrewUpdate(dt);
            if (IsLight) JeepControl(dt);                       // джип: водитель, стрелок и пассажир — по отдельности
            else if (crew == Crew.Bot) ThinkAI(dt);
            else if (crew == Crew.Player) PlayerInputUpdate();
            else { throttle = steer = 0; fireCannon = fireMg = false; }

            if (_crewVis && _crewVis.gameObject.activeSelf != (crew != Crew.None)) _crewVis.gameObject.SetActive(crew != Crew.None);
            Drive(dt);
            AimTurret(dt);
            Weapons(dt);
            RunOver();
            Sounds(dt);

            _noiseTimer -= dt;
            if (_noiseTimer <= 0) { _noiseTimer = 0.8f; CombatWorld.EmitNoise(transform.position, 70f + Mathf.Abs(_speed) * 10f, DriverActorId, Team, 2); }
        }

        float _simAcc, _probeT, _idleT;
        bool _bF, _bL, _bR;

        /// <summary>
        /// Уровни детализации: вблизи — вся модель; вдали (мельче ~9% высоты экрана) — только крупные части
        /// (корпус, башня, ствол, катки); мелочь (заклёпки, инструмент, траки, экипаж) не рисуется. LODGroup учитывает
        /// угол обзора камеры, поэтому в прицеле танка или в оптике модель снова полная. Совсем вдали тени не нужны.
        /// </summary>
        void SetupLod()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var big = new List<Renderer>();
            foreach (var r in all) if (r.bounds.size.magnitude > (IsLight ? 0.7f : 1.0f)) big.Add(r);
            if (big.Count == 0 || big.Count == all.Length) return;
            var lg = gameObject.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(0.09f, all), new LOD(0.0002f, big.ToArray()) });
            if (Application.isMobilePlatform)       // телефон: тень дают только крупные части
                foreach (var r in all) if (!big.Contains(r)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lg.fadeMode = LODFadeMode.None;
            lg.RecalculateBounds();
        }

        void Drive(float dt)
        {
            float target = throttle >= 0 ? throttle * maxSpeed : throttle * reverseSpeed;
            bool braking = Mathf.Abs(target) < Mathf.Abs(_speed) || Mathf.Sign(target) != Mathf.Sign(_speed);
            _speed = Mathf.MoveTowards(_speed, target, (braking ? (IsLight ? 10f : 7f) : (IsLight ? 5.5f : 2.6f)) * dt);
            float rate = Mathf.Lerp(turnRate, turnRate * 0.65f, Mathf.Abs(_speed) / maxSpeed);
            float dYaw = steer * rate * dt;
            // Колёсная машина на месте не разворачивается и при заднем ходе рулит «наоборот»
            if (IsLight) dYaw *= Mathf.Clamp(_speed / 3f, -1f, 1f);

            Vector3 pos = transform.position;
            Quaternion yawRot = Quaternion.Euler(0, _yaw + dYaw, 0);
            Vector3 fwd = yawRot * Vector3.forward;
            Vector3 delta = fwd * (_speed * dt);

            if (Mathf.Abs(_speed) > 0.02f || Mathf.Abs(dYaw) > 0.01f)
            {
                Vector3 dir = _speed >= 0 ? fwd : -fwd;
                if (Blocked(pos, yawRot, dir, Mathf.Abs(_speed * dt) + 0.2f))
                {
                    if (Mathf.Abs(_speed) > 4f) { BotAudio.PlayAt(SoundSynth.ArmorHit(), pos + dir * 3f, 0.5f); if (crew == Crew.Player) Shake(0.25f); }
                    _speed = -_speed * 0.15f; delta = Vector3.zero;
                }
            }
            _yaw += dYaw;

            Vector3 p = pos + delta;
            float lim = MapGenerator.Zone - 6f;
            p.x = Mathf.Clamp(p.x, -lim, lim); p.z = Mathf.Clamp(p.z, -lim, lim);

            // Четыре точки опоры
            float hx = rig.hullSize.x * 0.38f, hz = rig.hullSize.z * 0.4f;
            Quaternion yr = Quaternion.Euler(0, _yaw, 0);
            // Лучи опоры — раз в 3 кадра (у танка игрока — каждый кадр); между ними — высота рельефа + запомненная
            // «надставка» препятствия (мешки, брёвна). Экономит ~2/3 физических запросов главного потока.
            _gFull = PlayerAboard || ((Time.frameCount + index) % 3) == 0;
            float fl = GroundK(0, p + yr * new Vector3(-hx, 0, hz), p.y), fr = GroundK(1, p + yr * new Vector3(hx, 0, hz), p.y);
            float bl = GroundK(2, p + yr * new Vector3(-hx, 0, -hz), p.y), br = GroundK(3, p + yr * new Vector3(hx, 0, -hz), p.y);
            float front = (fl + fr) * 0.5f, back = (bl + br) * 0.5f, left = (fl + bl) * 0.5f, right = (fr + br) * 0.5f;
            float pitch = Mathf.Atan2(back - front, hz * 2f) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(right - left, hx * 2f) * Mathf.Rad2Deg;
            // Слишком крутой подъём — не лезем
            if (Mathf.Abs(pitch) > (IsLight ? 40f : 44f) && Mathf.Sign(-pitch) == Mathf.Sign(_speed)) { p = new Vector3(pos.x, p.y, pos.z); _speed *= 0.5f; }
            float gy = (fl + fr + bl + br) * 0.25f;
            _groundY = gy < _groundY ? Mathf.MoveTowards(_groundY, gy, 9.81f * dt * 1.5f) : Mathf.Lerp(_groundY, gy, dt * 14f);
            _pitchVis = Mathf.LerpAngle(_pitchVis, pitch, dt * 7f);
            _rollVis = Mathf.LerpAngle(_rollVis, roll, dt * 7f);

            // Раскачка корпуса от разгона/торможения и отдачи
            float accelSway = Mathf.Clamp((target - _speed) * 0.25f, -2f, 2f);
            _recoil = Mathf.MoveTowards(_recoil, 0, dt * 2.5f);
            float rec = Mathf.Sin(_recoil * Mathf.PI) * 2.2f;
            transform.SetPositionAndRotation(new Vector3(p.x, _groundY, p.z), Quaternion.Euler(_pitchVis + accelSway - rec, _yaw, _rollVis));
        }

        bool _gFull = true;
        readonly float[] _gLift = new float[4];

        float GroundK(int k, Vector3 at, float refY)
        {
            var t = Terrain.activeTerrain;
            float th = t ? t.SampleHeight(at) + t.transform.position.y : refY;
            if (_gFull || !t) { float g = Ground(at, refY); _gLift[k] = g - th; return g; }
            return th + _gLift[k];
        }

        float Ground(Vector3 at, float refY)
        {
            var o = new Vector3(at.x, refY + 4f, at.z);
            int n = Physics.RaycastNonAlloc(o, Vector3.down, _hits, 14f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (Mine(c) || c.attachedRigidbody || c.GetComponent<Fence>() || c.GetComponent<Brick>()) continue;
                if (_hits[i].point.y > best) best = _hits[i].point.y;
            }
            if (best > float.MinValue) return best;
            var t = Terrain.activeTerrain;
            return t ? t.SampleHeight(at) + t.transform.position.y : refY;
        }

        bool Blocked(Vector3 pos, Quaternion rot, Vector3 dir, float dist)
        {
            Vector3 half = new(rig.hullSize.x * 0.5f - 0.05f, rig.hullSize.y * 0.32f, rig.hullSize.z * 0.5f - 0.1f);
            Vector3 center = pos + Vector3.up * (rig.hullCenterY + 0.35f);
            int n = Physics.BoxCastNonAlloc(center, half, dir, _hits, rot, dist, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i]; var c = h.collider;
                if (Mine(c) || c is TerrainCollider || c.attachedRigidbody) continue;
                var fence = c.GetComponent<Fence>();
                if (fence) { fence.Break(pos, dir * Mathf.Abs(_speed)); continue; }
                // Низкие препятствия (мешки, брёвна, ящики, бортики окопов) переезжаем: Ground() поднимет корпус на них
                if (c.bounds.max.y - pos.y < (IsLight ? 0.75f : 1.35f)) continue;
                var brick = c.GetComponent<Brick>();
                if (brick) { if (Mathf.Abs(_speed) > 1.5f) { brick.Damage(1000f); continue; } blocked = true; continue; }
                if (h.distance <= 0f && Vector3.Dot(c.bounds.center - pos, dir) < 0) continue;   // уже внутри — даём выехать
                blocked = true;
            }
            return blocked;
        }

        void AimTurret(float dt)
        {
            Vector3 toAim = _aimPoint - rig.turret.position;
            if (toAim.sqrMagnitude < 1f) toAim = rig.hull.forward * 50f;
            Vector3 l = rig.hull.InverseTransformDirection(toAim);
            float targetYaw = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
            _lastTurretYaw = _turretYaw;
            _turretYaw = Mathf.MoveTowardsAngle(_turretYaw, targetYaw, traverseRate * dt);
            rig.turret.localRotation = Quaternion.Euler(0, _turretYaw, 0);

            Vector3 tl = rig.turret.InverseTransformDirection(_aimPoint - rig.gun.position);
            float targetPitch = -Mathf.Atan2(tl.y, new Vector2(tl.x, tl.z).magnitude) * Mathf.Rad2Deg;
            targetPitch = IsLight ? Mathf.Clamp(targetPitch, -35f, 25f) : Mathf.Clamp(targetPitch, -20f, 8f);
            _gunPitch = Mathf.MoveTowards(_gunPitch, targetPitch, elevateRate * dt);
            rig.gun.localRotation = Quaternion.Euler(_gunPitch, 0, 0);
            float back = Mathf.Sin(Mathf.Clamp01(_recoil) * Mathf.PI * 0.5f) * 0.35f;
            if (!IsLight) rig.gun.localPosition = new Vector3(rig.gun.localPosition.x, rig.gun.localPosition.y, (faction == Faction.Germany ? 1.15f : 1.2f) - back);

            // Куда смотрит ствол сейчас (нужно только прицелу игрока)
            var mz = rig.muzzle.position; var gf = rig.gun.forward;
            if (PlayerAboard) GunHitPoint = RayWorld(mz, gf, 900f, out var gh) ? gh : mz + gf * 900f;
        }

        bool RayWorld(Vector3 o, Vector3 d, float len, out Vector3 point)
        {
            int n = Physics.RaycastNonAlloc(o, d, _hits, len, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            float best = len; point = o + d * len; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (Mine(_hits[i].collider) || _hits[i].collider.attachedRigidbody) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; point = _hits[i].point; any = true; }
            }
            return any;
        }

        void Weapons(float dt)
        {
            _reload -= dt;
            if (fireCannon && !IsLight && _reload <= 0 && shells > 0) FireCannon();
            if (fireMg && Time.time >= _mgNext) FireMg();
        }

        void FireCannon()
        {
            _reload = reloadTime; shells--;
            _recoil = 1f;
            var mz = rig.muzzle.position;
            Vector3 dir = rig.gun.forward;
            if (crew == Crew.Player) dir = AssistDir(mz, dir);
            dir = Quaternion.Euler(Random.Range(-0.12f, 0.12f), Random.Range(-0.12f, 0.12f), 0) * dir;
            Projectile.Fire(Projectile.Type.Shell, mz + dir * 0.2f, dir, shellSpeed, 1f, DriverActorId, Team, 9f, 160f, 330f, 70f, transform);   // радиус разрыва 9 м (было 44 м — один снаряд выкашивал толпу на базе)
            if (FxPool.I)
            {
                FxPool.I.MuzzleFlashAt(mz, dir); FxPool.I.MuzzleFlashAt(mz + dir * 0.4f, dir);
                FxPool.I.Smoke(mz + dir * 0.8f);
                var gp = new Vector3(mz.x, Ground(mz, mz.y) + 0.1f, mz.z);
                if (mz.y - gp.y < 3.5f) { FxPool.I.Impact(gp, Vector3.up, _theme); FxPool.I.Impact(gp + dir * 2f, Vector3.up, _theme); }
            }
            BotAudio.ShotAt(_cannonClip, mz, 1.25f, 0.04f);
            CombatWorld.EmitNoise(mz, 450f, DriverActorId, Team, 1);
            if (crew == Crew.Player) Shake(0.45f);
            CameraEffects.Explosion(mz, 1.5f);
        }

        /// <summary>
        /// Небольшая помощь игроку в прицеливании: если вражеский танк в пределах 2,5° от ствола (до 500 м),
        /// снаряд доворачивается на него с упреждением и поправкой на падение. Навести всё равно нужно самому.
        /// </summary>
        Vector3 AssistDir(Vector3 mz, Vector3 dir)
        {
            Tank best = null; float bestAng = 2.5f;
            foreach (var t in All)
            {
                if (!t || t == this || !t.Alive || t.Team == Team) continue;
                Vector3 to = t.Center - mz;
                if (to.sqrMagnitude > 500f * 500f) continue;
                float a = Vector3.Angle(dir, to);
                if (a < bestAng) { bestAng = a; best = t; }
            }
            if (!best) return dir;
            Vector3 aim = best.Center;
            float tf = Vector3.Distance(mz, aim) / shellSpeed;
            aim += best.transform.forward * best.Speed * tf + Vector3.up * (0.5f * 9.81f * tf * tf);
            return Vector3.Slerp(dir, (aim - mz).normalized, 0.85f).normalized;
        }

        void FireMg()
        {
            _mgNext = Time.time + (IsLight ? 0.075f : 0.095f);
            var o = rig.coaxMuzzle.position;
            float sp = IsLight ? 1.1f : 0.7f;
            Vector3 dir = Quaternion.Euler(Random.Range(-sp, sp), Random.Range(-sp, sp), 0) * rig.gun.forward;
            if (IsLight) _recoil = Mathf.Min(1f, _recoil + 0.04f);
            float range = 400f;
            float worldDist = range; RaycastHit wh = default; bool world = false;
            int n = Physics.RaycastNonAlloc(o, dir, _hits, range, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!Mine(_hits[i].collider) && _hits[i].distance < worldDist) { worldDist = _hits[i].distance; wh = _hits[i]; world = true; }
            Vector3 end = o + dir * worldDist;
            if (CombatWorld.RaycastActors(o, dir, worldDist, Team, MgShooterId, out var ah))
            {
                float dmg = (ah.headshot ? 45f : 26f) * Mathf.Lerp(1f, 0.6f, ah.distance / range) * Sandbag.Factor(o, ah.point);
                CombatWorld.Damage.Enqueue(new DamageEvent { targetActorId = ah.actorId, sourceActorId = MgShooterId, amount = dmg, hitPoint = ah.point, direction = dir, headshot = ah.headshot });
                if (FxPool.I) FxPool.I.Blood(ah.point, dir);
                end = ah.point;
            }
            else if (world)
            {
                var t = wh.collider.GetComponentInParent<Tank>();
                var plane = t ? null : wh.collider.GetComponentInParent<Aircraft>();
                if (plane && plane.Alive && plane.Team != Team) plane.TakeDamage(9f, MgShooterId, wh.point);
                var fence = wh.collider.GetComponent<Fence>(); if (fence) fence.Damage(20f, wh.point, dir);
                var brick = wh.collider.GetComponent<Brick>(); if (brick) brick.Damage(12f);
                if (FxPool.I) { if (t) FxPool.I.BrickChips(wh.point, wh.normal); else FxPool.I.Impact(wh.point, wh.normal, _theme); }
                if (t && Random.value < 0.3f) BotAudio.PlayAt(SoundSynth.Ricochet(Random.Range(0, 3)), wh.point, 0.6f);
            }
            if (FxPool.I) { if ((Time.frameCount & 1) == 0) FxPool.I.Tracer(o, end, 750f); FxPool.I.MuzzleFlashAt(o, dir); }
            BotAudio.ShotAt(_mgClip, o, PlayerAboard ? 0.8f : 1f, 0.06f);
            if (Net.NetSession.IsHost) Net.NetDriver.RecordShot(-1, o, end, Team, false);
            CombatWorld.EmitNoise(o, 200f, DriverActorId, Team, 0);
        }

        void RunOver()
        {
            if (Mathf.Abs(_speed) < 1.6f || !CombatWorld.Initialized) return;
            if (!PlayerAboard && ((Time.frameCount + index) % 3) != 0) return;      // давка проверяется раз в 3 кадра
            float reach = rig.hullSize.z * 0.5f + 2f; reach *= reach;
            var actors = CombatWorld.Actors;
            float hx = rig.hullSize.x * 0.5f + 0.35f, hz = rig.hullSize.z * 0.5f + 0.4f;
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team == Team) continue;
                if (PlayerAboard && a.actorId == CombatWorld.PlayerActorId) continue;
                if (((Vector3)a.position - transform.position).sqrMagnitude > reach) continue;   // быстрый отсев по расстоянию
                Vector3 l = transform.InverseTransformPoint(a.position);
                if (Mathf.Abs(l.x) > hx || Mathf.Abs(l.z) > hz || l.y < -1.5f || l.y > 2.5f) continue;
                if (l.z * Mathf.Sign(_speed) < -hz * 0.3f) continue;     // давим передом (или кормой при заднем ходе)
                if (_runOver.TryGetValue(a.actorId, out var last) && Time.time - last < 1f) continue;
                _runOver[a.actorId] = Time.time;
                CombatWorld.Damage.Enqueue(new DamageEvent { targetActorId = a.actorId, sourceActorId = DriverActorId, amount = 400f, hitPoint = a.position, direction = transform.forward * Mathf.Sign(_speed), explosive = true });
                BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Flesh, 1), a.position, 0.9f);
            }
        }

        void Sounds(float dt)
        {
            if (!_audioOn) return;
            float sp = Mathf.Abs(_speed) / maxSpeed;
            _engine.pitch = Mathf.Lerp(_engine.pitch, 0.75f + sp * 0.55f + Mathf.Abs(throttle) * 0.18f, dt * 3f);
            _engine.volume = Mathf.Lerp(_engine.volume, 0.6f + Mathf.Abs(throttle) * 0.35f, dt * 3f);
            _tracks.volume = Mathf.Lerp(_tracks.volume, Mathf.Clamp01(sp * 1.6f) * 0.8f, dt * 5f);
            _tracks.pitch = 0.6f + sp * 0.8f;
            float trav = Mathf.Abs(Mathf.DeltaAngle(_lastTurretYaw, _turretYaw)) / Mathf.Max(dt, 1e-4f) / traverseRate;
            _turretSrc.volume = Mathf.Lerp(_turretSrc.volume, Mathf.Clamp01(trav) * 0.6f, dt * 10f);
        }

        void Shake(float t)
        {
            var pc = PlayerBridge.Controller; if (!pc) return;
            var fx = pc.worldCamera ? pc.worldCamera.GetComponent<CameraEffects>() : null;
            if (fx) fx.AddTrauma(t);
        }

        // ---------------- Урон ----------------

        public void TakeDamage(float amount, int source, Vector3 point, bool direct = false)
        {
            if (!Alive || amount <= 0) return;
            // Своих не трогаем (кроме собственного взрыва под гусеницами — тоже нет)
            if (source >= 0 && source < CombatWorld.Actors.Length && CombatWorld.Actors[source].team == Team) return;
            if (source <= -10) { int ti = -10 - source; foreach (var t in All) if (t.index == ti && t.Team == Team) return; }
            Health -= amount;
            LastHitTime = Time.time;
            HitFeedback.Report(source, Health <= 0);
            _lastAttacker = source; _lastAttackerPos = source >= 0 && source < CombatWorld.Actors.Length ? (Vector3)CombatWorld.Actors[source].position : point;
            if (crew == Crew.Player) Shake(direct ? 0.6f : 0.3f);
            if (FxPool.I) FxPool.I.BrickChips(point, (point - Center).normalized);
            if (Health <= 0) Kill(source);
        }

        void Kill(int source)
        {
            Health = 0; WreckTime = Time.time;
            var c = Center;
            if (PlayerAboard && PlayerBridge.Controller) VehicleManager.EjectPlayer(this, true, source);
            UnboardAll(source);                                  // боты в машине гибнут вместе с ней
            crew = Crew.None;
            Explosions.Detonate(c + Vector3.up * 0.5f, 7f, 140f, 0f, source, Team, Explosions.Kind.TankWreck, true, 300f, transform);
            if (FxPool.I) for (int i = 0; i < 3; i++) FxPool.I.Explosion(c + Random.insideUnitSphere * 2f + Vector3.up);
            // Сорванная башня
            rig.turret.localPosition += new Vector3(Random.Range(-0.3f, 0.3f), 0.35f, Random.Range(-0.3f, 0.3f));
            rig.turret.localRotation = Quaternion.Euler(Random.Range(-12f, 12f), _turretYaw + Random.Range(-50f, 50f), Random.Range(-15f, 15f));
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i]) mats[i] = ProceduralModels.Mat(Color.Lerp(mats[i].color, new Color(0.06f, 0.055f, 0.05f), 0.8f), 0.05f, 0.1f);
                r.sharedMaterials = mats;
            }
            MapProps.SmokeColumn(transform, c + Vector3.up * 1.5f);
            WreckLimiter.Add(gameObject);
            _engine.Stop(); _tracks.Stop(); _turretSrc.Stop();
            Destroyed?.Invoke(this, source);
        }

        void WreckUpdate(float dt)
        {
            if (Time.time - WreckTime > 60f) Destroy(gameObject);      // обломки исчезают через минуту
        }

        // ---------------- Игрок ----------------

        float _camYaw, _camPitch = 8f;
        int _enterFrame;
        readonly RaycastHit[] _camHits = new RaycastHit[12];

        public void OnPlayerEnter(PlayerController pc, JeepSeat seat = JeepSeat.Driver)
        {
            if (!IsLight) EjectBotDriver();
            PlayerSeat = IsLight ? seat : JeepSeat.Driver;
            if (PlayerSeat == JeepSeat.Driver) crew = Crew.Player;
            WeaponSlot = IsLight ? 1 : 0; Zoomed = false; _enterFrame = Time.frameCount;
            _camYaw = transform.eulerAngles.y + _turretYaw; _camPitch = 8f;
            _aimPoint = rig.muzzle.position + rig.gun.forward * 100f;
        }

        void PlayerInputUpdate()
        {
            var pc = PlayerBridge.Controller;
            if (!pc) { crew = Crew.None; return; }
            var inp = pc.Input;
            throttle = Mathf.Clamp(inp.move.y, -1, 1); steer = Mathf.Clamp(inp.move.x, -1, 1);
            if (inp.weaponSlot == 1) WeaponSlot = 0; else if (inp.weaponSlot == 2) WeaponSlot = 1;
            if (inp.scroll != 0) WeaponSlot = 1 - WeaponSlot;
            if (IsLight) WeaponSlot = 1;   // у джипа только пулемёт
            if (IsLight && inp.weaponSlot == 3 && !BuddyCalled) { BuddyCalled = true; BotAudio.PlayAt(SoundSynth.Hatch(), transform.position + Vector3.up, 0.3f); }
            Zoomed = inp.aimHeld;
            float sens = Zoomed ? 0.3f : 1f;
            _camYaw += inp.lookDelta.x * sens; _camPitch = Mathf.Clamp(_camPitch - inp.lookDelta.y * sens, -25f, 35f);
            fireCannon = WeaponSlot == 0 && inp.fireHeld;
            fireMg = WeaponSlot == 1 && inp.fireHeld;
            if (inp.interactPressed && Time.frameCount > _enterFrame + 1) VehicleManager.EjectPlayer(this, false, -1);
            else if (IsLight && inp.seatPressed && Time.frameCount > _enterFrame + 1) TrySwitchSeat();
        }

        /// <summary>Куда упадёт снаряд пушки (баллистика с гравитацией) — для мини-окна 3D у танка игрока.</summary>
        public Vector3 PredictedImpact { get; private set; }

        void PredictShell()
        {
            Vector3 p = rig.muzzle.position, v = rig.gun.forward * shellSpeed;
            const float step = 0.04f;
            for (int i = 0; i < 100; i++)
            {
                Vector3 nv = v + Physics.gravity * step;
                Vector3 d = (v + nv) * 0.5f * step;
                if (RayWorld(p, d.normalized, d.magnitude, out var hp)) { PredictedImpact = hp; return; }
                p += d; v = nv;
            }
            PredictedImpact = p;
        }

        void LateUpdate()
        {
            if (!Alive || !PlayerAboard) return;
            if (!IsLight && PlayerSeat == JeepSeat.Driver)
            {
                if ((Time.frameCount & 1) == 0) PredictShell();
                MiniView.Request(PredictedImpact, 75f, 34f, rig.turret.forward);
            }
            var pc = PlayerBridge.Controller; if (!pc) return;
            if (PlayerSeat == JeepSeat.Passenger)
            {
                // Пассажир смотрит и стреляет сам (PlayerController), машина только везёт
                pc.transform.position = SeatTransform(JeepSeat.Passenger).position - Vector3.up * 0.45f;
                return;
            }
            pc.transform.SetPositionAndRotation(SeatTransform(PlayerSeat == JeepSeat.None ? JeepSeat.Driver : PlayerSeat).position, Quaternion.Euler(0, _yaw, 0));

            var rot = Quaternion.Euler(_camPitch, _camYaw, 0);
            Vector3 camPos;
            if (Zoomed && IsLight)
            {
                // Из-за пулемёта: глаз над щитком, ствол и мушка внизу кадра; прицел — кольцевой (рисует HUD)
                camPos = rig.gun.position + rig.gun.up * 0.3f - rig.gun.forward * 0.75f;
                pc.worldCamera.fieldOfView = Mathf.Lerp(pc.worldCamera.fieldOfView, 55f, Time.deltaTime * 12f);
            }
            else if (Zoomed)
            {
                camPos = rig.gun.position + rig.gun.up * 0.42f + rig.gun.right * (faction == Faction.Germany ? 0.38f : -0.38f);
                pc.worldCamera.fieldOfView = Mathf.Lerp(pc.worldCamera.fieldOfView, 22f, Time.deltaTime * 12f);
            }
            else
            {
                Vector3 pivot = rig.turret.position + Vector3.up * (IsLight ? 1.2f : 2.1f);
                Vector3 want = pivot + rot * new Vector3(0, 0.6f, IsLight ? -6.5f : -9.5f);
                Vector3 d = want - pivot; float len = d.magnitude;
                int n = Physics.SphereCastNonAlloc(pivot, 0.35f, d / len, _camHits, len, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
                float best = len;
                for (int i = 0; i < n; i++) if (!Mine(_camHits[i].collider) && _camHits[i].distance > 0 && _camHits[i].distance < best) best = _camHits[i].distance;
                camPos = pivot + d / len * Mathf.Max(1f, best - 0.2f);
                pc.worldCamera.fieldOfView = Mathf.Lerp(pc.worldCamera.fieldOfView, pc.BaseFov, Time.deltaTime * 10f);
            }
            pc.cameraPivot.SetPositionAndRotation(camPos, rot);
            // Точка прицеливания — куда смотрит камера
            Vector3 fwd = rot * Vector3.forward;
            // Наводка ствола за камерой — если пулемётом управляет игрок (у джипа водитель стреляет, только пока нет стрелка)
            if (!IsLight || PlayerSeat == JeepSeat.Gunner || gunnerBot < 0)
                _aimPoint = RayWorld(camPos + fwd * (Zoomed ? 1f : 9f), fwd, 1200f, out var hp) ? hp : camPos + fwd * 1200f;
        }

        // ---------------- ИИ ----------------

        float _think, _goalTimer, _stuckCheck, _reverseUntil, _reverseSteer, _aimErrTimer;
        int _stuckTries;
        static readonly Collider[] _overlap = new Collider[16];

        /// <summary>
        /// Безнадёжно застрял (яма, окоп, между домами): ищем рядом ровное свободное место по кругу и переставляем
        /// туда технику — лучше, чем навсегда выбывший из боя танк. Игрок-водитель так не переставляется.
        /// </summary>
        void Unstick()
        {
            if (crew != Crew.Bot) return;                 // переставляем только машину, которую ведёт бот
            var t = Terrain.activeTerrain; if (!t) return;
            Vector3 c = transform.position;
            Vector3 half = new(rig.hullSize.x * 0.6f, rig.hullSize.y * 0.5f, rig.hullSize.z * 0.6f);
            for (int ring = 0; ring < 4; ring++)
                for (int k = 0; k < 12; k++)
                {
                    Vector3 tg = _goal - c; float a0 = tg.sqrMagnitude > 1f ? Mathf.Atan2(tg.z, tg.x) : 0f;
                    // сначала — в сторону цели, затем веером в обе стороны
                    float a = a0 + ((k & 1) == 0 ? 1 : -1) * ((k + 1) / 2) * Mathf.PI * 2f / 12f + ring * 0.26f, r = 7f + ring * 5f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                    float lim = MapGenerator.Zone - 8f;
                    if (Mathf.Abs(p.x) > lim || Mathf.Abs(p.z) > lim) continue;
                    var n = t.terrainData.GetInterpolatedNormal((p.x - t.transform.position.x) / t.terrainData.size.x, (p.z - t.transform.position.z) / t.terrainData.size.z);
                    if (n.y < 0.93f) continue;                           // склон круче ~21° — не годится
                    p.y = t.SampleHeight(p) + t.transform.position.y;
                    int nc = Physics.OverlapBoxNonAlloc(p + Vector3.up * (half.y + 0.5f), half, _overlap, Quaternion.Euler(0, _yaw, 0), CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
                    bool busy = false;
                    for (int q = 0; q < nc; q++) if (!Mine(_overlap[q]) && !(_overlap[q] is TerrainCollider)) { busy = true; break; }
                    if (busy) continue;
                    _groundY = p.y; _speed = 0; _reverseUntil = 0;
                    transform.SetPositionAndRotation(p, Quaternion.Euler(0, _yaw, 0));
                    Physics.SyncTransforms();
                    return;
                }
        }
        float _progT, _progBest = float.MaxValue;
        Vector3 _goal, _stuckPos, _aimErr;
        Tank _tgtTank; int _tgtActor = -1; Vector3 _tgtPos, _tgtVel, _tgtPrevPos;
        int _lastAttacker = int.MinValue; Vector3 _lastAttackerPos;
        float _holdDist = 30f;

        static float AimErrorDeg => MatchBridge.Difficulty switch
        {
            BotDifficulty.Easy => 1.8f, BotDifficulty.Medium => 1.0f, BotDifficulty.Hard => 0.55f, _ => 0.35f
        };

        void ThinkAI(float dt)
        {
            _think -= dt; _goalTimer -= dt; _aimErrTimer -= dt;
            if (_think <= 0) { _think = 0.45f; PickTarget(); }
            if (_goalTimer <= 0) { _goalTimer = 3f; PickGoal(); }

            Vector3 pos = transform.position;
            Vector3 to = _goal - pos; to.y = 0;
            float dist = to.magnitude;
            float desired = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float err = Mathf.DeltaAngle(_yaw, desired);
            bool hold = dist < _holdDist;

            // Держим лоб к вражескому танку
            if (_tgtTank && Vector3.Distance(_tgtPos, pos) < 300f)
            {
                Vector3 tt = _tgtPos - pos;
                float ty = Mathf.Atan2(tt.x, tt.z) * Mathf.Rad2Deg;
                // На захвате точек танк не останавливается ради дуэли — едет к точке, а стреляет башня на ходу
                bool capture = MatchBridge.Mode == GameModeType.TerritoryControl;
                if (hold || (!capture && Vector3.Distance(_tgtPos, pos) < 120f)) { err = Mathf.DeltaAngle(_yaw, ty); hold = true; }
            }

            float pk = IsLight ? 1.5f : 1f;
            // Лучи-«щупы» препятствий — 7 раз в секунду, а не каждый кадр
            _probeT -= dt;
            if (_probeT <= 0) { _probeT = 0.14f; _bF = Probe(0, 13f * pk); _bL = Probe(-32, 10f * pk); _bR = Probe(32, 10f * pk); }
            bool bF = _bF, bL = _bL, bR = _bR;
            if (bF && !hold) err = bL && !bR ? 70 : !bL && bR ? -70 : bL && bR ? 110 : (err >= 0 ? 70 : -70);
            else if (bL && !bR && !hold) err = Mathf.Max(err, 25);
            else if (bR && !bL && !hold) err = Mathf.Min(err, -25);

            steer = Mathf.Clamp(err / 22f, -1, 1);
            throttle = hold ? 0f : Mathf.Clamp01(1f - Mathf.Abs(err) / 100f) * (bF ? 0.3f : 1f);
            if (hold && Mathf.Abs(err) < 6f) steer = 0;
            if (_tgtTank && Reload01 > 0.9f && Vector3.Distance(_tgtPos, pos) < 260f) throttle *= 0.35f;   // короткая остановка для выстрела

            // Застрял — сдаём назад с поворотом
            _stuckCheck -= dt;
            if (_stuckCheck <= 0)
            {
                _stuckCheck = 2.5f;
                bool stuckNow = !hold && throttle > 0.1f && Vector3.Distance(_stuckPos, pos) < 1.2f;
                if (stuckNow && Time.time > _reverseUntil)
                {
                    _reverseUntil = Time.time + 2.4f; _reverseSteer = Random.value < 0.5f ? -1 : 1;
                    if (++_stuckTries >= 2) { _stuckTries = 0; Unstick(); }       // не выбрались за 2 попытки — переставляем
                }
                else if (!stuckNow) _stuckTries = 0;
                _stuckPos = pos;
                // Крутится на месте или ездит кругами у препятствия: 12 с без приближения к цели — переставляем
                float gd = dist;
                if (hold || gd < _progBest - 3f || gd > _progBest + 25f) { _progBest = gd; _progT = 0; }   // приблизился или сменилась цель
                else if ((_progT += 2.5f) >= 12f) { _progT = 0; _progBest = float.MaxValue; Unstick(); }
            }
            if (Time.time < _reverseUntil) { throttle = -0.8f; steer = _reverseSteer; }

            // Прицеливание и огонь
            fireCannon = fireMg = false;
            if (_tgtTank || _tgtActor >= 0)
            {
                Vector3 muzzle = rig.muzzle.position;
                float d = Vector3.Distance(muzzle, _tgtPos);
                float tFly = d / (IsLight ? 750f : shellSpeed);
                if (_aimErrTimer <= 0)
                {
                    _aimErrTimer = reloadTime;
                    _aimErr = Random.insideUnitSphere * d * Mathf.Tan(AimErrorDeg * Mathf.Deg2Rad);
                    // 20% выстрелов — сильный промах (спешка, плохой обзор из танка)
                    if (Random.value < 0.2f) { var r = Random.insideUnitCircle.normalized; _aimErr += new Vector3(r.x, Random.Range(-0.3f, 0.8f), r.y) * Mathf.Max(6f, d * Mathf.Tan(4.5f * Mathf.Deg2Rad)); }
                }
                bool cannonAtInf = !_tgtTank && d > 30f;
                Vector3 lead = _tgtPos + _tgtVel * tFly + Vector3.up * (IsLight ? 0f : 0.5f * 9.81f * tFly * tFly) + Vector3.up * (_tgtTank ? 1.2f : 1.0f);
                _aimPoint = lead + _aimErr;
                Vector3 want = (_aimPoint - muzzle).normalized;
                float ang = Vector3.Angle(rig.gun.forward, want);
                if (ang < 1.3f && (_tgtTank || cannonAtInf) && d < MaxTankRange + 20f) fireCannon = true;
                if (!_tgtTank && d < 170f && ang < 4f) fireMg = true;
                if (IsLight) { fireCannon = false; fireMg = d < 220f && ang < 5f; }
            }
            else _aimPoint = pos + Quaternion.Euler(0, _yaw, 0) * new Vector3(0, 2.2f, 60f);
        }

        bool Probe(float ang, float len)
        {
            Vector3 dir = Quaternion.Euler(0, _yaw + ang, 0) * Vector3.forward;
            Vector3 o = transform.position + Vector3.up * 1.3f + dir * (rig.hullSize.z * 0.5f - 0.5f);
            int n = Physics.RaycastNonAlloc(o, dir, _hits, len, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (Mine(c) || c is TerrainCollider || c.attachedRigidbody || c.GetComponent<Fence>() || c.GetComponent<Brick>()) continue;
                return true;
            }
            // Склон / обрыв
            var t = Terrain.activeTerrain;
            if (t)
            {
                Vector3 e = o + dir * len;
                float h0 = t.SampleHeight(transform.position), h1 = t.SampleHeight(e);
                if (Mathf.Abs(h1 - h0) / len > 0.6f) return true;
                if (Mathf.Abs(e.x) > MapGenerator.Zone - 10 || Mathf.Abs(e.z) > MapGenerator.Zone - 10) return true;
            }
            return false;
        }

        void PickGoal()
        {
            Vector3 pos = transform.position;
            var cps = WorldStores.CapturePoints;
            if (MatchBridge.Mode == GameModeType.TerritoryControl && cps.IsCreated && cps.Length > 0)
            {
                float best = float.MaxValue; int bi = -1;
                for (int i = 0; i < cps.Length; i++)
                {
                    var cp = cps[i];
                    float d = math.distance(cp.position, (float3)pos);
                    if (cp.owner == Team && cp.contested == 0) d += 600f;      // свои — в последнюю очередь
                    if (cp.contested != 0) d -= 150f;
                    if (d < best) { best = d; bi = i; }
                }
                Vector3 c = cps[bi].position;
                // Главная цель — сама точка: заезжаем в её радиус (танки тоже захватывают), чуть разводясь между собой
                Vector3 home = VehicleManager.I ? VehicleManager.I.BaseOf(Team) : Vector3.zero;
                Vector3 off = (home - c); off.y = 0; off = off.sqrMagnitude > 1 ? off.normalized : Vector3.forward;
                float side = ((index * 37) % 5 - 2) * 2.5f;
                _goal = c + off * 4f + Vector3.Cross(Vector3.up, off) * side;
                _holdDist = 5f;
            }
            else
            {
                Vector3 enemy = VehicleManager.I ? VehicleManager.I.BaseOf((byte)(1 - Team)) : Vector3.zero;
                _goal = Vector3.Lerp(Vector3.zero, enemy, 0.35f) + new Vector3(((index * 53) % 7 - 3) * 25f, 0, 0);
                _holdDist = 25f;
            }
            if (MatchBridge.Mode != GameModeType.TerritoryControl && _tgtActor >= 0 && !_tgtTank && Vector3.Distance(_tgtPos, pos) < 120f) { _goal = _tgtPos; _holdDist = 45f; }
        }

        // Дальность огня ботов-танкистов: по технике до 210 м, по пехоте до 140 м (раньше — через всю карту)
        const float MaxTankRange = 210f, MaxInfRange = 140f;

        void PickTarget()
        {
            Vector3 eye = rig.turret.position + Vector3.up * 1.0f;
            Tank bestTank = null; float bestD = MaxTankRange;
            foreach (var t in All)
            {
                if (IsLight && !t.IsLight) continue;     // из пулемёта по танку стрелять бессмысленно
                if (!t || !t.Alive || t.Team == Team) continue;
                float d = Vector3.Distance(t.Center, eye);
                if (d < bestD && Visible(eye, t.Center + Vector3.up * 0.6f, t)) { bestD = d; bestTank = t; }
            }
            // Пехота рядом (до 70 м) важнее далёкого танка: развернуть башню и выкосить пулемётом
            if (!bestTank || bestD > 110f)
            {
                var acts = CombatWorld.Actors;
                int close = -1; float cd = 70f; int chk = 0;
                int st = acts.Length > 0 ? Random.Range(0, acts.Length) : 0;
                for (int k = 0; k < acts.Length && chk < 8; k++)
                {
                    var a = acts[(st + k) % acts.Length];
                    if (!a.alive || a.team == Team || Aircraft.IsAirCrew(a.actorId) || BaseZone.Near(a.position, a.team)) continue;
                    if (a.actorId == CombatWorld.PlayerActorId && PlayerBridge.Controller && PlayerBridge.Controller.Vehicle) continue;
                    float d = Vector3.Distance(a.position, eye);
                    if (d >= cd) continue;
                    chk++;
                    if (Visible(eye, (Vector3)a.position + Vector3.up * 1.2f, null)) { cd = d; close = a.actorId; }
                }
                if (close >= 0)
                {
                    _tgtTank = null; _tgtActor = close; _tgtPos = acts[close].position; _tgtVel = acts[close].velocity;
                    return;
                }
            }
            if (bestTank)
            {
                _tgtTank = bestTank; _tgtActor = -1;
                var np = bestTank.Center;
                _tgtVel = Vector3.Lerp(_tgtVel, (np - _tgtPrevPos) / 0.45f, 0.5f); if (_tgtVel.magnitude > 20f) _tgtVel = Vector3.zero;
                _tgtPrevPos = np; _tgtPos = np - Vector3.up * 0.6f;
                return;
            }
            _tgtTank = null;
            var actors = CombatWorld.Actors;
            int best = -1; float bd = MaxInfRange;
            int checks = 0;
            // Сначала последний обидчик
            if (_lastAttacker >= 0 && _lastAttacker < actors.Length && actors[_lastAttacker].alive && Time.time - LastHitTime < 8f &&
                Vector3.Distance(actors[_lastAttacker].position, eye) < MaxTankRange && !Aircraft.IsAirCrew(_lastAttacker))
            {
                var a = actors[_lastAttacker];
                if (Visible(eye, (Vector3)a.position + Vector3.up * 1.2f, null)) best = _lastAttacker;
            }
            if (best < 0 && actors.Length > 0)
            {
                // Игрок — такой же солдат, как все: обход начинается со случайного места, берём ближайшего видимого
                int start = Random.Range(0, actors.Length);
                for (int k = 0; k < actors.Length && checks < 10; k++)
                {
                    int i = (start + k) % actors.Length;
                    var a = actors[i];
                    if (!a.alive || a.team == Team) continue;
                    if (a.actorId == CombatWorld.PlayerActorId && PlayerBridge.Controller && PlayerBridge.Controller.Vehicle) continue;
                    if (Aircraft.IsAirCrew(a.actorId)) continue;            // самолёты пушкой не достать
                    if (BaseZone.Near(a.position, a.team)) continue;          // по базе (точке высадки) противника не стреляем
                    float d = Vector3.Distance(a.position, eye);
                    if (d >= bd) continue;
                    checks++;
                    if (Visible(eye, (Vector3)a.position + Vector3.up * 1.2f, null)) { bd = d; best = i; }
                }
            }
            if (best >= 0)
            {
                var a = actors[best];
                _tgtActor = a.actorId; _tgtPos = a.position; _tgtVel = a.velocity;
            }
            else _tgtActor = -1;
        }

        bool Visible(Vector3 from, Vector3 to, Tank target)
        {
            // Обзор из танка закрывают и стены, и листва: кусты, изгороди, кроны деревьев (триггеры-заслоны)
            Vector3 d = to - from; float len = d.magnitude;
            int n = Physics.RaycastNonAlloc(from, d / len, _hits, len, CombatWorld.WorldMask | (1 << MapGenerator.VisionOccluderLayer), QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (Mine(c) || c.attachedRigidbody) continue;
                if (c.isTrigger && c.gameObject.layer != MapGenerator.VisionOccluderLayer) continue;
                if (target && c.transform.IsChildOf(target.transform)) continue;
                if (_hits[i].distance < len - 0.5f) return false;
            }
            return true;
        }
    }
}
