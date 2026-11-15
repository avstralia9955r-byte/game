using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Frontline.Bots
{
    /// <summary>
    /// Вешается на префаб бота (меш с LODGroup: LOD0 ~2.5k tris, LOD1 ~900, LOD2 ~250 + импостор/кулинг).
    /// Entities Graphics запекает LODGroup в MeshLODGroupComponent; материал — Frontline/StylizedLit с VAT.
    /// </summary>
    public class BotAuthoring : MonoBehaviour
    {
        public Faction faction;
        public float maxHealth = 50f;     // вдвое меньше прежнего — бои быстрее

        class BotBaker : Baker<BotAuthoring>
        {
            public override void Bake(BotAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new BotIdentity { team = (byte)a.faction });
                AddComponent(e, new BotHealth { value = a.maxHealth, max = a.maxHealth });
                AddComponent(e, new BotDead());
                SetComponentEnabled<BotDead>(e, false);
                AddComponent(e, new BotMotion());
                AddComponent(e, new BotBrain());
                AddComponent(e, new BotPerception { targetActorId = -1, candidateActorId = -1 });
                AddComponent(e, new BotWeapon());
                AddComponent(e, new BotSkill());
                AddBuffer<BotPathPoint>(e);
                AddComponent(e, new BotPathCursor());
                AddComponent(e, new BotPathRequest());
                AddComponent(e, new VatClip());
                AddComponent(e, new VatTime());
                AddComponent(e, new TeamTint { value = new float4(1, 1, 1, 1) });
            }
        }
    }

}
