using Frontline.Bots;
using Unity.Entities;

namespace Frontline.Net
{
    /// <summary>
    /// У клиента ИИ ботов, их стрельба, урон и снимки акторов выключаются целиком (ими управляет хост):
    /// системы просто отключаются в мире ECS — без проверок внутри Burst-кода.
    /// </summary>
    public static class NetSystems
    {
        public static void SetClientMode(bool client)
        {
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return;
            void Un<T>() where T : unmanaged, ISystem
            {
                var h = w.GetExistingSystem<T>();
                if (h != SystemHandle.Null) w.Unmanaged.ResolveSystemStateRef(h).Enabled = !client;
            }
            void Man<T>() where T : SystemBase
            {
                var s = w.GetExistingSystemManaged<T>();
                if (s != null) s.Enabled = !client;
            }
            Un<BotDecisionSystem>(); Un<BotMovementSystem>(); Un<BotHearingSystem>(); Un<BotVisionSystem>();
            Un<BotCombatSystem>(); Un<BotSnapshotSystem>();
            Man<BotPathRequestSystem>(); Man<BotShotResolveSystem>();
        }
    }
}
