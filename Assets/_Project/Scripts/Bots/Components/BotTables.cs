using Unity.Mathematics;

namespace Frontline.Bots
{
    public static class BotSkillTable
    {
        public static BotSkill For(BotDifficulty d) => d switch
        {
            BotDifficulty.Easy => new BotSkill
            {
                reactionTime = 0.9f, aimErrorDeg = 6f, aimSettleTime = 2.5f, visionRange = 70f, fovCos = math.cos(math.radians(35)),
                hearingMul = 0.5f, burstMin = 2, burstMax = 4, usesCover = false, flanks = false, reactsToSound = false,
                builds = false, thinkInterval = 0.6f
            },
            BotDifficulty.Medium => new BotSkill
            {
                reactionTime = 0.55f, aimErrorDeg = 3.5f, aimSettleTime = 1.6f, visionRange = 110f, fovCos = math.cos(math.radians(35)),
                hearingMul = 0.8f, burstMin = 3, burstMax = 6, usesCover = false, flanks = false, reactsToSound = true,
                builds = false, thinkInterval = 0.45f
            },
            BotDifficulty.Hard => new BotSkill
            {
                reactionTime = 0.32f, aimErrorDeg = 2.0f, aimSettleTime = 1.0f, visionRange = 160f, fovCos = math.cos(math.radians(35)),
                hearingMul = 1f, burstMin = 3, burstMax = 7, usesCover = true, flanks = true, reactsToSound = true,
                builds = true, thinkInterval = 0.3f
            },
            _ /* Realistic */ => new BotSkill
            {
                reactionTime = 0.22f, aimErrorDeg = 1.2f, aimSettleTime = 0.7f, visionRange = 220f, fovCos = math.cos(math.radians(35)),
                hearingMul = 1.2f, burstMin = 2, burstMax = 5, usesCover = true, flanks = true, reactsToSound = true,
                builds = true, thinkInterval = 0.2f
            }
        };
    }

    /// <summary>
    /// Упрощённые профили оружия ботов (ECS не хранит ScriptableObject). Заполняется из GameDatabase при старте матча,
    /// индекс = (byte)WeaponCategory, отдельно для каждой фракции. Здесь — значения по умолчанию.
    /// </summary>
    public static class BotWeaponTable
    {
        public static BotWeapon Default(Faction f, SoldierClass c)
        {
            switch (c)
            {
                case SoldierClass.Recon: // Kar98k / Мосина
                    return new BotWeapon { weaponIndex = (byte)WeaponCategory.SniperRifle, damage = 95, headMul = 2f, fireInterval = 1.25f,
                        range = 400, muzzleVelocity = 820, loudness = 220, spreadDeg = 0.2f, recoilClimbDeg = 0, magazine = 5, ammo = 5, reloadTime = 3.2f, isAuto = false };
                case SoldierClass.Support: // MG-42 / ДП-28
                    return f == Faction.Germany
                        ? new BotWeapon { weaponIndex = (byte)WeaponCategory.MachineGun, damage = 30, headMul = 1.6f, fireInterval = 0.05f,
                            range = 250, muzzleVelocity = 740, loudness = 200, spreadDeg = 2.4f, recoilClimbDeg = 0.25f, magazine = 50, ammo = 50, reloadTime = 5.5f, isAuto = true }
                        : new BotWeapon { weaponIndex = (byte)WeaponCategory.MachineGun, damage = 32, headMul = 1.6f, fireInterval = 0.109f,
                            range = 250, muzzleVelocity = 840, loudness = 190, spreadDeg = 2.0f, recoilClimbDeg = 0.2f, magazine = 47, ammo = 47, reloadTime = 4.8f, isAuto = true };
                case SoldierClass.Engineer: // MP-40 / ППШ-41
                    return f == Faction.Germany
                        ? new BotWeapon { weaponIndex = (byte)WeaponCategory.SubmachineGun, damage = 22, headMul = 1.5f, fireInterval = 0.12f,
                            range = 90, muzzleVelocity = 400, loudness = 120, spreadDeg = 2.8f, recoilClimbDeg = 0.12f, magazine = 32, ammo = 32, reloadTime = 2.6f, isAuto = true }
                        : new BotWeapon { weaponIndex = (byte)WeaponCategory.SubmachineGun, damage = 19, headMul = 1.5f, fireInterval = 0.063f,
                            range = 85, muzzleVelocity = 490, loudness = 125, spreadDeg = 3.4f, recoilClimbDeg = 0.1f, magazine = 71, ammo = 71, reloadTime = 3.6f, isAuto = true };
                default: // Assault: StG-44 / СВТ-40
                    return f == Faction.Germany
                        ? new BotWeapon { weaponIndex = (byte)WeaponCategory.AssaultRifle, damage = 30, headMul = 1.8f, fireInterval = 0.109f,
                            range = 180, muzzleVelocity = 685, loudness = 160, spreadDeg = 1.8f, recoilClimbDeg = 0.18f, magazine = 30, ammo = 30, reloadTime = 2.8f, isAuto = true }
                        : new BotWeapon { weaponIndex = (byte)WeaponCategory.AssaultRifle, damage = 42, headMul = 1.8f, fireInterval = 0.2f,
                            range = 220, muzzleVelocity = 840, loudness = 175, spreadDeg = 1.2f, recoilClimbDeg = 0.3f, magazine = 10, ammo = 10, reloadTime = 2.9f, isAuto = false };
            }
        }
    }
}
