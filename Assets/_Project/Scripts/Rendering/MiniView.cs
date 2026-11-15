using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Frontline
{
    /// <summary>
    /// Маленький 3D-вид сверху в текстуру (окно слева снизу): у артиллерии по ПКМ — на курсор, у танка — постоянно,
    /// на точку падения снаряда. Одна камера на всех, без теней и постобработки; на телефоне — каждый второй кадр.
    /// Кто-то должен «заказывать» вид каждый кадр (Request), иначе камера выключается.
    /// </summary>
    public static class MiniView
    {
        static Camera _cam;
        static RenderTexture _rt;
        static float _lastReq = -99f;
        static int _frame;

        public static RenderTexture Texture => _rt;
        public static bool Active => _cam && Time.unscaledTime - _lastReq < 0.15f;
        public static Camera Cam => _cam;
        /// <summary>Сколько метров земли в ширину видно в окне (для круга зоны поражения).</summary>
        public static float WorldWidth { get; private set; } = 100f;

        public static void Request(Vector3 center, float height, float fov, Vector3 upFlat)
        {
            Ensure();
            _lastReq = Time.unscaledTime;
            upFlat.y = 0; if (upFlat.sqrMagnitude < 1e-4f) upFlat = Vector3.forward;
            // Чуть наклонённый вид (как раньше): камера позади и выше точки
            Vector3 pos = center - upFlat.normalized * height * 0.35f + Vector3.up * height;
            _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(center - pos, upFlat.normalized));
            _cam.fieldOfView = fov;
            WorldWidth = 2f * Vector3.Distance(pos, center) * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            bool render = !Application.isMobilePlatform || (++_frame & 1) == 0;
            _cam.enabled = render;
        }

        static void Ensure()
        {
            if (_cam) return;
            int res = Application.isMobilePlatform ? 256 : 400;
            _rt = new RenderTexture(res, res, 16, RenderTextureFormat.ARGB32) { name = "MiniView", antiAliasing = 1 };
            var go = new GameObject("[MiniViewCam]");
            Object.DontDestroyOnLoad(go);
            _cam = go.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.nearClipPlane = 1f; _cam.farClipPlane = 900f;
            _cam.cullingMask = ~(1 << 31);
            _cam.depth = -10;
            var acd = go.AddComponent<UniversalAdditionalCameraData>();
            acd.renderShadows = false; acd.renderPostProcessing = false; acd.antialiasing = AntialiasingMode.None;
            _cam.enabled = false;
        }

        /// <summary>Вызывается раз в кадр (из HUD): если вид больше не нужен — камера выключается.</summary>
        public static void Tick()
        {
            if (_cam && !Active && _cam.enabled) _cam.enabled = false;
        }
    }
}
