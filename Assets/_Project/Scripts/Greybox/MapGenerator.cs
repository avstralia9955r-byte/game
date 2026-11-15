using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Frontline
{
    public class MapLayout
    {
        public GameObject root;
        public Terrain terrain;
        public Vector3[] points;
        public Vector3[] spawnsDe, spawnsSu;
        public float zoneHalf;
        public NavMeshSurface navSurface;
    }

    /// <summary>
    /// Процедурная карта 2×2 км на Unity Terrain.
    /// • Вся территория: рельеф (равнина в центре → холмы → горный хребет по краям), река через всю карту,
    ///   дороги к краям, леса (до ~25 тыс. деревьев), туман на горизонте.
    /// • Боевая зона ~1,5×1,5 км (долина; по краю — лесистый хребет как естественная граница): 5 деревень у точек A–E, церковь на C,
    ///   нормандский бокаж (поля с живыми изгородями и проходами), каменные фермы, окопы, мосты, базы фракций.
    /// • Зима (Арденны): снег, ели, бункеры, замёрзшая река (лёд — проходимая поверхность), снег на крышах.
    /// Генерация детерминирована по seed.
    /// </summary>
    public static partial class MapGenerator
    {
        // Карта 1×1 км (боевая зона 760×760 м) — вдвое меньше прежней, бои плотнее, как в Polyfield
        public const float Size = 1000f, Half = 500f, MaxH = 220f, Zone = 380f, FieldCell = 80f;
        const int Res = 1025, SplatRes = 1024;

        struct Flatten { public Vector2 c; public float r, h; }
        struct Seg { public Vector2 a, b; }

        static System.Random _rng;
        static float _phase;
        static List<Seg> _roads;
        static Dictionary<long, List<int>> _roadGrid;

        public static MapLayout Generate(MapTheme theme, int seed, int worldLayer)
        {
            _rng = new System.Random(seed);
            _phase = seed * 0.37f;
            _flat = TrenchMap ? 0.22f : 1f;
            _river = 1f;
            if (TrenchMap) return GenerateTrenchField(theme, seed, worldLayer);     // «Ржев: окопы» — отдельный генератор
            bool winter = theme == MapTheme.Winter;
            ProceduralModels.Theme = theme;
            var L = new MapLayout { zoneHalf = Zone, root = new GameObject(TrenchMap ? "[Map] Ржев: окопы 1×1 км" : winter ? "[Map] Арденны 1×1 км" : "[Map] Нормандия 1×1 км") };

            // ---------- 1. План: точки, базы, дороги, дома ----------
            var pts = new List<Vector2> { new(-205, -120), new(200, -150), new(0, 10), new(-190, 145), new(205, 125) };
            for (int i = 0; i < pts.Count; i++)
            {
                float rz = RiverZ(pts[i].x);
                if (Mathf.Abs(pts[i].y - rz) < 55) pts[i] = new Vector2(pts[i].x, rz + Mathf.Sign(pts[i].y - rz + 0.01f) * 62);
            }
            var baseDe = new Vector2(15, 330); var baseSu = new Vector2(-15, -330);

            _roads = new List<Seg>();
            void Road(Vector2 a, Vector2 b)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) / 70f));
                Vector2 dir = (b - a).normalized, perp = new(-dir.y, dir.x), prev = a;
                for (int i = 1; i <= n; i++)
                {
                    float t = i / (float)n;
                    Vector2 p = Vector2.Lerp(a, b, t) + perp * (i < n ? (float)(_rng.NextDouble() - 0.5) * 30f * Mathf.Sin(t * Mathf.PI) : 0);
                    _roads.Add(new Seg { a = prev, b = p }); prev = p;
                }
            }
            Road(baseSu, pts[0]); Road(baseSu, pts[1]); Road(pts[0], pts[2]); Road(pts[1], pts[2]);
            Road(pts[2], pts[3]); Road(pts[2], pts[4]); Road(pts[3], baseDe); Road(pts[4], baseDe);
            Road(pts[0], pts[3]); Road(pts[1], pts[4]);
            Road(baseDe, new Vector2(60, Half)); Road(baseSu, new Vector2(-75, -Half));
            Road(pts[4], new Vector2(Half, 165)); Road(pts[0], new Vector2(-Half, -165));
            Road(pts[1], new Vector2(Half, -280)); Road(pts[3], new Vector2(-Half, 260));
            BuildRoadGrid();

            var flats = new List<Flatten>();
            var houses = new List<(Vector2 p, float yaw, Vector3 size, int kind)>(); // kind: 0 дом, 1 амбар, 2 церковь, 3 бункер, 4 усадьба, 5 мельница, 6 вокзал, 7 замок
            PlanLandmarks(pts, flats, houses, winter);
            for (int i = 0; i < pts.Count; i++)
            {
                var c = pts[i];
                flats.Add(new Flatten { c = c, r = 28, h = H0(c.x, c.y) });
                if (i == 2) // церковь на центральной точке
                {
                    var cp = c + new Vector2(0, 42);
                    houses.Add((cp, 0, new Vector3(11, 10, 26), 2));
                    flats.Add(new Flatten { c = cp, r = 24, h = H0(cp.x, cp.y) });
                }
                int count = TrenchMap ? 3 + _rng.Next(2) : 8 + _rng.Next(4);   // на «окопной» карте от деревень остались хутора-руины
                for (int k = 0; k < count * 3 && count > 0; k++)
                {
                    float a = (float)_rng.NextDouble() * Mathf.PI * 2, r = 32 + (float)_rng.NextDouble() * 38;
                    var hp = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (RoadDist(hp) < 9 || Mathf.Abs(hp.y - RiverZ(hp.x)) < 25 || Blocked(hp, 10) || houses.Exists(h => Vector2.Distance(h.p, hp) < 15)) continue;
                    var size = new Vector3(7 + (float)_rng.NextDouble() * 4, 4 + (float)_rng.NextDouble() * 2, 8 + (float)_rng.NextDouble() * 4);
                    float yaw = Mathf.Atan2(c.x - hp.x, c.y - hp.y) * Mathf.Rad2Deg;
                    houses.Add((hp, yaw, size, 0));
                    flats.Add(new Flatten { c = hp, r = Mathf.Max(size.x, size.z) * 0.8f, h = H0(hp.x, hp.y) });
                    count--;
                }
                if (winter && (i == 1 || i == 4))
                {
                    var bp = c + new Vector2(-18, 14);
                    houses.Add((bp, 30, new Vector3(7, 2.6f, 6), 3));
                }
            }
            // Каменные фермы в полях
            for (int k = 0, made = 0; k < 400 && made < (TrenchMap ? 2 : 9); k++)
            {
                var p = new Vector2(Rnd(-Zone + 40, Zone - 40), Rnd(-Zone + 60, Zone - 60));
                if (MinDist(p, pts) < 85 || RoadDist(p) < 16 || Mathf.Abs(p.y - RiverZ(p.x)) < 30 || Vector2.Distance(p, baseDe) < 70 || Vector2.Distance(p, baseSu) < 70) continue;
                if (Blocked(p, 24) || houses.Exists(h => Vector2.Distance(h.p, p) < 60)) continue;
                float yaw = Rnd(0, 360);
                houses.Add((p, yaw, new Vector3(8, 4.5f, 11), 0));
                var barn = p + (Vector2)(Quaternion.Euler(0, 0, -yaw) * new Vector2(14, 4));
                houses.Add((barn, yaw + 90, new Vector3(9, 6, 14), 1));
                flats.Add(new Flatten { c = (p + barn) * 0.5f, r = 18, h = H0(p.x, p.y) });
                made++;
            }

            // ---------- 2. Высоты (Burst) ----------
            var heights = new NativeArray<float>(Res * Res, Allocator.TempJob);
            new HeightJob { heights = heights, phase = _phase, flat = _flat, river = _river }.Schedule(Res * Res, 4096).Complete();
            var hm = new float[Res, Res];
            for (int z = 0; z < Res; z++) for (int x = 0; x < Res; x++) hm[z, x] = heights[z * Res + x] / MaxH;
            heights.Dispose();

            foreach (var f in flats) FlattenArea(hm, f);
            BatteryHills(hm, baseDe, baseSu);
            // Окопы (зигзаг) у точек A и D
            foreach (int i in new[] { 0, 3 })
            {
                var c = pts[i];
                Vector2 prev = c + new Vector2(-40, -45);
                for (int s = 1; s <= 8; s++)
                {
                    var p = c + new Vector2(-40 + s * 10, -45 + ((s & 1) == 0 ? 0 : 6));
                    Carve(hm, prev, p, 1.3f, 1.7f); prev = p;
                }
            }
            CarveExtras(hm, pts);
            if (TrenchMap) { PlanTrenches(pts, houses, baseDe, baseSu); CarveTrenches(hm); }

            var td = new TerrainData { heightmapResolution = Res, size = new Vector3(Size, MaxH, Size) };
            td.SetHeights(0, 0, hm);

            // ---------- 3. Текстуры (слои + сплат) ----------
            td.terrainLayers = Layers(winter);
            td.alphamapResolution = SplatRes;
            var splat = Splat(winter, pts, hm);
            if (TrenchMap) TrenchSplat(splat);
            td.SetAlphamaps(0, 0, splat);

            // ---------- 4. Деревья ----------
            td.treePrototypes = TreeProtos(winter);
            var trees = new List<TreeInstance>(30000);

            var tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Terrain";
            tgo.layer = worldLayer;
            tgo.transform.SetParent(L.root.transform);
            tgo.transform.position = new Vector3(-Half, 0, -Half);
            var terrain = tgo.GetComponent<Terrain>();
            L.terrain = terrain;
            var tcol = tgo.GetComponent<TerrainCollider>();          // явная ссылка: модуль физики ландшафта не вырежется
            if (!tcol) tcol = tgo.AddComponent<TerrainCollider>();
            tcol.terrainData = td;
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            // Оптимизация: дальние деревья — билборды, дальний рельеф — базовая текстура, мягкая погрешность сетки
            terrain.basemapDistance = 260; terrain.treeDistance = 700; terrain.treeBillboardDistance = 160;
            terrain.treeCrossFadeLength = 20; terrain.treeMaximumFullLODCount = 300;
            terrain.heightmapPixelError = 4; terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
            // Деревья без коллизии: у префабов нет коллайдеров, TerrainCollider их не создаёт.

            float Y(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0, p.y));
            Vector3 W(Vector2 p, float dy = 0) => new(p.x, Y(p) + dy, p.y);

            var structures = new GameObject("Structures").transform;
            structures.SetParent(L.root.transform);

            BeginFoliage(structures, winter);

            _dyn = new GameObject("Dynamic").transform;
            _dyn.SetParent(L.root.transform);

            // ---------- 5. Постройки ----------
            foreach (var h in houses)
            {
                switch (h.kind)
                {
                    case 2: Church(structures, W(h.p), h.yaw, winter, worldLayer); break;
                    case 3: Bunker(structures, W(h.p, -1f), h.yaw, worldLayer); break;
                    case 4: MapProps.Manor(structures, W(h.p), h.yaw, winter, worldLayer); break;
                    case 5: MapProps.Windmill(structures, _dyn, W(h.p), h.yaw, winter, worldLayer); break;
                    case 6: MapProps.Station(structures, W(h.p), winter, worldLayer); break;
                    case 7: MapProps.CastleRuin(structures, W(h.p), h.yaw, _rng, worldLayer); break;
                    default: House(structures, W(h.p), h.yaw, h.size, h.kind == 1, winter, worldLayer); break;
                }
            }

            // ---------- 6. Бокаж: поля с живыми изгородями ----------
            float cell = FieldCell;
            Color hedgeCol = winter ? new Color(0.55f, 0.6f, 0.55f) : new Color(0.2f, 0.33f, 0.15f);
            int nCells = Mathf.CeilToInt(Zone * 2 / cell);
            Vector2 Corner(int ix, int iz)
            {
                float jx = (Hash(ix, iz, 1) - 0.5f) * 30, jz = (Hash(ix, iz, 2) - 0.5f) * 30;
                return new Vector2(-Zone + ix * cell + jx, -Zone + iz * cell + jz);
            }
            void Hedge(Vector2 a, Vector2 b)
            {
                Vector2 mid = (a + b) * 0.5f;
                if (MinDist(mid, pts) < 80 || Vector2.Distance(mid, baseDe) < 70 || Vector2.Distance(mid, baseSu) < 70) return;
                if (TrenchMap && Mathf.Max(Mathf.Abs(mid.x), Mathf.Abs(mid.y)) < Zone - 20) return;   // изгороди перепаханы снарядами
                if (Mathf.Abs(mid.y - RiverZ(mid.x)) < 18) return;
                // Делим на куски по ~14 м, пропуская участки у дорог и одну «калитку».
                int pieces = Mathf.CeilToInt(Vector2.Distance(a, b) / 14f);
                int gate = _rng.Next(pieces);
                for (int i = 0; i < pieces; i++)
                {
                    if (i == gate) continue;
                    Vector2 p0 = Vector2.Lerp(a, b, i / (float)pieces), p1 = Vector2.Lerp(a, b, (i + 1) / (float)pieces), pm = (p0 + p1) * 0.5f;
                    if (RoadDist(pm) < 7 || Blocked(pm, 4)) continue;
                    float len = Vector2.Distance(p0, p1) + 0.6f;
                    float yaw = Mathf.Atan2(p1.x - p0.x, p1.y - p0.y) * Mathf.Rad2Deg;
                    float hgt = 2.2f + Hash((int)pm.x, (int)pm.y, 3) * 0.8f;
                    HedgePiece(structures, W(pm), yaw, len, hgt, worldLayer, (int)pm.x * 31 + (int)pm.y * 17);
                    if (_rng.NextDouble() < 0.35) trees.Add(Tree(terrain, pm + new Vector2(Rnd(-1, 1), Rnd(-1, 1)), winter ? 0 : 0, 0.9f + Rnd(0, 0.5f)));
                }
            }
            for (int ix = 0; ix <= nCells; ix++)
                for (int iz = 0; iz <= nCells; iz++)
                {
                    if (ix < nCells) Hedge(Corner(ix, iz), Corner(ix + 1, iz));
                    if (iz < nCells) Hedge(Corner(ix, iz), Corner(ix, iz + 1));
                }

            // ---------- 7. Мешки с песком у точек, базы, мосты ----------
            Color sand = winter ? new Color(0.82f, 0.82f, 0.8f) : new Color(0.62f, 0.56f, 0.4f);
            for (int i = 0; i < pts.Count; i++)
                for (int k = 0; k < 5; k++)
                {
                    float a = k * Mathf.PI * 2 / 5 + i;
                    var sp = pts[i] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 13;
                    var sb = ProceduralModels.Box(structures, "Sandbags", W(sp, 0.45f), new Vector3(4f, 1.1f, 0.9f), sand, true, new Vector3(0, -a * Mathf.Rad2Deg + 90, 0));
                    sb.layer = worldLayer;
                }
            Base(structures, W(baseDe), 180, Faction.Germany, worldLayer);
            Base(structures, W(baseSu), 0, Faction.USSR, worldLayer);
            Bridges(structures, terrain, worldLayer, winter);
            River(L.root.transform, terrain, worldLayer, winter);
            BuildExtras(L, structures, terrain, worldLayer, winter, pts, baseDe, baseSu, houses);
            BuildFences(L.root.transform, terrain, worldLayer, winter, pts, houses);
            if (TrenchMap) BuildTrenches(structures, terrain, worldLayer);

            // ---------- 8. Леса за пределами боевой зоны + сады внутри ----------
            for (int k = 0; k < 40000 && trees.Count < 6000; k++)
            {
                var p = new Vector2(Rnd(-Half + 30, Half - 30), Rnd(-Half + 30, Half - 30));
                float edge = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                float forest = noise.snoise(new float2(p.x, p.y) / 230f + _phase) * 0.5f + 0.5f;
                // Рощи и перелески есть и в зоне боёв (укрытия), за её пределами — сплошной лес на склонах
                float want = edge < Zone ? (TrenchMap ? 0.05f : 0.18f) : 0.6f;
                if (edge < Zone && (MinDist(p, pts) < 70 || Vector2.Distance(p, baseDe) < 55 || Vector2.Distance(p, baseSu) < 55)) continue;
                if (forest < 1 - want - 0.15f) continue;
                if (RoadDist(p) < 10 || Mathf.Abs(p.y - RiverZ(p.x)) < 20 || Blocked(p, 6)) continue;
                if (td.GetSteepness((p.x + Half) / Size, (p.y + Half) / Size) > 38) continue;
                trees.Add(Tree(terrain, p, PickProto(winter, forest), 0.8f + Rnd(0, 0.7f)));
            }
            EdgeForest(terrain, trees, winter, L.root.transform, worldLayer);
            terrain.terrainData.SetTreeInstances(trees.ToArray(), true);
            TreeOccluders(terrain, trees);
            // Подлесок: отдельные кусты на опушках и вдоль дорог
            for (int k = 0, made = 0; k < 3000 && made < (TrenchMap ? 50 : 220); k++)
            {
                var p = new Vector2(Rnd(-Zone, Zone), Rnd(-Zone, Zone));
                if (RoadDist(p) < 5 || MinDist(p, pts) < 25 || Blocked(p, 3) || Mathf.Abs(p.y - RiverZ(p.x)) < 10) continue;
                if (houses.Exists(h => Vector2.Distance(h.p, p) < 9)) continue;
                Bush(W(p), 0.8f + Rnd(0, 1.2f), k * 7 + 3);
                made++;
            }
            _foliage.FlushAll();
            Details(terrain, winter, pts);

            // ---------- 9. Спавны ----------
            L.points = pts.ConvertAll(p => W(p)).ToArray();
            L.spawnsDe = Ring(baseDe, 10, 14, Y);
            L.spawnsSu = Ring(baseSu, 10, 14, Y);

            StaticBatchingUtility.Combine(structures.gameObject);

            // ---------- 10. NavMesh только боевой зоны, асинхронно ----------
            var navGo = new GameObject("NavMesh (combat zone)");
            navGo.transform.SetParent(L.root.transform);
            var surf = navGo.AddComponent<NavMeshSurface>();
            surf.collectObjects = CollectObjects.Volume;
            surf.center = new Vector3(0, 100, 0);
            surf.size = new Vector3(Zone * 2 + 40, 300, Zone * 2 + 40);
            surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surf.layerMask = 1 << worldLayer;
            surf.overrideVoxelSize = true; surf.voxelSize = 0.4f;
            surf.overrideTileSize = true; surf.tileSize = 256;
            surf.navMeshData = new NavMeshData(surf.agentTypeID);
            surf.AddData();
            surf.UpdateNavMesh(surf.navMeshData); // фоновая сборка; до готовности боты ходят по flow field
            L.navSurface = surf;

            // Ландшафт и постройки двигались после создания — без синхронизации физика «видит» их в старом месте.
            Physics.SyncTransforms();
            return L;
        }

        /// <summary>Кроны деревьев в зоне боёв закрывают обзор ботам и танкам (триггеры на слое заслонов, пули проходят).</summary>
        static void TreeOccluders(Terrain terrain, List<TreeInstance> trees)
        {
            var size = terrain.terrainData.size; var org = terrain.transform.position;
            foreach (var ti in trees)
            {
                Vector3 wp = new Vector3(ti.position.x * size.x, 0, ti.position.z * size.z) + new Vector3(org.x, 0, org.z);
                if (Mathf.Max(Mathf.Abs(wp.x), Mathf.Abs(wp.z)) > Zone + 30f) continue;
                float g = terrain.SampleHeight(wp) + org.y, h = 9f * ti.heightScale, w = 3.4f * ti.widthScale;
                Occluder(new Vector3(wp.x, g + h * 0.58f, wp.z), Quaternion.identity, new Vector3(w, h * 0.7f, w));
            }
        }

        // ================= Рельеф =================

        public static float RiverZ(float x) => 60f * math.sin(x / 165f + 0.7f) + 18f * math.sin(x / 55f + 1.9f) - 35f;

        /// <summary>Высота без выравниваний (м). Одна функция — и для Burst-джоба, и для планирования построек.</summary>
        public static float H0(float x, float z) => HeightAt(x, z, _phase, _flat, _river);
        static float _river = 1f;    // на «окопной» карте реки нет
        static float _flat = 1f;     // «окопная» карта почти плоская: открытое поле, окопы — единственное укрытие

        static float HeightAt(float x, float z, float phase, float flat, float river)
        {
            float2 p = new(x, z);
            float edge = math.max(math.abs(x), math.abs(z));
            float zoneMask = 1f - math.smoothstep(Zone - 60f, Zone + 70f, edge);
            // Холмистая долина: мягкие гряды в зоне боёв (высоты, укрытия от прямой видимости) + мелкая неровность
            float gentle = (Fbm(p / 170f + phase, 3) * 7f + Fbm(p / 45f + phase + 3f, 2) * 0.5f) * flat;
            // Без гор: за зоной боёв рельеф такой же пологий (граница карты — густой непроходимый лес, см. EdgeForest)
            float hills = gentle + (Fbm(p / 120f + phase + 50f, 2) * 0.5f + 0.5f) * 2f;
            float h = 30f + math.lerp(hills, gentle, zoneMask) + 0f * edge;
            // Река: русло 3 м глубиной, пологие берега
            float d = math.abs(z - RiverZ(x));
            h -= 3.2f * river * (1f - math.smoothstep(4f, 16f, d));
            return h;
        }

        static float Fbm(float2 p, int oct)
        {
            float s = 0, a = 0.5f, n = 0;
            for (int i = 0; i < oct; i++) { s += noise.snoise(p) * a; n += a; p *= 2.03f; a *= 0.5f; }
            return s / n;
        }

        [BurstCompile]
        struct HeightJob : IJobParallelFor
        {
            [WriteOnly] public NativeArray<float> heights;
            public float phase, flat, river;
            public void Execute(int i)
            {
                int z = i / Res, x = i % Res;
                float wx = -Half + x * Size / (Res - 1), wz = -Half + z * Size / (Res - 1);
                heights[i] = math.clamp(HeightAt(wx, wz, phase, flat, river), 0, MaxH);
            }
        }

        static void FlattenArea(float[,] hm, Flatten f)
        {
            float outer = f.r * 1.7f;
            int x0 = ToIdx(f.c.x - outer), x1 = ToIdx(f.c.x + outer), z0 = ToIdx(f.c.y - outer), z1 = ToIdx(f.c.y + outer);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float wx = -Half + x * Size / (Res - 1), wz = -Half + z * Size / (Res - 1);
                    float d = Vector2.Distance(new Vector2(wx, wz), f.c);
                    float k = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(f.r, outer, d));
                    hm[z, x] = Mathf.Lerp(hm[z, x], f.h / MaxH, k);
                }
        }

        static void Carve(float[,] hm, Vector2 a, Vector2 b, float halfWidth, float depth)
        {
            int x0 = ToIdx(Mathf.Min(a.x, b.x) - 4), x1 = ToIdx(Mathf.Max(a.x, b.x) + 4);
            int z0 = ToIdx(Mathf.Min(a.y, b.y) - 4), z1 = ToIdx(Mathf.Max(a.y, b.y) + 4);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var w = new Vector2(-Half + x * Size / (Res - 1), -Half + z * Size / (Res - 1));
                    if (SegDist(w, a, b) < halfWidth + 1.9f) hm[z, x] -= depth / MaxH;
                }
        }

        // ================= Холм батареи и лес по краю =================

        /// <summary>Центр холма батареи: позади базы (артиллерия и куча снарядов стоят на ровной вершине).</summary>
        public static Vector2 LayoutBase(int team) => team == 0 ? new Vector2(15, 330) : new Vector2(-15, -330);
        /// <summary>Мировая точка на вершине холма батареи: across — поперёк (м), depth — от передней кромки назад (м).</summary>
        public static Vector3 BatteryPoint(int team, float across, float depth)
        {
            Vector2 b = LayoutBase(team), c = BatteryCenter(b), fwd = (c - b).normalized, side = new(fwd.y, -fwd.x);
            Vector2 p = c + side * across + fwd * (depth - HillHalfD);
            var t = Terrain.activeTerrain;
            float y = t ? t.SampleHeight(new Vector3(p.x, 0, p.y)) + t.transform.position.y : 0f;
            return new Vector3(p.x, y, p.y);
        }
        public static Vector2 BatteryCenter(Vector2 b) { var away = b.sqrMagnitude > 1 ? b.normalized : Vector2.up; return b + away * 88f; }
        public const float HillHalfW = 92f, HillHalfD = 40f, HillRise = 3.5f, HillSlope = 14f;   // вершина 184×80 м

        /// <summary>Холм с полностью ровной вершиной (прямоугольник 150×80 м, +3,5 м) и пологими склонами.</summary>
        public static void BatteryHills(float[,] hm, Vector2 baseDe, Vector2 baseSu)
        {
            foreach (var b in new[] { baseDe, baseSu })
            {
                Vector2 c = BatteryCenter(b);
                Vector2 fwd = (c - b).normalized, side = new(fwd.y, -fwd.x);
                float top = (H0(c.x, c.y) + HillRise) / MaxH;
                float ext = Mathf.Max(HillHalfW, HillHalfD) + HillSlope + 4f;
                int x0 = ToIdx(c.x - ext), x1 = ToIdx(c.x + ext), z0 = ToIdx(c.y - ext), z1 = ToIdx(c.y + ext);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float wx = -Half + x * Size / (Res - 1), wz = -Half + z * Size / (Res - 1);
                        Vector2 d = new Vector2(wx, wz) - c;
                        float u = Mathf.Abs(Vector2.Dot(d, side)) - HillHalfW, v = Mathf.Abs(Vector2.Dot(d, fwd)) - HillHalfD;
                        float out_ = Mathf.Max(u, v);                       // >0 — за краем вершины
                        float k = out_ <= 0 ? 1f : 1f - Mathf.SmoothStep(0, 1, out_ / HillSlope);
                        if (k > 0) hm[z, x] = Mathf.Max(hm[z, x], Mathf.Lerp(hm[z, x], top, k));
                    }
            }
        }

        /// <summary>Поляна батареи (на холме и подъезд грузовика) — деревьев там нет.</summary>
        public static bool InBatteryClearing(Vector2 p)
        {
            foreach (var b in new[] { new Vector2(15, 330), new Vector2(-15, -330) })
            {
                Vector2 c = BatteryCenter(b), fwd = (c - b).normalized, side = new(fwd.y, -fwd.x);
                Vector2 d = p - c;
                if (Mathf.Abs(Vector2.Dot(d, side)) < HillHalfW + 90f && Mathf.Abs(Vector2.Dot(d, fwd)) < HillHalfD + HillSlope + 4f) return true;
            }
            return false;
        }

        /// <summary>
        /// Густой непроходимый лес по краю карты (за базами и по бокам): деревья в шахматном порядке через ~4,5 м
        /// и невидимые стены по кромке леса — за него не пройти ни игроку, ни технике.
        /// </summary>
        public static void EdgeForest(Terrain terrain, List<TreeInstance> trees, bool winter, Transform parent, int layer)
        {
            const float inner = 455f, step = 4.6f;
            trees.RemoveAll(t =>
            {
                var tp = t.position; Vector2 w = new(tp.x * Size - Half, tp.z * Size - Half);
                return InBatteryClearing(w);
            });
            for (float z = -Half + 2; z < Half - 2; z += step)
                for (float x = -Half + 2; x < Half - 2; x += step)
                {
                    float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                    if (edge < inner) continue;
                    var p = new Vector2(x + Rnd(-1.4f, 1.4f), z + Rnd(-1.4f, 1.4f));
                    if (InBatteryClearing(p)) continue;
                    trees.Add(Tree(terrain, p, winter ? 0 : (_rng.NextDouble() < 0.5 ? 1 : 2), 1.0f + Rnd(0, 0.6f)));
                }
            // Невидимые стены по кромке леса
            var walls = new GameObject("ForestWalls").transform; walls.SetParent(parent, false);
            void Wall(Vector3 c, Vector3 size)
            {
                var g = new GameObject("Wall"); g.layer = layer; g.transform.SetParent(walls, false); g.transform.position = c;
                var bc = g.AddComponent<BoxCollider>(); bc.size = size;
            }
            float wpos = inner + 8f, len = Size, hgt = 400f;
            Wall(new Vector3(0, 0, wpos), new Vector3(len, hgt, 4)); Wall(new Vector3(0, 0, -wpos), new Vector3(len, hgt, 4));
            Wall(new Vector3(wpos, 0, 0), new Vector3(4, hgt, len)); Wall(new Vector3(-wpos, 0, 0), new Vector3(4, hgt, len));
        }

        static int ToIdx(float w) => Mathf.Clamp(Mathf.RoundToInt((w + Half) / Size * (Res - 1)), 0, Res - 1);

        // ================= Текстуры =================

        static TerrainLayer[] Layers(bool winter)
        {
            Color[] cols = TrenchMap
                ? new[] { new Color(0.3f, 0.36f, 0.2f), new Color(0.45f, 0.41f, 0.29f), new Color(0.33f, 0.38f, 0.22f), new Color(0.33f, 0.27f, 0.2f), new Color(0.42f, 0.41f, 0.39f), new Color(0.3f, 0.27f, 0.21f) }
                : winter
                ? new[] { new Color(0.9f, 0.92f, 0.95f), new Color(0.84f, 0.86f, 0.9f), new Color(0.72f, 0.75f, 0.72f), new Color(0.55f, 0.52f, 0.48f), new Color(0.45f, 0.45f, 0.47f), new Color(0.75f, 0.85f, 0.92f) }
                : new[] { new Color(0.31f, 0.5f, 0.2f), new Color(0.78f, 0.66f, 0.34f), new Color(0.42f, 0.58f, 0.23f), new Color(0.5f, 0.41f, 0.28f), new Color(0.5f, 0.49f, 0.46f), new Color(0.38f, 0.33f, 0.23f) };
            var layers = new TerrainLayer[cols.Length];
            for (int i = 0; i < cols.Length; i++)
                layers[i] = new TerrainLayer { diffuseTexture = NoiseTex(cols[i], 128, 0.08f + i * 0.01f, i), tileSize = new Vector2(8, 8), smoothness = 0f, metallic = 0f };
            return layers;
        }

        static Texture2D NoiseTex(Color c, int n, float amp, int seed)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Тайлящийся шум: выборка по тору
                    float u = x / (float)n * Mathf.PI * 2, v = y / (float)n * Mathf.PI * 2;
                    float s = noise.snoise(new float4(math.cos(u), math.sin(u), math.cos(v), math.sin(v)) * 1.5f + seed * 3.1f) * 0.6f
                            + noise.snoise(new float4(math.cos(u), math.sin(u), math.cos(v), math.sin(v)) * 4f + seed) * 0.4f;
                    var col = c * (1f + s * amp * 2f);
                    col.a = 0f;     // альфа диффуза = гладкость в Terrain/Lit — земля матовая, без бликов
                    px[y * n + x] = col;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        /// <summary>Сплат: 0 трава/снег, 1 поле-пшеница, 2 поле-зелень, 3 дорога, 4 скалы, 5 берег/лёд.</summary>
        static float[,,] Splat(bool winter, List<Vector2> pts, float[,] hm)
        {
            var a = new float[SplatRes, SplatRes, 6];
            float step = Size / (SplatRes - 1);
            float[] v = new float[6];
            for (int z = 0; z < SplatRes; z++)
                for (int x = 0; x < SplatRes; x++)
                {
                    float wx = -Half + x * step, wz = -Half + z * step;
                    var w = new Vector2(wx, wz);
                    System.Array.Clear(v, 0, 6);
                    float edge = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz));

                    // Поля — лоскутами в боевой зоне и рядом
                    if (edge < Zone + 300)
                    {
                        int cx = Mathf.FloorToInt((wx + Zone) / FieldCell), cz = Mathf.FloorToInt((wz + Zone) / FieldCell);
                        float hsh = Hash(cx, cz, 7);
                        if (hsh < 0.33f) v[1] = 1;
                        else
                        {
                            // Луга — плавные пятна двух оттенков зелени вместо ровной заливки
                            float g = Mathf.SmoothStep(0, 1, Mathf.PerlinNoise(wx * 0.018f + 3f, wz * 0.018f + 9f) * 1.4f - 0.2f);
                            if (hsh < 0.66f) { v[2] = 1 - g * 0.6f; v[0] = g * 0.6f; } else { v[0] = 1 - g * 0.5f; v[2] = g * 0.5f; }
                        }
                    }
                    else { float g = Mathf.PerlinNoise(wx * 0.012f, wz * 0.012f); v[0] = 1 - g * 0.5f; v[2] = g * 0.5f; }

                    // Склоны — скалы
                    int hx = Mathf.Clamp(x * 2, 1, Res - 2), hz = Mathf.Clamp(z * 2, 1, Res - 2);
                    float dh = (Mathf.Abs(hm[hz, hx + 1] - hm[hz, hx - 1]) + Mathf.Abs(hm[hz + 1, hx] - hm[hz - 1, hx])) * MaxH / (Size / (Res - 1) * 2);
                    float rock = Mathf.InverseLerp(0.45f, 0.9f, dh);
                    for (int i = 0; i < 6; i++) v[i] *= 1 - rock;
                    v[4] += rock;

                    float rd = Mathf.Abs(wz - RiverZ(wx));
                    if (rd < 18) { float k = 1 - Mathf.InverseLerp(10, 18, rd); for (int i = 0; i < 6; i++) v[i] *= 1 - k; v[5] += k; }
                    if (MinDist(w, pts) < 26)   // площадь деревни: утоптанная земля с травой по краям и пятнами
                    {
                        float dirt = Mathf.Lerp(0.35f, 0.75f, Mathf.PerlinNoise(wx * 0.08f, wz * 0.08f)) * Mathf.InverseLerp(26f, 14f, MinDist(w, pts));
                        for (int i = 0; i < 6; i++) v[i] *= 1 - dirt; v[3] += dirt;
                    }
                    float road = RoadDist(w);
                    if (road < 6) { float k = 1 - Mathf.InverseLerp(3.5f, 6f, road); for (int i = 0; i < 6; i++) v[i] *= 1 - k; v[3] += k; }

                    ExtraSplat(w, v, edge);
                    float sum = 0; for (int i = 0; i < 6; i++) sum += v[i];
                    for (int i = 0; i < 6; i++) a[z, x, i] = v[i] / Mathf.Max(sum, 1e-4f);
                }
            return a;
        }

        static Material TerrainMaterial()
        {
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            var sh = urp ? Shader.Find("Universal Render Pipeline/Terrain/Lit") : Shader.Find("Nature/Terrain/Standard");
            return sh ? new Material(sh) : null;
        }

        // ================= Деревья =================

        static GameObject[] _treePrefabs;

        static TreePrototype[] TreeProtos(bool winter)
        {
            Color trunk = new(0.33f, 0.24f, 0.16f);
            Color leaf = winter ? new Color(0.2f, 0.32f, 0.24f) : new Color(0.24f, 0.4f, 0.17f);
            Color leaf2 = winter ? new Color(0.85f, 0.88f, 0.9f) : new Color(0.3f, 0.45f, 0.2f);
            _treePrefabs = winter
                ? new[] { Spruce(trunk, leaf, leaf2, 12), Spruce(trunk, leaf, leaf2, 18), Bare(trunk) }
                : new[] { Oak(trunk, leaf), Poplar(trunk, leaf2), Spruce(trunk, new Color(0.18f, 0.32f, 0.2f), new Color(0.2f, 0.35f, 0.22f), 15) };
            var protos = new TreePrototype[_treePrefabs.Length];
            for (int i = 0; i < protos.Length; i++) protos[i] = new TreePrototype { prefab = _treePrefabs[i] };
            return protos;
        }

        static int PickProto(bool winter, float forest) => winter ? (forest > 0.8f ? 1 : _rng.NextDouble() < 0.15 ? 2 : 0)
                                                                  : (forest > 0.82f ? 2 : _rng.NextDouble() < 0.25 ? 1 : 0);

        static GameObject TreeGo(string name, params (Mesh mesh, Matrix4x4 m, Color c)[] parts)
        {
            var go = new GameObject(name);
            go.transform.SetParent(ProceduralModels.Root, false);
            // Подмеши по материалам: ствол + крона(ы)
            var mats = new List<Material>(); var combines = new List<CombineInstance>();
            var meshesPerMat = new Dictionary<Color, List<CombineInstance>>();
            foreach (var p in parts)
            {
                if (!meshesPerMat.TryGetValue(p.c, out var l)) meshesPerMat[p.c] = l = new List<CombineInstance>();
                l.Add(new CombineInstance { mesh = p.mesh, transform = p.m });
            }
            var subs = new List<CombineInstance>();
            foreach (var kv in meshesPerMat)
            {
                var sub = new Mesh(); sub.CombineMeshes(kv.Value.ToArray(), true, true);
                subs.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
                mats.Add(ProceduralModels.Mat(kv.Key, 0.1f));
            }
            var mesh = new Mesh { name = name }; mesh.CombineMeshes(subs.ToArray(), false, false); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            // Без коллайдера: сквозь деревья можно пройти (как просили), пули и взгляд ботов листву не замечают
            return go;
        }

        static Matrix4x4 M(Vector3 p, Vector3 s, Vector3 e = default) => Matrix4x4.TRS(p, Quaternion.Euler(e), s);

        static GameObject Oak(Color trunk, Color leaf) => TreeGo("Oak",
            (MeshUtil.Cylinder(6), M(new(0, 2.5f, 0), new(0.6f, 5, 0.6f)), trunk),
            (Blob(0), M(new(0, 7f, 0), new(7, 5.5f, 7)), leaf),
            (Blob(1), M(new(1.6f, 6f, 1), new(4.8f, 4.2f, 4.8f)), leaf),
            (Blob(2), M(new(-1.7f, 6.3f, -0.8f), new(4.6f, 3.9f, 4.6f)), leaf * 0.9f),
            (Blob(3), M(new(0.3f, 8.6f, -0.5f), new(4.2f, 3.2f, 4.2f)), leaf * 1.08f),
            (MeshUtil.Cylinder(5), M(new(0.9f, 5.2f, 0.4f), new(0.22f, 2.6f, 0.22f), new(0, 0, -35)), trunk),
            (MeshUtil.Cylinder(5), M(new(-0.9f, 5.4f, -0.3f), new(0.2f, 2.4f, 0.2f), new(10, 0, 38)), trunk));

        static GameObject Poplar(Color trunk, Color leaf) => TreeGo("Poplar",
            (MeshUtil.Cylinder(6), M(new(0, 2, 0), new(0.45f, 4, 0.45f)), trunk),
            (Blob(4), M(new(0, 9, 0), new(3.4f, 12, 3.4f)), leaf),
            (Blob(5), M(new(0.2f, 5.5f, 0.1f), new(2.8f, 4f, 2.8f)), leaf * 0.9f));

        static GameObject Spruce(Color trunk, Color leaf, Color tip, float h) => TreeGo("Spruce" + h,
            (MeshUtil.Cylinder(6), M(new(0, 1, 0), new(0.4f, 2, 0.4f)), trunk),
            (MeshUtil.Cone(7), M(new(0, 1.5f, 0), new(h * 0.42f, h * 0.45f, h * 0.42f)), leaf),
            (MeshUtil.Cone(7), M(new(0, 1.5f + h * 0.3f, 0), new(h * 0.32f, h * 0.4f, h * 0.32f)), leaf),
            (MeshUtil.Cone(7), M(new(0, 1.5f + h * 0.58f, 0), new(h * 0.2f, h * 0.38f, h * 0.2f)), tip));

        static GameObject Bare(Color trunk) => TreeGo("BareTree",
            (MeshUtil.Cylinder(6), M(new(0, 3, 0), new(0.5f, 6, 0.5f)), trunk),
            (MeshUtil.Cylinder(5), M(new(0.8f, 5.5f, 0), new(0.18f, 3, 0.18f), new(0, 0, -40)), trunk),
            (MeshUtil.Cylinder(5), M(new(-0.7f, 6, 0.3f), new(0.15f, 2.6f, 0.15f), new(20, 0, 35)), trunk));

        static TreeInstance Tree(Terrain t, Vector2 p, int proto, float scale) => new()
        {
            position = new Vector3((p.x + Half) / Size, 0, (p.y + Half) / Size),
            prototypeIndex = Mathf.Clamp(proto, 0, t.terrainData.treePrototypes.Length - 1),
            widthScale = scale, heightScale = scale * Rnd(0.9f, 1.15f),
            rotation = Rnd(0, Mathf.PI * 2), color = Color.white, lightmapColor = Color.white
        };

        // ================= Постройки =================

        static void Church(Transform parent, Vector3 pos, float yaw, bool winter, int layer)
        {
            var root = new GameObject("Church").transform;
            root.SetParent(parent); root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Color stone = new(0.7f, 0.67f, 0.6f), roof = winter ? new Color(0.92f, 0.93f, 0.95f) : new Color(0.28f, 0.29f, 0.32f);
            ProceduralModels.Box(root, "Nave", new Vector3(0, 4.5f, 0), new Vector3(11, 10, 24), stone, true).layer = layer;
            ProceduralModels.Part(root, "NaveRoof", MeshUtil.Prism(), new Vector3(0, 12.2f, 0), new Vector3(12, 5.5f, 25), roof);
            ProceduralModels.Box(root, "Tower", new Vector3(0, 10, -14), new Vector3(6, 21, 6), stone, true).layer = layer;
            ProceduralModels.Part(root, "Spire", MeshUtil.Cone(4), new Vector3(0, 20.5f, -14), new Vector3(8.4f, 13, 8.4f), roof, new Vector3(0, 45, 0));
            ProceduralModels.Box(root, "Portal", new Vector3(0, 2, -17.05f), new Vector3(2.4f, 4, 0.1f), new Color(0.2f, 0.14f, 0.1f));
            for (int i = -2; i <= 2; i++)
            {
                ProceduralModels.Box(root, "Window", new Vector3(5.52f, 6, i * 4.2f), new Vector3(0.08f, 3.2f, 1.2f), new Color(0.15f, 0.18f, 0.28f), false, default, 0.8f);
                ProceduralModels.Box(root, "Window", new Vector3(-5.52f, 6, i * 4.2f), new Vector3(0.08f, 3.2f, 1.2f), new Color(0.15f, 0.18f, 0.28f), false, default, 0.8f);
            }
            for (int s = -1; s <= 1; s += 2) // кладбищенская ограда
                ProceduralModels.Box(root, "Wall", new Vector3(s * 12, 0.5f, 0), new Vector3(0.6f, 1.2f, 34), stone * 0.9f, true).layer = layer;
        }

        static void Bunker(Transform parent, Vector3 pos, float yaw, int layer, bool snow = true)
        {
            var root = new GameObject("Bunker").transform;
            root.SetParent(parent); root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Color concrete = new(0.6f, 0.6f, 0.58f);
            ProceduralModels.Box(root, "Base", new Vector3(0, 0.6f, 0), new Vector3(7, 1.5f, 6), concrete, true).layer = layer;
            ProceduralModels.Box(root, "Top", new Vector3(0, 2.6f, 0), new Vector3(7.4f, 1.2f, 6.4f), concrete, true).layer = layer;
            ProceduralModels.Box(root, "PillarL", new Vector3(-3, 1.75f, 2.6f), new Vector3(1, 1, 0.8f), concrete, true).layer = layer;
            ProceduralModels.Box(root, "PillarR", new Vector3(3, 1.75f, 2.6f), new Vector3(1, 1, 0.8f), concrete, true).layer = layer;
            ProceduralModels.Box(root, "Slit", new Vector3(0, 1.75f, 2.6f), new Vector3(5, 0.7f, 0.2f), new Color(0.05f, 0.05f, 0.05f));
            if (snow) ProceduralModels.Box(root, "Snow", new Vector3(0, 3.25f, 0), new Vector3(7.6f, 0.15f, 6.6f), new Color(0.93f, 0.94f, 0.96f));
        }

        static void Base(Transform parent, Vector3 pos, float yaw, Faction f, int layer)
        {
            var root = new GameObject("Base_" + f).transform;
            root.SetParent(parent); root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Color canvas = f == Faction.Germany ? new Color(0.36f, 0.4f, 0.33f) : new Color(0.45f, 0.42f, 0.28f);
            for (int i = 0; i < 4; i++)
            {
                var tp = new Vector3(-24 + i * 16, 0, -14);
                ProceduralModels.Part(root, "Tent", MeshUtil.Prism(), tp + Vector3.up * 1.6f, new Vector3(5, 3.2f, 7), canvas);
                ProceduralModels.Box(root, "Crate", tp + new Vector3(4, 0.5f, 3), new Vector3(1.2f, 1, 0.8f), ProceduralModels.Wood, true).layer = layer;
            }
            ProceduralModels.Box(root, "Pole", new Vector3(0, 5, -4), new Vector3(0.2f, 10, 0.2f), Color.gray);
            ProceduralModels.Box(root, "Flag", new Vector3(0.9f, 9, -4), new Vector3(1.8f, 1.1f, 0.05f), f == Faction.Germany ? new Color(0.25f, 0.25f, 0.28f) : new Color(0.7f, 0.1f, 0.08f));
        }

        static void Bridges(Transform parent, Terrain t, int layer, bool winter)
        {
            foreach (var s in _roads)
            {
                float da = s.a.y - RiverZ(s.a.x), db = s.b.y - RiverZ(s.b.x);
                if (Mathf.Sign(da) == Mathf.Sign(db)) continue;
                float k = da / (da - db);
                var c = Vector2.Lerp(s.a, s.b, k);
                var dir = (s.b - s.a).normalized;
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                float y = Mathf.Max(t.SampleHeight(new Vector3(c.x - dir.x * 16, 0, c.y - dir.y * 16)), t.SampleHeight(new Vector3(c.x + dir.x * 16, 0, c.y + dir.y * 16)));
                var root = new GameObject("Bridge").transform;
                root.SetParent(parent); root.SetPositionAndRotation(new Vector3(c.x, y, c.y), Quaternion.Euler(0, yaw, 0));
                Color stone = new(0.62f, 0.6f, 0.55f);
                ProceduralModels.Box(root, "Deck", new Vector3(0, 0.1f, 0), new Vector3(7, 0.6f, 34), stone, true).layer = layer;
                ProceduralModels.Box(root, "RailL", new Vector3(-3.3f, 0.9f, 0), new Vector3(0.4f, 1f, 34), stone * 0.9f, true).layer = layer;
                ProceduralModels.Box(root, "RailR", new Vector3(3.3f, 0.9f, 0), new Vector3(0.4f, 1f, 34), stone * 0.9f, true).layer = layer;
                ProceduralModels.Box(root, "Pier", new Vector3(0, -2.5f, 0), new Vector3(5, 5, 2.5f), stone * 0.85f);
            }
        }

        /// <summary>Лента воды/льда вдоль русла. Летом — без коллайдера (вброд), зимой — лёд с коллайдером.</summary>
        static void River(Transform parent, Terrain t, int layer, bool winter)
        {
            var v = new List<Vector3>(); var tri = new List<int>();
            for (float x = -Half; x <= Half; x += 16f)
            {
                float z = RiverZ(x);
                float dz = RiverZ(x + 1) - z;
                var n = new Vector2(-dz, 1).normalized;   // перпендикуляр к руслу
                float bank = Mathf.Min(t.SampleHeight(new Vector3(x + n.x * 16, 0, z + n.y * 16)), t.SampleHeight(new Vector3(x - n.x * 16, 0, z - n.y * 16)));
                float y = bank - (winter ? 0.7f : 0.9f);
                v.Add(new Vector3(x - n.x * 13, y, z - n.y * 13));
                v.Add(new Vector3(x + n.x * 13, y, z + n.y * 13));
                int i = v.Count;
                if (i >= 4) { tri.Add(i - 4); tri.Add(i - 3); tri.Add(i - 2); tri.Add(i - 3); tri.Add(i - 1); tri.Add(i - 2); }
            }
            var mesh = new Mesh { name = "River", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(winter ? "FrozenRiver" : "River");
            go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ProceduralModels.Mat(winter ? new Color(0.72f, 0.84f, 0.92f) : new Color(0.17f, 0.3f, 0.36f), winter ? 0.75f : 0.92f);
            if (winter) { go.AddComponent<MeshCollider>().sharedMesh = mesh; go.layer = layer; }
        }

        // ================= Утилиты =================

        static Vector3[] Ring(Vector2 c, int n, float r, System.Func<Vector2, float> y)
        {
            var arr = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var p = c + new Vector2((i - n / 2) * 3.2f, (i % 2) * 3.5f - 8);
                arr[i] = new Vector3(p.x, y(p) + 0.1f, p.y);
            }
            return arr;
        }

        static void BuildRoadGrid()
        {
            _roadGrid = new Dictionary<long, List<int>>();
            for (int i = 0; i < _roads.Count; i++)
            {
                var s = _roads[i];
                int x0 = Cell(Mathf.Min(s.a.x, s.b.x) - 20), x1 = Cell(Mathf.Max(s.a.x, s.b.x) + 20);
                int z0 = Cell(Mathf.Min(s.a.y, s.b.y) - 20), z1 = Cell(Mathf.Max(s.a.y, s.b.y) + 20);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        long key = ((long)x << 32) ^ (uint)z;
                        if (!_roadGrid.TryGetValue(key, out var l)) _roadGrid[key] = l = new List<int>();
                        l.Add(i);
                    }
            }
        }

        static int Cell(float w) => Mathf.FloorToInt(w / 100f);

        public static float RoadDist(Vector2 p)
        {
            long key = ((long)Cell(p.x) << 32) ^ (uint)Cell(p.y);
            if (_roadGrid == null || !_roadGrid.TryGetValue(key, out var l)) return 999f;
            float best = 999f;
            foreach (var i in l) best = Mathf.Min(best, SegDist(p, _roads[i].a, _roads[i].b));
            return best;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector2.Distance(p, a + ab * t);
        }

        static float MinDist(Vector2 p, List<Vector2> pts) { float m = 1e9f; foreach (var q in pts) m = Mathf.Min(m, Vector2.Distance(p, q)); return m; }
        static float Rnd(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        static float Hash(int x, int z, int s) { uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(s * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xFFFFFF) / (float)0xFFFFFF; }
    }
}
