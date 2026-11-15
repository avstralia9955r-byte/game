using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace Frontline
{
    /// <summary>
    /// Перевод игры на английский. Тексты в коде написаны по-русски; при выбранном английском языке строка перед
    /// показом проходит через Loc.T: известные фразы заменяются целиком (самые длинные — первыми, только по границам
    /// слов), оставшаяся кириллица (фамилии) транслитерируется. Результаты кэшируются.
    /// IMGUI-HUD вызывает T в своих функциях вывода; тексты UGUI (меню, сенсорные кнопки) переводит LocalizeUGUI.
    /// </summary>
    public static class Loc
    {
        public static bool English => SaveSystem.Settings != null && SaveSystem.Settings.language == 1;

        static Regex _rx;
        static Dictionary<string, string> _map;
        static readonly Dictionary<string, string> _cache = new();
        static readonly Regex _cyr = new("[А-Яа-яЁё]", RegexOptions.Compiled);

        public static string T(string s)
        {
            if (string.IsNullOrEmpty(s) || !English || !_cyr.IsMatch(s)) return s;
            if (_cache.TryGetValue(s, out var r)) return r;
            if (_rx == null) Build();
            r = _rx.Replace(s, m => _map.TryGetValue(m.Value, out var e) ? e : m.Value);
            if (_cyr.IsMatch(r)) r = Translit(r);
            if (_cache.Count > 6000) _cache.Clear();
            _cache[s] = r;
            return r;
        }

        static void Build()
        {
            _map = new Dictionary<string, string>();
            for (int i = 0; i + 1 < Pairs.Length; i += 2) _map[Pairs[i]] = Pairs[i + 1];
            var keys = new List<string>(_map.Keys);
            keys.Sort((a, b) => b.Length.CompareTo(a.Length));
            var sb = new StringBuilder("(?<![А-Яа-яЁёA-Za-z])(?:");
            for (int i = 0; i < keys.Count; i++) { if (i > 0) sb.Append('|'); sb.Append(Regex.Escape(keys[i])); }
            sb.Append(")(?![А-Яа-яЁёA-Za-z])");
            _rx = new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        static readonly Dictionary<char, string> Tr = new()
        {
            {'а',"a"},{'б',"b"},{'в',"v"},{'г',"g"},{'д',"d"},{'е',"e"},{'ё',"yo"},{'ж',"zh"},{'з',"z"},{'и',"i"},{'й',"y"},
            {'к',"k"},{'л',"l"},{'м',"m"},{'н',"n"},{'о',"o"},{'п',"p"},{'р',"r"},{'с',"s"},{'т',"t"},{'у',"u"},{'ф',"f"},
            {'х',"kh"},{'ц',"ts"},{'ч',"ch"},{'ш',"sh"},{'щ',"shch"},{'ъ',""},{'ы',"y"},{'ь',""},{'э',"e"},{'ю',"yu"},{'я',"ya"}
        };

        public static string Translit(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                char lo = char.ToLowerInvariant(ch);
                if (Tr.TryGetValue(lo, out var t))
                {
                    if (ch != lo && t.Length > 0) t = char.ToUpperInvariant(t[0]) + t.Substring(1);
                    sb.Append(t);
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>Пары «русский → английский».</summary>
        static readonly string[] Pairs =
        {
            // ---- Главное меню и общие ----
            "НОВАЯ ИГРА","NEW GAME", "ПОДКЛЮЧИТЬСЯ К ИГРЕ","JOIN GAME", "КАСТОМИЗАЦИЯ","CUSTOMIZATION", "НАСТРОЙКИ","SETTINGS", "ВЫХОД","QUIT",
            "ЗАКРЫТЬ","CLOSE", "СТАРТ","START", "← НАЗАД","← BACK", "ПРОДОЛЖИТЬ","CONTINUE", "В ГЛАВНОЕ МЕНЮ","MAIN MENU", "В МЕНЮ","MENU",
            "ПАУЗА","PAUSE", "Деньги","Money", "деньги","money", "Матчей","Matches", "Побед","Wins", "Убийств","Kills", "убийств","kills", "Смертей","Deaths", "смертей","deaths",
            "Выберите карту, длительность и число ботов. Сторону и класс — уже в матче, на экране высадки","Choose a map, duration and number of bots. Side and class are picked in the match, on the deploy screen",
            "Генерация карты 1×1 км","Generating 1×1 km map", "Рельеф, леса, деревни, бокаж, навигация ботов","Terrain, forests, villages, bocage, bot navigation",
            "Длительность матча","Match duration", "Количество ботов","Number of bots", "Количество танков","Number of tanks", "Количество самолётов","Number of planes", "Количество орудий","Number of guns",
            "экипаж — боты","crews are bots", "пилоты — боты или вы","pilots are bots or you", "гаубицы за базой","howitzers behind base",
            "Режим","Mode", "Захват точек","Capture points", "Командный бой","Team deathmatch", "Сложность ботов","Bot difficulty",
            "Лёгкая","Easy", "Средняя","Medium", "Сложная","Hard", "Реалистичная","Realistic", "Время суток","Time of day", "День","Day", "Вечер","Evening",
            "Сетевая игра","Network game", "Нет (один","No (solo", "Открыть в локальной сети","Host on local network",
            "Нормандия","Normandy", "Арденны","Ardennes", "Ржев: окопы","Rzhev: trenches", "Ржев","Rzhev",
            "Зима","Winter", "Лето","Summer", "Осень 1942","Autumn 1942", "ели, бункеры, замёрзшая","spruces, bunkers, frozen", "река, снег. 1×1 км","river, snow. 1×1 km",
            "три линии окопов","three trench lines", "у каждой стороны, воронки","for each side, craters", "блиндажи, колючка, дождь","dugouts, barbed wire, rain",
            "изгороди, фермы, церковь","hedges, farms, church", "окопы, река с мостами. 1×1 км","trenches, river with bridges. 1×1 km",
            // ---- Сеть ----
            "ИГРА ПО СЕТИ","NETWORK GAME", "ПОДКЛЮЧИТЬСЯ ›","CONNECT ›", "ПОДКЛЮЧИТЬСЯ","CONNECT", "Найденные игры","Games found", "Поиск игр в локальной сети","Searching LAN games",
            "Поиск игр недоступен","Game search unavailable", "Или адрес хоста (IP","Or host address (IP", "Введите адрес хоста или выберите игру из списка","Enter host address or pick a game from the list",
            "Подключение к","Connecting to", "Подключено","Connected", "Не удалось подключиться","Could not connect", "Хост не отвечает. Проверьте адрес и брандмауэр (UDP 27015","Host not responding. Check the address and firewall (UDP 27015",
            "Связь с хостом потеряна","Connection to host lost", "Хост завершил игру","Host ended the game", "адрес не найден","address not found", "Игра открыта в локальной сети","Game is open on the local network",
            "Кооператив по кабелю или в одной сети Wi-Fi. Хост: «Новая игра» → «Сетевая игра: открыть","Co-op over cable or the same Wi-Fi. Host: «New game» → «Network game: open",
            "Остальные подключаются здесь. Порты UDP 27015–27016 (разрешите игру в брандмауэре","Others join here. UDP ports 27015–27016 (allow the game in your firewall",
            "второй игрок","second player", "игроков","players", "Игрок","Player", "Хост","Host", "Порт","Port", "Офлайн","Offline", "офлайн-сборка","offline build",
            // ---- Настройки ----
            "Сохраняются автоматически","Saved automatically", "Игра","Game", "Звук","Sound", "ЗВУК","SOUND", "Управление","Controls", "УПРАВЛЕНИЕ","CONTROLS", "ГРАФИКА","GRAPHICS",
            "ИГРА И ЗВУК","GAME & SOUND", "КАМЕРА И ЭФФЕКТЫ","CAMERA & EFFECTS", "Камера и эффекты","Camera & effects", "Качество и производительность","Quality & performance",
            "Инерция обзора","Look inertia", "Вкл (тяжёлая","On (heavy", "Выкл (резкая","Off (snappy", "Общая громкость","Master volume", "Музыка в меню","Menu music", "Музыка","Music",
            "Чувствительность мыши","Mouse sensitivity", "Чувствит. в прицеле","ADS sensitivity", "Чувствительность в прицеле","ADS sensitivity", "Инверсия Y","Invert Y", "Инверсия по вертикали","Invert vertical",
            "Выкл","Off", "Вкл","On", "ВЫКЛ","OFF", "ВКЛ","ON", "нажмите клавишу","press a key", "СБРОСИТЬ КЛАВИШИ","RESET KEYS",
            "Огонь","Fire", "Прицел","Aim", "Перезарядка","Reload", "Прыжок","Jump", "Присесть","Crouch", "Бег","Sprint", "Строительство","Build", "Вращение ножа","Knife spin",
            "Удар ножом","Knife attack", "Граната","Grenade", "Осмотр оружия","Inspect weapon", "Действие / машина","Use / vehicle", "Бросить оружие","Drop weapon", "Бинокль","Binoculars",
            "Вид 1-е / 3-е лицо","1st / 3rd person view", "Пересесть в машине","Switch seat",
            "Автоподбор графики","Auto graphics", "Желаемый FPS","Target FPS", "Пресет","Preset", "Низкий","Low", "Средний","Medium", "Высокий","High", "Ультра","Ultra",
            "Пресет задаёт все параметры сразу; любой можно поменять отдельно. Сохраняется автоматически","The preset sets everything at once; any option can be changed separately. Saved automatically",
            "Тени","Shadows", "Сглаживание","Anti-aliasing", "Дальность прорисовки","View distance", "Плотность травы","Grass density", "Густота травы","Grass density", "Текстуры","Textures",
            "Масштаб рендера","Render scale", "Динамическое разрешение","Dynamic resolution", "Динам. разрешение","Dynamic res.", "Вертикальная синхронизация","Vertical sync", "Верт. синхронизация","VSync",
            "Поле зрения (по горизонтали","Field of view (horizontal", "Поле зрения (гориз","FOV (horiz", "Затенение (SSAO","Ambient occlusion (SSAO", "Свечение (bloom","Bloom (bloom",
            "Размытие движения","Motion blur", "Зерно плёнки","Film grain", "Хроматическая аберрация","Chromatic aberration", "Хроматизм","Chromatic", "Виньетка","Vignette",
            "Рыбий глаз","Fisheye", "Нагрудная камера (bodycam","Body camera (bodycam", "Нагрудная","Bodycam", "Низкие","Low", "Средние","Medium", "Высокие","High", "Полные","Full",
            "Половина","Half", "Близко","Near", "Средне","Medium", "Далеко","Far", "Слабый","Weak", "Сильный","Strong", "Обычная","Normal", "Язык","Language", "Русский","Russian",
            "Сенсор: чувствительность обзора","Touch: look sensitivity", "Клавиши переназначаются в главном меню → НАСТРОЙКИ","Keys are rebound in main menu → SETTINGS",
            // ---- Кастомизация и магазин ----
            "КЛАССЫ","CLASSES", "Штурмовик","Assault", "ШТУРМОВИК","ASSAULT", "Снайпер","Recon", "СНАЙПЕР","RECON", "Инженер","Engineer", "ИНЖЕНЕР","ENGINEER", "Поддержка","Support", "ПОДДЕРЖКА","SUPPORT",
            "Артиллерист","Artillerist", "Танкист","Tanker", "АРТИЛЛЕРИСТ","ARTILLERIST", "ТАНКИСТ","TANKER",
            "промах у орудия 5% (без — 60","gun miss chance 5% (without — 60", "по вашему танку попадают в 70% (без — 95","your tank is hit 70% of the time (without — 95",
            "открыт","unlocked", "закрыт","locked", "ЗАКРЫТ","LOCKED", "купить","buy", "КУПИТЬ","BUY", "КУПИТЬ КЛАСС","BUY CLASS", "куплено","bought", "куплен","owned", "Не куплен","Not owned", "не куплен","not owned",
            "Класс закрыт. После покупки можно покупать его оружие","Class locked. After buying it you can buy its weapons", "Оружие не куплено","Weapon not owned",
            "ВЗЯТЬ В СЛОТ","EQUIP", "★ В СЛОТЕ","★ EQUIPPED", "в слоте","equipped", "★ — оружие класса в бою","★ — class weapon in battle", "Обвесы","Attachments", "УСТАНОВИТЬ","INSTALL", "СНЯТЬ","REMOVE", "установлен","installed",
            "ОСНОВНОЕ","PRIMARY", "ВТОРОСТЕПЕННОЕ","SECONDARY", "НОЖ","KNIFE", "ПРОТИВОТАНК","ANTI-TANK", "противотанк","anti-tank", "СТОРОНА","SIDE",
            "перетаскивайте модель для осмотра","drag the model to inspect", "Деньги — за матчи: убийства, победы, танки","Money is earned in matches: kills, wins, tanks",
            "Бинокль 6× (клавиша Z","Binoculars 6× (key Z", "Может купить бинокль","Can buy binoculars", "Увеличение 6×. Враг в центре бинокля","6× zoom. The enemy in the center of the binoculars",
            "отмечается для вас на 8 секунд — удобно","is marked for you for 8 seconds — handy", "искать танки и пулемётные гнёзда","for finding tanks and MG nests",
            "В бою — клавиша Z (или кнопка «БИНОКЛЬ» на телефоне","In battle — key Z (or the «BINOCULARS» button on mobile", "БИНОКЛЬ 6","BINOCULARS 6", "БИНОКЛЬ","BINOCULARS",
            "1 автомат или самозарядная винтовка","1 assault rifle or semi-auto rifle", "1 снайперская винтовка","1 sniper rifle", "1 пистолет-пулемёт","1 submachine gun", "1 пулемёт","1 machine gun",
            "2 пистолет или лёгкий ПП","2 pistol or light SMG", "3 нож. Работает с дальней дистанции","3 knife. Works at long range",
            "3 на выбор: шпатель (укрепления), гаечный ключ","3 to choose: trowel (fortifications), wrench", "ремонт техники) или нож","vehicle repair) or knife",
            "4 противотанковое (фаустпатрон / ПТРД","4 anti-tank (Panzerfaust / PTRD", "Для поддержки (противотанковое оружие","For support (anti-tank weapon",
            "Основное оружие не куплено — купите в главном меню или подберите с поля боя","Primary weapon not owned — buy it in the main menu or pick one up on the battlefield",
            "купить в главном меню (Кастомизация","buy in main menu (Customization", "ПОЛУЧИТЬ (достижение","GET (achievement",
            "Шпатель (стройка","Trowel (building", "Гаечный ключ","Wrench", "Оптика 1,5× (разведчика","Optic 1.5× (scout", "Оптика 2","Optic 2", "Оптика 4","Optic 4",
            "Лёгкая оптика с большим выносом: прицеливание почти как с открытым прицелом","Light long-eye-relief optic: aiming almost like iron sights",
            "Гасит дульную вспышку — стрелка труднее заметить; чуть меньше отдача","Hides muzzle flash — the shooter is harder to spot; slightly less recoil",
            "Отдача заметно меньше, но разброс от бедра и вскидывание хуже","Much less recoil, but worse hip spread and ADS speed",
            "Штык на стволе: точнее от бедра, но медленнее вскидывание","Bayonet: more accurate from the hip but slower ADS", "Ёмкость ×2,2, но оружие тяжелее: медленнее прицеливание","Capacity ×2.2 but heavier: slower aiming",
            "Глушитель","Suppressor", "Пламегаситель","Flash hider", "Дульный тормоз","Muzzle brake", "Примкнутый штык","Fixed bayonet", "Барабанный магазин","Drum magazine",
            "Улучшенный магазин","Extended magazine", "Тактическая рукоятка","Tactical grip", "Лазерный целеуказатель","Laser sight", "Сошки","Bipod", "Магазин","Magazine",
            "Урон","Damage", "Темп","Rate", "в/мин","rpm", "Дальность","Range", "Разброс","Spread", "Разброс от бедра","Hip spread", "Отдача","Recoil", "Отдача (верт","Recoil (vert", "Прицеливание, с","ADS time, s", "Слышимость, м","Audible range, m",
            // ---- Оружие ----
            "Мосин обр. 1891/30","Mosin M1891/30", "Мосин-Наган","Mosin-Nagant", "Карабин обр. 1938","Carbine M1938", "Карабин обр. 1944","Carbine M1944", "Автомат Фёдорова","Fedorov Avtomat",
            "Штык-нож S84/98","Bayonet S84/98", "Штык-нож СВТ","SVT bayonet", "MG 30 (Солотурн","MG 30 (Solothurn", "MP 35 (Бергманн","MP 35 (Bergmann", "Вальтер","Walther", "Наган","Nagant",
            "ППШ-41","PPSh-41", "ППС-43","PPS-43", "ППД-40","PPD-40", "ППД-34/38","PPD-34/38", "СВТ-40","SVT-40", "АВС-36","AVS-36", "АВТ-40","AVT-40", "ДП-28","DP-28", "ДПМ","DPM", "ДТ","DT",
            "РП-46","RP-46", "ПТРД-41","PTRD-41", "СКС","SKS", "ТТ","TT", "Шпатель","Trowel",
            // ---- Техника ----
            "Т-34-85","T-34-85", "Kübelwagen с MG-34","Kübelwagen with MG-34", "ГАЗ-64 с ДП","GAZ-64 with DP", "Як-9","Yak-9", "122-мм гаубица М-30","122 mm howitzer M-30", "ДШК (зенитный","DShK (AA",
            "экипаж Pz.IV","Pz.IV crew", "экипаж Т-34","T-34 crew", "водитель","driver", "пулемётчик","gunner", "пассажир","passenger", "место","seat", "МЕСТО","SEAT", "занят","taken", "пусто","empty",
            "Сесть в самолёт","Board plane", "Выйти из самолёта","Leave plane", "Выпрыгнуть с парашютом","Bail out with parachute", "Встать к орудию","Man the gun", "Отойти от орудия","Leave the gun",
            "Выйти из машины","Leave vehicle", "Сесть в","Enter", "Встать в","Ride in", "За руль","Drive", "За пулемёт","Man MG", "Пересесть","Switch seat", "Выйти","Exit",
            "Позвать бойца на свободное место","Call a soldier to the free seat", "боец идёт","soldier coming", "Пополнить боеприпасы","Resupply ammo", "Ящик пуст","Crate empty",
            "бросьте своё оружие и подойдите — подберётся само","drop your weapon and walk over — it will be picked up automatically",
            "Танки — у баз: подойдите и нажмите E. Ящики с патронами — на базах и точках","Tanks are at the bases: walk up and press E. Ammo crates are at bases and points",
            "1] ПУШКА","1] CANNON", "2] СПАРЕННЫЙ ПУЛЕМЁТ","2] COAXIAL MG", "ПУЛЕМЁТ НА ТУРЕЛИ","PINTLE MG", "ЗА ПУЛЕМЁТОМ — БОЕЦ","SOLDIER ON THE MG", "снарядов","shells",
            "Броня","Armor", "Скорость","Speed", "Высота","Altitude", "Газ","Throttle", "Прочность","Integrity", "Орудие","Gun", "Возвышение","Elevation", "промах","miss", "ГОТОВО","READY", "Заряжание","Loading",
            "ПУЛЕМЁТЫ","MACHINE GUNS", "РАКЕТЫ","ROCKETS", "ЛКМ","LMB", "ПКМ","RMB",
            "WASD — движение","WASD — move", "мышь — башня","mouse — turret", "ЛКМ — огонь","LMB — fire", "ПКМ — прицел","RMB — aim", "1/2 — оружие","1/2 — weapon", "E — выход","E — exit",
            "мышь — пулемёт","mouse — machine gun", "X — пересесть","X — switch seat", "мышь — обзор","mouse — look", "ЛКМ — пулемёт (если нет стрелка","LMB — MG (if no gunner",
            "W/S — газ","W/S — throttle", "A/D — руль","A/D — rudder", "мышь — тангаж и крен","mouse — pitch and roll", "ЛКМ — пулемёты","LMB — machine guns", "ПКМ — ракеты","RMB — rockets",
            "F5 — вид","F5 — view", "парашют","parachute", "выход","exit", "мышь — наводка","mouse — aim", "ЛКМ — выстрел","LMB — fire", "E — отойти","E — step away",
            "ПКМ (держать) — карта для огня по точке","RMB (hold) — map for point fire", "мышь — курсор на карте","mouse — map cursor", "ЛКМ — огонь по точке","LMB — fire at point",
            "отпустите ПКМ — ручная наводка","release RMB — manual aiming", "ЛКМ — огонь (только по самолётам","LMB — fire (aircraft only", "ПКМ — приближение","RMB — zoom", "жёлтый круг — упреждение","yellow circle — lead",
            "специализация «Артиллерист» (8000) — промах 5","«Artillerist» specialization (8000) — miss 5", "специализация «Артиллерист»: промах 5","«Artillerist» specialization: miss 5",
            "у своей базы на земле — пополнение и ремонт","on the ground at your base — rearm and repair", "на стоянке у базы: пополнение и ремонт за 3 с","parked at base: rearm and repair in 3 s",
            "РАЗБЕГ… отрыв от земли с 110 км/ч — потяните мышь на себя","TAKEOFF RUN… lift off at 110 km/h — pull the mouse back",
            "СВАЛИВАНИЕ — опустите нос и дайте газ","STALL — lower the nose and add throttle", "ЗЕМЛЯ! ВЫРАВНИВАЙТЕ","GROUND! PULL UP", "ВНЕ ЗОНЫ БОЯ — РАЗВОРАЧИВАЙТЕСЬ","OUT OF BOUNDS — TURN BACK",
            "ЗАХВАТ","LOCK", "РИКОШЕТ","RICOCHET", "заряжание","loading", "ЛКМ — ОГОНЬ","LMB — FIRE", "полёт","flight", "км/ч","km/h",
            // ---- HUD ----
            "СССР","USSR", "ГЕРМАНИЯ","GERMANY", "Германия","Germany", "ПОБЕДА","VICTORY", "ПОРАЖЕНИЕ","DEFEAT", "НИЧЬЯ","DRAW", "СВОДКА БОЯ","BATTLE REPORT",
            "Высадка через","Deploying in", "Автовысадка через","Auto-deploy in", "ВЫСАДИТЬСЯ","DEPLOY", "ВЫСАДКА","DEPLOY", "ВЫБОР СТОРОНЫ И КЛАССА","CHOOSE SIDE AND CLASS",
            "ЗАЩИТА ПОСЛЕ ПОЯВЛЕНИЯ","SPAWN PROTECTION", "ВАС УБИЛ","KILLED BY", "МОИ УБИЙСТВА","MY KILLS", "в голову","headshot", "В ГОЛОВУ","HEADSHOT", "УБИТ","KILLED",
            "ТЕХНИКА УНИЧТОЖЕНА","VEHICLE DESTROYED", "САМОЛЁТ СБИТ","PLANE SHOT DOWN", "ОРУДИЕ УНИЧТОЖЕНО","GUN DESTROYED", "Крушение","Crash",
            "ТОЧКА ОСПАРИВАЕТСЯ","POINT CONTESTED", "ТОЧКА НАША","POINT SECURED", "НЕЙТРАЛИЗАЦИЯ","NEUTRALIZING", "на точке","on point",
            "НЕТ ПАТРОНОВ","NO AMMO", "R] ПЕРЕЗАРЯДКА","R] RELOAD", "Гранаты","Grenades", "ГРАНАТА","GRENADE", "Граната","Grenade", "бросить оружие [G","drop weapon [G", "вид [F5","view [F5",
            "1 ОСН","1 PRI", "2 ПИСТ","2 PIST", "2 ПП","2 SMG", "3 НОЖ","3 KNIFE", "3 нож","3 knife", "3 КЛЮЧ","3 WRENCH", "3 СТРОЙ","3 BUILD", "4 СТРОЙ","4 BUILD", "4 ПТ","4 AT",
            "ШПАТЕЛЬ — СТРОИТЕЛЬСТВО","TROWEL — BUILDING", "ИНСТРУМЕНТ","TOOL", "Кирпич","Brick", "держите ЛКМ","hold LMB", "ремонт","repair", "исправна","intact",
            "Убийств за матч: Германия","Kills this match: Germany", "Германия: убийств","Germany: kills", "СССР: убийств","USSR: kills", "победа по времени","win on time", "по убийствам","by kills",
            "ОДИН","SINGLE", "АВТО","AUTO", "ЗАТВОР","BOLT", "ПЕРЕЗ","RELOAD", "ОГОНЬ","FIRE", "ПРИЦЕЛ","AIM", "ПРЫЖОК","JUMP", "ПРИСЕСТЬ","CROUCH", "БРОСИТЬ","DROP", "ВИД","VIEW",
            "Награда","Reward", "Танков","Tanks", "танков","tanks", "ботов","bots", "Боец","Soldier", "на базе","at base", "в строю","in formation", "БАЗА","BASE", "Вы","You",
            "Tab сводка","Tab scoreboard", "Esc пауза","Esc pause", "G граната","G grenade", "Shift бег","Shift sprint", "Ctrl присед","Ctrl crouch", "F флип ножа","F knife flip",
            "B стройка","B build", "V удар","V melee", "T осмотр","T inspect", "E действие/танк","E use/tank", "1–5 слоты","1–5 slots",
            "Не хватает","Not enough", "сменить","change", "пистолет","pistol", "автомат","rifle", "пулемёт","machine gun", "снайп","sniper", "штурм","assault",
            "диагностика вида от первого лица","first-person view diagnostics", "Строй армий","Army formation", "Сплошная линия по всей ширине поля, наступает вместе","A continuous line across the field, advancing together",
            "Колонны по 4 в ряд, каждый полк — свой коридор и свой темп","Columns of 4, each regiment has its own lane and pace", "широкая полоса","wide line", "отдельные полки","separate regiments",
            "мин","min", "м","m", "с","s", "до 512 ботов","up to 512 bots", "Нормандия, Арденны и Ржев 1×1 км","Normandy, Ardennes and Rzhev 1×1 km",
            // ---- Артиллерия: снаряды и наводка ----
            "ГОТОВ К ВЫСТРЕЛУ","READY TO FIRE", "НАВОДИТСЯ","AIMING", "ЗАРЯЖАНИЕ","LOADING", "ЗАРЯДИТЬ ОРУДИЕ","LOAD THE GUN",
            "НЕТ СНАРЯДА — принесите из кучи за орудиями","NO SHELL — bring one from the pile behind the guns",
            "СНАРЯД В РУКАХ — отнесите к орудию и нажмите","CARRYING A SHELL — take it to a gun and press",
            "Взять снаряд","Take a shell", "в куче","in pile", "Снарядов нет — ждите грузовик снабжения","No shells — wait for the supply truck",
            "Зарядить орудие (5 с","Load the gun (5 s", "Заряжание орудия","Loading the gun", "куда упадёт снаряд","shell impact point",
            "без ограничения","unlimited", "2 cm Flak 38","2 cm Flak 38", "10,5 cm leFH 18","10.5 cm leFH 18",
            // ---- Аптечка, мешки с песком, тяжёлое ранение, заряжающий, разгрузка ----
            "АПТЕЧКА","MEDKIT", "МЕШКИ С ПЕСКОМ","SANDBAGS", "4 АПТЕЧКА","4 MEDKIT", "4 МЕШКИ","4 SANDBAGS",
            "ТЯЖЕЛО РАНЕН — ползите в укрытие, ждите аптечку штурмовика","CRITICALLY WOUNDED — crawl to cover, wait for an assault medkit",
            "Аптечки кончились — пополните у ящика с боеприпасами","Out of medkits — refill at an ammo crate",
            "Подойдите к тяжелораненому (лежит) — или лечите себя, когда ранены","Go to a downed soldier — or heal yourself when wounded",
            "Держите ЛКМ — перевязать себя","Hold LMB — bandage yourself", "Держите ЛКМ — поднять раненого","Hold LMB — revive the wounded",
            "Мешки кончились — пополните у ящика с боеприпасами","Out of sandbags — refill at an ammo crate",
            "Держите ЛКМ — уложить мешки","Hold LMB — place sandbags", "Здесь не уложить — нужна ровная земля перед вами","Can't place here — need flat ground in front",
            "Ствол 60°","Barrel 60°", "дальность","range", "мышь ←→ — поворот, ↑↓ — дальность","mouse ←→ — traverse, ↑↓ — range", "ЛКМ — выстрел","LMB — fire", "ПКМ — карта","RMB — map", "J — заряжающий","J — loader", "E — отойти","E — leave",
            "Нанять заряжающего","Hire a loader", "монет","coins", "у вас","you have", "заряжающий нанят","loader hired",
            "Взять снаряд из грузовика","Take a shell from the truck", "Положить снаряд в кучу","Put the shell on the pile",
            "4 два мешка с песком (ставятся за 3 с, урон сквозь них ×¼).","4 two sandbags (placed in 3 s, damage through them ×¼).",
            "4 аптечка: поднимает тяжелораненых (+50 HP).","4 medkit: revives the critically wounded (+50 HP).",
            "3 гаечный ключ (ремонт техники) или нож ·","3 wrench (vehicle repair) or knife ·",
            "Тряска экрана","Screen shake", "Обломки техники (макс.","Vehicle wrecks (max.", "выкл","off", "Z БИНОКЛЬ","Z BINOCULARS", "Z · БИНОКЛЬ","Z · BINOCULARS",
            // ---- Звания и фамилии (немецкие — по-немецки, русские — транслитом) ----
            "Шютце","Schütze", "Обершютце","Oberschütze", "Гефрайтер","Gefreiter", "Обергефрайтер","Obergefreiter", "Унтерофицер","Unteroffizier", "Фельдфебель","Feldwebel",
            "ряд.","Pvt.", "ефр.","Cpl.", "мл. серж.","Jr. Sgt.", "серж.","Sgt.", "ст. серж.","Sr. Sgt.", "мл. серж","Jr. Sgt", "ст. серж","Sr. Sgt", "серж","Sgt", "ефр","Cpl",
            "Шмидт","Schmidt", "Мюллер","Müller", "Шнайдер","Schneider", "Фишер","Fischer", "Вебер","Weber", "Майер","Meyer", "Вагнер","Wagner", "Беккер","Becker", "Шульц","Schulz",
            "Хоффман","Hoffmann", "Шефер","Schäfer", "Кох","Koch", "Бауэр","Bauer", "Рихтер","Richter", "Кляйн","Klein", "Вольф","Wolf", "Шрёдер","Schröder", "Нойман","Neumann",
            "Шварц","Schwarz", "Циммерман","Zimmermann", "Браун","Braun", "Крюгер","Krüger", "Хартман","Hartmann", "Ланге","Lange", "Вернер","Werner", "Кёлер","Köhler", "Кёниг","König", "Хубер","Huber",
        };
    }

    /// <summary>Переводит тексты UGUI (меню, сенсорные кнопки) при выбранном английском — проверка 4 раза в секунду.</summary>
    public class LocalizeUGUI : MonoBehaviour
    {
        float _t;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("[Localize]");
            DontDestroyOnLoad(go);
            go.AddComponent<LocalizeUGUI>();
        }

        void Update()
        {
            if (!Loc.English) return;
            if ((_t -= Time.unscaledDeltaTime) > 0) return;
            _t = Bots.MatchBridge.MatchRunning ? 1f : 0.25f;
            foreach (var t in FindObjectsByType<Text>())
            {
                var s = t.text;
                if (string.IsNullOrEmpty(s)) continue;
                var r = Loc.T(s);
                if (!ReferenceEquals(r, s) && r != s) t.text = r;
            }
        }
    }
}
