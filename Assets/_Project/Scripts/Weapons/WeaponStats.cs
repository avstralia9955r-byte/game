using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>Итоговые характеристики оружия после применения обвесов.</summary>
    public struct WeaponStats
    {
        public float damage, fireInterval, maxRange, muzzleVelocity;
        public float hipSpread, adsSpread, spreadPerShot, spreadRecovery, moveSpreadPenalty;
        public float recoilV, recoilH, recoilRecovery, adsRecoilMul;
        public int magazineSize, reserveAmmo;
        public float reloadTime, reloadTimeEmpty, adsTime, adsFov;
        public float loudnessRadius;
        public bool suppressed, hideFlash, scopeOverlay;
        public float damageRangeMul;

        public static WeaponStats Compute(WeaponDefinition w, IReadOnlyList<AttachmentDefinition> atts)
        {
            var s = new WeaponStats
            {
                damage = w.damage, fireInterval = w.FireInterval, maxRange = w.maxRange, muzzleVelocity = w.muzzleVelocity,
                hipSpread = w.hipSpread, adsSpread = w.adsSpread, spreadPerShot = w.spreadPerShot,
                spreadRecovery = w.spreadRecovery, moveSpreadPenalty = w.moveSpreadPenalty,
                recoilV = w.recoilVertical, recoilH = w.recoilHorizontal, recoilRecovery = w.recoilRecoverySpeed,
                adsRecoilMul = w.adsRecoilMultiplier,
                magazineSize = w.magazineSize, reserveAmmo = w.reserveAmmo,
                reloadTime = w.reloadTime, reloadTimeEmpty = w.reloadTimeEmpty,
                adsTime = w.adsTime, adsFov = w.adsFov, loudnessRadius = w.loudnessRadius,
                damageRangeMul = 1f
            };
            if (atts == null) return s;

            float magMul = 1f;
            foreach (var a in atts)
            {
                if (!a) continue;
                s.loudnessRadius *= a.loudnessMultiplier;
                s.recoilV *= a.verticalRecoilMultiplier;
                s.recoilH *= a.horizontalRecoilMultiplier;
                s.hipSpread *= a.hipSpreadMultiplier;
                s.adsTime *= a.adsTimeMultiplier;
                s.damageRangeMul *= a.damageRangeMultiplier;
                magMul *= a.magazineMultiplier;
                if (a.adsFovOverride > 0) s.adsFov = a.adsFovOverride;
                if (a.type == AttachmentType.Suppressor) s.suppressed = true;
                s.hideFlash |= a.suppressesMuzzleFlash;
                s.scopeOverlay |= a.usesScopeOverlay;
            }
            s.magazineSize = Mathf.Max(1, Mathf.RoundToInt(w.magazineSize * magMul));
            s.reserveAmmo = Mathf.RoundToInt(w.reserveAmmo * magMul);
            return s;
        }

        public float DamageAt(WeaponDefinition w, float distance) =>
            damage * w.damageFalloff.Evaluate(distance / Mathf.Max(0.01f, damageRangeMul));
    }

    /// <summary>Проверка совместимости и слота: в один слот — один обвес.</summary>
    public static class AttachmentRules
    {
        public static bool CanInstall(WeaponDefinition w, AttachmentDefinition a, IReadOnlyList<AttachmentDefinition> current)
        {
            if (!w.compatibleAttachments.Contains(a)) return false;
            // Оптика — только на снайперских и карабинах.
            if (a.type == AttachmentType.Scope &&
                w.category != WeaponCategory.SniperRifle && w.category != WeaponCategory.BattleRifle) return false;
            if (a.type == AttachmentType.ScopeShort && w.category is not (WeaponCategory.SniperRifle or WeaponCategory.BattleRifle or WeaponCategory.AssaultRifle)) return false;
            return true;
        }

        public static List<AttachmentDefinition> Resolve(WeaponLoadoutData l)
        {
            var db = GameDatabase.Instance;
            var res = new List<AttachmentDefinition>();
            if (l == null) return res;
            var w = db.FindWeapon(l.weaponId);
            foreach (var id in l.installedAttachmentIds)
            {
                var a = db.FindAttachment(id);
                if (w && a && !w.compatibleAttachments.Contains(a)) continue;     // старый сейв: обвес больше не подходит
                if (a && !res.Exists(x => x.slot == a.slot)) res.Add(a);
            }
            return res;
        }

        /// <summary>Установить обвес (заменяет обвес того же слота). Возвращает false, если не разблокирован.</summary>
        public static bool Install(WeaponDefinition w, AttachmentDefinition a)
        {
            var p = SaveSystem.Progress;
            if (!p.IsUnlocked(a.id)) return false;
            var l = p.GetLoadout(w.id);
            var current = Resolve(l);
            if (!CanInstall(w, a, current)) return false;
            l.installedAttachmentIds.RemoveAll(id => GameDatabase.Instance.FindAttachment(id)?.slot == a.slot);
            l.installedAttachmentIds.Add(a.id);
            SaveSystem.SaveProgress();
            return true;
        }

        public static void Uninstall(WeaponDefinition w, AttachmentSlot slot)
        {
            var l = SaveSystem.Progress.GetLoadout(w.id);
            l.installedAttachmentIds.RemoveAll(id => GameDatabase.Instance.FindAttachment(id)?.slot == slot);
            SaveSystem.SaveProgress();
        }

        /// <summary>Покупка за локальную валюту или по достижению.</summary>
        public static bool TryUnlock(AttachmentDefinition a)
        {
            var p = SaveSystem.Progress;
            if (p.IsUnlocked(a.id)) return true;
            bool byAchievement = !string.IsNullOrEmpty(a.requiredAchievementId) && p.achievementIds.Contains(a.requiredAchievementId);
            if (!byAchievement)
            {
                if (p.currency < a.price) return false;
                p.currency -= a.price;
            }
            p.unlockedAttachmentIds.Add(a.id);
            SaveSystem.SaveProgress();
            return true;
        }
    }
}
