using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{

    /// <summary>
    /// Собирает внешний вид оружия из базовой модели и обвесов. Используется и в бою (вид от первого лица),
    /// и в меню кастомизации (предпросмотр), поэтому изменения видны одинаково.
    /// </summary>
    public class WeaponVisualAssembler : MonoBehaviour
    {
        public WeaponSockets Sockets { get; private set; }
        public GameObject Root { get; private set; }

        readonly List<GameObject> _spawned = new();
        readonly List<GameObject> _hidden = new();
        readonly List<Renderer> _hiddenR = new();
        Vector3 _opticAnchorPos; bool _opticMoved;
        Vector3 _flashLocalPos;
        GameObject _incubator;

        /// <summary>Сводка состояния модели для диагностики (F3).</summary>
        public string Describe(Camera cam)
        {
            if (!Root) return "оружие: нет модели (Root = null)";
            var rs = Root.GetComponentsInChildren<Renderer>(true);
            int on = 0, vis = 0; Bounds b = default; bool first = true;
            foreach (var r in rs)
            {
                if (r.enabled && r.gameObject.activeInHierarchy) on++;
                if (r.isVisible) vis++;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            string pos = cam ? cam.transform.InverseTransformPoint(Root.transform.position).ToString("F2") : "?";
            string ctr = cam && !first ? cam.transform.InverseTransformPoint(b.center).ToString("F2") : "?";
            bool layerOn = cam && (cam.cullingMask & (1 << Root.layer)) != 0;
            return $"оружие: {Root.name} active={Root.activeInHierarchy} слой={Root.layer} (камера рисует: {layerOn})\n" +
                   $"рендеров {rs.Length}, включено {on}, видимо {vis}; центр в осях камеры {ctr}, корень {pos}, " +
                   $"масштаб {Root.transform.lossyScale.x:F2}; near={(cam ? cam.nearClipPlane : 0):F3}";
        }

        public GameObject Build(WeaponDefinition w, IReadOnlyList<AttachmentDefinition> atts, bool viewModel, int layer)
        {
            Clear(true);
            if (!w) return null;
            var prefab = viewModel ? w.viewModelPrefab : (w.worldModelPrefab ? w.worldModelPrefab : w.viewModelPrefab);
            if (!prefab) { Debug.LogWarning($"[Weapon] Нет модели для {w.id}"); return null; }

            // Копия собирается в неактивном «инкубаторе»: слой, тени и обвесы настраиваются ДО того, как рендеры
            // зарегистрируются в конвейере (GPU Resident Drawer / SRP Batcher кэшируют рендер при включении),
            // и только потом модель переносится под камеру и включается.
            if (!_incubator)
            {
                _incubator = new GameObject("VM_Incubator");
                _incubator.SetActive(false);
                _incubator.transform.SetParent(transform, false);
            }
            Root = Instantiate(prefab, _incubator.transform, false);
            Root.name = prefab.name;
            Root.transform.localPosition = Vector3.zero; Root.transform.localRotation = Quaternion.identity; Root.transform.localScale = Vector3.one;
            Sockets = Root.GetComponent<WeaponSockets>();
            if (Sockets && Sockets.muzzleFlashPoint) _flashLocalPos = Sockets.muzzleFlashPoint.localPosition;
            Apply(atts);
            SetLayerRecursive(Root, layer);
            foreach (var r in Root.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
                r.forceRenderingOff = false;
                r.allowOcclusionWhenDynamic = false;   // у камеры — никогда не отсекается окклюзией
                if (viewModel)   // руки и оружие от первого лица тени не отбрасывают (иначе «висящая» тень у камеры)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = true;
                }
            }
            Root.SetActive(true);
            Root.transform.SetParent(transform, false);
            Root.transform.localPosition = Vector3.zero; Root.transform.localRotation = Quaternion.identity; Root.transform.localScale = Vector3.one;
            return Root;
        }

        /// <summary>Перестроить только обвесы (быстро — для переключения «до/после» в превью).</summary>
        public void Apply(IReadOnlyList<AttachmentDefinition> atts)
        {
            Clear(false);
            if (!Root || !Sockets || atts == null) return;

            foreach (var a in atts)
            {
                if (!a) continue;
                bool magSlot = a.slot == AttachmentSlot.Magazine;
                var baseMag = magSlot ? FindDeep(Root.transform, "Mag_Standard") : null;
                if (a.hideBaseParts != null)
                    foreach (var partName in a.hideBaseParts)
                    {
                        if (magSlot && partName == "Mag_Standard") continue;    // магазин прячем по рендерам (ниже)
                        if (partName.EndsWith("*")) { HidePrefix(Root.transform, partName.Substring(0, partName.Length - 1)); continue; }
                        var part = FindDeep(Root.transform, partName);
                        if (part && part.gameObject.activeSelf) { part.gameObject.SetActive(false); _hidden.Add(part.gameObject); }
                    }

                var socket = Sockets.Get(a.slot);
                if (!socket || !a.visualPrefab) continue;
                GameObject go;
                if (magSlot && baseMag)
                {
                    // Увеличенный/барабанный магазин крепится к самому магазину оружия: верх нового магазина — там же,
                    // где верх штатного, поворот тот же, и при перезарядке он уходит и возвращается вместе с ним.
                    // Штатный магазин бывает просто масштабированным кубом — детям нельзя наследовать его масштаб,
                    // поэтому новый магазин кладём рядом (тот же родитель) и он повторяет движения штатного (MagFollower).
                    var space = baseMag.parent ? baseMag.parent : Root.transform;
                    var magBounds = LocalBounds(baseMag, space);
                    foreach (var r in baseMag.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; _hiddenR.Add(r); }
                    go = Instantiate(a.visualPrefab, space, false);
                    go.transform.localRotation = Quaternion.identity;
                    var attBounds = LocalBounds(go.transform, go.transform);
                    go.transform.localPosition = new Vector3(magBounds.center.x - attBounds.center.x, magBounds.max.y - attBounds.max.y,
                                                             magBounds.center.z - attBounds.center.z);
                    go.AddComponent<MagFollower>().Init(baseMag);
                }
                else go = Instantiate(a.visualPrefab, socket, false);
                go.layer = Root.layer;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Root.layer;
                _spawned.Add(go);

                // Оптика: точка прицеливания — точно на оси трубы прицела (у разных прицелов разная высота)
                if (a.slot == AttachmentSlot.Optic && Sockets.adsAnchorOptic)
                {
                    var tube = FindDeep(go.transform, "Tube");
                    if (tube)
                    {
                        if (!_opticMoved) { _opticAnchorPos = Sockets.adsAnchorOptic.localPosition; _opticMoved = true; }
                        var lp = _opticAnchorPos;
                        lp.y = Sockets.adsAnchorOptic.parent.InverseTransformPoint(tube.position).y;
                        Sockets.adsAnchorOptic.localPosition = lp;
                    }
                }

                // Глушитель удлиняет ствол — переносим точку вспышки на его конец.
                if (a.slot == AttachmentSlot.Muzzle && Sockets.muzzleFlashPoint)
                {
                    var tip = FindDeep(go.transform, "Tip");
                    if (tip) Sockets.muzzleFlashPoint.position = tip.position;
                }
            }
        }

        public void Clear() => Clear(true);

        void Clear(bool destroyRoot)
        {
            foreach (var g in _spawned) if (g) Destroy(g);
            _spawned.Clear();
            foreach (var g in _hidden) if (g) g.SetActive(true);
            _hidden.Clear();
            foreach (var r in _hiddenR) if (r) r.enabled = true;
            _hiddenR.Clear();
            if (Sockets && Sockets.muzzleFlashPoint) Sockets.muzzleFlashPoint.localPosition = _flashLocalPos;
            if (_opticMoved && Sockets && Sockets.adsAnchorOptic) Sockets.adsAnchorOptic.localPosition = _opticAnchorPos;
            _opticMoved = false;
            if (destroyRoot && Root) { if (FxPool.I) FxPool.I.Detach(Root.transform); Destroy(Root); Root = null; Sockets = null; }
        }

        public Transform AdsAnchor(bool hasOptic) =>
            Sockets ? (hasOptic && Sockets.adsAnchorOptic ? Sockets.adsAnchorOptic : Sockets.adsAnchor) : null;

        void HidePrefix(Transform t, string prefix)
        {
            foreach (Transform c in t)
            {
                if (c.name.StartsWith(prefix) && c.gameObject.activeSelf) { c.gameObject.SetActive(false); _hidden.Add(c.gameObject); continue; }
                HidePrefix(c, prefix);
            }
        }

        /// <summary>Габариты мешей поддерева в локальных осях space.</summary>
        static Bounds LocalBounds(Transform root, Transform space)
        {
            bool any = false; Bounds b = new(Vector3.zero, Vector3.zero);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds;
                var m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root) { var r = FindDeep(c, name); if (r) return r; }
            return null;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }

    /// <summary>Увеличенный магазин повторяет движение штатного при перезарядке (выход/вход, видимость).</summary>
    public class MagFollower : MonoBehaviour
    {
        Transform _src; Vector3 _srcPos, _myPos; Quaternion _srcRot, _myRot;

        public void Init(Transform src)
        {
            _src = src; _srcPos = src.localPosition; _srcRot = src.localRotation;
            _myPos = transform.localPosition; _myRot = transform.localRotation;
        }

        void LateUpdate()
        {
            if (!_src) return;
            var dq = _src.localRotation * Quaternion.Inverse(_srcRot);
            transform.localPosition = _src.localPosition + dq * (_myPos - _srcPos);
            transform.localRotation = dq * _myRot;
            bool on = _src.gameObject.activeSelf;
            foreach (Transform c in transform) if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }
    }
}
