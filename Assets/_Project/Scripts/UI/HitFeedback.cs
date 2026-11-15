namespace Frontline
{
    /// <summary>
    /// Отметки попаданий игрока из любого оружия и техники (винтовка, пушка танка, пулемёт самолёта, ракеты,
    /// артиллерия, гранаты) и сообщение о рикошете. Источник — системы урона; потребитель — HUD.
    /// </summary>
    public static class HitFeedback
    {
        public static event System.Action<bool> Hit;          // true — цель уничтожена
        public static event System.Action Ricocheted;

        public static void Report(int sourceActorId, bool kill)
        {
            if (sourceActorId == CombatWorld.PlayerActorId) Hit?.Invoke(kill);
        }

        public static void Ricochet() => Ricocheted?.Invoke();
    }
}
