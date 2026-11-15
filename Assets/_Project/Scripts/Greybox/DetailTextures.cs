using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Детализация моделей оружия и рук: процедурные текстуры (волокна дерева, воронёная/фосфатированная сталь с
    /// потёртостями, бакелит, кожа, ткань рукава, кожа рук) и меши с UV в метрах (плотность текстуры одинакова на
    /// любых деталях), а также сглаженные нормали у цилиндров, конусов и сфер (стволы и рукояти без «граней»).
    /// </summary>
    public enum DetailKind : byte { Metal, Wood, Bakelite, Leather, Fabric, Skin, Paint, Plaster, Stone, Tiles, Planks, Foliage, Glass }

    public static class MeshDetail
    {
        static readonly Dictionary<string, Mesh> _cache = new();
        static readonly Dictionary<int, Mesh> _round = new();

        /// <summary>
        /// Скруглённый параллелепипед с гладкими нормалями (единичный куб, радиус скругления — доля размера).
        /// Для пальцев, ладоней, деревянных лож и бакелитовых рукоятей — органичная форма вместо рубленых коробок.
        /// </summary>
        public static Mesh RoundBox(float radius = 0.3f, int n = 4)
        {
            int key = Mathf.RoundToInt(radius * 100) * 100 + n;
            if (_round.TryGetValue(key, out var m) && m) return m;
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var t = new List<int>();
            Vector3[] fn = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            float lo = -0.5f + radius, hi = 0.5f - radius;
            foreach (var f in fn)
            {
                Vector3 u = Mathf.Abs(f.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(f, u);
                int b = v.Count;
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 p = f * 0.5f + u * (i / (float)n - 0.5f) + w * (j / (float)n - 0.5f);
                        Vector3 inner = new(Mathf.Clamp(p.x, lo, hi), Mathf.Clamp(p.y, lo, hi), Mathf.Clamp(p.z, lo, hi));
                        Vector3 d = p - inner;
                        Vector3 q = d.sqrMagnitude > 1e-8f ? inner + d.normalized * radius : p;
                        v.Add(q); nr.Add(d.sqrMagnitude > 1e-8f ? d.normalized : f);
                    }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int a = b + j * (n + 1) + i, c = a + n + 1;
                        // Ориентация по нормали грани
                        Vector3 cr = Vector3.Cross(v[a + 1] - v[a], v[c] - v[a]);
                        // Unity: лицевая сторона — та, куда смотрит cross(v1 − v0, v2 − v0). Нужна наружу (вдоль f).
                        if (Vector3.Dot(cr, f) > 0) { t.Add(a); t.Add(a + 1); t.Add(c); t.Add(a + 1); t.Add(c + 1); t.Add(c); }
                        else { t.Add(a); t.Add(c); t.Add(a + 1); t.Add(a + 1); t.Add(c); t.Add(c + 1); }
                    }
            }
            m = new Mesh { name = "rbox" + key };
            m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(t, 0); m.RecalculateBounds();
            return _round[key] = m;
        }

        /// <summary>Копия меша с запечённым масштабом в UV (бокс-проекция) и сглаженными боковыми нормалями.</summary>
        public static Mesh Textured(Mesh src, Vector3 scale, float tile)
        {
            string key = $"{src.name}|{scale.x:0.000}|{scale.y:0.000}|{scale.z:0.000}|{tile:0.###}";
            if (_cache.TryGetValue(key, out var m) && m) return m;
            var v = src.vertices; var n = src.normals; var tri = src.triangles;
            var uv = new Vector2[v.Length];
            bool round = src.name.StartsWith("cyl") || src.name.StartsWith("fr");
            bool sphere = src.name == "Ico";
            // Для конуса: наклон боковой нормали (радиус 0.5 снизу → 0.5·top сверху)
            float slope = 0;
            if (src.name.StartsWith("fr") && float.TryParse(src.name.Substring(src.name.IndexOf('_') + 1).Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var top))
                slope = 0.5f * (1f - top);
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = Vector3.Scale(v[i], scale);
                Vector3 an = new(Mathf.Abs(n[i].x), Mathf.Abs(n[i].y), Mathf.Abs(n[i].z));
                uv[i] = an.x >= an.y && an.x >= an.z ? new Vector2(p.z, p.y)
                      : an.y >= an.z ? new Vector2(p.z, p.x) : new Vector2(p.x, p.y);
                uv[i] /= tile;
                if (round && i % 10 < 4)          // боковая поверхность (4 вершины квада на сегмент)
                {
                    var radial = new Vector3(v[i].x, 0, v[i].z);
                    if (radial.sqrMagnitude > 1e-6f) n[i] = (radial.normalized + Vector3.up * slope).normalized;
                }
                if (sphere) n[i] = v[i].normalized;
            }
            m = new Mesh { name = src.name + "_d" };
            m.vertices = v; m.normals = n; m.uv = uv; m.triangles = tri;
            m.RecalculateBounds();
            return _cache[key] = m;
        }
    }

    public static partial class ProceduralModels
    {
        static readonly Dictionary<DetailKind, Texture2D> _detailTex = new();
        static readonly Dictionary<(Color, DetailKind, float, float), Material> _detailMats = new();

        public static float DetailTile(DetailKind k) => k switch
        {
            DetailKind.Wood => 0.22f, DetailKind.Metal => 0.07f, DetailKind.Bakelite => 0.1f, DetailKind.Leather => 0.08f,
            DetailKind.Fabric => 0.09f, DetailKind.Skin => 0.12f, DetailKind.Plaster => 2.5f, DetailKind.Stone => 1.6f,
            DetailKind.Tiles => 1.2f, DetailKind.Planks => 1.8f, DetailKind.Foliage => 1.4f, DetailKind.Glass => 1.5f, _ => 0.12f
        };

        /// <summary>Материал URP Lit с процедурной текстурой-множителем поверх цвета детали.</summary>
        public static Material DetailMat(Color c, DetailKind kind, float smooth, float metal)
        {
            var key = (c, kind, smooth, metal);
            if (_detailMats.TryGetValue(key, out var m) && m) return m;
            m = new Material(Lit) { enableInstancing = true };
            // Текстура темнее единицы — компенсируем яркостью цвета
            Color bc = new(Mathf.Min(1, c.r * 1.18f), Mathf.Min(1, c.g * 1.18f), Mathf.Min(1, c.b * 1.18f), 1);
            m.color = bc;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", bc);
            var tex = DetailTex(kind);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return _detailMats[key] = m;
        }

        public static Texture2D DetailTex(DetailKind kind)
        {
            if (_detailTex.TryGetValue(kind, out var t) && t) return t;
            const int N = 256;
            t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "Detail_" + kind };
            var px = new Color32[N * N];
            var rnd = new System.Random(1000 + (int)kind);
            float seed = (float)rnd.NextDouble() * 100f;
            // Царапины для металла
            var scratches = new List<(Vector2 a, Vector2 b)>();
            if (kind == DetailKind.Metal)
                for (int i = 0; i < 26; i++)
                {
                    var a = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
                    float ang = (float)rnd.NextDouble() * Mathf.PI, len = 0.05f + (float)rnd.NextDouble() * 0.25f;
                    scratches.Add((a, a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len));
                }
            float Tiled(float x, float y, float f) // бесшовный шум (тор)
            {
                float a = Mathf.PerlinNoise(seed + x * f, seed + y * f), b = Mathf.PerlinNoise(seed + (x - 1) * f, seed + y * f);
                float c = Mathf.PerlinNoise(seed + x * f, seed + (y - 1) * f), d = Mathf.PerlinNoise(seed + (x - 1) * f, seed + (y - 1) * f);
                return Mathf.Lerp(Mathf.Lerp(a, b, x), Mathf.Lerp(c, d, x), y);
            }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N, k;
                    switch (kind)
                    {
                        case DetailKind.Wood:
                        {
                            // Волокна вдоль U: годичные полосы, изгибы, мелкие поры
                            float warp = Tiled(u, v, 3f) * 2.2f + Tiled(u, v, 9f) * 0.4f;
                            float ring = Mathf.Sin((v * 14f + warp) * Mathf.PI * 2f) * 0.5f + 0.5f;
                            ring = Mathf.Pow(ring, 3f);
                            float fiber = Tiled(u * 0.25f, v, 48f);
                            float pore = rnd.NextDouble() < 0.02 ? 0.85f : 1f;
                            k = (0.7f + 0.22f * (1 - ring) + 0.1f * fiber) * pore;
                            break;
                        }
                        case DetailKind.Metal:
                        {
                            float speck = 0.88f + 0.08f * Tiled(u, v, 40f) + ((float)rnd.NextDouble() - 0.5f) * 0.06f;
                            float wear = Mathf.Clamp01((Tiled(u, v, 4f) - 0.62f) * 4f) * 0.12f;   // потёртости до светлого металла
                            k = speck + wear;
                            foreach (var (a, b) in scratches)
                            {
                                Vector2 p = new(u, v), ab = b - a;
                                float tt = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                                if (Vector2.Distance(p, a + ab * tt) < 0.0025f) { k += 0.1f; break; }
                            }
                            break;
                        }
                        case DetailKind.Bakelite:
                            k = 0.72f + 0.2f * Tiled(u, v, 6f) + 0.08f * Tiled(u, v, 22f);
                            break;
                        case DetailKind.Leather:
                        {
                            float cell = Tiled(u, v, 28f);
                            k = 0.72f + 0.22f * Mathf.Abs(cell - 0.5f) * 2f + ((float)rnd.NextDouble() - 0.5f) * 0.05f;
                            break;
                        }
                        case DetailKind.Fabric:
                        {
                            // Саржевое плетение + пятна грязи/износа
                            float weave = ((x + y * 2) % 6 < 3 ? 0.95f : 0.84f) * (((x / 2) % 2 == 0) ? 1f : 0.96f);
                            float dirt = Tiled(u, v, 3f);
                            k = weave * (0.82f + 0.18f * dirt);
                            break;
                        }
                        case DetailKind.Plaster:
                        {
                            // Штукатурка: крупные пятна сырости, мелкая шероховатость, трещины
                            float stain = Tiled(u, v, 3f), grit = ((float)rnd.NextDouble() - 0.5f) * 0.07f;
                            float crack = Mathf.Abs(Tiled(u, v, 7f) - 0.5f) < 0.006f ? 0.75f : 1f;
                            k = (0.82f + 0.12f * stain + 0.06f * Tiled(u, v, 25f) + grit) * crack;
                            break;
                        }
                        case DetailKind.Stone:
                        {
                            // Кладка из камней неправильной формы: ряды со смещением, швы раствора
                            float rows = 6f, row = Mathf.Floor(v * rows), ry = v * rows - row;
                            float cols = 4f + (row % 2) * 0.5f, cx = u * cols + (row % 2) * 0.5f + Tiled(u, v, 4f) * 0.4f, cf = cx - Mathf.Floor(cx);
                            bool joint = ry < 0.07f || ry > 0.95f || cf < 0.04f || cf > 0.97f;
                            float stone = 0.75f + 0.2f * Hash01(Mathf.Floor(cx), row) + 0.08f * Tiled(u, v, 20f);
                            k = joint ? 0.55f : stone;
                            break;
                        }
                        case DetailKind.Tiles:
                        {
                            // Черепица: ряды с нахлёстом, тень под каждым рядом
                            float rows = 10f, row = Mathf.Floor(v * rows), ry = v * rows - row;
                            float cx = u * 8f + (row % 2) * 0.5f, cf = cx - Mathf.Floor(cx);
                            float shade = Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0, 1, ry * 1.4f));
                            k = shade * (0.82f + 0.16f * Hash01(Mathf.Floor(cx), row)) * (cf < 0.04f ? 0.8f : 1f) + 0.05f * Tiled(u, v, 12f);
                            break;
                        }
                        case DetailKind.Planks:
                        {
                            // Вертикальные доски с щелями и волокнами
                            float bx = u * 7f, bf = bx - Mathf.Floor(bx);
                            float grain = Mathf.Sin((v * 30f + Tiled(u, v, 6f) * 4f) * Mathf.PI) * 0.5f + 0.5f;
                            k = bf < 0.05f ? 0.45f : (0.7f + 0.15f * Hash01(Mathf.Floor(bx), 3) + 0.1f * grain);
                            break;
                        }
                        case DetailKind.Foliage:
                        {
                            // Листва: светлые и тёмные кластеры листьев
                            float leaf = Tiled(u, v, 18f), big = Tiled(u, v, 4f);
                            k = 0.62f + 0.3f * leaf * leaf + 0.12f * big + ((float)rnd.NextDouble() - 0.5f) * 0.08f;
                            break;
                        }
                        case DetailKind.Skin:
                            k = 0.9f + 0.08f * Tiled(u, v, 10f) + ((float)rnd.NextDouble() - 0.5f) * 0.03f;
                            break;
                        default:
                            k = 0.85f + 0.12f * Tiled(u, v, 8f) + ((float)rnd.NextDouble() - 0.5f) * 0.04f;
                            break;
                    }
                    byte b8 = (byte)(Mathf.Clamp01(k) * 255);
                    px[y * N + x] = new Color32(b8, b8, b8, 255);
                }
            t.SetPixels32(px); t.Apply(true, false);
            return _detailTex[kind] = t;
        }

        static float Hash01(float a, float b)
        {
            uint h = (uint)((int)a * 73856093 ^ (int)b * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }

        /// <summary>Деталь с UV в метрах и текстурой по типу поверхности.</summary>
        public static GameObject DetailPart(Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 scale, Color c, Vector3 euler, DetailKind kind, float smooth, float metal)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = MeshDetail.Textured(mesh, scale, DetailTile(kind));
            ScaleMesh(go, scale);
            go.AddComponent<MeshRenderer>().sharedMaterial = DetailMat(c, kind, smooth, metal);
            return go;
        }

        // Масштаб применяется трансформом (меш уже содержит UV в метрах) — так работают анимации деталей по localScale.
        static void ScaleMesh(GameObject go, Vector3 scale) => go.transform.localScale = scale;
    }
}
