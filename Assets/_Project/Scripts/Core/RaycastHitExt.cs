using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Было ли попадание у результата RaycastCommand. Не зависит от colliderInstanceID / colliderEntityId
    /// (API меняется между версиями Unity): у пустого результата нормаль нулевая, у реального попадания — нет.
    /// </summary>
    public static class RaycastHitExt
    {
        public static bool HasHit(this RaycastHit h) => h.normal.sqrMagnitude > 0.5f;
    }
}
