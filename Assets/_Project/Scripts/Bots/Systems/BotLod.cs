namespace Frontline.Bots
{
    /// <summary>
    /// Масштаб дистанций «упрощённого» поведения дальних ботов (движение через кадр, редкая поза и зрение).
    /// 1 — ПК, 0.75 — ПК при 256+ ботах, 0.5 — телефон: всё, что дальше, считается экономнее.
    /// </summary>
    public static class BotLod
    {
        public static float Scale = 1f;
    }
}
