using System.Collections.Generic;
using Frontline.Bots;
using UnityEngine;

namespace Frontline.Net
{
    public enum NetRole : byte { None, Host, Client }

    /// <summary>Найденная в локальной сети игра (из широковещательного объявления хоста).</summary>
    public class LanGame
    {
        public string address, hostName, mode;
        public int bots, minutes, players;
        public bool winter;
        public float seenAt;
    }

    /// <summary>
    /// Общее состояние сетевой игры по локальной сети (кабель или один Wi-Fi). Хост — авторитетный: у него идут ИИ ботов,
    /// попадания, техника, точки и счёт; клиенты присылают своё положение, урон, который нанесли, и визуальные события,
    /// а получают снимки мира 20 раз в секунду. Порты UDP: 27015 — игра, 27016 — поиск игр.
    ///
    /// Нумерация бойцов: у хоста 0 — сам хост, 1..N — боты, N+1..N+3 — подключённые игроки.
    /// У клиента 0 — всегда он сам (так устроены HUD, оружие, PlayerController), поэтому номера хоста и клиента
    /// для «0» и «своего слота» меняются местами: HostToLocal/LocalToHost.
    /// </summary>
    public static class NetSession
    {
        public const int GamePort = 27015, DiscoveryPort = 27016, MaxRemote = 3, Version = 1;
        public const string Magic = "PFRM";

        public static NetRole Role;
        public static bool IsHost => Role == NetRole.Host;
        public static bool IsClient => Role == NetRole.Client;
        public static bool Active => Role != NetRole.None;

        /// <summary>Общее зерно матча: одинаковые классы и стартовые точки ботов у хоста и клиентов.</summary>
        public static uint Seed = 1;
        /// <summary>Сторона игрока-хоста: от неё зависит, каким ботам какая команда (у клиента — та же раскладка).</summary>
        public static Faction HostFaction = Faction.USSR;
        /// <summary>Номер своего слота на хосте (только у клиента).</summary>
        public static int MyRemoteId;
        public static string PlayerName = "Боец";
        public static string Status = "";

        public static readonly List<LanGame> Found = new();

        /// <summary>Первый номер слота сетевых игроков (сразу после ботов).</summary>
        public static int FirstRemoteId => Mathf.Clamp(MatchBridge.BotCount, 0, BotRegistry.MaxBots) + 1;
        public static bool IsRemoteSlot(int actorId) => Active && actorId >= FirstRemoteId && actorId < FirstRemoteId + MaxRemote;

        /// <summary>Номер с хоста → локальный (у клиента меняются местами 0 и свой слот). Преобразование само себе обратно.</summary>
        public static int HostToLocal(int id) => !IsClient ? id : id == 0 ? MyRemoteId : id == MyRemoteId ? 0 : id;
        public static int LocalToHost(int id) => HostToLocal(id);

        /// <summary>Имя для ленты убийств: сетевые игроки и хост — по-человечески, а не «рядовой Шульц».</summary>
        public static string NameOf(int localActorId)
        {
            if (!Active) return null;
            if (IsClient && localActorId == MyRemoteId) return "Хост";
            if (IsRemoteSlot(localActorId)) return "Игрок " + (localActorId - FirstRemoteId + 2);
            return null;
        }

        public static void Stop()
        {
            if (NetDriver.I) Object.Destroy(NetDriver.I.gameObject);
            Role = NetRole.None;
            Status = "";
        }
    }
}
