# Lua Scripting Help Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat, unformatted Lua scripting Help window with a categorized, searchable navigation (grouped topic list acting as a "tree") and properly rendered content where prose and Lua code examples are visually distinct — code gets real syntax highlighting via the existing `LuaCodeEditor` control.

**Architecture:** Each `HelpTopic` gains a `Category` (for grouping) and its single giant `Content` string is replaced with an ordered list of `HelpContentBlock` items, each block being either a prose paragraph or a Lua code snippet. `HelpWindow` groups the topic list by `Category` using `CollectionViewSource`/`PropertyGroupDescription` (visually a two-level list: category header, then topics — the practical WPF equivalent of a small tree without building a `TreeView` view-model layer), and renders the selected topic's blocks via a `DataTemplateSelector` that picks a plain `TextBlock` for prose and a read-only `LuaCodeEditor` for code, so examples get the same colored keywords/strings/comments as the script editor itself. Search filters across topic titles AND all block text/code, same as before.

**Tech Stack:** C# / WPF (.NET Framework 4.6.1, `Adan.Client` + `Adan.Client.Common` projects), existing `LuaCodeEditor` RichTextBox control (already supports `IsReadOnly` via its `RichTextBox` base, no changes needed to that control itself).

---

### Task 1: Restructure the HelpTopic data model

**Files:**
- Create: `Adan.Client\ViewModel\HelpContentBlock.cs`
- Modify: `Adan.Client\ViewModel\HelpTopic.cs`

No automated tests for this task — these are plain data classes with no behavior beyond simple property storage; correctness is verified by the build in Task 4 and by visual inspection of rendered content.

- [ ] **Step 1: Create `HelpContentBlock.cs`**

```csharp
namespace Adan.Client.ViewModel
{
    /// <summary>
    /// One paragraph of help content: either a prose block (<see cref="Text"/>
    /// set, <see cref="Code"/> null) or a Lua code example (<see cref="Code"/>
    /// set, <see cref="Text"/> null). Never both set.
    /// </summary>
    public class HelpContentBlock
    {
        private HelpContentBlock(string text, string code)
        {
            Text = text;
            Code = code;
        }

        public static HelpContentBlock ForText(string text)
        {
            return new HelpContentBlock(text, null);
        }

        public static HelpContentBlock ForCode(string code)
        {
            return new HelpContentBlock(null, code);
        }

        public string Text { get; private set; }

        public string Code { get; private set; }

        public bool IsCode
        {
            get { return Code != null; }
        }
    }
}
```

- [ ] **Step 2: Rewrite `HelpTopic.cs`**

Replace the entire file content with:

```csharp
namespace Adan.Client.ViewModel
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// One entry in the static Lua scripting help list.
    /// </summary>
    public class HelpTopic
    {
        public HelpTopic(string category, string title, List<HelpContentBlock> blocks)
        {
            Category = category;
            Title = title;
            Blocks = blocks;
        }

        public string Category { get; private set; }

        public string Title { get; private set; }

        public List<HelpContentBlock> Blocks { get; private set; }

        /// <summary>
        /// Flattened text used for search matching across both prose and code blocks.
        /// </summary>
        public string SearchableText
        {
            get { return Title + " " + string.Join(" ", Blocks.Select(b => b.Text ?? b.Code ?? string.Empty)); }
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Adan.Client/ViewModel/HelpContentBlock.cs Adan.Client/ViewModel/HelpTopic.cs
git commit -m "refactor: split HelpTopic content into categorized prose/code blocks"
```

---

### Task 2: Migrate all help content into the new structure

**Files:**
- Modify: `Adan.Client\ViewModel\HelpTopics.cs` (full rewrite)

This is a mechanical content migration: every existing topic's giant string is split into `HelpContentBlock.ForText(...)` (prose paragraphs, headers folded into the following paragraph's first line) and `HelpContentBlock.ForCode(...)` (the indented Lua snippets), and every topic gets a `Category` assigned. No behavior changes, no new topics, no information removed.

No automated tests — verified by build (Task 4) and visual check that every topic still reads correctly in the new window.

- [ ] **Step 1: Replace the entire content of `HelpTopics.cs`**

```csharp
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
                    "ГДЕ ПИСАТЬ СКРИПТЫ\n\n" +
                    "Есть два независимых места:\n\n" +
                    "1) Действие триггера/алиаса \"Run Lua script\" (в редакторе Triggers/Aliases). " +
                    "Тут ничего не изменилось: код выполняется один раз, при каждом срабатывании " +
                    "этого конкретного триггера/алиаса, и просто выполняется до конца (это не " +
                    "корутина, Wait/WaitGroupState и т.п. тут не используются).\n\n" +
                    "2) Диалог Scripts (Profile Options -> Scripts). Список именованных скриптов " +
                    "профиля — НЕ привязанных к тексту и больше НЕ привязанных к фиксированному " +
                    "типу пакета через галочки. Каждый скрипт — это независимая Lua-корутина, " +
                    "которую вы запускаете и останавливаете кнопками Start/Stop. У неё есть имя, " +
                    "код, статус и галочка \"Auto-start on connect\"."),
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
                    "Триггер/алиас со скриптом — правки действуют сразу же после сохранения триггера. " +
                    "Диалог Scripts — правки кода применяются по кнопке Save или Close, но уже " +
                    "ЗАПУЩЕННЫЕ корутины правки кода на лету не подхватывают — остановите (Stop) " +
                    "и запустите (Start) скрипт заново, чтобы он начал работать по новому коду."),
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
                    "...плюс SendCommand, Wait, WaitGroupState, WaitRoomState, WaitRoomChange " +
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
```

- [ ] **Step 2: Commit**

```bash
git add Adan.Client/ViewModel/HelpTopics.cs
git commit -m "refactor: migrate help topics into categorized prose/code blocks"
```

---

### Task 3: Rebuild HelpWindow with grouped navigation and rendered code blocks

**Files:**
- Create: `Adan.Client\Controls\HelpBlockTemplateSelector.cs`
- Modify: `Adan.Client\Dialogs\HelpWindow.xaml` (full rewrite)
- Modify: `Adan.Client\Dialogs\HelpWindow.xaml.cs` (full rewrite)
- Modify: `Adan.Client\Adan.Client.csproj` — register the new `HelpBlockTemplateSelector.cs` file

No automated tests — WPF visual layout, verified by build + manual check in Task 4.

- [ ] **Step 1: Create `HelpBlockTemplateSelector.cs`**

```csharp
namespace Adan.Client.Controls
{
    using System.Windows;
    using System.Windows.Controls;

    using ViewModel;

    /// <summary>
    /// Picks the rendering template for a single <see cref="HelpContentBlock"/>:
    /// prose blocks render as plain wrapped text, code blocks render inside a
    /// read-only <see cref="Adan.Client.Common.Controls.LuaCodeEditor"/> so
    /// examples get the same syntax highlighting as the script editor.
    /// </summary>
    public class HelpBlockTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TextTemplate { get; set; }

        public DataTemplate CodeTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var block = item as HelpContentBlock;
            if (block != null && block.IsCode)
            {
                return CodeTemplate;
            }

            return TextTemplate;
        }
    }
}
```

- [ ] **Step 2: Replace `HelpWindow.xaml.cs` entirely**

```csharp
namespace Adan.Client.Dialogs
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows;
    using System.Windows.Data;

    using ViewModel;

    public partial class HelpWindow : Window, INotifyPropertyChanged
    {
        private string _searchText = string.Empty;
        private HelpTopic _selectedTopic;
        private ListCollectionView _groupedTopics;

        public HelpWindow()
        {
            InitializeComponent();
            DataContext = this;
            RebuildGroupedTopics();
            SelectedTopic = HelpTopics.All.FirstOrDefault();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                _searchText = value;
                OnPropertyChanged("SearchText");
                RebuildGroupedTopics();
                OnPropertyChanged("GroupedTopics");
            }
        }

        public ICollectionView GroupedTopics
        {
            get { return _groupedTopics; }
        }

        public HelpTopic SelectedTopic
        {
            get { return _selectedTopic; }
            set
            {
                _selectedTopic = value;
                OnPropertyChanged("SelectedTopic");
            }
        }

        private void RebuildGroupedTopics()
        {
            IEnumerable<HelpTopic> source = HelpTopics.All;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                source = HelpTopics.All.Where(t =>
                    t.SearchableText.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            _groupedTopics = new ListCollectionView(source.ToList());
            _groupedTopics.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        }

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
```

- [ ] **Step 3: Replace `HelpWindow.xaml` entirely**

```xml
<Window x:Class="Adan.Client.Dialogs.HelpWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:controls="clr-namespace:Adan.Client.Controls"
        xmlns:commonControls="clr-namespace:Adan.Client.Common.Controls;assembly=Adan.Client.Common"
        mc:Ignorable="d"
        Title="Lua scripting help"
        Width="820" Height="540"
        WindowStartupLocation="CenterOwner"
        Style="{StaticResource DefaultWindowStyle}">
    <Window.Resources>
        <DataTemplate x:Key="HelpTextBlockTemplate">
            <TextBlock Text="{Binding Path=Text}" TextWrapping="Wrap"
                       Foreground="#FFD4D4D4" FontFamily="Consolas" FontSize="13"
                       Margin="0,0,0,10" />
        </DataTemplate>

        <DataTemplate x:Key="HelpCodeBlockTemplate">
            <Border BorderBrush="#FF3C3C3C" BorderThickness="1" Margin="0,0,0,10">
                <commonControls:LuaCodeEditor Code="{Binding Path=Code, Mode=OneWay}"
                                               IsReadOnly="True" IsUndoEnabled="False"
                                               Focusable="True" IsTabStop="False"
                                               Background="#FF1E1E1E" BorderThickness="0"
                                               FontFamily="Consolas" FontSize="13" Padding="6" />
            </Border>
        </DataTemplate>

        <controls:HelpBlockTemplateSelector x:Key="HelpBlockTemplateSelector"
                                             TextTemplate="{StaticResource HelpTextBlockTemplate}"
                                             CodeTemplate="{StaticResource HelpCodeBlockTemplate}" />
    </Window.Resources>

    <Grid Margin="5">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="260" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBox Grid.Row="0" Grid.Column="0" Margin="0,0,5,5"
                  Text="{Binding Path=SearchText, UpdateSourceTrigger=PropertyChanged}"
                  ToolTip="Поиск по темам и содержимому" />

        <ListBox Grid.Row="1" Grid.Column="0" Margin="0,0,5,0"
                  ItemsSource="{Binding Path=GroupedTopics}"
                  SelectedItem="{Binding Path=SelectedTopic}"
                  DisplayMemberPath="Title"
                  ScrollViewer.VerticalScrollBarVisibility="Auto">
            <ListBox.GroupStyle>
                <GroupStyle>
                    <GroupStyle.HeaderTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding Path=Name}" FontWeight="Bold"
                                       Foreground="#FF9CDCFE" Margin="2,8,0,2" />
                        </DataTemplate>
                    </GroupStyle.HeaderTemplate>
                </GroupStyle>
            </ListBox.GroupStyle>
        </ListBox>

        <ScrollViewer Grid.Row="0" Grid.RowSpan="2" Grid.Column="1"
                      VerticalScrollBarVisibility="Auto"
                      Background="#FF1E1E1E">
            <ItemsControl ItemsSource="{Binding Path=SelectedTopic.Blocks}"
                          ItemTemplateSelector="{StaticResource HelpBlockTemplateSelector}"
                          Margin="10">
                <ItemsControl.Resources>
                    <Style TargetType="TextBlock">
                        <Setter Property="TextWrapping" Value="Wrap" />
                    </Style>
                </ItemsControl.Resources>
            </ItemsControl>
        </ScrollViewer>
    </Grid>
</Window>
```

- [ ] **Step 4: Register `HelpBlockTemplateSelector.cs` in `Adan.Client.csproj`**

Open `Adan.Client\Adan.Client.csproj`, find the `<Compile Include="Dialogs\HelpWindow.xaml.cs">` entry (it has `<DependentUpon>HelpWindow.xaml</DependentUpon>`), and add a new `<Compile>` entry right above it:

```xml
    <Compile Include="Controls\HelpBlockTemplateSelector.cs" />
```

- [ ] **Step 5: Build `Adan.Client.Common` and `Adan.Client`**

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /c/tmp/mudclient
"$MSBUILD" Adan.Client.Common/Adan.Client.Common.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.6.1 -v:minimal -nologo
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -v:minimal -nologo
```

Expected: both builds succeed with no errors (warnings about unrelated pre-existing code are fine, but `TreatWarningsAsErrors` is `true` for `Adan.Client.Common` — any NEW warning from Task 1/2 changes there must be fixed before moving on).

- [ ] **Step 6: Commit**

```bash
git add Adan.Client/Controls/HelpBlockTemplateSelector.cs Adan.Client/Dialogs/HelpWindow.xaml Adan.Client/Dialogs/HelpWindow.xaml.cs Adan.Client/Adan.Client.csproj
git commit -m "feat: redesign Help window with categorized topics and highlighted code examples"
```

---

### Task 4: Full rebuild, repackage, and manual verification

**Files:** none (build/packaging only)

- [ ] **Step 1: Run full test suite to confirm zero regressions**

```bash
VSTEST="/c/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/CommonExtensions/Microsoft/TestWindow/vstest.console.exe"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /c/tmp/mudclient
"$MSBUILD" Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
"$VSTEST" "Adan.Client.Common.Tests/bin/Debug/net48/Adan.Client.Common.Tests.dll"
```

Expected: all existing tests still pass (this plan adds no new automated tests, only changes static help content and one dialog's UI).

- [ ] **Step 2: Rebuild and repackage the full client**

```bash
powershell.exe -ExecutionPolicy Bypass -File C:\bot\repos\adan-refactor-clients-workspace\build_client.ps1
```

Note the new version folder name printed at the end (e.g. `adan-client-1.6.6-v247-custom`).

- [ ] **Step 3: Delete the previous version's build artifacts**

Once the new version above is confirmed to exist, delete the prior version's folder and zip (the one built right before this plan, `adan-client-1.6.6-v246-custom`) from `C:\bot\repos\adan-refactor-clients-workspace\builds\`.

- [ ] **Step 4: Manual verification (report back to user, do not skip)**

Launch the new client build, open Profile Options -> Scripts -> Help, and confirm:
- The left list now shows category headers (Где и как писать скрипты / События (Wait) / Данные пакетов / Функции / Управление и ограничения) with topics grouped underneath, instead of one flat list.
- Selecting a topic shows prose as plain wrapped paragraphs and code examples in a separate highlighted block (keywords blue, strings orange, comments green, numbers light green — same colors as the script code editor).
- Typing in the search box filters the grouped list by both topic title and any text inside its content (e.g. searching "EnableGroup" should surface the "Функции: переменные, Echo, группы, статус, другие окна" topic even though that's not in its title).
- Code blocks are not editable (typing into one does nothing) but text inside them can still be selected/copied.
