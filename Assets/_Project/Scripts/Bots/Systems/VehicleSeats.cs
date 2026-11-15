using Unity.Entities;
using Unity.Rendering;

namespace Frontline.Bots
{
    /// <summary>Посадка ботов в машину и высадка: включение BotInVehicle и скрытие/показ частей тела.</summary>
    public static class VehicleSeats
    {
        public static bool IsBoarded(EntityManager em, Entity e) =>
            e != Entity.Null && em.Exists(e) && em.HasComponent<BotInVehicle>(e) && em.IsComponentEnabled<BotInVehicle>(e);

        public static void Board(EntityManager em, Entity e, int vehicleIndex, byte seat)
        {
            if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<BotInVehicle>(e)) return;
            em.SetComponentData(e, new BotInVehicle { vehicleIndex = vehicleIndex, seat = seat });
            em.SetComponentEnabled<BotInVehicle>(e, true);
            SetVisible(em, e, false);
        }

        public static void Unboard(EntityManager em, Entity e)
        {
            if (!IsBoarded(em, e)) return;
            em.SetComponentEnabled<BotInVehicle>(e, false);
            SetVisible(em, e, true);
            if (em.HasComponent<BotLodLevel>(e)) em.SetComponentData(e, new BotLodLevel());   // всё тело снова видно — уровень 0
        }

        static void SetVisible(EntityManager em, Entity root, bool on)
        {
            if (!em.HasBuffer<LinkedEntityGroup>(root)) return;
            var group = em.GetBuffer<LinkedEntityGroup>(root);
            var list = new Unity.Collections.NativeList<Entity>(group.Length, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < group.Length; i++) list.Add(group[i].Value);
            foreach (var child in list)
            {
                if (child == root || !em.Exists(child)) continue;
                bool hidden = em.HasComponent<DisableRendering>(child);
                if (on && hidden) em.RemoveComponent<DisableRendering>(child);
                else if (!on && !hidden) em.AddComponent<DisableRendering>(child);
            }
            list.Dispose();
        }
    }
}
