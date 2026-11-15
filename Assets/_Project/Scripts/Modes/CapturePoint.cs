using System;
using Frontline.Bots;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Frontline
{
    /// <summary>Точка захвата на сцене (A–E). Визуал — флаг, цвет меняется по владельцу.</summary>
    public class CapturePoint : MonoBehaviour
    {
        public char letter = 'A';
        public float radius = 12f;
        public Renderer flagRenderer;
        public Color neutral = Color.white, germany = new(0.55f, 0.55f, 0.6f), ussr = new(0.75f, 0.15f, 0.12f);
        public Transform flag; public float flagLow = 1f, flagHigh = 7f;

        MaterialPropertyBlock _mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public void Visualize(in CapturePointData d)
        {
            _mpb ??= new MaterialPropertyBlock();
            Color c = d.owner == 0 ? germany : d.owner == 1 ? ussr : neutral;
            if (flagRenderer) { _mpb.SetColor(BaseColor, c); flagRenderer.SetPropertyBlock(_mpb); }
            if (flag) flag.localPosition = new Vector3(0, Mathf.Lerp(flagLow, flagHigh, Mathf.Abs(d.progress)), 0);
        }

        void OnDrawGizmos() { Gizmos.color = new Color(1, 1, 0, 0.4f); Gizmos.DrawWireSphere(transform.position, radius); }
    }
}
