using System.Collections.Generic;
using Frontline.Bots;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Оружие на земле. Бот при гибели роняет своё оружие, игрок бросает своё на [G].
    /// Подбор — касанием (подойти вплотную): если слот этой категории пуст — оружие берётся целиком с его патронами
    /// и обвесами; если в руках такое же — забираются только патроны. Чужое оружие при занятом слоте не подбирается:
    /// сначала бросьте своё. Лежит 75 с, не больше 40 штук (на телефоне 20) — старые исчезают.
    /// </summary>
    public class WeaponPickups : MonoBehaviour
    {
        class Item
        {
            public GameObject go; public WeaponDefinition def; public List<AttachmentDefinition> atts;
            public int ammo, reserve; public Vector3 vel; public bool resting; public float noPickupUntil, dieAt; public float spin;
        }

        static WeaponPickups _i;
        readonly List<Item> _items = new();
        static int Max => Application.isMobilePlatform ? 20 : 40;
        static readonly RaycastHit[] _hits = new RaycastHit[4];

        static WeaponPickups I
        {
            get
            {
                if (_i) return _i;
                var go = new GameObject("[WeaponPickups]");
                var match = GameObject.Find("[Match]");
                if (match) go.transform.SetParent(match.transform);
                return _i = go.AddComponent<WeaponPickups>();
            }
        }

        public static void Spawn(WeaponDefinition def, List<AttachmentDefinition> atts, int ammo, int reserve, Vector3 pos, Vector3 vel, float noPickup)
        {
            if (!def || def.category == WeaponCategory.Knife) return;
            var me = I;
            while (me._items.Count >= Max) { Destroy(me._items[0].go); me._items.RemoveAt(0); }
            var go = new GameObject("Pickup_" + def.id);
            go.transform.SetParent(me.transform);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), 90f));
            var mf = go.AddComponent<MeshFilter>(); var mr = go.AddComponent<MeshRenderer>();
            if (def.viewModelPrefab && GunBaker.TryBake(def.viewModelPrefab, out var mesh, out var mat)) { mf.sharedMesh = mesh; mr.sharedMaterial = mat; }
            else
            {
                mf.sharedMesh = BotBody.GunMesh(def.faction, ClassOf(def));
                mr.sharedMaterial = ProceduralModels.Mat(ProceduralModels.Wood);
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            me._items.Add(new Item
            {
                go = go, def = def, atts = atts, ammo = ammo, reserve = reserve, vel = vel,
                noPickupUntil = Time.time + noPickup, dieAt = Time.time + 75f, spin = Random.Range(-260f, 260f)
            });
        }

        /// <summary>Оружие погибшего бота: основное его класса, с неполным запасом патронов.</summary>
        public static void BotDrop(Vector3 pos, byte team, SoldierClass cls)
        {
            var db = GameDatabase.Instance; if (!db) return;
            // Оружие роняют все погибшие в 50 м от игрока; от 50 до 200 м — один из пятидесяти; дальше — никто
            var pc = PlayerBridge.Controller;
            if (!pc) return;
            float dist = Vector3.Distance(pc.transform.position, pos);
            if (dist > 200f || (dist > 50f && Random.value > 1f / 50f)) return;
            var lo = db.Arsenal((Faction)team)?.GetClass(cls);
            var w = lo != null ? lo.primary : null;
            if (!w) return;
            var st = WeaponStats.Compute(w, null);
            Spawn(w, new List<AttachmentDefinition>(), Mathf.Max(1, st.magazineSize / 2 + Random.Range(0, st.magazineSize / 2)),
                  Mathf.RoundToInt(st.reserveAmmo * Random.Range(0.25f, 0.6f)), pos + Vector3.up * 1.1f,
                  new Vector3(Random.Range(-1.5f, 1.5f), 1.5f, Random.Range(-1.5f, 1.5f)), 0.4f);
        }

        static SoldierClass ClassOf(WeaponDefinition w) => w.category switch
        {
            WeaponCategory.SubmachineGun => SoldierClass.Engineer, WeaponCategory.MachineGun => SoldierClass.Support,
            WeaponCategory.SniperRifle => SoldierClass.Recon, _ => SoldierClass.Assault
        };

        /// <summary>Ближайшее оружие рядом с игроком, которое нельзя взять (слот занят) — для подсказки.</summary>
        public static WeaponDefinition BlockedNear(Vector3 p, WeaponController wc)
        {
            if (!_i || !wc) return null;
            foreach (var it in _i._items)
            {
                if (!it.resting || (it.go.transform.position - p).sqrMagnitude > 4f) continue;
                var cur = wc.SlotWeapon(WeaponController.SlotFor(it.def));
                if (cur && cur != it.def) return it.def;
            }
            return null;
        }

        void Update()
        {
            float dt = Time.deltaTime, now = Time.time;
            var pc = PlayerBridge.Controller;
            var wc = pc ? pc.GetComponent<WeaponController>() : null;
            bool canTake = pc && wc && !pc.Vehicle && !pc.Flying && !pc.Gunning && PlayerBridge.Health && PlayerBridge.Health.Alive;
            Vector3 pp = pc ? pc.transform.position : Vector3.zero;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (!it.go || now > it.dieAt) { if (it.go) Destroy(it.go); _items.RemoveAt(i); continue; }
                var t = it.go.transform;
                if (!it.resting)
                {
                    // Падение: простая баллистика до первой поверхности
                    it.vel += Physics.gravity * dt;
                    Vector3 step = it.vel * dt;
                    int n = Physics.RaycastNonAlloc(t.position, step.normalized, _hits, step.magnitude + 0.08f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
                    if (n > 0)
                    {
                        var h = _hits[0]; for (int k = 1; k < n; k++) if (_hits[k].distance < h.distance) h = _hits[k];
                        t.position = h.point + h.normal * 0.04f;
                        t.rotation = Quaternion.FromToRotation(t.rotation * Vector3.right, h.normal) * t.rotation;   // ложится плашмя
                        it.resting = true;
                        BotAudio.PlayAt(SoundSynth.Impact(SoundSynth.ImpactKind.Metal, 0), t.position, 0.35f);
                    }
                    else
                    {
                        t.position += step;
                        t.Rotate(0, it.spin * dt, 0, Space.World);
                        var ter = Terrain.activeTerrain;
                        if (ter) { float g = ter.SampleHeight(t.position) + ter.transform.position.y; if (t.position.y < g + 0.04f) { t.position = new Vector3(t.position.x, g + 0.04f, t.position.z); it.resting = true; } }
                    }
                }
                if (!canTake || now < it.noPickupUntil) continue;
                Vector3 d = t.position - pp;
                if (d.y < -0.6f || d.y > 1.8f || d.x * d.x + d.z * d.z > 0.85f * 0.85f) continue;   // касание капсулой игрока
                if (wc.TryPickup(it.def, it.atts, it.ammo, it.reserve)) { Destroy(it.go); _items.RemoveAt(i); }
            }
        }
    }
}
