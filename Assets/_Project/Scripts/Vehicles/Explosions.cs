using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Общий взрыв: урон бойцам (с проверкой укрытия), технике, кирпичным стенам, разлёт досок заборов,
    /// толчок физических обломков, эффекты, тряска камеры, звон в ушах, шум для слуха ботов.
    /// </summary>
    public static class Explosions
    {
        public enum Kind : byte { Grenade, Shell, Rocket, TankWreck, Artillery }

        static readonly Collider[] _overlap = new Collider[64];

        public static void Detonate(Vector3 c, float radius, float damage, float armorDamage, int source, byte team, Kind kind,
                                    bool friendlyFire = false, float brickDamage = 160f, Transform ignoreRoot = null)
        {
            // Бойцы
            if (CombatWorld.Initialized)
            {
                var actors = CombatWorld.Actors;
                for (int i = 0; i < actors.Length; i++)
                {
                    var a = actors[i];
                    if (!a.alive) continue;
                    if (!friendlyFire && a.team == team && a.actorId != source) continue;
                    Vector3 chest = (Vector3)a.position + Vector3.up * (a.crouched ? 0.8f : 1.1f);
                    float d = Vector3.Distance(c, chest);
                    if (d > radius) continue;
                    if (Tank.IsArmoredCrew(a.actorId)) continue;          // экипаж танка под бронёй
                    if (Aircraft.IsAirCrew(a.actorId)) continue;          // лётчик — урон получает самолёт (ниже)
                    if (Physics.Linecast(c, chest, out var lh, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore) && (!ignoreRoot || !lh.collider.transform.IsChildOf(ignoreRoot))) continue;
                    float k = 1f - d / radius;
                    CombatWorld.Damage.Enqueue(new DamageEvent
                    {
                        targetActorId = a.actorId, sourceActorId = source, amount = damage * k * k,
                        hitPoint = chest, direction = (chest - c).normalized, explosive = true
                    });
                }
            }

            // Техника: урон по ближайшей точке корпуса
            foreach (var t in Tank.All)
            {
                if (!t || !t.Alive || (ignoreRoot && t.transform == ignoreRoot)) continue;
                // Джип рвётся и от гранаты (урон по людям), танк — только от бронебойного
                float ad = t.IsLight ? Mathf.Max(armorDamage, damage * 1.6f) : armorDamage;
                if (ad <= 0) continue;
                float d = t.DistanceTo(c);
                if (d > radius) continue;
                if (!t.IsLight && (kind == Kind.Artillery || kind == Kind.Rocket) && Random.value > t.HitChance) continue;   // фугас/ракета «не взяли» броню
                t.TakeDamage(ad * (1f - d / radius * 0.7f), source, c);
            }

            // Самолёты: осколки и ударная волна (зенитный эффект ракет и снарядов)
            foreach (var p in Aircraft.All)
            {
                if (!p || !p.Alive || (ignoreRoot && p.transform == ignoreRoot)) continue;
                float d = Mathf.Max(0f, Vector3.Distance(p.transform.position, c) - 3f);
                if (d > radius) continue;
                p.TakeDamage(Mathf.Max(armorDamage, damage * 1.2f) * (1f - d / radius * 0.7f), source, c);
            }

            // Артиллерийские орудия: щит держит пули, но не снаряды и ракеты
            foreach (var g in ArtilleryGun.All)
            {
                if (!g || !g.Alive || (ignoreRoot && g.transform == ignoreRoot)) continue;
                float d = Mathf.Max(0f, Vector3.Distance(g.transform.position + Vector3.up, c) - 1.5f);
                if (d > radius) continue;
                float ad = Mathf.Max(armorDamage, damage * 0.8f);
                if (ad > 0) g.TakeDamage(ad * (1f - d / radius * 0.7f), source, c);
            }

                        if (BrickSystem.I && brickDamage > 0) BrickSystem.I.Explosion(c, radius * 0.7f, brickDamage);

            // Заборы и обломки
            int n = Physics.OverlapSphereNonAlloc(c, radius * 0.9f, _overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = _overlap[i];
                var fence = col.GetComponent<Fence>();
                if (fence) fence.Break(c, Vector3.zero);
                var rb = col.attachedRigidbody;
                if (rb && !rb.isKinematic) rb.AddExplosionForce(kind == Kind.Grenade ? 350f : 900f, c, radius * 1.3f, 0.6f);
            }

            VisualOnly(c, radius, kind);
            CombatWorld.EmitNoise(c, kind == Kind.Grenade ? 250f : 380f, source, team, 1);
            // Сеть: хост показывает взрыв клиентам, клиент — хосту (урон уходит отдельно, событиями урона)
            if (Net.NetSession.IsHost) Net.NetDriver.RecordExplosion(c, radius, kind);
            else if (Net.NetSession.IsClient && source == CombatWorld.PlayerActorId) Net.NetDriver.SendExplosion(c, radius, kind);
        }

        /// <summary>Только эффекты, звук, тряска и звон — без урона (взрыв, пришедший по сети).</summary>
        public static void VisualOnly(Vector3 c, float radius, Kind kind)
        {
            if (FxPool.I)
            {
                FxPool.I.Explosion(c);
                if (kind != Kind.Grenade)
                {
                    FxPool.I.Explosion(c + Random.insideUnitSphere * 1.2f + Vector3.up * 0.6f);
                    FxPool.I.Smoke(c + Vector3.up * 1.5f);
                }
            }
            if (kind == Kind.Artillery)
            {
                ArtilleryFx.Play(c);
                BotAudio.PlayAt(SoundSynth.ShellExplosion(), c, 1.5f, 0.04f);
            }
            CameraEffects.Explosion(c, kind == Kind.Artillery ? radius * 1.6f : radius);
            var clip = kind == Kind.Grenade ? SoundSynth.Explosion() : SoundSynth.ShellExplosion();
            BotAudio.PlayAt(clip, c, kind == Kind.Grenade ? 1f : 1.15f, 0.06f);
            Tinnitus(c, radius);
        }

        /// <summary>Близкий разрыв — звон в ушах и приглушённый мир на пару секунд.</summary>
        static void Tinnitus(Vector3 c, float radius)
        {
            var pc = Bots.PlayerBridge.Controller;
            if (!pc || pc.Vehicle || pc.Flying) return;
            float d = Vector3.Distance(c, pc.transform.position + Vector3.up);
            if (d > radius * 1.3f) return;
            var src = pc.GetComponent<AudioSource>();
            if (src) src.PlayOneShot(SoundSynth.Tinnitus(), Mathf.Lerp(0.8f, 0.2f, d / (radius * 1.3f)));
            AudioDuck.Duck(Mathf.Lerp(2.4f, 0.8f, d / (radius * 1.3f)));
        }
    }
}
