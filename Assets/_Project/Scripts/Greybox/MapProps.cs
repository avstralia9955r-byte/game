using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>Сборщик одного меша из множества коробок (рельсы, шпалы, заборы, проволока, столбы) — один draw call на чанк.</summary>
    public class MeshBuilder
    {
        readonly List<Vector3> _v = new(); readonly List<int> _t = new();
        static readonly Vector3[] C =
        {
            new(-.5f,-.5f,-.5f), new(.5f,-.5f,-.5f), new(.5f,.5f,-.5f), new(-.5f,.5f,-.5f),
            new(-.5f,-.5f,.5f), new(.5f,-.5f,.5f), new(.5f,.5f,.5f), new(-.5f,.5f,.5f)
        };
        static readonly int[] F = { 0,3,2,1, 4,5,6,7, 0,4,7,3, 1,2,6,5, 3,7,6,2, 0,1,5,4 };

        public int VertexCount => _v.Count;

        public void Box(Vector3 center, Vector3 size, Quaternion rot)
        {
            for (int f = 0; f < 6; f++)
            {
                int k = _v.Count;
                Vector3 n = Vector3.zero;
                for (int i = 0; i < 4; i++) { var p = C[F[f * 4 + i]]; n += p; _v.Add(center + rot * Vector3.Scale(p, size)); }
                // Ориентация наружу: нормаль треугольника должна смотреть от центра коробки
                Vector3 a = _v[k], b = _v[k + 1], c = _v[k + 2];
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), rot * n) < 0;
                if (!flip) { _t.Add(k); _t.Add(k + 1); _t.Add(k + 2); _t.Add(k); _t.Add(k + 2); _t.Add(k + 3); }
                else { _t.Add(k); _t.Add(k + 2); _t.Add(k + 1); _t.Add(k); _t.Add(k + 3); _t.Add(k + 2); }
            }
        }

        public void Box(Vector3 center, Vector3 size) => Box(center, size, Quaternion.identity);

        /// <summary>Балка между двумя точками (провод, жердь, рельс).</summary>
        public void Beam(Vector3 a, Vector3 b, float w, float h)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < 0.01f) return;
            Box((a + b) * 0.5f, new Vector3(w, h, len), Quaternion.LookRotation(d / len));
        }

        /// <summary>Добавить произвольный меш (икосфера, призма, цилиндр) с трансформацией.</summary>
        public void Add(Mesh src, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var m = Matrix4x4.TRS(pos, rot, scale);
            var v = src.vertices; var t = src.triangles;
            int k = _v.Count;
            for (int i = 0; i < v.Length; i++) _v.Add(m.MultiplyPoint3x4(v[i]));
            bool flip = m.determinant < 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                _t.Add(k + t[i]);
                _t.Add(k + (flip ? t[i + 2] : t[i + 1]));
                _t.Add(k + (flip ? t[i + 1] : t[i + 2]));
            }
        }

        /// <summary>Сборка с процедурной текстурой (UV — бокс-проекция в метрах по нормали грани).</summary>
        public GameObject BuildDetail(Transform parent, string name, Color color, DetailKind kind, float smooth = 0.1f, float metal = 0, bool shadows = true, float tile = 0)
        {
            if (_v.Count == 0) return null;
            var m = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(_v); m.SetTriangles(_t, 0); m.RecalculateNormals();
            var n = m.normals; var uv = new Vector2[_v.Count];
            float tl = tile > 0 ? tile : ProceduralModels.DetailTile(kind);
            for (int i = 0; i < uv.Length; i++)
            {
                var p = _v[i]; var a = new Vector3(Mathf.Abs(n[i].x), Mathf.Abs(n[i].y), Mathf.Abs(n[i].z));
                uv[i] = (a.x >= a.y && a.x >= a.z ? new Vector2(p.z, p.y) : a.y >= a.z ? new Vector2(p.z, p.x) : new Vector2(p.x, p.y)) / tl;
            }
            m.uv = uv; m.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = ProceduralModels.DetailMat(color, kind, smooth, metal);
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            _v.Clear(); _t.Clear();
            return go;
        }

        public GameObject Build(Transform parent, string name, Color color, float smooth = 0.15f, float metal = 0, bool shadows = true)
        {
            if (_v.Count == 0) return null;
            var m = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(_v); m.SetTriangles(_t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = ProceduralModels.Mat(color, smooth, metal);
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            _v.Clear(); _t.Clear();
            return go;
        }
    }

    /// <summary>
    /// Процедурные объекты карты: подбитая техника, противотанковые ежи, мельница, усадьба, вокзал, вагоны,
    /// паровоз, водонапорная башня, стога, колодцы, телеги, ящики, вышки, руины замка, валуны, облака.
    /// </summary>
    public static class MapProps
    {
        static GameObject Bx(Transform p, string n, Vector3 pos, Vector3 size, Color c, int layer = -1, Vector3 euler = default, bool bevel = false)
        {
            var go = bevel ? ProceduralModels.Part(p, n, MeshUtil.Bevel(0.1f), pos, size, c, euler, layer >= 0)
                           : ProceduralModels.Box(p, n, pos, size, c, layer >= 0, euler);
            if (layer >= 0) go.layer = layer;
            return go;
        }

        static GameObject Cyl(Transform p, string n, Vector3 pos, float r, float h, Color c, Vector3 euler = default, int seg = 10, float top = 1f)
            => ProceduralModels.Part(p, n, top == 1f ? MeshUtil.Cylinder(seg) : MeshUtil.Frustum(seg, top), pos, new Vector3(r * 2, h, r * 2), c, euler);

        static Transform Node(Transform parent, string name, Vector3 pos, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent); t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            return t;
        }

        static readonly Color Rust = new(0.33f, 0.2f, 0.13f), Soot = new(0.1f, 0.1f, 0.1f), Track = new(0.16f, 0.15f, 0.14f),
            Stone = new(0.66f, 0.63f, 0.57f), Plank = new(0.4f, 0.29f, 0.19f), Snow = new(0.93f, 0.94f, 0.96f);

        // ---------------- Техника ----------------

        /// <summary>Подбитый танк (силуэты Pz.IV / Т-34): корпус, наклонный лоб, гусеницы с катками, башня с пушкой, открытый люк.</summary>
        public static Transform Tank(Transform parent, Vector3 pos, float yaw, bool german, bool winter, int layer, System.Random rng)
        {
            var t = Node(parent, german ? "Wreck_PzIV" : "Wreck_T34", pos, yaw);
            Color hull = winter ? new Color(0.78f, 0.79f, 0.78f) : german ? new Color(0.36f, 0.37f, 0.36f) : new Color(0.25f, 0.31f, 0.2f);
            hull = Color.Lerp(hull, Soot, 0.35f);
            float tilt = (float)(rng.NextDouble() - 0.5) * 6;
            t.rotation *= Quaternion.Euler(tilt, 0, tilt * 0.6f);
            Bx(t, "Hull", new Vector3(0, 1.05f, 0), new Vector3(2.9f, 0.95f, 5.6f), hull, layer, default, true);
            if (german) Bx(t, "Superstructure", new Vector3(0, 1.65f, 0.2f), new Vector3(2.7f, 0.45f, 3.2f), hull, -1, default, true);
            Bx(t, "Glacis", new Vector3(0, 1.2f, 2.9f), new Vector3(2.8f, 0.9f, 1.1f), hull, -1, new Vector3(german ? 20 : 50, 0, 0), true);
            Bx(t, "EngineDeck", new Vector3(0, 1.6f, -2.0f), new Vector3(2.4f, 0.25f, 1.5f), Color.Lerp(hull, Rust, 0.5f), -1, new Vector3(-6, 0, 0), true);
            for (int s = -1; s <= 1; s += 2)
            {
                Bx(t, "Track", new Vector3(s * 1.3f, 0.5f, 0), new Vector3(0.55f, 0.95f, 6.1f), Track, -1, default, true);
                Bx(t, "Fender", new Vector3(s * 1.3f, 1.05f, 0), new Vector3(0.7f, 0.06f, 5.9f), hull);
                int wheels = german ? 8 : 5;
                for (int i = 0; i < wheels; i++)
                    Cyl(t, "Wheel", new Vector3(s * 1.58f, 0.45f, -2.4f + i * (4.8f / (wheels - 1))), german ? 0.28f : 0.42f, 0.12f, Color.Lerp(hull, Track, 0.5f), new Vector3(0, 0, 90), 8);
            }
            // Башня развёрнута в сторону, как после попадания
            var tur = Node(t, "Turret", t.TransformPoint(new Vector3(0, german ? 2.25f : 1.9f, 0.3f)), 0);
            tur.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 120 - 60, (float)rng.NextDouble() * 4);
            if (german) Bx(tur, "TurretBody", Vector3.zero, new Vector3(2.0f, 0.75f, 2.5f), hull, layer, default, true);
            else ProceduralModels.Part(tur, "TurretBody", MeshUtil.Frustum(8, 0.7f), Vector3.zero, new Vector3(2.5f, 0.75f, 2.9f), hull, default, true).layer = layer;
            Bx(tur, "Mantlet", new Vector3(0, 0, 1.3f), new Vector3(1.1f, 0.6f, 0.35f), hull, -1, default, true);
            Cyl(tur, "Gun", new Vector3(0, 0, 2.9f), 0.09f, 3.2f, Color.Lerp(hull, Soot, 0.4f), new Vector3(90 + (float)rng.NextDouble() * 8, 0, 0), 8);
            Bx(tur, "Hatch", new Vector3(0.4f, 0.65f, -0.3f), new Vector3(0.7f, 0.06f, 0.7f), hull, -1, new Vector3(-70, 0, 0));
            Bx(tur, "Cupola", new Vector3(-0.4f, 0.48f, -0.4f), new Vector3(0.65f, 0.25f, 0.65f), hull, -1, default, true);
            Bx(t, "Scorch", new Vector3(0.6f, 1.56f, -0.6f), new Vector3(1.6f, 0.03f, 1.8f), Soot);
            return t;
        }

        /// <summary>Брошенный грузовик (Opel Blitz / ЗИС-5): рама, кабина с капотом, кузов с тентом, колёса.</summary>
        public static Transform Truck(Transform parent, Vector3 pos, float yaw, bool german, bool winter, int layer)
        {
            var t = Node(parent, "Wreck_Truck", pos, yaw);
            Color body = winter ? new Color(0.75f, 0.76f, 0.75f) : german ? new Color(0.35f, 0.37f, 0.35f) : new Color(0.27f, 0.33f, 0.22f);
            Bx(t, "Frame", new Vector3(0, 0.75f, 0), new Vector3(1.9f, 0.25f, 5.6f), Track);
            Bx(t, "Hood", new Vector3(0, 1.35f, 2.2f), new Vector3(1.4f, 0.9f, 1.4f), body, layer, default, true);
            Bx(t, "Grille", new Vector3(0, 1.3f, 2.92f), new Vector3(1.1f, 0.8f, 0.06f), Track);
            Bx(t, "Cab", new Vector3(0, 1.75f, 0.9f), new Vector3(2.0f, 1.6f, 1.4f), body, layer, default, true);
            Bx(t, "Windshield", new Vector3(0, 2.05f, 1.62f), new Vector3(1.6f, 0.6f, 0.04f), new Color(0.15f, 0.2f, 0.25f));
            Bx(t, "Bed", new Vector3(0, 1.3f, -1.4f), new Vector3(2.2f, 0.9f, 3.2f), Plank, layer);
            ProceduralModels.Part(t, "Canvas", MeshUtil.Prism(), new Vector3(0, 2.35f, -1.4f), new Vector3(2.3f, 1.2f, 3.2f), Color.Lerp(body, new Color(0.5f, 0.47f, 0.35f), 0.6f));
            foreach (var w in new[] { new Vector3(1, 0.5f, 2.1f), new Vector3(-1, 0.5f, 2.1f), new Vector3(1, 0.5f, -1.8f), new Vector3(-1, 0.5f, -1.8f) })
                Cyl(t, "Wheel", w, 0.5f, 0.3f, Soot, new Vector3(0, 0, 90), 10);
            return t;
        }

        /// <summary>Противотанковый ёж: три сваренные балки.</summary>
        public static void Hedgehog(Transform parent, Vector3 pos, float yaw, int layer)
        {
            var t = Node(parent, "Hedgehog", pos + Vector3.up * 0.55f, yaw);
            Bx(t, "A", Vector3.zero, new Vector3(0.14f, 0.14f, 1.9f), Rust, -1, new Vector3(35, 0, 0), true);
            Bx(t, "B", Vector3.zero, new Vector3(0.14f, 0.14f, 1.9f), Rust, -1, new Vector3(-35, 90, 0), true);
            Bx(t, "C", Vector3.zero, new Vector3(0.14f, 1.9f, 0.14f), Rust, -1, new Vector3(0, 45, 40), true);
            var col = t.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(1.1f, 1.2f, 1.1f);
            if (layer >= 0) t.gameObject.layer = layer;
        }

        // ---------------- Ориентиры ----------------

        /// <summary>Ветряная мельница: каменная башня, шатёр, вращающиеся крылья.</summary>
        public static void Windmill(Transform statics, Transform dynamics, Vector3 pos, float yaw, bool winter, int layer)
        {
            var t = Node(statics, "Windmill", pos, yaw);
            ProceduralModels.Part(t, "Tower", MeshUtil.Frustum(10, 0.68f), new Vector3(0, 5.5f, 0), new Vector3(7, 11, 7), new Color(0.82f, 0.79f, 0.72f));
            var col = t.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, 5.5f, 0); col.size = new Vector3(5.2f, 11, 5.2f);
            t.gameObject.layer = layer;
            ProceduralModels.Part(t, "Cap", MeshUtil.Cone(10), new Vector3(0, 11, 0), new Vector3(5.6f, 3.4f, 5.6f), winter ? Snow : new Color(0.3f, 0.22f, 0.17f));
            Bx(t, "Door", new Vector3(0, 1.1f, 3.4f), new Vector3(1.2f, 2.2f, 0.2f), Plank);
            Bx(t, "Window", new Vector3(0, 6.5f, 2.95f), new Vector3(0.8f, 1, 0.2f), Soot);
            Bx(t, "Gallery", new Vector3(0, 3.6f, 0), new Vector3(8.4f, 0.2f, 8.4f), Plank);
            Cyl(t, "Axle", new Vector3(0, 10.2f, 2.6f), 0.25f, 2.4f, Plank, new Vector3(90, 0, 0), 6);
            var sails = Node(dynamics, "WindmillSails", t.TransformPoint(new Vector3(0, 10.2f, 3.9f)), yaw);
            for (int i = 0; i < 4; i++)
            {
                var arm = Node(sails, "Arm" + i, sails.position, 0); arm.localRotation = Quaternion.Euler(0, 0, i * 90);
                Bx(arm, "Spar", new Vector3(0, 3.6f, 0), new Vector3(0.18f, 7.2f, 0.14f), Plank);
                Bx(arm, "Sail", new Vector3(0.75f, 4.2f, 0), new Vector3(1.4f, 5.4f, 0.05f), new Color(0.85f, 0.82f, 0.72f));
                for (int k = 0; k < 5; k++) Bx(arm, "Lath", new Vector3(0.75f, 1.9f + k * 1.15f, 0.04f), new Vector3(1.5f, 0.06f, 0.05f), Plank);
            }
            sails.gameObject.AddComponent<Spinner>().degreesPerSecond = new Vector3(0, 0, 14);
        }

        /// <summary>Усадьба (шато): главный корпус с вальмовой крышей, две башни со шпилями, крыльцо, ограда с воротами.</summary>
        public static void Manor(Transform parent, Vector3 pos, float yaw, bool winter, int layer)
        {
            var t = Node(parent, "Manor", pos, yaw);
            Color wall = new(0.78f, 0.73f, 0.62f), roof = winter ? Snow : new Color(0.25f, 0.27f, 0.32f), trim = new(0.9f, 0.88f, 0.82f);
            Bx(t, "Main", new Vector3(0, 4.5f, 0), new Vector3(24, 9, 11), wall, layer);
            ProceduralModels.Part(t, "Roof", MeshUtil.Prism(), new Vector3(0, 11.4f, 0), new Vector3(12, 5, 25), roof, new Vector3(0, 90, 0));
            Bx(t, "Cornice", new Vector3(0, 8.9f, 0), new Vector3(24.6f, 0.35f, 11.6f), trim);
            for (int s = -1; s <= 1; s += 2)
            {
                Cyl(t, "Tower", new Vector3(s * 12.5f, 6.5f, 4.5f), 2.6f, 13, wall, default, 10).AddComponent<BoxCollider>().gameObject.layer = layer;
                ProceduralModels.Part(t, "Spire", MeshUtil.Cone(10), new Vector3(s * 12.5f, 13, 4.5f), new Vector3(6, 6.5f, 6), roof);
                Bx(t, "Chimney", new Vector3(s * 7, 13, -1), new Vector3(1.1f, 3, 1.1f), wall * 0.9f);
            }
            Bx(t, "Porch", new Vector3(0, 0.4f, 6.6f), new Vector3(7, 0.8f, 2.4f), Stone, layer);
            Bx(t, "PorchRoof", new Vector3(0, 4.2f, 6.6f), new Vector3(7.4f, 0.3f, 2.8f), trim);
            for (int s = -1; s <= 1; s += 2) Cyl(t, "Column", new Vector3(s * 3, 2.3f, 7.4f), 0.25f, 3.6f, trim, default, 8);
            Bx(t, "Door", new Vector3(0, 2, 5.56f), new Vector3(1.8f, 3, 0.15f), new Color(0.25f, 0.17f, 0.12f));
            for (int fl = 0; fl < 2; fl++)
                for (int i = -4; i <= 4; i++)
                {
                    if (i == 0 && fl == 0) continue;
                    Bx(t, "Window", new Vector3(i * 2.5f, 2.6f + fl * 3.6f, 5.53f), new Vector3(1.1f, 1.9f, 0.1f), new Color(0.14f, 0.17f, 0.24f));
                    Bx(t, "WindowBack", new Vector3(i * 2.5f, 2.6f + fl * 3.6f, -5.53f), new Vector3(1.1f, 1.9f, 0.1f), new Color(0.14f, 0.17f, 0.24f));
                }
            // Ограда парка с воротами спереди
            float gw = 30, gd = 26;
            Bx(t, "WallBack", new Vector3(0, 0.9f, -gd), new Vector3(gw * 2, 1.8f, 0.6f), Stone, layer);
            Bx(t, "WallL", new Vector3(-gw, 0.9f, 0), new Vector3(0.6f, 1.8f, gd * 2), Stone, layer);
            Bx(t, "WallR", new Vector3(gw, 0.9f, 0), new Vector3(0.6f, 1.8f, gd * 2), Stone, layer);
            Bx(t, "WallFL", new Vector3(-gw * 0.56f, 0.9f, gd), new Vector3(gw * 0.88f, 1.8f, 0.6f), Stone, layer);
            Bx(t, "WallFR", new Vector3(gw * 0.56f, 0.9f, gd), new Vector3(gw * 0.88f, 1.8f, 0.6f), Stone, layer);
            for (int s = -1; s <= 1; s += 2) { Bx(t, "GatePost", new Vector3(s * 3.4f, 1.5f, gd), new Vector3(1.1f, 3, 1.1f), Stone, layer); ProceduralModels.Part(t, "PostCap", MeshUtil.Cone(4), new Vector3(s * 3.4f, 3, gd), new Vector3(1.4f, 0.8f, 1.4f), trim, new Vector3(0, 45, 0)); }
            Cyl(t, "Fountain", new Vector3(0, 0.35f, 15), 2.2f, 0.7f, Stone, default, 10);
            Cyl(t, "FountainCore", new Vector3(0, 1.2f, 15), 0.4f, 1.8f, trim, default, 8);
        }

        /// <summary>Вокзал: здание с навесом на столбах, перрон, табличка, водонапорная башня.</summary>
        public static void Station(Transform parent, Vector3 pos, bool winter, int layer)
        {
            var t = Node(parent, "Station", pos, 0);
            Color brick = new(0.6f, 0.35f, 0.27f), roof = winter ? Snow : new Color(0.3f, 0.2f, 0.17f);
            Bx(t, "Platform", new Vector3(0, 0.45f, 8.5f), new Vector3(46, 0.9f, 5), Stone, layer);
            Bx(t, "Building", new Vector3(0, 2.8f, 0), new Vector3(18, 5.6f, 8), brick, layer);
            ProceduralModels.Part(t, "Roof", MeshUtil.Prism(), new Vector3(0, 7.2f, 0), new Vector3(9.4f, 3.2f, 19), roof, new Vector3(0, 90, 0));
            Bx(t, "Canopy", new Vector3(0, 4.2f, 7.5f), new Vector3(24, 0.2f, 6), roof);
            for (int i = -2; i <= 2; i++) Cyl(t, "Post", new Vector3(i * 5.5f, 2.5f, 10), 0.14f, 3.4f, new Color(0.2f, 0.25f, 0.2f), default, 6);
            Bx(t, "Sign", new Vector3(0, 3.5f, 4.1f), new Vector3(4, 0.7f, 0.1f), new Color(0.9f, 0.9f, 0.85f));
            Bx(t, "Clock", new Vector3(0, 5, 4.1f), new Vector3(0.9f, 0.9f, 0.12f), new Color(0.95f, 0.95f, 0.9f));
            for (int i = -3; i <= 3; i++) if (i != 0) Bx(t, "Window", new Vector3(i * 2.4f, 2.7f, 4.03f), new Vector3(1.1f, 1.8f, 0.08f), new Color(0.14f, 0.17f, 0.24f));
            Bx(t, "Door", new Vector3(0, 1.2f, 4.03f), new Vector3(1.6f, 2.4f, 0.1f), Plank);
            for (int i = 0; i < 3; i++) Bx(t, "Bench", new Vector3(-12 + i * 12, 1.15f, 6.6f), new Vector3(2.2f, 0.12f, 0.5f), Plank);
            // Водонапорная башня
            var w = Node(t, "WaterTower", t.TransformPoint(new Vector3(30, 0, 0)), 0);
            for (int i = 0; i < 4; i++) Cyl(w, "Leg", new Vector3((i % 2 * 2 - 1) * 1.6f, 4, (i / 2 * 2 - 1) * 1.6f), 0.18f, 8, Plank, default, 6);
            Cyl(w, "Tank", new Vector3(0, 9.6f, 0), 2.6f, 3.4f, new Color(0.35f, 0.3f, 0.25f), default, 12);
            ProceduralModels.Part(w, "TankRoof", MeshUtil.Cone(12), new Vector3(0, 11.3f, 0), new Vector3(5.6f, 1.4f, 5.6f), roof);
            var c = w.gameObject.AddComponent<BoxCollider>(); c.center = new Vector3(0, 5, 0); c.size = new Vector3(3.6f, 10, 3.6f); w.gameObject.layer = layer;
        }

        /// <summary>Товарный вагон на рельсах (ось — вдоль X).</summary>
        public static void Wagon(Transform parent, Vector3 pos, int kind, bool winter, int layer)
        {
            var t = Node(parent, "Wagon", pos, 90);
            Bx(t, "Chassis", new Vector3(0, 0.9f, 0), new Vector3(2.6f, 0.3f, 9), Track, layer);
            if (kind == 0)
            {
                Bx(t, "Box", new Vector3(0, 2.4f, 0), new Vector3(2.8f, 2.7f, 8.6f), new Color(0.42f, 0.24f, 0.18f), layer, default, true);
                Bx(t, "Roof", new Vector3(0, 3.85f, 0), new Vector3(3, 0.2f, 8.9f), winter ? Snow : new Color(0.25f, 0.22f, 0.2f));
                Bx(t, "DoorOpen", new Vector3(1.43f, 2.3f, 2), new Vector3(0.08f, 2.3f, 2.2f), new Color(0.35f, 0.2f, 0.15f));
                Bx(t, "DoorGap", new Vector3(1.41f, 2.3f, -0.2f), new Vector3(0.06f, 2.2f, 2), Soot);
            }
            else if (kind == 1)
            {
                Bx(t, "Deck", new Vector3(0, 1.15f, 0), new Vector3(2.8f, 0.2f, 8.8f), Plank, layer);
                for (int i = 0; i < 4; i++) Bx(t, "Crate", new Vector3((i % 2 - 0.5f) * 1.2f, 1.85f, -2.5f + i * 1.6f), new Vector3(1.1f, 1.2f, 1.4f), Plank, layer, new Vector3(0, i * 9, 0), true);
            }
            else
            {
                Cyl(t, "Cistern", new Vector3(0, 2.3f, 0), 1.3f, 8, new Color(0.2f, 0.2f, 0.2f), new Vector3(90, 0, 0), 10).AddComponent<BoxCollider>().gameObject.layer = layer;
                Cyl(t, "Dome", new Vector3(0, 3.7f, 0), 0.5f, 0.5f, Soot, default, 8);
            }
            foreach (var z in new[] { -3f, -2f, 2f, 3f })
                for (int s = -1; s <= 1; s += 2) Cyl(t, "Wheel", new Vector3(s * 0.8f, 0.5f, z), 0.48f, 0.14f, Soot, new Vector3(0, 0, 90), 10);
        }

        public static void Locomotive(Transform parent, Vector3 pos, bool winter, int layer)
        {
            var t = Node(parent, "Locomotive", pos, 90);
            Bx(t, "Chassis", new Vector3(0, 0.95f, 0), new Vector3(2.6f, 0.4f, 10), Track, layer);
            Cyl(t, "Boiler", new Vector3(0, 2.5f, 1.2f), 1.25f, 6.4f, new Color(0.13f, 0.13f, 0.14f), new Vector3(90, 0, 0), 12).AddComponent<BoxCollider>().gameObject.layer = layer;
            Bx(t, "Cab", new Vector3(0, 2.7f, -3.4f), new Vector3(2.9f, 3, 2.6f), new Color(0.15f, 0.16f, 0.15f), layer, default, true);
            Bx(t, "CabRoof", new Vector3(0, 4.3f, -3.4f), new Vector3(3.2f, 0.18f, 3), winter ? Snow : Soot);
            Cyl(t, "Chimney", new Vector3(0, 4.1f, 3.6f), 0.35f, 1.2f, Soot, default, 8, 1.3f);
            Cyl(t, "SteamDome", new Vector3(0, 3.9f, 1.2f), 0.5f, 0.6f, new Color(0.5f, 0.4f, 0.2f), default, 8);
            ProceduralModels.Part(t, "Cowcatcher", MeshUtil.Prism(), new Vector3(0, 0.6f, 5.3f), new Vector3(2.6f, 0.9f, 1.2f), new Color(0.5f, 0.12f, 0.1f), new Vector3(90, 0, 0));
            for (int i = 0; i < 4; i++)
                for (int s = -1; s <= 1; s += 2) Cyl(t, "DriveWheel", new Vector3(s * 0.85f, 0.85f, -1.8f + i * 1.75f), 0.82f, 0.16f, new Color(0.45f, 0.1f, 0.08f), new Vector3(0, 0, 90), 12);
            for (int s = -1; s <= 1; s += 2) Bx(t, "Rod", new Vector3(s * 0.98f, 0.85f, 0.8f), new Vector3(0.06f, 0.12f, 5.6f), new Color(0.6f, 0.6f, 0.62f));
            // Тендер
            Bx(t, "Tender", new Vector3(0, 2.1f, -7.6f), new Vector3(2.8f, 2.2f, 5), new Color(0.14f, 0.14f, 0.15f), layer, default, true);
            Bx(t, "Coal", new Vector3(0, 3.25f, -7.6f), new Vector3(2.4f, 0.4f, 4.4f), Soot);
        }

        // ---------------- Мелкие объекты ----------------

        public static void Haystack(Transform parent, Vector3 pos, bool winter, int layer)
        {
            var go = ProceduralModels.Part(parent, "Haystack", MeshUtil.Ico(), pos + Vector3.up * 1.0f, new Vector3(3.4f, 3.2f, 3.4f), winter ? new Color(0.8f, 0.78f, 0.68f) : new Color(0.78f, 0.66f, 0.3f));
            go.AddComponent<BoxCollider>().size = new Vector3(0.75f, 0.8f, 0.75f); go.layer = layer;
            if (winter) ProceduralModels.Part(parent, "SnowCap", MeshUtil.Ico(), pos + Vector3.up * 2.1f, new Vector3(2.8f, 1.2f, 2.8f), Snow);
        }

        public static void LogPile(Transform parent, Vector3 pos, float yaw, int layer)
        {
            var t = Node(parent, "LogPile", pos, yaw);
            for (int r = 0; r < 3; r++)
                for (int i = 0; i < 4 - r; i++)
                    Cyl(t, "Log", new Vector3((i - (3 - r) * 0.5f) * 0.5f, 0.25f + r * 0.44f, 0), 0.25f, 3.2f, new Color(0.38f, 0.27f, 0.18f), new Vector3(90, 0, 0), 7);
            var c = t.gameObject.AddComponent<BoxCollider>(); c.center = new Vector3(0, 0.7f, 0); c.size = new Vector3(2.1f, 1.4f, 3.2f); t.gameObject.layer = layer;
        }

        public static void Well(Transform parent, Vector3 pos, bool winter)
        {
            var t = Node(parent, "Well", pos, 0);
            Cyl(t, "Ring", new Vector3(0, 0.5f, 0), 1.1f, 1, Stone, default, 10).AddComponent<BoxCollider>();
            Cyl(t, "Water", new Vector3(0, 0.95f, 0), 0.9f, 0.1f, new Color(0.1f, 0.15f, 0.2f), default, 10);
            for (int s = -1; s <= 1; s += 2) Bx(t, "Post", new Vector3(s * 1, 1.6f, 0), new Vector3(0.15f, 2.2f, 0.15f), Plank);
            ProceduralModels.Part(t, "Roof", MeshUtil.Prism(), new Vector3(0, 3, 0), new Vector3(2.6f, 0.9f, 1.6f), winter ? Snow : new Color(0.35f, 0.22f, 0.17f));
            Cyl(t, "Winch", new Vector3(0, 2.2f, 0), 0.1f, 2, Plank, new Vector3(0, 0, 90), 6);
        }

        public static void Cart(Transform parent, Vector3 pos, float yaw, int layer)
        {
            var t = Node(parent, "Cart", pos, yaw);
            Bx(t, "Bed", new Vector3(0, 0.95f, 0), new Vector3(1.5f, 0.5f, 2.8f), Plank, layer);
            Bx(t, "Shaft", new Vector3(0, 0.7f, 2.3f), new Vector3(0.1f, 0.1f, 2), Plank, -1, new Vector3(12, 0, 0));
            for (int s = -1; s <= 1; s += 2) Cyl(t, "Wheel", new Vector3(s * 0.9f, 0.6f, -0.3f), 0.6f, 0.1f, new Color(0.3f, 0.22f, 0.15f), new Vector3(0, 0, 90), 10);
        }

        public static void Crates(Transform parent, Vector3 pos, System.Random rng, int layer)
        {
            var t = Node(parent, "Supplies", pos, (float)rng.NextDouble() * 360);
            Bx(t, "CrateA", new Vector3(0, 0.45f, 0), new Vector3(1.1f, 0.9f, 0.8f), Plank, layer, default, true);
            Bx(t, "CrateB", new Vector3(0.1f, 1.2f, 0), new Vector3(0.9f, 0.6f, 0.7f), new Color(0.35f, 0.4f, 0.28f), layer, new Vector3(0, 15, 0), true);
            Cyl(t, "Barrel", new Vector3(1.1f, 0.55f, 0.3f), 0.38f, 1.1f, new Color(0.25f, 0.3f, 0.25f), default, 10).AddComponent<BoxCollider>().gameObject.layer = layer;
            Cyl(t, "Barrel2", new Vector3(1.2f, 0.4f, -0.7f), 0.38f, 1.1f, Rust, new Vector3(90, 30, 0), 10);
        }

        public static void Watchtower(Transform parent, Vector3 pos, float yaw, bool winter, int layer)
        {
            var t = Node(parent, "Watchtower", pos, yaw);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 * 2 - 1) * 1.5f, z = (i / 2 * 2 - 1) * 1.5f;
                Bx(t, "Leg", new Vector3(x * 1.1f, 3.5f, z * 1.1f), new Vector3(0.22f, 7.2f, 0.22f), Plank, -1, new Vector3(-z * 2.5f, 0, x * 2.5f));
            }
            Bx(t, "Brace1", new Vector3(0, 3, 1.6f), new Vector3(4.2f, 0.12f, 0.12f), Plank, -1, new Vector3(0, 0, 38));
            Bx(t, "Brace2", new Vector3(0, 3, -1.6f), new Vector3(4.2f, 0.12f, 0.12f), Plank, -1, new Vector3(0, 0, -38));
            Bx(t, "Floor", new Vector3(0, 7, 0), new Vector3(3.6f, 0.2f, 3.6f), Plank, layer);
            foreach (var p in new[] { new Vector3(0, 7.6f, 1.75f), new Vector3(0, 7.6f, -1.75f) }) Bx(t, "Rail", p, new Vector3(3.6f, 1, 0.12f), Plank, layer);
            foreach (var p in new[] { new Vector3(1.75f, 7.6f, 0), new Vector3(-1.75f, 7.6f, 0) }) Bx(t, "Rail", p, new Vector3(0.12f, 1, 3.6f), Plank, layer);
            for (int i = 0; i < 4; i++) Bx(t, "RoofPost", new Vector3((i % 2 * 2 - 1) * 1.7f, 8.6f, (i / 2 * 2 - 1) * 1.7f), new Vector3(0.12f, 2, 0.12f), Plank);
            ProceduralModels.Part(t, "Roof", MeshUtil.Cone(4), new Vector3(0, 9.6f, 0), new Vector3(5.6f, 1.6f, 5.6f), winter ? Snow : new Color(0.3f, 0.22f, 0.17f), new Vector3(0, 45, 0));
            Bx(t, "Ladder", new Vector3(0, 3.5f, -1.95f), new Vector3(0.6f, 7.2f, 0.08f), Plank, -1, new Vector3(6, 0, 0));
        }

        /// <summary>Руины замка на холме — дальний ориентир.</summary>
        public static void CastleRuin(Transform parent, Vector3 pos, float yaw, System.Random rng, int layer)
        {
            var t = Node(parent, "CastleRuin", pos, yaw);
            Color st = new(0.55f, 0.54f, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 * 2 - 1) * 16f, z = (i / 2 * 2 - 1) * 13f, h = 9 + (float)rng.NextDouble() * 9;
                Cyl(t, "Tower", new Vector3(x, h * 0.5f, z), 3.6f, h, st, default, 9).AddComponent<BoxCollider>().gameObject.layer = layer;
                for (int k = 0; k < 5; k++) { float a = k * Mathf.PI * 2 / 5; Bx(t, "Merlon", new Vector3(x + Mathf.Cos(a) * 3.1f, h + 0.5f, z + Mathf.Sin(a) * 3.1f), new Vector3(1.2f, 1, 1.2f), st); }
            }
            Bx(t, "WallN", new Vector3(0, 3.5f, 13), new Vector3(26, 7, 1.6f), st, layer);
            Bx(t, "WallS1", new Vector3(-9, 2.5f, -13), new Vector3(10, 5, 1.6f), st, layer);
            Bx(t, "WallS2", new Vector3(10, 1.5f, -13), new Vector3(7, 3, 1.6f), st, layer, new Vector3(0, 0, 4));
            Bx(t, "WallE", new Vector3(16, 3, 0), new Vector3(1.6f, 6, 20), st, layer);
            Bx(t, "Keep", new Vector3(-3, 7, 2), new Vector3(9, 14, 9), st * 0.95f, layer);
            Bx(t, "KeepBreak", new Vector3(-1, 14.5f, 3), new Vector3(5, 2.4f, 5), st * 0.9f, -1, new Vector3(8, 20, -6));
            for (int k = 0; k < 8; k++) Bx(t, "Rubble", new Vector3(-16 + (float)rng.NextDouble() * 6, 0.5f, -6 + (float)rng.NextDouble() * 14), Vector3.one * (0.8f + (float)rng.NextDouble() * 1.4f), st, -1, new Vector3(k * 37, k * 53, k * 19), true);
        }

        public static void Rock(Transform parent, Vector3 pos, float size, System.Random rng, int layer, bool winter)
        {
            var sc = new Vector3(size * (0.8f + (float)rng.NextDouble() * 0.7f), size * (0.5f + (float)rng.NextDouble() * 0.5f), size * (0.8f + (float)rng.NextDouble() * 0.7f));
            var go = ProceduralModels.Part(parent, "Rock", MeshUtil.Ico(), pos + Vector3.up * sc.y * 0.2f, sc, winter ? new Color(0.62f, 0.63f, 0.66f) : new Color(0.47f, 0.46f, 0.43f),
                new Vector3((float)rng.NextDouble() * 40, (float)rng.NextDouble() * 360, (float)rng.NextDouble() * 40));
            if (size > 1.2f) { go.AddComponent<BoxCollider>().size = Vector3.one * 0.7f; go.layer = layer; }
        }

        /// <summary>Облако из нескольких сплюснутых многогранников (без теней — дёшево).</summary>
        public static void Cloud(Transform parent, Vector3 pos, System.Random rng, Material mat)
        {
            var t = Node(parent, "Cloud", pos, (float)rng.NextDouble() * 360);
            int n = 3 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Puff");
                go.transform.SetParent(t, false);
                go.transform.localPosition = new Vector3((i - n * 0.5f) * 70 + (float)rng.NextDouble() * 30, (float)rng.NextDouble() * 12, (float)rng.NextDouble() * 50 - 25);
                go.transform.localScale = new Vector3(110 + (float)rng.NextDouble() * 90, 26 + (float)rng.NextDouble() * 18, 80 + (float)rng.NextDouble() * 60);
                go.AddComponent<MeshFilter>().sharedMesh = MeshUtil.Ico();
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }

        /// <summary>Столб чёрного дыма над подбитой техникой.</summary>
        public static void SmokeColumn(Transform parent, Vector3 pos)
        {
            var go = new GameObject("SmokeColumn");
            go.transform.SetParent(parent); go.transform.position = pos; go.transform.rotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.startLifetime = 9f; main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); main.maxParticles = 60;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.08f, 0.08f, 0.08f), new Color(0.22f, 0.21f, 0.2f));
            main.startRotation3D = true; main.startRotationX = new ParticleSystem.MinMaxCurve(0, 6.28f); main.startRotationY = new ParticleSystem.MinMaxCurve(0, 6.28f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 5;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6; sh.radius = 0.5f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.5f, 2.5f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);  // снос ветром
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 4f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = MeshUtil.Ico(); r.shadowCastingMode = ShadowCastingMode.Off;
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            var shd = urp ? Shader.Find("Universal Render Pipeline/Particles/Unlit") : Shader.Find("Particles/Standard Unlit");
            r.sharedMaterial = shd ? new Material(shd) { enableInstancing = true } : ProceduralModels.Mat(Color.gray);
            ps.Play();
        }
    }
}
