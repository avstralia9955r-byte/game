using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Frontline.Bots
{
    /// <summary>Текущий уровень детализации модели бота (0 — полная).</summary>
    public struct BotLodLevel : IComponentData { public byte level; }

    /// <summary>
    /// Детализация ботов по расстоянию до камеры. Расстояние делится на кратность приближения камеры
    /// (поле зрения оптики, бинокля или прицела танка), поэтому в прицеле дальние бойцы снова полные.
    /// • ближе 55 м — всё тело;
    /// • 55–140 м — без предплечий и ранца;
    /// • дальше — без рук и лица (силуэт: корпус, ноги, каска, оружие).
    /// Обрабатывается по 64 бота за кадр, смена уровня — с запасом (гистерезис), чтобы не мигало.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class BotVisualLodSystem : SystemBase
    {
        EntityQuery _q;
        int _cursor;
        static float _baseFov = 70f;

        protected override void OnCreate()
        {
            _q = GetEntityQuery(ComponentType.ReadOnly<BotIdentity>(), ComponentType.ReadOnly<LocalTransform>(),
                                ComponentType.ReadWrite<BotLodLevel>(), ComponentType.Exclude<Prefab>());
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            if (!cam) return;
            var pc = PlayerBridge.Controller;
            if (pc) _baseFov = Mathf.Max(30f, pc.BaseFov);
            float zoom = Mathf.Clamp(cam.fieldOfView / _baseFov, 0.05f, 1.2f);
            if (Application.isMobilePlatform) zoom *= 1.8f;     // телефон: упрощение с 35 и 80 м
            float3 cp = cam.transform.position;
            var ents = _q.ToEntityArray(Allocator.Temp);
            int n = ents.Length;
            if (n == 0) { ents.Dispose(); return; }
            var em = EntityManager;
            int budget = math.min(n, Application.isMobilePlatform ? 32 : 64), changes = 0;
            for (int k = 0; k < budget && changes < 24; k++)
            {
                var e = ents[(_cursor + k) % n];
                if (em.HasComponent<BotInVehicle>(e) && em.IsComponentEnabled<BotInVehicle>(e)) continue;   // в машине тело скрыто целиком
                float d = math.distance(cp, em.GetComponentData<LocalTransform>(e).Position) * zoom;
                byte cur = em.GetComponentData<BotLodLevel>(e).level;
                byte want = cur;
                if (d < (cur == 0 ? 62f : 50f)) want = 0;
                else if (d < (cur >= 2 ? 125f : 145f)) want = 1;
                else if (d < (cur == 3 ? 230f : 260f)) want = 2;
                else want = 3;                        // минималистичный силуэт: корпус, бёдра, каска, оружие
                if (want == cur) continue;
                Apply(em, e, want);
                em.SetComponentData(e, new BotLodLevel { level = want });
                changes++;
            }
            _cursor = (_cursor + budget) % n;
            ents.Dispose();
        }

        static bool Hidden(LimbKind k, byte level) => level switch
        {
            0 => false,
            1 => k is LimbKind.Forearm or LimbKind.Pack,
            2 => k is LimbKind.Forearm or LimbKind.Pack or LimbKind.UpperArm or LimbKind.Head,
            _ => k is LimbKind.Forearm or LimbKind.Pack or LimbKind.UpperArm or LimbKind.Head or LimbKind.Shin
        };

        public static void Apply(EntityManager em, Entity root, byte level)
        {
            if (!em.HasBuffer<LinkedEntityGroup>(root)) return;
            var group = em.GetBuffer<LinkedEntityGroup>(root);
            var list = new NativeList<Entity>(group.Length, Allocator.Temp);
            for (int i = 0; i < group.Length; i++) list.Add(group[i].Value);
            foreach (var c in list)
            {
                if (c == root || !em.Exists(c) || !em.HasComponent<BotLimb>(c)) continue;
                bool hide = Hidden(em.GetComponentData<BotLimb>(c).kind, level);
                bool off = em.HasComponent<DisableRendering>(c);
                if (hide && !off) em.AddComponent<DisableRendering>(c);
                else if (!hide && off) em.RemoveComponent<DisableRendering>(c);
            }
            list.Dispose();
        }
    }
}
