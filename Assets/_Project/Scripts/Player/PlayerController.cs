using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Движение (бег, прыжок, присед), обзор и отдача камеры. Публикует снимок игрока в CombatWorld
    /// (actorId = 0), чтобы боты видели/слышали его наравне с другими.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Ссылки")]
        public Transform cameraPivot;
        public Camera worldCamera;
        public Camera viewModelCamera;      // отдельная камера для рук/оружия (без клиппинга в стены)

        [Header("Движение")]
        public float walkSpeed = 4.6f, sprintSpeed = 6.8f, crouchSpeed = 2.4f, adsSpeedMul = 0.6f;
        public float jumpHeight = 1.05f, gravity = -22f, acceleration = 14f, airControl = 0.35f;
        public float standHeight = 1.8f, crouchHeight = 1.2f, eyeOffset = 0.12f;

        [Header("Шаги (шум для ботов)")]
        public float footstepNoiseRadius = 14f, sprintNoiseMul = 1.8f, crouchNoiseMul = 0.3f;

        public Faction Faction { get; set; }
        public byte Team => (byte)Faction;
        public bool IsCrouched { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsGrounded => _cc.isGrounded;
        public float Speed01 => new Vector2(_vel.x, _vel.z).magnitude / sprintSpeed;
        public PlayerInputState Input { get; private set; }
        public bool InputEnabled { get; set; } = true;

        /// <summary>
        /// Вертикальный FOV камеры. В настройках поле зрения задаётся по горизонтали (как в большинстве шутеров):
        /// 90° по горизонтали ≈ 59° по вертикали на 16:9. Нагрудная камера — не уже 100° по горизонтали.
        /// </summary>
        public float BaseFov
        {
            get
            {
                float h = SaveSystem.Settings.fieldOfView;
                if (SaveSystem.Settings.bodycam) h = Mathf.Max(h, 100f);
                float aspect = worldCamera ? Mathf.Max(0.5f, worldCamera.aspect) : 16f / 9f;
                return Camera.HorizontalToVerticalFieldOfView(Mathf.Clamp(h, 60f, 120f), aspect);
            }
        }

        // ---- Танк и вид от третьего лица ----
        public Tank Vehicle { get; private set; }
        /// <summary>Игрок пилотирует самолёт (управление и камеру ведёт Aircraft).</summary>
        public Aircraft Flying { get; private set; }
        /// <summary>Игрок у артиллерийского орудия (наводку и камеру ведёт ArtilleryGun).</summary>
        public ArtilleryGun Gunning { get; private set; }
        /// <summary>Выпрыгнул из самолёта — спускается на парашюте до земли.</summary>
        public bool Parachute { get; set; }
        /// <summary>Несёт артиллерийский снаряд: оружие убрано, ходит медленнее.</summary>
        public bool CarryingShell
        {
            get => _carry;
            set
            {
                if (_carry == value) return;
                _carry = value;
                if (value && !_carryModel && cameraPivot)
                {
                    // Снаряд перед камерой (рисуется камерой мира, как руки)
                    _carryModel = LogiModels.Shell(cameraPivot, new Vector3(0.05f, -0.42f, 0.62f), new Vector3(10, 80, 0), 1f);
                    foreach (var r in _carryModel.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (_carryModel) _carryModel.gameObject.SetActive(value);
            }
        }
        bool _carry; Transform _carryModel;
        /// <summary>Пассажир джипа: сам смотрит и стреляет из своего оружия, машину не ведёт.</summary>
        public bool Passenger { get; private set; }
        /// <summary>Место, на котором едет игрок-пассажир (у сетевого клиента — место пулемётчика джипа).</summary>
        public Transform RideSeat { get; private set; }

        /// <summary>Ехать стоя на месте пулемётчика со своим оружием (второй игрок по сети).</summary>
        public void RideAsPassenger(Tank t, Transform seat)
        {
            EnterVehicle(t);
            RideSeat = seat;
            SetPassenger(true);
        }

        void RideUpdate()
        {
            if (Vehicle && Passenger && RideSeat) transform.position = RideSeat.position;
            else if (Vehicle && Passenger && !Vehicle.gameObject.activeInHierarchy) ExitVehicle(transform.position + Vector3.right * 2f, _yaw);
        }
        float _lastVehYaw;
        public bool ThirdPerson { get; set; }
        /// <summary>Камера реально вынесена за спину (в прицеле — всегда от первого лица).</summary>
        public bool ThirdPersonActive => _tp > 0.5f && !Vehicle && !Flying && !Gunning;
        /// <summary>Насколько камера позади глаз — оружие стреляет от игрока, а не от камеры.</summary>
        public float ThirdPersonDistance => Vehicle ? 0f : _tpDist;
        public PlayerBody Body { get; private set; }
        float _tp, _tpDist;
        readonly RaycastHit[] _camHits = new RaycastHit[8];
        public float SpeedMultiplier { get; set; } = 1f;   // ADS / тяжёлое оружие
        public bool Aiming { get; set; }

        CharacterController _cc;
        Vector3 _vel;
        // Инерция обзора и физика движения
        Vector2 _lookVel;
        bool _vmShown = true;
        WeaponController _wcRef;
        Vector3 _groundNormal = Vector3.up;
        float _landSlow, _fallSpeed, _staminaDelay;
        bool _exhausted;
        WeaponController _wc;
        /// <summary>Выносливость 0..1: бег тратит, отдых восстанавливает; при нуле — одышка, бег недоступен до 25%.</summary>
        public float Stamina { get; private set; } = 1f;
        public bool Exhausted => _exhausted;

        /// <summary>«Вес» оружия в руках 0..1 — скорость поворота, разгон, раскачка.</summary>
        public float WeaponWeight
        {
            get
            {
                if (!_wc) _wc = GetComponent<WeaponController>();
                var w = _wc ? _wc.Current : null;
                if (!w) return 0.2f;
                return w.category switch
                {
                    WeaponCategory.Knife => 0.1f, WeaponCategory.Pistol or WeaponCategory.Pistol2 => 0.22f,
                    WeaponCategory.SubmachineGun => 0.45f, WeaponCategory.AssaultRifle => 0.6f, WeaponCategory.BattleRifle => 0.62f,
                    WeaponCategory.SniperRifle => 0.7f, WeaponCategory.Launcher => 0.9f, WeaponCategory.MachineGun => 1f, _ => 0.5f
                };
            }
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > 0.5f) _groundNormal = hit.normal;
        }
        float _yaw, _pitch;
        Vector2 _recoilOffset, _recoilTarget;
        float _recoilRecovery = 8f;
        float _stepTimer;
        PlayerHealth _hp;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _hp = GetComponent<PlayerHealth>();
            Body = GetComponent<PlayerBody>();
            _yaw = transform.eulerAngles.y;
            ThirdPerson = false;   // по умолчанию от первого лица; F5 — вид из-за спины (как в GTA)
        }

        void Update()
        {
            Input = InputEnabled ? GameInput.Read(Aiming) : default;
            var inp = Input;
            if (inp.cameraViewPressed && !Vehicle && !Flying && !Gunning) ThirdPerson = !ThirdPerson;
            // Руки и оружие видны, только пока жив и не в танке
            // В оптике модель оружия скрывается: камера стоит у окуляра, и корпус прицела закрывал бы весь экран
            if (!_wcRef) _wcRef = GetComponent<WeaponController>();
            bool scoped = _wcRef && _wcRef.HasOptic && _wcRef.Ads01 > 0.85f;
            bool showVm = (!Vehicle || Passenger) && !Flying && !Gunning && !CarryingShell && _tp < 0.5f && (!_hp || _hp.Alive) && !scoped && !Binoculars.Active;   // от 3-го лица руки с оружием «из камеры» не рисуем — оружие в руках у тела
            if (showVm != _vmShown) { _vmShown = showVm; SetViewModelVisible(showVm); }
            if (_carryModel) _carryModel.gameObject.SetActive(_carry && _tp < 0.5f && !Vehicle && !Flying);
            if (Flying || Gunning) { _vel = Vector3.zero; IsCrouched = false; IsSprinting = false; return; }   // самолётом и камерой управляет Aircraft
            if (Vehicle && !Passenger) { _vel = Vector3.zero; return; }   // управление и камеру ведёт танк
            if (Vehicle)
            {
                // Пассажир поворачивается вместе с машиной
                float vy = Vehicle.transform.eulerAngles.y;
                _yaw += Mathf.DeltaAngle(_lastVehYaw, vy); _lastVehYaw = vy;
            }


            // --- Обзор: с инерцией тело разворачивается не мгновенно, тяжёлое оружие — медленнее ---
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float weight = WeaponWeight;
            Vector2 look = inp.lookDelta;
            if (SaveSystem.Settings.heavyLook)
            {
                float response = Mathf.Lerp(40f, 18f, weight) * (Aiming ? 0.8f : 1f) * (IsSprinting ? 0.7f : 1f) * (_exhausted ? 0.8f : 1f);
                float maxRate = Mathf.Lerp(1000f, 380f, weight) * (Aiming ? 0.6f : 1f);
                Vector2 want = Vector2.ClampMagnitude(look / dt, maxRate);
                _lookVel = Vector2.Lerp(_lookVel, want, 1f - Mathf.Exp(-response * dt));
                look = _lookVel * dt;
            }
            else _lookVel = look / dt;
            _yaw += look.x;
            _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);
            if (_hp && _hp.Downed)
            {
                // Тяжело ранен: прицел «плывёт» сам по себе
                float tt = Time.time;
                _yaw += (Mathf.PerlinNoise(tt * 0.7f, 3.1f) - 0.5f) * 40f * dt;
                _pitch = Mathf.Clamp(_pitch + (Mathf.PerlinNoise(5.3f, tt * 0.6f) - 0.5f) * 30f * dt, -85f, 85f);
            }

            _recoilTarget = Vector2.Lerp(_recoilTarget, Vector2.zero, _recoilRecovery * Time.deltaTime);
            _recoilOffset = Vector2.Lerp(_recoilOffset, _recoilTarget, 25f * Time.deltaTime);

            transform.rotation = Quaternion.Euler(0, _yaw + _recoilOffset.x, 0);
            cameraPivot.localRotation = Quaternion.Euler(_pitch - _recoilOffset.y, 0, 0);
            if (Vehicle)
            {
                // Сидя в машине: глаз ниже, не бегаем и не приседаем; положение задаёт машина (Tank.LateUpdate)
                IsCrouched = false; IsSprinting = false; _vel = Vector3.zero;
                cameraPivot.localPosition = new Vector3(0, 1.3f, 0);
                return;
            }

            // --- Присед (тяжело раненый — лежит) ---
            bool downed = _hp && _hp.Downed;
            bool wantCrouch = inp.crouchHeld || downed;
            if (!wantCrouch && IsCrouched && Physics.SphereCast(transform.position + Vector3.up * crouchHeight, 0.3f, Vector3.up, out _, standHeight - crouchHeight, CombatWorld.WorldMask))
                wantCrouch = true; // некуда вставать
            IsCrouched = wantCrouch;
            float h = Mathf.Lerp(_cc.height, IsCrouched ? crouchHeight : standHeight, 12f * Time.deltaTime);
            _cc.height = h; _cc.center = new Vector3(0, h * 0.5f, 0);
            cameraPivot.localPosition = new Vector3(0, h - eyeOffset, 0);
            UpdateThirdPerson();

            // --- Выносливость ---
            bool wantSprint = inp.sprintHeld && inp.move.y > 0.5f && !IsCrouched && !Aiming;
            if (_exhausted && Stamina > 0.25f) _exhausted = false;
            IsSprinting = wantSprint && !_exhausted;
            if (IsSprinting) { Stamina = Mathf.Max(0, Stamina - dt / Mathf.Lerp(8f, 5.5f, weight)); _staminaDelay = 1.1f; if (Stamina <= 0) _exhausted = true; }
            else if ((_staminaDelay -= dt) <= 0) Stamina = Mathf.Min(1, Stamina + dt / (IsCrouched ? 3.5f : 5f));

            // --- Движение: разгон и торможение с инерцией, склоны, приземление, вес оружия ---
            float target = IsCrouched ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            target *= SpeedMultiplier * (Aiming ? adsSpeedMul : 1f);
            if (inp.move.y < -0.1f) target *= 0.78f;                                   // назад медленнее
            else if (Mathf.Abs(inp.move.x) > Mathf.Abs(inp.move.y)) target *= 0.9f;    // боком — чуть медленнее
            target *= Mathf.Lerp(1.04f, 0.9f, weight);
            if (_exhausted) target *= 0.85f;
            if (CarryingShell) target *= 0.55f;          // тяжёлый снаряд в руках
            if (_hp && _hp.Downed) target = crouchSpeed * 0.35f;     // тяжело ранен — ползёт

            Vector3 wish = transform.right * inp.move.x + transform.forward * inp.move.y;
            if (wish.sqrMagnitude > 1) wish.Normalize();
            // Подъём в гору замедляет, спуск немного разгоняет
            if (_cc.isGrounded && wish.sqrMagnitude > 0.01f)
            {
                float slope = Vector3.Angle(_groundNormal, Vector3.up);
                float along = Vector3.Dot(new Vector3(_groundNormal.x, 0, _groundNormal.z).normalized, wish.normalized);
                if (slope > 4f) target *= along < 0 ? Mathf.Lerp(1f, 0.62f, Mathf.InverseLerp(4f, 40f, slope) * -along) : 1f + 0.08f * along * Mathf.InverseLerp(4f, 30f, slope);
            }
            _landSlow = Mathf.MoveTowards(_landSlow, 0, dt);
            target *= 1f - _landSlow * 0.8f;

            Vector3 horiz = new(_vel.x, 0, _vel.z);
            bool speedingUp = wish.sqrMagnitude > 0.01f && Vector3.Dot(horiz, wish) >= -0.1f && horiz.magnitude < target;
            float accel = speedingUp ? acceleration * 0.65f : acceleration * 0.95f;     // разгон мягче, чем торможение
            if (Vector3.Dot(horiz, wish) < 0) accel *= 1.25f;                           // смена направления
            accel *= Mathf.Lerp(1.1f, 0.75f, weight);
            if (!_cc.isGrounded) accel *= airControl;
            horiz = Vector3.MoveTowards(horiz, wish * target, accel * Mathf.Max(target, walkSpeed) * dt);

            if (_cc.isGrounded)
            {
                if (_fallSpeed < -7f) _landSlow = Mathf.Clamp01((-_fallSpeed - 7f) / 8f) * 0.6f + 0.15f;   // жёсткое приземление
                _fallSpeed = 0;
                _vel.y = -2f;
                if (inp.jumpPressed && !IsCrouched && Stamina > 0.1f)
                {
                    _vel.y = Mathf.Sqrt(jumpHeight * -2f * gravity) * Mathf.Lerp(1f, 0.85f, weight);
                    Stamina = Mathf.Max(0, Stamina - 0.12f); _staminaDelay = 1f;
                }
            }
            else _fallSpeed = Mathf.Min(_fallSpeed, _vel.y);
            _vel.y += gravity * Time.deltaTime;
            if (Parachute)
            {
                // Парашют: медленный спуск, без жёсткого приземления
                if (_cc.isGrounded) Parachute = false;
                else { _vel.y = Mathf.Max(_vel.y, -4.5f); _fallSpeed = Mathf.Max(_fallSpeed, -4.5f); }
            }
            _vel = new Vector3(horiz.x, _vel.y, horiz.z);
            _cc.Move(_vel * Time.deltaTime);
            TerrainFloor();

            // --- Шаги ---
            float speed = horiz.magnitude;
            if (_cc.isGrounded && speed > 0.5f)
            {
                _stepTimer -= Time.deltaTime * speed;
                if (_stepTimer <= 0)
                {
                    _stepTimer = 2.2f;
                    float r = footstepNoiseRadius * (IsSprinting ? sprintNoiseMul : IsCrouched ? crouchNoiseMul : 1f);
                    CombatWorld.EmitNoise(transform.position, r, CombatWorld.PlayerActorId, Team, 2);
                    SendMessage("OnFootstep", SendMessageOptions.DontRequireReceiver);
                }
            }

            // FOV плавно — ADS задаёт WeaponController через TargetFov.
            worldCamera.fieldOfView = Mathf.Lerp(worldCamera.fieldOfView, TargetFov > 0 ? TargetFov : BaseFov, 14f * Time.deltaTime);
            if (viewModelCamera)
            {
                // Bodycam: оружие и руки рисуются шире (как с нагрудного объектива), в прицеле — ближе
                float ads = Aiming ? 1f : 0f;
                float vmFov = SaveSystem.Settings.bodycam ? Mathf.Lerp(74f, 58f, ads) : 60f;
                viewModelCamera.fieldOfView = Mathf.Lerp(viewModelCamera.fieldOfView, vmFov, 10f * Time.deltaTime);
            }
        }

        public float TargetFov { get; set; }

        void UpdateThirdPerson()
        {
            bool alive = !_hp || _hp.Alive;
            _tp = Mathf.MoveTowards(_tp, ThirdPerson && !Aiming && alive ? 1f : 0f, Time.deltaTime * 6f);
            if (Body) Body.SetView(_tp > 0.5f, false);
            if (viewModelCamera) viewModelCamera.enabled = _tp < 0.5f;
            if (_tp <= 0.001f) { _tpDist = 0; return; }
            Vector3 eye = cameraPivot.position;
            Quaternion rot = cameraPivot.rotation;
            Vector3 off = rot * new Vector3(0.55f, 0.18f, -2.4f) * Mathf.SmoothStep(0, 1, _tp);
            float len = off.magnitude;
            if (len < 0.01f) { _tpDist = 0; return; }
            int n = Physics.SphereCastNonAlloc(eye, 0.22f, off / len, _camHits, len, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            float best = len;
            for (int i = 0; i < n; i++)
                if (_camHits[i].distance > 0 && _camHits[i].distance < best && !_camHits[i].collider.transform.IsChildOf(transform)) best = _camHits[i].distance;
            cameraPivot.position = eye + off / len * Mathf.Max(0.05f, best - 0.1f);
            // Расстояние «за спиной» вдоль взгляда — с него начинается луч выстрела
            _tpDist = Mathf.Max(0, Vector3.Dot(eye - cameraPivot.position, rot * Vector3.forward));
        }

        /// <summary>Посадка в танк: тело, оружие и шаги выключаются, урон получает танк.</summary>
        public void EnterVehicle(Tank t)
        {
            var wc = GetComponent<WeaponController>();
            if (wc && wc.CurrentSlot == 4) wc.Equip(1);
            Vehicle = t;
            _cc.enabled = false; _vel = Vector3.zero; Aiming = false; TargetFov = 0;
            SetCombat(false);
        }

        /// <summary>Пересадка в джипе: пассажир — с оружием в руках, водитель/стрелок — управляют машиной.</summary>
        public void SetPassenger(bool on)
        {
            if (!Vehicle || Passenger == on) return;
            Passenger = on;
            cameraPivot.localPosition = new Vector3(0, 1.3f, 0);
            cameraPivot.localRotation = Quaternion.identity;
            worldCamera.fieldOfView = BaseFov;
            _yaw = Vehicle.transform.eulerAngles.y; _pitch = 0; _lastVehYaw = _yaw;
            SetCombat(on);
        }

        /// <summary>Посадка в самолёт: как в танке — тело и оружие выключены, урон получает самолёт.</summary>
        public void EnterAircraft(Aircraft a)
        {
            var wc = GetComponent<WeaponController>();
            if (wc && wc.CurrentSlot == 4) wc.Equip(1);
            ThirdPerson = false; _tp = 0; _tpDist = 0;
            Flying = a; Parachute = false;
            _cc.enabled = false; _vel = Vector3.zero; Aiming = false; TargetFov = 0;
            SetCombat(false);
        }

        /// <summary>Встал к орудию: оружие убрано, но расчёт стоит открыто — пули и осколки его достают.</summary>
        public void EnterArtillery(ArtilleryGun g)
        {
            var wc = GetComponent<WeaponController>();
            if (wc && wc.CurrentSlot == 4) wc.Equip(1);
            ThirdPerson = false; _tp = 0; _tpDist = 0;
            Gunning = g;
            _cc.enabled = false; _vel = Vector3.zero; Aiming = false; TargetFov = 0;
            SetCombat(false);
            if (_hp) _hp.Invulnerable = false;
        }

        public void ExitArtillery(Vector3 pos, float yaw)
        {
            Gunning = null;
            cameraPivot.localRotation = Quaternion.identity;
            worldCamera.fieldOfView = BaseFov;
            Teleport(pos, yaw);
            SetCombat(true);
        }

        public void ExitAircraft(Vector3 pos, float yaw, bool parachute)
        {
            Flying = null;
            cameraPivot.localRotation = Quaternion.identity;
            worldCamera.fieldOfView = BaseFov;
            Teleport(pos, yaw);
            Parachute = parachute;
            SetCombat(true);
        }

        public void ExitVehicle(Vector3 pos, float yaw)
        {
            Passenger = false; RideSeat = null;
            Vehicle = null;
            cameraPivot.localRotation = Quaternion.identity;
            worldCamera.fieldOfView = BaseFov;
            Teleport(pos, yaw);
            SetCombat(true);
        }

        /// <summary>Показ рук и оружия (слой 31) в камере мира — прячем в танке и после смерти.</summary>
        public void SetViewModelVisible(bool on)
        {
            if (!worldCamera) return;
            const int vm = 1 << 31;
            worldCamera.cullingMask = on ? worldCamera.cullingMask | vm : worldCamera.cullingMask & ~vm;
        }

        void SetCombat(bool on)
        {
            foreach (var b in new Behaviour[] { GetComponent<WeaponController>(), GetComponent<KnifeController>(), GetComponent<TrowelTool>(), GetComponent<FieldTools>(),
                                                 GetComponent<GrenadeThrower>(), GetComponent<ViewModelAnimator>(), GetComponent<FootstepAudio>() })
                if (b) b.enabled = on;
            if (viewModelCamera) viewModelCamera.enabled = on;
            SetViewModelVisible(on);
            if (Body) Body.SetView(false, !on);
            if (_hp) _hp.Invulnerable = !on;
        }

        void LateUpdate() { RideUpdate(); PublishSnapshot(!_hp || _hp.Alive); }

        public void PublishSnapshot(bool alive)
        {
            if (!CombatWorld.Initialized) return;
            var snap = new ActorSnapshot
            {
                position = transform.position, velocity = _vel, actorId = CombatWorld.PlayerActorId,
                team = Team, alive = alive && enabled, crouched = IsCrouched
            };
            // Игрок всегда в слоте 0 — боты добавляются после (BotSnapshotSystem начинает с индекса 1).
            if (CombatWorld.Actors.Length == 0) CombatWorld.Actors.Add(snap);
            else CombatWorld.Actors[0] = snap;
        }

        /// <summary>Отдача: x — горизонталь (±), y — вертикаль (вверх).</summary>
        public void AddRecoil(Vector2 kick, float recovery)
        {
            _recoilTarget += kick;
            _recoilRecovery = recovery;
            // Часть вертикали «съедается» в прицел навсегда, как в CS — игрок должен компенсировать мышью.
            _pitch -= kick.y * 0.35f;
        }

        public Ray AimRay() => new(worldCamera.transform.position, worldCamera.transform.forward);

        public void Teleport(Vector3 pos, float yaw)
        {
            _cc.enabled = false;
            transform.position = pos; _yaw = yaw; _pitch = 0; _vel = Vector3.zero;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Physics.SyncTransforms();      // autoSyncTransforms выключен — без этого контроллер «помнит» старую позицию
            _cc.enabled = !Vehicle;
        }

        /// <summary>
        /// Страховка от провала сквозь ландшафт (на телефонах при низком FPS / большом шаге кадра капсула могла
        /// проскочить коллайдер рельефа, после чего FallGuard телепортировал её обратно — «падает и прыгает вверх»).
        /// Ниже рельефа стоять нельзя нигде на карте (окопы и воронки вырезаны в самом рельефе), поэтому просто
        /// ставим игрока на поверхность и гасим скорость падения.
        /// </summary>
        void TerrainFloor()
        {
            var t = Terrain.activeTerrain;
            if (!t) return;
            Vector3 p = transform.position;
            float h = t.SampleHeight(p) + t.transform.position.y;
            if (p.y >= h - 0.15f) return;
            _cc.enabled = false;
            transform.position = new Vector3(p.x, h + 0.02f, p.z);
            Physics.SyncTransforms();
            _cc.enabled = true;
            if (_vel.y < 0) _vel.y = -2f;
            _fallSpeed = 0;
        }
    }
}
