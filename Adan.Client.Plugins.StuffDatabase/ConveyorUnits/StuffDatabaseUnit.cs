// --------------------------------------------------------------------------------------------------------------------
// <copyright file="StuffDatabaseUnit.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the StuffDatabaseUnit type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Plugins.StuffDatabase.ConveyorUnits
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;
    using System.Xml.Serialization;
    using Common.Commands;
    using Common.Conveyor;
    using Common.ConveyorUnits;
    using Common.Messages;
    using Common.Themes;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Properties;
    using Messages;
    using Common.Settings;

    /// <summary>
    /// <see cref="ConveyorUnit"/> implementation to handle lore messages and commands.
    /// </summary>
    public class StuffDatabaseUnit : ConveyorUnit
    {
        private const int MaxNegativeLoreLookupCacheSize = 12288;
        private const int MaxNegativeLoreLookupKeyLength = 160;
        // Кэш строк, которые были проверены и не содержат lore-предметов.
        // Ключ = нормализованная строка (цифровые последовательности → "#").
        // СТАТИЧЕСКИЙ: все табы шарят один кэш — первый промах сразу помогает всем.
        // Кэш результатов обработки строк: разделён на позитивный (найден матч) и негативный (промах).
        // ConcurrentDictionary потокобезопасен → устраняет гонку при параллельной обработке N табов.
        private const int MaxLinePositiveCacheSize = 2048;
        private const int MaxLineNegativeCacheSize = 4096;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LoreMatch> _linePositiveCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, LoreMatch>(StringComparer.Ordinal);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _lineNegativeCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        private string _lastShownObjectName = string.Empty;

        // Состояние последнего поиска (для пагинации)
        private List<string> _lastSearchFiles = new List<string>();
        private string _lastSearchQuery = string.Empty;
        private bool _lastSearchIsCompact = false;
        private int _lastSearchPage = 0;
        private const int LorePageSizeFull = 5;
        private const int LorePageSizeCompact = 15;

        // Отображение лор-предметов в тексте (тултипы + подсветка). Отключается командой "лор выкл".
        private bool _loreEnabled = true;
        // Подсветка лор-предметов жёлтым цветом. Можно отключить командой "лор цвет выкл".
        private bool _loreHighlightEnabled = true;
        // Автозапись места дропа при подборе вещи из трупа. Отключается командой "лор дроп выкл".
        private bool _loreDropEnabled = true;
        private static readonly string LoreColorConfigFileName = "_color.cfg";

        // Volatile: фоновый таймер атомарно заменяет весь словарь новой версией.
        // Горячий путь (MarkKnownLoreItems) только читает — никогда не трогает диск.
        // СТАТИЧЕСКИЕ: все табы читают один и тот же лор-кэш, загруженный один раз.
        private static volatile Dictionary<string, LoreTooltip> _loreTooltipsByObjectName = new Dictionary<string, LoreTooltip>(StringComparer.CurrentCultureIgnoreCase);
        private static volatile HashSet<string> _negativeLoreLookupKeys = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        private static DateTime _loreFolderStampUtc = DateTime.MinValue;
        private static readonly object _cacheLock = new object();
        private static readonly DateTime _startupTime = DateTime.UtcNow;
        private static System.Threading.Timer _cacheRefreshTimer;

        private static readonly Regex _whiteSpaceRx = new Regex(@" {2,}", RegexOptions.Compiled);
        private static readonly Regex _anyWhiteSpaceRx = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex _multiSpaceSplitRx = new Regex(@"\s{2,}", RegexOptions.Compiled);
        private static readonly Regex _quotedItemNameRegex = new Regex("'([^'\\r\\n]+)'", RegexOptions.Compiled);
        private static readonly Regex _countSuffixRx = new Regex(@"\s+(?:\(x?\d+\)|\[\d+\])\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _qualitySuffixRx = new Regex(@"\s+\[(?:ужасное|очень плохое|плохое|среднее|хорошее|очень хорошее|великолепное)\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Parenthetical property suffixes like "(невидимый)", "(заряженный)" etc.
        private static readonly Regex _propSuffixRx = new Regex(@"\s+\(\p{L}+\)\s*$", RegexOptions.Compiled);
        private static readonly Regex _floorTailRx = new Regex(@"\s+леж(?:ит|ат)\s+(?:тут|здесь)\.?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _digitsOnlyRx = new Regex(@"^[\d\s#\.\-<>\+ч]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _ellipsisTailRx = new Regex(@"\s*(?:\.{3,}|…)\s*$", RegexOptions.Compiled);
        private static readonly Regex _glowTailRx = new Regex(@"\s+\.{3,}\p{L}[^\r\n]*$", RegexOptions.Compiled);
        private static readonly Regex _zoneRx = new Regex(@"Вы находитесь в зоне (.+?)\.", RegexOptions.Compiled);
        private static readonly Regex _pickupFromCorpseRx = new Regex(@"(?:Вы|[А-ЯЁ]\S+)\s+взял[аи]?\s+(.+?)\s+из трупа\s+([а-яё].+?)[\.\!]?\s*$", RegexOptions.Compiled);

        private string CurrentZone
        {
            get
            {
                // Prefer zone from map plugin (automatic), fallback to parsed text
                var mapZone = Conveyor?.RootModel?.CurrentZoneName;
                return !string.IsNullOrEmpty(mapZone) ? mapZone : _currentZoneFromText;
            }
        }

        private string _currentZoneFromText = string.Empty;
        private static readonly Regex _wordRx = new Regex(@"\S+", RegexOptions.Compiled);
        private static readonly char[] _lineBreakChars = { '\r', '\n' };
        private static readonly XmlSerializer _loreSerializer = new XmlSerializer(typeof(LoreMessage));
        private static readonly Dictionary<char, char> _latinToCyrillicHomoglyphs = new Dictionary<char, char>
        {
            { 'A', 'А' }, { 'a', 'а' }, { 'B', 'В' }, { 'E', 'Е' }, { 'e', 'е' }, { 'K', 'К' }, { 'k', 'к' },
            { 'M', 'М' }, { 'H', 'Н' }, { 'O', 'О' }, { 'o', 'о' }, { 'P', 'Р' }, { 'p', 'р' }, { 'C', 'С' },
            { 'c', 'с' }, { 'T', 'Т' }, { 'X', 'Х' }, { 'x', 'х' }, { 'Y', 'У' }, { 'y', 'у' }
        };

        public StuffDatabaseUnit(MessageConveyor conveyor) : base(conveyor)
        {
            // Загрузить настройку цвета
            LoadLoreColorConfig();

            // Таймер и кэш — статические, создаём только один раз для всех табов
            lock (_cacheLock)
            {
                if (_cacheRefreshTimer == null)
                {
                    System.Threading.Tasks.Task.Run(() => RefreshLoreCache());
                    _cacheRefreshTimer = new System.Threading.Timer(_ => RefreshLoreCache(), null,
                        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            // Таймер статический — не диспозим при закрытии таба
            base.Dispose(disposing);
        }

        /// <summary>
        /// Gets a set of message types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledMessageTypes
        {
            get
            {
                return new[] { Constants.LoreMessageType, BuiltInMessageTypes.TextMessage, BuiltInMessageTypes.SystemMessage };
            }
        }

        /// <summary>
        /// Gets a set of command types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledCommandTypes
        {
            get
            {
                return Enumerable.Repeat(BuiltInCommandTypes.TextCommand, 1);
            }
        }

        public override void HandleCommand(Command command, bool isImport = false)
        {
            Assert.ArgumentNotNull(command, "command");

            var textCommand = command as TextCommand;
            if (textCommand == null)
            {
                return;
            }
            
            var commandText = _whiteSpaceRx.Replace(textCommand.CommandText.Trim(), " ");

            // Пустой Enter во время пагинации — следующая страница
            if (string.IsNullOrEmpty(commandText) && _lastSearchFiles.Count > 0)
            {
                int pageSizeCheck = _lastSearchIsCompact ? LorePageSizeCompact : LorePageSizeFull;
                int totalPagesCheck = (_lastSearchFiles.Count + pageSizeCheck - 1) / pageSizeCheck;
                if (_lastSearchPage < totalPagesCheck - 1)
                {
                    command.Handled = true;
                    _lastSearchPage++;
                    ShowLoreSearchPage();
                    return;
                }
                else
                {
                    _lastSearchFiles = new List<string>();
                }
            }

            if (commandText.StartsWith(Resources.LoreHelpCommand + " ", StringComparison.CurrentCultureIgnoreCase)
                || commandText.Equals(Resources.LoreHelpCommand, StringComparison.CurrentCultureIgnoreCase))
            {
                PushLoreCommandsHelp();
                command.Handled = true;
                return;
            }

            if (commandText.StartsWith(Resources.LoreCommentCommand + " ", StringComparison.CurrentCultureIgnoreCase)
                || commandText.Equals(Resources.LoreCommentCommand, StringComparison.CurrentCultureIgnoreCase))
            {
                if (!string.IsNullOrEmpty(_lastShownObjectName))
                {
                    var fileName = Path.Combine(GetStuffDbFolder(), _lastShownObjectName.Replace(" ", "_"));
                    LoreMessage lore = null;
                    if (File.Exists(fileName))
                    {
                        using (var inStream = File.OpenRead(fileName))
                        {
                            lore = (LoreMessage)_loreSerializer.Deserialize(inStream);
                            lore.Comments = commandText.Equals(Resources.LoreCommentCommand, StringComparison.CurrentCultureIgnoreCase)
                                                ? string.Empty
                                                : commandText.Remove(0, Resources.LoreCommentCommand.Length + 1);
                        }
                    }

                    if (lore != null)
                    {
                        var isClear = commandText.Equals(Resources.LoreCommentCommand, StringComparison.CurrentCultureIgnoreCase);
                        if (isClear)
                        {
                            // Явная очистка — сохраняем напрямую, минуя мердж комментария
                            SaveLoreFile(fileName, lore);
                            _loreFolderStampUtc = DateTime.MinValue;
                            ResetNegativeLoreLookupCache();
                            System.Threading.Tasks.Task.Run(() => RefreshLoreCache());
                            PushMessageToConveyor(new InfoMessage("Комментарий удалён.", TextColor.BrightYellow));
                        }
                        else
                        {
                            SaveOrUpdateObjectLore(lore);
                        }
                    }
                }
                else
                {
                    PushMessageToConveyor(new ErrorMessage(Resources.LoreCommentError));
                }

                command.Handled = true;
                return;
            }

            if (commandText.StartsWith(Resources.LoreDropCommand + " ", StringComparison.CurrentCultureIgnoreCase)
                || commandText.Equals(Resources.LoreDropCommand, StringComparison.CurrentCultureIgnoreCase))
            {
                command.Handled = true;
                if (string.IsNullOrEmpty(_lastShownObjectName))
                {
                    PushMessageToConveyor(new ErrorMessage(Resources.LoreDropError));
                    return;
                }

                var dropArg = commandText.Equals(Resources.LoreDropCommand, StringComparison.CurrentCultureIgnoreCase)
                    ? string.Empty
                    : commandText.Remove(0, Resources.LoreDropCommand.Length + 1).Trim();

                if (string.IsNullOrEmpty(dropArg))
                {
                    PushMessageToConveyor(new InfoMessage(Resources.LoreDropHelp, TextColor.BrightYellow));
                    return;
                }

                var fileName = Path.Combine(GetStuffDbFolder(), _lastShownObjectName.Replace(" ", "_").Replace("\"", string.Empty));
                if (!File.Exists(fileName))
                {
                    PushMessageToConveyor(new ErrorMessage(Resources.LoreDropError));
                    return;
                }

                LoreMessage lore;
                using (var inStream = File.OpenRead(fileName))
                    lore = (LoreMessage)_loreSerializer.Deserialize(inStream);

                // "lored del N" / "лорд удалить N"
                var delKeywords = new[] { "del ", "del", "удалить ", "удалить" };
                var isDelete = delKeywords.Any(k => dropArg.StartsWith(k, StringComparison.CurrentCultureIgnoreCase));
                if (isDelete)
                {
                    var numStr = dropArg.Substring(dropArg.IndexOf(' ') >= 0 ? dropArg.IndexOf(' ') + 1 : dropArg.Length).Trim();
                    int idx;
                    if (int.TryParse(numStr, out idx) && idx >= 1 && idx <= lore.DropLocations.Count)
                    {
                        var removed = lore.DropLocations[idx - 1];
                        lore.DropLocations.RemoveAt(idx - 1);
                        SaveLoreFile(fileName, lore);
                        PushMessageToConveyor(new InfoMessage(string.Format("Удалено место дропа #{0}: {1}", idx, removed.Monster), TextColor.BrightYellow));
                        _loreFolderStampUtc = DateTime.MinValue;
                        ResetNegativeLoreLookupCache();
                        System.Threading.Tasks.Task.Run(() => RefreshLoreCache());
                    }
                    else
                    {
                        PushMessageToConveyor(new ErrorMessage(string.Format("Неверный номер. Используйте {0} удалить 1..{1}", Resources.LoreDropCommand, lore.DropLocations.Count)));
                    }
                    return;
                }

                // "lored monster, zone" or "lored monster"
                string monster, zone;
                var commaIdx = dropArg.IndexOf(',');
                if (commaIdx >= 0)
                {
                    monster = dropArg.Substring(0, commaIdx).Trim();
                    zone = dropArg.Substring(commaIdx + 1).Trim();
                }
                else
                {
                    monster = dropArg.Trim();
                    zone = string.Empty;
                }

                if (string.IsNullOrEmpty(monster))
                {
                    PushMessageToConveyor(new InfoMessage(Resources.LoreDropHelp, TextColor.BrightYellow));
                    return;
                }

                var exists = lore.DropLocations.Any(d =>
                    string.Equals(d.Monster, monster, StringComparison.CurrentCultureIgnoreCase) &&
                    string.Equals(d.Zone, zone, StringComparison.CurrentCultureIgnoreCase));

                if (!exists)
                {
                    lore.DropLocations.Add(new DropLocation { Monster = monster, Zone = zone });
                    SaveLoreFile(fileName, lore);
                    PushMessageToConveyor(new InfoMessage(string.Format("Добавлено место дропа: {0}{1}", monster, string.IsNullOrEmpty(zone) ? "" : " (" + zone + ")"), TextColor.BrightYellow));
                    _loreFolderStampUtc = DateTime.MinValue;
                    ResetNegativeLoreLookupCache();
                    System.Threading.Tasks.Task.Run(() => RefreshLoreCache());
                }
                else
                {
                    PushMessageToConveyor(new InfoMessage("Такое место дропа уже есть.", TextColor.BrightYellow));
                }

                return;
            }

            // "лор+" / "лор+ <query>" — следующая страница поиска по имени
            var lorePlusCmd = Resources.LoreCommand + "+";
            if (commandText.Equals(lorePlusCmd, StringComparison.CurrentCultureIgnoreCase)
                || commandText.StartsWith(lorePlusCmd + " ", StringComparison.CurrentCultureIgnoreCase))
            {
                command.Handled = true;
                var newQuery = commandText.Length > lorePlusCmd.Length
                    ? commandText.Substring(lorePlusCmd.Length).Trim().Replace(" ", "_").Replace("\"", string.Empty)
                    : string.Empty;
                if (!string.IsNullOrEmpty(newQuery) && !string.Equals(newQuery, _lastSearchQuery, StringComparison.CurrentCultureIgnoreCase))
                {
                    _lastSearchQuery = newQuery;
                    _lastSearchPage = 0;
                    _lastSearchIsCompact = false;
                    _lastSearchFiles = Directory.Exists(GetStuffDbFolder())
                        ? Directory.GetFiles(GetStuffDbFolder())
                            .Where(f => Path.GetFileName(f).IndexOf(newQuery, StringComparison.CurrentCultureIgnoreCase) >= 0)
                            .OrderBy(f => f).ToList()
                        : new List<string>();
                }
                else
                {
                    _lastSearchPage++;
                }
                ShowLoreSearchPage();
                return;
            }

            // "лорхар <стат>[,<стат2>...]" — поиск по содержимому/характеристикам
            // "лорхар+" — следующая страница предыдущего поиска
            var loreStatCmd = Resources.LoreCommand + "хар";
            if (commandText.Equals(loreStatCmd + "+", StringComparison.CurrentCultureIgnoreCase))
            {
                command.Handled = true;
                _lastSearchPage++;
                ShowLoreSearchPage();
                return;
            }
            if (commandText.Equals(loreStatCmd, StringComparison.CurrentCultureIgnoreCase)
                || commandText.StartsWith(loreStatCmd + " ", StringComparison.CurrentCultureIgnoreCase))
            {
                command.Handled = true;
                var termsStr = commandText.Length > loreStatCmd.Length
                    ? commandText.Substring(loreStatCmd.Length).Trim()
                    : string.Empty;
                if (string.IsNullOrEmpty(termsStr))
                {
                    PushMessageToConveyor(new InfoMessage("Укажите характеристику: лорхар <стат>[,<стат2>...]", TextColor.BrightYellow));
                    PushMessageToConveyor(new InfoMessage("  Пример: лорхар сила,мудрость", TextColor.Cyan));
                    return;
                }
                // Маппинг русских названий слотов в XML-коды
                var slotMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase)
                {
                    { "голова", "HEAD" }, { "шлем", "HEAD" },
                    { "тело", "BODY" }, { "торс", "BODY" }, { "броня", "BODY" },
                    { "руки", "HANDS" }, { "перчатки", "HANDS" },
                    { "ноги", "LEGS" },
                    { "предплечья", "ARMS" }, { "наручи", "ARMS" },
                    { "запястья", "WRIST" },
                    { "шея", "NECK" },
                    { "палец", "FINGER" }, { "кольцо", "FINGER" },
                    { "пояс", "WAIST" },
                    { "плащ", "ABOUT" }, { "накидка", "ABOUT" },
                    { "оружие", "WIELD" }, { "двуручное", "DWIELD" }, { "двуруч", "DWIELD" },
                    { "щит", "SHIELD" },
                    { "держать", "HOLD" }, { "держ", "HOLD" },
                    // Типы оружия (сокращения → полное название навыка)
                    { "короткие", "короткие лезвия" }, { "клинки", "короткие лезвия" },
                    { "длинные", "длинные лезвия" }, { "мечи", "длинные лезвия" },
                    { "двуручник", "двуручники" },
                    { "копья", "копья и пики" }, { "пики", "копья и пики" },
                    { "луки", "луки" }, { "лук", "луки" },
                    { "посохи", "посохи и дубины" }, { "дубины", "посохи и дубины" },
                    { "проникающее", "проникающее оружие" }, { "кинжалы", "проникающее оружие" },
                    { "топоры", "топоры" }, { "топор", "топоры" },
                    { "разнообразное", "разнообразное оружие" },
                };
                var terms = termsStr.Split(',')
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .Select(t => slotMap.ContainsKey(t) ? slotMap[t] : t)
                    .ToArray();
                _lastSearchQuery = termsStr;
                _lastSearchPage = 0;
                _lastSearchIsCompact = true;
                if (Directory.Exists(GetStuffDbFolder()))
                {
                    _lastSearchFiles = Directory.GetFiles(GetStuffDbFolder())
                        .Where(f => {
                            if (string.Equals(Path.GetFileName(f), LoreColorConfigFileName, StringComparison.OrdinalIgnoreCase))
                                return false;
                            try
                            {
                                var text = File.ReadAllText(f);
                                return terms.All(term => text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0);
                            }
                            catch { return false; }
                        })
                        .OrderBy(f => f).ToList();
                }
                else
                {
                    _lastSearchFiles = new List<string>();
                }
                ShowLoreSearchPage();
                return;
            }

            if (commandText.StartsWith(Resources.LoreCommand + " ", StringComparison.CurrentCultureIgnoreCase)
                || commandText.Equals(Resources.LoreCommand, StringComparison.CurrentCultureIgnoreCase))
            {
                command.Handled = true;

                var searchQuery = commandText.Equals(Resources.LoreCommand, StringComparison.CurrentCultureIgnoreCase)
                                      ? string.Empty
                                      : commandText.Remove(0, Resources.LoreCommand.Length + 1).Trim().Replace(" ", "_").Replace("\"", string.Empty);

                // "лор стоп" — выход из пагинации
                if (searchQuery.Equals("стоп", StringComparison.CurrentCultureIgnoreCase))
                {
                    _lastSearchFiles = new List<string>();
                    PushMessageToConveyor(new InfoMessage("Поиск сброшен.", TextColor.BrightYellow));
                    return;
                }

                // "лор вкл" / "лор выкл"
                if (searchQuery.Equals("вкл", StringComparison.CurrentCultureIgnoreCase)
                    || searchQuery.Equals("выкл", StringComparison.CurrentCultureIgnoreCase))
                {
                    _loreEnabled = searchQuery.Equals("вкл", StringComparison.CurrentCultureIgnoreCase);
                    PushMessageToConveyor(new InfoMessage(
                        _loreEnabled ? "Лор вещей в тексте включён." : "Лор вещей в тексте выключен.",
                        TextColor.BrightYellow));
                    return;
                }

                // "лор цвет вкл" / "лор цвет выкл"
                if (searchQuery.Equals("цвет_вкл", StringComparison.CurrentCultureIgnoreCase)
                    || searchQuery.Equals("цвет_выкл", StringComparison.CurrentCultureIgnoreCase))
                {
                    _loreHighlightEnabled = searchQuery.Equals("цвет_вкл", StringComparison.CurrentCultureIgnoreCase);
                    SaveLoreColorConfig();
                    PushMessageToConveyor(new InfoMessage(
                        _loreHighlightEnabled ? "Подсветка лор-предметов включена." : "Подсветка лор-предметов выключена.",
                        TextColor.BrightYellow));
                    return;
                }

                // "лор дроп вкл" / "лор дроп выкл"
                if (searchQuery.Equals("дроп_вкл", StringComparison.CurrentCultureIgnoreCase)
                    || searchQuery.Equals("дроп_выкл", StringComparison.CurrentCultureIgnoreCase))
                {
                    _loreDropEnabled = searchQuery.Equals("дроп_вкл", StringComparison.CurrentCultureIgnoreCase);
                    PushMessageToConveyor(new InfoMessage(
                        _loreDropEnabled ? "Автозапись мест дропа включена." : "Автозапись мест дропа выключена.",
                        TextColor.BrightYellow));
                    return;
                }

                // "лор" / "lore" без аргументов или с "?" / "help" / "справка" — показать список команд
                if (string.IsNullOrEmpty(searchQuery)
                    || searchQuery.Equals("?", StringComparison.CurrentCultureIgnoreCase)
                    || searchQuery.Equals("help", StringComparison.CurrentCultureIgnoreCase)
                    || searchQuery.Equals("справка", StringComparison.CurrentCultureIgnoreCase)
                    || searchQuery.Equals("инфо", StringComparison.CurrentCultureIgnoreCase))
                {
                    PushLoreCommandsHelp();
                    return;
                }

                _lastSearchQuery = searchQuery;
                _lastSearchPage = 0;
                _lastSearchIsCompact = false;
                _lastSearchFiles = Directory.Exists(GetStuffDbFolder())
                    ? Directory.GetFiles(GetStuffDbFolder())
                        .Where(f => Path.GetFileName(f).IndexOf(searchQuery, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        .OrderBy(f => f).ToList()
                    : new List<string>();
                ShowLoreSearchPage();
            }
        }
        
        private void ShowLoreSearchPage()
        {
            if (_lastSearchFiles.Count == 0)
            {
                PushMessageToConveyor(new InfoMessage(Resources.LoreNothingFound, TextColor.BrightYellow));
                return;
            }

            int pageSize = _lastSearchIsCompact ? LorePageSizeCompact : LorePageSizeFull;
            int totalPages = (_lastSearchFiles.Count + pageSize - 1) / pageSize;
            if (_lastSearchPage >= totalPages)
                _lastSearchPage = totalPages - 1;

            int start = _lastSearchPage * pageSize;
            int end = Math.Min(start + pageSize, _lastSearchFiles.Count);

            PushMessageToConveyor(new InfoMessage(
                string.Format("Найдено: {0}. Страница {1}/{2}:", _lastSearchFiles.Count, _lastSearchPage + 1, totalPages),
                TextColor.BrightYellow));

            for (int i = start; i < end; i++)
            {
                var filePath = _lastSearchFiles[i];
                try
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        var msg = (LoreMessage)_loreSerializer.Deserialize(stream);
                        if (_lastSearchIsCompact)
                        {
                            var parts = new System.Collections.Generic.List<string>();
                            // Слоты надевания
                            if (msg.WearSlots.Count > 0)
                                parts.Add(string.Join("/", msg.WearSlots));
                            // Вес
                            parts.Add("вес:" + msg.Weight.ToString("0.#", CultureInfo.InvariantCulture));
                            // Броня/АС
                            if (msg.ArmorStats != null)
                            {
                                if (msg.ArmorStats.Armor != 0)
                                    parts.Add("броня:" + msg.ArmorStats.Armor);
                                if (msg.ArmorStats.ArmorClass != 0)
                                    parts.Add("АС:" + msg.ArmorStats.ArmorClass);
                            }
                            // Характеристики
                            var statParts = msg.AppliedAffects
                                .OfType<Adan.Client.Plugins.StuffDatabase.Model.Affects.Enhance>()
                                .Select(e => e.ModifiedParameter + e.Value.ToString("+#;-#;0", CultureInfo.InvariantCulture));
                            var statStr = string.Join(", ", statParts);
                            if (!string.IsNullOrEmpty(statStr))
                                parts.Add(statStr);
                            var line = "  " + msg.ObjectName + "  [" + string.Join(" | ", parts) + "]";
                            PushMessageToConveyor(new InfoMessage(line, TextColor.BrightGreen));
                        }
                        else
                        {
                            PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.CurrentCulture, Resources.LoreFoundObject, msg.ObjectName), TextColor.BrightYellow));
                            foreach (var displayMessage in msg.ConvertToMessages())
                                PushMessageToConveyor(displayMessage);
                        }
                        _lastShownObjectName = msg.ObjectName;
                    }
                }
                catch { }
            }

            if (_lastSearchPage < totalPages - 1)
            {
                var remaining = _lastSearchFiles.Count - end;
                PushMessageToConveyor(new InfoMessage(
                    string.Format("  [ ещё {0} — Enter для продолжения | лор стоп — выйти ]", remaining),
                    TextColor.Cyan));
            }
            else
            {
                _lastSearchFiles = new List<string>();
            }
        }

        public override void HandleMessage(Message message)
        {
            Assert.ArgumentNotNull(message, "message");

            // Сканируем только OutputToMainWindowMessage — реальный текст из игры.
            // Подклассы (InfoMessage, ErrorMessage, CommandRepeatMessage) пропускаем.
            if (message.GetType() == typeof(OutputToMainWindowMessage))
            {
                MarkKnownLoreItems((TextMessage)message);
            }

            var loreMessage = message as LoreMessage;
            if (loreMessage == null)
            {
                return;
            }

            foreach (var infoMessage in loreMessage.ConvertToMessages())
            {
                PushMessageToConveyor(infoMessage);
            }

            if (loreMessage.IsFull)
            {
                SaveOrUpdateObjectLore(loreMessage);
            }
        }

        [NotNull]
        private static string GetStuffDbFolder()
        {
            return Path.Combine(SettingsHolder.Instance.Folder, "Stuff");
        }

        private void SaveOrUpdateObjectLore([NotNull]LoreMessage loreMessage)
        {
            Assert.ArgumentNotNull(loreMessage, "loreMessage");

            if (!Directory.Exists(GetStuffDbFolder()))
            {
                Directory.CreateDirectory(GetStuffDbFolder());
            }

            var fileName = Path.Combine(GetStuffDbFolder(), loreMessage.ObjectName.Replace(" ", "_").Replace("\"", string.Empty));
            bool isUpdated = false;
            if (File.Exists(fileName))
            {
                LoreMessage oldLore;
                using (var inStream = File.OpenRead(fileName))
                {
                    oldLore = (LoreMessage)_loreSerializer.Deserialize(inStream);
                    if (!string.IsNullOrEmpty(oldLore.Comments) && string.IsNullOrEmpty(loreMessage.Comments))
                    {
                        loreMessage.Comments = oldLore.Comments;
                    }

                    // Preserve drop locations — re-lore should not erase where the item was found
                    foreach (var drop in oldLore.DropLocations)
                    {
                        var exists = loreMessage.DropLocations.Any(d =>
                            string.Equals(d.Monster, drop.Monster, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(d.Zone, drop.Zone, StringComparison.OrdinalIgnoreCase));
                        if (!exists)
                            loreMessage.DropLocations.Add(drop);
                    }

                    isUpdated = true;
                }
            }

            FileStream stream = null;
            try
            {
                stream = File.Open(fileName, FileMode.Create, FileAccess.Write);
                using (var streamWriter = new XmlTextWriter(stream, Encoding.Unicode))
                {
                    stream = null;
                    streamWriter.Formatting = Formatting.Indented;
                    _loreSerializer.Serialize(streamWriter, loreMessage);
                }
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }

            _lastShownObjectName = loreMessage.ObjectName;
            if (isUpdated)
            {
                PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.CurrentCulture, Resources.LoreUpdated, loreMessage.ObjectName), TextColor.BrightYellow));
            }
            else
            {
                PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.CurrentCulture, Resources.LoreCreated, loreMessage.ObjectName), TextColor.BrightYellow));
            }

            _loreFolderStampUtc = DateTime.MinValue;
            ResetNegativeLoreLookupCache();
            System.Threading.Tasks.Task.Run(() => RefreshLoreCache());
            PushMessageToConveyor(new InfoMessage(Resources.LoreGetHelp, TextColor.BrightYellow));
        }

        private void PushLoreCommandsHelp()
        {
            PushMessageToConveyor(new InfoMessage("── Команды лора ──────────────────────────────────────────", TextColor.BrightWhite));
            PushMessageToConveyor(new InfoMessage("  лор <название>              — найти предмет в базе (5 шт/стр)", TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage("  лор+ [<название>]           — следующая страница результатов", TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage("  лорхар <стат>[,<стат2>...]  — поиск по характеристикам/эффектам", TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage("    примеры: лорхар сила,мудрость   /   лорхар DAMROLL", TextColor.Cyan));
            PushMessageToConveyor(new InfoMessage("    пример: лор фляга для воды", TextColor.Cyan));
            PushMessageToConveyor(new InfoMessage("  лорк <текст>                — добавить комментарий к последнему предмету", TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage("    пример: лорк редкий дроп, фармить в подвале", TextColor.Cyan));
            PushMessageToConveyor(new InfoMessage("  лорк                        — удалить комментарий", TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage("  лорд <монстр>, <зона>       — добавить место дропа вручную", TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage("    пример: лорд небольшой крысы, Свалка в Минас-Моргуле", TextColor.Cyan));
            PushMessageToConveyor(new InfoMessage("  лорд удалить N              — удалить место дропа по номеру", TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage("    пример: лорд удалить 2", TextColor.Cyan));
            PushMessageToConveyor(new InfoMessage(string.Format("  лор вкл/выкл                — показывать лор вещей в тексте (сейчас: {0})", _loreEnabled ? "вкл" : "выкл"), TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage(string.Format("  лор цвет вкл/выкл           — подсветка [предметов] жёлтым (сейчас: {0})", _loreHighlightEnabled ? "вкл" : "выкл"), TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage(string.Format("  лор дроп вкл/выкл           — автозапись места дропа при подборе (сейчас: {0})", _loreDropEnabled ? "вкл" : "выкл"), TextColor.BrightYellow));
            PushMessageToConveyor(new InfoMessage("──────────────────────────────────────────────────────────", TextColor.BrightWhite));
        }

        private void LoadLoreColorConfig()
        {
            try
            {
                var path = Path.Combine(GetStuffDbFolder(), LoreColorConfigFileName);
                if (File.Exists(path))
                {
                    var val = File.ReadAllText(path).Trim();
                    _loreHighlightEnabled = !val.Equals("выкл", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
        }

        private void SaveLoreColorConfig()
        {
            try
            {
                if (!Directory.Exists(GetStuffDbFolder()))
                    Directory.CreateDirectory(GetStuffDbFolder());
                File.WriteAllText(Path.Combine(GetStuffDbFolder(), LoreColorConfigFileName),
                    _loreHighlightEnabled ? "вкл" : "выкл");
            }
            catch { }
        }

        private void SaveLoreFile([NotNull] string fileName, [NotNull] LoreMessage loreMessage)
        {
            FileStream stream = null;
            try
            {
                stream = File.Open(fileName, FileMode.Create, FileAccess.Write);
                using (var streamWriter = new XmlTextWriter(stream, Encoding.Unicode))
                {
                    stream = null;
                    streamWriter.Formatting = Formatting.Indented;
                    _loreSerializer.Serialize(streamWriter, loreMessage);
                }
            }
            finally
            {
                if (stream != null)
                    stream.Dispose();
            }
        }

        private void TryRecordDropLocation([NotNull] string itemRaw, [NotNull] string monsterRaw, [NotNull] string zone)
        {
            // This runs on a background thread — no shared cache access allowed
            if (string.IsNullOrEmpty(itemRaw) || string.IsNullOrEmpty(monsterRaw))
                return;

            try
            {
                var stuffFolder = GetStuffDbFolder();
                if (!Directory.Exists(stuffFolder))
                    return;

                var lookupKey = NormalizeLookupKey(TryStripCountSuffix(itemRaw).TrimEnd('.', ',', ':', ';'));
                if (string.IsNullOrEmpty(lookupKey))
                    return;

                // Find matching lore file by name only (no cache access — not thread-safe)
                string exactMatchFileName = null;
                string uniqueLongerFileName = null;
                bool ambiguousLonger = false;
                foreach (var file in Directory.GetFiles(stuffFolder))
                {
                    var baseName = NormalizeLookupKey(Path.GetFileName(file).Replace("_", " ").TrimEnd('.', ',', ':', ';'));
                    if (string.Equals(baseName, lookupKey, StringComparison.CurrentCultureIgnoreCase))
                        exactMatchFileName = file;
                    else if (baseName.Length > lookupKey.Length
                        && baseName.StartsWith(lookupKey, StringComparison.CurrentCultureIgnoreCase)
                        && !char.IsLetterOrDigit(baseName[lookupKey.Length]))
                    {
                        if (uniqueLongerFileName == null)
                            uniqueLongerFileName = file;
                        else
                            ambiguousLonger = true;
                    }
                }

                // If exactly one longer variant exists, prefer it — server abbreviates names.
                string matchedFileName;
                if (exactMatchFileName != null && !ambiguousLonger && uniqueLongerFileName != null)
                    matchedFileName = uniqueLongerFileName;
                else
                    matchedFileName = exactMatchFileName ?? uniqueLongerFileName;

                if (matchedFileName == null)
                    return;

                LoreMessage lore;
                using (var inStream = File.OpenRead(matchedFileName))
                {
                    lore = (LoreMessage)_loreSerializer.Deserialize(inStream);
                }

                var alreadyExists = lore.DropLocations.Any(d =>
                    string.Equals(d.Monster, monsterRaw, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(d.Zone, zone, StringComparison.OrdinalIgnoreCase));

                if (alreadyExists)
                    return;

                lore.DropLocations.Add(new DropLocation { Monster = monsterRaw, Zone = zone });

                FileStream stream = null;
                try
                {
                    stream = File.Open(matchedFileName, FileMode.Create, FileAccess.Write);
                    using (var writer = new System.Xml.XmlTextWriter(stream, System.Text.Encoding.Unicode))
                    {
                        stream = null;
                        writer.Formatting = System.Xml.Formatting.Indented;
                        _loreSerializer.Serialize(writer, lore);
                    }
                }
                finally
                {
                    if (stream != null) stream.Dispose();
                }

                // Запустить немедленный рефреш кэша в фоне (мы уже в Task.Run)
                _loreFolderStampUtc = DateTime.MinValue;
                RefreshLoreCache();
            }
            catch { }
        }

        private void MarkKnownLoreItems([NotNull] TextMessage textMessage)
        {
            Assert.ArgumentNotNull(textMessage, "textMessage");

            var rawText = textMessage.InnerText;
            if (!string.IsNullOrEmpty(rawText))
            {
                // Track current zone — быстрый pre-check перед regex
                if (rawText.IndexOf("находитесь в зоне", StringComparison.Ordinal) >= 0)
                {
                    var zoneMatch = _zoneRx.Match(rawText);
                    if (zoneMatch.Success)
                        _currentZoneFromText = zoneMatch.Groups[1].Value.Trim();
                }

                // Record drop location — быстрый pre-check перед regex
                if (_loreDropEnabled && rawText.IndexOf("из трупа", StringComparison.Ordinal) >= 0)
                {
                    var pickupMatch = _pickupFromCorpseRx.Match(rawText);
                    if (pickupMatch.Success)
                    {
                        var itemRaw = NormalizeWhitespace(pickupMatch.Groups[1].Value);
                        var monsterRaw = NormalizeWhitespace(pickupMatch.Groups[2].Value).TrimEnd('.', '!', ',');
                        var zone = CurrentZone;
                        System.Threading.Tasks.Task.Run(() => TryRecordDropLocation(itemRaw, monsterRaw, zone));
                    }
                }
            }

            var loreCache = _loreTooltipsByObjectName; // читаем volatile-ссылку один раз
            if (!_loreEnabled || loreCache.Count == 0 || textMessage.MessageBlocks.Count == 0)
            {
                return;
            }

            var sourceText = textMessage.InnerText;
            if (string.IsNullOrEmpty(sourceText))
            {
                return;
            }

            // Описания комнат всегда начинаются с пробела или таба (отступ сервера).
            // Лор-предметы в них никогда не встречаются — пропускаем без pipeline.
            if (sourceText[0] == ' ' || sourceText[0] == '\t')
            {
                return;
            }

            // Быстрый выход для частых боевых/служебных строк, которые гарантированно не
            // содержат lore-предметов. IndexOf на короткой строке (~200 симв) — несколько мкс,
            // полный lore-pipeline — 20–47 ms на каждый уникальный ключ кэша.
            // "сопротивляться" → дебафф "понизил способность X сопротивляться магии"
            // "в мир иной"     → убийство "послал X в мир иной"
            // " последней"     → "стрела была для него последней"
            // "поровну"        → дележ монет "поделила N монет поровну; вам досталось N монет"
            // " группе:"       → чат группы "Имя сказал группе: «текст»"
            // "набросился на " → таунт "Имя оскорбил X и он с бешенной яростью набросился на Имя"
            if (sourceText.IndexOf("сопротивляться", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf("в мир иной", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf(" последней", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf("поровну", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf(" группе:", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf("набросился на ", StringComparison.Ordinal) >= 0
                // "пустая ячейка" — незанятые слоты экипировки (<в правой руке>, <на голове> и т.п.)
                || sourceText.IndexOf("пустая ячейка", StringComparison.Ordinal) >= 0)
            {
                return;
            }

            var hasArrow = sourceText.IndexOf(" <---", StringComparison.Ordinal) >= 0;
            if (hasArrow && TryMarkArrowLine(textMessage, sourceText))
            {
                return;
            }

            var hasQuotedItem = sourceText.IndexOf('\'') >= 0;
            var matches = hasQuotedItem ? _quotedItemNameRegex.Matches(sourceText) : null;
            bool hasMatchingQuotes = matches != null && matches.Count > 0;

            // Быстрый выход: не строим спаны пока не знаем что строка вообще кандидат
            if (!hasMatchingQuotes && !IsLikelyStandaloneCandidateLine(sourceText, hasArrow))
            {
                return;
            }

            // Результирующий кэш строк: позитивный (найден LoreMatch) + негативный (промах).
            // Числа нормализуются → "#": "Вы нанесли 47 урона" и "52 урона" — один ключ.
            // ConcurrentDictionary устраняет гонку при параллельной обработке N табов.
            string normalizedLineKey = null;
            if (!hasMatchingQuotes && !hasArrow)
            {
                normalizedLineKey = NormalizeDigitsForLineCache(sourceText);

                LoreMatch cachedMatch;
                if (_linePositiveCache.TryGetValue(normalizedLineKey, out cachedMatch))
                {
                    // Матч уже известен — применяем без запуска pipeline
                    var spansForCached = BuildBlockSpans(textMessage.MessageBlocks);
                    if (spansForCached.Count > 0)
                        ApplyStandaloneLoreMatch(textMessage, sourceText, cachedMatch, spansForCached);
                    return;
                }

                if (_lineNegativeCache.ContainsKey(normalizedLineKey))
                    return;
            }

            var sourceBlocks = textMessage.MessageBlocks;
            var spans = BuildBlockSpans(sourceBlocks);
            if (spans.Count == 0)
            {
                return;
            }

            if (hasMatchingQuotes)
            {
                var resultBlocks = new List<TextMessageBlock>(sourceBlocks.Count);
                int currentPosition = 0;
                bool isChanged = false;

                foreach (Match match in matches)
                {
                    if (!match.Success || match.Length < 3)
                    {
                        continue;
                    }

                    var objectName = NormalizeWhitespace(match.Groups[1].Value);
                    LoreTooltip loreTooltip;
                    if (string.IsNullOrEmpty(objectName) || !loreCache.TryGetValue(objectName, out loreTooltip))
                    {
                        continue;
                    }

                    AppendRangeAsStyledBlocks(resultBlocks, spans, currentPosition, match.Index);

                    var styleBlock = GetBlockForCharIndex(spans, match.Index + 1) ?? GetBlockForCharIndex(spans, match.Index);
                    if (styleBlock != null)
                    {
                        resultBlocks.Add(new TextMessageBlock("[" + objectName + "]", _loreHighlightEnabled ? TextColor.BrightYellow : styleBlock.Foreground, styleBlock.Background, loreTooltip.PlainText, loreTooltip.Lines));
                        isChanged = true;
                    }
                    else
                    {
                        resultBlocks.Add(new TextMessageBlock("[" + objectName + "]", _loreHighlightEnabled ? TextColor.BrightYellow : TextColor.None, TextColor.None, loreTooltip.PlainText, loreTooltip.Lines));
                        isChanged = true;
                    }

                    currentPosition = match.Index + match.Length;
                }

                if (isChanged)
                {
                    AppendRangeAsStyledBlocks(resultBlocks, spans, currentPosition, sourceText.Length);
                    if (resultBlocks.Count > 0)
                    {
                        textMessage.UpdateMessageBlocks(resultBlocks);
                        return;
                    }
                }
            }

            // Standalone pipeline — вызываем TryResolveStandaloneLoreMatch напрямую,
            // чтобы закэшировать результат до применения к сообщению.
            if (normalizedLineKey != null)
            {
                LoreMatch loreMatch;
                if (TryResolveStandaloneLoreMatch(sourceText, out loreMatch))
                {
                    if (_linePositiveCache.Count < MaxLinePositiveCacheSize)
                        _linePositiveCache.TryAdd(normalizedLineKey, loreMatch);
                    ApplyStandaloneLoreMatch(textMessage, sourceText, loreMatch, spans);
                }
                else
                {
                    if (_lineNegativeCache.Count < MaxLineNegativeCacheSize)
                        _lineNegativeCache.TryAdd(normalizedLineKey, 0);
                }
            }
            else
            {
                // hasMatchingQuotes или hasArrow — кэш не используем, просто вызываем
                TryMarkStandaloneItemLine(textMessage, sourceText);
            }
        }

        // Вызывается из фонового потока (таймер + конструктор).
        // Горячий путь MarkKnownLoreItems никогда не трогает диск.
        // Защищён _cacheLock от одновременного запуска из нескольких табов.
        private static void RefreshLoreCache()
        {
            // ШАГ 1: Быстрая проверка актуальности — под локом, мгновенно
            string stuffFolder;
            DateTime folderStamp;
            lock (_cacheLock)
            {
                stuffFolder = GetStuffDbFolder();
                if (!Directory.Exists(stuffFolder))
                {
                    _loreTooltipsByObjectName = new Dictionary<string, LoreTooltip>(StringComparer.CurrentCultureIgnoreCase);
                    _negativeLoreLookupKeys = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                    _loreFolderStampUtc = DateTime.MinValue;
                    return;
                }
                folderStamp = Directory.GetLastWriteTimeUtc(stuffFolder);
                if (_loreTooltipsByObjectName.Count > 0 && folderStamp == _loreFolderStampUtc)
                    return;
            }

            // Если кэш пуст и с запуска < 10 сек — ждём окончания WPF-инициализации
            var _sinceStart = (DateTime.UtcNow - _startupTime).TotalSeconds;
            if (_sinceStart < 10.0)
                System.Threading.Thread.Sleep((int)((10.0 - _sinceStart) * 1000));

            // После ожидания повторно проверяем — может, другой поток уже загрузил кэш
            lock (_cacheLock)
            {
                var folderStamp2 = Directory.GetLastWriteTimeUtc(stuffFolder);
                if (_loreTooltipsByObjectName.Count > 0 && folderStamp2 == _loreFolderStampUtc)
                    return;
            }

            // ШАГ 2: Читаем 2000+ файлов БЕЗ лока — никто не блокируется
            var _refreshSw = System.Diagnostics.Stopwatch.StartNew();
            var newCache = new Dictionary<string, LoreTooltip>(StringComparer.CurrentCultureIgnoreCase);
            try
            {
                foreach (var fileName in Directory.GetFiles(stuffFolder))
                {
                    try
                    {
                        using (var stream = File.OpenRead(fileName))
                        {
                            var loreMessage = (LoreMessage)_loreSerializer.Deserialize(stream);
                            if (string.IsNullOrWhiteSpace(loreMessage.ObjectName))
                                continue;

                            var loreTooltip = CreateLoreTooltip(loreMessage);
                            var normalizedKey = NormalizeLookupKey(loreMessage.ObjectName);
                            newCache[normalizedKey] = loreTooltip;

                            var strippedCountKey = NormalizeLookupKey(TryStripCountSuffix(loreMessage.ObjectName));
                            if (!string.IsNullOrEmpty(strippedCountKey)
                                && !strippedCountKey.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase)
                                && !newCache.ContainsKey(strippedCountKey))
                            {
                                newCache[strippedCountKey] = loreTooltip;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // ШАГ 3: Атомарный своп — под локом, мгновенно (только присваивания)
            lock (_cacheLock)
            {
                _loreTooltipsByObjectName = newCache;
                _negativeLoreLookupKeys = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                _linePositiveCache.Clear();
                _lineNegativeCache.Clear();
                _loreFolderStampUtc = folderStamp;
            }
            _refreshSw.Stop();
            Adan.Client.Common.Conveyor.PerfLog.WriteTotal("LORE_CACHE", _refreshSw.ElapsedMilliseconds, "items=" + newCache.Count);
        }

        [NotNull]
        private static List<BlockSpan> BuildBlockSpans([NotNull] IList<TextMessageBlock> sourceBlocks)
        {
            Assert.ArgumentNotNull(sourceBlocks, "sourceBlocks");

            var spans = new List<BlockSpan>(sourceBlocks.Count);
            int currentOffset = 0;
            foreach (var block in sourceBlocks)
            {
                var blockLength = block.Text.Length;
                if (blockLength == 0)
                {
                    continue;
                }

                spans.Add(new BlockSpan
                {
                    Block = block,
                    Start = currentOffset,
                    End = currentOffset + blockLength
                });

                currentOffset += blockLength;
            }

            return spans;
        }

        private static void AppendRangeAsStyledBlocks(
            [NotNull] ICollection<TextMessageBlock> destination,
            [NotNull] IEnumerable<BlockSpan> spans,
            int rangeStart,
            int rangeEnd)
        {
            Assert.ArgumentNotNull(destination, "destination");
            Assert.ArgumentNotNull(spans, "spans");

            if (rangeEnd <= rangeStart)
            {
                return;
            }

            foreach (var span in spans)
            {
                if (span.End <= rangeStart || span.Start >= rangeEnd)
                {
                    continue;
                }

                var copyStart = Math.Max(span.Start, rangeStart);
                var copyEnd = Math.Min(span.End, rangeEnd);
                if (copyEnd <= copyStart)
                {
                    continue;
                }

                var textStartInBlock = copyStart - span.Start;
                var length = copyEnd - copyStart;
                destination.Add(new TextMessageBlock(span.Block.Text.Substring(textStartInBlock, length), span.Block.Foreground, span.Block.Background, span.Block.ToolTipText, span.Block.ToolTipLines));
            }
        }

        [CanBeNull]
        private static TextMessageBlock GetBlockForCharIndex([NotNull] IEnumerable<BlockSpan> spans, int charIndex)
        {
            Assert.ArgumentNotNull(spans, "spans");

            if (charIndex < 0)
            {
                return null;
            }

            foreach (var span in spans)
            {
                if (charIndex >= span.Start && charIndex < span.End)
                {
                    return span.Block;
                }
            }

            return null;
        }

        // Extracts the name suffix embedded after stat tokens in <--- arrow annotations.
        // E.g. "АС2,БР2,С4+1,!ДДКАСТЕРЫ, расшитый черными опалами [очень хорошее]"
        //      returns "расшитый черными опалами"
        // Stats: comma-separated tokens that are all-uppercase (with optional ! prefix, digits, +/-).
        // Name suffix: first token that contains a lowercase letter.
        private static string TryExtractNameSuffixFromArrow([NotNull] string afterArrow)
        {
            var tokens = afterArrow.Split(',');
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i].Trim();
                if (string.IsNullOrEmpty(token))
                    continue;
                var hasLower = false;
                foreach (var c in token)
                {
                    if (char.IsLower(c)) { hasLower = true; break; }
                }
                if (!hasLower)
                    continue;
                // Everything from this token onwards (joined back with commas) is the name suffix.
                var suffix = string.Join(",", tokens, i, tokens.Length - i).Trim();
                // Strip trailing quality bracket like [очень хорошее]
                var bracketIdx = suffix.LastIndexOf('[');
                if (bracketIdx > 0)
                    suffix = suffix.Substring(0, bracketIdx).TrimEnd();
                // Strip trailing punctuation/period
                suffix = suffix.TrimEnd('.', ',', ';', ':');
                return string.IsNullOrEmpty(suffix) ? null : suffix;
            }
            return null;
        }

        private bool TryMarkArrowLine([NotNull] TextMessage textMessage, [NotNull] string sourceText)
        {
            Assert.ArgumentNotNull(textMessage, "textMessage");
            Assert.ArgumentNotNull(sourceText, "sourceText");

            var arrowIndex = sourceText.IndexOf(" <---", StringComparison.Ordinal);
            if (arrowIndex <= 0)
            {
                return false;
            }

            LoreMatch loreMatch;
            if (!TryResolveLoreInRange(sourceText, 0, arrowIndex, out loreMatch))
            {
                return false;
            }

            var sourceBlocks = textMessage.MessageBlocks;
            var spans = BuildBlockSpans(sourceBlocks);
            if (spans.Count == 0)
            {
                return false;
            }

            var resultBlocks = new List<TextMessageBlock>(sourceBlocks.Count + 3);

            // Если item-name был внутри скобок [ItemName], loreMatch.Start указывает внутрь.
            // Включаем внешние скобки в замену, чтобы не получить двойные [[ и ]].
            var replaceStart = loreMatch.Start;
            var replaceEnd = loreMatch.End;
            if (replaceStart > 0 && sourceText[replaceStart - 1] == '[')
                replaceStart--;
            if (replaceEnd < sourceText.Length && sourceText[replaceEnd] == ']')
                replaceEnd++;

            AppendRangeAsStyledBlocks(resultBlocks, spans, 0, replaceStart);

            // Server sometimes sends abbreviated name before <--- (e.g. equipment screen).
            // Try to reconstruct full name from the lowercase name-suffix embedded after stats.
            // E.g.: "[алый плащ] <--- АС2,БР2,!ФЛАГ, расшитый черными опалами [хорошее]"
            var afterArrowText = sourceText.Substring(arrowIndex + 5).TrimStart(' ');
            var arrowNameSuffix = TryExtractNameSuffixFromArrow(afterArrowText);
            if (!string.IsNullOrEmpty(arrowNameSuffix))
            {
                var candidateFullName = loreMatch.ItemName + ", " + arrowNameSuffix;
                var candidateKey = NormalizeLookupKey(candidateFullName);
                LoreTooltip fullNameTooltip;
                if (_loreTooltipsByObjectName.TryGetValue(candidateKey, out fullNameTooltip))
                    loreMatch = new LoreMatch(loreMatch.Start, loreMatch.End, candidateFullName, fullNameTooltip);
            }

            var styleBlock = GetBlockForCharIndex(spans, replaceStart) ?? sourceBlocks[0];
            resultBlocks.Add(new TextMessageBlock("[" + loreMatch.ItemName + "]", _loreHighlightEnabled ? TextColor.BrightYellow : styleBlock.Foreground, styleBlock.Background, loreMatch.Tooltip.PlainText, loreMatch.Tooltip.Lines));

            AppendRangeAsStyledBlocks(resultBlocks, spans, replaceEnd, sourceText.Length);
            textMessage.UpdateMessageBlocks(resultBlocks);
            return true;
        }

        private bool TryMarkStandaloneItemLine([NotNull] TextMessage textMessage, [NotNull] string sourceText)
        {
            Assert.ArgumentNotNull(textMessage, "textMessage");
            Assert.ArgumentNotNull(sourceText, "sourceText");

            if (sourceText.IndexOf("<---", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf("'", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            LoreMatch loreMatch;
            if (!TryResolveStandaloneLoreMatch(sourceText, out loreMatch))
            {
                return false;
            }

            var sourceBlocks = textMessage.MessageBlocks;
            var spans = BuildBlockSpans(sourceBlocks);
            if (spans.Count == 0)
            {
                return false;
            }

            ApplyStandaloneLoreMatch(textMessage, sourceText, loreMatch, spans);
            return true;
        }

        private void ApplyStandaloneLoreMatch(
            [NotNull] TextMessage textMessage,
            [NotNull] string sourceText,
            [NotNull] LoreMatch loreMatch,
            [NotNull] IList<BlockSpan> spans)
        {
            var sourceBlocks = textMessage.MessageBlocks;
            var resultBlocks = new List<TextMessageBlock>(sourceBlocks.Count + 3);

            var replaceStart = loreMatch.Start;
            var replaceEnd = loreMatch.End;
            if (replaceStart > 0 && sourceText[replaceStart - 1] == '[')
                replaceStart--;
            if (replaceEnd < sourceText.Length && sourceText[replaceEnd] == ']')
                replaceEnd++;

            AppendRangeAsStyledBlocks(resultBlocks, spans, 0, replaceStart);
            var styleBlock = GetBlockForCharIndex(spans, replaceStart) ?? sourceBlocks[0];
            resultBlocks.Add(new TextMessageBlock("[" + loreMatch.ItemName + "]", _loreHighlightEnabled ? TextColor.BrightYellow : styleBlock.Foreground, styleBlock.Background, loreMatch.Tooltip.PlainText, loreMatch.Tooltip.Lines));
            AppendRangeAsStyledBlocks(resultBlocks, spans, replaceEnd, sourceText.Length);
            textMessage.UpdateMessageBlocks(resultBlocks);
        }

        private bool TryResolveStandaloneLoreMatch([NotNull] string sourceText, out LoreMatch match)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            match = null;
            var sourceLength = sourceText.Length;
            if (sourceLength == 0)
            {
                return false;
            }

            var ranges = new List<RangeCandidate>(6);
            var words = _wordRx.Matches(sourceText);

            if (sourceText.StartsWith("Вы ", StringComparison.CurrentCultureIgnoreCase))
            {
                var actionTailStart = FindActionTailStart(sourceText);
                if (words.Count >= 4)
                {
                    ranges.Add(new RangeCandidate(words[3].Index, sourceLength));
                    if (actionTailStart > words[3].Index)
                    {
                        ranges.Add(new RangeCandidate(words[3].Index, actionTailStart));
                    }
                }

                if (words.Count >= 3)
                {
                    ranges.Add(new RangeCandidate(words[2].Index, sourceLength));
                    if (actionTailStart > words[2].Index)
                    {
                        ranges.Add(new RangeCandidate(words[2].Index, actionTailStart));
                    }
                }

                if (words.Count >= 2)
                {
                    ranges.Add(new RangeCandidate(words[1].Index, sourceLength));
                    if (actionTailStart > words[1].Index)
                    {
                        ranges.Add(new RangeCandidate(words[1].Index, actionTailStart));
                    }
                }
            }
            else if (IsLikelyThirdPersonActionLine(words))
            {
                var actionTailStart = FindActionTailStart(sourceText);
                if (words.Count >= 3)
                {
                    ranges.Add(new RangeCandidate(words[2].Index, sourceLength));
                    if (actionTailStart > words[2].Index)
                    {
                        ranges.Add(new RangeCandidate(words[2].Index, actionTailStart));
                    }
                }

                if (words.Count >= 2)
                {
                    ranges.Add(new RangeCandidate(words[1].Index, sourceLength));
                    if (actionTailStart > words[1].Index)
                    {
                        ranges.Add(new RangeCandidate(words[1].Index, actionTailStart));
                    }
                }
            }

            ranges.Add(new RangeCandidate(0, sourceLength));
            foreach (var range in ranges)
            {
                LoreMatch resolvedMatch;
                if (!TryResolveLoreInRange(sourceText, range.Start, range.End, out resolvedMatch))
                {
                    continue;
                }

                match = resolvedMatch;
                return true;
            }

            return false;
        }

        private bool TryResolveLoreInRange([NotNull] string sourceText, int rangeStart, int rangeEnd, out LoreMatch match)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            match = null;
            if (rangeStart < 0 || rangeEnd > sourceText.Length || rangeEnd <= rangeStart)
            {
                return false;
            }

            var preferBestMatch = sourceText.IndexOf("Вещь:", StringComparison.CurrentCultureIgnoreCase) >= 0
                                  || sourceText.IndexOf("Объект ", StringComparison.CurrentCultureIgnoreCase) >= 0;

            LoreMatch bestMatch = null;

            // Try full range first so "эссенция из глаза дракона" wins over column match "[глаза дракона]"
            LoreMatch fullRangeMatch;
            if (TryParseLoreMatch(sourceText, rangeStart, rangeEnd, out fullRangeMatch))
            {
                if (!preferBestMatch)
                {
                    match = fullRangeMatch;
                    return true;
                }

                bestMatch = fullRangeMatch;
            }

            LoreMatch columnMatch;
            if (TryResolveLoreByColumns(sourceText, rangeStart, rangeEnd, out columnMatch))
            {
                if (!preferBestMatch)
                {
                    match = columnMatch;
                    return true;
                }

                if (bestMatch == null || IsBetterLoreMatch(columnMatch, bestMatch))
                {
                    bestMatch = columnMatch;
                }
            }

            var candidateStarts = CollectCandidateStarts(sourceText, rangeStart, rangeEnd);
            foreach (var candidateStart in candidateStarts)
            {
                if (candidateStart == rangeStart)
                {
                    continue; // already tried above
                }

                LoreMatch parsedMatch;
                if (TryParseLoreMatch(sourceText, candidateStart, rangeEnd, out parsedMatch))
                {
                    if (!preferBestMatch)
                    {
                        match = parsedMatch;
                        return true;
                    }

                    if (bestMatch == null || IsBetterLoreMatch(parsedMatch, bestMatch))
                    {
                        bestMatch = parsedMatch;
                    }
                }
            }

            if (bestMatch == null)
            {
                return false;
            }

            match = bestMatch;
            return true;
        }

        private static bool IsLikelyStandaloneCandidateLine([NotNull] string sourceText, bool hasArrow)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            if (sourceText.Length < 3)
            {
                return false;
            }

            if (hasArrow)
            {
                return true;
            }

            // Строки "X взял Y из трупа Z" — имя монстра в конце ломает lore pipeline,
            // а quoted items ('...') уже обработаны выше. Пропускаем standalone detection.
            if (sourceText.IndexOf("из трупа", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            var trimmed = sourceText.TrimStart();
            if (trimmed.Length < 3)
            {
                return false;
            }

            if (trimmed[0] == '[' || trimmed[0] == '<' || trimmed[0] == '!')
            {
                return true;
            }

            if (IsLikelyMarketTableRow(trimmed))
            {
                return true;
            }

            if (trimmed.StartsWith("Вещь:", StringComparison.CurrentCultureIgnoreCase)
                || trimmed.StartsWith("Объект ", StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            // Только строки с заглавной буквы могут быть NPC-действиями.
            // Описания комнат часто начинаются с маленькой (продолжение абзаца) — пропускаем.
            if (trimmed.Length > 4 && char.IsUpper(trimmed[0]))
            {
                // Строки присутствия NPC в комнате ("Орк стоит здесь", "Орк сидит здесь")
                // не содержат лор-предметов — пропускаем до дорогого pipeline.
                bool isRoomPresence =
                    trimmed.IndexOf(" здесь", StringComparison.Ordinal) >= 0 ||
                    trimmed.IndexOf(" тут,", StringComparison.Ordinal) >= 0 ||
                    (trimmed.EndsWith(" тут.", StringComparison.Ordinal) || trimmed.EndsWith(" тут", StringComparison.Ordinal));

                // Строки вида "Орк ударил вас на 47 урона" — атака на игрока,
                // не могут содержать лор-предмет. Пропускаем до дорогого pipeline.
                bool targetedAtPlayer =
                    trimmed.IndexOf(" вас", StringComparison.Ordinal) >= 0 ||
                    trimmed.IndexOf(" тебя", StringComparison.Ordinal) >= 0;

                if (!isRoomPresence && !targetedAtPlayer)
                {
                    // Для строк "Вы ..." пропускаем глаголы не связанные с предметами:
                    // "Вы стоите/находитесь/передвигаетесь/не можете/вернулись/вышли" и т.п.
                    // Подбор/экипировка предметов ("Вы взяли/надели/сняли") — пропускаем через pipeline.
                    bool runThirdPersonCheck = true;
                    if (trimmed.StartsWith("Вы ", StringComparison.Ordinal))
                    {
                        // Быстрый pre-check: глагол стоит вторым словом
                        var spaceIdx = trimmed.IndexOf(' ', 3); // после "Вы "
                        var verbEnd = spaceIdx >= 0 ? spaceIdx : trimmed.Length;
                        var v2 = trimmed.Substring(3, verbEnd - 3).Trim('.', ',', ':', ';').ToLowerInvariant();
                        runThirdPersonCheck = v2 == "взял" || v2 == "взяла" || v2 == "взяли"
                            || v2 == "поднял" || v2 == "подняла" || v2 == "подняли"
                            || v2 == "подобрал" || v2 == "подобрала" || v2 == "подобрали"
                            || v2 == "надел" || v2 == "надела" || v2 == "надели"
                            || v2 == "снял" || v2 == "сняла" || v2 == "сняли"
                            || v2 == "получил" || v2 == "получила" || v2 == "получили"
                            || v2 == "купил" || v2 == "купила" || v2 == "купили"
                            || v2 == "нашёл" || v2 == "нашла" || v2 == "нашли"
                            || v2 == "выбросил" || v2 == "выбросила" || v2 == "выбросили";
                    }
                    else
                    {
                        // Для "X <глагол> Y" — третье лицо. Боевые глаголы (атака, нокдаун, убийство)
                        // никогда не предшествуют lore-предмету — пропускаем.
                        var spaceIdx = trimmed.IndexOf(' ');
                        if (spaceIdx > 0)
                        {
                            var spaceIdx2 = trimmed.IndexOf(' ', spaceIdx + 1);
                            var verbEnd = spaceIdx2 >= 0 ? spaceIdx2 : trimmed.Length;
                            var v2 = trimmed.Substring(spaceIdx + 1, verbEnd - spaceIdx - 1)
                                           .Trim('.', ',', ':', ';', '!').ToLowerInvariant();
                            // Глаголы атаки/убийства/нокдауна/промаха — однозначно не lore-контекст
                            if (v2 == "ударил" || v2 == "ударила" || v2 == "ударили"
                                || v2 == "атаковал" || v2 == "атаковала" || v2 == "атаковали"
                                || v2 == "завалил" || v2 == "завалила" || v2 == "завалили"
                                || v2 == "промахнулся" || v2 == "промахнулась" || v2 == "промахнулись"
                                || v2 == "попытался" || v2 == "попыталась" || v2 == "попытались"
                                || v2 == "убил" || v2 == "убила" || v2 == "убили")
                            {
                                runThirdPersonCheck = false;
                            }

                            // "Магический/Меткий/Смертельный выстрел Verb..." — v2 = "выстрел" (существительное).
                            // "Сильный удар Verb..." — v2 = "удар".
                            // Настоящий глагол стоит на позиции v3. Если v2 — боевое существительное,
                            // строка не может содержать lore-предмет в позиции объекта.
                            if (runThirdPersonCheck
                                && (v2 == "выстрел" || v2 == "удар" || v2 == "выстрела" || v2 == "удара"))
                            {
                                runThirdPersonCheck = false;
                            }
                        }
                    }

                    if (runThirdPersonCheck)
                    {
                        var words = _wordRx.Matches(trimmed);
                        if (IsLikelyThirdPersonActionLine(words))
                        {
                            return true;
                        }
                    }
                }
            }

            if (sourceText.IndexOf(" !", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf(" лежит ", StringComparison.CurrentCultureIgnoreCase) >= 0
                || sourceText.IndexOf(" лот #", StringComparison.CurrentCultureIgnoreCase) >= 0)
            {
                return true;
            }

            if (IsLikelyPlainItemNameLine(trimmed))
            {
                return true;
            }

            return false;
        }

        private static bool IsLikelyMarketTableRow([NotNull] string trimmed)
        {
            Assert.ArgumentNotNull(trimmed, "trimmed");

            if (trimmed.Length < 8 || !char.IsDigit(trimmed[0]))
            {
                return false;
            }

            return _multiSpaceSplitRx.Matches(trimmed).Count >= 3;
        }

        private static bool IsLikelyPlainItemNameLine([NotNull] string trimmed)
        {
            Assert.ArgumentNotNull(trimmed, "trimmed");

            if (trimmed.Length < 4 || trimmed.Length > 72)
            {
                return false;
            }

            if (char.IsDigit(trimmed[0]))
            {
                return false;
            }

            // Строки, начинающиеся с местоимений "Вы/Ваш/Ваша" — всегда боевые действия игрока,
            // а не названия предметов. "Вы смертельно огрели X" не может быть лор-строкой.
            if (trimmed.StartsWith("Вы ", StringComparison.Ordinal)
                || trimmed.StartsWith("Ваш ", StringComparison.Ordinal)
                || trimmed.StartsWith("Ваша ", StringComparison.Ordinal))
            {
                return false;
            }

            // Строки, начинающиеся с предлога "По " — описания комнат/переходов в Adamant MUD.
            // "По широкому коридору", "По тёмному тоннелю" — пространственные фразы,
            // никогда не встречаются как названия lore-предметов.
            if (trimmed.StartsWith("По ", StringComparison.Ordinal))
            {
                return false;
            }

            if (trimmed.IndexOf('<') >= 0
                || trimmed.IndexOf('>') >= 0
                || trimmed.IndexOf(':') >= 0
                || trimmed.IndexOf('!') >= 0
                || trimmed.IndexOf('?') >= 0)
            {
                return false;
            }

            var hasCountSuffix = _countSuffixRx.IsMatch(trimmed);
            var phrase = _countSuffixRx.Replace(trimmed, string.Empty).Trim();
            if (phrase.EndsWith(".", StringComparison.Ordinal))
            {
                return false;
            }

            if (phrase.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            var words = SplitWords(phrase);
            if (words.Length < (hasCountSuffix ? 1 : 2) || words.Length > 6)
            {
                return false;
            }

            // Пространственные предлоги и конструкции характерны для названий локаций/комнат,
            // но никогда не встречаются в названиях lore-предметов.
            // "Перекресток недалеко от Минас-Моргула" → false (не предмет, а комната).
            if (phrase.IndexOf(" вдоль ", StringComparison.Ordinal) >= 0
                || phrase.IndexOf(" рядом ", StringComparison.Ordinal) >= 0
                || phrase.IndexOf(" вблизи ", StringComparison.Ordinal) >= 0
                || phrase.IndexOf(" недалеко ", StringComparison.Ordinal) >= 0
                || phrase.IndexOf(" между ", StringComparison.Ordinal) >= 0
                || phrase.IndexOf(" напротив ", StringComparison.Ordinal) >= 0
                || phrase.StartsWith("За ", StringComparison.Ordinal)
                || phrase.StartsWith("Перед ", StringComparison.Ordinal)
                || phrase.StartsWith("Между ", StringComparison.Ordinal)
                || phrase.StartsWith("Рядом ", StringComparison.Ordinal)
                || phrase.StartsWith("Вдоль ", StringComparison.Ordinal)
                || phrase.StartsWith("Вблизи ", StringComparison.Ordinal)
                || phrase.StartsWith("Недалеко ", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private bool TryResolveLoreByColumns([NotNull] string sourceText, int rangeStart, int rangeEnd, out LoreMatch match)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            match = null;
            var subText = sourceText.Substring(rangeStart, rangeEnd - rangeStart);
            var separators = _multiSpaceSplitRx.Matches(subText);
            if (separators.Count == 0)
            {
                return false;
            }

            var columns = new List<ColumnCandidate>(8);
            var currentStart = 0;
            foreach (Match separator in separators)
            {
                if (separator.Index > currentStart)
                {
                    columns.Add(new ColumnCandidate(rangeStart + currentStart, rangeStart + separator.Index));
                }

                currentStart = separator.Index + separator.Length;
            }

            if (currentStart < subText.Length)
            {
                columns.Add(new ColumnCandidate(rangeStart + currentStart, rangeStart + subText.Length));
            }

            if (columns.Count < 2)
            {
                return false;
            }

            foreach (var column in columns)
            {
                var raw = sourceText.Substring(column.Start, column.End - column.Start).Trim();
                if (raw.Length < 3)
                {
                    continue;
                }

                if (ShouldSkipColumnCandidate(raw))
                {
                    continue;
                }

                LoreMatch parsed;
                if (TryParseLoreMatch(sourceText, column.Start, column.End, out parsed))
                {
                    match = parsed;
                    return true;
                }
            }

            return false;
        }

        [NotNull]
        private static IEnumerable<int> CollectCandidateStarts([NotNull] string sourceText, int rangeStart, int rangeEnd)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            var starts = new List<int>(12) { rangeStart };
            var startSet = new HashSet<int> { rangeStart };

            Action<int> addStart = s =>
            {
                if (s <= rangeStart || s >= rangeEnd || startSet.Contains(s))
                {
                    return;
                }

                startSet.Add(s);
                starts.Add(s);
            };

            var lastQuote = sourceText.LastIndexOf('\'', rangeEnd - 1, rangeEnd - rangeStart);
            if (lastQuote >= rangeStart)
            {
                addStart(lastQuote + 1);
            }

            addStart(FindLastIndexOf(sourceText, "] ", rangeStart, rangeEnd) + 2);
            addStart(FindLastIndexOf(sourceText, "> ", rangeStart, rangeEnd) + 2);
            addStart(FindLastIndexOf(sourceText, ": ", rangeStart, rangeEnd) + 2);
            addStart(FindLastIndexOf(sourceText, " - ", rangeStart, rangeEnd) + 3);

            var subText = sourceText.Substring(rangeStart, rangeEnd - rangeStart);
            foreach (Match splitMatch in _multiSpaceSplitRx.Matches(subText))
            {
                addStart(rangeStart + splitMatch.Index + splitMatch.Length);
            }

            for (var i = rangeStart; i < rangeEnd; i++)
            {
                var ch = sourceText[i];
                if (ch == '\r' || ch == '\n')
                {
                    var lineStart = i + 1;
                    while (lineStart < rangeEnd && (sourceText[lineStart] == ' ' || sourceText[lineStart] == '\t' || sourceText[lineStart] == '\r' || sourceText[lineStart] == '\n'))
                    {
                        lineStart++;
                    }

                    addStart(lineStart);
                    continue;
                }

                if (ch == '!')
                {
                    addStart(i);
                }
            }

            starts.Sort((a, b) => b.CompareTo(a));
            return starts;
        }

        private static int FindLastIndexOf([NotNull] string sourceText, [NotNull] string token, int rangeStart, int rangeEnd)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");
            Assert.ArgumentNotNull(token, "token");

            var length = rangeEnd - rangeStart;
            if (length <= 0)
            {
                return -1;
            }

            return sourceText.LastIndexOf(token, rangeStart + length - 1, length, StringComparison.Ordinal);
        }

        private bool TryParseLoreMatch([NotNull] string sourceText, int candidateStart, int candidateEnd, out LoreMatch match)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            match = null;
            if (candidateEnd <= candidateStart)
            {
                return false;
            }

            int start = candidateStart;
            int end = candidateEnd;

            TrimRangeWhitespaces(sourceText, ref start, ref end);
            if (end <= start)
            {
                return false;
            }

            var lineBreakIndex = sourceText.IndexOfAny(_lineBreakChars, start, end - start);
            if (lineBreakIndex >= start)
            {
                end = lineBreakIndex;
                TrimRangeWhitespaces(sourceText, ref start, ref end);
                if (end <= start)
                {
                    return false;
                }
            }

            var rawCandidate = sourceText.Substring(start, end - start);
            var floorTailMatch = _floorTailRx.Match(rawCandidate);
            if (floorTailMatch.Success && floorTailMatch.Index + floorTailMatch.Length == rawCandidate.Length)
            {
                end = start + floorTailMatch.Index;
                TrimRangeWhitespaces(sourceText, ref start, ref end);
                if (end <= start)
                {
                    return false;
                }
            }

            // Strip trailing "...glow description" like "...мягко светится" that follows quality suffix
            rawCandidate = sourceText.Substring(start, end - start);
            var glowTailMatch = _glowTailRx.Match(rawCandidate);
            if (glowTailMatch.Success && glowTailMatch.Index > 0)
            {
                end = start + glowTailMatch.Index;
                TrimRangeWhitespaces(sourceText, ref start, ref end);
                if (end <= start)
                {
                    return false;
                }
            }

            int markerPrefixStart;
            var hadMarkerPrefix = ConsumeLeadingItemMarkers(sourceText, ref start, end, out markerPrefixStart);

            if (end <= start)
            {
                return false;
            }

            bool unwrapped;
            do
            {
                unwrapped = false;
                TrimRangeWhitespaces(sourceText, ref start, ref end);
                if (end - start >= 2 && sourceText[start] == '[' && sourceText[end - 1] == ']')
                {
                    start++;
                    end--;
                    unwrapped = true;
                }

                TrimRangeWhitespaces(sourceText, ref start, ref end);
                if (end - start >= 2 && sourceText[start] == '\'' && sourceText[end - 1] == '\'')
                {
                    start++;
                    end--;
                    unwrapped = true;
                }
            }
            while (unwrapped && end > start);

            var displayItemName = NormalizeWhitespace(sourceText.Substring(start, end - start));
            if (string.IsNullOrEmpty(displayItemName)
                || displayItemName.Equals("пустая ячейка", StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }

            var strippedDisplayItemName = StripNonItemPrefix(displayItemName);
            if (!string.IsNullOrEmpty(strippedDisplayItemName)
                && !strippedDisplayItemName.Equals(displayItemName, StringComparison.CurrentCultureIgnoreCase))
            {
                int removedWordsCount;
                if (TryGetRemovedLeadingWordsCount(displayItemName, strippedDisplayItemName, out removedWordsCount)
                    && removedWordsCount > 0)
                {
                    int shiftedStart;
                    if (TryGetShiftedStartByWords(sourceText, start, end, removedWordsCount, out shiftedStart))
                    {
                        start = shiftedStart;
                        int shiftedMarkerStart;
                        if (ConsumeLeadingItemMarkers(sourceText, ref start, end, out shiftedMarkerStart))
                        {
                            markerPrefixStart = shiftedMarkerStart;
                            hadMarkerPrefix = true;
                        }
                    }
                }

                displayItemName = strippedDisplayItemName;
            }

            var lookupItemName = displayItemName;
            var allowPrefixLookup = _ellipsisTailRx.IsMatch(lookupItemName);
            lookupItemName = _ellipsisTailRx.Replace(lookupItemName, string.Empty).Trim();
            lookupItemName = lookupItemName.TrimEnd('.', ',', ':', ';');
            lookupItemName = NormalizeLookupKey(lookupItemName);
            if (string.IsNullOrEmpty(lookupItemName))
            {
                return false;
            }

            LoreLookupResult lookupResult;
            var matchedWithoutCount = false;
            var matchedWithoutQuality = false;
            var matchedWithoutProp = false;
            if (!TryResolveLoreTooltip(displayItemName, lookupItemName, allowPrefixLookup, out lookupResult))
            {
                var withoutQualityDisplay = TryStripQualitySuffix(displayItemName);
                if (!string.IsNullOrEmpty(withoutQualityDisplay)
                    && !withoutQualityDisplay.Equals(displayItemName, StringComparison.CurrentCultureIgnoreCase))
                {
                    var withoutQualityLookup = NormalizeLookupKey(withoutQualityDisplay.TrimEnd('.', ',', ':', ';'));
                    if (!string.IsNullOrEmpty(withoutQualityLookup)
                        && TryResolveLoreTooltip(withoutQualityDisplay, withoutQualityLookup, allowPrefixLookup, out lookupResult))
                    {
                        matchedWithoutQuality = true;
                    }
                    else
                    {
                        var withoutQualityAndCountDisplay = TryStripCountSuffix(withoutQualityDisplay);
                        if (!string.IsNullOrEmpty(withoutQualityAndCountDisplay)
                            && !withoutQualityAndCountDisplay.Equals(withoutQualityDisplay, StringComparison.CurrentCultureIgnoreCase))
                        {
                            var withoutQualityAndCountLookup = NormalizeLookupKey(withoutQualityAndCountDisplay.TrimEnd('.', ',', ':', ';'));
                            if (!string.IsNullOrEmpty(withoutQualityAndCountLookup)
                                && TryResolveLoreTooltip(withoutQualityAndCountDisplay, withoutQualityAndCountLookup, allowPrefixLookup, out lookupResult))
                            {
                                matchedWithoutQuality = true;
                                matchedWithoutCount = true;
                            }
                        }
                    }
                }

                if (lookupResult == null)
                {
                    // Try stripping parenthetical property suffix like "(невидимый)"
                    // Also handles "item [quality] (prop)" — strip prop first, then quality
                    var withoutPropDisplay = TryStripPropSuffix(displayItemName);
                    if (!string.IsNullOrEmpty(withoutPropDisplay)
                        && !withoutPropDisplay.Equals(displayItemName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var withoutPropLookup = NormalizeLookupKey(withoutPropDisplay.TrimEnd('.', ',', ':', ';'));
                        if (!string.IsNullOrEmpty(withoutPropLookup)
                            && TryResolveLoreTooltip(withoutPropDisplay, withoutPropLookup, allowPrefixLookup, out lookupResult))
                        {
                            matchedWithoutProp = true;
                        }
                        else
                        {
                            // Try prop + quality stripped (e.g. "item [quality] (prop)")
                            var withoutPropAndQualityDisplay = TryStripQualitySuffix(withoutPropDisplay);
                            if (!string.IsNullOrEmpty(withoutPropAndQualityDisplay)
                                && !withoutPropAndQualityDisplay.Equals(withoutPropDisplay, StringComparison.CurrentCultureIgnoreCase))
                            {
                                var withoutPropAndQualityLookup = NormalizeLookupKey(withoutPropAndQualityDisplay.TrimEnd('.', ',', ':', ';'));
                                if (!string.IsNullOrEmpty(withoutPropAndQualityLookup)
                                    && TryResolveLoreTooltip(withoutPropAndQualityDisplay, withoutPropAndQualityLookup, allowPrefixLookup, out lookupResult))
                                {
                                    matchedWithoutProp = true;
                                    matchedWithoutQuality = true;
                                }
                            }
                        }
                    }
                }

                if (lookupResult == null)
                {
                    var withoutCountDisplay = TryStripCountSuffix(displayItemName);
                    if (string.IsNullOrEmpty(withoutCountDisplay)
                        || withoutCountDisplay.Equals(displayItemName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        return false;
                    }

                    // Strip embedded game brackets from display name (e.g. "эссенция из [глаза дракона]")
                    if (withoutCountDisplay.IndexOf('[') >= 0 || withoutCountDisplay.IndexOf(']') >= 0)
                    {
                        withoutCountDisplay = NormalizeWhitespace(withoutCountDisplay.Replace("[", string.Empty).Replace("]", string.Empty));
                    }

                    var withoutCountLookup = NormalizeLookupKey(withoutCountDisplay.TrimEnd('.', ',', ':', ';'));
                    if (string.IsNullOrEmpty(withoutCountLookup)
                        || !TryResolveLoreTooltip(withoutCountDisplay, withoutCountLookup, allowPrefixLookup, out lookupResult))
                    {
                        return false;
                    }

                    matchedWithoutCount = true;
                }
            }

            if (lookupResult.DroppedWordsCount > 0)
            {
                int shiftedStart;
                if (TryGetShiftedStartByWords(sourceText, start, end, lookupResult.DroppedWordsCount, out shiftedStart))
                {
                    start = shiftedStart;
                    int shiftedMarkerStart;
                    if (ConsumeLeadingItemMarkers(sourceText, ref start, end, out shiftedMarkerStart))
                    {
                        markerPrefixStart = shiftedMarkerStart;
                        hadMarkerPrefix = true;
                    }
                }
            }

            if (matchedWithoutQuality)
            {
                var candidateTail = sourceText.Substring(start, end - start);
                var qualitySuffixMatch = _qualitySuffixRx.Match(candidateTail);
                if (qualitySuffixMatch.Success && qualitySuffixMatch.Index + qualitySuffixMatch.Length == candidateTail.Length)
                {
                    end = start + qualitySuffixMatch.Index;
                    TrimRangeWhitespaces(sourceText, ref start, ref end);
                }
            }

            if (matchedWithoutProp)
            {
                var candidateTail = sourceText.Substring(start, end - start);
                var propSuffixMatch = _propSuffixRx.Match(candidateTail);
                if (propSuffixMatch.Success && propSuffixMatch.Index + propSuffixMatch.Length == candidateTail.Length)
                {
                    end = start + propSuffixMatch.Index;
                    TrimRangeWhitespaces(sourceText, ref start, ref end);
                }
            }

            if (matchedWithoutCount)
            {
                var candidateTail = sourceText.Substring(start, end - start);
                var countSuffixMatch = _countSuffixRx.Match(candidateTail);
                if (countSuffixMatch.Success && countSuffixMatch.Index + countSuffixMatch.Length == candidateTail.Length)
                {
                    end = start + countSuffixMatch.Index;
                    TrimRangeWhitespaces(sourceText, ref start, ref end);
                }
            }

            displayItemName = lookupResult.DisplayItemName;
            displayItemName = NormalizeWhitespace(displayItemName.TrimEnd('.', ',', ':', ';'));
            var matchStart = hadMarkerPrefix ? markerPrefixStart : start;
            match = new LoreMatch(matchStart, end, displayItemName, lookupResult.Tooltip);
            return true;
        }

        private static bool IsBetterLoreMatch([NotNull] LoreMatch candidate, [NotNull] LoreMatch currentBest)
        {
            Assert.ArgumentNotNull(candidate, "candidate");
            Assert.ArgumentNotNull(currentBest, "currentBest");

            var candidateLength = candidate.End - candidate.Start;
            var currentLength = currentBest.End - currentBest.Start;
            if (candidateLength != currentLength)
            {
                return candidateLength > currentLength;
            }

            if (candidate.Start != currentBest.Start)
            {
                return candidate.Start < currentBest.Start;
            }

            return candidate.ItemName.Length > currentBest.ItemName.Length;
        }

        [NotNull]
        private static string TryStripCountSuffix([NotNull] string value)
        {
            Assert.ArgumentNotNull(value, "value");

            var normalized = NormalizeWhitespace(value);
            if (normalized.Length == 0)
            {
                return normalized;
            }

            return NormalizeWhitespace(_countSuffixRx.Replace(normalized, string.Empty).Trim());
        }

        [NotNull]
        private static string TryStripQualitySuffix([NotNull] string value)
        {
            Assert.ArgumentNotNull(value, "value");

            var normalized = NormalizeWhitespace(value);
            if (normalized.Length == 0)
            {
                return normalized;
            }

            return NormalizeWhitespace(_qualitySuffixRx.Replace(normalized, string.Empty).Trim());
        }

        [NotNull]
        private static string TryStripPropSuffix([NotNull] string value)
        {
            Assert.ArgumentNotNull(value, "value");

            var normalized = NormalizeWhitespace(value);
            if (normalized.Length == 0)
            {
                return normalized;
            }

            return NormalizeWhitespace(_propSuffixRx.Replace(normalized, string.Empty).Trim());
        }

        private static int FindActionTailStart([NotNull] string sourceText)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            var effectiveEnd = sourceText.Length;
            while (effectiveEnd > 0 && char.IsWhiteSpace(sourceText[effectiveEnd - 1]))
            {
                effectiveEnd--;
            }

            if (effectiveEnd == 0)
            {
                return -1;
            }

            var lastChar = sourceText[effectiveEnd - 1];
            if (lastChar == '.' || lastChar == '!' || lastChar == '?')
            {
                effectiveEnd--;
            }

            if (effectiveEnd <= 0)
            {
                return -1;
            }

            var tailTokens = new[] { " на ", " во ", " в ", " под ", " к " };
            foreach (var token in tailTokens)
            {
                var index = sourceText.LastIndexOf(token, effectiveEnd - 1, effectiveEnd, StringComparison.CurrentCultureIgnoreCase);
                if (index <= 0)
                {
                    continue;
                }

                var tailStart = index + token.Length;
                if (tailStart >= effectiveEnd)
                {
                    continue;
                }

                var tailText = sourceText.Substring(tailStart, effectiveEnd - tailStart).Trim();
                if (tailText.Length == 0 || tailText.IndexOf(',') >= 0)
                {
                    continue;
                }

                var tailWords = SplitWords(tailText);
                if (tailWords.Length == 0 || tailWords.Length > 3)
                {
                    continue;
                }

                return index;
            }

            return -1;
        }

        // Белый список глаголов взаимодействия с предметами.
        // Только строки, где второе слово — один из этих глаголов, могут содержать
        // название lore-предмета в позиции объекта. Все остальные глаголы прошедшего
        // времени (заморозила, оскорбил, поделила, набросился и т.п.) предметов не содержат.
        private static readonly HashSet<string> _itemInteractionVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            // подбор
            "взял", "взяла", "взяли",
            "поднял", "подняла", "подняли",
            "подобрал", "подобрала", "подобрали",
            // выброс / положить
            "бросил", "бросила", "бросили",
            "выбросил", "выбросила", "выбросили",
            "положил", "положила", "положили",
            // экипировка
            "надел", "надела", "надели",
            "снял", "сняла", "сняли",
            "одел", "одела", "одели",
            // передача / торговля
            "дал", "дала", "дали",
            "получил", "получила", "получили",
            "купил", "купила", "купили",
            "продал", "продала", "продали",
            // попытка надеть/снять ("попробовал надеть X", "прекратил носить X")
            "попробовал", "попробовала", "попробовали",
            "прекратил", "прекратила", "прекратили",
        };

        private static bool IsLikelyThirdPersonActionLine([NotNull] MatchCollection words)
        {
            Assert.ArgumentNotNull(words, "words");

            if (words.Count < 2)
            {
                return false;
            }

            var verb = words[1].Value.Trim('.', ',', ':', ';').ToLowerInvariant();
            return verb.Length > 0 && _itemInteractionVerbs.Contains(verb);
        }

        private bool TryResolveLoreTooltip(
            [NotNull] string displayItemName,
            [NotNull] string lookupItemName,
            bool allowPrefixLookup,
            out LoreLookupResult result)
        {
            Assert.ArgumentNotNull(displayItemName, "displayItemName");
            Assert.ArgumentNotNull(lookupItemName, "lookupItemName");

            result = null;
            lookupItemName = StripNonItemPrefix(lookupItemName);
            if (string.IsNullOrEmpty(lookupItemName))
            {
                return false;
            }

            LoreTooltip loreTooltip;
            if (_loreTooltipsByObjectName.TryGetValue(lookupItemName, out loreTooltip))
            {
                // Server sometimes abbreviates item names (e.g. sending only the first two words).
                // If exactly one longer variant exists in the DB starting with this key + separator,
                // prefer it — it is unambiguously more specific than the short lookup key.
                var len = lookupItemName.Length;
                string uniqueLongerKey = null;
                bool ambiguous = false;
                foreach (var key in _loreTooltipsByObjectName.Keys)
                {
                    if (key.Length > len
                        && key.StartsWith(lookupItemName, StringComparison.CurrentCultureIgnoreCase)
                        && !char.IsLetterOrDigit(key[len]))
                    {
                        if (uniqueLongerKey == null)
                            uniqueLongerKey = key;
                        else
                        {
                            ambiguous = true;
                            break;
                        }
                    }
                }
                if (!ambiguous && uniqueLongerKey != null)
                {
                    LoreTooltip longerTooltip;
                    if (_loreTooltipsByObjectName.TryGetValue(uniqueLongerKey, out longerTooltip))
                    {
                        loreTooltip = longerTooltip;
                        displayItemName = uniqueLongerKey;
                    }
                }
                result = new LoreLookupResult(displayItemName, loreTooltip, 0);
                return true;
            }

            if (IsNegativeLoreLookupCached(lookupItemName, allowPrefixLookup))
            {
                return false;
            }

            foreach (var normalized in BuildPhraseVariants(lookupItemName))
            {
                if (_loreTooltipsByObjectName.TryGetValue(normalized, out loreTooltip))
                {
                    // Keep source text in brackets, only use normalized name for tooltip lookup.
                    result = new LoreLookupResult(displayItemName, loreTooltip, 0);
                    return true;
                }
            }

            if (allowPrefixLookup && TryResolveLoreByPrefix(lookupItemName, out loreTooltip))
            {
                result = new LoreLookupResult(displayItemName, loreTooltip, 0);
                return true;
            }

            var words = SplitWords(lookupItemName);
            if (words.Length <= 1)
            {
                RememberNegativeLoreLookup(lookupItemName, allowPrefixLookup);
                return false;
            }

            var maxDropCount = Math.Min(words.Length - 1, 4);
            for (var dropCount = 1; dropCount <= maxDropCount; dropCount++)
            {
                var candidateDisplay = string.Join(" ", words.Skip(dropCount));
                if (candidateDisplay.Length < 3)
                {
                    continue;
                }

                candidateDisplay = StripNonItemPrefix(candidateDisplay);
                if (candidateDisplay.Length < 3)
                {
                    continue;
                }

                if (_loreTooltipsByObjectName.TryGetValue(candidateDisplay, out loreTooltip))
                {
                    result = new LoreLookupResult(candidateDisplay, loreTooltip, dropCount);
                    return true;
                }

                foreach (var normalizedCandidate in BuildPhraseVariants(candidateDisplay))
                {
                    if (!_loreTooltipsByObjectName.TryGetValue(normalizedCandidate, out loreTooltip))
                    {
                        continue;
                    }

                    result = new LoreLookupResult(candidateDisplay, loreTooltip, dropCount);
                    return true;
                }

                if (allowPrefixLookup && TryResolveLoreByPrefix(candidateDisplay, out loreTooltip))
                {
                    result = new LoreLookupResult(candidateDisplay, loreTooltip, dropCount);
                    return true;
                }
            }

            RememberNegativeLoreLookup(lookupItemName, allowPrefixLookup);
            return false;
        }

        private bool IsNegativeLoreLookupCached([NotNull] string lookupItemName, bool allowPrefixLookup)
        {
            Assert.ArgumentNotNull(lookupItemName, "lookupItemName");

            return _negativeLoreLookupKeys.Contains(GetNegativeLoreLookupKey(lookupItemName, allowPrefixLookup));
        }

        private void RememberNegativeLoreLookup([NotNull] string lookupItemName, bool allowPrefixLookup)
        {
            Assert.ArgumentNotNull(lookupItemName, "lookupItemName");

            if (lookupItemName.Length == 0 || lookupItemName.Length > MaxNegativeLoreLookupKeyLength)
            {
                return;
            }

            if (_negativeLoreLookupKeys.Count >= MaxNegativeLoreLookupCacheSize)
            {
                ResetNegativeLoreLookupCache();
            }

            _negativeLoreLookupKeys.Add(GetNegativeLoreLookupKey(lookupItemName, allowPrefixLookup));
        }

        [NotNull]
        private static string GetNegativeLoreLookupKey([NotNull] string lookupItemName, bool allowPrefixLookup)
        {
            Assert.ArgumentNotNull(lookupItemName, "lookupItemName");

            return (allowPrefixLookup ? "P:" : "E:") + lookupItemName;
        }

        private void ResetNegativeLoreLookupCache()
        {
            _negativeLoreLookupKeys.Clear();
            _linePositiveCache.Clear();
            _lineNegativeCache.Clear();
        }

        /// <summary>
        /// Нормализует строку для строчного негативного кэша:
        /// заменяет последовательности цифр на "#".
        /// "Вы нанесли 47 урона" → "Вы нанесли # урона"
        /// </summary>
        private static string NormalizeDigitsForLineCache(string s)
        {
            if (s == null) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            bool inDigit = false;
            foreach (char c in s)
            {
                if (c >= '0' && c <= '9')
                {
                    if (!inDigit) { sb.Append('#'); inDigit = true; }
                }
                else
                {
                    sb.Append(c);
                    inDigit = false;
                }
            }
            return sb.ToString();
        }

        [NotNull]
        private static IEnumerable<string> BuildPhraseVariants([NotNull] string itemName)
        {
            Assert.ArgumentNotNull(itemName, "itemName");

            var words = SplitWords(itemName);
            if (words.Length == 0)
            {
                yield break;
            }

            var variants = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase) { itemName };
            var wordsToNormalize = Math.Min(words.Length, 4);
            var maxNormalizationPasses = words.Length <= 3 ? 3 : 2;

            for (var pass = 0; pass < maxNormalizationPasses; pass++)
            {
                var snapshot = variants.ToList();
                foreach (var phrase in snapshot)
                {
                    var phraseWords = SplitWords(phrase);
                    var normalizeLimit = Math.Min(phraseWords.Length, wordsToNormalize);
                    for (var i = 0; i < normalizeLimit; i++)
                    {
                        var sourceWord = phraseWords[i];
                        foreach (var normalizedWord in BuildWordVariants(sourceWord))
                        {
                            if (normalizedWord.Equals(sourceWord, StringComparison.CurrentCultureIgnoreCase))
                            {
                                continue;
                            }

                            var candidateWords = (string[])phraseWords.Clone();
                            candidateWords[i] = normalizedWord;
                            var candidatePhrase = string.Join(" ", candidateWords);
                            variants.Add(candidatePhrase);
                        }
                    }
                }
            }

            foreach (var variant in variants)
            {
                if (!variant.Equals(itemName, StringComparison.CurrentCultureIgnoreCase))
                {
                    yield return variant;
                }
            }
        }

        [NotNull]
        private static IEnumerable<string> BuildWordVariants([NotNull] string word)
        {
            Assert.ArgumentNotNull(word, "word");

            var variants = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            Func<string, bool> addVariant = value =>
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                return variants.Add(value);
            };

            if (word.Length > 3 && word.EndsWith("ого", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 3);
                addVariant(stem + "ый");
                addVariant(stem + "ой");
                addVariant(stem + "ий");
            }

            if (word.Length > 3 && word.EndsWith("его", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 3);
                addVariant(stem + "ий");
                addVariant(stem + "ой");
            }

            if (word.Length > 3 && word.EndsWith("ому", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 3);
                addVariant(stem + "ый");
                addVariant(stem + "ой");
                addVariant(stem + "ий");
            }

            if (word.Length > 3 && word.EndsWith("ему", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 3);
                addVariant(stem + "ий");
                addVariant(stem + "ой");
            }

            if (word.Length > 3 && word.EndsWith("ыми", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 3);
                addVariant(stem + "ые");
                addVariant(stem + "ый");
                addVariant(stem + "ой");
            }

            if (word.Length > 3 && word.EndsWith("ими", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 3);
                addVariant(stem + "ие");
                addVariant(stem + "ий");
                addVariant(stem + "ой");
            }

            if (word.Length > 2 && word.EndsWith("ых", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 2);
                addVariant(stem + "ые");
                addVariant(stem + "ый");
                addVariant(stem + "ой");
            }

            if (word.Length > 2 && word.EndsWith("их", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 2);
                addVariant(stem + "ие");
                addVariant(stem + "ий");
                addVariant(stem + "ой");
            }

            if (word.Length > 2 && word.EndsWith("ым", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 2);
                addVariant(stem + "ый");
                addVariant(stem + "ой");
            }

            if (word.Length > 2 && word.EndsWith("им", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 2);
                addVariant(stem + "ий");
                addVariant(stem + "ой");
            }

            if (word.Length > 2 && word.EndsWith("ую", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 2);
                addVariant(stem + "ая");
                addVariant(stem + "яя");
            }

            if (word.Length > 2 && word.EndsWith("юю", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 2);
                addVariant(stem + "яя");
            }

            if (word.Length > 2 && word.EndsWith("ой", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 2);
                addVariant(stem + "ая");
                addVariant(stem + "а");
            }

            if (word.Length > 2 && word.EndsWith("ей", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 2);
                addVariant(stem + "яя");
                addVariant(stem + "я");
            }

            if (word.Length > 1 && word.EndsWith("у", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 1);
                addVariant(stem + "а");
                addVariant(stem + "я");
            }

            if (word.Length > 1 && word.EndsWith("ю", StringComparison.CurrentCultureIgnoreCase))
            {
                var stem = word.Substring(0, word.Length - 1);
                addVariant(stem + "я");
            }

            if (word.Length > 2 && word.EndsWith("ом", StringComparison.CurrentCultureIgnoreCase))
            {
                addVariant(word.Substring(0, word.Length - 2));
            }

            if (word.Length > 2 && word.EndsWith("ем", StringComparison.CurrentCultureIgnoreCase))
            {
                addVariant(word.Substring(0, word.Length - 2));
            }

            if (word.Length > 3 && word.EndsWith("а", StringComparison.CurrentCultureIgnoreCase))
            {
                var previous = char.ToLowerInvariant(word[word.Length - 2]);
                if ("кгхчщжш".IndexOf(previous) < 0)
                {
                    addVariant(word.Substring(0, word.Length - 1));
                }
            }

            if (word.Length > 3 && word.EndsWith("я", StringComparison.CurrentCultureIgnoreCase))
            {
                addVariant(word.Substring(0, word.Length - 1));
            }

            foreach (var variant in variants)
            {
                yield return variant;
            }
        }

        private bool TryResolveLoreByPrefix([NotNull] string prefix, out LoreTooltip loreTooltip)
        {
            Assert.ArgumentNotNull(prefix, "prefix");

            loreTooltip = null;
            var normalizedPrefix = StripNonItemPrefix(NormalizeLookupKey(prefix));
            if (normalizedPrefix.Length < 4)
            {
                return false;
            }

            var prefixWords = SplitWords(normalizedPrefix);
            if (prefixWords.Length < 2)
            {
                return false;
            }

            string matchedKey = null;
            foreach (var key in _loreTooltipsByObjectName.Keys)
            {
                if (key.Length <= normalizedPrefix.Length)
                {
                    continue;
                }

                if (!key.StartsWith(normalizedPrefix, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                if (matchedKey != null)
                {
                    // Allow duplicate if both keys point to the same tooltip object
                    // (e.g. "item (x5)" and "item" added by count-strip indexing)
                    LoreTooltip existingTooltip, newTooltip;
                    if (_loreTooltipsByObjectName.TryGetValue(matchedKey, out existingTooltip)
                        && _loreTooltipsByObjectName.TryGetValue(key, out newTooltip)
                        && ReferenceEquals(existingTooltip, newTooltip))
                    {
                        continue;
                    }

                    return false;
                }

                matchedKey = key;
            }

            return matchedKey != null && _loreTooltipsByObjectName.TryGetValue(matchedKey, out loreTooltip);
        }

        [NotNull]
        private static string NormalizeWhitespace([NotNull] string value)
        {
            Assert.ArgumentNotNull(value, "value");
            return _anyWhiteSpaceRx.Replace(value, " ").Trim();
        }

        [NotNull]
        private static string[] SplitWords([NotNull] string value)
        {
            Assert.ArgumentNotNull(value, "value");
            return _wordRx.Matches(value).Cast<Match>().Select(match => match.Value).ToArray();
        }

        [NotNull]
        private static string NormalizeLookupKey([NotNull] string value)
        {
            Assert.ArgumentNotNull(value, "value");
            var normalized = NormalizeWhitespace(value);
            if (normalized.Length == 0)
            {
                return normalized;
            }

            // Strip embedded game-output brackets (e.g. "[\u0433\u043B\u0430\u0437\u0430 \u0434\u0440\u0430\u043A\u043E\u043D\u0430]" inside item names).
            // This covers all fallback lookup paths automatically.
            if (normalized.IndexOf('[') >= 0 || normalized.IndexOf(']') >= 0)
            {
                normalized = NormalizeWhitespace(normalized.Replace("[", string.Empty).Replace("]", string.Empty));
                if (normalized.Length == 0)
                {
                    return normalized;
                }
            }

            bool hasCyrillic = normalized.Any(ch => ch >= '\u0400' && ch <= '\u04FF');
            bool hasLatin = normalized.Any(ch => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'));
            if (!hasCyrillic || !hasLatin)
            {
                return normalized;
            }

            var chars = normalized.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                char mapped;
                if (_latinToCyrillicHomoglyphs.TryGetValue(chars[i], out mapped))
                {
                    chars[i] = mapped;
                }
            }

            return new string(chars);
        }

        [NotNull]
        private static string StripNonItemPrefix([NotNull] string value)
        {
            Assert.ArgumentNotNull(value, "value");

            var words = SplitWords(value);
            if (words.Length == 0)
            {
                return string.Empty;
            }

            var markerIndex = -1;
            for (var i = 0; i < words.Length; i++)
            {
                var token = words[i].TrimStart('(', ')', '[', ']', '{', '}', '"', '\'', '«', '»');
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (token[0] == '!' || token[0] == '#' || token[0] == '*' || token[0] == '-')
                {
                    markerIndex = i;
                    break;
                }
            }

            if (markerIndex > 0)
            {
                words = words.Skip(markerIndex).ToArray();
            }

            if (words.Length == 0)
            {
                return string.Empty;
            }

            words[0] = words[0].TrimStart('!', '#', '*', '-');
            return NormalizeWhitespace(string.Join(" ", words));
        }

        private static bool TryGetShiftedStartByWords([NotNull] string sourceText, int start, int end, int skippedWords, out int shiftedStart)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            shiftedStart = start;
            if (skippedWords <= 0 || end <= start)
            {
                return false;
            }

            var rangeText = sourceText.Substring(start, end - start);
            var matches = _wordRx.Matches(rangeText);
            if (matches.Count <= skippedWords)
            {
                return false;
            }

            shiftedStart = start + matches[skippedWords].Index;
            return true;
        }

        private static bool ConsumeLeadingItemMarkers([NotNull] string sourceText, ref int start, int end, out int markerStart)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            markerStart = start;
            var hadMarker = false;
            while (start < end && (sourceText[start] == '!' || sourceText[start] == '#' || sourceText[start] == '*' || sourceText[start] == '-'))
            {
                hadMarker = true;
                start++;
                while (start < end && sourceText[start] == ' ')
                {
                    start++;
                }
            }

            return hadMarker;
        }

        private static bool TryGetRemovedLeadingWordsCount([NotNull] string originalText, [NotNull] string strippedText, out int removedWordsCount)
        {
            Assert.ArgumentNotNull(originalText, "originalText");
            Assert.ArgumentNotNull(strippedText, "strippedText");

            removedWordsCount = 0;
            var originalWords = SplitWords(originalText);
            var strippedWords = SplitWords(strippedText);
            if (originalWords.Length == 0 || strippedWords.Length == 0 || strippedWords.Length > originalWords.Length)
            {
                return false;
            }

            var offset = originalWords.Length - strippedWords.Length;
            for (var i = 0; i < strippedWords.Length; i++)
            {
                if (!originalWords[i + offset].TrimStart('!', '#', '*', '-').Equals(strippedWords[i], StringComparison.CurrentCultureIgnoreCase))
                {
                    return false;
                }
            }

            removedWordsCount = offset;
            return true;
        }

        private static void TrimRangeWhitespaces([NotNull] string sourceText, ref int start, ref int end)
        {
            Assert.ArgumentNotNull(sourceText, "sourceText");

            while (start < end && char.IsWhiteSpace(sourceText[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(sourceText[end - 1]))
            {
                end--;
            }
        }

        private static bool ShouldSkipColumnCandidate([NotNull] string text)
        {
            Assert.ArgumentNotNull(text, "text");

            var normalized = text.Trim();
            if (normalized.Length == 0)
            {
                return true;
            }

            if (normalized.Equals("мало", StringComparison.CurrentCultureIgnoreCase)
                || normalized.Equals("средне", StringComparison.CurrentCultureIgnoreCase)
                || normalized.Equals("много", StringComparison.CurrentCultureIgnoreCase)
                || normalized.Equals("ставок нет", StringComparison.CurrentCultureIgnoreCase)
                || normalized.Equals("свободен", StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            return _digitsOnlyRx.IsMatch(normalized);
        }

        [NotNull]
        private static LoreTooltip CreateLoreTooltip([NotNull] LoreMessage loreMessage)
        {
            Assert.ArgumentNotNull(loreMessage, "loreMessage");

            var tooltipBuilder = new StringBuilder();
            var tooltipLines = new List<TextMessageBlock.ToolTipLine>();
            foreach (var infoMessage in loreMessage.ConvertToMessages())
            {
                if (tooltipBuilder.Length > 0)
                {
                    tooltipBuilder.AppendLine();
                }

                tooltipBuilder.Append(infoMessage.InnerText);

                if (infoMessage.MessageBlocks.Count > 0)
                {
                    tooltipLines.Add(new TextMessageBlock.ToolTipLine(infoMessage.MessageBlocks));
                }
            }

            return new LoreTooltip(tooltipBuilder.ToString(), tooltipLines);
        }

        private sealed class LoreTooltip
        {
            public LoreTooltip([NotNull] string plainText, [CanBeNull] IList<TextMessageBlock.ToolTipLine> lines)
            {
                Assert.ArgumentNotNull(plainText, "plainText");
                PlainText = plainText;
                Lines = lines;
            }

            [NotNull]
            public string PlainText
            {
                get;
                private set;
            }

            [CanBeNull]
            public IList<TextMessageBlock.ToolTipLine> Lines
            {
                get;
                private set;
            }
        }

        private sealed class LoreMatch
        {
            public LoreMatch(int start, int end, [NotNull] string itemName, [NotNull] LoreTooltip tooltip)
            {
                Assert.ArgumentNotNull(itemName, "itemName");
                Assert.ArgumentNotNull(tooltip, "tooltip");

                Start = start;
                End = end;
                ItemName = itemName;
                Tooltip = tooltip;
            }

            public int Start
            {
                get;
                private set;
            }

            public int End
            {
                get;
                private set;
            }

            [NotNull]
            public string ItemName
            {
                get;
                private set;
            }

            [NotNull]
            public LoreTooltip Tooltip
            {
                get;
                private set;
            }
        }

        private sealed class LoreLookupResult
        {
            public LoreLookupResult([NotNull] string displayItemName, [NotNull] LoreTooltip tooltip, int droppedWordsCount)
            {
                Assert.ArgumentNotNull(displayItemName, "displayItemName");
                Assert.ArgumentNotNull(tooltip, "tooltip");

                DisplayItemName = displayItemName;
                Tooltip = tooltip;
                DroppedWordsCount = droppedWordsCount;
            }

            [NotNull]
            public string DisplayItemName
            {
                get;
                private set;
            }

            [NotNull]
            public LoreTooltip Tooltip
            {
                get;
                private set;
            }

            public int DroppedWordsCount
            {
                get;
                private set;
            }
        }

        private sealed class ColumnCandidate
        {
            public ColumnCandidate(int start, int end)
            {
                Start = start;
                End = end;
            }

            public int Start
            {
                get;
                private set;
            }

            public int End
            {
                get;
                private set;
            }
        }

        private sealed class RangeCandidate
        {
            public RangeCandidate(int start, int end)
            {
                Start = start;
                End = end;
            }

            public int Start
            {
                get;
                private set;
            }

            public int End
            {
                get;
                private set;
            }
        }

        private sealed class BlockSpan
        {
            public TextMessageBlock Block
            {
                get;
                set;
            }

            public int Start
            {
                get;
                set;
            }

            public int End
            {
                get;
                set;
            }
        }

    }
}
