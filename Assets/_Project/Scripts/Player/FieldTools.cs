using System.Collections.Generic;
using Frontline.Bots;
using Unity.Entities;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Мешки с песком: не останавливают пули, но всё, что прошло сквозь них, теряет ¾ урона.
    /// Коллайдер — на слое Ignore Raycast: игрок упирается в мешки, а лучи пуль идут насквозь (урон режет Factor).
    /// </summary>
    public class Sandbag : MonoBehaviour
    {
        public static readonly List<Sandbag> All = new();
        Bounds _local;

        public static Sandbag Build(Vector3 pos, float yaw, MapTheme theme)
        {
            var go = new GameObject("Sandbags");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var sb = go.AddComponent<Sandbag>();
            Color c = theme == MapTheme.Winter ? new Color(0.72f, 0.72f, 0.66f) : new Color(0.55f, 0.48f, 0.33f);
            for (int row = 0; row < 3; row++)
                for (int k = 0; k < 4 - (row == 2 ? 1 : 0); k++)
                {
                    float x = (k - (row == 2 ? 1f : 1.5f)) * 0.52f + (row == 1 ? 0.12f : 0f);
                    ProceduralModels.Part(go.transform, "Bag", MeshUtil.Bevel(0.12f), new Vector3(x, 0.15f + row * 0.26f, 0), new Vector3(0.5f, 0.27f, 0.42f),
                        Color.Lerp(c, Color.black, (k + row) % 2 * 0.08f), new Vector3(0, (k * 7) % 11 - 5, 0), false, 0.05f, 0f);
                }
            var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.42f, 0); bc.size = new Vector3(2.1f, 0.84f, 0.5f);
            go.layer = 2;                                       // Ignore Raycast: пули проходят, игрок — нет
            foreach (Transform t in go.transform) t.gameObject.layer = 2;
            sb._local = new Bounds(bc.center, bc.size);
            All.Add(sb);
            return sb;
        }

        void OnDestroy() => All.Remove(this);

        bool Crosses(Vector3 a, Vector3 b)
        {
            Vector3 la = transform.InverseTransformPoint(a), lb = transform.InverseTransformPoint(b);
            Vector3 d = lb - la; float len = d.magnitude;
            if (len < 1e-3f) return false;
            return _local.IntersectRay(new Ray(la, d / len), out float t) && t <= len;
        }

        /// <summary>Множитель урона для выстрела from→to: 0,25, если на пути есть мешки, иначе 1.</summary>
        public static float Factor(Vector3 from, Vector3 to)
        {
            if (All.Count == 0) return 1f;
            foreach (var s in All) if (s && s.Crosses(from, to)) return 0.25f;
            return 1f;
        }
    }

    /// <summary>
    /// Инструмент 4-го слота. Штурмовик — аптечка: держите ЛКМ рядом с тяжелораненым (своим бойцом, игроком
    /// или собой) 2 с — +50 HP. Инженер — 2 мешка с песком: держите ЛКМ 3 с — мешки ложатся перед вами.
    /// Пополнение — у ящика с боеприпасами.
    /// </summary>
    public class FieldTools : MonoBehaviour
    {
        public const float DownedHp = 10f;
        public ToolKind Kind { get; private set; }
        public bool Active => Kind != ToolKind.None;
        public int Charges { get; private set; }
        public float Progress01 => _hold / HoldTime;
        public string Status { get; private set; }
        float HoldTime => Kind == ToolKind.Medkit ? 2f : 3f;

        PlayerController _pc;
        PlayerHealth _hp;
        Transform _vm, _ghost;
        float _hold;
        int _medkits = 3, _bags = 2;

        void Awake() { _pc = GetComponent<PlayerController>(); _hp = GetComponent<PlayerHealth>(); }

        public void Refill() { _medkits = 3; _bags = 2; }
        public void ResetForSpawn() => Refill();

        public void SetActive(ToolKind kind)
        {
            Kind = kind; _hold = 0;
            if (_vm) Destroy(_vm.gameObject);
            if (_ghost) _ghost.gameObject.SetActive(false);
            if (kind == ToolKind.None || !_pc || !_pc.cameraPivot) return;
            _vm = new GameObject("ToolVM").transform;
            _vm.SetParent(_pc.cameraPivot, false);
            _vm.localPosition = new Vector3(0.22f, -0.24f, 0.45f);
            _vm.localRotation = Quaternion.Euler(10, -15, 0);
            if (kind == ToolKind.Medkit)
            {
                ProceduralModels.Part(_vm, "Box", MeshUtil.Bevel(0.02f), Vector3.zero, new Vector3(0.22f, 0.15f, 0.08f), new Color(0.85f, 0.85f, 0.8f), default, false, 0.3f, 0f);
                ProceduralModels.Part(_vm, "CrossV", MeshUtil.Bevel(0.005f), new Vector3(0, 0, -0.042f), new Vector3(0.04f, 0.11f, 0.01f), new Color(0.8f, 0.08f, 0.06f), default, false, 0.3f, 0f);
                ProceduralModels.Part(_vm, "CrossH", MeshUtil.Bevel(0.005f), new Vector3(0, 0, -0.042f), new Vector3(0.11f, 0.04f, 0.01f), new Color(0.8f, 0.08f, 0.06f), default, false, 0.3f, 0f);
            }
            else ProceduralModels.Part(_vm, "Bag", MeshUtil.Bevel(0.08f), Vector3.zero, new Vector3(0.32f, 0.18f, 0.24f), new Color(0.55f, 0.48f, 0.33f), default, false, 0.05f, 0f);
            foreach (var t in _vm.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            foreach (var r in _vm.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Update()
        {
            if (!Active || !_pc) { Status = null; return; }
            Charges = Kind == ToolKind.Medkit ? _medkits : _bags;
            var inp = _pc.Input;
            bool held = inp.fireHeld && _pc.InputEnabled && (!_hp || _hp.Alive);
            if (_vm) _vm.localPosition = new Vector3(0.22f, -0.24f + Mathf.Sin(_hold * 12f) * 0.01f, 0.45f - (held ? 0.06f : 0f));
            if (Kind == ToolKind.Medkit) MedkitUpdate(held);
            else SandbagUpdate(held);
        }

        // ---------------- Аптечка ----------------

        int _healTarget = -2;           // -1 — себя, >0 — actorId бота

        void MedkitUpdate(bool held)
        {
            if (_medkits <= 0) { Status = "Аптечки кончились — пополните у ящика с боеприпасами"; _hold = 0; return; }
            int target = FindDowned();
            if (target == -2) { Status = "Подойдите к тяжелораненому (лежит) — или лечите себя, когда ранены"; _hold = 0; return; }
            Status = target == -1 ? "Держите ЛКМ — перевязать себя" : "Держите ЛКМ — поднять раненого";
            if (target != _healTarget) { _hold = 0; _healTarget = target; }
            if (!held) { _hold = Mathf.Max(0, _hold - Time.deltaTime * 2f); return; }
            _hold += Time.deltaTime;
            if (_hold < HoldTime) return;
            _hold = 0; _medkits--;
            if (target == -1) { if (_hp) _hp.Heal(50f); }
            else HealBot(target, 50f);
            BotAudio.PlayAt(SoundSynth.AmmoPickup(), transform.position + Vector3.up, 0.6f);
            HitFeedback.Report(CombatWorld.PlayerActorId, false);
        }

        /// <summary>Ближайший лежащий свой боец в 2,5 м перед игроком; иначе сам игрок, если ранен тяжело; иначе −2.</summary>
        int FindDowned()
        {
            var actors = CombatWorld.Actors;
            var w = World.DefaultGameObjectInjectionWorld;
            Vector3 me = transform.position;
            int best = -2; float bd = 2.6f;
            if (actors.IsCreated && w != null && w.IsCreated)
            {
                var em = w.EntityManager;
                for (int i = 1; i < actors.Length; i++)
                {
                    var a = actors[i];
                    if (!a.alive || a.team != _pc.Team) continue;
                    float d = Vector3.Distance(a.position, me);
                    if (d >= bd) continue;
                    if (a.actorId >= BotRegistry.ActorToEntity.Length) continue;
                    var e = BotRegistry.ActorToEntity[a.actorId];
                    if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<BotHealth>(e)) continue;
                    var h = em.GetComponentData<BotHealth>(e);
                    if (h.value <= 0 || h.value >= DownedHp) continue;
                    bd = d; best = a.actorId;
                }
            }
            if (best == -2 && _hp && _hp.Downed) best = -1;
            return best;
        }

        public static void HealBot(int actorId, float amount)
        {
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated || actorId <= 0 || actorId >= BotRegistry.ActorToEntity.Length) return;
            var em = w.EntityManager; var e = BotRegistry.ActorToEntity[actorId];
            if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<BotHealth>(e)) return;
            var h = em.GetComponentData<BotHealth>(e);
            if (h.value <= 0) return;
            h.value = Mathf.Min(h.max, h.value + amount);
            em.SetComponentData(e, h);
        }

        // ---------------- Мешки с песком ----------------

        void SandbagUpdate(bool held)
        {
            if (_bags <= 0) { Status = "Мешки кончились — пополните у ящика с боеприпасами"; _hold = 0; ShowGhost(false, default, 0, false); return; }
            bool ok = Place(out var pos, out float yaw);
            ShowGhost(true, pos, yaw, ok);
            Status = ok ? $"Держите ЛКМ — уложить мешки ({_bags})" : "Здесь не уложить — нужна ровная земля перед вами";
            if (!held || !ok || !_pc.IsGrounded) { _hold = Mathf.Max(0, _hold - Time.deltaTime * 2f); return; }
            _hold += Time.deltaTime;
            if (_hold < HoldTime) return;
            _hold = 0; _bags--;
            Sandbag.Build(pos, yaw, ProceduralModels.Theme);
            BotAudio.PlayAt(SoundSynth.Thud(), pos, 0.8f);
        }

        bool Place(out Vector3 pos, out float yaw)
        {
            yaw = _pc.transform.eulerAngles.y;
            Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            Vector3 p = transform.position + fwd * 1.8f + Vector3.up * 2f;
            pos = p;
            if (!Physics.Raycast(p, Vector3.down, out var h, 4f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore)) return false;
            pos = h.point;
            if (h.normal.y < 0.8f) return false;
            return !Physics.CheckBox(pos + Vector3.up * 0.5f, new Vector3(1.0f, 0.38f, 0.22f), Quaternion.Euler(0, yaw, 0), CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
        }

        void ShowGhost(bool on, Vector3 pos, float yaw, bool ok)
        {
            if (!on) { if (_ghost) _ghost.gameObject.SetActive(false); return; }
            if (!_ghost)
            {
                _ghost = ProceduralModels.Part(null, "SandbagGhost", MeshUtil.Bevel(0.1f), Vector3.zero, new Vector3(2.1f, 0.84f, 0.5f), Color.white).transform;
                _ghost.gameObject.layer = 2;
                var r = _ghost.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _ghost.gameObject.SetActive(true);
            _ghost.SetPositionAndRotation(pos + Vector3.up * 0.42f, Quaternion.Euler(0, yaw, 0));
            var rr = _ghost.GetComponent<Renderer>();
            if (rr) rr.sharedMaterial = ProceduralModels.Mat(ok ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.3f, 0.3f), 0.1f);
        }

        void OnDisable() { if (_ghost) _ghost.gameObject.SetActive(false); }
    }
}
