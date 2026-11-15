using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Ящик с боеприпасами (как в Polyfield): у баз и на каждой точке. E — пополнить запас патронов всех слотов,
    /// гранаты и противотанковое. После использования ящик «пустеет» на 20 с (для этого игрока).
    /// </summary>
    public class AmmoCrate : MonoBehaviour
    {
        public static readonly List<AmmoCrate> All = new();
        public float cooldown = 20f;
        float _readyAt;
        Transform _lid;
        float _lidT;

        public bool Ready => Time.time >= _readyAt;
        public float Cooldown => Mathf.Max(0, _readyAt - Time.time);

        public static AmmoCrate Nearest(Vector3 p, float maxDist)
        {
            AmmoCrate best = null; float bd = maxDist * maxDist;
            foreach (var c in All)
            {
                if (!c) continue;
                float d = (c.transform.position + Vector3.up * 0.4f - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        public static AmmoCrate Create(Transform parent, Vector3 pos, float yaw, MapTheme theme, int layer)
        {
            var root = new GameObject("AmmoCrate");
            root.transform.SetParent(parent);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var t = root.transform;
            Color wood = theme == MapTheme.Winter ? new Color(0.5f, 0.45f, 0.36f) : new Color(0.36f, 0.33f, 0.2f);
            Color band = new(0.2f, 0.2f, 0.2f), stencil = new(0.85f, 0.75f, 0.25f), can = new(0.27f, 0.3f, 0.2f);
            ProceduralModels.Part(t, "Body", MeshUtil.Bevel(0.04f), new Vector3(0, 0.3f, 0), new Vector3(1.2f, 0.55f, 0.62f), wood);
            for (int i = -1; i <= 1; i += 2)
            {
                ProceduralModels.Box(t, "Band", new Vector3(i * 0.42f, 0.3f, 0), new Vector3(0.05f, 0.57f, 0.64f), band, false, default, 0.4f, 0.7f);
                ProceduralModels.Part(t, "Handle", MeshUtil.Cylinder(6), new Vector3(i * 0.62f, 0.38f, 0), new Vector3(0.04f, 0.18f, 0.04f), new Color(0.5f, 0.44f, 0.3f), new Vector3(90, 0, 0));
            }
            ProceduralModels.Box(t, "Stencil", new Vector3(0, 0.33f, 0.315f), new Vector3(0.5f, 0.08f, 0.01f), stencil);
            ProceduralModels.Box(t, "Stencil2", new Vector3(0, 0.2f, 0.315f), new Vector3(0.3f, 0.05f, 0.01f), stencil);
            var lid = new GameObject("LidPivot").transform; lid.SetParent(t, false); lid.localPosition = new Vector3(0, 0.58f, -0.31f);
            ProceduralModels.Part(lid, "Lid", MeshUtil.Bevel(0.04f), new Vector3(0, 0.03f, 0.31f), new Vector3(1.22f, 0.06f, 0.64f), wood * 0.92f);
            // Цинки и подсумки рядом
            ProceduralModels.Part(t, "Can1", MeshUtil.Bevel(0.05f), new Vector3(0.95f, 0.15f, 0.15f), new Vector3(0.3f, 0.3f, 0.2f), can, new Vector3(0, 12, 0), false, 0.4f, 0.5f);
            ProceduralModels.Part(t, "Can2", MeshUtil.Bevel(0.05f), new Vector3(0.9f, 0.15f, -0.2f), new Vector3(0.3f, 0.3f, 0.2f), can, new Vector3(0, -8, 0), false, 0.4f, 0.5f);
            ProceduralModels.Part(t, "Can3", MeshUtil.Bevel(0.05f), new Vector3(0.93f, 0.44f, -0.02f), new Vector3(0.3f, 0.28f, 0.2f), can, new Vector3(0, 40, 0), false, 0.4f, 0.5f);
            var col = root.AddComponent<BoxCollider>(); col.center = new Vector3(0.15f, 0.3f, 0); col.size = new Vector3(1.55f, 0.6f, 0.66f);
            root.layer = layer;
            var crate = root.AddComponent<AmmoCrate>();
            crate._lid = lid;
            All.Add(crate);
            return crate;
        }

        void OnDestroy() => All.Remove(this);

        public void Use(PlayerController p)
        {
            bool any = false;
            var wc = p.GetComponent<WeaponController>(); if (wc) any |= wc.RefillAll();
            var gt = p.GetComponent<GrenadeThrower>(); if (gt && gt.Count < gt.Max) { gt.RefillToMax(); any = true; }
            var ft = p.GetComponent<FieldTools>(); if (ft) { ft.Refill(); any = true; }      // аптечки и мешки с песком
            var src = p.GetComponent<AudioSource>(); if (src) src.PlayOneShot(SoundSynth.AmmoPickup(), 0.9f);
            _readyAt = Time.time + (any ? cooldown : 2f);
            _lidT = 1f;
        }

        void Update()
        {
            if (_lidT <= 0 || !_lid) return;
            _lidT = Mathf.MoveTowards(_lidT, 0, Time.deltaTime * 0.8f);
            _lid.localRotation = Quaternion.Euler(-Mathf.Sin(_lidT * Mathf.PI) * 70f, 0, 0);
        }
    }
}
