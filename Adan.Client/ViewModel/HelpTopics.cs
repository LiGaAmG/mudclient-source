namespace Adan.Client.ViewModel
{
    using System.Collections.Generic;

    public static class HelpTopics
    {
        private const string CatOverview = "Где и как писать скрипты";
        private const string CatEvents = "События (Wait)";
        private const string CatData = "Данные пакетов";
        private const string CatFunctions = "Функции";
        private const string CatControl = "Управление и ограничения";

        public static List<HelpTopic> All = new List<HelpTopic>
        {
            new HelpTopic(CatOverview, "Обзор", new List<HelpContentBlock>
            {
                HelpContentBlock.ForText(
                    "ГДЕ ХРАНЯТСЯ СКРИПТЫ\n\n" +
                    "Все Lua-скрипты лежат в общей папке scripts. Код хранится в файле .lua, а настройки конкретного скрипта — рядом, в .script.json. " +
                    "Скрипт можно назначить выбранным профилям или сделать глобальным для всех текущих и будущих профилей.\n\n" +
                    "Авто-старт запускает назначенный скрипт при подключении профиля. Кнопки Старт и Стоп управляют только текущими запущенными экземплярами."),
                HelpContentBlock.ForText(
                    "ГДЕ ПИСАТЬ СКРИПТЫ\n\n" +
                    "Диалог Scripts (Options -> Scripts, active tab). Список именованных скриптов " +
                    "профиля. Каждый скрипт — это независимая Lua-корутина, которую вы запускаете " +
                    "и останавливаете кнопками Start/Stop. У неё есть имя, код, статус и галочка " +
                    "\"Auto-start on connect\".\n\n" +
                    "Скрипт видит каждую строку текстового вывода через WaitText() и любые " +
                    "структурные пакеты через WaitGroupState()/WaitRoomState()/WaitRoomChange()."),
                HelpContentBlock.ForText(
                    "ОСНОВНАЯ ИДИОМА\n\n" +
                    "Скрипт в диалоге Scripts обычно выглядит как бесконечный цикл, который сам " +
                    "себя приостанавливает в ожидании нужного события:"),
                HelpContentBlock.ForCode(
                    "while true do\n" +
                    "    WaitGroupState()\n" +
                    "    -- читаем __last_group, что-то делаем\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "Вместо одной фиксированной функции-обработчика на тип пакета (как было раньше) " +
                    "теперь сам скрипт решает, чего и в каком порядке ждать: Wait(ms) — подождать " +
                    "по таймеру, WaitGroupState()/WaitRoomState()/WaitRoomChange() — подождать " +
                    "конкретный пакет с сервера. После возврата из любой Wait-функции скрипт читает " +
                    "свежие данные из глобальных таблиц __last_group / __last_room_monsters / " +
                    "__last_room_id / __last_zone_id / __last_room (подробности — в темах ниже)."),
                HelpContentBlock.ForText(
                    "ГЛАВНОЕ УЛУЧШЕНИЕ: НЕТ КОЛЛИЗИЙ\n\n" +
                    "Это и было главной причиной перехода на корутины. Раньше на профиль можно было " +
                    "включить только один скрипт на каждый тип пакета (второй включённый скрипт " +
                    "с тем же обработчиком молча перебивал первый). Теперь сколько угодно скриптов " +
                    "могут одновременно вызывать WaitGroupState() (или любую другую Wait-функцию) — " +
                    "каждый получает свою копию свежих данных и работает независимо от остальных, " +
                    "никто никого не перебивает."),
                HelpContentBlock.ForText(
                    "ОБЩЕЕ СОСТОЯНИЕ\n\n" +
                    "У каждой вкладки (таба) — одно общее, постоянное изолированное состояние Lua. " +
                    "Скрипты из триггеров и корутины из диалога Scripts для одной и той же вкладки " +
                    "выполняются в этом же состоянии и видят одни и те же глобальные переменные. " +
                    "У разных вкладок (персонажей) — разные, не пересекающиеся состояния."),
                HelpContentBlock.ForText(
                    "ПРИМЕНЕНИЕ ПРАВОК\n\n" +
                    "Диалог Scripts — правки кода применяются по кнопке Save или Close. " +
                    "Уже ЗАПУЩЕННЫЕ корутины правки кода на лету не подхватывают — " +
                    "остановите (Stop) и запустите (Start) скрипт заново, чтобы он начал работать " +
                    "по новому коду. Скрипты, код которых не менялся, при закрытии диалога " +
                    "не перезапускаются и продолжают работать без прерывания."),
            }),

            new HelpTopic(CatEvents, "Wait(ms) — ждать по таймеру", new List<HelpContentBlock>
            {
                HelpContentBlock.ForCode("Wait(ms)"),
                HelpContentBlock.ForText(
                    "Приостанавливает ТОЛЬКО этот скрипт (не весь клиент и не другие скрипты) на " +
                    "примерно ms миллисекунд, затем продолжает выполнение со следующей строки.\n\n" +
                    "Пример — повторять команду каждые 5 секунд:"),
                HelpContentBlock.ForCode(
                    "while true do\n" +
                    "    SendCommand(\"осмотреться\")\n" +
                    "    Wait(5000)\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "Возобновление управляется периодическим таймером клиента, а не точным " +
                    "будильником — реальная задержка может быть на ~150мс больше запрошенной " +
                    "(но не меньше). Для точного тайминга в духе \"раунд боя\" это не годится — " +
                    "используйте WaitGroupState()/WaitRoomState() вместо подбора Wait(ms)."),
            }),

            new HelpTopic(CatEvents, "WaitGroupState() — ждать пакет группы", new List<HelpContentBlock>
            {
                HelpContentBlock.ForCode("WaitGroupState()"),
                HelpContentBlock.ForText(
                    "Приостанавливает скрипт до следующего пакета состояния группы (тип 12) — " +
                    "то есть до момента, когда меняется HP/состояние кого-то из группы. Когда " +
                    "WaitGroupState() возвращается, сразу читайте __last_group — там свежие данные " +
                    "именно этого пакета.\n\n" +
                    "Пример — автооповещение о низком HP:"),
                HelpContentBlock.ForCode(
                    "while true do\n" +
                    "    WaitGroupState()\n" +
                    "    for i = 1, #__last_group do\n" +
                    "        if __last_group[i].HitsPercent < 30 then\n" +
                    "            SendCommand(\"ооц низкое HP: \" .. __last_group[i].Name)\n" +
                    "        end\n" +
                    "    end\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "Несколько разных скриптов могут одновременно сидеть в WaitGroupState() — " +
                    "каждый получит то же __last_group и продолжит независимо от остальных."),
            }),

            new HelpTopic(CatEvents, "WaitRoomState() — ждать пакет мобов комнаты", new List<HelpContentBlock>
            {
                HelpContentBlock.ForCode("WaitRoomState()"),
                HelpContentBlock.ForText(
                    "Приостанавливает скрипт до следующего пакета с мобами текущей комнаты " +
                    "(тип 13) — примерно раз за боевой раунд, либо при заходе/уходе моба. После " +
                    "возврата сразу читайте __last_room_monsters.\n\n" +
                    "Пример — оповестить о появлении босса:"),
                HelpContentBlock.ForCode(
                    "while true do\n" +
                    "    WaitRoomState()\n" +
                    "    for i = 1, #__last_room_monsters do\n" +
                    "        if __last_room_monsters[i].IsBoss then\n" +
                    "            SendCommand(\"ооц босс в комнате: \" .. __last_room_monsters[i].Name)\n" +
                    "        end\n" +
                    "    end\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "Как и с WaitGroupState() — несколько скриптов могут ждать WaitRoomState() " +
                    "одновременно, без коллизий друг с другом."),
            }),

            new HelpTopic(CatEvents, "WaitRoomChange() — ждать смену комнаты", new List<HelpContentBlock>
            {
                HelpContentBlock.ForCode("WaitRoomChange()"),
                HelpContentBlock.ForText(
                    "Приостанавливает скрипт до момента, когда сервер подтвердит переход персонажа " +
                    "в новую комнату (тип 14). После возврата читайте __last_room_id, __last_zone_id " +
                    "и __last_room.\n\n" +
                    "__last_room_id/__last_zone_id — это всё, что реально присылает сервер в этом " +
                    "пакете. __last_room клиент достаёт из уже загруженных файлов карты — поэтому " +
                    "работает только для комнат, которые есть на карте. Если комната не размечена — " +
                    "__last_room будет nil, ВСЕГДА проверяйте это перед обращением к полям:"),
                HelpContentBlock.ForCode(
                    "while true do\n" +
                    "    WaitRoomChange()\n" +
                    "    if __last_room ~= nil then\n" +
                    "        SendCommand(\"ооц зона: \" .. __last_room.ZoneName ..\n" +
                    "            \", комната: \" .. __last_room.Name)\n" +
                    "    end\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "Поля __last_room — см. тему \"Поля __last_room (после WaitRoomChange)\"."),
            }),

            new HelpTopic(CatEvents, "WaitText(ms) — ждать строку вывода", new List<HelpContentBlock>
            {
                HelpContentBlock.ForCode("local line = WaitText(timeout_ms)"),
                HelpContentBlock.ForText(
                    "Приостанавливает скрипт до следующей текстовой строки от сервера " +
                    "(любой строки в окне вывода). Возвращает строку как обычную Lua-строку.\n\n" +
                    "timeout_ms — необязательный таймаут в миллисекундах. Если строки нет " +
                    "дольше этого времени, WaitText возвращает nil. При timeout_ms = 0 или " +
                    "без аргумента — ждёт бесконечно.\n\n" +
                    "Обычная идиома — реакция на конкретную строку:"),
                HelpContentBlock.ForCode(
                    "while true do\n" +
                    "    local line = WaitText(0)\n" +
                    "    if line:find(\"Вы огляделись\", 1, true) then\n" +
                    "        -- дальше собираем строки огляда\n" +
                    "    end\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "В строке можно использовать Lua-паттерны (аналог регулярок) через " +
                    "string.find / string.match / string.gmatch:"),
                HelpContentBlock.ForCode(
                    "local line = WaitText(2000)\n" +
                    "if not line then\n" +
                    "    Echo(\"таймаут — строки не было\")\n" +
                    "elseif line:match(\"^%d+H %d+V\") then\n" +
                    "    Echo(\"строка статуса: \" .. line)\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "Пример — реагировать на огляд и собирать мобов по направлениям:"),
                HelpContentBlock.ForCode(
                    "while true do\n" +
                    "    -- ждём строку начала огляда\n" +
                    "    local line\n" +
                    "    repeat\n" +
                    "        line = WaitText(0)\n" +
                    "    until line:find(\"Вы огляделись\", 1, true)\n" +
                    "\n" +
                    "    -- собираем строки огляда до строки статуса\n" +
                    "    local cur = nil\n" +
                    "    while true do\n" +
                    "        local t = WaitText(400)\n" +
                    "        if not t then break end\n" +
                    "        if t:find(\"Вых\", 1, true) and t:find(\">\", 1, true) then break end\n" +
                    "        if t:find(\"На севере\", 1, true) then cur = \"Север\" end\n" +
                    "        -- ... и т.д.\n" +
                    "    end\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "ВАЖНО: WaitText() работает только в корутине (диалог Scripts). " +
                    "В действии триггера вызов WaitText() немедленно падает с ошибкой " +
                    "\"attempt to yield from outside a coroutine\" — используйте скрипты."),
            }),

            new HelpTopic(CatData, "Поля __last_group[i] / __last_room_monsters[i]", new List<HelpContentBlock>
            {
                HelpContentBlock.ForText(
                    "Каждая запись __last_group[i] или __last_room_monsters[i] — таблица с этими " +
                    "полями:"),
                HelpContentBlock.ForCode(
                    "Name           строка    имя\n" +
                    "TargetName     строка    имя для команды атаки/обращения\n" +
                    "Position       строка    поза: Dying, Sleeping, Resting, Sitting,\n" +
                    "                         Fighting, Standing, Riding\n" +
                    "InSameRoom     true/false в одной комнате с вами\n" +
                    "IsAttacked     true/false атакован кем-то прямо сейчас\n" +
                    "HitsPercent    число      процент HP, 0-100\n" +
                    "MovesPercent   число      процент выносливости, 0-100\n" +
                    "MemTime        число      остаток memtime, -1 если нет\n" +
                    "WaitState      число      остаток wait, -1 если нет\n" +
                    "Affects        таблица    баффы/дебаффы, индекс с 1 (см. ниже)"),
                HelpContentBlock.ForText(
                    "Только у __last_room_monsters[i] (этих полей нет у __last_group[i]):"),
                HelpContentBlock.ForCode(
                    "IsPlayerCharacter  true/false   это игрок, а не моб\n" +
                    "IsBoss             true/false   это босс"),
                HelpContentBlock.ForText("Affects[j] — отдельная таблица с полями:"),
                HelpContentBlock.ForCode(
                    "Name      строка   название баффа/дебаффа\n" +
                    "Duration  число    секунд осталось, -1 = бессрочно\n" +
                    "Rounds    число    раундов осталось"),
                HelpContentBlock.ForText("Пример обращения:"),
                HelpContentBlock.ForCode(
                    "__last_group[1].Name\n" +
                    "__last_group[1].Affects[1].Name\n" +
                    "#__last_group[1].Affects   -- сколько баффов всего"),
                HelpContentBlock.ForText(
                    "Подтверждено вживую: ваш собственный персонаж тоже есть в __last_group[] " +
                    "(рядом с петами, если есть) — ищите по __last_group[i].Name == \"ВашеИмя\", " +
                    "так получите и свой HitsPercent."),
            }),

            new HelpTopic(CatData, "Поля __last_room (после WaitRoomChange)", new List<HelpContentBlock>
            {
                HelpContentBlock.ForText(
                    "__last_room — таблица с тем, что клиент уже знает о комнате из своей " +
                    "локальной карты (не от сервера):"),
                HelpContentBlock.ForCode(
                    "ZoneName        строка    название зоны\n" +
                    "Name            строка    название комнаты\n" +
                    "Description     строка    полный текст описания комнаты\n" +
                    "X, Y, Z         числа     координаты комнаты на карте\n" +
                    "Exits           таблица   список выходов, индекс с 1 (см. ниже)\n" +
                    "Alias           строка    ваша личная пометка-название комнаты\n" +
                    "Comments        строка    ваш комментарий к комнате\n" +
                    "HasBeenVisited  true/false вы уже бывали здесь раньше\n" +
                    "HasHerb         true/false здесь отмечен сбор трав\n" +
                    "HerbDangerLevel строка    None, Low, Medium или High"),
                HelpContentBlock.ForText("Exits[j] — отдельная таблица с полями:"),
                HelpContentBlock.ForCode(
                    "Direction  строка  North, South, East, West, Up, Down\n" +
                    "RoomId     число   id комнаты, куда ведёт этот выход"),
                HelpContentBlock.ForText("Пример — вывести все выходы:"),
                HelpContentBlock.ForCode(
                    "for i = 1, #__last_room.Exits do\n" +
                    "    SendCommand(\"ооц выход \" .. __last_room.Exits[i].Direction ..\n" +
                    "        \" -> \" .. __last_room.Exits[i].RoomId)\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "Если комната не размечена на карте — __last_room будет nil целиком, " +
                    "а не таблица с пустыми полями (см. пример проверки в предыдущей теме)."),
            }),

            new HelpTopic(CatFunctions, "Функции: SendCommand(text)", new List<HelpContentBlock>
            {
                HelpContentBlock.ForCode("SendCommand(\"атаковать крысу\")"),
                HelpContentBlock.ForText(
                    "Отправляет текстовую команду серверу — точно так же, как если бы вы её " +
                    "ввели в строку ввода и нажали Enter. Одинаково работает и из скрипта, " +
                    "прикреплённого к триггеру, и из корутины в диалоге Scripts.\n\n" +
                    "Кроме SendCommand, в Lua выставлены ещё несколько функций — переменные, " +
                    "Echo, группы, статус, отправка в другие окна (см. отдельную тему ниже) — " +
                    "и Wait/WaitGroupState/WaitRoomState/WaitRoomChange (см. отдельные темы). " +
                    "Команд на чтение инвентаря, движение по карте и т.п. всё ещё нет — " +
                    "см. \"Чего пока нет\"."),
            }),

            new HelpTopic(CatFunctions, "Функции: переменные, Echo, группы, статус, другие окна", new List<HelpContentBlock>
            {
                HelpContentBlock.ForText("SetVariable / ClearVariable / GetVariable — переменные клиента"),
                HelpContentBlock.ForCode(
                    "SetVariable(\"hpThreshold\", \"30\")\n" +
                    "GetVariable(\"hpThreshold\")\n" +
                    "ClearVariable(\"hpThreshold\")"),
                HelpContentBlock.ForText(
                    "Это НЕ какое-то приватное хранилище Lua — это те же самые переменные " +
                    "профиля, что видны в списке Variables и используются как $hpThreshold " +
                    "в текстовых полях триггеров/алиасов. SetVariable создаёт или обновляет " +
                    "переменную, GetVariable возвращает её текущее значение строкой (или nil, " +
                    "если такой переменной нет), ClearVariable удаляет переменную совсем.\n\n" +
                    "Пример — задать переменную, тут же прочитать и вывести её:"),
                HelpContentBlock.ForCode(
                    "SetVariable(\"hpThreshold\", \"30\")\n" +
                    "local v = GetVariable(\"hpThreshold\")\n" +
                    "Echo(\"hpThreshold = \" .. v)"),
                HelpContentBlock.ForText("Echo(text) — локальный вывод, без сервера"),
                HelpContentBlock.ForCode("Echo(\"скрипт запущен\")"),
                HelpContentBlock.ForText(
                    "Главное отличие от SendCommand: текст просто появляется в окне вывода " +
                    "локально, у вас на экране — сервер его вообще не видит, никакого " +
                    "round-trip не происходит, и это не считается отправленной командой " +
                    "(не тратит ваш ход, не попадает в историю команд на сервере). Удобно для " +
                    "статусных сообщений \"от самого скрипта\", не засоряя при этом сервер:"),
                HelpContentBlock.ForCode(
                    "while true do\n" +
                    "    WaitGroupState()\n" +
                    "    Echo(\"получен пакет состояния группы\")\n" +
                    "end"),
                HelpContentBlock.ForText("EnableGroup / DisableGroup — переключение групп триггеров"),
                HelpContentBlock.ForCode(
                    "EnableGroup(\"Бой\")\n" +
                    "DisableGroup(\"Бой\")"),
                HelpContentBlock.ForText(
                    "Включает или выключает целиком Группу триггеров/алиасов/хоткеев по её " +
                    "имени — те же самые Группы, что видны и редактируются в диалоге Groups. " +
                    "Удобно, чтобы скрипт сам переключал режимы клиента:"),
                HelpContentBlock.ForCode(
                    "SendCommand(\"атаковать крысу\")\n" +
                    "EnableGroup(\"Бой\")\n" +
                    "WaitGroupState()\n" +
                    "DisableGroup(\"Бой\")"),
                HelpContentBlock.ForText("SetStatus(text) — команда #status"),
                HelpContentBlock.ForCode("SetStatus(\"в бою\")"),
                HelpContentBlock.ForText(
                    "Отправляет серверу \"#status в бою\" — то же самое, что если бы вы " +
                    "вручную набрали команду #status с этим текстом в строке ввода."),
                HelpContentBlock.ForText("SendToWindow / SendToAllWindows — другие вкладки"),
                HelpContentBlock.ForCode(
                    "SendToWindow(\"Танк\", \"лечить танк\")\n" +
                    "SendToAllWindows(\"ооц все на месте\")"),
                HelpContentBlock.ForText(
                    "Отправляют текстовую команду не серверу текущей вкладки, а другой " +
                    "открытой вкладке (по имени профиля/персонажа — тому самому имени, что " +
                    "написано на её ярлычке), либо сразу всем открытым вкладкам одновременно " +
                    "(включая ту, что запустила скрипт). Это координация между несколькими " +
                    "одновременно открытыми персонажами.\n\n" +
                    "Пример — лекарь видит низкое HP танка и шлёт ему команду на лечение " +
                    "в его собственную вкладку:"),
                HelpContentBlock.ForCode(
                    "while true do\n" +
                    "    WaitGroupState()\n" +
                    "    for i = 1, #__last_group do\n" +
                    "        if __last_group[i].Name == \"Танк\" and\n" +
                    "           __last_group[i].HitsPercent < 50 then\n" +
                    "            SendToWindow(\"Танк\", \"лечить танк\")\n" +
                    "        end\n" +
                    "    end\n" +
                    "end"),
                HelpContentBlock.ForText(
                    "ОГРАНИЧЕНИЕ: у скрипта нет способа узнать, какие вкладки сейчас вообще " +
                    "открыты и как они называются — имя нужно знать и прописать заранее. Это " +
                    "ровно то же самое ограничение, что уже есть у действия \"отправить в " +
                    "окно\" в редакторе Triggers/Aliases — ничего нового или худшего здесь " +
                    "не появилось."),
            }),

            new HelpTopic(CatFunctions, "Функции: Lower(text)", new List<HelpContentBlock>
            {
                HelpContentBlock.ForCode("local s = Lower(\"Привет МИР\")  --> \"привет мир\""),
                HelpContentBlock.ForText(
                    "Приводит строку к нижнему регистру с учётом Unicode (включая кириллицу). " +
                    "Стандартный string.lower() Lua работает только с ASCII — для кириллических " +
                    "строк используйте именно Lower().\n\n" +
                    "Типичное применение — нормализация имён мобов для сравнения или вывода:"),
                HelpContentBlock.ForCode(
                    "local name = Lower(\"Серый Шакал\")\n" +
                    "-- name == \"серый шакал\""),
            }),

            new HelpTopic(CatControl, "Управление скриптами: Start/Stop/Auto-start", new List<HelpContentBlock>
            {
                HelpContentBlock.ForText(
                    "Диалог Profile Options -> Scripts управляет именованными корутинами профиля."),
                HelpContentBlock.ForCode(
                    "Add               добавить новый скрипт в список\n" +
                    "Delete            удалить выбранный скрипт из списка\n" +
                    "Name              поле имени — просто метка для себя\n" +
                    "Code              редактор кода выбранного скрипта (с подсветкой синтаксиса)\n" +
                    "Auto-start on     если отмечено — этот скрипт запускается сам, когда вкладка\n" +
                    "  connect         подключается к серверу под этим профилем. Это НЕ означает\n" +
                    "                  \"сейчас выполняется\" — это просто настройка \"запускать ли\n" +
                    "                  автоматически при следующем подключении\".\n" +
                    "Status            текущее состояние выбранного скрипта на текущей вкладке:\n" +
                    "                  NotRunning, Running, WaitingOnTimer, WaitingOnGroupState,\n" +
                    "                  WaitingOnRoomState, WaitingOnRoomChange, Finished, Faulted\n" +
                    "Start             запускает скрипт прямо сейчас, независимо от Auto-start\n" +
                    "                  (если он уже был запущен — перезапускает с начала)\n" +
                    "Stop              останавливает скрипт; корутина просто бросается там, где\n" +
                    "                  была — никакой кооперативной очистки/finally нет\n" +
                    "Save              применить код без закрытия окна\n" +
                    "Load...           залить код из .lua-файла (если правили во внешнем редакторе)\n" +
                    "Help              это окно\n" +
                    "Close             применить и закрыть"),
                HelpContentBlock.ForText(
                    "Start/Stop и Status относятся к конкретной вкладке — если у вас несколько " +
                    "вкладок с одним и тем же профилем, скрипт может быть Running на одной и " +
                    "NotRunning на другой."),
            }),

            new HelpTopic(CatControl, "Ограничения песочницы", new List<HelpContentBlock>
            {
                HelpContentBlock.ForText("Доступны только эти стандартные имена Lua:"),
                HelpContentBlock.ForCode(
                    "string, table, math, tostring, tonumber, type, pairs, ipairs,\n" +
                    "select, error, pcall, xpcall, assert, print"),
                HelpContentBlock.ForText(
                    "...плюс SendCommand, Echo, SetVariable/GetVariable/ClearVariable, " +
                    "EnableGroup/DisableGroup, SetStatus, SendToWindow, SendToAllWindows, Lower, " +
                    "Wait, WaitText, WaitGroupState, WaitRoomState, WaitRoomChange " +
                    "(см. отдельные темы), а также ограниченная таблица coroutine — из неё " +
                    "доступны ТОЛЬКО create, resume, yield, status. wrap, close и прочие функции " +
                    "coroutine НЕ доступны.\n\n" +
                    "Намеренно удалены и НЕ могут быть восстановлены из скрипта:"),
                HelpContentBlock.ForCode(
                    "io, os, package, require, debug, dofile, loadfile, load,\n" +
                    "getmetatable, setmetatable"),
                HelpContentBlock.ForText(
                    "Из Lua нет доступа к файловой системе, сети или процессам вообще — " +
                    "это сделано намеренно, не баг и не временное ограничение."),
            }),

            new HelpTopic(CatControl, "Защита от зависания", new List<HelpContentBlock>
            {
                HelpContentBlock.ForText(
                    "Watchdog теперь срабатывает на каждое ВОЗОБНОВЛЕНИЕ (resume) корутины, а не " +
                    "на каждый top-level вызов целиком, как раньше. Каждое такое возобновление " +
                    "ограничено примерно 1 000 000 инструкций виртуальной машины Lua.\n\n" +
                    "Скрипт с бесконечным циклом без единого Wait/WaitGroupState/WaitRoomState/ " +
                    "WaitRoomChange (например, голый while true do end) почти сразу переходит " +
                    "в статус Faulted — вместо зависшего клиента вы просто увидите этот статус " +
                    "в диалоге Scripts.\n\n" +
                    "Скрипт, который регулярно вызывает Wait/WaitGroupState/WaitRoomState/ " +
                    "WaitRoomChange, может крутиться бесконечно сколько угодно — это нормальный, " +
                    "ожидаемый способ использования, а не то, на что реагирует watchdog: каждый " +
                    "отдельный отрезок кода между двумя Wait-вызовами укладывается в бюджет " +
                    "инструкций, и счётчик каждый раз обнуляется заново при возобновлении.\n\n" +
                    "Это работает даже если код обёрнут в pcall — защита не даёт скрипту " +
                    "\"поймать\" свою же ошибку таймаута и продолжить работу."),
            }),

            new HelpTopic(CatControl, "Чего пока нет", new List<HelpContentBlock>
            {
                HelpContentBlock.ForText(
                    "• Только 3 события из 4 структурных пакетов сервера подключены к " +
                    "скриптам: GroupState (тип 12, через WaitGroupState), RoomState " +
                    "(тип 13, через WaitRoomState), RoomChange (тип 14, через " +
                    "WaitRoomChange). Лор предмета (тип 10) — отдельная система (команды " +
                    "лор/лорк, тултипы), к скриптам не подключена.\n\n" +
                    "• Инвентарь и мана — не выставлены в Lua вообще. Свой HP доступен " +
                    "косвенно: ваш персонаж тоже есть в __last_group[i] (подтверждено " +
                    "вживую), ищите по __last_group[i].Name == \"ВашеИмя\".\n\n" +
                    "• Данные о комнате в __last_room (после WaitRoomChange) берутся из " +
                    "ЛОКАЛЬНОЙ карты клиента, не от сервера — для неразмеченных комнат " +
                    "__last_room будет nil.\n\n" +
                    "• Stop у запущенного скрипта не делает кооперативную очистку. Если " +
                    "скрипт был приостановлен в Wait/WaitGroupState/WaitRoomState/ " +
                    "WaitRoomChange и в этот момент его остановили — корутина просто " +
                    "бросается там, где была. Поддержки \"finally\"-блока, который успел " +
                    "бы выполниться при остановке (например, чтобы отправить ещё одну " +
                    "команду в продолжение начатого), нет."),
            }),
        };
    }
}
