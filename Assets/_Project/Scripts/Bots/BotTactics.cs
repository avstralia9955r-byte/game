using System.Collections.Generic;
using Frontline.Bots;
using Unity.Entities;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Тактика ботов, которой нужен главный поток (физические гранаты, снаряды):
    /// • гранаты — по цели, спрятавшейся за укрытием (видели &lt; 4 с назад) на 9–30 м; не кидают, если рядом свои;
    /// • противотанковая борьба — немцы стреляют фаустпатронами, советские бойцы — из ПТРД и бросают РПГ-40 вблизи.
    /// Частота зависит от сложности.
    /// </summary>
    public class BotTactics : MonoBehaviour
    {
        public Grenade grenadePrefab;
        float _nextGrenadeScan, _nextAtScan;
        readonly Dictionary<int, float> _grenadeCd = new(), _atCd = new();
        readonly HashSet<int> _faustUsed = new();    // фаустпатрон у бойца один — до гибели
        float _faustClean;
        readonly float[] _teamGrenadeNext = new float[2];
        readonly List<int> _cands = new();
        readonly RaycastHit[] _hits = new RaycastHit[12];

        static float DiffMul => MatchBridge.Difficulty switch
        {
            BotDifficulty.Easy => 0.4f, BotDifficulty.Medium => 0.75f, BotDifficulty.Hard => 1f, _ => 1.25f
        };
        static float AtErrDeg => MatchBridge.Difficulty switch
        {
            BotDifficulty.Easy => 3.5f, BotDifficulty.Medium => 2.2f, BotDifficulty.Hard => 1.3f, _ => 0.9f
        };

        void Update()
        {
            if (Net.NetSession.IsClient) return;   // гранаты и противотанковое ботов — на хосте
            if (!MatchBridge.MatchRunning || !CombatWorld.Initialized || !BotRegistry.ActorToEntity.IsCreated) return;
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return;
            if (Time.time >= _nextGrenadeScan) { _nextGrenadeScan = Time.time + 0.35f; TryGrenade(w); }
            if (Time.time >= _nextAtScan) { _nextAtScan = Time.time + 1f; TryAntiTank(w.EntityManager); }
        }

        bool AliveBot(EntityManager em, int id, out Entity e)
        {
            e = Entity.Null;
            if (id <= 0 || id >= BotRegistry.ActorToEntity.Length || id >= CombatWorld.Actors.Length) return false;
            e = BotRegistry.ActorToEntity[id];
            if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<BotPerception>(e)) return false;
            if (em.HasComponent<BotDead>(e) && em.IsComponentEnabled<BotDead>(e)) return false;
            return CombatWorld.Actors[id].alive;
        }

        // ---------------- Гранаты ----------------

        void TryGrenade(World w)
        {
            if (!grenadePrefab) return;
            var em = w.EntityManager;
            int n = Mathf.Min(BotRegistry.BotCount, CombatWorld.Actors.Length - 1);
            if (n <= 0) return;
            float now = (float)w.Time.ElapsedTime;
            var actors = CombatWorld.Actors;
            for (int k = 0; k < 4; k++)
            {
                int id = 1 + Random.Range(0, n);
                if (!AliveBot(em, id, out var e)) continue;
                var p = em.GetComponentData<BotPerception>(e);
                if (p.targetActorId < 0) continue;
                byte team = actors[id].team;
                if (Time.time < _teamGrenadeNext[team]) return;
                if (_grenadeCd.TryGetValue(id, out var cd) && Time.time < cd) continue;

                Vector3 from = (Vector3)actors[id].position + Vector3.up * 1.6f;
                Vector3 target = p.targetPos;
                Vector3 flat = target - from; flat.y = 0;
                float d = flat.magnitude;
                if (d < 9f || d > 30f) continue;
                bool hidden = !p.targetVisible && now - p.lastSeenTime < 4f;
                float chance = (hidden ? 0.4f : 0.08f) * DiffMul;
                if (Random.value > chance) { _grenadeCd[id] = Time.time + 2.5f; continue; }
                // Свои рядом с целью — не бросаем
                bool friends = false;
                for (int i = 0; i < actors.Length; i++)
                    if (actors[i].alive && actors[i].team == team && Vector3.Distance(actors[i].position, target) < 7f) { friends = true; break; }
                if (friends) continue;
                // Стена прямо перед лицом — не бросаем
                if (Physics.Raycast(from, flat / d, 2.5f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore)) continue;

                float err = MatchBridge.Difficulty switch { BotDifficulty.Easy => 4f, BotDifficulty.Medium => 2.5f, BotDifficulty.Hard => 1.6f, _ => 1f };
                Vector2 rnd = Random.insideUnitCircle * err;
                Throw(from, target + new Vector3(rnd.x, 0.3f, rnd.y), id, team, false);
                _grenadeCd[id] = Time.time + Random.Range(18f, 32f);
                _teamGrenadeNext[team] = Time.time + 2.2f;
                return;
            }
        }

        void Throw(Vector3 from, Vector3 target, int id, byte team, bool antiTank)
        {
            Vector3 delta = target - from;
            float d = new Vector2(delta.x, delta.z).magnitude;
            float T = Mathf.Clamp(d / 13f, 0.8f, 2.3f);
            float g = Physics.gravity.y;
            Vector3 v = new(delta.x / T, delta.y / T - 0.5f * g * T, delta.z / T);
            var gr = Instantiate(grenadePrefab, from + delta.normalized * 0.4f, Random.rotation);
            gr.fuse = T + (antiTank ? 0.05f : Random.Range(0.5f, 1.1f));
            if (antiTank) { gr.armorDamage = 340f; gr.radius = 4f; gr.damage = 120f; }
            gr.Launch(v, id, team);
        }

        // ---------------- Против танков ----------------

        void TryAntiTank(EntityManager em)
        {
            var actors = CombatWorld.Actors;
            if (Time.time >= _faustClean)
            {
                _faustClean = Time.time + 2f;
                _faustUsed.RemoveWhere(id => id >= actors.Length || !actors[id].alive);   // погиб — после возрождения снова с фаустом
            }
            foreach (var tank in Tank.All)
            {
                if (!tank || !tank.Alive) continue;
                _cands.Clear();
                Vector3 tc = tank.Center;
                for (int i = 1; i < actors.Length; i++)
                {
                    var a = actors[i];
                    if (!a.alive || a.team == tank.Team) continue;
                    float d = Vector3.Distance(a.position, tc);
                    if (d < 12f || d > 140f) continue;
                    if (_atCd.TryGetValue(a.actorId, out var cd) && Time.time < cd) continue;
                    if (a.team == (byte)Faction.Germany && _faustUsed.Contains(a.actorId)) continue;
                    _cands.Add(a.actorId);
                    if (_cands.Count >= 8) break;
                }
                if (_cands.Count == 0) continue;
                float p = 0.09f * DiffMul * Mathf.Min(3, _cands.Count) * (tank.Team == (byte)Faction.Germany ? 1.6f : 1f);
                if (Random.value > p) continue;

                // Противотанковое — у поддержки: они в приоритете
                int pick = _cands[Random.Range(0, _cands.Count)];
                foreach (var c in _cands)
                    if (AliveBot(em, c, out var ce) && em.HasComponent<BotIdentity>(ce) && em.GetComponentData<BotIdentity>(ce).soldierClass == SoldierClass.Support) { pick = c; break; }
                if (!AliveBot(em, pick, out _)) continue;

                Vector3 from = (Vector3)actors[pick].position + Vector3.up * 1.5f;
                if (!Visible(from, tc, tank)) { _atCd[pick] = Time.time + 2f; continue; }
                byte team = actors[pick].team;
                _atCd[pick] = Time.time + Random.Range(16f, 26f);
                float dist = Vector3.Distance(from, tc);

                if (team == (byte)Faction.USSR && dist < 26f && grenadePrefab)
                {
                    Throw(from, tc + Vector3.up * 0.8f, pick, team, true);       // РПГ-40
                    continue;
                }
                float err = AtErrDeg;
                if (team == (byte)Faction.Germany) { FireFaust(from, tank, pick, team, err); _faustUsed.Add(pick); }
                else FirePtrd(from, tank, pick, team, err);
            }
        }

        bool Visible(Vector3 from, Vector3 to, Tank target)
        {
            Vector3 d = to - from; float len = d.magnitude;
            int n = Physics.RaycastNonAlloc(from, d / len, _hits, len, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(target.transform) || _hits[i].collider.attachedRigidbody) continue;
                if (_hits[i].distance < len - 1f) return false;
            }
            return true;
        }

        void FireFaust(Vector3 from, Tank tank, int id, byte team, float errDeg)
        {
            const float speed = 62f, grav = 0.45f;
            Vector3 aim = tank.Center + Vector3.up * 0.3f;
            float t = Vector3.Distance(from, aim) / speed;
            aim += tank.transform.forward * (tank.Speed * t);
            aim += Vector3.up * (0.5f * -Physics.gravity.y * grav * t * t);
            aim += Random.insideUnitSphere * (Vector3.Distance(from, aim) * Mathf.Tan(errDeg * Mathf.Deg2Rad));
            Vector3 dir = (aim - from).normalized;
            Projectile.Fire(Projectile.Type.Rocket, from + dir * 0.7f, dir, speed, grav, id, team, 4.5f, 160f, 1600f, 320f, null);
            BotAudio.ShotAt(SoundSynth.RocketLaunch(), from, 1f);
            if (FxPool.I) { FxPool.I.Smoke(from - dir * 1.5f); FxPool.I.MuzzleFlashAt(from - dir * 1f, -dir); }
            CombatWorld.EmitNoise(from, 220f, id, team, 0);
        }

        void FirePtrd(Vector3 from, Tank tank, int id, byte team, float errDeg)
        {
            // Короткая серия из 2–3 выстрелов по одной цели
            StartCoroutine(PtrdBurst(from, tank, id, team, errDeg, Random.Range(2, 4)));
        }

        System.Collections.IEnumerator PtrdBurst(Vector3 from, Tank tank, int id, byte team, float errDeg, int shots)
        {
            for (int s = 0; s < shots; s++)
            {
                if (!tank || !tank.Alive || id >= CombatWorld.Actors.Length || !CombatWorld.Actors[id].alive) yield break;
                Vector3 aim = tank.Center + Random.insideUnitSphere * (Vector3.Distance(from, tank.Center) * Mathf.Tan(errDeg * Mathf.Deg2Rad));
                Vector3 dir = (aim - from).normalized;
                Vector3 end = from + dir * 300f;
                int n = Physics.RaycastNonAlloc(from + dir * 0.5f, dir, _hits, 300f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
                float best = 300f; RaycastHit hit = default; bool any = false;
                for (int i = 0; i < n; i++) if (!_hits[i].collider.attachedRigidbody && _hits[i].distance < best) { best = _hits[i].distance; hit = _hits[i]; any = true; }
                if (any)
                {
                    end = hit.point;
                    var t = hit.collider.GetComponentInParent<Tank>();
                    if (t && t.Team != team) { t.TakeDamage(140f, id, hit.point, true); BotAudio.PlayAt(SoundSynth.ArmorHit(), hit.point, 0.8f); }
                    else if (FxPool.I) FxPool.I.Impact(hit.point, hit.normal, MatchSession.Current.map ? MatchSession.Current.map.theme : MapTheme.Summer);
                }
                if (FxPool.I) { FxPool.I.Tracer(from, end, 1000f); FxPool.I.MuzzleFlashAt(from + dir * 1.2f, dir); }
                BotAudio.ShotAt(SoundSynth.AntiTankRifle(), from, 1f);
                CombatWorld.EmitNoise(from, 280f, id, team, 0);
                yield return new WaitForSeconds(Random.Range(2.2f, 3.2f));
                if (id < CombatWorld.Actors.Length) from = (Vector3)CombatWorld.Actors[id].position + Vector3.up * 0.9f;   // стреляет лёжа/с колена
            }
        }
    }

    /// <summary>Имена бойцов для ленты убийств (детерминированно по actorId).</summary>
    public static class BotNames
    {
        static readonly string[] De =
        {
            "Шмидт", "Мюллер", "Шнайдер", "Фишер", "Вебер", "Майер", "Вагнер", "Беккер", "Шульц", "Хоффман",
            "Шефер", "Кох", "Бауэр", "Рихтер", "Кляйн", "Вольф", "Шрёдер", "Нойман", "Шварц", "Циммерман",
            "Браун", "Крюгер", "Хартман", "Ланге", "Вернер", "Кёлер", "Майер", "Кёниг", "Вальтер", "Хубер"
        };
        static readonly string[] Su =
        {
            "Иванов", "Смирнов", "Кузнецов", "Попов", "Васильев", "Петров", "Соколов", "Михайлов", "Новиков", "Фёдоров",
            "Морозов", "Волков", "Алексеев", "Лебедев", "Семёнов", "Егоров", "Павлов", "Козлов", "Степанов", "Николаев",
            "Орлов", "Андреев", "Макаров", "Никитин", "Захаров", "Зайцев", "Соловьёв", "Борисов", "Яковлев", "Григорьев"
        };
        static readonly string[] RankDe = { "Шютце", "Обершютце", "Гефрайтер", "Обергефрайтер", "Унтерофицер", "Фельдфебель" };
        static readonly string[] RankSu = { "ряд.", "ряд.", "ефр.", "мл. серж.", "серж.", "ст. серж." };

        public static string Get(int actorId, byte team)
        {
            if (actorId == CombatWorld.PlayerActorId) return "Вы";
            var netName = Net.NetSession.NameOf(actorId);
            if (netName != null) return netName;
            if (actorId <= -10) return team == 0 ? "экипаж Pz.IV" : "экипаж Т-34";
            if (actorId < 0) return "—";
            uint h = (uint)actorId * 2654435761u;
            var names = team == 0 ? De : Su; var ranks = team == 0 ? RankDe : RankSu;
            return $"{ranks[(h >> 8) % ranks.Length]} {names[h % names.Length]}";
        }
    }
}
