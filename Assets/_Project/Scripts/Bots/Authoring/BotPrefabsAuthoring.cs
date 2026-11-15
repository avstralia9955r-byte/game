using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Frontline.Bots
{
    /// <summary>В сабсцене карты: ссылки на префабы ботов обеих фракций.</summary>
    public class BotPrefabsAuthoring : MonoBehaviour
    {
        public GameObject germanyBot, ussrBot;

        class PrefabsBaker : Baker<BotPrefabsAuthoring>
        {
            public override void Bake(BotPrefabsAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new BotPrefabs
                {
                    germany = GetEntity(a.germanyBot, TransformUsageFlags.Dynamic),
                    ussr = GetEntity(a.ussrBot, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}
