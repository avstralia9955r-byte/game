using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{

    /// <summary>
    /// Стена — локальная сетка length × height, кирпичи перевязкой (нечётные ряды смещены на половину).
    /// Кирпич ставится только на землю (нижний ряд) или поверх двух/одного кирпича ниже.
    /// </summary>
    public class BrickWall
    {
        public readonly Vector3 origin;      // левый нижний край стены на земле
        public readonly Quaternion rotation;
        public readonly int maxLength, maxHeight;
        public readonly byte team;
        readonly Brick[,] _cells;
        readonly bool[,] _obstacle;          // клетка занята препятствием (ящик, мешки, забор, камень) — служит опорой
        public int Count { get; private set; }

        /// <summary>Результат проверки клетки: можно класть / занята препятствием (опора для ряда выше) / нет опоры.</summary>
        public enum CellCheck : byte { Free, Obstacle, NoGround }

        public BrickWall(Vector3 origin, Quaternion rot, int len, int h, byte team)
        {
            this.origin = origin; rotation = rot; maxLength = len; maxHeight = h; this.team = team;
            _cells = new Brick[len + 1, h];   // +1 — нечётные ряды на полкирпича длиннее с краю
            _obstacle = new bool[len + 1, h];
        }

        public Vector3 Right => rotation * Vector3.right;
        public bool IsFull => NextFreeCell(null) == null;

        int RowLength(int row) => (row & 1) == 0 ? maxLength : maxLength + 1;

        public Vector3 CellCenter(Vector2Int c, Vector3 brickSize)
        {
            bool odd = (c.y & 1) == 1;
            float x = (c.x + 0.5f) * brickSize.x - (odd ? brickSize.x * 0.5f : 0);
            // Крайние кирпичи нечётного ряда — половинки (визуально масштабируются в Place).
            return origin + rotation * new Vector3(x, (c.y + 0.5f) * brickSize.y, 0);
        }

        /// <summary>
        /// Следующая клетка для кирпича: снизу вверх, внутри ряда — от центра к краям (стена растёт ровно).
        /// Клетки, занятые препятствием, пропускаются и считаются опорой — стена обходит ящик или мешки и растёт
        /// дальше вверх, а не упирается в «Мешает препятствие».
        /// </summary>
        public Vector2Int? NextFreeCell(System.Func<Vector2Int, CellCheck> check)
        {
            System.Array.Clear(_obstacle, 0, _obstacle.Length);
            for (int y = 0; y < maxHeight; y++)
            {
                int len = RowLength(y);
                for (int i = 0; i < len; i++)
                {
                    int x = len / 2 + ((i & 1) == 0 ? i / 2 : -(i / 2 + 1));
                    if (x < 0 || x >= len) continue;
                    var c = new Vector2Int(x, y);
                    if (_cells[x, y] != null || !IsSupported(c)) continue;
                    var r = check == null ? CellCheck.Free : check(c);
                    if (r == CellCheck.Free) return c;
                    if (r == CellCheck.Obstacle) _obstacle[x, y] = true;
                }
            }
            return null;
        }

        public bool IsSupported(Vector2Int c)
        {
            if (c.y == 0) return true;               // земля проверяется при создании стены/укладке
            int below = c.y - 1;
            bool odd = (c.y & 1) == 1;
            // Нечётный ряд сдвинут влево: клетка x опирается на x-1 и x нижнего ряда; чётный — на x и x+1.
            int a = odd ? c.x - 1 : c.x, b = odd ? c.x : c.x + 1;
            return Has(a, below) || Has(b, below);
        }

        bool Has(int x, int y) => x >= 0 && y >= 0 && x < RowLength(y) && (_cells[x, y] != null || _obstacle[x, y]);

        public void Set(Vector2Int c, Brick b) { _cells[c.x, c.y] = b; Count++; }

        public void RemoveBrick(Brick b, bool fx)
        {
            if (!b || _cells[b.Cell.x, b.Cell.y] != b) return;
            _cells[b.Cell.x, b.Cell.y] = null;
            Count--;
            if (fx && FxPool.I) FxPool.I.BrickChips(b.transform.position, Vector3.up);
            BrickSystem.I.Release(b);

            // Каскад: всё, что потеряло опору, осыпается.
            for (int y = b.Cell.y + 1; y < maxHeight; y++)
                for (int x = 0; x < RowLength(y); x++)
                {
                    var above = _cells[x, y];
                    if (above && !IsSupported(new Vector2Int(x, y))) RemoveBrick(above, true);
                }
            if (Count == 0) BrickSystem.I.RemoveWall(this);
        }

        public IEnumerable<Brick> All()
        {
            foreach (var b in _cells) if (b) yield return b;
        }
    }

    /// <summary>
    /// Менеджер строительства: пул кирпичей, стены, проверки коллизий, урон от взрывов.
    /// Высота/длина стены настраиваются в инспекторе (по умолчанию 5×3).
    /// </summary>
    public class BrickSystem : MonoBehaviour
    {
        public static BrickSystem I { get; private set; }

        public Brick brickPrefab;
        public Vector3 brickSize = new(0.6f, 0.3f, 0.3f);
        [Range(1, 20)] public int maxWallLength = 5;
        [Range(1, 10)] public int maxWallHeight = 6;     // до 1,8 м — можно строить и вверх
        public int maxBricksInWorld = 1500;
        public float joinDistance = 1.2f;         // насколько близко к стене нужно целиться, чтобы достраивать её
        public LayerMask groundMask;

        readonly List<BrickWall> _walls = new();
        readonly Stack<Brick> _pool = new();
        int _live;

        public IReadOnlyList<BrickWall> Walls => _walls;

        void Awake()
        {
            I = this;
            for (int i = 0; i < 128; i++) { var b = Instantiate(brickPrefab, transform); b.gameObject.SetActive(false); _pool.Push(b); }
        }

        public struct PlacementPreview
        {
            public bool valid;
            public BrickWall wall;      // null → будет создана новая
            public Vector3 wallOrigin;
            public Quaternion rotation;
            public Vector2Int cell;
            public Vector3 position;
            public string reason;
        }

        /// <summary>Расчёт, куда встанет следующий кирпич, по лучу прицела.</summary>
        public PlacementPreview Evaluate(Ray aim, float maxDistance, float builderYaw, byte team)
        {
            var p = new PlacementPreview();
            if (_live >= maxBricksInWorld) { p.reason = "Лимит кирпичей"; return p; }
            if (!Physics.Raycast(aim, out var hit, maxDistance, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore))
            { p.reason = "Слишком далеко"; return p; }

            // 1) Целимся в существующий кирпич → достраиваем его стену.
            var brick = hit.collider.GetComponent<Brick>();
            BrickWall wall = brick ? brick.Wall : null;

            // 2) Целимся в землю рядом с линией существующей стены → тоже достраиваем.
            bool ground = !brick && hit.normal.y > 0.75f && ((1 << hit.collider.gameObject.layer) & groundMask) != 0;
            if (!brick && !ground) { p.reason = "Только на землю или кирпичи"; return p; }
            if (ground) wall = FindWallNear(hit.point);

            BrickWall target = wall;
            if (target == null)
            {
                // Новая стена — поперёк взгляда строителя, шаг поворота 15°, центр — в точке прицела.
                float yaw = Mathf.Round(builderYaw / 15f) * 15f;
                p.rotation = Quaternion.Euler(0, yaw, 0);
                Vector3 right = p.rotation * Vector3.right;
                p.wallOrigin = hit.point - right * (maxWallLength * brickSize.x * 0.5f);
                target = new BrickWall(p.wallOrigin, p.rotation, maxWallLength, maxWallHeight, team);
            }
            else { p.wall = wall; p.rotation = wall.rotation; }

            BrickWall.CellCheck Check(Vector2Int cell)
            {
                var pos = target.CellCenter(cell, brickSize);
                if (!IsSpaceFree(pos, target.rotation, out bool byActor)) return byActor ? BrickWall.CellCheck.NoGround : BrickWall.CellCheck.Obstacle;
                if (cell.y == 0 && !HasGroundBelow(pos)) return BrickWall.CellCheck.NoGround;
                return BrickWall.CellCheck.Free;
            }
            var c = target.NextFreeCell(Check);
            if (c == null)
            {
                p.reason = wall != null && wall.IsFull ? "Стена достроена" : "Мешает препятствие";
                p.position = target.CellCenter(new Vector2Int(maxWallLength / 2, 0), brickSize);
                return p;
            }
            p.cell = c.Value;
            p.position = target.CellCenter(c.Value, brickSize);
            p.valid = true;
            return p;
        }

        public Brick Place(in PlacementPreview p, byte team)
        {
            if (!p.valid) return null;
            var wall = p.wall;
            if (wall == null)
            {
                wall = new BrickWall(p.wallOrigin, p.rotation, maxWallLength, maxWallHeight, team);
                _walls.Add(wall);
            }
            var b = _pool.Count > 0 ? _pool.Pop() : Instantiate(brickPrefab, transform);
            b.transform.SetPositionAndRotation(p.position, p.rotation);
            // Крайние кирпичи нечётных рядов — половинки, чтобы торцы стены были ровными.
            bool odd = (p.cell.y & 1) == 1;
            bool edgeHalf = odd && (p.cell.x == 0 || p.cell.x == wall.maxLength);
            b.transform.localScale = edgeHalf ? new Vector3(0.5f, 1, 1) : Vector3.one;
            if (edgeHalf) b.transform.position += wall.Right * (p.cell.x == 0 ? brickSize.x * 0.25f : -brickSize.x * 0.25f);
            b.Wall = wall; b.Cell = p.cell;
            b.gameObject.SetActive(true);
            wall.Set(p.cell, b);
            _live++;
            // Стена от 2 рядов (> высоты шага) перекрывает клетку навигации — flow field ботов обойдёт её.
            if (p.cell.y == 1 && Bots.FlowFieldBaker.I) Bots.FlowFieldBaker.I.SetBlocked(b.transform.position, true);
            CombatWorld.EmitNoise(p.position, 10f, -1, team, 3);
            return b;
        }

        /// <summary>Запросы ботов-инженеров (Hard+): по одному кирпичу на запрос, тем же правилам, что у игрока.</summary>
        void Update()
        {
            if (!Bots.BotRegistry.BuildRequests.IsCreated) return;
            int budget = 8;
            while (budget-- > 0 && Bots.BotRegistry.BuildRequests.TryDequeue(out var r))
            {
                var ray = new Ray((Vector3)r.position + Vector3.up * 2f, Vector3.down);
                var p = Evaluate(ray, 4f, r.yaw, r.team);
                if (p.valid) Place(p, r.team);
            }
        }

        public void Release(Brick b)
        {
            if (b.Cell.y == 1 && Bots.FlowFieldBaker.I) Bots.FlowFieldBaker.I.SetBlocked(b.transform.position, false);
            b.gameObject.SetActive(false); _pool.Push(b); _live--;
        }
        public void RemoveWall(BrickWall w) => _walls.Remove(w);

        BrickWall FindWallNear(Vector3 point)
        {
            BrickWall best = null; float bestD = joinDistance;
            foreach (var w in _walls)
            {
                // Расстояние до отрезка основания стены.
                Vector3 a = w.origin, b = w.origin + w.Right * (w.maxLength * brickSize.x);
                Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude);
                float d = Vector3.Distance(point, a + ab * t);
                if (d < bestD && !w.IsFull) { bestD = d; best = w; }
            }
            return best;
        }

        bool HasGroundBelow(Vector3 center)
        {
            Vector3 bottom = center - Vector3.up * (brickSize.y * 0.5f - 0.02f);
            return Physics.Raycast(bottom, Vector3.down, 0.18f, groundMask, QueryTriggerInteraction.Ignore);
        }

        bool IsSpaceFree(Vector3 center, Quaternion rot, out bool byActor)
        {
            byActor = false;
            Vector3 half = brickSize * 0.45f; // небольшой зазор — соседние кирпичи не мешают
            if (Physics.CheckBox(center, half, rot, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore)) return false;
            byActor = true;
            // Нельзя замуровать бойцов: проверка против аналитических капсул акторов.
            foreach (var a in CombatWorld.Actors)
            {
                if (!a.alive) continue;
                Vector3 pos = a.position;
                if (Mathf.Abs(center.y - (pos.y + 0.9f)) < 1.2f &&
                    new Vector2(center.x - pos.x, center.z - pos.z).magnitude < CombatWorld.BodyRadius + brickSize.x * 0.5f)
                    return false;
            }
            return true;
        }

        /// <summary>Взрыв: урон кирпичам в радиусе с затуханием (вызывается из гранат/артиллерии).</summary>
        public void Explosion(Vector3 center, float radius, float damage)
        {
            var hits = Physics.OverlapSphere(center, radius, CombatWorld.BrickMask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                var b = h.GetComponent<Brick>();
                if (!b || !b.gameObject.activeSelf) continue;
                float k = 1f - Vector3.Distance(center, b.transform.position) / radius;
                b.Damage(damage * Mathf.Clamp01(k) * 2.5f);
            }
        }
    }
}
