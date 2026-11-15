using UnityEngine;

namespace Frontline
{
    /// <summary>Облака медленно плывут по ветру и «перематываются» на другой край карты.</summary>
    public class CloudDrift : MonoBehaviour
    {
        public Vector3 wind = new(6f, 0, 2f);
        public float limit = 5200f;

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (Transform c in transform)
            {
                var p = c.position + wind * dt;
                if (p.x > limit) p.x -= limit * 2; else if (p.x < -limit) p.x += limit * 2;
                if (p.z > limit) p.z -= limit * 2; else if (p.z < -limit) p.z += limit * 2;
                c.position = p;
            }
        }
    }
}
