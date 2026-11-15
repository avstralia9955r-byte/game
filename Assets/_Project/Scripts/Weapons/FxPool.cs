using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Пулы эффектов: дульная вспышка, трассеры, пыль/искры попаданий, стилизованная полигональная кровь, взрывы, дым.
    /// Без Instantiate в бою — только переиспользование. Трассеры рисуются одним инстанс-вызовом.
    /// </summary>
    public class FxPool : MonoBehaviour
    {
        public static FxPool I { get; private set; }

        [SerializeField] ParticleSystem muzzleFlashPrefab, impactDustPrefab, impactSnowPrefab, bloodPolyPrefab,
                                        brickChipsPrefab, explosionPrefab, smokePrefab;
        [SerializeField] Mesh tracerMesh;          // вытянутый quad/октаэдр
        [SerializeField] Material tracerMaterial;  // unlit additive, GPU instancing
        [SerializeField] int maxTracers = 512;

        readonly Dictionary<ParticleSystem, Queue<ParticleSystem>> _pools = new();

        struct TracerData { public Vector3 pos, dir; public float speed, life; }
        TracerData[] _tracers;
        int _tracerCount;
        Matrix4x4[] _tracerMatrices;
        RenderParams _tracerRp;
        readonly Matrix4x4[] _bulletM = new Matrix4x4[256];
        int _bulletCount;

        /// <summary>Пуля игрока в полёте: светящийся штрих от прошлой позиции к текущей (рисуется в этом кадре).</summary>
        public void Bullet(Vector3 from, Vector3 to, float width)
        {
            if (_bulletCount >= _bulletM.Length) return;
            Vector3 d = to - from; float len = d.magnitude;
            if (len < 0.05f) return;
            _bulletM[_bulletCount++] = Matrix4x4.TRS((from + to) * 0.5f, Quaternion.LookRotation(d / len), new Vector3(width, width, len));
        }

        void Awake()
        {
            I = this;
            _tracers = new TracerData[maxTracers];
            _tracerMatrices = new Matrix4x4[maxTracers];
            if (tracerMaterial) _tracerRp = new RenderParams(tracerMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off };
            foreach (var p in new[] { muzzleFlashPrefab, impactDustPrefab, impactSnowPrefab, bloodPolyPrefab, brickChipsPrefab, explosionPrefab, smokePrefab })
                if (p) Prewarm(p, p == explosionPrefab ? 8 : 32);
        }

        void Prewarm(ParticleSystem prefab, int n)
        {
            var q = new Queue<ParticleSystem>(n);
            for (int i = 0; i < n; i++) { var ps = Instantiate(prefab, transform); ps.gameObject.SetActive(false); q.Enqueue(ps); }
            _pools[prefab] = q;
        }

        void Play(ParticleSystem prefab, Vector3 pos, Quaternion rot, Transform parent = null)
        {
            if (!prefab || !_pools.TryGetValue(prefab, out var q) || q.Count == 0) return;
            var ps = q.Dequeue();
            // Вспышку, прикреплённую к модели оружия, могли уничтожить вместе с моделью (смена оружия) — заменяем новой
            if (!ps) { ps = Instantiate(prefab, transform); ps.gameObject.SetActive(false); }
            q.Enqueue(ps);                                    // кольцевой буфер: самый старый эффект переиспользуется
            var t = ps.transform;
            t.SetParent(parent ? parent : transform, false);
            // Вспышка у оружия от первого лица рисуется той же камерой, что и оружие (свой слой)
            int layer = parent ? parent.gameObject.layer : 0;
            if (ps.gameObject.layer != layer) foreach (var c in ps.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
            t.SetPositionAndRotation(pos, rot);
            ps.gameObject.SetActive(true);
            ps.Clear(true); ps.Play(true);
        }

        public void MuzzleFlashAt(Vector3 pos, Vector3 dir) => Play(muzzleFlashPrefab, pos, Quaternion.LookRotation(dir));
        public void MuzzleFlash(Transform point) { if (point) Play(muzzleFlashPrefab, point.position, point.rotation, point); }

        /// <summary>Вернуть в пул эффекты, прикреплённые к объекту, который сейчас будет уничтожен (модель оружия).</summary>
        public void Detach(Transform root)
        {
            if (!root) return;
            foreach (var q in _pools.Values)
                foreach (var ps in q)
                    if (ps && ps.transform.IsChildOf(root))
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        ps.transform.SetParent(transform, false);
                        ps.gameObject.SetActive(false);
                    }
        }
        public void Impact(Vector3 p, Vector3 n, MapTheme theme) { Play(theme == MapTheme.Winter ? impactSnowPrefab : impactDustPrefab, p, Quaternion.LookRotation(n)); Decal(p, n, Random.Range(0.06f, 0.1f), false); }
        public void Blood(Vector3 p, Vector3 dir) => Play(bloodPolyPrefab, p, Quaternion.LookRotation(-dir));
        public void BrickChips(Vector3 p, Vector3 n) => Play(brickChipsPrefab, p, Quaternion.LookRotation(n));
        public void Explosion(Vector3 p)
        {
            Play(explosionPrefab, p, Quaternion.identity); Play(smokePrefab, p, Quaternion.identity);
            // Опалённое пятно на земле под взрывом
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var h, 6f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore))
                Decal(h.point, h.normal, Random.Range(2.2f, 3.4f), true);
        }

        // ---------------- Следы: пулевые отверстия и опалины (кольцевой буфер, один инстанс-вызов на тип) ----------------
        Matrix4x4[] _holes = new Matrix4x4[1];     // размер — в InitDecals (Application нельзя трогать в конструкторе)
        readonly Matrix4x4[] _scorch = new Matrix4x4[48];
        int _holeN, _holeHead, _scorchN, _scorchHead;
        Mesh _quad; RenderParams _holeRp, _scorchRp; bool _decalInit;

        public void Decal(Vector3 p, Vector3 n, float size, bool scorch)
        {
            if (!_decalInit) InitDecals();
            if (!_quad) return;
            var m = Matrix4x4.TRS(p + n * (scorch ? 0.05f : 0.008f), Quaternion.LookRotation(-n) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)), new Vector3(size, size, 1f));
            if (scorch) { _scorch[_scorchHead] = m; _scorchHead = (_scorchHead + 1) % _scorch.Length; _scorchN = Mathf.Min(_scorchN + 1, _scorch.Length); }
            else { _holes[_holeHead] = m; _holeHead = (_holeHead + 1) % _holes.Length; _holeN = Mathf.Min(_holeN + 1, _holes.Length); }
        }

        void InitDecals()
        {
            _decalInit = true;
            _holes = new Matrix4x4[Application.isMobilePlatform ? 120 : 400]; _holeN = _holeHead = 0;
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g);
            var hole = ProceduralModels.Mat(new Color(0.05f, 0.045f, 0.04f), 0.05f); hole.enableInstancing = true;
            var scorch = ProceduralModels.Mat(new Color(0.08f, 0.07f, 0.06f), 0.02f); scorch.enableInstancing = true;
            _holeRp = new RenderParams(hole) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true };
            _scorchRp = new RenderParams(scorch) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true };
        }

        void DrawDecals()
        {
            if (!_quad) return;
            if (_holeN > 0) Graphics.RenderMeshInstanced(_holeRp, _quad, 0, _holes, _holeN);
            if (_scorchN > 0) Graphics.RenderMeshInstanced(_scorchRp, _quad, 0, _scorch, _scorchN);
        }

        /// <summary>Новый матч — следы старого не нужны.</summary>
        public void ClearDecals() { _holeN = _holeHead = _scorchN = _scorchHead = 0; }
        public void Smoke(Vector3 p) => Play(smokePrefab, p, Quaternion.identity);

        public void Tracer(Vector3 from, Vector3 to, float speed)
        {
            if (_tracerCount >= maxTracers) return;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 2f) return;
            _tracers[_tracerCount++] = new TracerData { pos = from, dir = d / len, speed = speed, life = len / speed };
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            int w = 0;
            for (int i = 0; i < _tracerCount; i++)
            {
                var t = _tracers[i];
                t.life -= dt;
                if (t.life <= 0) continue;
                t.pos += t.dir * (t.speed * dt);
                _tracers[w] = t;
                _tracerMatrices[w] = Matrix4x4.TRS(t.pos, Quaternion.LookRotation(t.dir), new Vector3(0.03f, 0.03f, Mathf.Min(6f, t.speed * 0.012f)));
                w++;
            }
            _tracerCount = w;
            if (w > 0 && tracerMesh && tracerMaterial)
                Graphics.RenderMeshInstanced(_tracerRp, tracerMesh, 0, _tracerMatrices, w);
            DrawDecals();
            if (_bulletCount > 0 && tracerMesh && tracerMaterial)
                Graphics.RenderMeshInstanced(_tracerRp, tracerMesh, 0, _bulletM, _bulletCount);
            _bulletCount = 0;
        }
    }
}
