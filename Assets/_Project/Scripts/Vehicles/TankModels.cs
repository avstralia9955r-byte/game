using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>Склейка дочерних мешей группы в один меш с подмешами по материалам (меньше draw calls, быстрая перекраска обломков).</summary>
    public static class MeshMerge
    {
        public static void Merge(Transform group, HashSet<Transform> groups)
        {
            var byMat = new Dictionary<Material, List<CombineInstance>>();
            var toStrip = new List<MeshFilter>();
            var w2l = group.worldToLocalMatrix;
            foreach (var mf in group.GetComponentsInChildren<MeshFilter>(true))
            {
                // Принадлежит ближайшей вышестоящей группе — не этой, если по пути есть другая группа
                var t = mf.transform; bool mine = true;
                while (t && t != group) { if (groups.Contains(t)) { mine = false; break; } t = t.parent; }
                if (!mine || mf.transform == group || !mf.sharedMesh) continue;
                var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mr.sharedMaterial) continue;
                if (!byMat.TryGetValue(mr.sharedMaterial, out var list)) byMat[mr.sharedMaterial] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = w2l * mf.transform.localToWorldMatrix });
                toStrip.Add(mf);
            }
            if (byMat.Count == 0) return;
            var mats = new List<Material>(); var parts = new List<CombineInstance>();
            int verts = 0;
            foreach (var kv in byMat)
            {
                var sub = new Mesh { indexFormat = IndexFormat.UInt32 };
                sub.CombineMeshes(kv.Value.ToArray(), true, true);
                verts += sub.vertexCount;
                mats.Add(kv.Key); parts.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
            }
            var mesh = new Mesh { name = group.name + "_merged", indexFormat = verts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.CombineMeshes(parts.ToArray(), false, false);
            mesh.RecalculateBounds();
            foreach (var p in parts) Object.Destroy(p.mesh);
            var gmf = group.gameObject.GetComponent<MeshFilter>();
            if (!gmf) gmf = group.gameObject.AddComponent<MeshFilter>();
            gmf.sharedMesh = mesh;
            var gmr = group.gameObject.GetComponent<MeshRenderer>();
            if (!gmr) gmr = group.gameObject.AddComponent<MeshRenderer>();
            gmr.sharedMaterials = mats.ToArray();
            foreach (var mf in toStrip)
            {
                var go = mf.gameObject;
                var r = mf.GetComponent<MeshRenderer>();
                if (r) Object.DestroyImmediate(r);
                Object.DestroyImmediate(mf);
                if (go.transform.childCount == 0 && !groups.Contains(go.transform)) Object.Destroy(go);
            }
        }
    }

    /// <summary>
    /// Процедурные модели управляемых танков: Pz.Kpfw. IV Ausf. H (Германия) и Т-34-85 (СССР).
    /// Иерархия: Tank → Hull → Turret → Gun → Muzzle. Опорные катки, ленивец, ведущее колесо, поддерживающие ролики,
    /// траки с грунтозацепами, экраны «шюрцен», ЗИП, выхлопные трубы, курсовой и спаренный пулемёты, командирская башенка,
    /// антенна, опознавательные знаки (крест / звезда), зимой — побелка.
    /// </summary>
    public static class TankModels
    {
        public class Rig
        {
            public GameObject root;
            public Transform hull, turret, gun, muzzle, coaxMuzzle, seat, exhaust;
            public Vector3 hullSize; public float hullCenterY;
            public Vector3 turretSize; public float turretY;
        }

        static Transform _p;
        static readonly HashSet<Transform> _groups = new();

        static GameObject B(string n, Vector3 pos, Vector3 size, Color c, Vector3 euler = default, float bevel = 0.06f, float smooth = 0.25f, float metal = 0.35f)
            => ProceduralModels.Part(_p, n, MeshUtil.Bevel(bevel), pos, size, c, euler, false, smooth, metal);
        static GameObject C(string n, Vector3 pos, float r, float len, Color c, Vector3 euler, int seg = 12, float smooth = 0.3f, float metal = 0.4f)
            => ProceduralModels.Part(_p, n, MeshUtil.Cylinder(seg), pos, new Vector3(r * 2, len, r * 2), c, euler, false, smooth, metal);
        static GameObject F(string n, Vector3 pos, float r, float len, float top, Color c, Vector3 euler, int seg = 12)
            => ProceduralModels.Part(_p, n, MeshUtil.Frustum(seg, top), pos, new Vector3(r * 2, len, r * 2), c, euler, false, 0.3f, 0.4f);
        static Transform G(string n, Vector3 pos)
        {
            var t = new GameObject(n).transform; t.SetParent(_p, false); t.localPosition = pos;
            _groups.Add(t); return t;
        }

        static Mesh _star;
        static Mesh Star()
        {
            if (_star) return _star;
            var v = new List<Vector3> { Vector3.zero }; var tr = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f, r = (i & 1) == 0 ? 0.5f : 0.2f;
                v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
            }
            for (int i = 0; i < 10; i++) { tr.Add(0); tr.Add(1 + (i + 1) % 10); tr.Add(1 + i); }
            _star = new Mesh { name = "Star" }; _star.SetVertices(v); _star.SetTriangles(tr, 0); _star.RecalculateNormals();
            return _star;
        }

        static void Cross(Vector3 pos, float yaw, float s)
        {
            var g = new GameObject("Balkenkreuz").transform; g.SetParent(_p, false); g.localPosition = pos; g.localEulerAngles = new Vector3(0, yaw, 0);
            var prev = _p; _p = g;
            ProceduralModels.Box(_p, "W1", Vector3.zero, new Vector3(0.58f * s, 0.2f * s, 0.01f), Color.white);
            ProceduralModels.Box(_p, "W2", Vector3.zero, new Vector3(0.2f * s, 0.58f * s, 0.01f), Color.white);
            ProceduralModels.Box(_p, "K1", new Vector3(0, 0, 0.006f), new Vector3(0.5f * s, 0.1f * s, 0.01f), new Color(0.06f, 0.06f, 0.06f));
            ProceduralModels.Box(_p, "K2", new Vector3(0, 0, 0.006f), new Vector3(0.1f * s, 0.5f * s, 0.01f), new Color(0.06f, 0.06f, 0.06f));
            _p = prev;
        }

        static void RedStar(Vector3 pos, float yaw, float s)
        {
            var go = ProceduralModels.Part(_p, "Star", Star(), pos, Vector3.one * s, new Color(0.72f, 0.08f, 0.06f), new Vector3(0, yaw, 0));
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var back = ProceduralModels.Part(_p, "StarB", Star(), pos, Vector3.one * s, new Color(0.72f, 0.08f, 0.06f), new Vector3(0, yaw + 180, 0));
            back.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        public static Rig Build(Faction f, MapTheme theme, Transform parent)
        {
            bool de = f == Faction.Germany, winter = theme == MapTheme.Winter;
            _groups.Clear();
            Color paint = de ? new Color(0.62f, 0.55f, 0.38f) : new Color(0.27f, 0.33f, 0.2f);     // Dunkelgelb / 4БО
            if (winter) paint = new Color(0.82f, 0.83f, 0.82f);
            Color paint2 = de && !winter ? new Color(0.3f, 0.36f, 0.24f) : paint * 0.9f;           // пятна камуфляжа
            Color dark = Color.Lerp(paint, new Color(0.12f, 0.12f, 0.11f), 0.55f);
            Color track = new(0.18f, 0.17f, 0.16f), steel = new(0.3f, 0.3f, 0.31f), rubber = new(0.1f, 0.1f, 0.1f), wood = ProceduralModels.Wood;

            var rig = new Rig();
            var root = new GameObject(de ? "Tank_PzIV" : "Tank_T34-85");
            root.transform.SetParent(parent, false);
            rig.root = root; _p = root.transform;
            var hull = G("Hull", Vector3.zero); rig.hull = hull; _p = hull;

            float halfW = de ? 1.15f : 1.2f, len = de ? 5.9f : 6.1f, trackX = de ? 1.2f : 1.27f;
            // ---- Ходовая ----
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * trackX;
                B("TrackTop", new Vector3(x, 0.98f, -0.05f), new Vector3(0.5f, 0.06f, len - 0.9f), track, default, 0.02f, 0.1f, 0.6f);
                B("TrackBot", new Vector3(x, 0.05f, 0f), new Vector3(0.5f, 0.07f, len - 1.4f), track, default, 0.02f, 0.1f, 0.6f);
                B("TrackFront", new Vector3(x, 0.55f, len * 0.5f - 0.38f), new Vector3(0.5f, 0.07f, 1.1f), track, new Vector3(-52, 0, 0), 0.02f, 0.1f, 0.6f);
                B("TrackRear", new Vector3(x, 0.55f, -len * 0.5f + 0.42f), new Vector3(0.5f, 0.07f, 1.05f), track, new Vector3(50, 0, 0), 0.02f, 0.1f, 0.6f);
                var mb = new MeshBuilder();
                for (float z = -len * 0.5f + 0.9f; z < len * 0.5f - 0.7f; z += 0.17f)
                {
                    mb.Box(new Vector3(x, 1.02f, z), new Vector3(0.52f, 0.04f, 0.06f));
                    mb.Box(new Vector3(x, 0.01f, z), new Vector3(0.52f, 0.04f, 0.06f));
                }
                var gr = mb.Build(_p, "Grousers", track * 0.8f, 0.1f, 0.6f);
                if (gr) gr.transform.localPosition = Vector3.zero;

                float wx = s * (trackX + 0.02f);
                if (de)
                {
                    for (int i = 0; i < 8; i++)       // 8 сдвоенных малых катков
                    {
                        float z = -2.05f + i * 0.56f;
                        C("Wheel", new Vector3(wx, 0.36f, z), 0.3f, 0.36f, rubber, new Vector3(0, 0, 90), 12);
                        C("Hub", new Vector3(wx + s * 0.19f, 0.36f, z), 0.12f, 0.04f, steel, new Vector3(0, 0, 90), 8);
                    }
                    for (int i = 0; i < 4; i++) C("Return", new Vector3(wx, 0.86f, -1.6f + i * 1.05f), 0.11f, 0.3f, rubber, new Vector3(0, 0, 90), 8);
                }
                else
                {
                    for (int i = 0; i < 5; i++)       // 5 больших катков «Кристи»
                    {
                        float z = -2.25f + i * 1.12f;
                        C("Wheel", new Vector3(wx, 0.43f, z), 0.41f, 0.38f, steel * 0.9f, new Vector3(0, 0, 90), 14);
                        C("Tyre", new Vector3(wx, 0.43f, z), 0.415f, 0.12f, rubber, new Vector3(0, 0, 90), 14);
                        C("Hub", new Vector3(wx + s * 0.2f, 0.43f, z), 0.14f, 0.04f, steel, new Vector3(0, 0, 90), 8);
                    }
                }
                C("Sprocket", new Vector3(wx, de ? 0.62f : 0.55f, de ? len * 0.5f - 0.45f : -len * 0.5f + 0.48f), 0.34f, 0.3f, steel, new Vector3(0, 0, 90), 10);
                C("Idler", new Vector3(wx, 0.58f, de ? -len * 0.5f + 0.45f : len * 0.5f - 0.5f), 0.3f, 0.3f, steel, new Vector3(0, 0, 90), 10);
                // Надгусеничные полки
                B("Fender", new Vector3(s * (trackX + 0.05f), 1.12f, 0), new Vector3(0.62f, 0.04f, len - 0.3f), paint, default, 0.02f);
                if (de)
                {
                    for (int i = 0; i < 5; i++)       // экраны Schürzen, местами отсутствуют
                    {
                        if ((i == 3 && s > 0) || (i == 1 && s < 0)) continue;
                        B("Schurze", new Vector3(s * 1.62f, 0.86f, -2.1f + i * 1.05f), new Vector3(0.025f, 0.95f, 1.0f), (i & 1) == 0 ? paint : paint2, new Vector3(0, 0, s * -3f), 0.01f);
                    }
                    B("SchurzeRail", new Vector3(s * 1.6f, 1.35f, 0), new Vector3(0.05f, 0.05f, 5.2f), dark, default, 0.01f);
                }
            }

            // ---- Корпус ----
            if (de)
            {
                B("LowerHull", new Vector3(0, 0.78f, 0), new Vector3(halfW * 2 - 0.5f, 0.75f, len - 0.3f), paint);
                B("Superstructure", new Vector3(0, 1.45f, -0.25f), new Vector3(halfW * 2 + 0.6f, 0.6f, 4.2f), paint);
                B("FrontPlate", new Vector3(0, 1.45f, 1.92f), new Vector3(halfW * 2 + 0.2f, 0.62f, 0.12f), paint, new Vector3(-10, 0, 0));
                B("Glacis", new Vector3(0, 1.06f, 2.42f), new Vector3(halfW * 2 - 0.5f, 0.12f, 0.8f), paint, new Vector3(18, 0, 0));
                B("LowerNose", new Vector3(0, 0.68f, 2.84f), new Vector3(halfW * 2 - 0.5f, 0.62f, 0.12f), paint, new Vector3(20, 0, 0));
                B("DriverVisor", new Vector3(-0.55f, 1.55f, 2.0f), new Vector3(0.36f, 0.12f, 0.12f), dark);
                B("BallMount", new Vector3(0.55f, 1.5f, 2.0f), new Vector3(0.3f, 0.3f, 0.14f), paint);
                C("HullMG", new Vector3(0.55f, 1.5f, 2.3f), 0.022f, 0.45f, steel, new Vector3(90, 0, 0), 6);
                B("EngineDeck", new Vector3(0, 1.66f, -2.35f), new Vector3(halfW * 2 + 0.2f, 0.18f, 1.25f), paint);
                for (int i = 0; i < 4; i++) B("Grille", new Vector3(-0.75f + i * 0.5f, 1.77f, -2.3f), new Vector3(0.35f, 0.03f, 0.9f), dark, default, 0.01f);
                B("RearPlate", new Vector3(0, 1.0f, -len * 0.5f + 0.12f), new Vector3(halfW * 2 - 0.4f, 1.1f, 0.12f), paint);
                C("Muffler", new Vector3(0, 0.95f, -len * 0.5f - 0.05f), 0.16f, 1.1f, dark * 0.7f, new Vector3(0, 0, 90), 10);
                rig.exhaust = new GameObject("Exhaust").transform; rig.exhaust.SetParent(hull, false); rig.exhaust.localPosition = new Vector3(0.5f, 1.05f, -len * 0.5f - 0.1f);
                C("Jack", new Vector3(-1.0f, 1.25f, -1.5f), 0.1f, 0.6f, dark, new Vector3(90, 0, 0), 8);
                for (int i = 0; i < 6; i++) B("SpareTrack", new Vector3(-0.6f + i * 0.24f, 1.25f, 2.5f), new Vector3(0.2f, 0.06f, 0.45f), track, new Vector3(-70, 0, 0), 0.02f);
                B("Shovel", new Vector3(1.28f, 1.17f, 0.5f), new Vector3(0.04f, 0.03f, 1.1f), wood, default, 0.01f);
                C("Headlight", new Vector3(-0.85f, 1.82f, 2.0f), 0.1f, 0.14f, dark, new Vector3(90, 0, 0), 8);
                Cross(new Vector3(1.66f, 1.25f, -1.1f), 90, 1.3f);
                Cross(new Vector3(-1.66f, 1.25f, -1.1f), -90, 1.3f);
                Cross(new Vector3(0.6f, 1.0f, -len * 0.5f + 0.06f), 180, 1f);
            }
            else
            {
                B("LowerHull", new Vector3(0, 0.8f, -0.1f), new Vector3(halfW * 2 - 0.4f, 0.8f, len - 1.2f), paint);
                B("SideUpperL", new Vector3(-halfW - 0.05f, 1.4f, -0.2f), new Vector3(0.45f, 0.55f, len - 1.5f), paint, new Vector3(0, 0, -40));
                B("SideUpperR", new Vector3(halfW + 0.05f, 1.4f, -0.2f), new Vector3(0.45f, 0.55f, len - 1.5f), paint, new Vector3(0, 0, 40));
                B("Deck", new Vector3(0, 1.62f, -0.45f), new Vector3(halfW * 2 + 0.15f, 0.12f, len - 2.1f), paint);
                B("Glacis", new Vector3(0, 1.17f, 2.43f), new Vector3(halfW * 2 + 0.15f, 0.12f, 1.75f), paint, new Vector3(30, 0, 0));   // 60° от вертикали
                B("LowerNose", new Vector3(0, 0.5f, 3.0f), new Vector3(halfW * 2 - 0.4f, 0.12f, 0.7f), paint, new Vector3(-55, 0, 0));
                B("DriverHatch", new Vector3(-0.45f, 1.48f, 2.0f), new Vector3(0.55f, 0.08f, 0.5f), paint * 0.95f, new Vector3(-30, 0, 0));
                B("BallMount", new Vector3(0.55f, 1.3f, 2.4f), new Vector3(0.3f, 0.26f, 0.2f), paint, new Vector3(-30, 0, 0));
                C("HullMG", new Vector3(0.55f, 1.32f, 2.6f), 0.022f, 0.45f, steel, new Vector3(90, 0, 0), 6);
                B("EngineDeck", new Vector3(0, 1.66f, -2.1f), new Vector3(1.5f, 0.1f, 1.5f), dark * 1.2f);
                B("RearPlate", new Vector3(0, 1.2f, -2.7f), new Vector3(halfW * 2 + 0.1f, 0.12f, 1.1f), paint, new Vector3(-48, 0, 0));
                for (int s = -1; s <= 1; s += 2)
                {
                    C("ExhaustPipe", new Vector3(s * 0.5f, 1.05f, -len * 0.5f + 0.1f), 0.08f, 0.25f, dark * 0.6f, new Vector3(-40, 0, 0), 8);
                    C("FuelTank", new Vector3(s * (halfW + 0.15f), 1.42f, -1.9f), 0.2f, 0.8f, paint * 0.95f, new Vector3(90, 0, 0), 10);
                }
                rig.exhaust = new GameObject("Exhaust").transform; rig.exhaust.SetParent(hull, false); rig.exhaust.localPosition = new Vector3(0.5f, 1.1f, -len * 0.5f);
                B("Tow1", new Vector3(-0.8f, 0.95f, 3.05f), new Vector3(0.12f, 0.2f, 0.1f), dark);
                B("Tow2", new Vector3(0.8f, 0.95f, 3.05f), new Vector3(0.12f, 0.2f, 0.1f), dark);
                C("Log", new Vector3(0, 1.0f, -len * 0.5f - 0.1f), 0.12f, 2.3f, wood, new Vector3(0, 0, 90), 8);
                C("Headlight", new Vector3(-0.8f, 1.55f, 2.6f), 0.09f, 0.14f, dark, new Vector3(60, 0, 0), 8);
                for (int i = 0; i < 4; i++) B("SpareTrack", new Vector3(-0.6f + i * 0.38f, 1.42f, 2.75f), new Vector3(0.3f, 0.05f, 0.5f), track, new Vector3(-60, 0, 0), 0.02f);
            }

            // ---- Башня ----
            float ringY = de ? 1.75f : 1.68f;
            var turret = G("Turret", new Vector3(0, ringY, de ? 0.15f : 0.55f)); rig.turret = turret; _p = turret;
            rig.turretY = ringY;
            if (de)
            {
                B("TurretSides", new Vector3(0, 0.36f, -0.1f), new Vector3(1.9f, 0.72f, 2.0f), paint, new Vector3(0, 0, 0));
                B("TurretFront", new Vector3(0, 0.36f, 0.92f), new Vector3(1.55f, 0.72f, 0.2f), paint, new Vector3(-10, 0, 0));
                B("TurretRoof", new Vector3(0, 0.75f, -0.1f), new Vector3(1.7f, 0.06f, 1.9f), paint);
                B("Mantlet", new Vector3(0, 0.33f, 1.1f), new Vector3(0.65f, 0.48f, 0.3f), paint);
                // Командирская башенка
                C("Cupola", new Vector3(0, 0.88f, -0.75f), 0.36f, 0.26f, paint, Vector3.zero, 14);
                C("CupolaHatch", new Vector3(0, 1.03f, -0.75f), 0.33f, 0.05f, paint * 0.95f, Vector3.zero, 14);
                for (int i = 0; i < 6; i++) { float a = i * 60 * Mathf.Deg2Rad; B("Vision", new Vector3(Mathf.Sin(a) * 0.35f, 0.88f, -0.75f + Mathf.Cos(a) * 0.35f), new Vector3(0.1f, 0.06f, 0.04f), dark, new Vector3(0, i * 60, 0), 0.01f); }
                B("SideHatchL", new Vector3(-0.96f, 0.36f, -0.05f), new Vector3(0.04f, 0.5f, 0.6f), paint * 0.95f);
                B("SideHatchR", new Vector3(0.96f, 0.36f, -0.05f), new Vector3(0.04f, 0.5f, 0.6f), paint * 0.95f);
                // Башенные экраны
                B("TurretSkirtL", new Vector3(-1.22f, 0.3f, -0.25f), new Vector3(0.03f, 0.75f, 1.7f), paint2);
                B("TurretSkirtR", new Vector3(1.22f, 0.3f, -0.25f), new Vector3(0.03f, 0.75f, 1.7f), paint2);
                B("TurretSkirtB", new Vector3(0, 0.3f, -1.12f), new Vector3(2.45f, 0.75f, 0.03f), paint2);
                B("Bustle", new Vector3(0, 0.45f, -1.25f), new Vector3(1.0f, 0.4f, 0.3f), paint);
                Cross(new Vector3(1.24f, 0.4f, -0.3f), 90, 0.8f);
                Cross(new Vector3(-1.24f, 0.4f, -0.3f), -90, 0.8f);
                C("Antenna", new Vector3(-0.85f, 1.6f, -1.6f), 0.008f, 2.0f, steel, Vector3.zero, 4);
                rig.turretSize = new Vector3(2.3f, 0.95f, 2.6f);
            }
            else
            {
                F("TurretBody", new Vector3(0, 0.38f, -0.1f), 1.25f, 0.76f, 0.78f, paint, Vector3.zero, 12);
                B("TurretCheeks", new Vector3(0, 0.36f, 0.35f), new Vector3(1.95f, 0.66f, 1.3f), paint, default, 0.25f);
                B("TurretRear", new Vector3(0, 0.42f, -0.95f), new Vector3(1.75f, 0.6f, 0.7f), paint, default, 0.2f);
                B("Mantlet", new Vector3(0, 0.36f, 1.12f), new Vector3(0.8f, 0.5f, 0.42f), paint, default, 0.15f);
                C("Cupola", new Vector3(0.38f, 0.85f, -0.45f), 0.32f, 0.22f, paint, Vector3.zero, 14);
                C("CupolaHatch", new Vector3(0.38f, 0.98f, -0.45f), 0.29f, 0.05f, paint * 0.95f, Vector3.zero, 14);
                B("LoaderHatch", new Vector3(-0.4f, 0.77f, -0.4f), new Vector3(0.5f, 0.05f, 0.5f), paint * 0.95f);
                B("Periscope", new Vector3(-0.2f, 0.82f, 0.25f), new Vector3(0.16f, 0.12f, 0.1f), dark);
                C("Vent", new Vector3(0, 0.78f, -0.85f), 0.16f, 0.1f, dark, Vector3.zero, 8);
                for (int i = 0; i < 3; i++) B("Handrail", new Vector3(-0.9f, 0.5f, -0.6f + i * 0.4f), new Vector3(0.04f, 0.04f, 0.34f), steel, default, 0.01f);
                RedStar(new Vector3(1.0f, 0.42f, -0.2f), 90, 0.55f);
                RedStar(new Vector3(-1.0f, 0.42f, -0.2f), -90, 0.55f);
                C("Antenna", new Vector3(0.75f, 1.5f, -1.1f), 0.008f, 1.8f, steel, Vector3.zero, 4);
                rig.turretSize = new Vector3(2.1f, 0.95f, 2.7f);
            }

            // ---- Пушка ----
            var gun = G("Gun", new Vector3(0, de ? 0.33f : 0.36f, de ? 1.15f : 1.2f)); rig.gun = gun; _p = gun;
            float barrel = de ? 3.25f : 3.9f;
            C("BarrelBase", new Vector3(0, 0, 0.35f), de ? 0.1f : 0.12f, 0.7f, paint, new Vector3(90, 0, 0), 12);
            F("Barrel", new Vector3(0, 0, 0.7f + barrel * 0.5f), de ? 0.075f : 0.085f, barrel, 0.85f, paint, new Vector3(90, 0, 0), 12);
            if (de)
            {
                C("BrakeA", new Vector3(0, 0, 0.75f + barrel), 0.11f, 0.16f, dark, new Vector3(90, 0, 0), 10);
                B("BrakeBaffle", new Vector3(0, 0, 0.88f + barrel), new Vector3(0.36f, 0.16f, 0.1f), dark);
                C("BrakeB", new Vector3(0, 0, 1.0f + barrel), 0.11f, 0.14f, dark, new Vector3(90, 0, 0), 10);
            }
            else C("MuzzleRing", new Vector3(0, 0, 0.72f + barrel), 0.1f, 0.1f, dark, new Vector3(90, 0, 0), 10);
            rig.muzzle = new GameObject("Muzzle").transform; rig.muzzle.SetParent(gun, false); rig.muzzle.localPosition = new Vector3(0, 0, (de ? 1.12f : 0.8f) + barrel);
            C("CoaxMG", new Vector3(de ? -0.28f : 0.3f, 0.02f, 0.28f), 0.022f, 0.4f, steel, new Vector3(90, 0, 0), 6);
            rig.coaxMuzzle = new GameObject("CoaxMuzzle").transform; rig.coaxMuzzle.SetParent(gun, false); rig.coaxMuzzle.localPosition = new Vector3(de ? -0.28f : 0.3f, 0.02f, 0.5f);

            // Сиденье (точка актора-водителя внутри корпуса) и габариты корпуса для коллайдеров
            rig.seat = new GameObject("Seat").transform; rig.seat.SetParent(hull, false); rig.seat.localPosition = new Vector3(0, 0.75f, 0.3f);
            rig.hullSize = new Vector3(de ? 3.25f : 3.0f, 1.45f, len); rig.hullCenterY = 0.95f;

            // Склейка по группам
            foreach (var g in new[] { gun, turret, hull }) MeshMerge.Merge(g, _groups);
            _p = null;
            return rig;
        }
    }
}
