using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Детальные дома и живые изгороди.
    /// Дом: каменный цоколь, оштукатуренные стены (или фахверк), угловой руст, двускатная крыша из двух скатов
    /// с черепицей и свесами, коньковая черепица, фронтоны, ветровые доски, водостоки и трубы, дымоход с колпаком,
    /// окна с наличниками, переплётом, подоконниками, перемычками и ставнями (открыты/закрыты/сорваны), дверь
    /// с крыльцом и козырьком, следы обстрелов (пробоины, обвалившийся участок кровли со стропилами, битый камень),
    /// кусты у стен. Зимой — снег на скатах. Амбар — дощатые стены, большие ворота с раскосами, сеновал.
    /// Всё собирается в несколько мешей по материалам; LODGroup убирает мелочь вдали.
    /// Изгородь: ряд неровных «облаков» листвы трёх оттенков вместо зелёных параллелепипедов + невидимый коллайдер.
    /// </summary>
    public static partial class MapGenerator
    {
        // ---------- Листва ----------

        static readonly Dictionary<int, Mesh> _blobs = new();

        /// <summary>Неровная «шапка» листвы: икосфера, разбитая один раз, с шумовым смещением; плоские грани.</summary>
        public static Mesh Blob(int variant)
        {
            variant = Mathf.Abs(variant) % 6;
            if (_blobs.TryGetValue(variant, out var cached) && cached) return cached;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
            };
            var faces = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int i)) return i;
                verts.Add((verts[a] + verts[b]) * 0.5f);
                return mid[key] = verts.Count - 1;
            }
            var sub = new List<int>();
            for (int i = 0; i < faces.Count; i += 3)
            {
                int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                sub.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            for (int i = 0; i < verts.Count; i++)
            {
                var d = verts[i].normalized;
                float n = Mathf.PerlinNoise(d.x * 1.7f + variant * 3.1f + 10f, d.z * 1.7f + d.y * 0.9f + variant * 1.3f);
                float r = 0.5f * (0.82f + n * 0.36f);
                if (d.y < -0.3f) r *= 0.85f;          // приплюснутый низ
                verts[i] = d * r;
            }
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i < sub.Count; i += 3)
            {
                int k = v.Count;
                v.Add(verts[sub[i]]); v.Add(verts[sub[i + 1]]); v.Add(verts[sub[i + 2]]);
                tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
            }
            var m = new Mesh { name = "Blob" + variant };
            m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            // Ориентация треугольников наружу
            var nrm = m.normals;
            bool inward = Vector3.Dot(nrm[0], v[0]) < 0;
            if (inward) { for (int i = 0; i < tri.Count; i += 3) { (tri[i + 1], tri[i + 2]) = (tri[i + 2], tri[i + 1]); } m.SetTriangles(tri, 0); m.RecalculateNormals(); }
            return _blobs[variant] = m;
        }

        // ---------- Изгороди ----------

        class FoliageBatch
        {
            readonly Transform _parent; readonly Color[] _cols; readonly MeshBuilder[] _mb; int _n;
            public FoliageBatch(Transform parent, Color[] cols) { _parent = parent; _cols = cols; _mb = new MeshBuilder[cols.Length]; for (int i = 0; i < cols.Length; i++) _mb[i] = new MeshBuilder(); }
            public void Add(int tone, Vector3 pos, Quaternion rot, Vector3 scale, int variant)
            {
                var mb = _mb[tone % _mb.Length];
                mb.Add(Blob(variant), pos, rot, scale);
                if (mb.VertexCount > 55000) Flush(tone % _mb.Length);
            }
            void Flush(int i) { var go = _mb[i].BuildDetail(_parent, "Foliage", _cols[i], DetailKind.Foliage, 0.05f); _n++; }
            public void FlushAll() { for (int i = 0; i < _mb.Length; i++) if (_mb[i].VertexCount > 0) Flush(i); }
        }

        static FoliageBatch _foliage;
        static Transform _occluders;

        /// <summary>Слой «заслонов зрения»: триггеры листвы. Не мешают ходьбе и пулям, но закрывают обзор ботам.</summary>
        public const int VisionOccluderLayer = 30;

        static void Occluder(Vector3 center, Quaternion rot, Vector3 size)
        {
            if (!_occluders) return;
            var go = new GameObject("FoliageOccluder");
            go.layer = VisionOccluderLayer;
            go.transform.SetParent(_occluders, false);
            go.transform.SetPositionAndRotation(center, rot);
            var bc = go.AddComponent<BoxCollider>(); bc.size = size; bc.isTrigger = true;
        }

        static void BeginFoliage(Transform parent, bool winter)
        {
            var root = new GameObject("Hedgerows").transform; root.SetParent(parent);
            _occluders = new GameObject("VisionOccluders").transform; _occluders.SetParent(parent);
            _foliage = new FoliageBatch(root, winter
                ? new[] { new Color(0.42f, 0.46f, 0.4f), new Color(0.78f, 0.8f, 0.82f), new Color(0.35f, 0.4f, 0.34f) }
                : new[] { new Color(0.2f, 0.33f, 0.14f), new Color(0.27f, 0.4f, 0.17f), new Color(0.16f, 0.27f, 0.12f), new Color(0.32f, 0.42f, 0.18f) });
        }

        /// <summary>Кусок живой изгороди: 4–6 неровных крон разных оттенков (без коллизии — сквозь изгородь можно пройти).</summary>
        static void HedgePiece(Transform structures, Vector3 groundCenter, float yaw, float len, float hgt, int layer, int seed)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            int n = Mathf.Max(3, Mathf.CeilToInt(len / 3f) + 1);
            for (int j = 0; j < n; j++)
            {
                float t = -len * 0.5f + len * (j + 0.5f) / n;
                float h = hgt * (0.85f + Hash(seed, j, 11) * 0.3f);
                var local = new Vector3((Hash(seed, j, 12) - 0.5f) * 0.6f, h * 0.45f - 0.2f, t + (Hash(seed, j, 13) - 0.5f) * 0.8f);
                var scale = new Vector3(2.4f + Hash(seed, j, 14) * 0.8f, h, len / n * 1.6f + 0.6f);
                _foliage.Add((seed + j) & 3, groundCenter + rot * local, rot * Quaternion.Euler(0, Hash(seed, j, 15) * 50f - 25f, 0), scale, seed + j);
                // нижний ярус — плотный подлесок
                if ((j & 1) == 0)
                    _foliage.Add((seed + j + 2) & 3, groundCenter + rot * new Vector3(local.x * 0.5f, 0.35f, t), rot, new Vector3(2.8f, 1.1f, len / n * 1.4f), seed + j + 3);
            }
            // Без коллизии: сквозь изгородь проходят. Но взгляд она закрывает — триггер на слое заслонов
            Occluder(groundCenter + Vector3.up * (hgt * 0.45f - 0.2f), rot, new Vector3(1.8f, hgt * 0.9f, len));
        }

        /// <summary>Отдельный куст (у домов, на опушках).</summary>
        static void Bush(Vector3 ground, float size, int seed)
        {
            var rot = Quaternion.Euler(0, Hash(seed, 1, 21) * 360f, 0);
            _foliage.Add(seed & 3, ground + Vector3.up * size * 0.35f, rot, new Vector3(size * 1.3f, size, size * 1.2f), seed);
            _foliage.Add((seed + 1) & 3, ground + rot * new Vector3(size * 0.5f, size * 0.25f, 0.2f), rot, new Vector3(size * 0.8f, size * 0.7f, size * 0.8f), seed + 7);
            if (size > 0.9f) Occluder(ground + Vector3.up * size * 0.4f, rot, new Vector3(size * 1.15f, size * 0.85f, size * 1.05f));
        }

        // ---------- Дома ----------

        class Kit
        {
            readonly Dictionary<(Color, DetailKind, bool), MeshBuilder> _b = new();
            public MeshBuilder B(Color c, DetailKind k, bool detail = true)
            {
                var key = (c, k, detail);
                if (!_b.TryGetValue(key, out var mb)) _b[key] = mb = new MeshBuilder();
                return mb;
            }
            public void Box(Color c, DetailKind k, Vector3 center, Vector3 size, Quaternion rot, bool detail = true) => B(c, k, detail).Box(center, size, rot);
            public void Box(Color c, DetailKind k, Vector3 center, Vector3 size, bool detail = true) => B(c, k, detail).Box(center, size, Quaternion.identity);

            public void Build(Transform root, List<Renderer> main, List<Renderer> detail)
            {
                foreach (var kv in _b)
                {
                    var (c, k, d) = kv.Key;
                    float smooth = k == DetailKind.Glass ? 0.9f : k == DetailKind.Metal ? 0.4f : 0.08f;
                    var go = kv.Value.BuildDetail(root, k.ToString(), c, k, smooth, k == DetailKind.Metal ? 0.5f : 0f, !d || k != DetailKind.Glass);
                    if (!go) continue;
                    (d ? detail : main).Add(go.GetComponent<Renderer>());
                }
            }
        }

        static void House(Transform parent, Vector3 pos, float yaw, Vector3 size, bool barn, bool winter, int layer)
        {
            var root = new GameObject(barn ? "Barn" : "Farmhouse").transform;
            root.SetParent(parent); root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            int seed = (int)(pos.x * 7.3f) ^ (int)(pos.z * 3.1f);
            float R(int k) => Hash(seed, k, 31);
            var kit = new Kit();
            float w = size.x, d = size.z, hgt = size.y, top = hgt + 0.2f;

            bool fachwerk = !barn && R(1) < 0.35f, quoins = !barn && !fachwerk && R(2) < 0.7f, damaged = R(3) < 0.35f;
            Color plaster = fachwerk ? new Color(0.84f, 0.8f, 0.7f) : R(4) < 0.5f ? new Color(0.78f, 0.72f, 0.6f) : new Color(0.7f, 0.68f, 0.63f);
            Color stone = new(0.58f, 0.55f, 0.5f), stoneDark = new(0.44f, 0.42f, 0.38f);
            Color timber = new(0.24f, 0.17f, 0.12f), frameCol = R(5) < 0.5f ? new Color(0.86f, 0.84f, 0.8f) : new Color(0.32f, 0.23f, 0.16f);
            Color shutterCol = R(6) < 0.33f ? new Color(0.25f, 0.36f, 0.3f) : R(6) < 0.66f ? new Color(0.27f, 0.33f, 0.45f) : new Color(0.42f, 0.28f, 0.18f);
            Color tiles = barn ? new Color(0.36f, 0.27f, 0.2f) : R(7) < 0.5f ? new Color(0.58f, 0.3f, 0.22f) : new Color(0.32f, 0.33f, 0.36f);
            Color barnWood = new(0.42f, 0.3f, 0.2f), glass = new(0.08f, 0.1f, 0.12f), dark = new(0.06f, 0.05f, 0.05f), snow = new(0.93f, 0.94f, 0.96f);
            Color metal = new(0.38f, 0.38f, 0.36f);

            // Цоколь и стены
            kit.Box(stone, DetailKind.Stone, new Vector3(0, -0.15f, 0), new Vector3(w + 0.3f, 1.1f, d + 0.3f), false);
            kit.Box(barn ? barnWood : plaster, barn ? DetailKind.Planks : DetailKind.Plaster, new Vector3(0, top * 0.5f + 0.2f, 0), new Vector3(w, top - 0.4f, d), false);

            // Крыша
            float rh = w * (barn ? 0.5f : 0.42f), over = 0.5f, gable = 0.45f;
            float hw = w * 0.5f + over, L = Mathf.Sqrt(hw * hw + rh * rh), ang = Mathf.Atan2(rh, hw) * Mathf.Rad2Deg;
            float roofBase = top - over * rh / (w * 0.5f);
            kit.B(barn ? barnWood : plaster, barn ? DetailKind.Planks : DetailKind.Plaster, false).Add(MeshUtil.Prism(), new Vector3(0, top + rh * 0.5f, 0), Quaternion.identity, new Vector3(w, rh, d));
            int holeSeg = damaged && !barn ? (int)(R(8) * 2.99f) : -1;
            for (int s = -1; s <= 1; s += 2)
            {
                var rot = Quaternion.Euler(0, 0, -s * ang);
                for (int seg = 0; seg < 3; seg++)
                {
                    float segLen = (d + gable * 2) / 3f, z = -d * 0.5f - gable + segLen * (seg + 0.5f);
                    Vector3 c = new(s * hw * 0.5f, (top + rh + roofBase) * 0.5f + 0.1f, z);
                    if (s > 0 && seg == holeSeg)
                    {
                        // Обвалившийся участок: остались стропила и обрешётка
                        for (int r = 0; r < 4; r++)
                            kit.Box(timber, DetailKind.Wood, c + new Vector3(0, -0.05f, -segLen * 0.4f + r * segLen * 0.27f), new Vector3(L, 0.14f, 0.12f), rot);
                        kit.Box(timber, DetailKind.Wood, c + rot * new Vector3(L * 0.15f, 0.02f, 0), new Vector3(0.08f, 0.06f, segLen), rot);
                        continue;
                    }
                    kit.Box(tiles, DetailKind.Tiles, c, new Vector3(L, 0.22f, segLen + 0.01f), rot, false);
                    if (winter) kit.Box(snow, DetailKind.Plaster, c + rot * new Vector3(0, 0.16f, 0), new Vector3(L * 0.96f, 0.1f, segLen), rot, false);
                }
                // Ветровые доски по фронтонам и водосток вдоль свеса
                for (int g = -1; g <= 1; g += 2)
                    kit.Box(timber, DetailKind.Wood, new Vector3(s * hw * 0.5f, (top + rh + roofBase) * 0.5f - 0.05f, g * (d * 0.5f + gable)), new Vector3(L, 0.26f, 0.05f), rot);
                if (!barn)
                {
                    kit.B(metal, DetailKind.Metal).Add(MeshUtil.Cylinder(8), new Vector3(s * (hw + 0.05f), roofBase - 0.1f, 0), Quaternion.Euler(90, 0, 0), new Vector3(0.14f, d + gable * 2, 0.14f));
                    kit.B(metal, DetailKind.Metal).Add(MeshUtil.Cylinder(8), new Vector3(s * (w * 0.5f + 0.12f), (roofBase + 0.2f) * 0.5f, d * 0.5f - 0.25f), Quaternion.identity, new Vector3(0.1f, roofBase, 0.1f));
                }
            }
            kit.Box(tiles * 0.8f, DetailKind.Tiles, new Vector3(0, top + rh + 0.05f, 0), new Vector3(0.38f, 0.22f, d + gable * 2 + 0.05f), Quaternion.Euler(0, 0, 45), false);

            // Дымоход
            if (!barn)
            {
                var cpos = new Vector3(w * 0.2f, top + rh * 0.55f, d * 0.22f);
                kit.Box(stoneDark, DetailKind.Stone, cpos, new Vector3(0.75f, rh * 0.9f + 1.6f, 0.75f), false);
                kit.Box(stone, DetailKind.Stone, cpos + Vector3.up * (rh * 0.45f + 0.85f), new Vector3(0.95f, 0.14f, 0.95f));
                kit.Box(dark, DetailKind.Stone, cpos + Vector3.up * (rh * 0.45f + 0.9f), new Vector3(0.45f, 0.06f, 0.45f));
            }

            // Угловой руст
            if (quoins)
                for (int cx = -1; cx <= 1; cx += 2)
                    for (int cz = -1; cz <= 1; cz += 2)
                        for (int k = 0; k * 0.42f + 0.6f < top; k++)
                        {
                            bool lng = (k & 1) == 0;
                            var c = new Vector3(cx * (w * 0.5f - (lng ? 0.22f : 0.12f) + 0.03f), 0.6f + k * 0.42f, cz * (d * 0.5f - (lng ? 0.12f : 0.22f) + 0.03f));
                            kit.Box(stone, DetailKind.Stone, c, new Vector3(lng ? 0.5f : 0.3f, 0.38f, lng ? 0.3f : 0.5f));
                        }

            // Грани стен: 0 перед (+Z), 1 зад, 2 право (+X), 3 лево
            void Face(int f, out Vector3 n, out Vector3 right, out float halfLen, out float off)
            {
                n = f switch { 0 => Vector3.forward, 1 => Vector3.back, 2 => Vector3.right, _ => Vector3.left };
                right = f switch { 0 => Vector3.right, 1 => Vector3.left, 2 => Vector3.back, _ => Vector3.forward };
                halfLen = f < 2 ? w * 0.5f : d * 0.5f; off = f < 2 ? d * 0.5f : w * 0.5f;
            }
            Quaternion FaceRot(Vector3 n) => Quaternion.LookRotation(n);

            // Фахверк
            if (fachwerk)
                for (int f = 0; f < 4; f++)
                {
                    Face(f, out var n, out var right, out float hl, out float off);
                    var fr = FaceRot(n);
                    Vector3 o = n * (off + 0.03f);
                    foreach (float y in hgt > 5 ? new[] { 0.45f, 2.75f, top - 0.1f } : new[] { 0.45f, top - 0.1f })
                        kit.Box(timber, DetailKind.Wood, o + Vector3.up * y, new Vector3(hl * 2, 0.2f, 0.08f), fr);
                    int posts = Mathf.Max(2, Mathf.RoundToInt(hl * 2 / 1.5f));
                    for (int i = 0; i <= posts; i++)
                    {
                        float x = -hl + hl * 2 * i / posts;
                        kit.B(timber, DetailKind.Wood).Beam(o + right * x + Vector3.up * 0.45f, o + right * x + Vector3.up * (top - 0.1f), 0.18f, 0.08f);
                    }
                    kit.B(timber, DetailKind.Wood).Beam(o - right * hl + Vector3.up * 0.5f, o - right * (hl - 1.4f) + Vector3.up * Mathf.Min(2.7f, top - 0.2f), 0.16f, 0.07f);
                    kit.B(timber, DetailKind.Wood).Beam(o + right * hl + Vector3.up * 0.5f, o + right * (hl - 1.4f) + Vector3.up * Mathf.Min(2.7f, top - 0.2f), 0.16f, 0.07f);
                }

            // Окна, двери, ставни
            int floors = !barn && hgt > 5 ? 2 : 1;
            for (int f = 0; f < 4; f++)
            {
                Face(f, out var n, out var right, out float hl, out float off);
                var fr = FaceRot(n);
                Vector3 o = n * off;
                if (barn)
                {
                    if (f == 0)
                    {
                        // Ворота амбара с раскосами и сеновал
                        var gate = o + Vector3.up * 1.8f;
                        kit.Box(barnWood * 0.8f, DetailKind.Planks, gate + n * 0.05f, new Vector3(3.6f, 3.4f, 0.1f).Rotate(fr), fr);
                        kit.B(timber, DetailKind.Wood).Beam(gate + n * 0.12f + right * -1.7f + Vector3.up * -1.6f, gate + n * 0.12f + right * 1.7f + Vector3.up * 1.6f, 0.16f, 0.08f);
                        kit.B(timber, DetailKind.Wood).Beam(gate + n * 0.12f + right * 1.7f + Vector3.up * -1.6f, gate + n * 0.12f + right * -1.7f + Vector3.up * 1.6f, 0.16f, 0.08f);
                        kit.Box(timber, DetailKind.Wood, gate + n * 0.08f + Vector3.up * 1.8f, new Vector3(4f, 0.25f, 0.14f).Rotate(fr), fr);
                        kit.Box(dark, DetailKind.Planks, o + n * 0.02f + Vector3.up * (top - 0.2f + rh * 0.35f), new Vector3(1.5f, 1.2f, 0.05f).Rotate(fr), fr);
                    }
                    continue;
                }
                int count = f < 2 ? Mathf.Max(2, Mathf.FloorToInt(hl * 2 / 2.6f)) : Mathf.Max(1, Mathf.FloorToInt(hl * 2 / 3.4f));
                for (int fl = 0; fl < floors; fl++)
                    for (int i = 0; i < count; i++)
                    {
                        float x = -hl + hl * 2 * (i + 0.5f) / count;
                        bool door = f == 0 && fl == 0 && i == count / 2;
                        float y = fl == 0 ? 1.65f : 4.25f;
                        var c = o + right * x + Vector3.up * y;
                        int k = f * 100 + fl * 10 + i;
                        if (door)
                        {
                            var dc = o + right * x + Vector3.up * 1.25f;
                            kit.Box(shutterCol * 0.85f, DetailKind.Planks, dc + n * 0.03f, new Vector3(1.05f, 2.15f, 0.06f).Rotate(fr), fr);
                            kit.Box(frameCol, DetailKind.Wood, dc + n * 0.06f + Vector3.up * 1.12f, new Vector3(1.35f, 0.14f, 0.1f).Rotate(fr), fr);
                            kit.Box(frameCol, DetailKind.Wood, dc + n * 0.06f + right * 0.6f, new Vector3(0.12f, 2.25f, 0.1f).Rotate(fr), fr);
                            kit.Box(frameCol, DetailKind.Wood, dc + n * 0.06f - right * 0.6f, new Vector3(0.12f, 2.25f, 0.1f).Rotate(fr), fr);
                            kit.Box(metal, DetailKind.Metal, dc + n * 0.08f + right * 0.38f, new Vector3(0.05f, 0.05f, 0.08f));
                            kit.Box(stone, DetailKind.Stone, o + right * x + n * 0.35f + Vector3.up * 0.12f, new Vector3(1.6f, 0.24f, 0.6f).Rotate(fr), fr, false);
                            // Козырёк над дверью
                            var cr = fr * Quaternion.Euler(20, 0, 0);
                            kit.Box(tiles, DetailKind.Tiles, dc + Vector3.up * 1.42f + n * 0.4f, new Vector3(1.8f, 0.08f, 0.85f).Rotate(fr), cr);
                            kit.B(timber, DetailKind.Wood).Beam(dc + n * 0.02f + right * 0.75f + Vector3.up * 0.95f, dc + n * 0.7f + right * 0.75f + Vector3.up * 1.32f, 0.07f, 0.07f);
                            kit.B(timber, DetailKind.Wood).Beam(dc + n * 0.02f - right * 0.75f + Vector3.up * 0.95f, dc + n * 0.7f - right * 0.75f + Vector3.up * 1.32f, 0.07f, 0.07f);
                            continue;
                        }
                        bool broken = R(40 + k) < (damaged ? 0.45f : 0.12f);
                        // Стекло (или чёрный проём, если выбито)
                        kit.Box(broken ? dark : glass, broken ? DetailKind.Plaster : DetailKind.Glass, c + n * 0.015f, new Vector3(0.9f, 1.2f, 0.03f).Rotate(fr), fr);
                        // Наличник и переплёт
                        kit.Box(frameCol, DetailKind.Wood, c + n * 0.05f + Vector3.up * 0.64f, new Vector3(1.08f, 0.09f, 0.08f).Rotate(fr), fr);
                        kit.Box(frameCol, DetailKind.Wood, c + n * 0.05f - Vector3.up * 0.64f, new Vector3(1.08f, 0.09f, 0.08f).Rotate(fr), fr);
                        kit.Box(frameCol, DetailKind.Wood, c + n * 0.05f + right * 0.5f, new Vector3(0.09f, 1.36f, 0.08f).Rotate(fr), fr);
                        kit.Box(frameCol, DetailKind.Wood, c + n * 0.05f - right * 0.5f, new Vector3(0.09f, 1.36f, 0.08f).Rotate(fr), fr);
                        if (!broken)
                        {
                            kit.Box(frameCol, DetailKind.Wood, c + n * 0.04f, new Vector3(0.05f, 1.2f, 0.05f).Rotate(fr), fr);
                            kit.Box(frameCol, DetailKind.Wood, c + n * 0.04f + Vector3.up * 0.15f, new Vector3(0.9f, 0.05f, 0.05f).Rotate(fr), fr);
                        }
                        // Подоконник и перемычка
                        kit.Box(stone, DetailKind.Stone, c + n * 0.1f - Vector3.up * 0.72f, new Vector3(1.2f, 0.08f, 0.2f).Rotate(fr), fr);
                        if (!fachwerk) kit.Box(stone, DetailKind.Stone, c + n * 0.04f + Vector3.up * 0.78f, new Vector3(1.25f, 0.16f, 0.08f).Rotate(fr), fr);
                        // Ставни: открытые, закрытые или сорванная одна
                        float sr = R(70 + k);
                        if (sr < 0.55f)
                        {
                            kit.Box(shutterCol, DetailKind.Planks, c + n * 0.05f + right * 0.82f, new Vector3(0.5f, 1.3f, 0.05f).Rotate(fr), fr);
                            if (sr > 0.12f) kit.Box(shutterCol, DetailKind.Planks, c + n * 0.05f - right * 0.82f, new Vector3(0.5f, 1.3f, 0.05f).Rotate(fr), fr);
                            else kit.Box(shutterCol, DetailKind.Planks, o + right * (x - 0.9f) + n * 0.3f + Vector3.up * 0.1f, new Vector3(0.5f, 0.05f, 1.3f).Rotate(fr), fr * Quaternion.Euler(0, 0, 12));
                        }
                        else if (sr < 0.7f)
                        {
                            kit.Box(shutterCol, DetailKind.Planks, c + n * 0.06f + right * 0.24f, new Vector3(0.48f, 1.3f, 0.05f).Rotate(fr), fr);
                            kit.Box(shutterCol, DetailKind.Planks, c + n * 0.06f - right * 0.24f, new Vector3(0.48f, 1.3f, 0.05f).Rotate(fr), fr);
                        }
                    }
            }

            // Следы обстрела: пробоины и обломки
            if (damaged)
                for (int i = 0; i < 2 + (int)(R(90) * 3); i++)
                {
                    int f = (int)(R(91 + i) * 3.99f);
                    Face(f, out var n, out var right, out float hl, out float off);
                    var fr = FaceRot(n);
                    var c = n * (off + 0.01f) + right * ((R(95 + i) - 0.5f) * hl * 1.5f) + Vector3.up * (0.8f + R(97 + i) * (top - 1.6f));
                    kit.Box(dark, DetailKind.Stone, c, new Vector3(0.7f + R(99 + i) * 0.8f, 0.6f + R(101 + i) * 0.7f, 0.04f).Rotate(fr), fr * Quaternion.Euler(0, 0, R(103 + i) * 40f));
                    kit.Box(stoneDark, DetailKind.Stone, c + n * 0.03f, new Vector3(1.2f, 1.0f, 0.03f).Rotate(fr), fr * Quaternion.Euler(0, 0, R(105 + i) * 60f));
                    for (int r = 0; r < 4; r++)
                        kit.Box(stone, DetailKind.Stone, n * (off + 0.5f + R(110 + r + i) * 1.2f) + right * ((R(120 + r + i) - 0.5f) * hl) + Vector3.up * 0.1f,
                                new Vector3(0.3f + R(130 + r) * 0.4f, 0.2f + R(140 + r) * 0.25f, 0.3f + R(150 + r) * 0.4f), Quaternion.Euler(R(160 + r) * 40, R(170 + r) * 360, 0));
                }

            var main = new List<Renderer>(); var detail = new List<Renderer>();
            kit.Build(root, main, detail);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.gameObject.layer = layer;

            // Коллайдер стен (от земли с запасом вниз на склонах) и ската крыши
            var bc = root.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, (top - 0.6f) * 0.5f, 0); bc.size = new Vector3(w, top + 0.6f, d);
            root.gameObject.layer = layer;

            // Кусты у стен
            for (int i = 0; i < 2; i++)
            {
                float a = R(180 + i) * Mathf.PI * 2;
                var p = root.TransformPoint(new Vector3(Mathf.Cos(a) * (w * 0.5f + 1.2f), 0, Mathf.Sin(a) * (d * 0.5f + 1.2f)));
                Bush(p, 1.1f + R(185 + i) * 0.8f, seed + i * 13);
            }

            // LOD: вдали остаются стены, крыша и цоколь
            var lod = root.gameObject.AddComponent<LODGroup>();
            var all = new List<Renderer>(main); all.AddRange(detail);
            lod.SetLODs(new[] { new LOD(0.09f, all.ToArray()), new LOD(0.012f, main.ToArray()) });
            lod.RecalculateBounds();
        }
    }

    static class VecExt
    {
        /// <summary>Размер коробки задан в осях грани — MeshBuilder сам поворачивает, поэтому размер оставляем как есть.</summary>
        public static Vector3 Rotate(this Vector3 size, Quaternion _) => size;
    }
}
