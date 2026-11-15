using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Frontline.Bots
{
    /// <summary>
    /// Автоматический поиск укрытий: из проходимых клеток бросаются короткие горизонтальные лучи на высоте
    /// пояса (0.9 м) и головы (1.6 м). Попадание на поясе = укрытие; нет попадания на уровне головы = низкое (сидя).
    /// Запускается после FlowFieldBaker. Ручные точки можно добавить объектами с тегом "CoverPoint".
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class CoverPointBaker : MonoBehaviour
    {
        public int cellStride = 2;
        public float probeLength = 0.9f;
        public LayerMask obstacleMask;

        void Start() => Bake();

        public void Bake()
        {
            if (!WorldStores.Walkable.IsCreated) { Debug.LogError("[Cover] Сначала FlowFieldBaker"); return; }
            Physics.SyncTransforms();
            var size = WorldStores.GridSize;
            var origins = new NativeList<float3>(Allocator.Temp);
            for (int y = 0; y < size.y; y += cellStride)
                for (int x = 0; x < size.x; x += cellStride)
                {
                    int i = WorldStores.CellIndex(new int2(x, y));
                    if (WorldStores.Walkable[i] == 0) continue;
                    var p = WorldStores.CellToWorld(new int2(x, y));
                    p.y = WorldStores.Height[i];
                    origins.Add(p);
                }

            const int dirs = 8;
            int n = origins.Length * dirs * 2;
            var cmds = new NativeArray<RaycastCommand>(n, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(n, Allocator.TempJob);
            var qp = new QueryParameters(obstacleMask, false, QueryTriggerInteraction.Ignore, false);
            for (int o = 0; o < origins.Length; o++)
                for (int d = 0; d < dirs; d++)
                {
                    float a = d * math.PI * 2f / dirs;
                    Vector3 dir = new(math.cos(a), 0, math.sin(a));
                    Vector3 p = origins[o];
                    cmds[(o * dirs + d) * 2] = new RaycastCommand(p + Vector3.up * 0.9f, dir, qp, probeLength);
                    cmds[(o * dirs + d) * 2 + 1] = new RaycastCommand(p + Vector3.up * 1.6f, dir, qp, probeLength);
                }
            RaycastCommand.ScheduleBatch(cmds, hits, 512, 1).Complete();

            var covers = new NativeList<CoverPoint>(Allocator.Temp);
            for (int o = 0; o < origins.Length; o++)
                for (int d = 0; d < dirs; d += 2) // 4 направления на точку достаточно
                {
                    var waist = hits[(o * dirs + d) * 2];
                    if (!waist.HasHit()) continue;
                    var head = hits[(o * dirs + d) * 2 + 1];
                    covers.Add(new CoverPoint
                    {
                        position = origins[o],
                        normal = math.normalizesafe(new float3(waist.normal.x, 0, waist.normal.z)),
                        crouchOnly = !head.HasHit()
                    });
                }

            try
            {
                foreach (var go in GameObject.FindGameObjectsWithTag("CoverPoint"))
                    covers.Add(new CoverPoint { position = go.transform.position, normal = -go.transform.forward, crouchOnly = false });
            }
            catch (UnityException) { /* тег не заведён в Tags & Layers — ручных укрытий нет */ }

            if (WorldStores.Covers.IsCreated) WorldStores.Covers.Dispose();
            WorldStores.Covers = new NativeArray<CoverPoint>(covers.AsArray(), Allocator.Persistent);
            Debug.Log($"[Cover] Найдено укрытий: {covers.Length}");
            cmds.Dispose(); hits.Dispose(); origins.Dispose(); covers.Dispose();
        }
    }
}
