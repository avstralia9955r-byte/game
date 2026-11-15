using System.Collections.Generic;
using Frontline.Bots;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Бинокль 6× (покупается в меню для поддержки — класса с противотанковым оружием). [Z] — поднести/опустить.
    /// Пока смотришь: оружие опущено, стрелять нельзя; враг в центре бинокля (до 600 м, в прямой видимости)
    /// отмечается красным маркером на 8 секунд — маркеры видны и без бинокля.
    /// </summary>
    public class Binoculars : MonoBehaviour
    {
        public static bool Allowed;            // выставляет GameModeManager при высадке
        public static bool Active { get; private set; }
        public static readonly Dictionary<int, float> Spotted = new();

        PlayerController _pc;
        float _scan;
        static Texture2D _mask, _mark;

        void Awake() => _pc = GetComponent<PlayerController>();
        void OnDisable() => Active = false;

        void Update()
        {
            if (!_pc) return;
            bool alive = PlayerBridge.Health && PlayerBridge.Health.Alive;
            if (!Allowed || !alive || _pc.Vehicle || _pc.Flying || _pc.Gunning || !_pc.InputEnabled) { if (Active) Set(false); return; }
            if (_pc.Input.binoPressed) Set(!Active);
            if (Active && (_pc.Input.firePressed || _pc.IsSprinting)) Set(false);
            if (!Active) return;
            _pc.TargetFov = _pc.BaseFov / 6f;
            if (Time.time < _scan) return;
            _scan = Time.time + 0.25f;
            var cam = _pc.worldCamera.transform;
            var actors = CombatWorld.Actors;
            if (!actors.IsCreated) return;
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team == _pc.Team) continue;
                Vector3 p = (Vector3)a.position + Vector3.up * 1.2f;
                Vector3 d = p - cam.position; float dist = d.magnitude;
                if (dist > 600f || Vector3.Angle(cam.forward, d) > 3.5f) continue;
                if (Physics.Raycast(cam.position, d / dist, dist - 1f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore)) continue;
                Spotted[a.actorId] = Time.time + 8f;
            }
            foreach (var t in Tank.All)
                if (t && t.Alive && t.Team != _pc.Team && Vector3.Angle(cam.forward, t.transform.position + Vector3.up - cam.position) < 4f)
                    Spotted[-1000 - t.index] = Time.time + 8f;
        }

        void Set(bool on)
        {
            Active = on;
            if (!on && _pc) _pc.TargetFov = 0;
            BotAudio.PlayAt(SoundSynth.Holster(), transform.position + Vector3.up * 1.5f, 0.3f);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            var cam = _pc ? _pc.worldCamera : null;
            if (!cam) return;
            if (Active)
            {
                if (!_mask) _mask = MakeMask();
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _mask, ScaleMode.StretchToFill);
                GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
                float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
                for (int k = -4; k <= 4; k++) GUI.DrawTexture(new Rect(cx + k * Screen.height * 0.04f - 1, cy - (k == 0 ? 14 : 7), 2, k == 0 ? 28 : 14), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - Screen.height * 0.17f, cy, Screen.height * 0.34f, 1), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            // Маркеры замеченных врагов
            if (Spotted.Count == 0) return;
            if (!_mark) { _mark = new Texture2D(1, 1); _mark.SetPixel(0, 0, Color.white); _mark.Apply(); }
            var actors = CombatWorld.Actors;
            float now = Time.time;
            List<int> dead = null;
            foreach (var kv in Spotted)
            {
                if (now > kv.Value) { (dead ??= new List<int>()).Add(kv.Key); continue; }
                Vector3 wp;
                if (kv.Key <= -1000)
                {
                    Tank tk = null; foreach (var t in Tank.All) if (t && -1000 - t.index == kv.Key) tk = t;
                    if (!tk || !tk.Alive) continue;
                    wp = tk.transform.position + Vector3.up * 3.5f;
                }
                else
                {
                    if (!actors.IsCreated || kv.Key >= actors.Length || !actors[kv.Key].alive) continue;
                    wp = (Vector3)actors[kv.Key].position + Vector3.up * 2.2f;
                }
                Vector3 sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0) continue;
                float s = Mathf.Clamp(14f * 60f / Mathf.Max(8f, sp.z), 7f, 16f);
                float a = Mathf.Clamp01((kv.Value - now) / 1.5f);
                GUI.color = new Color(0.95f, 0.2f, 0.15f, 0.85f * a);
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45, new Vector2(sp.x, Screen.height - sp.y));
                GUI.DrawTexture(new Rect(sp.x - s * 0.5f, Screen.height - sp.y - s * 0.5f, s, s), _mark);
                GUI.matrix = m;
            }
            GUI.color = Color.white;
            if (dead != null) foreach (var k in dead) Spotted.Remove(k);
        }

        /// <summary>Чёрная маска с двумя круглыми окулярами и мягким краем.</summary>
        static Texture2D MakeMask()
        {
            int w = 512, h = 256;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f, v = (y + 0.5f) / h;      // u 0..2, v 0..1 (кадр 2:1)
                    float d1 = Vector2.Distance(new Vector2(u, v), new Vector2(0.72f, 0.5f));
                    float d2 = Vector2.Distance(new Vector2(u, v), new Vector2(1.28f, 0.5f));
                    float d = Mathf.Min(d1, d2);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.46f, d));
                    px[y * w + x] = new Color(0, 0, 0, a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }
    }
}
