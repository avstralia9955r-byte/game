using System;
using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>Базовые характеристики оружия. Обвесы модифицируют их через WeaponStats.Compute.</summary>
    [CreateAssetMenu(menuName = "Frontline/Weapon", fileName = "WPN_")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Идентификация")]
        public string id;                 // "kar98k", "mosin", ...
        public string displayName;
        public Faction faction;
        public WeaponCategory category;
        public int price;                 // цена в магазине главного меню (0 — бесплатно)

        [Header("Стрельба")]
        public FireMode fireMode = FireMode.Semi;
        public float roundsPerMinute = 600;
        public float damage = 30;
        public float headshotMultiplier = 2f;
        public AnimationCurve damageFalloff = AnimationCurve.Linear(0, 1, 150, 0.6f); // x = метры
        public float maxRange = 300;
        public float muzzleVelocity = 760;          // м/с, для трассеров и упреждения ботов
        public int pelletsPerShot = 1;

        [Header("Точность (градусы)")]
        public float hipSpread = 3.5f;
        public float adsSpread = 0.15f;
        public float moveSpreadPenalty = 2f;
        public float spreadPerShot = 0.35f;
        public float spreadRecovery = 6f;

        [Header("Отдача")]
        public float recoilVertical = 1.2f;         // градусы за выстрел
        public float recoilHorizontal = 0.4f;       // ± случайно
        public float recoilRecoverySpeed = 8f;
        public float adsRecoilMultiplier = 0.7f;

        [Header("Боезапас и перезарядка")]
        public int magazineSize = 30;
        public int reserveAmmo = 120;
        public ReloadType reloadType = ReloadType.Magazine;
        public float reloadTime = 2.4f;
        public float reloadTimeEmpty = 3.0f;
        public int clipSize = 5;                    // для StripperClip

        [Header("ADS")]
        public float adsTime = 0.25f;
        public float adsFov = 60f;
        public float moveSpeedMultiplier = 1f;

        [Header("Противотанковое / снаряды")]
        [Tooltip("Урон по технике за попадание (0 — пули рикошетят от брони).")]
        public float armorDamage = 0f;
        [Tooltip("> 0 — оружие стреляет снарядом с этой скоростью (м/с) вместо мгновенного луча.")]
        public float projectileSpeed = 0f;
        public float projectileGravity = 1f, explosionRadius = 0f, explosionDamage = 0f;

        [Header("Шум")]
        public float loudnessRadius = 120f;         // радиус, на котором боты «слышат» выстрел

        [Header("Визуал / звук")]
        public GameObject viewModelPrefab;          // содержит WeaponSockets
        public GameObject worldModelPrefab;         // упрощённая модель для ботов (LOD)
        public AudioClip fireSound, fireSoundSuppressed, reloadSound, dryFireSound;
        public string animatorProfile;              // набор анимаций: "rifle_bolt", "smg", "pistol", ...

        [Header("Обвесы")]
        public List<AttachmentDefinition> compatibleAttachments = new();

        public float FireInterval => 60f / Mathf.Max(1f, roundsPerMinute);

        void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name.Replace("WPN_", "").ToLowerInvariant();
        }
    }
}
