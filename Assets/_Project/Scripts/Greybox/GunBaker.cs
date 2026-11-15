using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>
    /// Запекает детальную модель оружия (та же, что от первого лица, без кистей) в один меш с палитровой текстурой:
    /// дерево, воронёная сталь, бакелит, латунь и ремни сохраняют свои цвета, но рисуются одним материалом —
    /// так боты (ECS, одна сетка на часть тела) и тело игрока держат в руках настоящий Kar98k, StG-44, ППШ и т.д.
    /// Мелкие детали (винтики, прорези мушки) отбрасываются ради треугольников.
    /// </summary>
    public static class GunBaker
    {
        static readonly Dictionary<string, (Mesh mesh, Material mat)> _cache = new();

        public static bool Enabled => !Application.isMobilePlatform || QualitySettings.GetQualityLevel() >= 2;

        public static bool TryGet(Faction f, SoldierClass cls, bool alt, out Mesh mesh, out Material mat)
        {
            mesh = null; mat = null;
            if (!Enabled) return false;
            var db = GameDatabase.Instance;
            var lo = db ? db.Arsenal(f)?.GetClass(cls) : null;
            var w = lo == null ? null : alt && lo.primaryAlt ? lo.primaryAlt : lo.primary;
            if (!w || !w.viewModelPrefab) return false;
            return TryBake(w.viewModelPrefab, out mesh, out mat);
        }

        public static bool TryBake(GameObject template, out Mesh mesh, out Material mat)
        {
            mesh = null; mat = null;
            if (!template) return false;
            if (_cache.TryGetValue(template.name, out var c) && c.mesh) { mesh = c.mesh; mat = c.mat; return true; }

            var colors = new List<Color>();
            var parts = new List<(Mesh m, Matrix4x4 tr, int col)>();
            var w2l = template.transform.worldToLocalMatrix;
            foreach (var mf in template.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || Under(mf.transform, template.transform, "HandR", "HandL")) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (!mr || !mr.sharedMaterial) continue;
                var size = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale);
                if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < 0.014f) continue;   // мелочь не видна на расстоянии
                Color col = mr.sharedMaterial.color;
                if (mr.sharedMaterial.mainTexture) col *= 0.88f;   // детальные материалы осветлены под текстуру
                int idx = colors.FindIndex(x => Mathf.Abs(x.r - col.r) + Mathf.Abs(x.g - col.g) + Mathf.Abs(x.b - col.b) < 0.02f);
                if (idx < 0) { idx = colors.Count; colors.Add(col); }
                parts.Add((mf.sharedMesh, w2l * mf.transform.localToWorldMatrix, idx));
            }
            if (parts.Count == 0 || colors.Count > 64) return false;

            // Палитра 8×8 клеток по 4 px
            const int cell = 4, cells = 8, N = cell * cells;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "GunPalette_" + template.name };
            var px = new Color[N * N];
            for (int i = 0; i < px.Length; i++) px[i] = Color.gray;
            for (int i = 0; i < colors.Count; i++)
            {
                int cx = i % cells, cy = i / cells;
                for (int y = 0; y < cell; y++) for (int x = 0; x < cell; x++) px[(cy * cell + y) * N + cx * cell + x] = colors[i];
            }
            tex.SetPixels(px); tex.Apply();

            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            foreach (var (m, tr, ci) in parts)
            {
                var v = m.vertices; var n = m.normals; var t = m.triangles;
                Vector2 uv = new(((ci % cells) * cell + cell * 0.5f) / N, ((ci / cells) * cell + cell * 0.5f) / N);
                int b = verts.Count;
                var nm = tr.inverse.transpose;
                for (int i = 0; i < v.Length; i++)
                {
                    verts.Add(tr.MultiplyPoint3x4(v[i]));
                    norms.Add(nm.MultiplyVector(i < n.Length ? n[i] : Vector3.up).normalized);
                    uvs.Add(uv);
                }
                bool flip = tr.determinant < 0;
                for (int i = 0; i < t.Length; i += 3)
                {
                    tris.Add(b + t[i]);
                    tris.Add(b + (flip ? t[i + 2] : t[i + 1]));
                    tris.Add(b + (flip ? t[i + 1] : t[i + 2]));
                }
            }
            mesh = new Mesh { name = "WorldGun_" + template.name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            mat = new Material(ProceduralModels.Lit) { enableInstancing = true, name = "WorldGun_" + template.name };
            mat.color = Color.white;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.38f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.35f);
            _cache[template.name] = (mesh, mat);
            return true;
        }

        static bool Under(Transform t, Transform root, string a, string b)
        {
            for (; t && t != root; t = t.parent) if (t.name == a || t.name == b) return true;
            return false;
        }
    }
}
