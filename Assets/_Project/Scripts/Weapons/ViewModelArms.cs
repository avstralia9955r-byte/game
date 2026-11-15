using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>
    /// Руки от первого лица целиком: плечо → локоть → запястье решаются двухзвенной IK от «плеч» (точек относительно
    /// камеры) к точкам WristPoint на кистях модели оружия. Кисти двигает ViewModelAnimator (перезарядка, затвор,
    /// осмотр) — предплечья и плечи послушно следуют, поэтому оружие всегда в руках, а не «висит» перед камерой.
    /// Рукава — ткань формы своей стороны (зимой — маскхалат), со складками и налокотником.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class ViewModelArms : MonoBehaviour
    {
        public WeaponVisualAssembler assembler;
        public Transform knifeModel, trowelModel;
        public Faction faction;
        public MapTheme theme;
        public int layer = 31;

        public Vector3 shoulderR = new(0.2f, -0.33f, -0.07f), shoulderL = new(-0.19f, -0.33f, 0.03f);
        public float upperLen = 0.29f, foreLen = 0.27f;

        Transform _root, _upR, _foreR, _upL, _foreL, _elbowR, _elbowL;
        Transform _handR, _handL, _wristR, _wristL;
        GameObject _src;
        bool _built;

        public void SetFaction(Faction f, MapTheme t)
        {
            if (_built && f == faction && t == theme) return;
            faction = f; theme = t;
            if (_root) Destroy(_root.gameObject);
            _built = false;
        }

        void Build()
        {
            _built = true;
            _root = new GameObject("Arms").transform;
            _root.SetParent(transform, false);
            Color sl = theme == MapTheme.Winter ? ProceduralModels.SleeveWinter : faction == Faction.Germany ? ProceduralModels.SleeveDe : ProceduralModels.SleeveSu;
            _upR = Segment("UpperArmR", 0.115f, 0.092f, upperLen, sl, true);
            _foreR = Segment("ForearmR", 0.094f, 0.074f, foreLen, sl, false);
            _upL = Segment("UpperArmL", 0.115f, 0.092f, upperLen, sl, true);
            _foreL = Segment("ForearmL", 0.094f, 0.074f, foreLen, sl, false);
            _elbowR = Elbow("ElbowR", sl); _elbowL = Elbow("ElbowL", sl);
            foreach (var t in _root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in _root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        Transform Segment(string name, float d0, float d1, float len, Color c, bool upper)
        {
            var root = new GameObject(name).transform; root.SetParent(_root, false);
            var mat = ProceduralModels.DetailMat(c, DetailKind.Fabric, 0.12f, 0f);
            void Piece(string n, Mesh mesh, float z, Vector3 scale)
            {
                var go = new GameObject(n);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(0, 0, z);
                go.transform.localEulerAngles = new Vector3(90, 0, 0);
                go.transform.localScale = scale;
                go.AddComponent<MeshFilter>().sharedMesh = MeshDetail.Textured(mesh, scale, ProceduralModels.DetailTile(DetailKind.Fabric));
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            Piece("Sleeve", MeshUtil.Frustum(14, d1 / d0), len * 0.5f, new Vector3(d0, len, d0));
            // Складки ткани — слегка утолщённые кольца
            for (int i = 0; i < (upper ? 2 : 3); i++)
            {
                float t = upper ? 0.35f + i * 0.3f : 0.2f + i * 0.22f;
                float d = Mathf.Lerp(d0, d1, t) * 1.07f;
                Piece("Fold" + i, MeshUtil.Cylinder(14), len * t, new Vector3(d, 0.018f, d * 0.96f));
            }
            if (!upper)   // подвёрнутый обшлаг у запястья
                Piece("Turnup", MeshUtil.Cylinder(14), len * 0.93f, new Vector3(d1 * 1.12f, 0.035f, d1 * 1.12f));
            return root;
        }

        Transform Elbow(string name, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var s = Vector3.one * 0.098f;
            go.transform.localScale = s;
            go.AddComponent<MeshFilter>().sharedMesh = MeshDetail.Textured(MeshUtil.Ico(), s, ProceduralModels.DetailTile(DetailKind.Fabric));
            go.AddComponent<MeshRenderer>().sharedMaterial = ProceduralModels.DetailMat(c * 0.92f, DetailKind.Fabric, 0.12f, 0f);
            return go.transform;
        }

        GameObject ActiveSource()
        {
            if (assembler && assembler.Root && assembler.Root.activeInHierarchy) return assembler.Root;
            if (knifeModel && knifeModel.gameObject.activeInHierarchy) return knifeModel.gameObject;
            if (trowelModel && trowelModel.gameObject.activeInHierarchy) return trowelModel.gameObject;
            return null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root) { var r = FindDeep(c, name); if (r) return r; }
            return null;
        }

        void LateUpdate()
        {
            if (!_built) Build();
            var src = ActiveSource();
            if (src != _src)
            {
                _src = src;
                _handR = src ? FindDeep(src.transform, "HandR") : null;
                _handL = src ? FindDeep(src.transform, "HandL") : null;
                _wristR = _handR ? FindDeep(_handR, "WristPoint") : null;
                _wristL = _handL ? FindDeep(_handL, "WristPoint") : null;
            }
            Solve(_handR, _wristR, shoulderR, new Vector3(0.9f, -1f, -0.35f), _upR, _foreR, _elbowR);
            Solve(_handL, _wristL, shoulderL, new Vector3(-0.9f, -1f, -0.1f), _upL, _foreL, _elbowL);
        }

        void Solve(Transform hand, Transform wristPt, Vector3 shoulderLocal, Vector3 poleLocal, Transform up, Transform fore, Transform elbowT)
        {
            bool on = hand && hand.gameObject.activeInHierarchy;
            if (up.gameObject.activeSelf != on) { up.gameObject.SetActive(on); fore.gameObject.SetActive(on); elbowT.gameObject.SetActive(on); }
            if (!on) return;

            var cam = transform;
            Vector3 s = cam.TransformPoint(shoulderLocal);
            Vector3 w = wristPt ? wristPt.position : hand.position - hand.forward * 0.1f;
            Vector3 pole = cam.TransformDirection(poleLocal).normalized;

            Vector3 d = w - s; float dist = d.magnitude;
            if (dist < 1e-4f) return;
            Vector3 dir = d / dist;
            float a = upperLen, b = foreLen;
            float reach = Mathf.Clamp(dist, Mathf.Abs(a - b) + 0.01f, a + b - 0.001f);
            float cosA = Mathf.Clamp((a * a + reach * reach - b * b) / (2 * a * reach), -1f, 1f);
            float sinA = Mathf.Sqrt(1 - cosA * cosA);
            Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
            if (perp.sqrMagnitude < 1e-6f) perp = cam.TransformDirection(Vector3.down) - dir * Vector3.Dot(cam.TransformDirection(Vector3.down), dir);
            perp.Normalize();
            Vector3 elbow = s + (dir * cosA + perp * sinA) * a;

            Place(up, s, elbow, cam.up);
            Place(fore, elbow, w, cam.up);
            elbowT.position = elbow;
            elbowT.rotation = up.rotation;
        }

        void Place(Transform seg, Vector3 from, Vector3 to, Vector3 upHint)
        {
            Vector3 d = to - from; float len = d.magnitude;
            if (len < 1e-4f) return;
            float nominal = seg == _upR || seg == _upL ? upperLen : foreLen;
            seg.SetPositionAndRotation(from, Quaternion.LookRotation(d / len, upHint));
            seg.localScale = new Vector3(1, 1, len / nominal);
        }
    }
}
