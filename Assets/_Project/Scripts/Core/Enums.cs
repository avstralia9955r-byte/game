namespace Frontline
{
    public enum Faction : byte { Germany = 0, USSR = 1 }

    /// <summary>Классы бойцов. Шпатель (строительство) есть у Assault и Engineer.</summary>
    public enum SoldierClass : byte { Assault = 0, Engineer = 1, Support = 2, Recon = 3 }

    public enum WeaponCategory : byte
    {
        SniperRifle, AssaultRifle, SubmachineGun, Pistol, Pistol2,
        MachineGun, BattleRifle, Knife, Trowel,
        Launcher        // Panzerfaust / ПТРД-41 — противотанковое (5-й слот у инженера)
    }

    public enum FireMode : byte { Semi, Auto, Bolt }
    public enum ReloadType : byte { Magazine, StripperClip, SingleRound }

    public enum AttachmentSlot : byte { Muzzle, Magazine, Optic, Underbarrel, Laser }

    public enum AttachmentType : byte
    {
        Suppressor, ExtendedMag, Scope, TacticalGrip, LaserSight, MuzzleBrake,
        FlashHider, DrumMag, ScopeShort, Bipod, Bayonet
    }

    public enum MapTheme : byte { Summer = 0, Winter = 1 }
    public enum TimeOfDay : byte { Day = 0, Evening = 1 }
    public enum Weather : byte { Clear = 0, LightRain = 1, Snow = 2 }

    public enum GameModeType : byte { TerritoryControl = 0, TeamDeathmatch = 1 }

    public enum BotDifficulty : byte { Easy = 0, Medium = 1, Hard = 2, Realistic = 3 }

    public enum GraphicsPreset : byte { Low = 0, Medium = 1, High = 2, Ultra = 3 }
}
