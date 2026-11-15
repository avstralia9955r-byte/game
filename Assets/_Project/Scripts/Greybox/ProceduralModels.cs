using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>Генерация простых low-poly мешей: призма (двускатная крыша), конус, восьмигранный цилиндр.</summary>
    public static class MeshUtil
    {
        static readonly Dictionary<string, Mesh> _cache = new();

        public static Mesh Prism()
        {
            if (_cache.TryGetValue("prism", out var m)) return m;
            // Треугольная призма: основание 1×1 по X/Z, конёк по Z на высоте 1 (в локальных -0.5..0.5).
            Vector3 a = new(-0.5f, -0.5f, -0.5f), b = new(0.5f, -0.5f, -0.5f), c = new(0, 0.5f, -0.5f);
            Vector3 d = new(-0.5f, -0.5f, 0.5f), e = new(0.5f, -0.5f, 0.5f), f = new(0, 0.5f, 0.5f);
            var v = new List<Vector3>(); var t = new List<int>();
            void Tri(Vector3 p0, Vector3 p1, Vector3 p2) { int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); t.Add(i); t.Add(i + 1); t.Add(i + 2); }
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) { Tri(p0, p1, p2); Tri(p0, p2, p3); }
            Tri(a, c, b); Tri(d, e, f);
            Quad(a, d, f, c); Quad(b, c, f, e); Quad(a, b, e, d);
            m = new Mesh { name = "Prism" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return _cache["prism"] = m;
        }

        /// <summary>Конус: основание радиуса 0.5 на y=0, вершина y=1. Плоское затенение (low-poly).</summary>
        public static Mesh Cone(int seg = 7)
        {
            string key = "cone" + seg;
            if (_cache.TryGetValue(key, out var m)) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            var top = new Vector3(0, 1, 0);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                int k = v.Count; v.Add(p0); v.Add(top); v.Add(p1); t.Add(k); t.Add(k + 1); t.Add(k + 2);
                k = v.Count; v.Add(p0); v.Add(p1); v.Add(Vector3.zero); t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }
            m = new Mesh { name = key }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return _cache[key] = m;
        }

        /// <summary>Многогранный цилиндр вдоль Y (-0.5..0.5), радиус 0.5. Меньше треугольников, чем у примитива.</summary>
        public static Mesh Cylinder(int seg = 8)
        {
            string key = "cyl" + seg;
            if (_cache.TryGetValue(key, out var m)) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 b0 = new(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f), b1 = new(Mathf.Cos(a1) * 0.5f, -0.5f, Mathf.Sin(a1) * 0.5f);
                Vector3 u0 = b0 + Vector3.up, u1 = b1 + Vector3.up;
                int k = v.Count; v.Add(b0); v.Add(u0); v.Add(u1); v.Add(b1);
                t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
                k = v.Count; v.Add(u0); v.Add(new Vector3(0, 0.5f, 0)); v.Add(u1); t.Add(k); t.Add(k + 1); t.Add(k + 2);
                k = v.Count; v.Add(b1); v.Add(new Vector3(0, -0.5f, 0)); v.Add(b0); t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }
            m = new Mesh { name = key }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return _cache[key] = m;
        }

        /// <summary>Куб со снятыми фасками (chamfer) — «дорогой» силуэт low-poly: блики на рёбрах вместо острых углов.</summary>
        public static Mesh Bevel(float b = 0.1f)
        {
            string key = "bevel" + b;
            if (_cache.TryGetValue(key, out var m)) return m;
            float h = 0.5f, a = h - b;
            var v = new List<Vector3>(); var t = new List<int>();
            void Poly(params Vector3[] p)
            {
                Vector3 c = Vector3.zero; foreach (var q in p) c += q;
                if (Vector3.Dot(Vector3.Cross(p[1] - p[0], p[2] - p[0]), c) < 0) System.Array.Reverse(p);
                int k = v.Count; v.AddRange(p);
                for (int i = 1; i < p.Length - 1; i++) { t.Add(k); t.Add(k + i); t.Add(k + i + 1); }
            }
            Vector3 P(int ax, float va, int bx, float vb, int cx, float vc) { var r = Vector3.zero; r[ax] = va; r[bx] = vb; r[cx] = vc; return r; }
            for (int ax = 0; ax < 3; ax++)
            {
                int bx = (ax + 1) % 3, cx = (ax + 2) % 3;
                for (int s = -1; s <= 1; s += 2)   // 6 граней
                    Poly(P(ax, s * h, bx, -a, cx, -a), P(ax, s * h, bx, a, cx, -a), P(ax, s * h, bx, a, cx, a), P(ax, s * h, bx, -a, cx, a));
                for (int s1 = -1; s1 <= 1; s1 += 2) // 12 фасок вдоль рёбер (ребро параллельно оси cx)
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        Poly(P(ax, s1 * h, bx, s2 * a, cx, -a), P(ax, s1 * h, bx, s2 * a, cx, a), P(ax, s1 * a, bx, s2 * h, cx, a), P(ax, s1 * a, bx, s2 * h, cx, -a));
            }
            for (int sx = -1; sx <= 1; sx += 2)     // 8 углов
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Poly(new Vector3(sx * h, sy * a, sz * a), new Vector3(sx * a, sy * h, sz * a), new Vector3(sx * a, sy * a, sz * h));
            m = new Mesh { name = key }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return _cache[key] = m;
        }

        /// <summary>Усечённый конус вдоль Y (-0.5..0.5): нижний радиус 0.5, верхний 0.5·top. Пламегасители, дульные раструбы, башни.</summary>
        public static Mesh Frustum(int seg, float top)
        {
            string key = "fr" + seg + "_" + top;
            if (_cache.TryGetValue(key, out var m)) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 b0 = new(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f), b1 = new(Mathf.Cos(a1) * 0.5f, -0.5f, Mathf.Sin(a1) * 0.5f);
                Vector3 u0 = new(b0.x * top, 0.5f, b0.z * top), u1 = new(b1.x * top, 0.5f, b1.z * top);
                int k = v.Count; v.Add(b0); v.Add(u0); v.Add(u1); v.Add(b1);
                t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
                k = v.Count; v.Add(u0); v.Add(new Vector3(0, 0.5f, 0)); v.Add(u1); t.Add(k); t.Add(k + 1); t.Add(k + 2);
                k = v.Count; v.Add(b1); v.Add(new Vector3(0, -0.5f, 0)); v.Add(b0); t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }
            m = new Mesh { name = key }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return _cache[key] = m;
        }

        /// <summary>
        /// Сужающийся брусок с плоскими гранями (стиль Ravenfield): основание 1×1 на y = −0.5, верх — tx×tz на y = +0.5,
        /// верх может быть сдвинут вперёд на topZ (наклон груди, носок сапога).
        /// </summary>
        public static Mesh Taper(float tx, float tz, float topZ = 0f)
        {
            string key = "taper" + tx + "_" + tz + "_" + topZ;
            if (_cache.TryGetValue(key, out var m)) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            var center = new Vector3(0, 0, topZ * 0.5f);
            void Poly(params Vector3[] p)
            {
                Vector3 c = Vector3.zero; foreach (var q in p) c += q;
                c /= p.Length;
                if (Vector3.Dot(Vector3.Cross(p[1] - p[0], p[2] - p[0]), c - center) < 0) System.Array.Reverse(p);
                int k = v.Count; v.AddRange(p);
                for (int i = 1; i < p.Length - 1; i++) { t.Add(k); t.Add(k + i); t.Add(k + i + 1); }
            }
            var b = new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f) };
            var u = new Vector3[4];
            for (int i = 0; i < 4; i++) u[i] = new Vector3(b[i].x * tx, 0.5f, b[i].z * tz + topZ);
            Poly(b[0], b[1], b[2], b[3]);
            Poly(u[0], u[1], u[2], u[3]);
            for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; Poly(b[i], b[j], u[j], u[i]); }
            m = new Mesh { name = key }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return _cache[key] = m;
        }

        /// <summary>Низкополигональная «сфера» (икосаэдр) — кроны деревьев, головы.</summary>
        public static Mesh Ico()
        {
            if (_cache.TryGetValue("ico", out var m)) return m;
            float p = (1 + Mathf.Sqrt(5)) / 2;
            var pts = new[] { new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0), new Vector3(0, -1, p), new Vector3(0, 1, p),
                              new Vector3(0, -1, -p), new Vector3(0, 1, -p), new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1) };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < f.Length; i++) { v.Add(pts[f[i]].normalized * 0.5f); t.Add(i); }
            m = new Mesh { name = "Ico" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return _cache["ico"] = m;
        }
    }

    /// <summary>
    /// Процедурные low-poly модели: оружие (индивидуально по образцу) с руками и сокетами обвесов, обвесы,
    /// нож, шпатель, части тела ботов. Без коллайдеров. Шаблоны лежат под скрытым неактивным корнем.
    /// </summary>
    public static partial class ProceduralModels
    {
        static Transform _root;
        static Shader _lit;
        static readonly Dictionary<Color, Material> _mats = new();

        public static Transform Root
        {
            get
            {
                if (_root) return _root;
                var go = new GameObject("[ProceduralTemplates]");
                go.SetActive(false);
                Object.DontDestroyOnLoad(go);
                return _root = go.transform;
            }
        }

        /// <summary>Шейдер по активному пайплайну: URP Lit при URP, Standard при Built-in (иначе — розовый).</summary>
        public static Shader Lit
        {
            get
            {
                if (_lit) return _lit;
                bool urp = GraphicsSettings.currentRenderPipeline != null;
                _lit = urp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
                if (!_lit) _lit = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard");
                return _lit;
            }
        }

        public static Material Mat(Color c, float smooth = 0.2f, float metal = 0f)
        {
            var key = new Color(c.r, c.g, c.b, smooth + metal * 10f);
            if (_mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(Lit) { color = c, enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return _mats[key] = m;
        }

        public static GameObject Part(Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 scale, Color c, Vector3 euler = default, bool collider = false, float smooth = 0.2f, float metal = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = Mat(c, smooth, metal);
            if (collider) go.AddComponent<BoxCollider>();
            return go;
        }

        static Mesh _cube;
        public static Mesh CubeMesh
        {
            get
            {
                if (_cube) return _cube;
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = g.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(g);
                return _cube;
            }
        }

        public static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Color c, bool collider = false, Vector3 euler = default, float smooth = 0.2f, float metal = 0f)
            => Part(parent, name, CubeMesh, pos, size, c, euler, collider, smooth, metal);

        // Цилиндр вдоль +Z (стволы, кожухи, глушители)
        public static GameObject Tube(Transform p, string n, Vector3 pos, float r, float len, Color c, int seg = 8, float smooth = 0.35f, float metal = 0.6f)
            => Part(p, n, MeshUtil.Cylinder(seg), pos, new Vector3(r * 2, len, r * 2), c, new Vector3(90, 0, 0), false, smooth, metal);

        public static Transform Empty(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false); t.localPosition = pos;
            return t;
        }

        // Палитра
        public static readonly Color Wood = new(0.43f, 0.27f, 0.15f), WoodDark = new(0.3f, 0.19f, 0.11f),
            Blued = new(0.13f, 0.14f, 0.16f), Steel = new(0.32f, 0.33f, 0.35f), Bakelite = new(0.22f, 0.13f, 0.09f),
            Skin = new(0.86f, 0.66f, 0.52f), SleeveDe = new(0.36f, 0.39f, 0.35f), SleeveSu = new(0.45f, 0.41f, 0.27f),
            SleeveWinter = new(0.88f, 0.89f, 0.9f);

        public static MapTheme Theme = MapTheme.Summer;

        /// <summary>Сбросить кэш материалов/шейдера (после смены пайплайна или темы карты).</summary>
        public static void ResetCaches() { _mats.Clear(); _lit = null; }
    }
}
