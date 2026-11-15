using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Обвес: визуальная деталь + модификаторы. Все множители — относительно базы оружия (1 = без изменений).
    /// </summary>
    [CreateAssetMenu(menuName = "Frontline/Attachment", fileName = "ATT_")]
    public class AttachmentDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public AttachmentType type;
        public AttachmentSlot slot;

        [Header("Разблокировка (локально)")]
        public int price = 500;
        public string requiredAchievementId;   // пусто = только за валюту

        [Header("Визуал")]
        public GameObject visualPrefab;        // крепится к сокету слота
        [Tooltip("Имена дочерних объектов модели оружия, скрываемых при установке (напр. 'Mag_Standard', 'IronSights').")]
        public string[] hideBaseParts;

        [Header("Модификаторы")]
        public float loudnessMultiplier = 1f;      // глушитель ≈ 0.08, дульный тормоз ≈ 1.4
        public float verticalRecoilMultiplier = 1f;
        public float horizontalRecoilMultiplier = 1f;
        public float hipSpreadMultiplier = 1f;
        public float magazineMultiplier = 1f;      // улучшенный магазин = 1.5
        public float adsTimeMultiplier = 1f;
        public float adsFovOverride = 0f;          // > 0 — оптика задаёт свой FOV (зум)
        public float damageRangeMultiplier = 1f;
        public bool suppressesMuzzleFlash;
        public bool usesScopeOverlay;              // снайперская оптика с чёрной виньеткой / PiP

        void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name.Replace("ATT_", "").ToLowerInvariant();
        }
    }
}
