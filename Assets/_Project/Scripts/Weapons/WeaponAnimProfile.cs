using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Описание модели оружия для процедурной анимации: как извлекается магазин, где затвор, куда летят гильзы.
    /// Аниматор ищет части по именам: Mag_Standard, Socket_Mag, BoltHandle, Slide, TopCover, Cylinder, Hammer,
    /// ChargingHandle, HandL, HandR.
    /// </summary>
    public class WeaponAnimProfile : MonoBehaviour
    {
        public enum Style { Magazine, PanTop, SideMag, Belt, Clip, Revolver, Launcher }

        public Style style = Style.Magazine;
        [Tooltip("Смещение магазина при извлечении (локально).")]
        public Vector3 magOut = new(0, -0.32f, 0.03f);
        [Tooltip("Ход затвора/рукоятки взведения назад, м.")]
        public float boltTravel = 0.06f;
        [Tooltip("Рукоять затвора поворачивается вверх перед отводом (болтовки).")]
        public float boltLift = 70f;
        public Vector3 ejectDir = new(1f, 0.6f, -0.1f);
        public float casingLength = 0.03f, casingRadius = 0.006f;
        [Tooltip("Куда тянется левая рука при перезарядке, если не к магазину (обойма сверху).")]
        public Vector3 clipHandPos = new(0.02f, 0.11f, -0.02f);
        public bool ejectsCasings = true;
        [Tooltip("Показывать обойму в левой руке при перезарядке (Clip). У однозарядных — нет.")]
        public bool showClip = true;
    }
}
