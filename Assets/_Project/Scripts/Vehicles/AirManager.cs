using System.Collections;
using Frontline.Bots;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Самолёты матча: стоянки рядами слева и справа от ряда танков, взлёт по свободной от построек полосе,
    /// возрождение сбитых через respawnDelay. Количество — ползунок «Количество самолётов» в «Новой игре».
    /// Сбитый самолёт засчитывается противнику как уничтоженная техника.
    /// </summary>
    public class AirManager : MonoBehaviour
    {
        public static AirManager I { get; private set; }
        public int planesGermany = 1, planesUssr = 1;
        public float respawnDelay = 40f, firstSpawnDelay = 5f;
        public MapTheme theme;
        public int worldLayer;
        public Vector3 baseGermany, baseUSSR;

        Aircraft[,] _planes;
        float[,] _respawnAt;
        int _slots, _next = 0;
        float _audioT;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        Vector3 BaseOf(int team) => team == 0 ? baseGermany : baseUSSR;
        int CountOf(int team) => team == 0 ? planesGermany : planesUssr;

        void Awake() => I = this;
        void OnEnable() => Aircraft.Destroyed += OnDestroyedPlane;
        void OnDisable() => Aircraft.Destroyed -= OnDestroyedPlane;
        void OnDestroy() { if (I == this) I = null; }

        IEnumerator Start()
        {
            if (Net.NetSession.IsClient) { enabled = false; yield break; }   // у клиента самолётов нет (только у хоста)
            _slots = Mathf.Max(planesGermany, planesUssr);
            _planes = new Aircraft[2, Mathf.Max(1, _slots)];
            _respawnAt = new float[2, Mathf.Max(1, _slots)];
            yield return new WaitForSeconds(firstSpawnDelay);
            for (int team = 0; team < 2; team++)
                for (int s = 0; s < CountOf(team); s++) Spawn(team, s);
        }

        void Spawn(int team, int slot)
        {
            Vector3 b = BaseOf(team);
            Vector3 toC = -b; toC.y = 0; toC = toC.sqrMagnitude > 1 ? toC.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toC);
            // Рядами слева и справа от ряда танков (танки: по 12 в ряд через 10 м, начиная в 40 м перед базой)
            int tanks = VehicleManager.I ? (team == 0 ? VehicleManager.I.tanksGermany : VehicleManager.I.tanksUssr) : 0;
            float tankHalf = Mathf.Max(1, Mathf.Min(12, tanks)) * 5f;
            float sideSign = slot % 2 == 0 ? -1f : 1f;
            int k = slot / 2, col = k % 4, row = k / 4;
            Vector3 p = b + toC * (40f + row * 24f) + side * (sideSign * (tankHalf + 22f + col * 16f));
            float lim = MapGenerator.Half - 25f;
            p.x = Mathf.Clamp(p.x, -lim, lim); p.z = Mathf.Clamp(p.z, -lim, lim);
            var t = Terrain.activeTerrain;
            p.y = t ? t.SampleHeight(p) + t.transform.position.y : 0f;
            float yaw = PickHeading(p, toC, side);
            var a = Aircraft.Create((Faction)team, theme, p, yaw, 1000 * team + _next++ % 1000, transform, worldLayer);
            a.Home = p;
            _planes[team, slot] = a;
        }

        /// <summary>Курс взлёта: к центру карты или вбок — куда первые 110 м свободны от домов и деревьев.</summary>
        float PickHeading(Vector3 p, Vector3 toC, Vector3 side)
        {
            Vector3[] cands = { toC, (toC + side).normalized, (toC - side).normalized, side, -side };
            foreach (var d in cands)
            {
                int n = Physics.SphereCastNonAlloc(p + Vector3.up * 3f, 3f, d, _hits, 110f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore);
                bool clear = true;
                for (int i = 0; i < n; i++)
                {
                    var c = _hits[i].collider;
                    if (!c || c is TerrainCollider || c.GetComponentInParent<Aircraft>()) continue;
                    clear = false; break;
                }
                if (clear) return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            return Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg;
        }

        void Update()
        {
            if (_planes == null) return;
            for (int team = 0; team < 2; team++)
                for (int s = 0; s < CountOf(team); s++)
                {
                    if (_planes[team, s]) continue;
                    if (_respawnAt[team, s] > 0 && Time.time >= _respawnAt[team, s]) { _respawnAt[team, s] = 0; Spawn(team, s); }
                }
            if ((_audioT -= Time.deltaTime) <= 0)
            {
                _audioT = 1f;
                var cam = Camera.main;
                if (cam) Aircraft.AudioBudget(cam.transform.position);
            }
        }

        void OnDestroyedPlane(Aircraft a, int killer)
        {
            if (_planes != null)
                for (int team = 0; team < 2; team++)
                    for (int s = 0; s < _planes.GetLength(1); s++)
                        if (_planes[team, s] == a) { _planes[team, s] = null; _respawnAt[team, s] = Time.time + respawnDelay; }
            if (GameModeManager.I) GameModeManager.I.OnAircraftDestroyed(a, killer);
        }
    }
}
