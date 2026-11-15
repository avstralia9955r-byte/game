using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>
    /// Картинки обвесов: каждая модель обвеса один раз фотографируется в отдельной «студии» (свой слой, три источника
    /// света, ракурс три четверти) в текстуру 160×120 и кэшируется. Используется в меню кастомизации.
    /// </summary>
    public static class AttachmentIcons
    {
        const int Layer = 29, W = 160, Hh = 120;
        static readonly Dictionary<string, Texture2D> _cache = new();
        static Transform _studio;
        static Camera _cam;

        public static Texture2D Get(AttachmentDefinition a)
        {
            if (!a) return null;
            if (_cache.TryGetValue(a.id, out var t) && t) return t;
            t = Render(a);
            if (t) _cache[a.id] = t;
            return t;
        }

        static void EnsureStudio()
        {
            if (_studio) return;
            _studio = new GameObject("[AttachmentIconStudio]").transform;
            Object.DontDestroyOnLoad(_studio.gameObject);
            _studio.position = new Vector3(0, -2000, 0);
            var camGo = new GameObject("IconCam"); camGo.transform.SetParent(_studio, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false; _cam.cullingMask = 1 << Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.11f, 0.12f, 0.1f, 1f);
            _cam.fieldOfView = 22f; _cam.nearClipPlane = 0.02f; _cam.farClipPlane = 10f;
            void L(Vector3 e, Color c, float i)
            {
                var l = new GameObject("Light").AddComponent<Light>(); l.transform.SetParent(_studio, false);
                l.type = LightType.Directional; l.transform.localRotation = Quaternion.Euler(e); l.color = c; l.intensity = i; l.cullingMask = 1 << Layer;
            }
            L(new Vector3(35, -35, 0), new Color(1f, 0.93f, 0.82f), 1.5f);
            L(new Vector3(10, 150, 0), new Color(0.55f, 0.65f, 0.85f), 0.8f);
            L(new Vector3(-40, 20, 0), new Color(1f, 0.85f, 0.65f), 0.45f);
            
        }

        static Texture2D Render(AttachmentDefinition a) =>
            a.visualPrefab ? RenderPrefab(a.visualPrefab, "Icon_" + a.id, Quaternion.Euler(-8f, 140f, 6f), W, Hh, false, 1.15f) : null;

        // ---------------- Картинки оружия (вид сбоку, ствол вправо, без рук) ----------------
        const int WW = 288, WH = 112;
        static readonly Dictionary<string, Texture2D> _wcache = new();

        public static Texture2D Get(WeaponDefinition w)
        {
            if (!w || !w.viewModelPrefab) return null;
            if (_wcache.TryGetValue(w.id, out var t) && t) return t;
            t = RenderPrefab(w.viewModelPrefab, "WIcon_" + w.id, Quaternion.Euler(-4f, 96f, 0f), WW, WH, true, 1.02f);
            if (t) _wcache[w.id] = t;
            return t;
        }

        static readonly Dictionary<long, RenderTexture> _rts = new();

        static Texture2D RenderPrefab(GameObject prefab, string name, Quaternion rot, int w, int h, bool hideHands, float margin)
        {
            try
            {
                EnsureStudio();
                long key = ((long)w << 32) | (uint)h;
                if (!_rts.TryGetValue(key, out var rt) || !rt) { rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "IconRT" }; _rts[key] = rt; }
                var go = Object.Instantiate(prefab, _studio, false);
                go.SetActive(true);
                foreach (var tr in go.GetComponentsInChildren<Transform>(true))
                {
                    tr.gameObject.layer = Layer;
                    if (hideHands && (tr.name == "HandR" || tr.name == "HandL")) tr.gameObject.SetActive(false);
                }
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) { Object.Destroy(go); return null; }
                go.transform.localRotation = rot;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                // Вписываем по ширине и высоте кадра
                float aspect = (float)w / h;
                float halfV = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float need = Mathf.Max(b.extents.y / halfV, b.extents.x / (halfV * aspect), 0.05f);
                float dist = need * margin + b.extents.z;
                _cam.aspect = aspect;
                _cam.transform.position = b.center + new Vector3(0, 0, -dist);
                _cam.transform.LookAt(b.center);
                _cam.targetTexture = rt;

                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (GraphicsSettings.currentRenderPipeline != null && RenderPipeline.SupportsRenderRequest(_cam, req))
                    RenderPipeline.SubmitRenderRequest(_cam, req);
                else _cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                _cam.targetTexture = null; _cam.ResetAspect();
                go.SetActive(false);
                Object.Destroy(go);
                return tex;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Frontline] Картинка не отрисована: " + e.Message);
                return null;
            }
        }
    }
}
