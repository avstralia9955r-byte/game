using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>
    /// «Ржев: окопы» — отдельный генератор с нуля. Поле, где бои идут уже месяцами:
    /// почти плоская перепаханная равнина без реки и деревень, сплошная грязь и сотни воронок,
    /// по три линии траншей у каждой стороны с ходами сообщения, блиндажами, пулемётными гнёздами и проволокой.
    /// На точках захвата — развалины (стены без крыш, кучи кирпича) и бетонные доты. Деревья — только
    /// обломанные стволы; за пределами боевой зоны — тёмный лес на горизонте. Повсюду подбитые танки и грузовики
    /// (часть ещё дымит) и тела погибших обеих сторон.
    /// </summary>
    public static partial class MapGenerator
    {
        static MapLayout GenerateTrenchField(MapTheme theme, int seed, int worldLayer)
        {
            bool winter = false;
            _flat = 0.18f; _river = 0f;
            ProceduralModels.Theme = theme;
            var L = new MapLayout { zoneHalf = Zone, root = new GameObject("[Map] Ржев: окопы 1×1 км") };
            _clear.Clear(); _craters.Clear(); _lakes.Clear(); _castles.Clear();

            // ---------- План ----------
            var pts = new List<Vector2> { new(-210, -130), new(205, -150), new(0, 5), new(-195, 140), new(210, 125) };
            var baseDe = new Vector2(15, 330); var baseSu = new Vector2(-15, -330);
            // Разбитые колейные дороги снабжения: от баз к ближним точкам (по ним проходят танки — окопы прерываются)
            _roads = new List<Seg>();
            void Road(Vector2 a, Vector2 b)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) / 60f));
                Vector2 prev = a, dir = (b - a).normalized, perp = new(-dir.y, dir.x);
                for (int i = 1; i <= n; i++)
                {
                    float t = i / (float)n;
                    var p = Vector2.Lerp(a, b, t) + perp * (i < n ? Rnd(-12f, 12f) * Mathf.Sin(t * Mathf.PI) : 0);
                    _roads.Add(new Seg { a = prev, b = p }); prev = p;
                }
            }
            Road(baseSu, pts[0]); Road(baseSu, pts[1]); Road(pts[0], pts[2]); Road(pts[1], pts[2]);
            Road(baseDe, pts[3]); Road(baseDe, pts[4]); Road(pts[3], pts[2]); Road(pts[4], pts[2]);
            BuildRoadGrid();

            var flats = new List<Flatten>();
            var houses = new List<(Vector2 p, float yaw, Vector3 size, int kind)>();
            foreach (var c in pts) flats.Add(new Flatten { c = c, r = 26, h = H0(c.x, c.y) });
            flats.Add(new Flatten { c = baseDe, r = 40, h = H0(baseDe.x, baseDe.y) });
            flats.Add(new Flatten { c = baseSu, r = 40, h = H0(baseSu.x, baseSu.y) });
            // Развалины у точек (их учитывает разметка окопов, чтобы траншеи не шли сквозь стены)
            var ruins = new List<(Vector2 p, float yaw)>();
            for (int i = 0; i < pts.Count; i++)
            {
                int n = i == 2 ? 3 : 2;
                for (int k = 0; k < 20 && n > 0; k++)
                {
                    float a = Rnd(0, Mathf.PI * 2), r = Rnd(16, 34);
                    var p = pts[i] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (RoadDist(p) < 8 || ruins.Exists(x => Vector2.Distance(x.p, p) < 16)) continue;
                    ruins.Add((p, Rnd(0, 360)));
                    houses.Add((p, 0, new Vector3(10, 4, 10), 0));
                    flats.Add(new Flatten { c = p, r = 8, h = H0(p.x, p.y) });
                    n--;
                }
            }
            // Воронки: сотни, от мелких до огромных
            for (int k = 0; k < 4000 && _craters.Count < 320; k++)
            {
                var p = new Vector2(Rnd(-Zone + 20, Zone - 20), Rnd(-Zone + 20, Zone - 20));
                if (MinDist(p, pts) < 14 || Vector2.Distance(p, baseDe) < 55 || Vector2.Distance(p, baseSu) < 55) continue;
                if (ruins.Exists(x => Vector2.Distance(x.p, p) < 9)) continue;
                float r = _rng.NextDouble() < 0.12 ? Rnd(7f, 11f) : Rnd(2.2f, 6f);
                _craters.Add((p, r, r * 0.32f));
            }

            // ---------- Рельеф ----------
            var heights = new NativeArray<float>(Res * Res, Allocator.TempJob);
            new HeightJob { heights = heights, phase = _phase, flat = _flat, river = 0f }.Schedule(Res * Res, 4096).Complete();
            var hm = new float[Res, Res];
            for (int z = 0; z < Res; z++) for (int x = 0; x < Res; x++) hm[z, x] = heights[z * Res + x] / MaxH;
            heights.Dispose();
            foreach (var f in flats) FlattenArea(hm, f);
            BatteryHills(hm, baseDe, baseSu);
            foreach (var c in _craters) Bowl(hm, c.c, c.r, c.d, c.d * 0.35f);
            PlanTrenches(pts, houses, baseDe, baseSu);
            CarveTrenches(hm);

            var td = new TerrainData { heightmapResolution = Res, size = new Vector3(Size, MaxH, Size) };
            td.SetHeights(0, 0, hm);
            td.terrainLayers = Layers(winter);
            td.alphamapResolution = SplatRes;
            var splat = FieldSplat(pts);
            TrenchSplat(splat);
            td.SetAlphamaps(0, 0, splat);

            // Деревья: в зоне — только обломанные стволы, за ней — тёмный лес
            Color trunk = new(0.27f, 0.22f, 0.17f);
            _treePrefabs = new[] { Bare(trunk), Oak(trunk, new Color(0.2f, 0.29f, 0.16f)), Spruce(trunk, new Color(0.15f, 0.24f, 0.17f), new Color(0.17f, 0.27f, 0.18f), 16) };
            var protos = new TreePrototype[_treePrefabs.Length];
            for (int i = 0; i < protos.Length; i++) protos[i] = new TreePrototype { prefab = _treePrefabs[i] };
            td.treePrototypes = protos;

            var tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Terrain"; tgo.layer = worldLayer;
            tgo.transform.SetParent(L.root.transform);
            tgo.transform.position = new Vector3(-Half, 0, -Half);
            var terrain = tgo.GetComponent<Terrain>();
            var tcol = tgo.GetComponent<TerrainCollider>(); if (!tcol) tcol = tgo.AddComponent<TerrainCollider>(); tcol.terrainData = td;
            L.terrain = terrain;
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.basemapDistance = 260; terrain.treeDistance = 700; terrain.treeBillboardDistance = 160;
            terrain.treeCrossFadeLength = 20; terrain.treeMaximumFullLODCount = 300;
            terrain.heightmapPixelError = 4; terrain.shadowCastingMode = ShadowCastingMode.TwoSided;

            float Y(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0, p.y));
            Vector3 W(Vector2 p, float dy = 0) => new(p.x, Y(p) + dy, p.y);

            var structures = new GameObject("Structures").transform; structures.SetParent(L.root.transform);
            BeginFoliage(structures, winter);
            _dyn = new GameObject("Dynamic").transform; _dyn.SetParent(L.root.transform);

            // ---------- Развалины и доты на точках ----------
            Color brick = new(0.5f, 0.36f, 0.3f), plaster = new(0.55f, 0.53f, 0.48f), charred = new(0.16f, 0.14f, 0.12f);
            foreach (var r in ruins) Ruin(structures, W(r.p), r.yaw, brick, plaster, charred, worldLayer);
            for (int i = 0; i < pts.Count; i++)
            {
                var bp = pts[i] + new Vector2(Rnd(-12, 12), Rnd(-12, 12));
                if (ruins.Exists(x => Vector2.Distance(x.p, bp) < 10)) bp = pts[i] + new Vector2(12, -12);
                Bunker(structures, W(bp, -1.1f), Rnd(0, 360), worldLayer, false);
                // Кольцо мешков вокруг флага
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI * 2 / 6 + i;
                    var sp = pts[i] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 11;
                    ProceduralModels.Box(structures, "Sandbags", W(sp, 0.4f), new Vector3(3.6f, 0.9f, 0.9f), new Color(0.48f, 0.43f, 0.32f), true, new Vector3(0, -a * Mathf.Rad2Deg + 90, 0)).layer = worldLayer;
                }
            }

            // ---------- Траншеи, блиндажи, проволока ----------
            BuildTrenches(structures, terrain, worldLayer);

            // ---------- Подбитая техника ----------
            int wrecks = 0;
            for (int k = 0; k < 2000 && wrecks < 34; k++)
            {
                var p = new Vector2(Rnd(-Zone + 40, Zone - 40), Rnd(-Zone + 60, Zone - 60));
                if (MinDist(p, pts) < 22 || Vector2.Distance(p, baseDe) < 70 || Vector2.Distance(p, baseSu) < 70) continue;
                if (NearTrench(p, 6f) || ruins.Exists(x => Vector2.Distance(x.p, p) < 14) || _clear.Exists(c => Vector2.Distance(c.c, p) < c.r + 6)) continue;
                _clear.Add((p, 6));
                bool german = p.y > 0 ? _rng.NextDouble() < 0.35 : _rng.NextDouble() < 0.65;    // ближе к чужим окопам — больше чужих
                var pos = W(p, -Rnd(0.1f, 0.6f));   // осевшие в грязь
                if (wrecks % 4 == 3) MapProps.Truck(structures, pos, Rnd(0, 360), german, winter, worldLayer);
                else MapProps.Tank(structures, pos, Rnd(0, 360), german, winter, worldLayer, _rng);
                if (wrecks % 4 == 0) MapProps.SmokeColumn(_dyn, W(p, 2.4f));
                wrecks++;
            }
            // Противотанковые ежи на нейтральной полосе
            for (int k = 0; k < 400; k++)
            {
                var p = new Vector2(Rnd(-Zone + 40, Zone - 40), Rnd(-80, 80));
                if (MinDist(p, pts) < 20 || NearTrench(p, 4f) || RoadDist(p) < 5) continue;
                if (k % 9 == 0) MapProps.Hedgehog(structures, W(p), Rnd(0, 360), worldLayer);
            }

            // ---------- Тела погибших ----------
            int bodies = Application.isMobilePlatform ? 70 : 150;
            for (int k = 0, made = 0; k < 3000 && made < bodies; k++)
            {
                var p = new Vector2(Rnd(-Zone + 30, Zone - 30), Rnd(-Zone + 60, Zone - 60));
                if (MinDist(p, pts) < 8 || Vector2.Distance(p, baseDe) < 60 || Vector2.Distance(p, baseSu) < 60) continue;
                // гуще всего — на нейтральной полосе и перед проволокой
                float noMan = 1f - Mathf.Clamp01((Mathf.Abs(p.y) - 60f) / 200f);
                if (_rng.NextDouble() > 0.25 + noMan * 0.75) continue;
                var f = p.y > 0 ? (_rng.NextDouble() < 0.7 ? Faction.Germany : Faction.USSR) : (_rng.NextDouble() < 0.7 ? Faction.USSR : Faction.Germany);
                Corpse(structures, W(p), Rnd(0, 360), f, theme, k);
                made++;
            }

            // ---------- Деревья: обломанные стволы в зоне, лес за её пределами ----------
            var trees = new List<TreeInstance>(5000);
            for (int k = 0; k < 30000 && trees.Count < 4200; k++)
            {
                var p = new Vector2(Rnd(-Half + 30, Half - 30), Rnd(-Half + 30, Half - 30));
                float edge = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                if (edge < Zone)
                {
                    if (trees.Count > 0 && _rng.NextDouble() > 0.012) continue;      // в зоне — редкие остовы
                    if (MinDist(p, pts) < 30 || NearTrench(p, 5f) || RoadDist(p) < 6) continue;
                    trees.Add(Tree(terrain, p, 0, 0.7f + Rnd(0, 0.5f)));
                    continue;
                }
                float forest = noise.snoise(new float2(p.x, p.y) / 200f + _phase) * 0.5f + 0.5f;
                if (forest < 0.3f) continue;
                trees.Add(Tree(terrain, p, forest > 0.7f ? 2 : _rng.NextDouble() < 0.3 ? 0 : 1, 0.8f + Rnd(0, 0.6f)));
            }
            EdgeForest(terrain, trees, theme == MapTheme.Winter, L.root.transform, worldLayer);
            terrain.terrainData.SetTreeInstances(trees.ToArray(), true);
            TreeOccluders(terrain, trees);
            for (int k = 0, made = 0; k < 1500 && made < 40; k++)
            {
                var p = new Vector2(Rnd(-Zone, Zone), Rnd(-Zone, Zone));
                if (RoadDist(p) < 5 || MinDist(p, pts) < 20 || NearTrench(p, 3f)) continue;
                Bush(W(p), 0.6f + Rnd(0, 0.8f), k * 7 + 3);
                made++;
            }
            _foliage.FlushAll();

            // ---------- Базы, спавны ----------
            Base(structures, W(baseDe), 180, Faction.Germany, worldLayer);
            Base(structures, W(baseSu), 0, Faction.USSR, worldLayer);
            L.points = pts.ConvertAll(p => W(p)).ToArray();
            L.spawnsDe = Ring(baseDe, 10, 14, Y);
            L.spawnsSu = Ring(baseSu, 10, 14, Y);
            StaticBatchingUtility.Combine(structures.gameObject);

            // ---------- NavMesh ----------
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
            surf.UpdateNavMesh(surf.navMeshData);
            L.navSurface = surf;
            Physics.SyncTransforms();
            return L;
        }

        static bool NearTrench(Vector2 p, float margin)
        {
            foreach (var t in _trench) if (SegDist(p, t.a, t.b) < TrenchHalf + 1.8f + margin) return true;
            return false;
        }

        /// <summary>Сплат поля боя: выжженная трава, сплошная грязь пятнами, глина в воронках, колеи дорог.</summary>
        static float[,,] FieldSplat(List<Vector2> pts)
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
                    float g = Mathf.PerlinNoise(wx * 0.02f + 3f, wz * 0.02f + 9f);
                    v[0] = 0.55f + g * 0.3f; v[1] = (1 - g) * 0.35f; v[2] = 0.1f;
                    if (edge < Zone + 40)
                    {
                        // Перепаханная земля: чем ближе к нейтральной полосе, тем больше грязи
                        float war = 1f - Mathf.Clamp01((Mathf.Abs(wz) - 40f) / 260f);
                        float m = Mathf.PerlinNoise(wx * 0.03f + 7.3f, wz * 0.03f + 3.1f);
                        float mud = Mathf.Clamp01((m - 0.3f) * 1.6f) * (0.45f + war * 0.5f);
                        for (int i = 0; i < 6; i++) v[i] *= 1 - mud; v[3] += mud;
                    }
                    foreach (var c in _craters)
                    {
                        float d = Vector2.Distance(w, c.c);
                        if (d < c.r * 1.6f) { float k = 1 - Mathf.InverseLerp(c.r * 0.6f, c.r * 1.6f, d); for (int i = 0; i < 6; i++) v[i] *= 1 - k; v[3] += k; break; }
                    }
                    float road = RoadDist(w);
                    if (road < 5) { float k = 1 - Mathf.InverseLerp(3f, 5f, road); for (int i = 0; i < 6; i++) v[i] *= 1 - k; v[3] += k; }
                    float pd = MinDist(w, pts);
                    if (pd < 30) { float k = Mathf.InverseLerp(30f, 12f, pd) * 0.7f; for (int i = 0; i < 6; i++) v[i] *= 1 - k; v[4] += k; }   // щебень развалин
                    float sum = 0; for (int i = 0; i < 6; i++) sum += v[i];
                    for (int i = 0; i < 6; i++) a[z, x, i] = v[i] / Mathf.Max(sum, 1e-4f);
                }
            return a;
        }

        /// <summary>Развалины дома: две-три обломанные стены разной высоты, обгоревшие балки, груды кирпича. Крыши нет.</summary>
        static void Ruin(Transform parent, Vector3 pos, float yaw, Color brick, Color plaster, Color charred, int layer)
        {
            var root = new GameObject("Ruin").transform;
            root.SetParent(parent); root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            float w = Rnd(7, 10), d = Rnd(7, 9);
            void Wall(Vector3 c, Vector3 s, Color col) => ProceduralModels.Box(root, "Wall", c, s, col, true).layer = layer;
            // Задняя стена — почти целая, с проломом
            float h1 = Rnd(2.6f, 4.2f);
            Wall(new Vector3(-w * 0.3f, h1 * 0.5f, -d * 0.5f), new Vector3(w * 0.4f, h1, 0.5f), _rng.NextDouble() < 0.5 ? brick : plaster);
            Wall(new Vector3(w * 0.32f, h1 * 0.35f, -d * 0.5f), new Vector3(w * 0.36f, h1 * 0.7f, 0.5f), brick);
            // Боковая — обломана ступенями
            for (int i = 0; i < 4; i++)
            {
                float hh = Mathf.Max(0.6f, h1 - i * Rnd(0.5f, 1.1f));
                Wall(new Vector3(-w * 0.5f, hh * 0.5f, -d * 0.5f + d * (i + 0.5f) / 4f), new Vector3(0.5f, hh, d / 4f), brick);
            }
            // Передняя — только цоколь
            Wall(new Vector3(0, 0.45f, d * 0.5f), new Vector3(w * 0.7f, 0.9f, 0.5f), brick);
            // Обгоревшие балки и груды кирпича
            for (int i = 0; i < 3; i++)
                ProceduralModels.Box(root, "Beam", new Vector3(Rnd(-w * 0.3f, w * 0.3f), Rnd(0.3f, 1.2f), Rnd(-d * 0.3f, d * 0.3f)), new Vector3(0.22f, 0.22f, Rnd(3f, 5f)), charred, false, new Vector3(Rnd(-30, 30), Rnd(0, 180), Rnd(-20, 20)));
            for (int i = 0; i < 6; i++)
                ProceduralModels.Box(root, "Rubble", new Vector3(Rnd(-w * 0.4f, w * 0.4f), 0.3f, Rnd(-d * 0.4f, d * 0.4f)), Vector3.one * Rnd(0.7f, 1.6f), i % 2 == 0 ? brick : plaster, true, new Vector3(i * 37, i * 53, i * 19)).layer = layer;
        }

        /// <summary>Тело погибшего: лежит ничком или на спине, руки раскинуты, каска иногда откатилась.</summary>
        static void Corpse(Transform parent, Vector3 ground, float yaw, Faction f, MapTheme theme, int seed)
        {
            BotBody.Palette(f, theme, out var uni, out var trousers, out var helm, out var gear, out var skin);
            Color mud = new(0.3f, 0.26f, 0.2f);
            uni = Color.Lerp(uni, mud, 0.35f); trousers = Color.Lerp(trousers, mud, 0.45f);
            bool back = (seed & 1) == 0;
            var root = new GameObject("Corpse").transform;
            root.SetParent(parent);
            root.SetPositionAndRotation(ground + Vector3.up * 0.16f, Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(back ? -90f : 90f, 0, Rnd(-15f, 15f)));
            var cls = (SoldierClass)(seed % 4);
            ProceduralModels.Part(root, "Torso", BotBody.TorsoMesh(cls), new Vector3(0, BotBody.HipY + 0.08f - 0.9f, 0), Vector3.one, uni);
            ProceduralModels.Part(root, "Head", BotBody.HeadMesh(), new Vector3(0, BotBody.HipY + 0.82f - 0.9f, 0.02f), Vector3.one, skin, new Vector3(Rnd(-30, 30), Rnd(-50, 50), 0));
            if (seed % 3 != 0) ProceduralModels.Part(root, "Helmet", BotBody.HelmetMesh(f), new Vector3(0, BotBody.HipY + 0.88f - 0.9f, 0.02f), Vector3.one, helm);
            else ProceduralModels.Part(parent, "HelmetLost", BotBody.HelmetMesh(f), ground + new Vector3(Rnd(-1.5f, 1.5f), 0.1f, Rnd(-1.5f, 1.5f)), Vector3.one, helm, new Vector3(Rnd(100, 260), Rnd(0, 360), 0));
            for (int s = -1; s <= 1; s += 2)
            {
                ProceduralModels.Part(root, "Arm", BotBody.LimbMesh("upper", 0.115f, BotBody.Upper, 0), new Vector3(s * 0.2f, BotBody.HipY + 0.55f - 0.9f, 0), Vector3.one, uni, new Vector3(Rnd(-20, 20), 0, s * Rnd(40f, 120f)));
                ProceduralModels.Part(root, "Thigh", BotBody.LimbMesh("thigh", 0.17f, BotBody.Thigh, 0), new Vector3(s * 0.1f, BotBody.HipY - 0.9f, 0), Vector3.one, trousers, new Vector3(Rnd(-15, 25), 0, s * Rnd(4f, 20f)));
                ProceduralModels.Part(root, "Shin", BotBody.LimbMesh("shin", 0.13f, BotBody.Shin, 1), new Vector3(s * 0.14f, BotBody.HipY - BotBody.Thigh - 0.9f, 0.02f), Vector3.one, trousers, new Vector3(Rnd(-10, 40), 0, s * Rnd(2f, 12f)));
            }
        }
    }
}
