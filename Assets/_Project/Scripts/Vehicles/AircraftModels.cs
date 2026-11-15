using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Низкополигональные истребители: Германия — Bf 109 G, СССР — Як-9. Нос по +Z, центр — ось фюзеляжа.
    /// Шасси с хвостовым колесом (на земле нос задран), винт крутится, под крыльями по 5 ракет (РС-82 / Wfr.Gr).
    /// </summary>
    public static class AircraftModels
    {
        public class Rig
        {
            public GameObject root;
            public Transform prop, pilot;
            public Transform[] guns = new Transform[2];
            public Transform[] rockets = new Transform[10];
        }

        static Transform _p;

        static GameObject B(string n, Vector3 pos, Vector3 size, Color c, Vector3 euler = default, float bevel = 0.08f, float smooth = 0.35f, float metal = 0.25f)
            => ProceduralModels.Part(_p, n, MeshUtil.Bevel(bevel), pos, size, c, euler, false, smooth, metal);
        static GameObject C(string n, Vector3 pos, float r, float len, Color c, Vector3 euler, int seg = 12, float smooth = 0.4f, float metal = 0.3f)
            => ProceduralModels.Part(_p, n, MeshUtil.Cylinder(seg), pos, new Vector3(r * 2, len, r * 2), c, euler, false, smooth, metal);
        static GameObject F(string n, Vector3 pos, float r, float len, float top, Color c, Vector3 euler, int seg = 12)
            => ProceduralModels.Part(_p, n, MeshUtil.Frustum(seg, top), pos, new Vector3(r * 2, len, r * 2), c, euler, false, 0.4f, 0.25f);
        static GameObject T(string n, Vector3 pos, Vector3 size, float tx, float tz, Color c, Vector3 euler)
            => ProceduralModels.Part(_p, n, MeshUtil.Taper(tx, tz), pos, size, c, euler, false, 0.35f, 0.25f);

        public static Rig Build(Faction f, MapTheme theme, Transform parent)
        {
            bool de = f == Faction.Germany, winter = theme == MapTheme.Winter;
            Color paint = de ? new Color(0.42f, 0.45f, 0.4f) : new Color(0.3f, 0.37f, 0.24f);
            if (winter) paint = new Color(0.82f, 0.83f, 0.82f);
            Color belly = de ? new Color(0.58f, 0.66f, 0.74f) : new Color(0.55f, 0.62f, 0.68f);
            Color dark = Color.Lerp(paint, Color.black, 0.45f), steel = new(0.28f, 0.28f, 0.3f), tyre = new(0.08f, 0.08f, 0.08f);
            Color glass = new(0.35f, 0.45f, 0.52f), yellow = new(0.85f, 0.7f, 0.15f), spinner = de ? new Color(0.15f, 0.15f, 0.14f) : new Color(0.75f, 0.12f, 0.1f);

            var rig = new Rig();
            var root = new GameObject(de ? "Plane_Bf109" : "Plane_Yak9");
            root.transform.SetParent(parent, false);
            rig.root = root; _p = root.transform;

            // ---- Фюзеляж ----
            C("Fuselage", new Vector3(0, 0, 0.6f), 0.56f, 4.4f, paint, new Vector3(90, 0, 0), 12);
            F("FuselageRear", new Vector3(0, 0.12f, -2.8f), 0.56f, 2.6f, 0.32f, paint, new Vector3(-90, 0, 0));
            B("Belly", new Vector3(0, -0.36f, 0.4f), new Vector3(0.8f, 0.3f, 3.8f), belly, default, 0.1f);
            C("Cowling", new Vector3(0, 0.02f, 3.35f), 0.52f, 1.1f, de ? yellow : dark, new Vector3(90, 0, 0), 12);
            ProceduralModels.Part(_p, "Spinner", MeshUtil.Cone(10), new Vector3(0, 0.02f, 3.9f), new Vector3(0.62f, 0.55f, 0.62f), spinner, new Vector3(90, 0, 0), false, 0.5f, 0.3f);
            for (int i = 0; i < 6; i++) B("Exhaust", new Vector3(i % 2 == 0 ? 0.53f : -0.53f, 0.12f, 2.6f + (i / 2) * 0.22f), new Vector3(0.08f, 0.08f, 0.16f), steel, default, 0.02f);
            if (!de) B("Radiator", new Vector3(0, -0.62f, -0.2f), new Vector3(0.55f, 0.3f, 1.1f), dark, default, 0.08f);
            else for (int s = -1; s <= 1; s += 2) B("Radiator", new Vector3(s * 1.7f, -0.42f, 0.7f), new Vector3(0.6f, 0.18f, 0.7f), dark, default, 0.05f);

            // ---- Фонарь кабины и пилот ----
            B("Canopy", new Vector3(0, 0.66f, 0.15f), new Vector3(0.64f, 0.5f, 1.55f), glass, default, 0.18f, 0.9f, 0.1f);
            B("CanopyFrame", new Vector3(0, 0.66f, 0.15f), new Vector3(0.66f, 0.06f, 1.57f), dark, default, 0.02f);
            B("CanopyFrame", new Vector3(0, 0.66f, 0.55f), new Vector3(0.66f, 0.52f, 0.05f), dark, default, 0.02f);
            B("Spine", new Vector3(0, 0.55f, -1.4f), new Vector3(0.44f, 0.34f, 1.7f), paint, new Vector3(-6, 0, 0), 0.12f);
            var pilot = new GameObject("Pilot").transform; pilot.SetParent(_p, false); pilot.localPosition = new Vector3(0, 0.62f, 0.05f);
            rig.pilot = pilot;
            _p = pilot;
            ProceduralModels.Part(_p, "Head", MeshUtil.Ico(), Vector3.zero, new Vector3(0.26f, 0.3f, 0.28f), new Color(0.75f, 0.6f, 0.5f), default, false, 0.2f, 0f);
            ProceduralModels.Part(_p, "Helmet", MeshUtil.Ico(), new Vector3(0, 0.05f, -0.02f), new Vector3(0.3f, 0.26f, 0.32f), new Color(0.3f, 0.22f, 0.15f), default, false, 0.3f, 0f);
            _p = root.transform;

            // ---- Крылья (сужаются к законцовкам, лёгкое V) ----
            float span = de ? 4.6f : 4.8f;
            for (int s = -1; s <= 1; s += 2)
            {
                T(s > 0 ? "WingR" : "WingL", new Vector3(s * (span * 0.5f + 0.35f), -0.3f + span * 0.5f * 0.07f, 0.75f),
                  new Vector3(0.18f, span, 1.75f), 0.55f, 0.55f, paint, new Vector3(0, 0, s > 0 ? -86f : 86f));
                B("Aileron", new Vector3(s * (span * 0.72f + 0.35f), -0.3f + span * 0.72f * 0.07f, 0.0f), new Vector3(span * 0.35f, 0.06f, 0.3f), dark, new Vector3(0, 0, s * 4f), 0.02f);
                // Пулемёты в крыльях (ствол наружу) и ракеты на балках
                var gun = C("WingGun", new Vector3(s * 1.55f, -0.28f, 1.75f), 0.04f, 0.6f, steel, new Vector3(90, 0, 0), 6).transform;
                var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(_p, false); muzzle.localPosition = new Vector3(s * 1.55f, -0.28f, 2.1f);
                rig.guns[s > 0 ? 1 : 0] = muzzle;
                for (int i = 0; i < 5; i++)
                {
                    float x = s * (1.95f + i * 0.42f);
                    float y = -0.3f + Mathf.Abs(x) * 0.07f - 0.22f;
                    B("Rail", new Vector3(x, y + 0.1f, 0.85f), new Vector3(0.05f, 0.08f, 1.1f), steel, default, 0.01f);
                    var rk = new GameObject("Rocket").transform; rk.SetParent(_p, false); rk.localPosition = new Vector3(x, y, 0.95f);
                    _p = rk;
                    C("Body", Vector3.zero, 0.055f, 0.95f, new Color(0.4f, 0.42f, 0.36f), new Vector3(90, 0, 0), 6);
                    ProceduralModels.Part(_p, "Tip", MeshUtil.Cone(6), new Vector3(0, 0, 0.47f), new Vector3(0.11f, 0.18f, 0.11f), new Color(0.3f, 0.3f, 0.28f), new Vector3(90, 0, 0), false, 0.3f, 0.2f);
                    B("Fin", new Vector3(0, 0, -0.45f), new Vector3(0.24f, 0.02f, 0.14f), steel, default, 0.005f);
                    _p = root.transform;
                    rig.rockets[(s > 0 ? 5 : 0) + i] = rk;
                }
                // Опознавательные знаки на крыльях
                Insignia(new Vector3(s * (span * 0.62f + 0.35f), -0.3f + span * 0.62f * 0.07f + 0.1f, 0.7f), de, true);
            }

            // ---- Хвост ----
            B("Stabilizer", new Vector3(0, 0.28f, -3.75f), new Vector3(3.3f, 0.09f, 0.85f), paint, default, 0.04f);
            B("Elevator", new Vector3(0, 0.28f, -4.25f), new Vector3(3.0f, 0.06f, 0.25f), dark, default, 0.02f);
            T("Fin", new Vector3(0, 0.95f, -3.85f), new Vector3(0.1f, 1.3f, 1.15f), 1f, 0.55f, paint, default);
            B("Rudder", new Vector3(0, 0.9f, -4.45f), new Vector3(0.07f, 1.15f, 0.3f), dark, default, 0.02f);
            if (!de) Insignia(new Vector3(0.07f, 0.95f, -3.85f), false, false);
            Insignia(new Vector3(0.48f, 0.05f, -1.9f), de, false);

            // ---- Шасси ----
            for (int s = -1; s <= 1; s += 2)
            {
                B("Strut", new Vector3(s * 1.05f, -0.75f, 1.3f), new Vector3(0.09f, 0.9f, 0.09f), steel, new Vector3(0, 0, s * -8f), 0.02f);
                C("Wheel", new Vector3(s * 1.12f, -1.18f, 1.3f), 0.32f, 0.16f, tyre, new Vector3(0, 0, 90), 14, 0.1f, 0f);
                B("GearDoor", new Vector3(s * 1.02f, -0.8f, 1.42f), new Vector3(0.03f, 0.55f, 0.32f), paint, new Vector3(0, 0, s * -8f), 0.02f);
            }
            B("TailStrut", new Vector3(0, -0.4f, -3.9f), new Vector3(0.06f, 0.4f, 0.06f), steel, default, 0.01f);
            C("TailWheel", new Vector3(0, -0.62f, -3.9f), 0.15f, 0.08f, tyre, new Vector3(0, 0, 90), 10, 0.1f, 0f);

            // ---- Винт ----
            var prop = new GameObject("Prop").transform; prop.SetParent(root.transform, false); prop.localPosition = new Vector3(0, 0.02f, 4.02f);
            rig.prop = prop;
            _p = prop;
            for (int i = 0; i < 3; i++)
            {
                var blade = new GameObject("Blade").transform; blade.SetParent(prop, false); blade.localRotation = Quaternion.Euler(0, 0, i * 120f);
                _p = blade;
                B("BladeMesh", new Vector3(0, 0.8f, 0), new Vector3(0.2f, 1.45f, 0.05f), new Color(0.12f, 0.12f, 0.11f), new Vector3(0, 18f, 0), 0.02f, 0.3f, 0.1f);
                B("BladeTip", new Vector3(0, 1.48f, 0), new Vector3(0.2f, 0.1f, 0.055f), yellow, new Vector3(0, 18f, 0), 0.01f);
                _p = prop;
            }
            _p = root.transform;

            // Приборная доска и коллиматорный прицел — видны из кабины (F5)
            B("Dash", new Vector3(0, 0.5f, 0.6f), new Vector3(0.62f, 0.3f, 0.06f), new Color(0.12f, 0.12f, 0.12f), new Vector3(-15, 0, 0), 0.02f, 0.2f, 0.1f);
            for (int i = 0; i < 5; i++)
                ProceduralModels.Part(_p, "Gauge", MeshUtil.Cylinder(10), new Vector3(-0.22f + i * 0.11f, 0.53f + (i % 2) * 0.06f, 0.565f), new Vector3(0.075f, 0.01f, 0.075f), new Color(0.85f, 0.85f, 0.8f), new Vector3(75, 0, 0), false, 0.6f, 0f);
            B("GunsightBase", new Vector3(0, 0.7f, 0.58f), new Vector3(0.1f, 0.1f, 0.12f), new Color(0.15f, 0.15f, 0.15f), default, 0.01f);
            B("GunsightGlass", new Vector3(0, 0.8f, 0.6f), new Vector3(0.12f, 0.1f, 0.01f), new Color(0.55f, 0.7f, 0.65f), new Vector3(-20, 0, 0), 0.005f, 0.95f, 0f);

            // Склейка: один меш на фюзеляж, винт, пилота и каждую ракету (ракеты исчезают по одной)
            var groups = new HashSet<Transform> { root.transform, rig.prop, rig.pilot };
            foreach (var rk in rig.rockets) if (rk) groups.Add(rk);
            foreach (var gr in groups) if (gr != root.transform) MeshMerge.Merge(gr, groups);
            MeshMerge.Merge(root.transform, groups);
            return rig;
        }

        /// <summary>Германия — прямой крест с белой каймой; СССР — красная звезда (лучи) с белой окантовкой.</summary>
        static void Insignia(Vector3 at, bool de, bool horizontal)
        {
            var g = new GameObject("Insignia").transform; g.SetParent(_p, false); g.localPosition = at;
            g.localRotation = horizontal ? Quaternion.identity : Quaternion.Euler(0, 0, 90);
            var keep = _p; _p = g;
            if (de)
            {
                B("CrossW1", Vector3.zero, new Vector3(0.75f, 0.02f, 0.26f), Color.white, default, 0.005f, 0.2f, 0f);
                B("CrossW2", Vector3.zero, new Vector3(0.26f, 0.02f, 0.75f), Color.white, default, 0.005f, 0.2f, 0f);
                B("CrossB1", new Vector3(0, 0.006f, 0), new Vector3(0.62f, 0.02f, 0.13f), Color.black, default, 0.005f, 0.2f, 0f);
                B("CrossB2", new Vector3(0, 0.006f, 0), new Vector3(0.13f, 0.02f, 0.62f), Color.black, default, 0.005f, 0.2f, 0f);
            }
            else
            {
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 72f;
                    Vector3 d = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    B("StarW", d * 0.16f, new Vector3(0.17f, 0.02f, 0.36f), Color.white, new Vector3(0, a, 0), 0.005f, 0.2f, 0f);
                    B("StarR", d * 0.16f + Vector3.up * 0.006f, new Vector3(0.11f, 0.02f, 0.3f), new Color(0.78f, 0.1f, 0.08f), new Vector3(0, a, 0), 0.005f, 0.2f, 0f);
                }
                B("StarC", Vector3.up * 0.006f, new Vector3(0.2f, 0.02f, 0.2f), new Color(0.78f, 0.1f, 0.08f), new Vector3(0, 36f, 0), 0.005f, 0.2f, 0f);
            }
            _p = keep;
        }

        /// <summary>Рендереры, видимые только вблизи (мелочь: знаки, выхлопы, балки, ракеты, пилот).</summary>
        public static void SetupLod(GameObject root)
        {
            var all = root.GetComponentsInChildren<Renderer>(true);
            var big = new List<Renderer>();
            foreach (var r in all) if (r.bounds.size.magnitude > 1.2f) big.Add(r);
            if (big.Count == 0 || big.Count == all.Length) return;
            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(0.05f, all), new LOD(0.0004f, big.ToArray()) });
            if (Application.isMobilePlatform)
                foreach (var r in all) if (!big.Contains(r)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lg.fadeMode = LODFadeMode.None;
            lg.RecalculateBounds();
        }
    }
}
