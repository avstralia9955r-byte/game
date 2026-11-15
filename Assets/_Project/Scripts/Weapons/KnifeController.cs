using System.Collections;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Штык-нож. Всё анимируется процедурно:
    /// • F — флип: нож подбрасывается из руки, делает два оборота и ловится (кисть «подыгрывает»);
    /// • V / ЛКМ со слотом ножа — выпад: замах, быстрый укол вперёд, возврат. Удар в спину — повышенный урон.
    /// Пока нож показан (Showing), основное оружие опускается (ViewModelAnimator).
    /// </summary>
    public class KnifeController : MonoBehaviour
    {
        [SerializeField] Transform knifeModel;        // корень: рука + KnifeBody
        [SerializeField] Animator animator;           // необязателен
        [SerializeField] float spinDuration = 0.95f;
        [SerializeField] float stabRange = 1.9f, stabDamageFront = 55f, stabDamageBack = 120f, stabCooldown = 0.7f;
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip spinSound, stabSound, stabHitSound;

        public bool Busy { get; private set; }
        public bool Showing { get; private set; }
        float _nextStab;
        Transform _body;
        Vector3 _basePos, _bodyPos; Quaternion _baseRot, _bodyRot = Quaternion.identity; bool _cached;
        Coroutine _routine;

        void Cache()
        {
            if (_cached || !knifeModel) return;
            _cached = true;
            _basePos = knifeModel.localPosition; _baseRot = knifeModel.localRotation;
            _body = knifeModel.Find("KnifeBody");
            if (_body) { _bodyPos = _body.localPosition; _bodyRot = _body.localRotation; }   // обратный хват задан в модели
        }

        void Begin()
        {
            Cache();
            if (_routine != null) StopCoroutine(_routine);
            if (knifeModel)
            {
                knifeModel.gameObject.SetActive(true);
                knifeModel.localPosition = _basePos; knifeModel.localRotation = _baseRot;
                if (_body) { _body.localPosition = _bodyPos; _body.localRotation = _bodyRot; }
            }
            Busy = true; Showing = true;
        }

        void End()
        {
            if (knifeModel)
            {
                knifeModel.localPosition = _basePos; knifeModel.localRotation = _baseRot;
                if (_body) { _body.localPosition = _bodyPos; _body.localRotation = _bodyRot; }
                knifeModel.gameObject.SetActive(false);
            }
            Busy = false; Showing = false; _routine = null;
        }

        public void Spin()
        {
            if (Busy) return;
            Begin();
            if (animator) animator.SetTrigger("KnifeSpin");
            if (audioSource && spinSound) audioSource.PlayOneShot(spinSound, 0.5f);
            _routine = StartCoroutine(SpinRoutine());
        }

        IEnumerator SpinRoutine()
        {
            // Нож не подбрасывается — крутится в пальцах: 0–0.15 кисть поднимает нож в кадр, 0.15–0.75 два оборота
            // вокруг рукояти (пальцы перехватывают, кисть подкручивает запястьем), 0.75–0.85 перехват обратно
            // в обратный хват с «защёлкиванием», 0.85–1 уход вниз.
            for (float t = 0; t < spinDuration; t += Time.deltaTime)
            {
                float n = t / spinDuration;
                float rise = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 0.15f, n)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.88f, 1f, n)));
                float sp = Mathf.InverseLerp(0.15f, 0.75f, n);
                // Ускорение в начале оборота и торможение пальцами в конце
                float ang = -720f * (sp < 0.5f ? 2f * sp * sp : 1f - Mathf.Pow(-2f * sp + 2f, 2f) / 2f);
                float snap = Bump01(0.75f, 0.8f, 0.86f, n);                       // перехват: короткий кивок кисти
                float roll = Mathf.Sin(sp * Mathf.PI * 2f) * 18f * (sp > 0 && sp < 1 ? 1f : 0f);
                if (knifeModel)
                {
                    knifeModel.localPosition = _basePos + new Vector3(-0.015f * rise, -0.22f * (1 - rise) + 0.04f * rise + 0.008f * Mathf.Sin(ang * Mathf.Deg2Rad), 0.02f * rise);
                    knifeModel.localRotation = _baseRot * Quaternion.Euler(-20f * rise + 14f * snap, -12f * rise, 28f * rise + roll);
                }
                if (_body)
                {
                    _body.localPosition = _bodyPos;
                    _body.localRotation = Quaternion.AngleAxis(ang, Vector3.right) * _bodyRot;   // вращение вокруг оси через рукоять
                }
                yield return null;
            }
            End();
        }

        static float Bump01(float a, float b, float c, float t) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, t)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(b, c, t)));

        public void Stab(PlayerController player)
        {
            if (Time.time < _nextStab) return;
            _nextStab = Time.time + stabCooldown;
            Begin();
            if (animator) animator.SetTrigger("Stab");
            if (audioSource && stabSound) audioSource.PlayOneShot(stabSound);
            _routine = StartCoroutine(StabRoutine(player));
        }

        IEnumerator StabRoutine(PlayerController player)
        {
            const float dur = 0.5f;
            bool hitDone = false;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float n = t / dur;
                float windup = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 0.2f, n)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.2f, 0.3f, n)));
                float thrust = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.2f, 0.32f, n)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.5f, 0.95f, n)));
                float show = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 0.12f, n)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.85f, 1f, n)));
                if (knifeModel)
                {
                    // Обратный хват: кулак взлетает к плечу (замах), затем клинок рубит вперёд-вниз по дуге
                    knifeModel.localPosition = _basePos + new Vector3(-0.12f * thrust + 0.03f * windup, -0.2f * (1 - show) + 0.1f * windup - 0.05f * thrust,
                                                                      -0.08f * windup + 0.3f * thrust);
                    knifeModel.localRotation = _baseRot * Quaternion.Euler(-35f * windup + 60f * thrust, -10f * windup - 22f * thrust, 15f * windup + 20f * thrust);
                }
                if (!hitDone && n >= 0.3f) { hitDone = true; DoHit(player); }
                yield return null;
            }
            End();
        }

        void DoHit(PlayerController player)
        {
            var ray = player.AimRay();
            float worldDist = Physics.Raycast(ray, out var rh, stabRange, CombatWorld.WorldMask) ? rh.distance : stabRange;
            if (CombatWorld.RaycastActors(ray.origin, ray.direction, worldDist, player.Team, CombatWorld.PlayerActorId, out var hit))
            {
                var target = FindActor(hit.actorId);
                Vector3 toTarget = ((Vector3)target.position - player.transform.position).normalized;
                Vector3 tv = target.velocity; tv.y = 0;
                bool back = tv.sqrMagnitude > 0.01f && Vector3.Dot(tv.normalized, toTarget) > 0.5f;
                CombatWorld.Damage.Enqueue(new DamageEvent
                {
                    targetActorId = hit.actorId, sourceActorId = CombatWorld.PlayerActorId,
                    amount = back ? stabDamageBack : stabDamageFront, hitPoint = hit.point, direction = ray.direction
                });
                if (audioSource && stabHitSound) audioSource.PlayOneShot(stabHitSound);
                if (FxPool.I) FxPool.I.Blood(hit.point, ray.direction);
            }
            CombatWorld.EmitNoise(player.transform.position, 4f, CombatWorld.PlayerActorId, player.Team, 0);
        }

        static ActorSnapshot FindActor(int id)
        {
            foreach (var a in CombatWorld.Actors) if (a.actorId == id) return a;
            return default;
        }
    }
}
