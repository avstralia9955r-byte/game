using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Разрыв артиллерийского фугаса: яркий огненный шар, высокий столб огня и земли (вчетверо выше обычного взрыва),
    /// широкое пылевое кольцо, комья земли с гравитацией, столб дыма, вспышка света и большая опалина.
    /// Четыре готовых экземпляра по кругу — без создания объектов во время боя.
    /// </summary>
    public static class ArtilleryFx
    {
        const int N = 4;
        static GameObject _template;
        static Host _host;

        class Host : MonoBehaviour
        {
            public GameObject[] inst = new GameObject[N];
            public ParticleSystem[][] ps = new ParticleSystem[N][];
            public Light[] lights = new Light[N];
            public float[] lightT = new float[N];
            public int next;

            void Update()
            {
                for (int i = 0; i < N; i++)
                {
                    var l = lights[i];
                    if (!l || !l.enabled) continue;
                    lightT[i] -= Time.deltaTime;
                    if (lightT[i] <= 0) { l.enabled = false; continue; }
                    l.intensity = 9f * (lightT[i] / 0.4f);
                }
            }
        }

        static void BuildTemplate(MapTheme theme)
        {
            bool winter = theme == MapTheme.Winter;
            var g = new GameObject("ArtilleryBlast");
            g.transform.SetParent(ProceduralModels.Root, false);
            void Add(ParticleSystem p) { p.transform.SetParent(g.transform, false); p.transform.localRotation = Quaternion.Euler(-90f, 0, 0); }
            // Огненный шар — шире и ярче обычного взрыва
            Add(ProceduralFx.Ps("Fireball", MeshUtil.Ico(), 34, 0.8f, 14f, 5.2f, 2.4f, new Color(1f, 0.97f, 0.7f), new Color(1f, 0.72f, 0.2f), -0.2f, 0, burstCone: 55));
            // Столб огня и земли — высота ~4× обычного разрыва
            Add(ProceduralFx.Ps("Column", MeshUtil.Ico(), 28, 1.5f, 38f, 4.2f, 1.8f, new Color(1f, 0.62f, 0.18f), winter ? new Color(0.75f, 0.75f, 0.76f) : new Color(0.38f, 0.3f, 0.22f), 0.35f, 0, burstCone: 9));
            // Комья земли
            Add(ProceduralFx.Ps("Clods", ProceduralModels.CubeMesh, 46, 3.2f, 32f, 1.0f, 0.3f,
                winter ? new Color(0.8f, 0.82f, 0.85f) : new Color(0.22f, 0.16f, 0.09f), winter ? new Color(0.55f, 0.52f, 0.5f) : new Color(0.42f, 0.31f, 0.18f), 1.6f, 6f, burstCone: 32));
            // Пылевое кольцо по земле
            Add(ProceduralFx.Ps("DustRing", MeshUtil.Ico(), 32, 2.4f, 17f, 4.8f, 2.4f,
                winter ? new Color(0.85f, 0.87f, 0.9f) : new Color(0.5f, 0.42f, 0.32f), winter ? Color.white : new Color(0.64f, 0.57f, 0.46f), 0.05f, 0, burstCone: 86, grow: true));
            // Дым
            Add(ProceduralFx.Ps("SmokeColumn", MeshUtil.Ico(), 22, 6f, 11f, 6.5f, 3f, new Color(0.2f, 0.19f, 0.18f), new Color(0.38f, 0.36f, 0.33f), -0.05f, 0, burstCone: 14, grow: true));
            _template = g;
        }

        public static void Play(Vector3 p)
        {
            if (!_host)
            {
                var theme = MatchSession.Current.map ? MatchSession.Current.map.theme : MapTheme.Summer;
                if (_template) Object.Destroy(_template);          // новый матч — шаблон под тему карты (снег / земля)
                BuildTemplate(theme);
                var hg = new GameObject("[ArtilleryFx]");
                _host = hg.AddComponent<Host>();
                for (int i = 0; i < N; i++)
                {
                    var go = Object.Instantiate(_template, hg.transform);
                    go.SetActive(true);
                    _host.inst[i] = go;
                    _host.ps[i] = go.GetComponentsInChildren<ParticleSystem>(true);
                    if (!Application.isMobilePlatform)
                    {
                        var lg = new GameObject("Flash"); lg.transform.SetParent(go.transform, false); lg.transform.localPosition = Vector3.up * 3f;
                        var l = lg.AddComponent<Light>();
                        l.type = LightType.Point; l.color = new Color(1f, 0.72f, 0.38f); l.range = 55f; l.intensity = 0; l.shadows = LightShadows.None; l.enabled = false;
                        _host.lights[i] = l;
                    }
                }
            }
            int k = _host.next; _host.next = (k + 1) % N;
            var inst = _host.inst[k];
            if (!inst) return;
            inst.transform.position = p;
            foreach (var s in _host.ps[k]) { if (!s) continue; s.Clear(true); s.Play(true); }
            var light = _host.lights[k];
            if (light) { light.enabled = true; light.intensity = 9f; _host.lightT[k] = 0.4f; }
            if (FxPool.I)
            {
                FxPool.I.Explosion(p + Vector3.up * 0.5f);
                if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var h, 8f, CombatWorld.WorldMask, QueryTriggerInteraction.Ignore))
                    FxPool.I.Decal(h.point, h.normal, Random.Range(6f, 8f), true);
            }
        }
    }
}
