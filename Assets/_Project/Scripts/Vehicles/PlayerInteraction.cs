using UnityEngine;
using UnityEngine.InputSystem;

namespace Frontline
{
    /// <summary>Действие на E: сесть в свой танк, пополнить боеприпасы из ящика. Текст подсказки — для HUD.</summary>
    public class PlayerInteraction : MonoBehaviour
    {
        public PlayerController player;
        public static string Prompt { get; private set; }
        float _cooldown;
        PlayerHealth _hp;

        void Awake() => _hp = GetComponent<PlayerHealth>();

        static string SeatKey => GameInput.Initialized ? GameInput.FindAction("SwitchSeat").GetBindingDisplayString(0) : "X";
        static string ReloadKey => GameInput.Initialized ? GameInput.FindAction("Reload").GetBindingDisplayString(0) : "R";
        static string Key => GameInput.Initialized ? GameInput.FindAction("Interact").GetBindingDisplayString(0) : "E";

        void Update()
        {
            Prompt = null;
            if (!player) return;
            if (player.Gunning)
            {
                var gg = player.Gunning;
                Prompt = $"[{Key}] Отойти от орудия";
                if (!gg.IsAA && !Net.NetSession.IsClient)
                {
                    if (gg.Loader) Prompt += "   · заряжающий нанят";
                    else
                    {
                        Prompt += $"   [J] Нанять заряжающего — {Shop.LoaderPrice} монет (у вас {Shop.Coins})";
                        bool hire = Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame;
                        if (hire && Time.time > _cooldown)
                        {
                            _cooldown = Time.time + 0.6f;
                            if (!gg.HireLoader()) BotAudio.PlayAt(SoundSynth.Hatch(), gg.transform.position, 0.2f);
                        }
                    }
                }
                return;
            }
            if (player.Flying)
            {
                var a = player.Flying;
                Prompt = a.Airborne ? $"[{Key}] Выпрыгнуть с парашютом" : $"[{Key}] Выйти из самолёта";
                return;
            }
            if (player.Vehicle)
            {
                var v = player.Vehicle;
                if (player.RideSeat)
                {
                    // Второй игрок едет за пулемётчика со своим оружием
                    Prompt = $"[{Key}] Выйти из машины";
                    if (player.Input.interactPressed && Time.time > _cooldown)
                    {
                        _cooldown = Time.time + 0.6f;
                        Vector3 exit = v.transform.position - v.transform.right * 2.4f;
                        exit = GameModeManager.GroundAt(exit) + Vector3.up * 0.2f;
                        player.ExitVehicle(exit, v.transform.eulerAngles.y);
                    }
                    return;
                }
                Prompt = v.IsLight ? $"[{Key}] Выйти   [{SeatKey}] Пересесть ({Tank.SeatName(v.PlayerSeat)})" +
                                     (v.AnySeatFree ? (v.BuddyCalled ? "   · боец идёт" : "   [3] Позвать бойца на свободное место") : "")
                                   : $"[{Key}] Выйти";
                return;
            }
            if (Net.NetSession.IsClient)
            {
                // Клиент: техникой управляет хост, но в джип можно встать вторым игроком (место пулемётчика, своё оружие)
                if (!player.InputEnabled || (_hp && !_hp.Alive)) return;
                Vector3 cp = player.transform.position + Vector3.up;
                Tank jeep = null; float jd = 4.2f;
                foreach (var t in Tank.All)
                {
                    if (!t || !t.Alive || !t.IsLight || t.Team != player.Team) continue;
                    float d = t.DistanceTo(cp);
                    if (d < jd) { jd = d; jeep = t; }
                }
                if (jeep)
                {
                    Prompt = $"[{Key}] Встать в {jeep.DisplayName} (второй игрок)";
                    if (player.Input.interactPressed && Time.time > _cooldown) { _cooldown = Time.time + 0.6f; player.RideAsPassenger(jeep, jeep.SeatTransform(JeepSeat.Gunner)); }
                }
                return;
            }
            if (!player.InputEnabled || (_hp && !_hp.Alive)) return;
            bool press = player.Input.interactPressed && Time.time > _cooldown;
            Vector3 pos = player.transform.position + Vector3.up;

            // Снаряды для артиллерии: взять из кучи (E), зарядить орудие рядом (R)
            var pile = AmmoPile.Of(player.Team);
            if (pile && !player.CarryingShell && Vector3.Distance(pile.transform.position, player.transform.position) < 3.6f)
            {
                Prompt = pile.Count > 0 ? $"[{Key}] Взять снаряд ({pile.Count} в куче)" : "Снарядов нет — ждите грузовик снабжения";
                if (press && pile.Count > 0 && pile.Take()) { _cooldown = Time.time + 0.6f; player.CarryingShell = true; BotAudio.PlayAt(SoundSynth.AmmoPickup(), pile.transform.position, 0.7f); }
                return;
            }
            // Помощь в разгрузке: снаряд из кузова стоящего грузовика → отнести в кучу
            if (!player.CarryingShell)
            {
                foreach (var tr in SupplyTruck.All)
                {
                    if (!tr || tr.Team != player.Team || !tr.CanPlayerTake) continue;
                    if (Vector3.Distance(tr.BackPos, player.transform.position) > 3.2f) continue;
                    Prompt = $"[{Key}] Взять снаряд из грузовика ({tr.Cargo})";
                    if (press && tr.PlayerTake()) { _cooldown = Time.time + 0.6f; player.CarryingShell = true; BotAudio.PlayAt(SoundSynth.AmmoPickup(), tr.BackPos, 0.7f); }
                    return;
                }
            }
            if (player.CarryingShell && pile && pile.Count < AmmoPile.Capacity && Vector3.Distance(pile.transform.position, player.transform.position) < 3.6f)
            {
                Prompt = $"[{Key}] Положить снаряд в кучу ({pile.Count}/{AmmoPile.Capacity})";
                if (press && pile.Add()) { _cooldown = Time.time + 0.6f; player.CarryingShell = false; BotAudio.PlayAt(SoundSynth.AmmoPickup(), pile.transform.position, 0.6f); }
                // со снарядом у кучи всё ещё можно подойти к орудию — подсказку про R покажем ниже, если оно рядом
                bool gunNear = false;
                foreach (var g in ArtilleryGun.All)
                    if (g && !g.IsAA && g.Alive && g.Team == player.Team && !g.Loaded && !g.Loading && Vector3.Distance(g.transform.position, player.transform.position) <= 7f) { gunNear = true; break; }
                if (!gunNear) return;
            }
            if (player.CarryingShell)
            {
                foreach (var g in ArtilleryGun.All)
                {
                    if (!g || g.IsAA || !g.Alive || g.Team != player.Team || g.Loaded || g.Loading) continue;
                    if (Vector3.Distance(g.transform.position, player.transform.position) > 7f) continue;
                    Prompt = $"[{ReloadKey}] Зарядить орудие (5 с)";
                    if (player.Input.reloadPressed) g.StartPlayerLoad(player);
                    return;
                }
            }
            foreach (var g in ArtilleryGun.All)
                if (g && g.Loading && g.Team == player.Team && Vector3.Distance(g.transform.position, player.transform.position) < 8f)
                { Prompt = $"Заряжание орудия… {g.LoadProgress * 100f:0}%"; break; }

            // Самолёт на стоянке (свой, на земле, без игрока): бот-пилот уступит место
            Aircraft plane = null; float pd = 6.5f;
            foreach (var a in Aircraft.All)
            {
                if (!a || !a.CanEnter(player)) continue;
                float d = Vector3.Distance(a.transform.position, pos);
                if (d < pd) { pd = d; plane = a; }
            }
            if (plane)
            {
                Prompt = $"[{Key}] Сесть в самолёт {plane.DisplayName}";
                if (press) { _cooldown = Time.time + 0.6f; plane.EnterPlayer(player); }
                return;
            }

            // Артиллерийское орудие (своё, целое, без игрока): бот-наводчик уступит место
            ArtilleryGun artGun = null; float gd = 3.2f;
            foreach (var g in ArtilleryGun.All)
            {
                if (!g || !g.CanEnter(player)) continue;
                float d = Mathf.Min(Vector3.Distance(g.SeatPos, player.transform.position), Vector3.Distance(g.transform.position, player.transform.position) - 1f);
                if (d < gd) { gd = d; artGun = g; }
            }
            if (artGun)
            {
                Prompt = $"[{Key}] Встать к орудию {artGun.DisplayName}";
                if (press) { _cooldown = Time.time + 0.6f; artGun.EnterPlayer(player); }
                return;
            }

            Tank best = null; float bd = 4.2f;
            foreach (var t in Tank.All)
            {
                if (!t || !t.Alive || t.Team != player.Team || t.PlayerAboard) continue;
                if (t.IsLight ? !t.AnySeatFree : t.crew == Tank.Crew.Player) continue;
                float d = t.DistanceTo(pos);
                if (d < bd) { bd = d; best = t; }
            }
            if (best)
            {
                if (best.IsLight)
                {
                    // Выбор места: E — за руль, X — за пулемёт
                    bool drv = best.SeatFree(JeepSeat.Driver), gun = best.SeatFree(JeepSeat.Gunner);
                    Prompt = (drv ? $"[{Key}] За руль   " : "") + (gun ? $"[{(drv ? SeatKey : Key)}] За пулемёт" : "") + $"  ·  {best.DisplayName}";
                    bool pressSeat = player.Input.seatPressed && Time.time > _cooldown;
                    if (press) { _cooldown = Time.time + 0.6f; VehicleManager.EnterPlayer(best, player, drv ? JeepSeat.Driver : JeepSeat.Gunner); }
                    else if (pressSeat && drv && gun) { _cooldown = Time.time + 0.6f; VehicleManager.EnterPlayer(best, player, JeepSeat.Gunner); }
                }
                else
                {
                    Prompt = $"[{Key}] Сесть в {best.DisplayName}";
                    if (press) { _cooldown = Time.time + 0.6f; VehicleManager.EnterPlayer(best, player); }
                }
                return;
            }

            // Оружие на земле, которое не подобрать: слот занят другим
            var blocked = WeaponPickups.BlockedNear(player.transform.position, player.GetComponent<WeaponController>());
            if (blocked)
            {
                string drop = GameInput.Initialized ? GameInput.FindAction("DropWeapon").GetBindingDisplayString(0) : "G";
                Prompt = $"{blocked.displayName}: [{drop}] бросьте своё оружие и подойдите — подберётся само";
            }

            var crate = AmmoCrate.Nearest(pos, 2.8f);
            if (crate)
            {
                Prompt = crate.Ready ? $"[{Key}] Пополнить боеприпасы" : $"Ящик пуст — {Mathf.CeilToInt(crate.Cooldown)} с";
                if (press && crate.Ready) { _cooldown = Time.time + 0.6f; crate.Use(player); }
            }
        }
    }
}
