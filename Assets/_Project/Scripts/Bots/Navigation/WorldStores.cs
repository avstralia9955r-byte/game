using Unity.Collections;
using Unity.Mathematics;

namespace Frontline.Bots
{
    public struct CapturePointData
    {
        public float3 position;
        public float radius;
        public float progress;      // -1 (полностью Германия) .. +1 (полностью СССР)
        public byte owner;          // 0 Германия, 1 СССР, 255 нейтральна
        public byte contested;
    }

    public struct CoverPoint
    {
        public float3 position;
        public float3 normal;       // направление «от стены» — бот прячется, когда угроза с противоположной стороны
        public bool crouchOnly;     // низкое укрытие (изгородь, мешки, кирпичная стена 3 ряда)
    }

    /// <summary>
    /// Статические хранилища мира для Burst-джобов ботов. Заполняются при загрузке карты (FlowFieldBaker,
    /// CoverPointBaker, режим игры) и живут до выхода из матча.
    /// </summary>
    public static class WorldStores
    {
        // --- Сетка навигации ---
        public static float CellSize = 1f;
        public static float2 GridOrigin;
        public static int2 GridSize;
        public static NativeArray<byte> Walkable;          // 1 = проходимо (с учётом кирпичных стен)
        public static NativeArray<byte> StaticWalkable;    // 1 = проходимо по статической геометрии
        public static NativeArray<float> Height;           // высота земли в клетке (снаппинг ботов без лучей)
        public static NativeArray<ushort> Integration;     // fieldCount × cells, стоимость до цели (ushort.MaxValue = недостижимо)
        public static int FieldCount;                      // 5 точек + 2 базы

        // --- Режим ---
        public static NativeArray<CapturePointData> CapturePoints;
        public static NativeArray<float3> SpawnsGermany, SpawnsUSSR;
        public static NativeArray<CoverPoint> Covers;

        public static bool Ready => Integration.IsCreated && CapturePoints.IsCreated;

        /// <summary>Джобы не принимают несозданные контейнеры — пустой массив вместо default.</summary>
        public static void EnsureOptional()
        {
            if (!Covers.IsCreated) Covers = new NativeArray<CoverPoint>(0, Allocator.Persistent);
        }

        public const int BaseFieldGermany = 5, BaseFieldUSSR = 6;

        public static int CellIndex(int2 c) => c.y * GridSize.x + c.x;

        public static int2 WorldToCell(float3 p) =>
            math.clamp((int2)math.floor((p.xz - GridOrigin) / CellSize), 0, GridSize - 1);

        public static float3 CellToWorld(int2 c) =>
            new(GridOrigin.x + (c.x + 0.5f) * CellSize, 0, GridOrigin.y + (c.y + 0.5f) * CellSize);

        public static void Dispose()
        {
            DisposeNav();
            if (CapturePoints.IsCreated) CapturePoints.Dispose();
            if (SpawnsGermany.IsCreated) SpawnsGermany.Dispose();
            if (SpawnsUSSR.IsCreated) SpawnsUSSR.Dispose();
            if (Covers.IsCreated) Covers.Dispose();
        }

        public static void DisposeNav()
        {
            if (Walkable.IsCreated) Walkable.Dispose();
            if (StaticWalkable.IsCreated) StaticWalkable.Dispose();
            if (Height.IsCreated) Height.Dispose();
            if (Integration.IsCreated) Integration.Dispose();
        }
    }

    /// <summary>Чистые функции выборки flow field — используются внутри Burst-джобов.</summary>
    public static class FlowField
    {
        /// <summary>Направление движения к цели: к соседу с минимальной стоимостью (градиентный спуск).</summary>
        public static float2 Sample(in NativeArray<ushort> integration, in NativeArray<byte> walkable, int field,
                                    int2 gridSize, float2 origin, float cell, float3 pos)
        {
            int cells = gridSize.x * gridSize.y;
            int2 c = math.clamp((int2)math.floor((pos.xz - origin) / cell), 0, gridSize - 1);
            int baseIdx = field * cells;
            ushort best = integration[baseIdx + c.y * gridSize.x + c.x];
            float2 dir = float2.zero;
            for (int i = 0; i < 8; i++)
            {
                int2 n = c + Offset(i);
                if (math.any(n < 0) || math.any(n >= gridSize)) continue;
                int ni = n.y * gridSize.x + n.x;
                if (walkable[ni] == 0) continue;
                // Диагональ только если оба ортогональных соседа проходимы (не срезаем углы зданий).
                if (i >= 4 && (walkable[c.y * gridSize.x + n.x] == 0 || walkable[n.y * gridSize.x + c.x] == 0)) continue;
                ushort v = integration[baseIdx + ni];
                if (v < best) { best = v; dir = math.normalize((float2)Offset(i)); }
            }
            return dir;
        }

        public static int2 Offset(int i) => i switch
        {
            0 => new int2(1, 0), 1 => new int2(-1, 0), 2 => new int2(0, 1), 3 => new int2(0, -1),
            4 => new int2(1, 1), 5 => new int2(-1, 1), 6 => new int2(1, -1), _ => new int2(-1, -1)
        };

        public static float GroundHeight(in NativeArray<float> height, int2 gridSize, float2 origin, float cell, float3 pos)
        {
            // Билинейная интерполяция — без ступенек при движении по склону.
            float2 g = (pos.xz - origin) / cell - 0.5f;
            int2 c0 = math.clamp((int2)math.floor(g), 0, gridSize - 2);
            float2 f = math.saturate(g - c0);
            float h00 = height[c0.y * gridSize.x + c0.x], h10 = height[c0.y * gridSize.x + c0.x + 1];
            float h01 = height[(c0.y + 1) * gridSize.x + c0.x], h11 = height[(c0.y + 1) * gridSize.x + c0.x + 1];
            return math.lerp(math.lerp(h00, h10, f.x), math.lerp(h01, h11, f.x), f.y);
        }
    }
}
