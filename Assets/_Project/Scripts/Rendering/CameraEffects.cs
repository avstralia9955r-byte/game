using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Frontline
{
    /// <summary>
    /// Покачивание камеры и тряска: шаг (восьмёрка, сильнее при беге), наклон при стрейфе, «присед» при приземлении,
    /// тряска от выстрела (по калибру), попаданий и взрывов (по расстоянию). В прицеливании эффекты гасятся.
    /// Камера — дочерний объект CameraPivot, поэтому обзор/отдача PlayerController не затрагиваются.
    /// </summary>
    public class CameraEffects : MonoBehaviour
    {
        public PlayerController player;
        public WeaponController weapon;
        public PlayerHealth health;
        [Range(0, 2)] public float bobStrength = 1f;

        static CameraEffects _active;
        float _phase, _trauma, _land, _roll, _walkBlend;
        bool _wasGrounded = true;
        int _lastAmmo;
        Vector3 _seed;

        void OnEnable()
        {
            _active = this;
            _seed = new Vector3(Random.value * 100, Random.value * 100, Random.value * 100);
            if (weapon) weapon.AmmoChanged += OnAmmo;
            if (health) health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            if (weapon) weapon.AmmoChanged -= OnAmmo;
            if (health) health.Damaged -= OnDamaged;
        }

        void OnAmmo(WeaponController w)
        {
            if (w.Ammo < _lastAmmo && !w.Reloading && w.Current)
                AddTrauma(w.Current.category switch
                {
                    WeaponCategory.SniperRifle => 0.32f, WeaponCategory.MachineGun => 0.12f, WeaponCategory.BattleRifle => 0.18f,
                    WeaponCategory.SubmachineGun => 0.07f, WeaponCategory.Pistol or WeaponCategory.Pistol2 => 0.09f, _ => 0.11f
                } * Mathf.Lerp(1f, 0.5f, w.Ads01));
            _lastAmmo = w.Ammo;
        }

        void OnDamaged(Vector3 dir, float amount) => AddTrauma(Mathf.Clamp(amount / 60f, 0.15f, 0.6f));

        public void AddTrauma(float t) => _trauma = Mathf.Clamp01(_trauma + t);

        /// <summary>Тряска от взрыва для активной камеры игрока.</summary>
        public static void Explosion(Vector3 pos, float radius)
        {
            if (!_active) return;
            float d = Vector3.Distance(pos, _active.transform.position);
            if (d < radius * 6f) _active.AddTrauma(Mathf.Lerp(0.9f, 0f, d / (radius * 6f)));
        }

        // ---- Bodycam ----
        Vector2 _lag, _lagVel;
        float _turnRoll, _stepKick, _stepSide = 1f, _lastStepPhase, _breath;

        void LateUpdate()
        {
            if (!player) return;
            float dt = Time.deltaTime;
            bool bodycam = SaveSystem.Settings != null && SaveSystem.Settings.bodycam;
            float ads = weapon ? weapon.Ads01 : 0;
            float speed = player.IsGrounded ? player.Speed01 : 0;
            _walkBlend = Mathf.Lerp(_walkBlend, speed, dt * 8f);
            _phase += dt * (player.IsSprinting ? 14f : player.IsCrouched ? 7f : 10f) * Mathf.Max(0.2f, _walkBlend);

            if (!_wasGrounded && player.IsGrounded) _land = 1f;
            _wasGrounded = player.IsGrounded;
            _land = Mathf.MoveTowards(_land, 0, dt * 3.5f);
            float landCurve = Mathf.Sin(_land * Mathf.PI);

            float k = _walkBlend * bobStrength * (1f - ads * 0.85f) * (player.IsSprinting ? 1.6f : 1f);
            Vector3 pos = new(Mathf.Sin(_phase * 0.5f) * 0.035f * k, -Mathf.Abs(Mathf.Cos(_phase * 0.5f)) * 0.05f * k - landCurve * 0.08f, 0);

            _roll = Mathf.Lerp(_roll, -player.Input.move.x * 2.2f * (1 - ads * 0.7f), dt * 6f);
            Vector3 rot = new(landCurve * 2.5f + Mathf.Abs(Mathf.Sin(_phase * 0.5f)) * 0.6f * k,
                              Mathf.Sin(_phase * 0.5f) * 0.5f * k,
                              _roll + Mathf.Sin(_phase * 0.5f) * 0.8f * k);

            if (bodycam) BodycamMotion(dt, ads, k, landCurve, ref pos, ref rot);

            // Тряска (trauma²) — шумом Перлина
            _trauma = Mathf.MoveTowards(_trauma, 0, dt * 1.6f);
            float sh = _trauma * _trauma * (bodycam ? 1.45f : 1f) * (SaveSystem.Settings != null ? SaveSystem.Settings.shake : 1f), t = Time.time * 22f;   // «Тряска экрана» в настройках графики
            rot += new Vector3((Mathf.PerlinNoise(_seed.x, t) - 0.5f) * 6f, (Mathf.PerlinNoise(_seed.y, t) - 0.5f) * 6f, (Mathf.PerlinNoise(_seed.z, t) - 0.5f) * 8f) * sh;
            pos += new Vector3(Mathf.PerlinNoise(_seed.x + 5, t) - 0.5f, Mathf.PerlinNoise(_seed.y + 5, t) - 0.5f, 0) * 0.06f * sh;

            DeathFall(ref pos, ref rot);
            transform.localPosition = pos;
            transform.localRotation = Quaternion.Euler(rot);
        }

        PlayerHealth _hp;
        float _fallSide = 1f;

        /// <summary>
        /// Гибель: камера (голова) оседает и падает на землю с креном набок, в сторону от выстрела, с коротким отскоком;
        /// затем HUD «закрывает глаза». Тряска и покачивание гаснут.
        /// </summary>
        void DeathFall(ref Vector3 pos, ref Vector3 rot)
        {
            if (!_hp) _hp = player.GetComponent<PlayerHealth>();
            if (!_hp || _hp.Alive) return;
            float t = Time.time - _hp.DeathTime;
            if (t < 0.02f)
            {
                var d = player.transform.InverseTransformDirection(_hp.DeathDir);
                _fallSide = d.x > 0 ? -1f : 1f;
            }
            // Сначала колени подгибаются (0–0.35 с), затем падение с ускорением (0.35–0.85 с) и отскок
            float kneel = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 0.35f));
            float fall = Mathf.Clamp01((t - 0.3f) / 0.55f); fall *= fall;
            float bounce = t > 0.85f ? Mathf.Sin(Mathf.Clamp01((t - 0.85f) / 0.25f) * Mathf.PI) * 0.05f : 0f;
            float drop = 0.45f * kneel + 0.95f * fall - bounce;
            pos = new Vector3(_fallSide * 0.35f * fall, -drop, 0.25f * fall);
            rot = new Vector3(-12f * kneel + 25f * fall, 10f * fall * _fallSide, _fallSide * 78f * fall + bounce * 40f);
        }

        /// <summary>
        /// Нагрудная камера: объектив ниже глаз и чуть впереди, инерция корпуса при повороте (камера «догоняет» взгляд),
        /// крен в поворотах, жёсткие толчки на каждом шаге с перекатом с ноги на ногу, дыхание и мелкая дрожь,
        /// тяжёлое приземление. В прицеле почти всё гасится — оружие поднято к глазу.
        /// </summary>
        void BodycamMotion(float dt, float ads, float k, float landCurve, ref Vector3 pos, ref Vector3 rot)
        {
            float free = 1f - ads * 0.8f;
            Vector2 look = player.Input.lookDelta;

            // Инерция: смещение против движения мыши, пружиной возвращается к центру
            Vector2 target = -look * 0.55f;
            _lagVel += (target - _lag) * (dt * 60f) - _lagVel * Mathf.Min(1f, dt * 14f);
            _lag += _lagVel * dt;
            _lag = Vector2.ClampMagnitude(_lag, 3.2f);
            _lag = Vector2.Lerp(_lag, Vector2.zero, dt * 6f);

            // Крен в поворотах
            _turnRoll = Mathf.Lerp(_turnRoll, Mathf.Clamp(-look.x * 0.9f, -5f, 5f), dt * 5f);

            // Шаги: толчок на каждой постановке стопы, перекат с ноги на ногу
            float stepPhase = Mathf.Floor(_phase * 0.5f / Mathf.PI);
            if (stepPhase != _lastStepPhase && _walkBlend > 0.15f) { _stepKick = 1f; _stepSide = -_stepSide; }
            _lastStepPhase = stepPhase;
            _stepKick = Mathf.MoveTowards(_stepKick, 0, dt * 7f);
            float kick = _stepKick * _stepKick * (player.IsSprinting ? 1.8f : 1f) * free;

            _breath += dt * (player.IsSprinting ? 2.4f : 1.1f);
            float breath = Mathf.Sin(_breath) * (player.IsSprinting ? 0.6f : 0.25f) * free;
            float tj = Time.time * 7f;
            Vector3 jitter = new(Mathf.PerlinNoise(_seed.x + 20, tj) - 0.5f, Mathf.PerlinNoise(_seed.y + 20, tj) - 0.5f, Mathf.PerlinNoise(_seed.z + 20, tj) - 0.5f);

            pos += new Vector3(0, -0.09f * (1 - ads), 0.07f * (1 - ads));                      // объектив на груди
            pos += new Vector3(_stepSide * 0.012f * kick, -0.035f * kick - landCurve * 0.06f, 0);
            pos *= 1f;
            // Камера жёстко закреплена на теле: никакого запаздывания при повороте — только толчки шагов и дыхание
            rot += new Vector3(kick * 1.6f + breath + landCurve * 3f,
                               _stepSide * kick * 0.6f,
                               _stepSide * kick * 1.4f + Mathf.Sin(_phase * 0.5f) * 1.2f * k);
            rot += jitter * (0.45f + _walkBlend * 0.9f) * free;
        }
    }

}
