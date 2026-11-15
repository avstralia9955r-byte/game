using UnityEngine;

namespace Frontline
{
    /// <summary>Равномерное вращение (крылья мельницы).</summary>
    public class Spinner : MonoBehaviour
    {
        public Vector3 degreesPerSecond = new(0, 0, 15);
        void Update() => transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
