using System;
using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    [Serializable]
    /// <summary>Инструмент 4-го слота: аптечка (штурмовик), мешки с песком (инженер).</summary>
    public enum ToolKind : byte { None, Trowel, Sandbags, Medkit }

    public class ClassLoadout
    {
        public ToolKind tool;
        public SoldierClass soldierClass;
        public WeaponDefinition primary;
        public WeaponDefinition primaryAlt;   // штурмовик: FG-42 / АВС-36 вместо StG-44 / АВТ-40
        public System.Collections.Generic.List<WeaponDefinition> primaryOptions = new();   // все варианты основного оружия класса
        public WeaponDefinition secondary;
        public WeaponDefinition melee;     // штык-нож
        public bool hasTrowel;             // Assault + Engineer
        public WeaponDefinition gadget;    // 5-й слот: противотанковое у инженера
        public int grenades = 2;
    }

    [Serializable]
    public class CamoPalette
    {
        public Color primary = new(0.33f, 0.37f, 0.24f);
        public Color secondary = new(0.45f, 0.40f, 0.28f);
        public Color tertiary = new(0.20f, 0.18f, 0.14f);
        public Texture2D pattern;           // R/G/B-маска пятен
        public Texture2D brickAlbedo;
        [Range(0, 1)] public float snowCover;  // зимой: снег на верхних гранях (по нормали)
    }

    [Serializable]
    public class MatchConfig
    {
        public MapDefinition map;
        public GameModeType mode = GameModeType.TerritoryControl;
        public Faction playerFaction = Faction.USSR;
        public SoldierClass playerClass = SoldierClass.Assault;
        public bool useAltPrimary;
        [Range(0, 512)] public int botCount = 128;
        public BotDifficulty difficulty = BotDifficulty.Medium;
        public TimeOfDay timeOfDay = TimeOfDay.Day;
        public Weather weather = Weather.Clear;
        public float timeLimitMinutes = 25f;     // TC: 20–30
        public int scoreLimit = 1000;            // TC
        public int killLimit = 50;               // TDM
        [Range(0, 4)] public int tanksPerTeam = 1;   // устарело: см. tanksTotal
        [Range(0, 128)] public int tanksTotal = 2;   // всего танков на карте (поровну сторонам), ползунок в «Новой игре»
        [Range(0, 32)] public int planesTotal = 2;
        [Range(0, 24)] public int artilleryTotal = 2;  // артиллерийских орудий (поровну сторонам), ползунок в «Новой игре»   // всего самолётов (поровну сторонам), ползунок в «Новой игре»
        public bool jeepBuddyPlayer;             // второе место в джипе: false — подбегает бот, true — для второго игрока
    }

    /// <summary>Единый реестр контента. Лежит в Resources/GameDatabase.asset.</summary>
    [CreateAssetMenu(menuName = "Frontline/Game Database", fileName = "GameDatabase")]
    public class GameDatabase : ScriptableObject
    {
        public FactionArsenal germany, ussr;
        public List<AttachmentDefinition> attachments = new();
        public List<MapDefinition> maps = new();
        public GameObject brickPrefab;
        public Mesh brickMesh;
        public Material brickMaterial;

        static GameDatabase _instance;
        public static GameDatabase Instance
        {
            get
            {
                if (_instance) return _instance;
                // Ассет Resources/GameDatabase (меню «Frontline → Generate Content» старых версий) содержит только цифры:
                // у оружия нет моделей от первого лица и звуков — с ним оружие в руках было невидимым, а у ботов
                // вместо детальных моделей — заглушки. Берём ассет, только если он полный; иначе — встроенный контент.
                var asset = Resources.Load<GameDatabase>("GameDatabase");
                if (asset && asset.IsComplete()) return _instance = asset;
                if (asset) Debug.LogWarning("[Frontline] Resources/GameDatabase.asset устарел (у оружия нет моделей) — используется встроенный контент. " +
                                            "Ассет можно удалить: Assets/_Project/Resources/GameDatabase.asset.");
                return _instance = RuntimeContent.Create();
            }
        }

        /// <summary>У каждого оружия обеих сторон есть модель от первого лица, у стрелкового — звук выстрела.</summary>
        public bool IsComplete()
        {
            foreach (var a in new[] { germany, ussr })
            {
                if (!a || a.weapons == null || a.weapons.Count == 0) return false;
                foreach (var w in a.weapons)
                    if (!w || !w.viewModelPrefab || (w.category != WeaponCategory.Knife && !w.fireSound)) return false;
            }
            return true;
        }

        public FactionArsenal Arsenal(Faction f) => f == Faction.Germany ? germany : ussr;

        public WeaponDefinition FindWeapon(string id)
        {
            foreach (var a in new[] { germany, ussr })
                if (a) foreach (var w in a.weapons) if (w && w.id == id) return w;
            return null;
        }

        public AttachmentDefinition FindAttachment(string id) => attachments.Find(a => a && a.id == id);
    }

    /// <summary>Текущий матч — передаётся из меню в игровую сцену.</summary>
    public static class MatchSession
    {
        public static MatchConfig Current = new();
    }
}
