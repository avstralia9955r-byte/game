using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>Точки крепления на модели оружия. Ставится на корень viewModel/worldModel префаба.</summary>
    public class WeaponSockets : MonoBehaviour
    {
        public Transform muzzle, magazine, optic, underbarrel, laser;
        public Transform muzzleFlashPoint, shellEjectPoint, adsAnchor;
        [Tooltip("Точка прицеливания при установленной оптике (вместо механического прицела).")]
        public Transform adsAnchorOptic;

        public Transform Get(AttachmentSlot s) => s switch
        {
            AttachmentSlot.Muzzle => muzzle,
            AttachmentSlot.Magazine => magazine,
            AttachmentSlot.Optic => optic,
            AttachmentSlot.Underbarrel => underbarrel,
            AttachmentSlot.Laser => laser,
            _ => null
        };
    }
}
