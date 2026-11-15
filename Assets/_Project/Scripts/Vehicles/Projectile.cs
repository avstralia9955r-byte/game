using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Снаряд с баллистикой: фаустпатрон (медленная ракета с дымным следом и шипением) и танковый снаряд
    /// (быстрый трассер). Шаг — луч по миру + аналитический тест капсул бойцов; при попадании в танк — прямой
    /// бронебойный урон, затем общий взрыв (Explosions).
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public enum Type : byte { Rocket, Shell }

        Type _type;
        Vector3 _vel;
        float _gravity, _radius, _damage, _armorDirect, _armorSplash, _life;
        int _source; byte _team;
        Transform _ignore;
        readonly RaycastHit[] _hits = new RaycastHit[8];
        static Material _trailMat, _glowMat;

        /// <summary>Время жизни снаряда (артиллерия стреляет по навесной — полёт дольше стандартных 5 с).</summary>
        public float Life { get => _life; set => _life = value; }
        /// <summary>Вид взрыва вместо стандартного (артиллерийский фугас — огромный столб земли и огня).</summary>
        public Explosions.Kind? KindOverride { get; set; }

        public static Projectile Fire(Type type, Vector3 origin, Vector3 dir, float speed, float gravity, int source, byte team,
                                      float radius, float damage, float armorDirect, float armorSplash, Transform ignoreRoot)
        {
            // 5% ракет уходят мимо (отказ стабилизатора, порыв ветра) — у игрока и у ботов
            if (type == Type.Rocket && Random.value < 0.05f)
                dir = Quaternion.AngleAxis(Random.Range(6f, 12f) * (Random.value < 0.5f ? -1f : 1f), Vector3.up) * Quaternion.AngleAxis(Random.Range(-5f, 3f), Vector3.Cross(Vector3.up, dir).normalized) * dir;
            var go = new GameObject(type == Type.Rocket ? "Rocket" : "Shell");
            go.layer = 2;
            go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir));
            var p = go.AddComponent<Projectile>();
            p._type = type; p._vel = dir.normalized * speed; p._gravity = gravity; p._source = source; p._team = team;
            p._radius = radius; p._damage = damage; p._armorDirect = armorDirect; p._armorSplash = armorSplash; p._ignore = ignoreRoot;
            p._life = type == Type.Rocket ? 6f : 5f;
            p.BuildVisual();
            return p;
        }

        static Material Unlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        void BuildVisual()
        {
            var t = transform;
            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.numCapVertices = 2;
            if (_type == Type.Rocket)
            {
                ProceduralModels.Part(t, "Warhead", MeshUtil.Ico(), new Vector3(0, 0, 0.12f), new Vector3(0.15f, 0.15f, 0.28f), new Color(0.3f, 0.32f, 0.22f));
                ProceduralModels.Tube(t, "Tail", new Vector3(0, 0, -0.18f), 0.025f, 0.4f, new Color(0.25f, 0.25f, 0.25f));
                _trailMat ??= Unlit(new Color(0.75f, 0.73f, 0.7f, 0.55f));
                trail.sharedMaterial = _trailMat;
                trail.time = 1.4f; trail.startWidth = 0.22f; trail.endWidth = 0.9f;
                trail.startColor = new Color(1, 1, 1, 0.7f); trail.endColor = new Color(1, 1, 1, 0f);
                var flame = ProceduralModels.Part(t, "Flame", MeshUtil.Ico(), new Vector3(0, 0, -0.42f), new Vector3(0.1f, 0.1f, 0.25f), Color.white);
                _glowMat ??= Unlit(new Color(1f, 0.75f, 0.35f));
                flame.GetComponent<MeshRenderer>().sharedMaterial = _glowMat;
                var src = gameObject.AddComponent<AudioSource>();
                src.clip = SoundSynth.RocketFlight(); src.loop = true; src.spatialBlend = 1f; src.minDistance = 4f; src.maxDistance = 150f;
                src.rolloffMode = AudioRolloffMode.Linear; src.volume = 0.8f; src.dopplerLevel = 1.2f; src.Play();
            }
            else
            {
                var core = ProceduralModels.Part(t, "Tracer", MeshUtil.Ico(), Vector3.zero, new Vector3(0.12f, 0.12f, 1.4f), Color.white);
                _glowMat ??= Unlit(new Color(1f, 0.75f, 0.35f));
                core.GetComponent<MeshRenderer>().sharedMaterial = _glowMat;
                core.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _trailMat ??= Unlit(new Color(0.75f, 0.73f, 0.7f, 0.55f));
                trail.sharedMaterial = _trailMat;
                trail.time = 0.35f; trail.startWidth = 0.12f; trail.endWidth = 0.4f;
                trail.startColor = new Color(1, 1, 1, 0.35f); trail.endColor = new Color(1, 1, 1, 0f);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life -= dt;
            Vector3 pos = transform.position;
            _vel += Physics.gravity * (_gravity * dt);
            Vector3 step = _vel * dt;
            float len = step.magnitude;
            if (len < 1e-5f) return;
            Vector3 dir = step / len;

            // Мир (кроме своего танка)
            float best = len; RaycastHit hit = default; bool world = false;
            int n = Physics.RaycastNonAlloc(pos, dir, _hits, len, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (_ignore && h.collider.transform.IsChildOf(_ignore)) continue;
                if (h.distance < best) { best = h.distance; hit = h; world = true; }
            }
            // Бойцы
            if (CombatWorld.Initialized && CombatWorld.RaycastActors(pos, dir, best, _team, _source, out var ah))
            {
                CombatWorld.Damage.Enqueue(new DamageEvent
                {
                    targetActorId = ah.actorId, sourceActorId = _source, amount = _damage * 1.5f,
                    hitPoint = ah.point, direction = dir, explosive = true, headshot = false
                });
                Detonate((Vector3)ah.point - dir * 0.2f);
                return;
            }
            if (world)
            {
                var gun = hit.collider.GetComponentInParent<ArtilleryGun>();
                if (gun) { gun.TakeDamage(_armorDirect, _source, hit.point); Detonate(hit.point - dir * 0.15f, gun.transform); return; }
                var plane = hit.collider.GetComponentInParent<Aircraft>();
                if (plane)
                {
                    if (plane.Alive) plane.TakeDamage(_armorDirect, _source, hit.point);
                    BotAudio.PlayAt(SoundSynth.ArmorHit(), hit.point, 1f);
                    Detonate(hit.point - dir * 0.15f, plane.transform);
                    return;
                }
                var tank = hit.collider.GetComponentInParent<Tank>();
                // Рикошет от брони: по танку попадают в 95% (игрок со специализацией «Танкист» — 70%), иначе снаряд уходит вверх
                if (tank && tank.Alive && !tank.IsLight && tank.Team != _team && Random.value > tank.HitChance)
                {
                    float sp = _vel.magnitude;
                    Vector3 rv = Vector3.Reflect(_vel, hit.normal); rv.y = Mathf.Abs(rv.y) + sp * 0.5f;
                    _vel = rv.normalized * sp * 0.55f;
                    _ignore = tank.transform;
                    transform.position = hit.point + _vel.normalized * 0.3f;
                    _life = Mathf.Min(_life, 2.5f);
                    BotAudio.PlayAt(SoundSynth.Ricochet(Random.Range(0, 3)), hit.point, 1f);
                    BotAudio.PlayAt(SoundSynth.ArmorHit(), hit.point, 0.7f);
                    if (FxPool.I) { FxPool.I.BrickChips(hit.point, hit.normal); FxPool.I.MuzzleFlashAt(hit.point, _vel.normalized); }
                    if (tank.PlayerAboard || _source == CombatWorld.PlayerActorId) HitFeedback.Ricochet();
                    return;
                }
                if (tank && tank.Alive)
                {
                    tank.TakeDamage(_armorDirect, _source, hit.point, true);
                    BotAudio.PlayAt(SoundSynth.ArmorHit(), hit.point, 1f);
                }
                var fence = hit.collider.GetComponent<Fence>();
                if (fence) { fence.Break(hit.point, _vel * 0.05f); transform.position = hit.point + dir * 0.05f; return; }   // доски не останавливают снаряд
                Detonate(hit.point - dir * 0.15f, tank ? tank.transform : null);
                return;
            }
            transform.position = pos + step;
            transform.rotation = Quaternion.LookRotation(_vel);
            if (_life <= 0 || transform.position.y < -50f) Detonate(transform.position);
        }

        void Detonate(Vector3 at, Transform directTank = null)
        {
            // Прямое попадание в танк уже засчитано — сплэш по нему же не дублируем
            Explosions.Detonate(at, _radius, _damage, _armorSplash, _source, _team,
                KindOverride ?? (_type == Type.Rocket ? Explosions.Kind.Rocket : Explosions.Kind.Shell), false, KindOverride == Explosions.Kind.Artillery ? 600f : 260f, directTank ? directTank : _ignore);
            var trail = GetComponent<TrailRenderer>();
            if (trail) { trail.transform.SetParent(null); trail.emitting = false; trail.autodestruct = true; }
            foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.enabled = false;
            var src = GetComponent<AudioSource>(); if (src) src.Stop();
            enabled = false;
            Destroy(gameObject, trail ? trail.time + 0.1f : 0f);
        }
    }
}
