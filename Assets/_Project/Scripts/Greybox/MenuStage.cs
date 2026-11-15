using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>3D-сцена меню: оружие на поворотном столе, трёхточечный свет, рендер в текстуру.</summary>
    public class MenuStage : MonoBehaviour
    {
        public RenderTexture Texture { get; private set; }
        public bool Visible { get; set; } = true;
        /// <summary>Кастомизация на весь экран: оружие по центру и вдвое мельче, меню — по бокам.</summary>
        public bool Centered { get; set; }
        float _center;
        Transform _turn;
        WeaponVisualAssembler _asm;
        Camera _cam;
        Vector2 _rot = new(8, -25);
        float _idle;

        public static MenuStage Create(Transform parent)
        {
            var go = new GameObject("MenuStage");
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(0, -1000, 0);
            var s = go.AddComponent<MenuStage>();
            s.Texture = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var camGo = new GameObject("StageCam"); camGo.transform.SetParent(go.transform, false);
            s._cam = camGo.AddComponent<Camera>();
            s._cam.targetTexture = s.Texture; s._cam.clearFlags = CameraClearFlags.SolidColor;
            s._cam.backgroundColor = new Color(0.07f, 0.08f, 0.065f); s._cam.fieldOfView = 26; s._cam.nearClipPlane = 0.05f; s._cam.farClipPlane = 20;
            camGo.transform.localPosition = new Vector3(-0.35f, 0.15f, -2.6f);
            camGo.transform.localRotation = Quaternion.Euler(3, 8, 0);
            void L(Vector3 e, Color c, float i) { var l = new GameObject("Light").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.type = LightType.Directional; l.transform.localRotation = Quaternion.Euler(e); l.color = c; l.intensity = i; }
            L(new(30, -40, 0), new Color(1f, 0.92f, 0.8f), 1.6f);
            L(new(10, 140, 0), new Color(0.5f, 0.6f, 0.8f), 0.7f);
            L(new(-30, 0, 0), new Color(1f, 0.85f, 0.6f), 0.5f);
            s._turn = new GameObject("Turntable").transform; s._turn.SetParent(go.transform, false);
            s._asm = s._turn.gameObject.AddComponent<WeaponVisualAssembler>();
            return s;
        }

        public void ShowWeapon(WeaponDefinition w, IReadOnlyList<AttachmentDefinition> atts)
        {
            if (!w) return;
            _asm.Build(w, atts, true, 0);
            if (!_asm.Root) return;
            // Руки в меню не нужны
            foreach (var n in new[] { "HandR", "HandL", "ForearmR", "ForearmL" }) { var t = _asm.Root.transform.Find(n); if (t) t.gameObject.SetActive(false); }
            var rs = _asm.Root.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float scale = 1.25f / Mathf.Max(0.3f, b.size.magnitude);
            _asm.Root.transform.localScale = Vector3.one * scale * 1.6f;   // размер подстраивает камера (Centered)
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            _asm.Root.transform.position -= b.center - _turn.position;
        }

        public void Drag(Vector2 d) { _rot += new Vector2(-d.y, d.x) * 0.3f; _idle = 0; }

        void Update()
        {
            _cam.enabled = Visible;
            // Плавный переход ракурса: обычный — оружие справа крупно; Centered — по центру, вдвое мельче
            _center = Mathf.MoveTowards(_center, Centered ? 1f : 0f, Time.deltaTime * 4f);
            float k = Mathf.SmoothStep(0, 1, _center);
            _cam.transform.localPosition = Vector3.Lerp(new Vector3(-0.35f, 0.15f, -2.6f), new Vector3(0f, 0.12f, -5.2f), k);
            _cam.transform.localRotation = Quaternion.Slerp(Quaternion.Euler(3, 8, 0), Quaternion.Euler(2, 0, 0), k);
            _idle += Time.deltaTime;
            if (_idle > 2.5f) _rot.y += 14f * Time.deltaTime;
            _rot.x = Mathf.Clamp(_rot.x, -50, 50);
            _turn.localRotation = Quaternion.Euler(_rot.x, _rot.y, 0);
        }

        void OnDestroy() { if (Texture) Texture.Release(); }
    }
}
