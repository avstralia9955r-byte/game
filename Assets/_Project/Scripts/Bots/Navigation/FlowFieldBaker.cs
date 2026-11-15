using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Frontline.Bots
{
    /// <summary>
    /// Строит навигационную сетку и flow fields при загрузке карты:
    /// 1) RaycastCommand-батч сверху вниз по каждой клетке → высота земли и проходимость по уклону;
    /// 2) Burst-BFS от каждой цели (5 точек захвата + 2 базы) → поле стоимостей.
    /// Один flow field обслуживает всех 128 ботов, идущих к этой цели, — O(1) на бота вместо поиска пути.
    /// Кирпичные стены помечают клетки непроходимыми; поля перестраиваются в фоне не чаще раза в секунду.
    /// </summary>
    public class FlowFieldBaker : MonoBehaviour
    {
        public Vector2 boundsMin = new(-200, -200), boundsMax = new(200, 200);
        public float cellSize = 1f;
        public float rayTop = 120f, maxSlopeCos = 0.7f, maxStep = 0.55f;
        public LayerMask groundMask;
        public Transform[] capturePoints = new Transform[5];
        public Transform baseGermany, baseUSSR;
        public float rebakeInterval = 1f;

        public static FlowFieldBaker I { get; private set; }

        NativeArray<int> _goalCells;
        JobHandle _rebake;
        bool _dirty, _rebaking;
        float _nextRebake;
        NativeArray<ushort> _scratch;

        void Awake() { I = this; Bake(); }

        void OnDestroy()
        {
            _rebake.Complete();
            if (_goalCells.IsCreated) _goalCells.Dispose();
            if (_scratch.IsCreated) _scratch.Dispose();
            WorldStores.Dispose();
        }

        public void Bake()
        {
            Physics.SyncTransforms();   // коллайдеры, созданные/сдвинутые в этом кадре, иначе не видны лучам
            _rebake.Complete();
            WorldStores.DisposeNav();
            if (_goalCells.IsCreated) _goalCells.Dispose();
            if (_scratch.IsCreated) _scratch.Dispose();
            WorldStores.CellSize = cellSize;
            WorldStores.GridOrigin = boundsMin;
            int2 size = (int2)math.ceil((float2)(boundsMax - boundsMin) / cellSize);
            WorldStores.GridSize = size;
            int cells = size.x * size.y;

            // --- 1. Высоты и проходимость ---
            var cmds = new NativeArray<RaycastCommand>(cells, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(cells, Allocator.TempJob);
            var qp = new QueryParameters(groundMask, false, QueryTriggerInteraction.Ignore, false);
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    var w = WorldStores.CellToWorld(new int2(x, y));
                    cmds[y * size.x + x] = new RaycastCommand(new Vector3(w.x, rayTop, w.z), Vector3.down, qp, rayTop + 50f);
                }
            RaycastCommand.ScheduleBatch(cmds, hits, 256, 1).Complete();

            WorldStores.Height = new NativeArray<float>(cells, Allocator.Persistent);
            WorldStores.StaticWalkable = new NativeArray<byte>(cells, Allocator.Persistent);
            WorldStores.Walkable = new NativeArray<byte>(cells, Allocator.Persistent);
            for (int i = 0; i < cells; i++)
            {
                var h = hits[i];
                bool ok = h.HasHit() && h.normal.y >= maxSlopeCos;
                WorldStores.Height[i] = h.HasHit() ? h.point.y : 0;
                WorldStores.StaticWalkable[i] = (byte)(ok ? 1 : 0);
                WorldStores.Walkable[i] = WorldStores.StaticWalkable[i];
            }
            cmds.Dispose(); hits.Dispose();

            // --- 2. Цели и точки режима ---
            int fields = 7;
            WorldStores.FieldCount = fields;
            _goalCells = new NativeArray<int>(fields, Allocator.Persistent);
            for (int i = 0; i < 5; i++)
                _goalCells[i] = capturePoints[i] ? WorldStores.CellIndex(WorldStores.WorldToCell(capturePoints[i].position)) : -1;
            _goalCells[WorldStores.BaseFieldGermany] = baseGermany ? WorldStores.CellIndex(WorldStores.WorldToCell(baseGermany.position)) : -1;
            _goalCells[WorldStores.BaseFieldUSSR] = baseUSSR ? WorldStores.CellIndex(WorldStores.WorldToCell(baseUSSR.position)) : -1;

            WorldStores.Integration = new NativeArray<ushort>(fields * cells, Allocator.Persistent);
            _scratch = new NativeArray<ushort>(fields * cells, Allocator.Persistent);
            Schedule(WorldStores.Integration).Complete();
            Debug.Log($"[FlowField] {size.x}×{size.y} клеток, {fields} полей, {fields * cells * 2 / 1024} КБ");
        }

        JobHandle Schedule(NativeArray<ushort> target, JobHandle dep = default) => new IntegrationJob
        {
            walkable = WorldStores.Walkable, height = WorldStores.Height, goals = _goalCells,
            gridSize = WorldStores.GridSize, maxStep = maxStep, output = target
        }.ScheduleParallel(WorldStores.FieldCount, 1, dep);

        /// <summary>Кирпичная стена заняла/освободила клетку.</summary>
        public void SetBlocked(Vector3 worldPos, bool blocked)
        {
            if (!WorldStores.Walkable.IsCreated) return;
            _rebake.Complete();
            int i = WorldStores.CellIndex(WorldStores.WorldToCell(worldPos));
            WorldStores.Walkable[i] = (byte)(blocked ? 0 : WorldStores.StaticWalkable[i]);
            _dirty = true;
        }

        void Update()
        {
            if (_rebaking && _rebake.IsCompleted)
            {
                _rebake.Complete();
                // Готовое поле копируем в основное (боты читают Integration в своих джобах в начале кадра).
                WorldStores.Integration.CopyFrom(_scratch);
                _rebaking = false;
            }
            if (_dirty && !_rebaking && Time.time >= _nextRebake)
            {
                _dirty = false; _rebaking = true; _nextRebake = Time.time + rebakeInterval;
                _rebake = Schedule(_scratch);
            }
        }

        [BurstCompile]
        struct IntegrationJob : IJobFor
        {
            [ReadOnly] public NativeArray<byte> walkable;
            [ReadOnly] public NativeArray<float> height;
            [ReadOnly] public NativeArray<int> goals;
            public int2 gridSize;
            public float maxStep;
            [NativeDisableParallelForRestriction] public NativeArray<ushort> output;

            public void Execute(int field)
            {
                int cells = gridSize.x * gridSize.y;
                int baseIdx = field * cells;
                for (int i = 0; i < cells; i++) output[baseIdx + i] = ushort.MaxValue;
                int goal = goals[field];
                if (goal < 0) return;

                var queue = new NativeQueue<int>(Allocator.Temp);
                output[baseIdx + goal] = 0;
                queue.Enqueue(goal);
                while (queue.TryDequeue(out int cur))
                {
                    ushort cost = output[baseIdx + cur];
                    int cx = cur % gridSize.x, cy = cur / gridSize.x;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0);
                        int ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= gridSize.x || ny >= gridSize.y) continue;
                        int ni = ny * gridSize.x + nx;
                        if (walkable[ni] == 0 || output[baseIdx + ni] != ushort.MaxValue) continue;
                        if (math.abs(height[ni] - height[cur]) > maxStep) continue; // стена/крыша/обрыв
                        output[baseIdx + ni] = (ushort)math.min(cost + 1, ushort.MaxValue - 1);
                        queue.Enqueue(ni);
                    }
                }
                queue.Dispose();
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            var c = new Vector3((boundsMin.x + boundsMax.x) * 0.5f, 0, (boundsMin.y + boundsMax.y) * 0.5f);
            Gizmos.DrawWireCube(c, new Vector3(boundsMax.x - boundsMin.x, 1, boundsMax.y - boundsMin.y));
        }
#endif
    }
}
