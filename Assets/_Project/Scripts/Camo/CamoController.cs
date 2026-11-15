using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Автоматическая смена камуфляжа по карте: все материалы персонажей, оружия и кирпичей используют
    /// шейдер Frontline/StylizedLit, который берёт палитру из ГЛОБАЛЬНЫХ свойств. Поэтому одна запись
    /// при загрузке карты перекрашивает всё сразу — без дублирования материалов и без потери SRP Batcher.
    /// </summary>
    public class CamoController : MonoBehaviour
    {
        static readonly int Primary = Shader.PropertyToID("_CamoPrimary");
        static readonly int Secondary = Shader.PropertyToID("_CamoSecondary");
        static readonly int Tertiary = Shader.PropertyToID("_CamoTertiary");
        static readonly int Pattern = Shader.PropertyToID("_CamoPattern");
        static readonly int BrickAlbedo = Shader.PropertyToID("_BrickAlbedo");
        static readonly int SnowCover = Shader.PropertyToID("_SnowCover");

        public void Apply(MapDefinition map) => Apply(map.camo, map.theme);

        public static void Apply(CamoPalette c, MapTheme theme)
        {
            Shader.SetGlobalColor(Primary, c.primary.linear);
            Shader.SetGlobalColor(Secondary, c.secondary.linear);
            Shader.SetGlobalColor(Tertiary, c.tertiary.linear);
            if (c.pattern) Shader.SetGlobalTexture(Pattern, c.pattern);
            if (c.brickAlbedo) Shader.SetGlobalTexture(BrickAlbedo, c.brickAlbedo);
            Shader.SetGlobalFloat(SnowCover, theme == MapTheme.Winter ? Mathf.Max(0.35f, c.snowCover) : c.snowCover);
        }

        /// <summary>Палитры по умолчанию, если в MapDefinition не заданы свои.</summary>
        public static CamoPalette DefaultFor(MapTheme t) => t == MapTheme.Winter
            ? new CamoPalette { primary = new Color(0.86f, 0.87f, 0.88f), secondary = new Color(0.62f, 0.64f, 0.66f), tertiary = new Color(0.22f, 0.27f, 0.22f), snowCover = 0.45f }
            : new CamoPalette { primary = new Color(0.33f, 0.37f, 0.24f), secondary = new Color(0.47f, 0.42f, 0.29f), tertiary = new Color(0.22f, 0.19f, 0.14f), snowCover = 0f };
    }
}
