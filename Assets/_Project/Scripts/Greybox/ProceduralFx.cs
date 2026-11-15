using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>
    /// Эффекты в духе Polyfield: полигональные частицы-меши (икосаэдры и кубики) вместо спрайтов —
    /// дульная вспышка, пыль/снег от попаданий, стилизованная «кровь»-кубики, осколки кирпича, взрыв, дым, трассеры.
    /// Создаёт FxPool и заполняет его шаблонами.
    /// </summary>
    public static class ProceduralFx
    {
        static Material _particle, _tracer;

        static Material ParticleMat()
        {
            if (_particle) return _particle;
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            var sh = urp ? Shader.Find("Universal Render Pipeline/Particles/Unlit") : Shader.Find("Particles/Standard Unlit");
            if (!sh) sh = ProceduralModels.Lit;
            _particle = new Material(sh) { color = Color.white, enableInstancing = true };
            return _particle;
        }

        static Material TracerMat()
        {
            if (_tracer) return _tracer;
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            var sh = urp ? Shader.Find("Universal Render Pipeline/Unlit") : Shader.Find("Unlit/Color");
            _tracer = new Material(sh ? sh : ProceduralModels.Lit) { color = new Color(1f, 0.85f, 0.4f), enableInstancing = true };
            if (_tracer.HasProperty("_BaseColor")) _tracer.SetColor("_BaseColor", new Color(1f, 0.85f, 0.4f));
            return _tracer;
        }

        public static FxPool Create(Transform parent, MapTheme theme)
        {
            var go = new GameObject("FxPool");
            go.SetActive(false);
            go.transform.SetParent(parent);
            var pool = go.AddComponent<FxPool>();
            bool winter = theme == MapTheme.Winter;
            Set(pool, "muzzleFlashPrefab", Ps("MuzzleFlash", MeshUtil.Ico(), 6, 0.06f, 0.0f, 0.35f, 0.3f, new Color(1f, 0.75f, 0.3f), new Color(1f, 0.95f, 0.6f), 0, 8f, burstCone: 12));
            Set(pool, "impactDustPrefab", Ps("ImpactDust", MeshUtil.Ico(), 9, 0.6f, 2.5f, 0.12f, 0.06f, new Color(0.45f, 0.38f, 0.28f), new Color(0.6f, 0.52f, 0.4f), 0.6f, 0, burstCone: 35));
            Set(pool, "impactSnowPrefab", Ps("ImpactSnow", MeshUtil.Ico(), 10, 0.7f, 2.2f, 0.12f, 0.06f, new Color(0.9f, 0.92f, 0.95f), Color.white, 0.4f, 0, burstCone: 40));
            Set(pool, "bloodPolyPrefab", Ps("BloodPoly", ProceduralModels.CubeMesh, 10, 0.55f, 3f, 0.09f, 0.04f, new Color(0.55f, 0.05f, 0.05f), new Color(0.75f, 0.1f, 0.08f), 1.2f, 0, burstCone: 30));
            Set(pool, "brickChipsPrefab", Ps("BrickChips", ProceduralModels.CubeMesh, 8, 0.8f, 3.5f, 0.1f, 0.05f, new Color(0.55f, 0.24f, 0.17f), new Color(0.7f, 0.35f, 0.25f), 1.5f, 0, burstCone: 40));
            Set(pool, "explosionPrefab", Ps("Explosion", MeshUtil.Ico(), 34, 0.9f, 9f, 1.6f, 0.6f, new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.35f), -0.1f, 0, burstCone: 90));
            Set(pool, "smokePrefab", Ps("Smoke", MeshUtil.Ico(), 18, 3.5f, 1.5f, 2.4f, 1.2f, new Color(0.25f, 0.24f, 0.23f), winter ? new Color(0.7f, 0.7f, 0.72f) : new Color(0.45f, 0.43f, 0.4f), -0.05f, 0, burstCone: 60, grow: true));
            var cube = ProceduralModels.CubeMesh;
            Set(pool, "tracerMesh", cube);
            Set(pool, "tracerMaterial", TracerMat());
            go.SetActive(true);
            return pool;
        }

        /// <summary>Одноразовый всплеск полигональных частиц-мешей.</summary>
        internal static ParticleSystem Ps(string name, Mesh mesh, int count, float life, float speed, float sizeMax, float sizeMin,
                                 Color c1, Color c2, float gravity, float spin, float burstCone = 25, bool grow = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(ProceduralModels.Root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(c1, c2);
            main.gravityModifier = gravity;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = burstCone; sh.radius = 0.02f;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, grow ? AnimationCurve.Linear(0, 0.5f, 1, 1.8f) : AnimationCurve.EaseInOut(0, 1, 1, 0));
            if (spin > 0) { var rol = ps.rotationOverLifetime; rol.enabled = true; rol.z = spin; }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = mesh;
            r.sharedMaterial = ParticleMat();
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static void Set(object t, string f, object v)
        {
            var fi = t.GetType().GetField(f, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null) fi.SetValue(t, v); else Debug.LogError("[Fx] нет поля " + f);
        }
    }
}
