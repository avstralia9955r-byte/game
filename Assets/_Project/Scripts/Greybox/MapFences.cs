using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Деревянные заборы вдоль сельских дорог у деревень (жерди или штакетник), с калитками и проломами.
    /// Каждый пролёт — отдельный объект с одним мешем и коллайдером (ломается целиком на доски, см. Fence).
    /// Не участвуют в статическом батчинге: пролёты исчезают во время боя.
    /// </summary>
    public static partial class MapGenerator
    {
        const float FenceSpan = 2.6f;

        static void BuildFences(Transform parent, Terrain terrain, int layer, bool winter, List<Vector2> pts,
                                List<(Vector2 p, float yaw, Vector3 size, int kind)> houses)
        {
            var root = new GameObject("Fences").transform;
            root.SetParent(parent);
            Color rail = winter ? new Color(0.36f, 0.3f, 0.25f) : new Color(0.45f, 0.36f, 0.25f);
            Color picketCol = winter ? new Color(0.7f, 0.68f, 0.64f) : new Color(0.62f, 0.58f, 0.5f);
            float Y(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0, p.y));
            int made = 0;

            bool Free(Vector2 p)
            {
                if (Blocked(p, 1.5f) || Mathf.Abs(p.y - RiverZ(p.x)) < 16f || RoadDist(p) < 3.2f) return false;
                foreach (var h in houses)
                    if (Vector2.Distance(h.p, p) < Mathf.Max(h.size.x, h.size.z) * 0.6f + 2f) return false;
                return true;
            }

            foreach (var seg in _roads)
            {
                Vector2 mid = (seg.a + seg.b) * 0.5f;
                float near = MinDist(mid, pts);
                if (near > 170f || near < 20f) continue;
                Vector2 dir = seg.b - seg.a; float len = dir.magnitude; if (len < 4f) continue;
                dir /= len;
                Vector2 perp = new(-dir.y, dir.x);
                for (int side = -1; side <= 1; side += 2)
                {
                    bool picket = Hash((int)mid.x, (int)mid.y, side + 7) < 0.4f;
                    int spans = Mathf.FloorToInt(len / FenceSpan);
                    int gapAt = _rng.Next(Mathf.Max(1, spans));
                    for (int i = 0; i < spans; i++)
                    {
                        if (i == gapAt || i == gapAt + 1) continue;                 // калитка
                        if (Hash((int)(mid.x + i), (int)mid.y, side) < 0.18f) continue;   // проломы
                        Vector2 a = seg.a + dir * (i * FenceSpan) + perp * (side * 5.6f);
                        Vector2 b = a + dir * FenceSpan;
                        Vector2 c = (a + b) * 0.5f;
                        if (MinDist(c, pts) < 22f || !Free(c) || !Free(a) || !Free(b)) continue;
                        float yc = Y(c), ya = Y(a) - yc, yb = Y(b) - yc;
                        if (Mathf.Abs(ya - yb) > 1.2f) continue;
                        Span(root, new Vector3(c.x, yc, c.y), Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, ya, yb, picket ? picketCol : rail, picket, layer);
                        made++;
                    }
                }
            }
            Debug.Log($"[Frontline] Заборов: {made} пролётов");
        }

        static void Span(Transform parent, Vector3 center, float yaw, float ya, float yb, Color color, bool picket, int layer)
        {
            float half = FenceSpan * 0.5f;
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0, ya + 0.45f, -half), new Vector3(0.12f, 1.2f, 0.12f));
            mb.Box(new Vector3(0, yb + 0.45f, half), new Vector3(0.12f, 1.2f, 0.12f));
            foreach (float h in new[] { 0.35f, 0.8f })
                mb.Beam(new Vector3(0.05f, ya + h, -half), new Vector3(0.05f, yb + h, half), 0.05f, 0.1f);
            if (picket)
                for (int i = 0; i < 9; i++)
                {
                    float z = -half + 0.15f + i * (FenceSpan - 0.3f) / 8f;
                    float y = Mathf.Lerp(ya, yb, (z + half) / FenceSpan);
                    mb.Box(new Vector3(0.09f, y + 0.48f, z), new Vector3(0.025f, 0.95f, 0.1f));
                }
            var go = mb.Build(parent, "Fence", color, 0.1f, 0f);
            if (!go) return;
            go.transform.SetPositionAndRotation(center, Quaternion.Euler(0, yaw, 0));
            go.layer = layer;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0.04f, (ya + yb) * 0.5f + 0.5f, 0);
            col.size = new Vector3(0.22f, 1.05f + Mathf.Abs(ya - yb), FenceSpan);
            var f = go.AddComponent<Fence>();
            f.length = FenceSpan; f.yA = ya; f.yB = yb; f.color = color; f.picket = picket;
        }
    }
}
