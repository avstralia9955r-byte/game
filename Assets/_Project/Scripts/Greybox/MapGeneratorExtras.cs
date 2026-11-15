using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Вторая часть генератора: ориентиры и детали.
    /// Боевая зона: железная дорога со станцией, составом и паровозом (точка D), усадьба с парком (E), ветряная мельница (B),
    /// укреплённая ферма и окопы (A), церковь с кладбищем (C); подбитые танки и грузовики с дымом, противотанковые ежи,
    /// колючая проволока, воронки, стога / поленницы, каменные ограды ферм, телеграфные столбы с проводами, валуны,
    /// колодцы, телеги, ящики, вышки на базах, трава и пшеница (детали террейна).
    /// Вся карта: хутора вдоль дорог, озёра, руины замков на холмах, туннельные порталы, облака.
    /// </summary>
    public static partial class MapGenerator
    {
        public const float RailZ = 205f, RailEnd = 490f;

        static readonly List<(Vector2 c, float r)> _clear = new();
        static readonly List<(Vector2 c, float r, float d)> _craters = new();
        static readonly List<(Vector2 c, float r)> _lakes = new();
        static readonly List<Vector2> _castles = new();
        static Vector2 _mill, _manor, _station;
        static Transform _dyn;

        static bool Blocked(Vector2 p, float margin)
        {
            if (!TrenchMap && Mathf.Abs(p.y - RailZ) < 8 + margin && Mathf.Abs(p.x) < RailEnd) return true;
            foreach (var c in _clear) if (Vector2.Distance(p, c.c) < c.r + margin) return true;
            return false;
        }

        static float RailH(float x) { float s = 0; for (int k = -3; k <= 3; k++) s += H0(x + k * 45f, RailZ); return s / 7f + 0.6f; }

        // ============ План ============

        static void PlanLandmarks(List<Vector2> pts, List<Flatten> flats, List<(Vector2 p, float yaw, Vector3 size, int kind)> houses, bool winter)
        {
            _clear.Clear(); _craters.Clear(); _lakes.Clear(); _castles.Clear();

            // Железная дорога: насыпь со сглаженным профилем
            for (float x = -RailEnd; x <= RailEnd; x += 22f) flats.Add(new Flatten { c = new Vector2(x, RailZ), r = 6.5f, h = RailH(x) });
            _station = new Vector2(pts[3].x, RailZ - 13f);
            flats.Add(new Flatten { c = _station, r = 36, h = RailH(_station.x) }); _clear.Add((_station, 34));
            houses.Add((_station, 0, Vector3.zero, 6));

            _mill = pts[1] + new Vector2(55, -45);
            _manor = pts[4] + new Vector2(80, -20);
            if (!TrenchMap)      // на «окопной» карте поле открытое: без мельницы и усадьбы
            {
                flats.Add(new Flatten { c = _mill, r = 16, h = H0(_mill.x, _mill.y) + 1.5f }); _clear.Add((_mill, 14));
                houses.Add((_mill, 200, Vector3.zero, 5));
                flats.Add(new Flatten { c = _manor, r = 48, h = H0(_manor.x, _manor.y) }); _clear.Add((_manor, 44));
                houses.Add((_manor, Mathf.Atan2(pts[4].x - _manor.x, pts[4].y - _manor.y) * Mathf.Rad2Deg, Vector3.zero, 4));
            }

            foreach (var c in new[] { new Vector2(420, 360), new Vector2(-425, -370) })
            {
                _castles.Add(c); _clear.Add((c, 36));
                flats.Add(new Flatten { c = c, r = 34, h = H0(c.x, c.y) + 2 });
                houses.Add((c, Rnd(0, 360), Vector3.zero, 7));
            }

            // Озёра за пределами боевой зоны
            foreach (var c0 in new[] { new Vector2(420, -400), new Vector2(-410, 400) })
            {
                var c = c0 + new Vector2(Rnd(-30, 30), Rnd(-30, 30));
                float r = Rnd(28, 40);
                if (RoadDist(c) < r + 25 || Mathf.Abs(c.y - RiverZ(c.x)) < r + 40) continue;
                _lakes.Add((c, r)); _clear.Add((c, r + 12));
            }

            // Хутора вдоль дорог по всей карте
            int made = 0;
            for (int i = 2; i < _roads.Count && made < 7; i += 5)
            {
                var s = _roads[i];
                Vector2 mid = (s.a + s.b) * 0.5f, dir = (s.b - s.a).normalized, perp = new(-dir.y, dir.x);
                float edge = Mathf.Max(Mathf.Abs(mid.x), Mathf.Abs(mid.y));
                if (edge < 250 || edge > Zone + 60) continue;
                var center = mid + perp * 38f;
                if (Blocked(center, 45) || Mathf.Abs(center.y - RiverZ(center.x)) < 45 || MinDist(center, pts) < 100) continue;
                int n = 4 + _rng.Next(4);
                for (int k = 0; k < n * 3 && n > 0; k++)
                {
                    float a = Rnd(0, Mathf.PI * 2), r = Rnd(14, 44);
                    var hp = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (RoadDist(hp) < 8 || houses.Exists(h => Vector2.Distance(h.p, hp) < 15)) continue;
                    var size = new Vector3(Rnd(7, 10), Rnd(4, 5.5f), Rnd(8, 12));
                    houses.Add((hp, Rnd(0, 360), size, _rng.NextDouble() < 0.25 ? 1 : 0));
                    flats.Add(new Flatten { c = hp, r = Mathf.Max(size.x, size.z) * 0.8f, h = H0(hp.x, hp.y) });
                    n--;
                }
                _clear.Add((center, 30));
                made++;
            }

            // Воронки в боевой зоне
            for (int k = 0; k < 1500 && _craters.Count < (TrenchMap ? 150 : 45); k++)
            {
                var p = new Vector2(Rnd(-Zone + 30, Zone - 30), Rnd(-Zone + 30, Zone - 30));
                if (Blocked(p, 8) || MinDist(p, pts) < 12 || Mathf.Abs(p.y - RiverZ(p.x)) < 22) continue;
                float r = TrenchMap ? Rnd(3f, 9f) : Rnd(4f, 8f);
                _craters.Add((p, r, r * 0.3f));
            }
        }

        // ============ Рельеф ============

        static void CarveExtras(float[,] hm, List<Vector2> pts)
        {
            foreach (var c in _craters) Bowl(hm, c.c, c.r, c.d, c.d * 0.3f);
            foreach (var l in _lakes) Bowl(hm, l.c, l.r, 5.5f, 0);
            // Дополнительные окопы у B, C, E (перпендикулярно тем, что у A и D)
            foreach (int i in new[] { 1, 2, 4 })
            {
                var c = pts[i];
                Vector2 prev = c + new Vector2(46, -40);
                for (int s = 1; s <= 8; s++)
                {
                    var p = c + new Vector2(46 + ((s & 1) == 0 ? 0 : 6), -40 + s * 10);
                    if (!Blocked(p, 3) && RoadDist(p) > 6) Carve(hm, prev, p, 1.3f, 1.7f);
                    prev = p;
                }
            }
        }

        static void Bowl(float[,] hm, Vector2 c, float r, float depth, float rim)
        {
            float outer = r * 1.6f;
            int x0 = ToIdx(c.x - outer), x1 = ToIdx(c.x + outer), z0 = ToIdx(c.y - outer), z1 = ToIdx(c.y + outer);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Vector2.Distance(new Vector2(-Half + x * Size / (Res - 1), -Half + z * Size / (Res - 1)), c);
                    if (d < r) { float k = d / r; hm[z, x] -= depth * (1 - k * k) / MaxH; }
                    else if (rim > 0 && d < r * 1.5f) hm[z, x] += rim * Mathf.Max(0, 1 - Mathf.Abs(d - r * 1.25f) / (r * 0.25f)) / MaxH;
                }
        }

        static void ExtraSplat(Vector2 w, float[] v, float edge)
        {
            void Mix(int layer, float k) { if (k <= 0) return; for (int i = 0; i < 6; i++) v[i] *= 1 - k; v[layer] += k; }
            if (Mathf.Abs(w.y - RailZ) < 4.5f && Mathf.Abs(w.x) < RailEnd) Mix(4, 0.85f);                       // щебень насыпи
            if (TrenchMap && edge < Zone + 30)   // перепаханная земля: пятна грязи по всей нейтральной полосе
            {
                float m = Mathf.PerlinNoise(w.x * 0.025f + 7.3f, w.y * 0.025f + 3.1f);
                Mix(3, Mathf.Clamp01((m - 0.38f) * 1.8f) * 0.75f);
            }
            if (edge < Zone + 60)
                foreach (var c in _craters)
                {
                    float d = Vector2.Distance(w, c.c);
                    if (d < c.r * 1.5f) { Mix(3, 1 - Mathf.InverseLerp(c.r * 0.7f, c.r * 1.5f, d)); break; }
                }
            foreach (var l in _lakes)
                {
                    float d = Vector2.Distance(w, l.c);
                    if (d < l.r + 14) { Mix(5, 1 - Mathf.InverseLerp(l.r - 6, l.r + 14, d)); break; }
                }
        }

        // ============ Объекты ============

        static void BuildExtras(MapLayout L, Transform structures, Terrain terrain, int layer, bool winter, List<Vector2> pts,
                                Vector2 baseDe, Vector2 baseSu, List<(Vector2 p, float yaw, Vector3 size, int kind)> houses)
        {
            float Y(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0, p.y));
            Vector3 W(Vector2 p, float dy = 0) => new(p.x, Y(p) + dy, p.y);
            bool Free(Vector2 p, float m) => !Blocked(p, m) && MinDist(p, pts) > 30 && Mathf.Abs(p.y - RiverZ(p.x)) > 22 && Vector2.Distance(p, baseDe) > 40 && Vector2.Distance(p, baseSu) > 40;

            // --- Железная дорога: балласт, шпалы, рельсы, туннельные порталы ---
            var rails = new MeshBuilder(); var sleepers = new MeshBuilder(); var ballast = new MeshBuilder();
            int chunk = 0;
            Vector3 prevL = default, prevR = default; bool hasPrev = false;
            for (float x = -RailEnd; x <= RailEnd; x += 13f)
            {
                float y = Y(new Vector2(x, RailZ));
                Vector3 l = new(x, y + 0.36f, RailZ - 0.75f), r = new(x, y + 0.36f, RailZ + 0.75f);
                if (hasPrev)
                {
                    rails.Beam(prevL, l, 0.08f, 0.14f); rails.Beam(prevR, r, 0.08f, 0.14f);
                    Vector3 pm = (prevL + prevR) * 0.5f, cm = (l + r) * 0.5f;
                    ballast.Beam(pm + Vector3.down * 0.26f, cm + Vector3.down * 0.26f, 3.8f, 0.3f);
                    for (int k = 0; k < 6; k++) { var sp = Vector3.Lerp(pm, cm, (k + 0.5f) / 6f); sleepers.Box(sp + Vector3.down * 0.1f, new Vector3(0.26f, 0.14f, 2.5f)); }
                }
                prevL = l; prevR = r; hasPrev = true;
                if (sleepers.VertexCount > 40000)
                {
                    rails.Build(structures, "Rails" + chunk, new Color(0.35f, 0.33f, 0.32f), 0.5f, 0.8f);
                    sleepers.Build(structures, "Sleepers" + chunk, new Color(0.26f, 0.19f, 0.13f));
                    ballast.Build(structures, "Ballast" + chunk, winter ? new Color(0.7f, 0.7f, 0.72f) : new Color(0.42f, 0.4f, 0.38f));
                    chunk++;
                }
            }
            rails.Build(structures, "Rails" + chunk, new Color(0.35f, 0.33f, 0.32f), 0.5f, 0.8f);
            sleepers.Build(structures, "Sleepers" + chunk, new Color(0.26f, 0.19f, 0.13f));
            ballast.Build(structures, "Ballast" + chunk, winter ? new Color(0.7f, 0.7f, 0.72f) : new Color(0.42f, 0.4f, 0.38f));
            foreach (float ex in new[] { -RailEnd, RailEnd })
            {
                var p = W(new Vector2(ex, RailZ));
                Color st = new(0.5f, 0.49f, 0.46f);
                ProceduralModels.Box(structures, "PortalL", p + new Vector3(0, 3.5f, -3.4f), new Vector3(3, 7, 1.6f), st, true).layer = layer;
                ProceduralModels.Box(structures, "PortalR", p + new Vector3(0, 3.5f, 3.4f), new Vector3(3, 7, 1.6f), st, true).layer = layer;
                ProceduralModels.Box(structures, "PortalTop", p + new Vector3(0, 7.6f, 0), new Vector3(3.2f, 1.6f, 8.4f), st, true).layer = layer;
                ProceduralModels.Box(structures, "PortalDark", p + new Vector3(Mathf.Sign(ex) * 1.2f, 3.3f, 0), new Vector3(1, 6.6f, 5.2f), new Color(0.03f, 0.03f, 0.03f));
            }

            // --- Состав у станции и брошенные вагоны дальше по линии ---
            float sx = _station.x;
            Vector3 OnRail(float x) => W(new Vector2(x, RailZ), 0.42f);
            MapProps.Locomotive(structures, OnRail(sx + 18), winter, layer);
            MapProps.Wagon(structures, OnRail(sx - 2), 0, winter, layer);
            MapProps.Wagon(structures, OnRail(sx - 12.5f), 1, winter, layer);
            MapProps.Wagon(structures, OnRail(sx - 23), 2, winter, layer);
            MapProps.Wagon(structures, OnRail(520), 0, winter, layer);
            MapProps.Wagon(structures, OnRail(531), 0, winter, layer);
            MapProps.Wagon(structures, OnRail(-1400), 1, winter, layer);

            // --- Подбитая техника ---
            int wrecks = 0;
            for (int k = 0; k < 600 && wrecks < 10; k++)
            {
                var p = new Vector2(Rnd(-Zone + 60, Zone - 60), Rnd(-Zone + 60, Zone - 60));
                float rd = RoadDist(p);
                if (rd < 5 || rd > 32 || !Free(p, 10)) continue;
                _clear.Add((p, 6));
                if (wrecks % 3 == 2) MapProps.Truck(structures, W(p), Rnd(0, 360), wrecks % 2 == 0, winter, layer);
                else
                {
                    MapProps.Tank(structures, W(p), Rnd(0, 360), wrecks % 2 == 0, winter, layer, _rng);
                    if (wrecks < 5) MapProps.SmokeColumn(_dyn, W(p, 2.4f));
                }
                wrecks++;
            }

            // --- Противотанковые ежи поперёк дорог ---
            int clusters = 0;
            for (int i = 3; i < _roads.Count && clusters < 14; i += 5)
            {
                var s = _roads[i];
                Vector2 mid = (s.a + s.b) * 0.5f, dir = (s.b - s.a).normalized, perp = new(-dir.y, dir.x);
                if (Mathf.Max(Mathf.Abs(mid.x), Mathf.Abs(mid.y)) > Zone - 40 || MinDist(mid, pts) < 50 || !Free(mid, 6)) continue;
                for (int j = -3; j <= 3; j++)
                {
                    if (j == 0 || j == 1) continue;   // проезд
                    var hp = mid + perp * (j * 3.1f) + dir * Rnd(-1.5f, 1.5f);
                    MapProps.Hedgehog(structures, W(hp), Rnd(0, 360), layer);
                }
                clusters++;
            }

            // --- Колючая проволока дугами вокруг точек ---
            var wire = new MeshBuilder(); var posts = new MeshBuilder();
            for (int i = 0; i < pts.Count; i++)
                for (int arc = 0; arc < 2; arc++)
                {
                    float a0 = i * 1.3f + arc * Mathf.PI, R = 36f;
                    Vector3 prev = default; bool has = false;
                    for (float a = a0; a < a0 + 1.5f; a += 3.6f / R)
                    {
                        var p2 = pts[i] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;
                        if (RoadDist(p2) < 6 || Blocked(p2, 3)) { has = false; continue; }
                        var p = W(p2);
                        posts.Box(p + Vector3.up * 0.65f, new Vector3(0.1f, 1.3f, 0.1f), Quaternion.Euler(0, a * 57f, 6));
                        if (has) for (int k = 0; k < 3; k++) { wire.Beam(prev + Vector3.up * (0.35f + k * 0.4f), p + Vector3.up * (0.35f + k * 0.4f), 0.025f, 0.025f); wire.Beam(prev + Vector3.up * (0.35f + k * 0.4f), p + Vector3.up * (0.35f + ((k + 1) % 3) * 0.4f), 0.02f, 0.02f); }
                        prev = p; has = true;
                    }
                }
            posts.Build(structures, "WirePosts", new Color(0.3f, 0.22f, 0.15f));
            wire.Build(structures, "BarbedWire", new Color(0.22f, 0.2f, 0.19f), 0.3f, 0.6f, false);

            // --- Стога (лето) / поленницы (зима) ---
            int stacks = 0;
            for (int k = 0; k < 900 && stacks < 55; k++)
            {
                var p = new Vector2(Rnd(-Zone + 40, Zone - 40), Rnd(-Zone + 40, Zone - 40));
                int cx = Mathf.FloorToInt((p.x + Zone) / FieldCell), cz = Mathf.FloorToInt((p.y + Zone) / FieldCell);
                if (Hash(cx, cz, 7) >= 0.33f || RoadDist(p) < 9 || !Free(p, 6)) continue;
                if (winter) { if (stacks % 2 == 0) MapProps.LogPile(structures, W(p), Rnd(0, 180), layer); else MapProps.Haystack(structures, W(p), true, layer); }
                else MapProps.Haystack(structures, W(p), false, layer);
                stacks++;
            }

            // --- Каменные ограды у амбаров (внутри зоны) ---
            Color stone = winter ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.58f, 0.56f, 0.5f);
            foreach (var h in houses)
            {
                if (h.kind != 1 || Mathf.Max(Mathf.Abs(h.p.x), Mathf.Abs(h.p.y)) > Zone) continue;
                var rot = Quaternion.Euler(0, h.yaw, 0);
                Vector3 c = W(h.p);
                foreach (var (off, size) in new[] { (new Vector3(-11, 0.55f, 0), new Vector3(0.7f, 1.1f, 18)), (new Vector3(-2, 0.55f, -11), new Vector3(17, 1.1f, 0.7f)), (new Vector3(8, 0.55f, 11), new Vector3(9, 1.1f, 0.7f)) })
                {
                    Vector3 wp = c + rot * off; wp.y = terrain.SampleHeight(wp) + 0.5f;
                    if (RoadDist(new Vector2(wp.x, wp.z)) < 6) continue;
                    ProceduralModels.Box(structures, "FarmWall", wp, size, stone, true, new Vector3(0, h.yaw, 0)).layer = layer;
                }
            }

            // --- Телеграфные столбы с проводами вдоль дорог ---
            var poles = new MeshBuilder(); var lines = new MeshBuilder();
            Vector3 lastTop = default; bool lastOk = false;
            for (int i = 0; i < _roads.Count; i += 1)
            {
                var s = _roads[i];
                if (Mathf.Max(Mathf.Abs(s.a.x), Mathf.Abs(s.a.y)) > Half - 40) { lastOk = false; continue; }
                Vector2 dir = (s.b - s.a).normalized, perp = new(-dir.y, dir.x);
                var p2 = s.a + perp * 6f;
                if (Blocked(p2, 2) || Mathf.Abs(p2.y - RiverZ(p2.x)) < 16) { lastOk = false; continue; }
                var p = W(p2);
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                poles.Box(p + Vector3.up * 3.7f, new Vector3(0.2f, 7.4f, 0.2f));
                poles.Box(p + Vector3.up * 6.9f, new Vector3(1.7f, 0.12f, 0.12f), Quaternion.Euler(0, yaw, 0));
                poles.Box(p + Vector3.up * 6.4f, new Vector3(1.2f, 0.1f, 0.1f), Quaternion.Euler(0, yaw, 0));
                Vector3 top = p + Vector3.up * 7f;
                if (lastOk && Vector3.Distance(lastTop, top) < 110)
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Vector3 o = Quaternion.Euler(0, yaw, 0) * new Vector3(k * 0.7f, 0, 0);
                        Vector3 m = (lastTop + top) * 0.5f + o + Vector3.down * 0.9f;   // провис
                        lines.Beam(lastTop + o, m, 0.03f, 0.03f); lines.Beam(m, top + o, 0.03f, 0.03f);
                    }
                lastTop = top; lastOk = true;
            }
            poles.Build(structures, "TelegraphPoles", new Color(0.3f, 0.23f, 0.17f));
            lines.Build(structures, "TelegraphWires", new Color(0.08f, 0.08f, 0.08f), 0.2f, 0, false);

            // --- Валуны ---
            for (int k = 0; k < 70; k++)
            {
                var p = new Vector2(Rnd(-Zone, Zone), Rnd(-Zone, Zone));
                if (RoadDist(p) < 6 || !Free(p, 4)) continue;
                MapProps.Rock(structures, W(p), Rnd(0.7f, 2.4f), _rng, layer, winter);
            }
            for (int k = 0, n = 0; k < 3000 && n < 140; k++)
            {
                var p = new Vector2(Rnd(-Half + 20, Half - 20), Rnd(-Half + 20, Half - 20));
                if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) < Zone - 40) continue;
                if (terrain.terrainData.GetSteepness((p.x + Half) / Size, (p.y + Half) / Size) < 16 || Blocked(p, 4)) continue;
                MapProps.Rock(structures, W(p), Rnd(1.6f, 5f), _rng, layer, winter); n++;
            }

            // --- Деревенская утварь, кладбище, вышки ---
            for (int i = 0; i < pts.Count; i++)
            {
                if (i != 2) MapProps.Well(structures, W(pts[i] + new Vector2(-9, 8)), winter);
                MapProps.Cart(structures, W(pts[i] + new Vector2(10, -7)), i * 70, layer);
                for (int k = 0; k < 5; k++)
                {
                    float a = Rnd(0, 6.28f); var p = pts[i] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rnd(17, 27);
                    if (RoadDist(p) > 5 && !Blocked(p, 2)) MapProps.Crates(structures, W(p), _rng, layer);
                }
            }
            var crosses = new MeshBuilder();
            for (int k = 0; k < 18; k++)
            {
                var p = W(pts[2] + new Vector2(16 + (k % 6) * 3.2f, 34 + (k / 6) * 5f));
                crosses.Box(p + Vector3.up * 0.7f, new Vector3(0.14f, 1.4f, 0.1f)); crosses.Box(p + Vector3.up * 1.0f, new Vector3(0.7f, 0.14f, 0.1f));
                crosses.Box(p + Vector3.up * 0.08f, new Vector3(0.9f, 0.16f, 1.8f));
            }
            crosses.Build(structures, "Cemetery", new Color(0.62f, 0.61f, 0.58f));
            MapProps.Watchtower(structures, W(baseDe + new Vector2(16, 10)), 180, winter, layer);
            MapProps.Watchtower(structures, W(baseSu + new Vector2(-16, -10)), 0, winter, layer);

            // --- Озёра ---
            foreach (var l in _lakes)
            {
                var c = W(l.c);
                float bank = terrain.SampleHeight(new Vector3(l.c.x + l.r * 1.2f, 0, l.c.y));
                ProceduralModels.Part(L.root.transform, winter ? "FrozenLake" : "Lake", MeshUtil.Cylinder(24), new Vector3(l.c.x, bank - 1.6f, l.c.y),
                    new Vector3(l.r * 2.1f, 0.3f, l.r * 2.1f), winter ? new Color(0.72f, 0.84f, 0.92f) : new Color(0.15f, 0.28f, 0.34f), default, false, winter ? 0.75f : 0.92f);
            }

            // --- Облака ---
            var clouds = new GameObject("Clouds").transform; clouds.SetParent(L.root.transform);
            var cmat = ProceduralModels.Mat(winter ? new Color(0.83f, 0.85f, 0.88f) : new Color(0.98f, 0.98f, 1f), 0f);
            for (int k = 0; k < 26; k++) MapProps.Cloud(clouds, new Vector3(Rnd(-2400, 2400), Rnd(320, 520), Rnd(-2400, 2400)), _rng, cmat);
            clouds.gameObject.AddComponent<CloudDrift>().limit = 2600f;
        }

        // ============ Трава и посевы (детали террейна) ============

        static void Details(Terrain terrain, bool winter, List<Vector2> pts)
        {
            try
            {
                var td = terrain.terrainData;
                const int res = 512;      // ~2 м на ячейку: плотность травы прежняя, экземпляров вчетверо меньше
                td.SetDetailResolution(res, 32);
                td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
                Color grassC = winter ? new Color(0.6f, 0.58f, 0.45f) : new Color(0.3f, 0.5f, 0.2f);
                Color cropC = winter ? new Color(0.7f, 0.66f, 0.5f) : new Color(0.8f, 0.68f, 0.3f);
                td.detailPrototypes = new[]
                {
                    Detail(Tuft("GrassTuft", 5, 0.5f, 0.35f, grassC), 0.8f, 1.5f),
                    Detail(Tuft("WheatTuft", 6, 0.95f, 0.3f, cropC), 0.9f, 1.3f),
                    // Полевые цветы: маки и ромашки пятнами по лугам (зимой — сухие стебли)
                    Detail(Tuft("Poppies", 4, 0.42f, 0.12f, winter ? new Color(0.55f, 0.5f, 0.4f) : new Color(0.85f, 0.16f, 0.1f)), 0.8f, 1.2f),
                    Detail(Tuft("Daisies", 5, 0.3f, 0.12f, winter ? new Color(0.62f, 0.6f, 0.52f) : new Color(0.95f, 0.93f, 0.78f)), 0.8f, 1.2f),
                };
                var grass = new int[res, res]; var crop = new int[res, res];
                var poppy = new int[res, res]; var daisy = new int[res, res];
                float step = Size / res;
                int i0 = 0, i1 = res;
                for (int z = i0; z < i1; z++)
                    for (int x = i0; x < i1; x++)
                    {
                        var w = new Vector2(-Half + (x + 0.5f) * step, -Half + (z + 0.5f) * step);
                        if (RoadDist(w) < 5.5f || MinDist(w, pts) < 24 || Blocked(w, 1) || Mathf.Abs(w.y - RiverZ(w.x)) < 13) continue;
                        int cx = Mathf.FloorToInt((w.x + Zone) / FieldCell), cz = Mathf.FloorToInt((w.y + Zone) / FieldCell);
                        float h = Hash(cx, cz, 7);
                        if (h < 0.33f) crop[z, x] = winter ? 1 : 5;             // пшеница (зимой — стерня)
                        else
                        {
                            grass[z, x] = winter ? (Hash(x, z, 9) < 0.3f ? 1 : 0) : (h < 0.66f ? 3 : 2);
                            // Цветы пятнами (шум Перлина), реже у дорог и деревень
                            float fl = Mathf.PerlinNoise(w.x * 0.035f + 17f, w.y * 0.035f + 5f);
                            if (!winter && fl > 0.62f) { if (Hash(x, z, 31) < 0.5f) poppy[z, x] = 1 + (fl > 0.72f ? 1 : 0); else daisy[z, x] = 1; }
                            else if (!winter && Hash(x, z, 37) < 0.03f) daisy[z, x] = 1;
                        }
                    }
                td.SetDetailLayer(0, 0, 0, grass);
                td.SetDetailLayer(0, 0, 1, crop);
                td.SetDetailLayer(0, 0, 2, poppy);
                td.SetDetailLayer(0, 0, 3, daisy);
                terrain.detailObjectDistance = 110; terrain.detailObjectDensity = 1f;
            }
            catch (System.Exception e) { Debug.LogWarning("[Map] Детали травы пропущены: " + e.Message); }
        }

        static DetailPrototype Detail(GameObject prefab, float min, float max) => new()
        {
            usePrototypeMesh = true, prototype = prefab, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
            minWidth = min, maxWidth = max, minHeight = min, maxHeight = max, healthyColor = Color.white, dryColor = new Color(0.86f, 0.86f, 0.72f), noiseSpread = 0.3f
        };

        /// <summary>Пучок травы: несколько тонких наклонённых трёхгранных «стеблей».</summary>
        static GameObject Tuft(string name, int blades, float height, float spread, Color c)
        {
            var mb = new List<CombineInstance>();
            var cone = MeshUtil.Cone(3);
            for (int i = 0; i < blades; i++)
            {
                float a = i * Mathf.PI * 2 / blades + i;
                var pos = new Vector3(Mathf.Cos(a) * spread * 0.5f, 0, Mathf.Sin(a) * spread * 0.5f);
                var rot = Quaternion.Euler(Mathf.Sin(a) * 18f, a * 57f, Mathf.Cos(a) * 18f);
                mb.Add(new CombineInstance { mesh = cone, transform = Matrix4x4.TRS(pos, rot, new Vector3(0.07f, height * (0.7f + 0.3f * ((i * 37) % 10) / 10f), 0.07f)) });
            }
            var mesh = new Mesh { name = name }; mesh.CombineMeshes(mb.ToArray(), true, true); mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(ProceduralModels.Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = ProceduralModels.Mat(c, 0.05f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
