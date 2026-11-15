using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Третья карта «Ржев: окопы». Поверх обычного рельефа прорезаются сплошные линии траншей:
    /// по три линии обороны у каждой стороны (ломаные, с огневыми ячейками-уступами), между линиями — ходы сообщения,
    /// в центре — перепаханная воронками нейтральная полоса с рекой. В линиях через каждые ~50 м оставлены переходы
    /// (по ним проходят боты, танки и джипы), у дорог, точек захвата, домов и реки траншеи прерываются.
    /// Обстановка: бруствер из мешков с песком (укрытие, с коллизией) на стороне противника, дощатые стенки и настил,
    /// блиндажи из брёвен, пулемётные гнёзда, колючая проволока в 16 м перед каждой линией.
    /// </summary>
    public static partial class MapGenerator
    {
        /// <summary>Задаётся перед Generate (GameFlow): карта с окопами.</summary>
        public static bool TrenchMap;

        // a, b — концы отрезка; front — направление на противника (0 — ход сообщения без бруствера)
        static readonly List<(Vector2 a, Vector2 b, float front)> _trench = new();
        static readonly List<(Vector2 p, float yaw)> _dugouts = new();
        static readonly List<(Vector2 a, Vector2 b)> _wireLines = new();
        const float TrenchHalf = 1.15f, TrenchDepth = 1.9f;

        static void PlanTrenches(List<Vector2> pts, List<(Vector2 p, float yaw, Vector3 size, int kind)> houses, Vector2 baseDe, Vector2 baseSu)
        {
            _trench.Clear(); _dugouts.Clear(); _wireLines.Clear();

            bool Bad(Vector2 p)
            {
                if (Mathf.Abs(p.x) > Zone - 12 || Mathf.Abs(p.y) > Zone - 12) return true;
                if (MinDist(p, pts) < 24 || Vector2.Distance(p, baseDe) < 62 || Vector2.Distance(p, baseSu) < 62) return true;
                if ((_river > 0 && Mathf.Abs(p.y - RiverZ(p.x)) < 22) || RoadDist(p) < 5.5f || Blocked(p, 3)) return true;
                foreach (var h in houses) if (Vector2.Distance(h.p, p) < Mathf.Max(h.size.x, h.size.z) * 0.7f + 6f) return true;
                return false;
            }

            // Линии обороны: СССР на юге (смотрят на север), Германия на севере (смотрят на юг)
            float[] lines = { -255f, -175f, -105f, 100f, 172f, 250f };
            for (int li = 0; li < lines.Length; li++)
            {
                float z0 = lines[li], front = z0 < 0 ? 1f : -1f;
                float x = -Zone + 14f;
                Vector2 prev = new(x, z0);
                int s = 0, run = 0;
                Vector2 wirePrev = default; bool wireHas = false;
                while (x < Zone - 14f)
                {
                    x += Rnd(7.5f, 10f);
                    float wig = 9f * Mathf.Sin(x / 70f + li * 1.7f) + 4f * Mathf.Sin(x / 23f + li);
                    var p = new Vector2(x, z0 + wig + ((s & 1) == 0 ? 0f : 3.8f * front));   // уступы огневых ячеек к противнику
                    var mid = (prev + p) * 0.5f;
                    bool crossing = run >= 5 && _rng.NextDouble() < 0.45;           // переход через траншею
                    if (crossing || Bad(prev) || Bad(p) || Bad(mid)) run = 0;
                    else
                    {
                        _trench.Add((prev, p, front)); run++;
                        if (_rng.NextDouble() < 0.07) _dugouts.Add((mid - new Vector2(0, front) * 6.5f, front > 0 ? 0f : 180f));
                    }
                    // Колючая проволока перед линией (с проходами)
                    var wp = new Vector2(x, z0 + wig * 0.6f + front * 16f);
                    bool wireOk = !Bad(wp) && (s % 9) != 4;
                    if (wireOk && wireHas) _wireLines.Add((wirePrev, wp));
                    wirePrev = wp; wireHas = wireOk;
                    prev = p; s++;
                }
            }

            // Ходы сообщения между линиями одной стороны (зигзаг вдоль оси наступления)
            float[] xs = { -280f, -150f, -40f, 70f, 185f, 300f };
            foreach (var pair in new[] { (-255f, -175f), (-175f, -105f), (100f, 172f), (172f, 250f) })
                foreach (float cx in xs)
                {
                    float x0 = cx + Rnd(-25f, 25f);
                    float za = Mathf.Min(pair.Item1, pair.Item2) + 3f, zb = Mathf.Max(pair.Item1, pair.Item2) - 3f;
                    Vector2 prev = new(x0, za);
                    int s = 0;
                    for (float z = za + 8f; z <= zb; z += 8f, s++)
                    {
                        var p = new Vector2(x0 + ((s & 1) == 0 ? 3.5f : -3.5f), z);
                        if (!Bad(prev) && !Bad(p)) _trench.Add((prev, p, 0f));
                        prev = p;
                    }
                }
        }

        static void CarveTrenches(float[,] hm)
        {
            // Мягкий профиль: ровное дно шириной ~2,3 м и крутые стенки; на стыках отрезков берём максимум глубины
            float outer = TrenchHalf + 1.4f;
            var dig = new float[Res, Res];
            foreach (var t in _trench)
            {
                int x0 = ToIdx(Mathf.Min(t.a.x, t.b.x) - outer - 1), x1 = ToIdx(Mathf.Max(t.a.x, t.b.x) + outer + 1);
                int z0 = ToIdx(Mathf.Min(t.a.y, t.b.y) - outer - 1), z1 = ToIdx(Mathf.Max(t.a.y, t.b.y) + outer + 1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var w = new Vector2(-Half + x * Size / (Res - 1), -Half + z * Size / (Res - 1));
                        float d = SegDist(w, t.a, t.b);
                        if (d >= outer) continue;
                        float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TrenchHalf, outer, d));
                        dig[z, x] = Mathf.Max(dig[z, x], TrenchDepth * k / MaxH);
                    }
            }
            for (int z = 0; z < Res; z++)
                for (int x = 0; x < Res; x++)
                    if (dig[z, x] > 0) hm[z, x] = Mathf.Max(0f, hm[z, x] - dig[z, x]);
        }

        static void TrenchSplat(float[,,] a)
        {
            float step = Size / (SplatRes - 1);
            int Idx(float w) => Mathf.Clamp(Mathf.RoundToInt((w + Half) / step), 0, SplatRes - 1);
            void Paint(Vector2 pa, Vector2 pb, float r)
            {
                int x0 = Idx(Mathf.Min(pa.x, pb.x) - r), x1 = Idx(Mathf.Max(pa.x, pb.x) + r);
                int z0 = Idx(Mathf.Min(pa.y, pb.y) - r), z1 = Idx(Mathf.Max(pa.y, pb.y) + r);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var w = new Vector2(-Half + x * step, -Half + z * step);
                        float d = SegDist(w, pa, pb);
                        if (d >= r) continue;
                        float k = 1f - Mathf.InverseLerp(r * 0.55f, r, d);
                        for (int i = 0; i < 6; i++) a[z, x, i] *= 1f - k;
                        a[z, x, 3] += k;
                    }
            }
            foreach (var t in _trench) Paint(t.a, t.b, TrenchHalf + 3.2f);       // свежая земля бруствера и дна
            foreach (var w in _wireLines) Paint(w.a, w.b, 1.6f);
        }

        static void BuildTrenches(Transform structures, Terrain terrain, int layer)
        {
            float Y(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0, p.y));
            Color sand = new(0.5f, 0.45f, 0.33f), sandDark = new(0.42f, 0.38f, 0.28f), logs = new(0.3f, 0.23f, 0.16f);
            var planks = new MeshBuilder(); var boards = new MeshBuilder(); var stakes = new MeshBuilder();
            int bags = 0, nests = 0;
            foreach (var t in _trench)
            {
                Vector2 mid = (t.a + t.b) * 0.5f, dir = t.b - t.a;
                float len = dir.magnitude; if (len < 0.5f) continue;
                dir /= len;
                Vector2 perp = new(-dir.y, dir.x);
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0, yaw, 0);
                float bottom = Y(mid);
                // Настил по дну и дощатые стенки с кольями
                boards.Box(new Vector3(mid.x, bottom + 0.05f, mid.y), new Vector3(0.9f, 0.06f, len + 0.2f), rot);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector2 wp = mid + perp * sgn * (TrenchHalf + 0.05f);
                    planks.Box(new Vector3(wp.x, bottom + 0.85f, wp.y), new Vector3(0.07f, 1.5f, len), rot * Quaternion.Euler(0, 0, sgn * 6f));
                    for (float u = -len * 0.5f + 0.4f; u < len * 0.5f; u += 2.2f)
                    {
                        Vector2 sp = wp + dir * u;
                        stakes.Box(new Vector3(sp.x, bottom + 0.95f, sp.y), new Vector3(0.12f, 1.9f, 0.12f), rot);
                    }
                }
                if (t.front == 0f) continue;
                // Бруствер из мешков с песком на стороне противника — укрытие для стрельбы стоя из траншеи
                Vector2 frontDir = new(0, t.front);
                float side = Vector2.Dot(perp, frontDir) >= 0 ? 1f : -1f;
                Vector2 bp = mid + perp * side * (TrenchHalf + 1.7f);
                if (_rng.NextDouble() < 0.7)
                {
                    var g = Y(bp);
                    var sb = ProceduralModels.Box(structures, "Parapet", new Vector3(bp.x, g + 0.3f, bp.y), new Vector3(0.9f, 0.62f, len * 0.95f),
                                                  (bags & 1) == 0 ? sand : sandDark, true, new Vector3(0, yaw, 0));
                    sb.layer = layer; bags++;
                }
                // Пулемётное гнездо: подкова из мешков
                if (nests < 40 && _rng.NextDouble() < 0.05)
                {
                    Vector2 np = mid + perp * side * (TrenchHalf + 2.9f);
                    float g = Y(np);
                    for (int k = -1; k <= 1; k++)
                    {
                        float ay = yaw + (side > 0 ? 90f : -90f) + k * 50f;
                        var off = Quaternion.Euler(0, ay, 0) * Vector3.forward * 1.6f;
                        ProceduralModels.Box(structures, "MgNest", new Vector3(np.x, g + 0.45f, np.y) + off, new Vector3(1.9f, 0.9f, 0.8f), sand, true, new Vector3(0, ay, 0)).layer = layer;
                    }
                    nests++;
                }
            }
            planks.Build(structures, "TrenchPlanks", new Color(0.36f, 0.28f, 0.19f));
            boards.Build(structures, "TrenchBoards", new Color(0.33f, 0.26f, 0.18f));
            stakes.Build(structures, "TrenchStakes", new Color(0.28f, 0.21f, 0.14f));

            // Блиндажи: бревенчатый накат, присыпанный землёй, вход со стороны траншеи
            foreach (var d in _dugouts)
            {
                var root = new GameObject("Dugout").transform;
                root.SetParent(structures);
                root.SetPositionAndRotation(new Vector3(d.p.x, Y(d.p), d.p.y), Quaternion.Euler(0, d.yaw, 0));
                for (int i = 0; i < 6; i++)
                {
                    var lg = ProceduralModels.Box(root, "Log", new Vector3(0, 0.95f, -1.9f + i * 0.72f), new Vector3(4.4f, 0.32f, 0.34f), logs, true);
                    lg.layer = layer;
                }
                ProceduralModels.Box(root, "Earth", new Vector3(0, 1.3f, 0), new Vector3(4.8f, 0.5f, 4.6f), new Color(0.3f, 0.26f, 0.19f), true).layer = layer;
                ProceduralModels.Box(root, "WallL", new Vector3(-2.1f, 0.45f, 0), new Vector3(0.35f, 0.9f, 4.2f), logs, true).layer = layer;
                ProceduralModels.Box(root, "WallR", new Vector3(2.1f, 0.45f, 0), new Vector3(0.35f, 0.9f, 4.2f), logs, true).layer = layer;
                ProceduralModels.Box(root, "Door", new Vector3(0, 0.45f, 2.15f), new Vector3(1.1f, 0.9f, 0.05f), new Color(0.04f, 0.035f, 0.03f));
            }

            // Колючая проволока: колья и три нитки, крест-накрест
            var wire = new MeshBuilder(); var posts = new MeshBuilder();
            foreach (var w in _wireLines)
            {
                Vector3 a = new(w.a.x, Y(w.a), w.a.y), b = new(w.b.x, Y(w.b), w.b.y);
                posts.Box(a + Vector3.up * 0.6f, new Vector3(0.09f, 1.2f, 0.09f), Quaternion.Euler(Rnd(-8, 8), 0, Rnd(-8, 8)));
                for (int k = 0; k < 3; k++)
                {
                    wire.Beam(a + Vector3.up * (0.3f + k * 0.38f), b + Vector3.up * (0.3f + k * 0.38f), 0.022f, 0.022f);
                    wire.Beam(a + Vector3.up * (0.3f + k * 0.38f), b + Vector3.up * (0.3f + ((k + 1) % 3) * 0.38f), 0.018f, 0.018f);
                }
            }
            posts.Build(structures, "TrenchWirePosts", new Color(0.27f, 0.2f, 0.14f));
            wire.Build(structures, "TrenchWire", new Color(0.2f, 0.19f, 0.18f), 0.3f, 0.6f, false);
        }
    }
}
