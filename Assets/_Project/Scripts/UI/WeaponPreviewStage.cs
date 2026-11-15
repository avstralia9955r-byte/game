using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Frontline
{
    /// <summary>
    /// Сцена осмотра оружия: отдельная камера рендерит модель в RenderTexture, оружие вращается перетаскиванием
    /// (мышь/палец), колесо/щипок — зум. Использует тот же WeaponVisualAssembler, что и бой, — вид совпадает 1:1.
    /// </summary>
    public class WeaponPreviewStage : MonoBehaviour, IDragHandler, IScrollHandler
    {
        public WeaponVisualAssembler assembler;
        public Transform turntable;
        public Camera previewCamera;
        public float rotateSpeed = 0.35f, autoSpin = 12f, minDist = 0.6f, maxDist = 2.2f;
        public int previewLayer = 10;

        Vector2 _rot = new(10, -30);
        float _dist = 1.3f, _idle;

        public void Show(WeaponDefinition w, IReadOnlyList<AttachmentDefinition> atts)
        {
            assembler.Build(w, atts, false, previewLayer);
            if (assembler.Root)
            {
                // Центрируем по габаритам, чтобы любое оружие вращалось вокруг середины.
                var r = assembler.Root.GetComponentsInChildren<Renderer>();
                if (r.Length > 0)
                {
                    var b = r[0].bounds; foreach (var x in r) b.Encapsulate(x.bounds);
                    assembler.Root.transform.position -= b.center - turntable.position;
                    _dist = Mathf.Clamp(b.extents.magnitude * 2.2f, minDist, maxDist);
                }
            }
        }

        public void ApplyAttachments(IReadOnlyList<AttachmentDefinition> atts) => assembler.Apply(atts);

        public void OnDrag(PointerEventData e) { _rot += new Vector2(-e.delta.y, e.delta.x) * rotateSpeed; _idle = 0; }
        public void OnScroll(PointerEventData e) { _dist = Mathf.Clamp(_dist - e.scrollDelta.y * 0.1f, minDist, maxDist); }

        void Update()
        {
            _idle += Time.deltaTime;
            if (_idle > 3f) _rot.y += autoSpin * Time.deltaTime;  // авто-вращение, если не трогают
            _rot.x = Mathf.Clamp(_rot.x, -60, 60);
            turntable.rotation = Quaternion.Euler(_rot.x, _rot.y, 0);
            if (previewCamera) previewCamera.transform.localPosition = new Vector3(0, 0, -_dist);

            // Щипок двумя пальцами на мобильных (только новый Input System — legacy Input отключён).
            var ts = UnityEngine.InputSystem.Touchscreen.current;
            if (ts != null && ts.touches[0].isInProgress && ts.touches[1].isInProgress)
            {
                Vector2 pa = ts.touches[0].position.ReadValue(), pb = ts.touches[1].position.ReadValue();
                Vector2 da = ts.touches[0].delta.ReadValue(), db = ts.touches[1].delta.ReadValue();
                float prev = ((pa - da) - (pb - db)).magnitude;
                float cur = (pa - pb).magnitude;
                _dist = Mathf.Clamp(_dist - (cur - prev) * 0.002f, minDist, maxDist);
            }
        }
    }
}
