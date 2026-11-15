using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Процедурная анимация вида от первого лица (без Animator и клипов), управляется WeaponAnimProfile модели.
    ///
    /// Выстрел: пружинная отдача; откат затвора-кожуха у пистолетов (у Люгера рычаги «ломаются» вверх);
    ///   возвратно-поступательная рукоятка взведения у автоматов; поворот барабана и курок у Нагана;
    ///   у болтовок — полный цикл: правая рука уходит на рукоять, подъём, отвод (вылет гильзы), досыл, запирание.
    /// Гильзы: латунные меш-частицы с гравитацией и отскоком от земли; направление — из профиля оружия.
    /// Перезарядка по типам: магазин вниз / боковой магазин FG-42 влево / диск ДП-28 снимается вверх /
    ///   лента MG-42 с открытием крышки / обойма сверху при открытом затворе / Наган по одному патрону;
    ///   при пустом оружии — взведение левой рукой.
    /// Прочее: доставание, осмотр (T), бег, присед, прыжок/приземление, инерция обзора, дыхание в оптике,
    ///   опускание при ударе ножом и броске гранаты.
    /// </summary>
    public class ViewModelAnimator : MonoBehaviour
    {
        public WeaponController weapon;
        public PlayerController player;
        public WeaponVisualAssembler assembler;
        public KnifeController knife;
        public AudioSource audioSource;

        // Состояние
        Vector3 _kickPos, _kickVel, _kickRot, _kickRotVel;
        float _draw = 1f, _sprint, _crouch, _reloadBlend, _air, _land, _lower, _throw;
        Vector2 _sway;
        int _lastAmmo;
        bool _wasReloading, _wasGrounded = true;
        float _reloadStart, _reloadDur, _boltT = 1f, _slideT = 1f, _chargeT = 1f, _inspectT = -1f, _cylAngle, _cylTarget, _roundPush;
        bool _pendingCasing;
        float _lastRn = 1f, _lastBoltT = 1f, _wall, _strafe, _adsPrev, _adsSettle, _inspMag, _inspMagPrev;

        // Части модели
        WeaponAnimProfile _prof;
        Transform _mag, _magSocket, _handL, _handR, _bolt, _slide, _cover, _cyl, _hammer, _charge, _clip, _warhead;
        Vector3 _magBase, _magSocketBase, _handLBase, _handRBase, _boltBase, _slideBase, _chargeBase;
        Quaternion _boltRot, _slideRot, _coverRot, _hammerRot, _handLRot;
        ParticleSystem _casings;

        void Awake() => BuildCasings();

        void OnEnable()
        {
            if (!weapon) return;
            weapon.WeaponChanged += OnWeaponChanged;
            weapon.AmmoChanged += OnAmmo;
            var gt = GetComponent<GrenadeThrower>();
            if (gt) gt.Thrown += OnThrow;
            // Оружие могли выдать, пока компонент был выключен (экран высадки) — подхватываем его детали
            if (assembler && assembler.Root) OnWeaponChanged(weapon);
        }

        void OnDisable()
        {
            if (!weapon) return;
            weapon.WeaponChanged -= OnWeaponChanged;
            weapon.AmmoChanged -= OnAmmo;
            var gt = GetComponent<GrenadeThrower>();
            if (gt) gt.Thrown -= OnThrow;
        }

        void OnThrow() => _throw = 1f;

        void BuildCasings()
        {
            var go = new GameObject("Casings");
            go.transform.SetParent(transform, false);
            _casings = go.AddComponent<ParticleSystem>();
            _casings.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _casings.main;
            main.loop = false; main.playOnAwake = false; main.startLifetime = 3f; main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 80; main.startSize3D = true; main.startRotation3D = true;
            var em = _casings.emission; em.enabled = false;
            var sh = _casings.shape; sh.enabled = false;
            var col = _casings.collision;
            col.enabled = true; col.type = ParticleSystemCollisionType.World; col.mode = ParticleSystemCollisionMode.Collision3D;
            col.bounce = 0.35f; col.dampen = 0.5f; col.lifetimeLoss = 0.1f; col.quality = ParticleSystemCollisionQuality.Low;
            col.collidesWith = CombatWorld.WorldMask != 0 ? CombatWorld.WorldMask : Physics.DefaultRaycastLayers;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = MeshUtil.Cylinder(6);
            r.sharedMaterial = ProceduralModels.Mat(ProceduralModels.Brass, 0.6f, 0.9f);
            r.alignment = ParticleSystemRenderSpace.World;
        }

        void OnWeaponChanged(WeaponController w)
        {
            _draw = 0f; _inspectT = -1f; _checkAt = Time.time + 1.2f + w.DrawTime;
            _kickPos = _kickVel = _kickRot = _kickRotVel = Vector3.zero;
            var r = assembler && assembler.Root ? assembler.Root.transform : null;
            _prof = r ? r.GetComponent<WeaponAnimProfile>() : null;
            _mag = Find(r, "Mag_Standard"); _magSocket = Find(r, "Socket_Mag");
            _handL = Find(r, "HandL"); _handR = Find(r, "HandR");
            _bolt = Find(r, "BoltHandle"); _slide = Find(r, "Slide"); _cover = Find(r, "TopCover");
            _cyl = Find(r, "Cylinder"); _hammer = Find(r, "Hammer"); _charge = Find(r, "ChargingHandle");
            _warhead = Find(r, "Warhead");
            if (_mag) _magBase = _mag.localPosition;
            if (_magSocket) _magSocketBase = _magSocket.localPosition;
            if (_handL) { _handLBase = _handL.localPosition; _handLRot = _handL.localRotation; }
            if (_handR) _handRBase = _handR.localPosition;
            if (_bolt) { _boltBase = _bolt.localPosition; _boltRot = _bolt.localRotation; }
            if (_slide) { _slideBase = _slide.localPosition; _slideRot = _slide.localRotation; }
            if (_cover) _coverRot = _cover.localRotation;
            if (_hammer) _hammerRot = _hammer.localRotation;
            if (_charge) _chargeBase = _charge.localPosition;
            if (_clip) Destroy(_clip.gameObject);
            _clip = null;
            if (_prof && _prof.style == WeaponAnimProfile.Style.Clip && _prof.showClip && _handL)
            {
                _clip = ProceduralModels.StripperClip(_handL).transform;
                _clip.localPosition = new Vector3(0.01f, 0.03f, 0.02f); _clip.localRotation = Quaternion.Euler(0, 0, 90);
                foreach (var c in _clip.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = _handL.gameObject.layer;
                foreach (var rr in _clip.GetComponentsInChildren<Renderer>(true)) rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _clip.gameObject.SetActive(false);
            }
            _lastAmmo = w.Ammo; _boltT = _slideT = _chargeT = 1f; _cylAngle = _cylTarget = 0;
        }

        void OnAmmo(WeaponController w)
        {
            if (!w.Current) { _lastAmmo = w.Ammo; return; }
            if (w.Ammo < _lastAmmo && !w.Reloading)
            {
                bool bolt = w.Current.fireMode == FireMode.Bolt;
                bool pistol = w.Current.category is WeaponCategory.Pistol or WeaponCategory.Pistol2;
                bool at = w.Current.category == WeaponCategory.Launcher;
                float k = (at ? 2.6f : bolt ? 1.8f : pistol ? 1.25f : w.Current.category == WeaponCategory.MachineGun ? 0.8f : 1f) * Mathf.Lerp(1f, 0.5f, w.Ads01);
                _kickVel += new Vector3(Random.Range(-0.015f, 0.015f), 0.015f, -0.085f) * k;
                _kickRotVel += new Vector3(-95f, Random.Range(-28f, 28f), Random.Range(-40f, 40f)) * k;
                _inspectT = -1f;
                if (bolt) { if (w.Ammo > 0) { _boltT = 0f; _pendingCasing = true; } }
                else
                {
                    _slideT = 0f; _chargeT = 0f;
                    if (_prof == null || _prof.ejectsCasings) EjectCasing();
                }
                if (_cyl) _cylTarget += 360f / 7f;
            }
            else if (w.Ammo > _lastAmmo && w.Reloading)
            {
                _roundPush = 1f;                                  // патрон/обойма досланы
                if (_cyl) { _cylTarget += 360f / 7f; Mech(SoundSynth.CylinderTick(), 0.5f); }
                var st = _prof ? _prof.style : WeaponAnimProfile.Style.Magazine;
                if (st == WeaponAnimProfile.Style.Clip) Mech(w.Ammo - _lastAmmo >= 3 ? SoundSynth.ClipIn() : SoundSynth.RoundIn(), 0.8f);
                else if (st == WeaponAnimProfile.Style.Revolver) Mech(SoundSynth.RoundIn(), 0.7f);
            }
            _lastAmmo = w.Ammo;
        }

        void EjectCasing()
        {
            if (!_casings || !assembler || !assembler.Sockets) return;
            var p = assembler.Sockets.shellEjectPoint; if (!p) return;
            Vector3 dir = _prof ? _prof.ejectDir : new Vector3(1, 0.6f, 0);
            Vector3 vel = p.TransformDirection(dir.normalized) * Random.Range(1.6f, 2.6f) + player.GetComponent<CharacterController>().velocity;
            float len = _prof ? _prof.casingLength : 0.03f, rad = _prof ? _prof.casingRadius : 0.006f;
            _casings.Emit(new ParticleSystem.EmitParams
            {
                position = p.position, velocity = vel, startSize3D = new Vector3(rad * 2, len, rad * 2),
                rotation3D = new Vector3(Random.Range(0, 360f), Random.Range(0, 360f), 90),
                angularVelocity3D = new Vector3(Random.Range(-900, 900), Random.Range(-900, 900), 0), startLifetime = 3f, startColor = Color.white
            }, 1);
            if (audioSource && Random.value < 0.6f) audioSource.PlayOneShot(SoundSynth.ShellTink(Random.Range(0, 3)), 0.25f);
        }

        void Mech(AudioClip c, float vol)
        {
            if (!audioSource || !c) return;
            audioSource.pitch = Random.Range(0.96f, 1.04f);
            audioSource.PlayOneShot(c, vol);
        }

        static bool Cross(float prev, float cur, float at) => prev < at && cur >= at;

        /// <summary>Вариант осмотра: смещения (м) и повороты (°) четырёх фаз, «закрутка» и что проверяется.</summary>
        struct InspectVariant
        {
            public Vector3 pA, rA, pB, rB, pC, rC, pD, rD, spin;
            public int check;          // 0 — магазин, 1 — затвор/рукоятка заряжания, 2 — крышка пулемёта
        }
        InspectVariant _iv;
        int _lastVariant = -1;

        /// <summary>Разные осмотры: у каждого типа оружия свой набор, вариант выбирается случайно и не повторяется подряд.</summary>
        void PickInspectVariant()
        {
            var cat = weapon && weapon.Current ? weapon.Current.category : WeaponCategory.AssaultRifle;
            var list = new System.Collections.Generic.List<InspectVariant>();
            // Классика: правый бок → левый бок у лица → магазин → вдоль ствола
            list.Add(new InspectVariant { pA = new(0.03f, 0.03f, -0.06f), rA = new(-14, 58, 30), pB = new(-0.07f, 0.04f, -0.05f), rB = new(-10, -48, -68),
                                          pC = new(-0.02f, -0.02f, -0.03f), rC = new(24, 22, -38), pD = new(-0.01f, 0.05f, -0.02f), rD = new(-22, 8, 4), check = 0 });
            // Окно выброса: завал на левый бок, оттянуть затвор и заглянуть в патронник
            list.Add(new InspectVariant { pA = new(-0.04f, 0.05f, -0.07f), rA = new(-8, -20, -82), pB = new(-0.05f, 0.06f, -0.09f), rB = new(-18, -30, -88),
                                          pC = new(-0.03f, 0.03f, -0.06f), rC = new(-12, -24, -70), pD = new(0.0f, 0.02f, 0.02f), rD = new(-6, 30, 12), check = 1 });
            if (cat == WeaponCategory.Pistol)
            {
                // Пистолет: поворот кистью «туда-обратно» с полным переворотом и проверка затвора
                list.Add(new InspectVariant { pA = new(-0.03f, 0.05f, -0.05f), rA = new(-20, 40, 0), pB = new(-0.04f, 0.06f, -0.06f), rB = new(-24, -40, 0),
                                              pC = new(-0.02f, 0.03f, -0.04f), rC = new(10, 0, -60), pD = new(0, 0.03f, 0), rD = new(-30, 0, 0),
                                              spin = new Vector3(0, 0, 360), check = 1 });
            }
            else if (cat == WeaponCategory.MachineGun)
            {
                // Пулемёт: вскинуть, приоткрыть крышку, похлопать по коробу
                list.Add(new InspectVariant { pA = new(0.02f, 0.06f, -0.04f), rA = new(-28, 18, 10), pB = new(-0.04f, 0.05f, -0.06f), rB = new(-20, -25, -30),
                                              pC = new(-0.02f, 0.02f, -0.05f), rC = new(-10, -10, -18), pD = new(0.01f, 0.03f, 0), rD = new(-15, 12, 6), check = 2 });
            }
            else if (cat == WeaponCategory.SniperRifle || cat == WeaponCategory.BattleRifle)
            {
                // Винтовка: вскинуть к плечу, осмотреть прицел, перевернуть и проверить затвор
                list.Add(new InspectVariant { pA = new(-0.02f, 0.06f, -0.08f), rA = new(-6, -8, -6), pB = new(0.04f, 0.02f, -0.04f), rB = new(-12, 62, 48),
                                              pC = new(-0.03f, 0.02f, -0.05f), rC = new(-8, -26, -76), pD = new(0, 0.05f, 0.02f), rD = new(-26, 4, 0), check = 1 });
            }
            else if (cat == WeaponCategory.SubmachineGun || cat == WeaponCategory.AssaultRifle)
            {
                // Пистолет-пулемёт/автомат: быстрый «флип» на ремне и шлепок по магазину
                list.Add(new InspectVariant { pA = new(0.03f, 0.05f, -0.05f), rA = new(-20, 70, 20), pB = new(-0.06f, 0.05f, -0.05f), rB = new(-16, -70, -40),
                                              pC = new(-0.02f, -0.03f, -0.02f), rC = new(30, 10, -30), pD = new(0, 0.04f, 0), rD = new(-18, 0, 0),
                                              spin = new Vector3(0, 0, -180), check = 0 });
            }
            int pick = UnityEngine.Random.Range(0, list.Count);
            if (list.Count > 1 && pick == _lastVariant) pick = (pick + 1) % list.Count;
            _lastVariant = pick;
            _iv = list[pick];
        }

        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        float _checkAt = -1f;

        /// <summary>Самопроверка: через секунду после доставания оружия хоть один его рендер должен быть видим камерой.
        /// Если нет — пишем подробную сводку в консоль (её удобно прислать для разбора).</summary>
        void CheckVisibleOnce()
        {
            if (_checkAt < 0 || Time.time < _checkAt) return;
            _checkAt = -1f;
            var cam = player ? player.worldCamera : null;
            if (!cam || !assembler || !assembler.Root || (cam.cullingMask & (1 << 31)) == 0) return;
            foreach (var r in assembler.Root.GetComponentsInChildren<Renderer>()) if (r.isVisible) return;
            Debug.LogWarning("[Frontline] Модель оружия от первого лица не попала в кадр.\n" + assembler.Describe(cam));
        }

        /// <summary>Строка диагностики для HUD (F3).</summary>
        public string Diagnostics()
        {
            var cam = player ? player.worldCamera : null;
            string s = assembler ? assembler.Describe(cam) : "assembler = null";
            s += $"\nанимация: draw={_draw:F2} holster={(weapon ? weapon.Holster01 : 0):F2} lower={_lower:F2} wall={_wall:F2} kick={_kickPos.magnitude:F3}";
            if (cam) s += $"\nкамера: mask31={(cam.cullingMask & (1 << 31)) != 0} fov={cam.fieldOfView:F0} pos={cam.transform.localPosition:F2}";
            if (knife) s += $"\nнож: showing={knife.Showing}";
            return s;
        }

        /// <summary>Озвучка фаз: каждый звук привязан к моменту движения детали.</summary>
        void PhaseSounds(WeaponAnimProfile.Style style, float rn, bool reloading, bool emptyReload, bool justStarted, bool justEnded)
        {
            if (reloading)
            {
                float p = justStarted ? 0f : _lastRn;
                switch (style)
                {
                    case WeaponAnimProfile.Style.Clip:
                        if (justStarted) { Mech(SoundSynth.BoltUp(), 0.7f); Mech(SoundSynth.BoltBack(), 0.8f); if (weapon.Current && weapon.Current.magazineSize == 1) EjectCasing(); }
                        break;
                    case WeaponAnimProfile.Style.Revolver:
                        if (justStarted) Mech(SoundSynth.CoverOpen(), 0.5f);   // дверца барабана
                        break;
                    case WeaponAnimProfile.Style.Launcher:
                        if (Cross(p, rn, 0.2f)) Mech(SoundSynth.Thud(), 0.5f);          // пустая труба брошена
                        if (Cross(p, rn, 0.62f)) Mech(SoundSynth.CoverClose(), 0.8f);  // новый фауст, прицел откинут
                        break;
                    default:
                        if (style == WeaponAnimProfile.Style.Belt)
                        {
                            if (Cross(p, rn, 0.07f)) Mech(SoundSynth.CoverOpen(), 0.8f);
                            if (Cross(p, rn, 0.78f)) Mech(SoundSynth.CoverClose(), 0.9f);
                        }
                        if (Cross(p, rn, 0.17f)) Mech(style == WeaponAnimProfile.Style.PanTop ? SoundSynth.PanOff() : SoundSynth.MagOut(), 0.85f);
                        if (Cross(p, rn, 0.66f)) Mech(SoundSynth.MagIn(), 0.9f);
                        if (emptyReload && Cross(p, rn, 0.89f)) Mech(SoundSynth.ChargeBack(), 0.9f);
                        break;
                }
            }
            if (justEnded && style == WeaponAnimProfile.Style.Clip) { Mech(SoundSynth.BoltForward(), 0.8f); Mech(SoundSynth.BoltDown(), 0.7f); }
            if (justEnded && style == WeaponAnimProfile.Style.Revolver) Mech(SoundSynth.CoverClose(), 0.5f);
            _lastRn = reloading ? rn : 1f;
            if (_inspMagPrev < 0.5f && _inspMag >= 0.5f) Mech(SoundSynth.MagOut(), 0.35f);
            if (_inspMagPrev >= 0.5f && _inspMag < 0.5f) Mech(SoundSynth.MagIn(), 0.45f);
            _inspMagPrev = _inspMag;

            // Цикл затвора болтовки после выстрела
            if (Cross(_lastBoltT, _boltT, 0.16f)) Mech(SoundSynth.BoltUp(), 0.7f);
            if (Cross(_lastBoltT, _boltT, 0.33f)) Mech(SoundSynth.BoltBack(), 0.8f);
            if (Cross(_lastBoltT, _boltT, 0.5f)) Mech(SoundSynth.BoltForward(), 0.8f);
            if (Cross(_lastBoltT, _boltT, 0.72f)) Mech(SoundSynth.BoltDown(), 0.7f);
            _lastBoltT = _boltT;
        }

        static float Ease(float a, float b, float t) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, t));
        static float Bump(float a, float b, float c, float d, float t) => Ease(a, b, t) * (1 - Ease(c, d, t));

        void LateUpdate()
        {
            if (!assembler || !assembler.Root || !player || !weapon) return;
            var root = assembler.Root.transform;
            float dt = Time.deltaTime, t = Time.time;
            bool reloading = weapon.Reloading;
            var style = _prof ? _prof.style : WeaponAnimProfile.Style.Magazine;
            bool clipLike = style is WeaponAnimProfile.Style.Clip or WeaponAnimProfile.Style.Revolver;

            // Пружины отдачи — мелкими шагами (≤ 1/240 с). Одним шагом при низком FPS (dt > 0.12 с) явная схема
            // расходилась: отдача раскачивалась до бесконечности, позиция становилась NaN и оружие исчезало навсегда.
            float left = Mathf.Min(dt, 0.1f);
            while (left > 1e-5f)
            {
                float h = Mathf.Min(left, 1f / 240f); left -= h;
                _kickVel += (-_kickPos * 190f - _kickVel * 17f) * h; _kickPos += _kickVel * h;
                _kickRotVel += (-_kickRot * 210f - _kickRotVel * 19f) * h; _kickRot += _kickRotVel * h;
            }
            _kickPos = Vector3.ClampMagnitude(_kickPos, 0.25f);
            _kickRot = Vector3.ClampMagnitude(_kickRot, 60f);
            if (!Finite(_kickPos) || !Finite(_kickVel) || !Finite(_kickRot) || !Finite(_kickRotVel))
                _kickPos = _kickVel = _kickRot = _kickRotVel = Vector3.zero;

            bool knifeOut = knife && knife.Showing;
            _draw = Mathf.MoveTowards(_draw, 1f, dt / Mathf.Max(0.15f, weapon.DrawTime));
            _sprint = Mathf.MoveTowards(_sprint, player.IsSprinting && !reloading ? 1f : 0f, dt * 5f);
            _crouch = Mathf.MoveTowards(_crouch, player.IsCrouched ? 1f : 0f, dt * 5f);
            _reloadBlend = Mathf.MoveTowards(_reloadBlend, reloading ? 1f : 0f, dt * 6f);
            _lower = Mathf.MoveTowards(_lower, knifeOut ? 1f : 0f, dt * 9f);
            _throw = Mathf.MoveTowards(_throw, 0f, dt * 2.2f);
            _roundPush = Mathf.MoveTowards(_roundPush, 0f, dt * 5f);

            // Осмотр оружия (T)
            if (player.Input.inspectPressed && !reloading && weapon.Ads01 < 0.1f && _inspectT < 0) { _inspectT = 0f; PickInspectVariant(); }
            if (_inspectT >= 0) { _inspectT += dt / 4.6f; if (_inspectT >= 1f || reloading || weapon.Ads01 > 0.2f || player.IsSprinting) _inspectT = -1f; }
            float insp = Mathf.Max(0, _inspectT);
            // Осмотр в четыре фазы: (A) поднять и повернуть правым боком, (B) перекатить на левый бок ближе к лицу,
            // (C) проверка магазина/затвора — оружие наклоняется, левая рука отводит магазин (или затвор) и досылает,
            // (D) взгляд вдоль ствола и возврат. Между фазами — плавные перекаты, а не «рывки».
            float inspA = Bump(0.02f, 0.13f, 0.28f, 0.36f, insp), inspB = Bump(0.3f, 0.4f, 0.52f, 0.6f, insp);
            float inspC = Bump(0.56f, 0.64f, 0.8f, 0.86f, insp), inspD = Bump(0.82f, 0.88f, 0.93f, 0.99f, insp);
            _inspMag = insp > 0 ? Bump(0.62f, 0.68f, 0.74f, 0.79f, insp) : 0f;
            float inspSway = insp > 0 ? Mathf.Sin(insp * 23f) * 0.6f * (1f - inspC) : 0f;    // живые микродвижения кистей

            // Таймлайн перезарядки
            bool justStarted = reloading && !_wasReloading, justEnded = !reloading && _wasReloading;
            if (justStarted)
            {
                _reloadStart = t;
                _reloadDur = weapon.Ammo == 0 ? weapon.Stats.reloadTimeEmpty : weapon.Stats.reloadTime;
            }
            _wasReloading = reloading;
            float rn = reloading ? Mathf.Clamp01((t - _reloadStart) / Mathf.Max(0.3f, _reloadDur)) : 1f;
            bool emptyReload = reloading && weapon.Ammo == 0;

            // Прыжок / приземление
            if (!_wasGrounded && player.IsGrounded) _land = 1f;
            _wasGrounded = player.IsGrounded;
            _land = Mathf.MoveTowards(_land, 0, dt * 4f);
            _air = Mathf.MoveTowards(_air, player.IsGrounded ? 0 : 1, dt * 5f);

            _sway = Vector2.Lerp(_sway, Vector2.ClampMagnitude(player.Input.lookDelta * 0.7f, 5f), dt * 9f);
            float ads = weapon.Ads01;
            // Доставание (время зависит от оружия) и убирание перед сменой — одна кривая «из-под кадра»
            float drawCurve = Mathf.Max(1f - Mathf.SmoothStep(0, 1, _draw), Mathf.SmoothStep(0, 1, weapon.Holster01));
            float walk = player.IsGrounded ? player.Speed01 * (1 - ads * 0.85f) : 0;
            float ph = t * (player.IsSprinting ? 13f : 8.5f);

            Vector3 bob = new Vector3(Mathf.Sin(ph * 0.5f) * 0.014f, -Mathf.Abs(Mathf.Cos(ph * 0.5f)) * 0.012f, 0) * walk * (1 + _sprint * 1.5f);
            Vector3 bobRot = new Vector3(Mathf.Abs(Mathf.Cos(ph * 0.5f)) * 2f, Mathf.Sin(ph * 0.5f) * 2f, Mathf.Sin(ph * 0.5f) * 3f) * walk * (1 + _sprint);

            // Дыхание: слабое от бедра, заметное в оптике (восьмёрка)
            float breathAmp = weapon.HasOptic ? Mathf.Lerp(0.0015f, 0.0009f, ads) : 0.0015f * (1 - ads);
            Vector3 breath = new Vector3(Mathf.Sin(t * 1.1f), Mathf.Sin(t * 2.2f) * 0.6f, 0) * breathAmp;
            Vector3 breathRot = weapon.HasOptic ? new Vector3(Mathf.Sin(t * 2.2f) * 0.25f, Mathf.Sin(t * 1.1f) * 0.35f, 0) * ads : Vector3.zero;

            // Цикл затвора болтовки
            _boltT = Mathf.MoveTowards(_boltT, 1f, dt / 0.8f);
            float boltRoll = Bump(0.05f, 0.2f, 0.8f, 0.98f, _boltT);
            if (_pendingCasing && _boltT > 0.45f) { _pendingCasing = false; EjectCasing(); }

            // Поза перезарядки по типу оружия
            Vector3 reloadPos, reloadRot;
            switch (style)
            {
                case WeaponAnimProfile.Style.Clip: reloadPos = new Vector3(-0.03f, 0.03f, -0.03f); reloadRot = new Vector3(-10f, 18f, 32f); break;
                case WeaponAnimProfile.Style.Revolver: reloadPos = new Vector3(-0.04f, 0.04f, -0.02f); reloadRot = new Vector3(-25f, -30f, -35f); break;
                case WeaponAnimProfile.Style.PanTop: reloadPos = new Vector3(0, -0.05f, -0.02f); reloadRot = new Vector3(12f, 6f, 8f); break;
                case WeaponAnimProfile.Style.Belt: reloadPos = new Vector3(0.02f, -0.04f, -0.02f); reloadRot = new Vector3(10f, 14f, -14f); break;
                case WeaponAnimProfile.Style.SideMag: reloadPos = new Vector3(0.03f, -0.01f, 0); reloadRot = new Vector3(-4f, 16f, -20f); break;
                case WeaponAnimProfile.Style.Launcher:
                {
                    // Пустую трубу — вниз из кадра, новую — обратно
                    float dip = Bump(0.05f, 0.35f, 0.55f, 0.9f, rn);
                    reloadPos = new Vector3(0.06f, -0.42f, -0.12f) * dip; reloadRot = new Vector3(45f, -18f, 12f) * dip;
                    break;
                }
                default: reloadPos = new Vector3(-0.02f, -0.02f, 0.02f); reloadRot = new Vector3(-6f, 12f, 28f); break;
            }
            if (!clipLike && emptyReload) reloadRot += new Vector3(0, 0, 10f) * Bump(0.8f, 0.86f, 0.93f, 1f, rn);
            reloadRot += new Vector3(-3f, 0, 0) * _roundPush;   // толчок при досылании патрона/магазина

            // Упор в стену: оружие отводится назад и вниз (как в Polyfield у стен и изгородей)
            var camT = player.worldCamera.transform;
            float wallTarget = Physics.Raycast(camT.position, camT.forward, out var wh, 0.95f, CombatWorld.WorldMask & ~((1 << 31) | (1 << 2)), QueryTriggerInteraction.Ignore)
                ? 1f - Mathf.InverseLerp(0.35f, 0.95f, wh.distance) : 0f;
            _wall = Mathf.MoveTowards(_wall, wallTarget, dt * 6f);
            // Наклон оружия при стрейфе, «дожим» в прицел с небольшим перелётом
            _strafe = Mathf.Lerp(_strafe, player.Input.move.x * (1 - ads * 0.7f), dt * 7f);
            float adsVel = (ads - _adsPrev) / Mathf.Max(dt, 1e-4f); _adsPrev = ads;
            _adsSettle = Mathf.Lerp(_adsSettle, Mathf.Clamp(adsVel * 0.05f, -1, 1), dt * 10f);

            Vector3 pos = _kickPos + bob + breath
                + new Vector3(0.01f, -0.06f, -0.14f) * _wall
                + new Vector3(0.006f, 0, 0) * _strafe
                + new Vector3(0, -0.006f, 0.004f) * _adsSettle
                + new Vector3(0.02f, -0.28f, -0.06f) * drawCurve
                + new Vector3(-0.05f, -0.06f, -0.04f) * _sprint
                + new Vector3(0, -0.02f, 0) * _crouch
                + reloadPos * _reloadBlend
                + new Vector3(0.05f, -0.3f, -0.08f) * _lower
                + new Vector3(0, -0.12f, -0.03f) * Mathf.Sin(_throw * Mathf.PI)
                + _iv.pA * inspA + _iv.pB * inspB + _iv.pC * inspC + _iv.pD * inspD
                + new Vector3(0, 0.03f, 0) * _air + new Vector3(0, -0.035f, 0) * Mathf.Sin(_land * Mathf.PI);

            Vector3 rot = _kickRot + bobRot + breathRot
                + new Vector3(18f, -28f, 10f) * _wall
                + new Vector3(0, 0, -5f) * _strafe
                + new Vector3(-1.5f, 0, 0) * _adsSettle
                + new Vector3(55f, -10f, 0) * drawCurve
                + new Vector3(12f, -40f, 22f) * _sprint
                + reloadRot * _reloadBlend
                + new Vector3(40f, -20f, 0) * _lower
                + new Vector3(25f, -10f, 0) * Mathf.Sin(_throw * Mathf.PI)
                + _iv.rA * inspA + _iv.rB * inspB + _iv.rC * inspC + _iv.rD * inspD
                + _iv.spin * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.3f, 0.6f, insp)) * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.6f, 0.7f, insp)))
                + new Vector3(inspSway, inspSway * 0.7f, 0)
                + new Vector3(0, 0, -16f) * boltRoll
                + new Vector3(-6f * _air, 0, 0)
                + new Vector3(_sway.y, -_sway.x, -_sway.x * 1.8f) * (1 - ads * 0.85f);

            // В прицеле прицельная линия должна смотреть точно в центр: покачивания, наклоны и инерция гасятся,
            // остаются только отдача (быстро возвращается) и дыхание
            float lockK = Mathf.SmoothStep(0, 1, ads) * (reloading ? 0f : 1f);
            if (lockK > 0f)
            {
                pos = Vector3.Lerp(pos, new Vector3(_kickPos.x * 0.3f, _kickPos.y * 0.5f, _kickPos.z) + breath, lockK);
                rot = Vector3.Lerp(rot, new Vector3(_kickRot.x * 0.3f, _kickRot.y * 0.15f, _kickRot.z * 0.1f) + breathRot, lockK);
            }
            // Страховка: модель всегда остаётся у камеры — никаких NaN и улётов за пределы кадра
            if (!Finite(pos) || pos.sqrMagnitude > 0.5f * 0.5f) pos = Vector3.ClampMagnitude(Finite(pos) ? pos : Vector3.zero, 0.5f);
            if (!Finite(rot)) rot = Vector3.zero;
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(rot);
            root.localScale = Vector3.one;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            CheckVisibleOnce();

            AnimateParts(style, rn, reloading, emptyReload, dt);
            PhaseSounds(style, rn, reloading, emptyReload, justStarted, justEnded);
        }

        void AnimateParts(WeaponAnimProfile.Style style, float rn, bool reloading, bool emptyReload, float dt)
        {
            Vector3 magOff = Vector3.zero, handLOff = Vector3.zero, handROff = Vector3.zero;
            Quaternion handLRot = _handLRot;
            float cover = 0f, charge = 0f;
            bool showClip = false;

            // Проверка при осмотре: магазин чуть выходит и досылается ладонью; у затворных — затвор приоткрывается
            if (!reloading && _inspMag > 0f)
            {
                if (_iv.check == 2) cover = _inspMag * 0.8f;                     // пулемёт: приоткрыть крышку ствольной коробки
                else if (_iv.check == 1 || style == WeaponAnimProfile.Style.Clip || style == WeaponAnimProfile.Style.Revolver) charge = _inspMag * 0.6f;
                else
                {
                    Vector3 outDir = _prof ? _prof.magOut : new Vector3(0, -0.32f, 0.03f);
                    magOff = outDir.normalized * 0.04f * _inspMag;
                    handLOff = (_magBase + new Vector3(-0.03f, -0.02f, 0) - _handLBase) * Mathf.Min(1f, _inspMag * 1.6f) + magOff;
                }
            }

            if (reloading)
            {
                switch (style)
                {
                    case WeaponAnimProfile.Style.Clip:
                    {
                        // Затвор открыт; левая рука с обоймой над окном ствольной коробки вдавливает патроны
                        float push = Mathf.Max(_roundPush, Mathf.Abs(Mathf.Sin((Time.time - _reloadStart) * 5f)) * 0.5f);
                        handLOff = (_prof.clipHandPos + new Vector3(0, -0.035f * push, 0) - _handLBase) * _reloadBlend;
                        handLRot = _handLRot * Quaternion.Euler(0, 0, -70f * _reloadBlend);
                        showClip = _reloadBlend > 0.5f;
                        break;
                    }
                    case WeaponAnimProfile.Style.Launcher:
                        break;
                    case WeaponAnimProfile.Style.Revolver:
                    {
                        float push = _roundPush;
                        handLOff = (_prof.clipHandPos + new Vector3(-0.02f * push, 0, 0.02f * push) - _handLBase) * _reloadBlend;
                        break;
                    }
                    default:
                    {
                        // 0–0.15 рука к магазину · 0.15–0.4 магазин уходит · 0.4–0.65 новый · 0.65–0.72 досыл · 0.75–0.9 рука назад
                        Vector3 outDir = _prof ? _prof.magOut : new Vector3(0, -0.32f, 0.03f);
                        float reach = Ease(0f, 0.15f, rn) * (1 - Ease(0.74f, 0.82f, rn));
                        float away = Bump(0.15f, 0.4f, 0.4f, 0.65f, rn);
                        float seat = (1 - Ease(0.65f, 0.72f, rn)) * Ease(0.4f, 0.5f, rn);
                        magOff = outDir * away + outDir.normalized * 0.035f * seat;
                        Vector3 grip = style == WeaponAnimProfile.Style.PanTop ? new Vector3(0, 0.03f, 0) : new Vector3(-0.03f, -0.02f, 0);
                        handLOff = (_magBase + grip - _handLBase) * reach + magOff * reach;
                        if (style == WeaponAnimProfile.Style.PanTop) handLRot = _handLRot * Quaternion.Euler(0, 0, 180f * reach);
                        if (style == WeaponAnimProfile.Style.Belt) cover = Bump(0.05f, 0.18f, 0.72f, 0.82f, rn);
                        // Взведение при пустом оружии: левая рука на рукоятку взведения, рывок назад
                        if (emptyReload && (_charge || _bolt))
                        {
                            float toCharge = Bump(0.8f, 0.86f, 0.95f, 1f, rn);
                            charge = Bump(0.86f, 0.9f, 0.92f, 0.95f, rn);
                            Vector3 cp = _charge ? _chargeBase : _boltBase;
                            handLOff = Vector3.Lerp(handLOff, cp + new Vector3(-0.02f, 0, -0.06f * charge) - _handLBase, toCharge);
                        }
                        break;
                    }
                }
            }

            // ---- Затвор болтовки и правая рука ----
            float lift = 0, back = 0;
            if (_bolt && _prof && _prof.style == WeaponAnimProfile.Style.Clip)
            {
                if (reloading) { lift = 1; back = 1; }
                else
                {
                    lift = Bump(0.15f, 0.3f, 0.7f, 0.85f, _boltT);
                    back = Bump(0.3f, 0.45f, 0.5f, 0.68f, _boltT);
                }
                float handOn = reloading ? 0 : Bump(0.0f, 0.15f, 0.85f, 1f, _boltT);
                _bolt.localRotation = _boltRot * Quaternion.Euler(0, 0, _prof.boltLift * lift);
                _bolt.localPosition = _boltBase + new Vector3(0, 0, -_prof.boltTravel * back);
                // Правая рука: с шейки ложи — на рукоять затвора и обратно
                Vector3 knob = _boltBase + new Vector3(0.05f, 0.02f * lift - 0.01f, -_prof.boltTravel * back);
                handROff = (knob - _handRBase) * handOn;
            }
            else if (_bolt && _charge == null)
            {
                _bolt.localPosition = _boltBase + new Vector3(0, 0, -0.06f * Mathf.Max(charge, Mathf.Sin(_chargeT * Mathf.PI)));
            }

            // ---- Рукоятка взведения автоматики ----
            _chargeT = Mathf.MoveTowards(_chargeT, 1f, dt / 0.07f);
            if (_charge) _charge.localPosition = _chargeBase + new Vector3(0, 0, -(_prof ? _prof.boltTravel : 0.05f) * Mathf.Max(charge, Mathf.Sin(_chargeT * Mathf.PI) * 0.8f));

            // ---- Кожух-затвор пистолета / рычаги Люгера ----
            _slideT = Mathf.MoveTowards(_slideT, 1f, dt / 0.09f);
            if (_slide)
            {
                bool lockedBack = emptyReload && rn < 0.88f;
                float sl = lockedBack ? 1f : Mathf.Sin(_slideT * Mathf.PI);
                bool toggle = weapon.Current && weapon.Current.id == "p08";
                float travel = _prof ? _prof.boltTravel : 0.035f;
                _slide.localPosition = _slideBase + new Vector3(0, toggle ? 0.012f * sl : 0, -travel * sl);
                _slide.localRotation = toggle ? _slideRot * Quaternion.Euler(-35f * sl, 0, 0) : _slideRot;
            }

            // ---- Фаустпатрон: граната видна, пока заряжен (или новый уже поднят) ----
            if (_warhead) _warhead.gameObject.SetActive(weapon.Ammo > 0 || (reloading && rn > 0.5f));

            // ---- Наган: барабан и курок ----
            if (_cyl) { _cylAngle = Mathf.Lerp(_cylAngle, _cylTarget, Mathf.Min(1, dt * 22f)); _cyl.localRotation = Quaternion.Euler(0, 0, _cylAngle); }
            if (_hammer) _hammer.localRotation = _hammerRot * Quaternion.Euler(-35f * Mathf.Sin(Mathf.Clamp01(_slideT) * Mathf.PI), 0, 0);

            // ---- Крышка лентоприёмника MG-42 ----
            if (_cover) _cover.localRotation = _coverRot * Quaternion.Euler(-65f * cover, 0, 0);

            // ---- Применение ----
            float kM = Mathf.Min(1, dt * 25f), kH = Mathf.Min(1, dt * 18f);
            if (_mag) _mag.localPosition = Vector3.Lerp(_mag.localPosition, _magBase + magOff, kM);
            if (_magSocket) _magSocket.localPosition = Vector3.Lerp(_magSocket.localPosition, _magSocketBase + magOff, kM);
            if (_handL)
            {
                _handL.localPosition = Vector3.Lerp(_handL.localPosition, _handLBase + handLOff, kH);
                _handL.localRotation = Quaternion.Slerp(_handL.localRotation, handLRot, kH);
            }
            if (_handR) _handR.localPosition = Vector3.Lerp(_handR.localPosition, _handRBase + handROff, Mathf.Min(1, dt * 22f));
            if (_clip && _clip.gameObject.activeSelf != showClip) _clip.gameObject.SetActive(showClip);
        }

        static Transform Find(Transform r, string n)
        {
            if (!r) return null;
            if (r.name == n) return r;
            foreach (Transform c in r) { var x = Find(c, n); if (x) return x; }
            return null;
        }
    }
}
