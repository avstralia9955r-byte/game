using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Доводка собранной модели оружия (вызывается в конце ProceduralModels.Weapon):
    /// • дульный сокет и точка вспышки ставятся на настоящий торец ствола / дульного устройства — глушитель,
    ///   пламегаситель и вспышка больше не висят в воздухе и не утоплены в ствол;
    /// • целик выравнивается по высоте мушки, а точка прицеливания (ADS) — ровно на линию «целик — мушка».
    /// </summary>
    public static partial class ProceduralModels
    {
        static readonly string[] NotBarrel = { "Bayonet", "Point", "IronSights", "Hood", "FrontSight", "Sling", "Bipod", "Foregrip", "Clean", "Grip", "Hand", "Stock" };

        static bool Skip(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
            {
                if (p.name == "HandR" || p.name == "HandL") return true;
                foreach (var n in NotBarrel) if (p.name.StartsWith(n)) return true;
            }
            return false;
        }

        static Bounds RootBounds(MeshFilter mf, Transform root)
        {
            var mb = mf.sharedMesh.bounds;
            var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds b = new(m.MultiplyPoint3x4(mb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return b;
        }

        static void FinishWeapon(GameObject root, WeaponCategory cat)
        {
            var s = root.GetComponent<WeaponSockets>();
            if (!s || cat is WeaponCategory.Knife or WeaponCategory.Launcher || !s.muzzle) return;
            var rt = root.transform;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);

            // ---- 1. Торец ствола ----
            float bore = s.muzzle.localPosition.y, tip = float.MinValue;
            foreach (var mf in filters)
            {
                if (!mf.sharedMesh || Skip(mf.transform, rt)) continue;
                var b = RootBounds(mf, rt);
                if (Mathf.Abs(b.center.y - bore) < 0.02f && Mathf.Abs(b.center.x) < 0.02f && b.size.y < 0.075f && b.size.x < 0.075f)
                    tip = Mathf.Max(tip, b.max.z);
            }
            // ---- 1а. Ствол от ствольной коробки: у СВТ/АВТ, G43, автомата Фёдорова ствол начинался посреди цевья,
            // и между коробкой и стволом сбоку был виден разрыв. Если на оси канала есть пустота — ствол дотягивается.
            float barrelStart = float.MaxValue, barrelR = 0.0095f;
            foreach (var mf in filters)
                if (mf.sharedMesh && mf.name == "Barrel")
                {
                    var b = RootBounds(mf, rt);
                    if (b.min.z < barrelStart) { barrelStart = b.min.z; barrelR = Mathf.Clamp(Mathf.Min(b.size.x, b.size.y) * 0.5f, 0.006f, 0.016f); }
                }
            if (barrelStart < float.MaxValue)
            {
                float behind = float.MinValue;
                foreach (var mf in filters)
                {
                    if (!mf.sharedMesh || mf.name == "Barrel" || Skip(mf.transform, rt)) continue;
                    var b = RootBounds(mf, rt);
                    if (Mathf.Abs(b.center.y - bore) > 0.022f || Mathf.Abs(b.center.x) > 0.02f || b.size.y > 0.09f) continue;
                    if (b.max.z < barrelStart + 0.004f && b.min.z < barrelStart) behind = Mathf.Max(behind, b.max.z);
                }
                float gap = barrelStart - behind;
                if (behind > float.MinValue && gap > 0.006f && gap < 0.5f)
                {
                    var prevT = _t; _t = rt;
                    cz("BarrelRoot", 0, bore, (behind - 0.004f + barrelStart + 0.004f) * 0.5f, barrelR, gap + 0.008f, Blued, 8);
                    _t = prevT;
                }
            }

            if (tip > 0.05f && Mathf.Abs(tip - s.muzzle.localPosition.z) < 0.3f)
            {
                s.muzzle.localPosition = new Vector3(0, bore, tip);
                if (s.muzzleFlashPoint) s.muzzleFlashPoint.localPosition = new Vector3(0, bore, tip + 0.015f);
            }

            // ---- 2. Прицельные приспособления ----
            float frontTop = float.MinValue, frontZ = float.MinValue;
            foreach (var mf in filters)
            {
                if (!mf.sharedMesh || mf.name != "IronSights") continue;
                var b = RootBounds(mf, rt);
                if (b.center.z > frontZ) { frontZ = b.center.z; frontTop = b.max.y; }
            }
            if (frontTop == float.MinValue || !s.adsAnchor) return;
            float rearTop = float.MinValue; bool notch = false;
            foreach (var mf in filters)
            {
                if (!mf.sharedMesh) continue;
                bool isNotch = mf.name.StartsWith("RearNotch"), isRear = isNotch || mf.name.StartsWith("RearSight");
                if (!isRear) continue;
                var b = RootBounds(mf, rt);
                if (b.center.z > frontZ) continue;
                if (isNotch && !notch) { notch = true; rearTop = float.MinValue; }
                if (isNotch || !notch) rearTop = Mathf.Max(rearTop, b.max.y);
            }
            if (rearTop > float.MinValue)
            {
                float d = frontTop - rearTop;
                if (Mathf.Abs(d) > 0.001f && Mathf.Abs(d) < 0.04f)
                    foreach (var mf in filters)
                        if (mf.name.StartsWith("RearNotch") || mf.name.StartsWith("RearSight"))
                        {
                            var t = mf.transform;
                            Vector3 wUp = rt.TransformVector(Vector3.up * d);
                            t.position += wUp;
                        }
            }
            var ap = s.adsAnchor.localPosition;
            s.adsAnchor.localPosition = new Vector3(0, frontTop, ap.z);
        }
    }
}
