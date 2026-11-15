using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Оружие игрока: стрельба (hitscan + трассер), разброс, отдача, ADS, перезарядка (магазин/обойма/по патрону),
    /// глушитель (тихий звук + почти нулевой радиус шума для ботов), слоты, нож, шпатель.
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] Transform viewModelRoot;      // родитель модели оружия, двигается для ADS/качания
        [SerializeField] WeaponVisualAssembler assembler;
        [SerializeField] Animator viewAnimator;        // руки: Fire, Reload, ReloadEmpty, ClipInsert, Inspect, KnifeSpin, Melee, Draw
        [SerializeField] AudioSource audioSource;
        [SerializeField] KnifeController knife;
        [SerializeField] TrowelTool trowel;
        [SerializeField] int viewModelLayer = 9;
        [SerializeField] Vector3 hipPosition = new(0.18f, -0.17f, 0.32f);

        public WeaponDefinition Current { get; private set; }
        public WeaponStats Stats { get; private set; }
        public int Ammo { get; private set; }
        public int Reserve { get; private set; }
        public bool Reloading { get; private set; }
        public float Ads01 { get; private set; }
        public bool HasOptic { get; private set; }
        public int CurrentSlot { get; private set; }

        public event Action<WeaponController> AmmoChanged, WeaponChanged;
        public event Action<ActorHit> HitConfirmed;

        class SlotState { public WeaponDefinition def; public List<AttachmentDefinition> atts; public int ammo, reserve; }
        readonly SlotState[] _slots = new SlotState[5]; // 1 основное, 2 пистолет, 3 нож, 4 шпатель, 5 противотанковое
        public bool HasSlot(int slot) => slot >= 1 && slot <= 5 && _slots[slot - 1] != null;
        float _nextFire, _spread;
        Coroutine _reload;
        bool _triggerReleased = true;
        MapTheme _theme;

        public void SetupLoadout(ClassLoadout loadout, MapTheme theme)
        {
            _theme = theme;
            // Внутренние слоты: 1 основное, 2 второстепенное (пистолет / лёгкий ПП), 3 нож или ключ, 4 шпатель, 5 противотанковое.
            // Клавиши: 1, 2, 3 (нож/ключ — или шпатель, если инженер выбрал его), 4 (противотанковое у поддержки).
            _slots[0] = MakeSlot(loadout.primary);
            _slots[1] = MakeSlot(loadout.secondary);
            _slots[2] = MakeSlot(loadout.melee);
            Tool = loadout.hasTrowel ? ToolKind.Trowel : loadout.tool;
            _slots[3] = Tool != ToolKind.None ? new SlotState() : null;
            if (!_hpRef) _hpRef = GetComponent<PlayerHealth>();
            if (!_tools) { _tools = GetComponent<FieldTools>(); if (!_tools) _tools = gameObject.AddComponent<FieldTools>(); }
            _tools.ResetForSpawn(); _tools.SetActive(ToolKind.None);
            _slots[4] = MakeSlot(loadout.gadget);
            _hasKnife = loadout.melee && loadout.melee.id.StartsWith("bayonet");
            CurrentSlot = 0;
            // Первое имеющееся оружие (основного может не быть, если оно ещё не куплено)
            for (int i = 1; i <= 5; i++) if (_slots[i - 1] != null) { Equip(i); break; }
        }

        static SlotState MakeSlot(WeaponDefinition w)
        {
            if (!w) return null;
            var atts = AttachmentRules.Resolve(SaveSystem.Progress.GetLoadout(w.id));
            var st = WeaponStats.Compute(w, atts);
            return new SlotState { def = w, atts = atts, ammo = st.magazineSize, reserve = st.reserveAmmo };
        }

        // ---- Смена оружия: убрать текущее (время по весу), затем достать новое ----
        int _pendingSlot;
        /// <summary>0..1 — насколько текущее оружие уже убрано (ViewModelAnimator опускает его из кадра).</summary>
        public float Holster01 { get; private set; }
        /// <summary>Время доставания текущего оружия, с.</summary>
        public float DrawTime { get; private set; } = 0.5f;
        public bool Switching => _pendingSlot != 0;

        public static float DrawTimeOf(WeaponDefinition w) => !w ? 0.5f : w.category switch
        {
            WeaponCategory.Knife => 0.3f, WeaponCategory.Pistol or WeaponCategory.Pistol2 => 0.42f,
            WeaponCategory.SubmachineGun => 0.62f, WeaponCategory.AssaultRifle => 0.78f, WeaponCategory.BattleRifle => 0.85f,
            WeaponCategory.SniperRifle => 0.9f, WeaponCategory.MachineGun => 1.25f, WeaponCategory.Launcher => 1.1f, _ => 0.6f
        };

        public void Equip(int slot)
        {
            if (slot < 1 || slot > 5) return;
            var s = _slots[slot - 1];
            if (s == null || slot == CurrentSlot) { _pendingSlot = 0; return; }
            // Первое оружие при появлении — сразу; иначе сначала убираем текущее
            if (CurrentSlot == 0 || !isActiveAndEnabled) { EquipNow(slot); return; }
            if (_pendingSlot == 0) { CancelReload(); PlayClip(SoundSynth.Holster(), 0.55f); }
            _pendingSlot = slot;
        }

        void UpdateSwitch()
        {
            if (_pendingSlot == 0) { Holster01 = Mathf.MoveTowards(Holster01, 0, Time.deltaTime * 8f); return; }
            float holsterTime = (CurrentSlot == 4 ? 0.5f : DrawTimeOf(Current)) * 0.55f;
            Holster01 = Mathf.MoveTowards(Holster01, 1f, Time.deltaTime / Mathf.Max(0.1f, holsterTime));
            Ads01 = Mathf.MoveTowards(Ads01, 0, Time.deltaTime * 6f);
            if (Holster01 >= 1f)
            {
                int slot = _pendingSlot; _pendingSlot = 0;
                EquipNow(slot);
                Holster01 = 0f;
            }
        }

        void EquipNow(int slot)
        {
            var s = _slots[slot - 1];
            if (s == null) return;
            StoreCurrent();
            CancelReload();
            CurrentSlot = slot;

            bool isTrowel = slot == 4;
            if (trowel) trowel.SetActive(isTrowel && Tool == ToolKind.Trowel);
            if (_tools) _tools.SetActive(isTrowel && Tool != ToolKind.Trowel ? Tool : ToolKind.None);
            if (isTrowel) { Current = null; DrawTime = 0.5f; assembler.Clear(); WeaponChanged?.Invoke(this); return; }

            Current = s.def;
            Stats = WeaponStats.Compute(Current, s.atts);
            HasOptic = s.atts.Exists(a => a.slot == AttachmentSlot.Optic);
            Ammo = s.ammo; Reserve = s.reserve;
            var built = assembler.Build(Current, s.atts, true, viewModelLayer);
            if (!built) Debug.LogWarning($"[Frontline] Модель оружия {Current.id} не создана (нет viewModelPrefab)");
            if (viewAnimator) viewAnimator.SetTrigger("Draw");
            player.SpeedMultiplier = Current.moveSpeedMultiplier;
            DrawTime = DrawTimeOf(Current);
            _nextFire = Time.time + DrawTime;   // пока достаём — не стреляем
            PlayClip(SoundSynth.Draw(Current.category), 0.6f);
            WeaponChanged?.Invoke(this); AmmoChanged?.Invoke(this);
        }

        // ---------------- Бросить / подобрать оружие ----------------

        /// <summary>Слот оружия по категории: 1 основное, 2 пистолет, 5 противотанковое; ножи не бросаются.</summary>
        public static int SlotFor(WeaponDefinition w) => !w ? 0 : w.category switch
        {
            WeaponCategory.Knife => 0,
            WeaponCategory.Pistol or WeaponCategory.Pistol2 => 2,
            WeaponCategory.Launcher => 5,                 // клавиша 4
            _ => 1
        };

        /// <summary>Клавиша → внутренний слот: 3 — нож/ключ или шпатель, 4 — противотанковое (или шпатель).</summary>
        public int MapKey(int key) => key switch
        {
            3 => _slots[2] != null ? 3 : 4,
            4 => _slots[4] != null ? 5 : 4,
            _ => key
        };

        public WeaponDefinition SlotWeapon(int slot) => slot >= 1 && slot <= 5 ? _slots[slot - 1]?.def : null;

        /// <summary>[G] — бросить оружие из рук на землю (с его патронами и обвесами).</summary>
        public void DropCurrent()
        {
            if (Switching || !Current || CurrentSlot == 4) return;
            int slot = SlotFor(Current);
            if (slot == 0 || slot != CurrentSlot) return;
            StoreCurrent(); CancelReload();
            var s = _slots[slot - 1];
            _slots[slot - 1] = null;
            var cam = player.worldCamera.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            WeaponPickups.Spawn(s.def, s.atts, s.ammo, s.reserve, player.transform.position + fwd * 1.3f + Vector3.up * 0.9f, fwd * 3f + Vector3.up * 1.5f, 1.2f);
            PlayClip(SoundSynth.Holster(), 0.6f);
            Current = null; Ammo = Reserve = 0; CurrentSlot = 0;
            assembler.Clear();
            for (int i = 1; i <= 5; i++) if (i != 4 && _slots[i - 1] != null) { EquipNow(i); break; }
            if (!Current) { WeaponChanged?.Invoke(this); AmmoChanged?.Invoke(this); }
        }

        /// <summary>Подбор с земли: в пустой слот — оружие целиком; то же оружие — только патроны. Возвращает true, если что-то взято.</summary>
        public bool TryPickup(WeaponDefinition def, List<AttachmentDefinition> atts, int ammo, int reserve)
        {
            int slot = SlotFor(def);
            if (slot == 0) return false;
            StoreCurrent();
            var s = _slots[slot - 1];
            if (s != null)
            {
                if (s.def != def) return false;
                var st = WeaponStats.Compute(def, s.atts);
                int add = Mathf.Min(ammo + reserve, st.reserveAmmo - s.reserve);
                if (add <= 0) return false;
                s.reserve += add;
                if (CurrentSlot == slot) { Reserve = s.reserve; AmmoChanged?.Invoke(this); }
                PlayClip(SoundSynth.Holster(), 0.4f);
                return true;
            }
            _slots[slot - 1] = new SlotState { def = def, atts = atts ?? new List<AttachmentDefinition>(), ammo = ammo, reserve = reserve };
            Equip(slot);
            return true;
        }

        void StoreCurrent()
        {
            if (CurrentSlot <= 0 || CurrentSlot == 4) return;
            var s = _slots[CurrentSlot - 1];
            if (s != null) { s.ammo = Ammo; s.reserve = Reserve; }
        }

        void Update()
        {
            var inp = player.Input;

            if (inp.weaponSlot != 0) Equip(MapKey(inp.weaponSlot));
            if (inp.scroll != 0) CycleSlot(inp.scroll > 0 ? -1 : 1);
            if (inp.buildToggle && _slots[3] != null) Equip(CurrentSlot == 4 ? 1 : 4);
            if (inp.dropPressed && !Binoculars.Active) DropCurrent();
            UpdateBullets();
            if (Binoculars.Active)
            {
                // Смотрим в бинокль: оружие опущено, огня нет; FOV задаёт бинокль
                Ads01 = Mathf.MoveTowards(Ads01, 0, Time.deltaTime * 6f); player.Aiming = false;
                UpdateViewModelPose();
                return;
            }
            UpdateSwitch();
            if (Switching) { player.Aiming = false; UpdateViewModelPose(); return; }

            if (CurrentSlot == 4) { Ads01 = 0; player.Aiming = false; player.TargetFov = 0; return; } // строительство ведёт TrowelTool

            // Вращение ножа на F — доступно в любой момент, кроме перезарядки/стрельбы.
            if (inp.knifeSpinPressed && knife && _hasKnife && !Reloading) knife.Spin();
            if (inp.meleePressed && knife && _hasKnife) { CancelReload(); knife.Stab(player); }
            else if (inp.meleePressed && !_hasKnife && _slots[2] != null && CurrentSlot != 3) Equip(3);   // V у инженера — достать ключ
            if (!Current) RepairInfo = null;

            if (!Current) return;
            bool isMelee = Current.category == WeaponCategory.Knife;

            // ADS
            bool wantAds = inp.aimHeld && !Reloading && !player.IsSprinting && !isMelee && (!knife || !knife.Busy);
            Ads01 = Mathf.MoveTowards(Ads01, wantAds ? 1 : 0, Time.deltaTime / Mathf.Max(0.05f, Stats.adsTime));
            player.Aiming = Ads01 > 0.5f;
            // adsFov задан для базового вертикального FOV 80° — переводим в кратность приближения
            player.TargetFov = Mathf.Lerp(player.BaseFov, player.BaseFov * Mathf.Clamp(Stats.adsFov / 80f, 0.1f, 1f), Ads01);
            UpdateViewModelPose();

            // Разброс
            float moveSpread = player.Speed01 * Stats.moveSpreadPenalty + (player.IsGrounded ? 0 : 3f);
            float baseSpread = Mathf.Lerp(Stats.hipSpread, Stats.adsSpread, Ads01) + moveSpread * (1 - Ads01 * 0.7f);
            _spread = Mathf.MoveTowards(_spread, baseSpread, Stats.spreadRecovery * Time.deltaTime);

            if (inp.reloadPressed) StartReload();

            if (!inp.fireHeld) _triggerReleased = true;
            if (isMelee && Current.id.StartsWith("wrench")) { WrenchUpdate(inp); return; }
            RepairInfo = null;
            if (isMelee) { if (inp.firePressed && knife) knife.Stab(player); return; }

            bool wantsFire = Current.fireMode == FireMode.Auto ? inp.fireHeld : (inp.firePressed || (inp.fireHeld && _triggerReleased));
            if (player.IsSprinting || player.CarryingShell) wantsFire = false;          // на бегу не стреляем — оружие опущено
            if (wantsFire && Time.time >= _nextFire && (!knife || !knife.Busy))
            {
                if (Reloading)
                {
                    // Перезарядку по одному патрону (обойма/винтовка) можно прервать выстрелом.
                    if (Current.reloadType != ReloadType.Magazine && Ammo > 0) CancelReload(); else return;
                }
                if (Ammo <= 0) { if (_triggerReleased) PlayClip(Current.dryFireSound, 0.7f); _triggerReleased = false; StartReload(); return; }
                Fire();
                _triggerReleased = false;
            }
        }

        void CycleSlot(int dir)
        {
            int s = CurrentSlot;
            for (int i = 0; i < 5; i++)
            {
                s = (s - 1 + dir + 5) % 5 + 1;
                if (_slots[s - 1] != null) { Equip(s); return; }
            }
        }

        int _wildLeft;
        PlayerHealth _hpRef;
        FieldTools _tools;
        /// <summary>Инструмент 4-го слота (аптечка / мешки с песком).</summary>
        public ToolKind Tool { get; private set; }
        /// <summary>Текущий разброс (полный угол конуса, градусы) — по нему HUD рисует прицел.</summary>
        public float SpreadDeg => _spread;

        void Fire()
        {
            _nextFire = Time.time + Stats.fireInterval;
            Ammo--;
            AmmoChanged?.Invoke(this);

            var cam = player.worldCamera.transform;
            // От третьего лица луч начинается у игрока, а не за его спиной
            Vector3 origin = cam.position + cam.forward * player.ThirdPersonDistance;
            if (Current.projectileSpeed > 0f) FireProjectile(cam, origin);
            else
                for (int p = 0; p < Mathf.Max(1, Current.pelletsPerShot); p++)
                {
                    // Срыв: в 5% случаев следующие 10 пуль уходят в сторону (дёрнул спуск, сбилось дыхание)
                    if (p == 0 && _wildLeft <= 0 && UnityEngine.Random.value < 0.05f) _wildLeft = 10;
                    float extra = (_wildLeft > 0 ? 4.5f : 0f) + (_hpRef && _hpRef.Downed ? 3.5f : 0f);   // тяжело ранен — косит
                    Vector3 dir = SpreadDirection(cam.forward, cam, _spread + extra);
                    FireRay(origin, dir);
                }

            _spread += Stats.spreadPerShot;
            if (_wildLeft > 0) _wildLeft--;

            // Отдача
            float mul = Mathf.Lerp(1f, Stats.adsRecoilMul, Ads01) * (player.IsCrouched ? 0.8f : 1f);
            player.AddRecoil(new Vector2(UnityEngine.Random.Range(-Stats.recoilH, Stats.recoilH), Stats.recoilV) * mul, Stats.recoilRecovery);

            // Визуал и звук
            if (viewAnimator) viewAnimator.SetTrigger("Fire");
            if (!Stats.hideFlash && FxPool.I)
            {
                if (player.ThirdPersonActive && player.Body) FxPool.I.MuzzleFlashAt(player.Body.GunMuzzle, cam.forward);
                else FxPool.I.MuzzleFlash(assembler.Sockets ? assembler.Sockets.muzzleFlashPoint : null);
            }
            PlayClip(Stats.suppressed && Current.fireSoundSuppressed ? Current.fireSoundSuppressed : Current.fireSound,
                     Stats.suppressed ? 0.7f : 2f);      // громкость оружия ×2 (PlayOneShot допускает >1)

            // Шум для ботов. С глушителем шумового события нет вовсе — боты выстрел не слышат
            // (но могут увидеть стрелка или заметить попадание по себе).
            if (!Stats.suppressed)
                CombatWorld.EmitNoise(cam.position, Stats.loudnessRadius, CombatWorld.PlayerActorId, player.Team, 0);

            if (Ammo == 0 && Reserve > 0) StartReload();
        }

        /// <summary>Фаустпатрон: ракета из дула в точку под прицелом, реактивная струя назад.</summary>
        void FireProjectile(Transform cam, Vector3 origin)
        {
            Vector3 aim = Physics.Raycast(origin, cam.forward, out var ah, 600f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore)
                ? ah.point : origin + cam.forward * 600f;
            var mp = assembler.Sockets ? assembler.Sockets.muzzleFlashPoint : null;
            Vector3 from = player.ThirdPersonActive || !mp ? origin + cam.forward * 0.8f : mp.position + cam.forward * 0.3f;
            Vector3 dir = SpreadDirection((aim - from).normalized, cam, _spread * 0.5f);
            Projectile.Fire(Projectile.Type.Rocket, from, dir, Current.projectileSpeed, Current.projectileGravity, CombatWorld.PlayerActorId, player.Team,
                            Mathf.Max(1f, Current.explosionRadius), Current.explosionDamage, Current.armorDamage, Mathf.Min(Current.armorDamage * 0.2f, 400f), null);
            if (FxPool.I) { FxPool.I.Smoke(cam.position - cam.forward * 1.6f); FxPool.I.MuzzleFlashAt(cam.position - cam.forward * 1.1f, -cam.forward); }
        }

        // ---------------- Настоящие пули игрока ----------------
        // Каждый выстрел — пуля с начальной скоростью оружия, гравитацией и сопротивлением воздуха; попадание
        // считается по отрезку полёта за кадр. Пулю видно (светящийся штрих), видно, куда она летит и где падает.

        struct Bullet { public Vector3 pos, vel, origin, vis; public float traveled, range, age; public WeaponDefinition def; public WeaponStats stats; }
        readonly List<Bullet> _bullets = new();

        void FireRay(Vector3 origin, Vector3 dir)
        {
            var mp = assembler.Sockets ? assembler.Sockets.muzzleFlashPoint : null;
            Vector3 muzzle = player.ThirdPersonActive && player.Body ? player.Body.GunMuzzle : mp ? mp.position : origin;
            float v = Mathf.Max(250f, Stats.muzzleVelocity);
            _bullets.Add(new Bullet { pos = origin, vel = dir * v, origin = origin, vis = muzzle - origin, range = Stats.maxRange * 1.6f, def = Current, stats = Stats });
            // Сеть: другие игроки видят и слышат выстрел (урон уходит событиями урона)
            Vector3 to = Physics.Raycast(origin, dir, out var nh, Stats.maxRange, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore) ? nh.point : origin + dir * Stats.maxRange;
            if (Net.NetSession.IsHost) Net.NetDriver.RecordShot(CombatWorld.PlayerActorId, origin, to, player.Team, Stats.suppressed);
            else if (Net.NetSession.IsClient) Net.NetDriver.SendShot(origin, to, player.Team, Stats.suppressed);
        }

        void UpdateBullets()
        {
            float dt = Time.deltaTime;
            for (int i = _bullets.Count - 1; i >= 0; i--)
            {
                var b = _bullets[i];
                Vector3 prev = b.pos;
                b.vel += Physics.gravity * dt;
                b.vel *= Mathf.Exp(-0.12f * dt);                         // сопротивление воздуха
                Vector3 step = b.vel * dt;
                float len = step.magnitude;
                b.age += dt;
                bool done = HitSegment(ref b, prev, step / Mathf.Max(len, 1e-5f), len);
                b.pos = prev + step; b.traveled += len;
                // Видимая пуля: из дула (первые метры смещение затухает) к текущей точке
                float k = Mathf.Clamp01(1f - b.traveled / 25f);
                if (FxPool.I) FxPool.I.Bullet(prev + b.vis * Mathf.Clamp01(1f - (b.traveled - len) / 25f), (done ? prev : b.pos) + b.vis * k, 0.022f);
                if (done || b.traveled > b.range || b.age > 4f) { _bullets.RemoveAt(i); continue; }
                _bullets[i] = b;
            }
        }

        /// <summary>Проверка отрезка полёта пули: бойцы, техника, стены, земля. true — пуля остановилась.</summary>
        bool HitSegment(ref Bullet b, Vector3 from, Vector3 dir, float segLen)
        {
            var w = b.def; var st = b.stats;
            if (!w) return true;
            bool worldHit = Physics.Raycast(from, dir, out var rh, segLen, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            float worldDist = worldHit ? rh.distance : segLen;
            if (CombatWorld.RaycastActors(from, dir, worldDist, player.Team, CombatWorld.PlayerActorId, out var ah))
            {
                float total = b.traveled + ah.distance;
                // Пуля прошла сквозь мешки с песком (где угодно на пути от ствола) — урон ×¼
                float dmg = st.DamageAt(w, total) * (ah.headshot ? w.headshotMultiplier : 1f) * Sandbag.Factor(b.origin, ah.point);
                CombatWorld.Damage.Enqueue(new DamageEvent
                {
                    targetActorId = ah.actorId, sourceActorId = CombatWorld.PlayerActorId, amount = dmg,
                    hitPoint = ah.point, direction = dir, headshot = ah.headshot
                });
                if (FxPool.I) FxPool.I.Blood(ah.point, dir);
                BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Flesh, UnityEngine.Random.Range(0, 2)), ah.point, 0.7f);
                ah.distance = total;
                HitConfirmed?.Invoke(ah);
                return true;
            }
            if (!worldHit) return false;
            float dist = b.traveled + rh.distance;
            {
                var plane = rh.collider.GetComponentInParent<Aircraft>();
                if (plane)
                {
                    if (plane.Alive && plane.Team != player.Team)
                    {
                        plane.TakeDamage(st.DamageAt(w, dist) * 0.35f + w.armorDamage * 0.5f, CombatWorld.PlayerActorId, rh.point);
                        HitConfirmed?.Invoke(new ActorHit { actorId = -1, distance = dist, point = rh.point });
                    }
                    BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Metal, UnityEngine.Random.Range(0, 3)), rh.point, 0.6f);
                    if (FxPool.I) FxPool.I.BrickChips(rh.point, rh.normal);
                    return true;
                }
                var tank = rh.collider.GetComponentInParent<Tank>();
                if (tank)
                {
                    if (w.armorDamage > 0 && tank.Alive && tank.Team != player.Team)
                    {
                        tank.TakeDamage(w.armorDamage, CombatWorld.PlayerActorId, rh.point, true);
                        BotAudio.PlayAt(SoundSynth.ArmorHit(), rh.point, 0.9f);
                        HitConfirmed?.Invoke(new ActorHit { actorId = -1, distance = dist, point = rh.point });
                    }
                    else if (tank.IsLight && tank.Alive && tank.Team != player.Team)
                    {
                        tank.TakeDamage(st.DamageAt(w, dist) * 0.5f, CombatWorld.PlayerActorId, rh.point);
                        BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Metal, UnityEngine.Random.Range(0, 3)), rh.point, 0.7f);
                        HitConfirmed?.Invoke(new ActorHit { actorId = -1, distance = dist, point = rh.point });
                    }
                    else BotAudio.PlayAt(SoundSynth.Ricochet(UnityEngine.Random.Range(0, 3)), rh.point, Mathf.Lerp(0.9f, 0.3f, dist / 80f));
                    if (FxPool.I) FxPool.I.BrickChips(rh.point, rh.normal);
                    return true;
                }
                var fence = rh.collider.GetComponent<Fence>();
                if (fence) fence.Damage(st.DamageAt(w, dist), rh.point, dir);
                var brick = rh.collider.GetComponent<Brick>();
                if (brick) { brick.Damage(st.DamageAt(w, dist) * 0.5f); if (FxPool.I) FxPool.I.BrickChips(rh.point, rh.normal); }
                else if (FxPool.I) FxPool.I.Impact(rh.point, rh.normal, _theme);
                if (dist < 35f) BotAudio.PlayAt(SoundSynth.Impact(SurfaceUtil.Impact(rh.collider), UnityEngine.Random.Range(0, 3)), rh.point, Mathf.Lerp(0.55f, 0.12f, dist / 35f));
                // Пролетающая рядом с ботами пуля их пугает (как свист над головой)
                CombatWorld.EmitNoise(rh.point, 12f, CombatWorld.PlayerActorId, player.Team, 0);
            }
            return true;
        }

        void FireRayOld(Vector3 origin, Vector3 dir)
        {
            float range = Stats.maxRange;
            bool worldHit = Physics.Raycast(origin, dir, out var rh, range, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            float worldDist = worldHit ? rh.distance : range;

            if (CombatWorld.RaycastActors(origin, dir, worldDist, player.Team, CombatWorld.PlayerActorId, out var ah))
            {
                float dmg = Stats.DamageAt(Current, ah.distance) * (ah.headshot ? Current.headshotMultiplier : 1f);
                CombatWorld.Damage.Enqueue(new DamageEvent
                {
                    targetActorId = ah.actorId, sourceActorId = CombatWorld.PlayerActorId, amount = dmg,
                    hitPoint = ah.point, direction = dir, headshot = ah.headshot
                });
                if (FxPool.I) FxPool.I.Blood(ah.point, dir);
                BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Flesh, UnityEngine.Random.Range(0, 2)), ah.point, 0.7f);
                HitConfirmed?.Invoke(ah);
                Tracer(origin, ah.point);
                return;
            }

            if (worldHit)
            {
                var tank = rh.collider.GetComponentInParent<Tank>();
                if (tank)
                {
                    if (Current.armorDamage > 0 && tank.Alive && tank.Team != player.Team)
                    {
                        tank.TakeDamage(Current.armorDamage, CombatWorld.PlayerActorId, rh.point, true);
                        BotAudio.PlayAt(SoundSynth.ArmorHit(), rh.point, 0.9f);
                        HitConfirmed?.Invoke(new ActorHit { actorId = -1, distance = rh.distance, point = rh.point });
                    }
                    else if (tank.IsLight && tank.Alive && tank.Team != player.Team)
                    {
                        // Джип пробивается обычными пулями
                        tank.TakeDamage(Stats.DamageAt(Current, rh.distance) * 0.5f, CombatWorld.PlayerActorId, rh.point);
                        BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Metal, UnityEngine.Random.Range(0, 3)), rh.point, 0.7f);
                        HitConfirmed?.Invoke(new ActorHit { actorId = -1, distance = rh.distance, point = rh.point });
                    }
                    else BotAudio.PlayAt(SoundSynth.Ricochet(UnityEngine.Random.Range(0, 3)), rh.point, Mathf.Lerp(0.9f, 0.3f, rh.distance / 80f));
                    if (FxPool.I) FxPool.I.BrickChips(rh.point, rh.normal);
                    Tracer(origin, rh.point);
                    return;
                }
                var fence = rh.collider.GetComponent<Fence>();
                if (fence) fence.Damage(Stats.DamageAt(Current, rh.distance), rh.point, dir);
                var brick = rh.collider.GetComponent<Brick>();
                if (brick) { brick.Damage(Stats.DamageAt(Current, rh.distance) * 0.5f); if (FxPool.I) FxPool.I.BrickChips(rh.point, rh.normal); }
                else if (FxPool.I) FxPool.I.Impact(rh.point, rh.normal, _theme);
                if (rh.distance < 35f) BotAudio.PlayAt(SoundSynth.Impact(SurfaceUtil.Impact(rh.collider), UnityEngine.Random.Range(0, 3)), rh.point, Mathf.Lerp(0.55f, 0.12f, rh.distance / 35f));   // тихий шлепок рядом; дальние попадания не слышны
            }
            Tracer(origin, worldHit ? rh.point : origin + dir * range);
        }

        void Tracer(Vector3 from, Vector3 to)
        {
            // Трассер стартует из дула (визуально), а не из камеры.
            var mp = assembler.Sockets ? assembler.Sockets.muzzleFlashPoint : null;
            if (FxPool.I && Ammo % 3 == 0) FxPool.I.Tracer(mp ? mp.position : from, to, Stats.muzzleVelocity);
            // Сеть: другие игроки слышат и видят наш выстрел (урон уходит событиями урона)
            if (Net.NetSession.IsHost) Net.NetDriver.RecordShot(CombatWorld.PlayerActorId, from, to, player.Team, Stats.suppressed);
            else if (Net.NetSession.IsClient) Net.NetDriver.SendShot(from, to, player.Team, Stats.suppressed);
        }

        static Vector3 SpreadDirection(Vector3 fwd, Transform basis, float spreadDeg)
        {
            // Равномерно по диску (sqrt), а не по кругу — меньше «кучности в центре».
            float r = Mathf.Tan(spreadDeg * 0.5f * Mathf.Deg2Rad) * Mathf.Sqrt(UnityEngine.Random.value);
            float a = UnityEngine.Random.value * Mathf.PI * 2f;
            return (fwd + basis.right * (Mathf.Cos(a) * r) + basis.up * (Mathf.Sin(a) * r)).normalized;
        }

        // ---------- Перезарядка ----------

        void StartReload()
        {
            if (Reloading || Ammo >= Stats.magazineSize || Reserve <= 0) return;
            _reload = StartCoroutine(Current.reloadType switch
            {
                ReloadType.Magazine => ReloadMagazine(),
                ReloadType.StripperClip => ReloadClip(),
                _ => ReloadSingle()
            });
        }

        void CancelReload()
        {
            if (_reload != null) StopCoroutine(_reload);
            _reload = null; Reloading = false;
        }

        IEnumerator ReloadMagazine()
        {
            Reloading = true;
            bool empty = Ammo == 0;
            if (viewAnimator) viewAnimator.SetTrigger(empty ? "ReloadEmpty" : "Reload");
            PlayClip(Current.reloadSound, 1f);   // обычно null: механика озвучивается по фазам в ViewModelAnimator
            yield return new WaitForSeconds(empty ? Stats.reloadTimeEmpty : Stats.reloadTime);
            // Сброс неполного магазина как в хардкорных шутерах — не делаем: патроны возвращаются в запас.
            int need = Stats.magazineSize - Ammo;
            int take = Mathf.Min(need, Reserve);
            Ammo += take; Reserve -= take;
            Reloading = false; _reload = null;
            AmmoChanged?.Invoke(this);
        }

        /// <summary>Kar98k / Мосина / СВТ: обоймами по 5, остаток — по одному патрону.</summary>
        IEnumerator ReloadClip()
        {
            Reloading = true;
            if (viewAnimator) viewAnimator.SetTrigger("BoltOpen");
            yield return new WaitForSeconds(0.45f);
            while (Ammo < Stats.magazineSize && Reserve > 0)
            {
                int space = Stats.magazineSize - Ammo;
                bool fullClip = space >= Current.clipSize && Reserve >= Current.clipSize;
                if (viewAnimator) viewAnimator.SetTrigger(fullClip ? "ClipInsert" : "RoundInsert");
                PlayClip(Current.reloadSound, 1f);
                yield return new WaitForSeconds(fullClip ? Stats.reloadTime * 0.6f : 0.5f);
                int n = fullClip ? Current.clipSize : 1;
                n = Mathf.Min(n, Reserve);
                Ammo += n; Reserve -= n;
                AmmoChanged?.Invoke(this);
            }
            if (viewAnimator) viewAnimator.SetTrigger("BoltClose");
            yield return new WaitForSeconds(0.35f);
            Reloading = false; _reload = null;
        }

        IEnumerator ReloadSingle()
        {
            Reloading = true;
            while (Ammo < Stats.magazineSize && Reserve > 0)
            {
                if (viewAnimator) viewAnimator.SetTrigger("RoundInsert");
                yield return new WaitForSeconds(Stats.reloadTime / Mathf.Max(1, Stats.magazineSize));
                Ammo++; Reserve--;
                AmmoChanged?.Invoke(this);
            }
            Reloading = false; _reload = null;
        }

        /// <summary>Ящик с боеприпасами: полный запас во всех слотах. true — если что-то пополнилось.</summary>
        public bool RefillAll()
        {
            StoreCurrent();
            bool any = false;
            for (int i = 0; i < _slots.Length; i++)
            {
                var s = _slots[i];
                if (s == null || !s.def || s.def.category is WeaponCategory.Knife or WeaponCategory.Trowel) continue;
                var st = WeaponStats.Compute(s.def, s.atts);
                if (s.reserve < st.reserveAmmo) { s.reserve = st.reserveAmmo; any = true; }
                if (i + 1 != CurrentSlot && s.ammo < st.magazineSize && s.def.category == WeaponCategory.Launcher) { s.ammo = st.magazineSize; any = true; }
            }
            if (CurrentSlot > 0 && CurrentSlot != 4 && _slots[CurrentSlot - 1] != null) { Reserve = _slots[CurrentSlot - 1].reserve; AmmoChanged?.Invoke(this); }
            return any;
        }

        public void AddReserveAmmo(int magazines)
        {
            Reserve = Mathf.Min(Stats.reserveAmmo * 2, Reserve + Stats.magazineSize * magazines);
            AmmoChanged?.Invoke(this);
        }

        // ---------- Положение модели ----------

        // Точки прицельной линии текущего оружия: прорезь целика и вершина мушки
        Renderer _rearL, _rearR, _front;
        GameObject _sightsOf;

        void FindSights()
        {
            _sightsOf = assembler.Root;
            _rearL = _rearR = _front = null;
            if (!_sightsOf) return;
            foreach (var r in _sightsOf.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "RearNotchL") _rearL = r;
                else if (r.name == "RearNotchR") _rearR = r;
                else if (r.name == "IronSights" && !_front) _front = r;
            }
        }

        static Vector3 Top(Renderer r) { var b = r.bounds; return new Vector3(b.center.x, b.max.y, b.center.z); }

        /// <summary>
        /// Прицеливание: оружие поворачивается так, чтобы линия «прорезь целика → вершина мушки» легла точно на ось
        /// камеры, и подводится так, чтобы прорезь была в ~18 см от глаза (оптика — 9 см от окуляра).
        /// Работает по реальной геометрии модели, поэтому мушка всегда в центре экрана, у любого образца.
        /// </summary>
        // ---------------- Гаечный ключ инженера: ремонт техники ----------------
        bool _hasKnife = true;
        float _wrenchNext, _swing;
        /// <summary>Подсказка для HUD: что чиним и сколько осталось (null — ремонтировать нечего).</summary>
        public static string RepairInfo;
        public static float Repair01 = -1f;

        Tank LookedVehicle(float range)
        {
            var cam = player.worldCamera.transform;
            Vector3 o = cam.position + cam.forward * player.ThirdPersonDistance;
            if (Physics.Raycast(o, cam.forward, out var h, range, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore))
            {
                var t = h.collider.GetComponentInParent<Tank>();
                if (t) return t;
            }
            Tank best = null; float bd = 3.2f;
            foreach (var t in Tank.All)
            {
                if (!t || !t.Alive) continue;
                float d = t.DistanceTo(player.transform.position + Vector3.up);
                if (d < bd && Vector3.Dot(cam.forward, (t.transform.position - cam.position).normalized) > 0.2f) { bd = d; best = t; }
            }
            return best;
        }

        void WrenchUpdate(PlayerInputState inp)
        {
            Ads01 = 0; player.Aiming = false; player.TargetFov = 0;
            _swing = Mathf.MoveTowards(_swing, 0, Time.deltaTime * 3.2f);
            UpdateViewModelPose();
            // Взмах ключом: вперёд-вниз и обратно
            float k = Mathf.Sin(_swing * Mathf.PI);
            viewModelRoot.localRotation *= Quaternion.Euler(-55f * k, -10f * k, 20f * k);
            viewModelRoot.localPosition += new Vector3(-0.04f * k, 0.03f * k, 0.08f * k);

            var t = LookedVehicle(3.8f);
            RepairInfo = null; Repair01 = -1f;
            if (t && t.Alive && t.Team == player.Team)
            {
                Repair01 = t.Health / t.maxHealth;
                RepairInfo = Repair01 >= 0.999f ? $"{t.DisplayName}: исправна" : $"{t.DisplayName}: ремонт {Repair01 * 100:0}% · держите ЛКМ";
                if (inp.fireHeld && Repair01 < 0.999f && Time.time >= _wrenchNext)
                {
                    _wrenchNext = Time.time + 0.42f; _swing = 1f;
                    t.Repair(t.maxHealth * (t.IsLight ? 0.06f : 0.035f));
                    Vector3 hp = t.transform.position + Vector3.up * 1.2f;
                    if (Physics.Raycast(player.worldCamera.transform.position, player.worldCamera.transform.forward, out var h, 4f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore)) hp = h.point;
                    BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Metal, UnityEngine.Random.Range(0, 3)), hp, 0.8f);
                    if (FxPool.I) FxPool.I.BrickChips(hp, -player.worldCamera.transform.forward);
                }
                return;
            }
            // Иначе — удар ключом в ближнем бою
            if (inp.firePressed && Time.time >= _wrenchNext)
            {
                _wrenchNext = Time.time + 0.6f; _swing = 1f;
                var ray = player.AimRay();
                float wd = Physics.Raycast(ray, out var rh, 2.2f, CombatWorld.WorldMask) ? rh.distance : 2.2f;
                if (CombatWorld.RaycastActors(ray.origin, ray.direction, wd, player.Team, CombatWorld.PlayerActorId, out var ah))
                {
                    CombatWorld.Damage.Enqueue(new DamageEvent { targetActorId = ah.actorId, sourceActorId = CombatWorld.PlayerActorId, amount = 45f, hitPoint = ah.point, direction = ray.direction });
                    if (FxPool.I) FxPool.I.Blood(ah.point, ray.direction);
                    BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Flesh, 1), ah.point, 0.8f);
                    HitConfirmed?.Invoke(ah);
                }
                else if (wd < 2.2f) BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Metal, 0), rh.point, 0.5f);
                PlayClip(SoundSynth.Holster(), 0.4f);
            }
        }

        void UpdateViewModelPose()
        {
            if (!viewModelRoot) return;
            if (_sightsOf != assembler.Root) FindSights();
            Vector3 hip = hipPosition;
            Quaternion adsRot = Quaternion.identity;
            Vector3 ads = hip;
            var anchor = assembler.AdsAnchor(HasOptic);
            Vector3 rear = default, front = default; bool line = false;
            if (!HasOptic && _rearL && _rearR && _front)
            {
                rear = viewModelRoot.InverseTransformPoint((Top(_rearL) + Top(_rearR)) * 0.5f);
                front = viewModelRoot.InverseTransformPoint(Top(_front));
                line = (front - rear).z > 0.05f;
            }
            if (line)
            {
                adsRot = Quaternion.FromToRotation((front - rear).normalized, Vector3.forward);
                Vector3 r = adsRot * rear;
                ads = new Vector3(-r.x, -r.y, 0.18f - r.z);
            }
            else if (anchor)
            {
                Vector3 a = viewModelRoot.InverseTransformPoint(anchor.position);
                ads = new Vector3(-a.x, -a.y, (HasOptic ? 0.09f : 0.18f) - a.z + 0.02f);
            }
            if (!IsFinite(ads)) ads = hip;
            float k = Mathf.SmoothStep(0, 1, Ads01);
            float bob = player.IsGrounded ? Mathf.Sin(Time.time * (player.IsSprinting ? 14f : 9f)) * 0.008f * player.Speed01 * (1 - Ads01) : 0;
            viewModelRoot.localRotation = Quaternion.Slerp(Quaternion.identity, adsRot, k);
            viewModelRoot.localPosition = Vector3.Lerp(hip, ads, k) + new Vector3(bob * 0.5f, Mathf.Abs(bob), 0);
        }

        static bool IsFinite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || Mathf.Abs(v.x) > 1f || Mathf.Abs(v.y) > 1f || Mathf.Abs(v.z) > 1.5f);

        void PlayClip(AudioClip c, float vol)
        {
            if (!c || !audioSource) return;
            audioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(c, vol);
        }

        IEnumerator BoltCycleSound(float delay)
        {
            yield return new WaitForSeconds(delay);
            PlayClip(SoundSynth.Bolt(), 0.8f);
        }

        void Awake() => HitConfirmed += h => PlayClip(h.headshot ? SoundSynth.Headshot() : SoundSynth.HitTick(), h.headshot ? 0.8f : 0.6f);
    }
}
