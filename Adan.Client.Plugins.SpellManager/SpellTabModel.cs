using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Adan.Client.Common.Commands;
using Adan.Client.Common.Model;
using Adan.Client.Common.Settings;

namespace Adan.Client.Plugins.SpellManager
{
    /// <summary>
    /// Core per-tab model: holds spell data and parsing logic.
    /// </summary>
    public class SpellTabModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
        // ---- Pre-compiled regexes ----
        private static readonly Regex _zauсhSpellEntry = new Regex(@"\[\s*(\d+)\]([^\[]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _zauchLinePrefix = new Regex(@"^(\d+):\s", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _freeSlots = new Regex(@"(\d+)-(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _circleHeader = new Regex(@"-==\s*Круг\s+(\d+)\s*==-", RegexOptions.Compiled);
        // Matches: "   благословение 100% (совершенно)" и "   восстановление 150% *(божественно)"
        private static readonly Regex _spellLine = new Regex(@"^\s+(.+?)\s+\d+%\s*\*?\s*\(", RegexOptions.Compiled);

        // Events — кавычки: прямые ' " и типографские ' ' " "
        private const string Q = @"['‘’""“”]";
        private static readonly Regex _reReady  = new Regex(@"^Вы теперь готовы произнести заклинание " + Q + @"(.+)" + Q + @"\.$", RegexOptions.Compiled);
        private static readonly Regex _reCast   = new Regex(@"^Вы произнесли магические слова: " + Q + @"(.+)" + Q + @"\.$", RegexOptions.Compiled);
        private static readonly Regex _reForgot = new Regex(@"^Вы успешно забыли заклинание " + Q + @"(.+)" + Q + @"\.$", RegexOptions.Compiled);
        private static readonly Regex _reAdded  = new Regex(@"^Вы добавили заклинание " + Q + @"(.+)" + Q + @" в список для запоминания\.$", RegexOptions.Compiled);
        private static readonly Regex _reRemoved= new Regex(@"^Вы убрали заклинание " + Q + @"(.+)" + Q + @" из вашего списка\.$", RegexOptions.Compiled);
        private static readonly Regex _reDied = new Regex(@"^Вы умерли\.$", RegexOptions.Compiled);
        // "Список заклинаний для запоминания:" entry: "1. заживление (8)"
        private static readonly Regex _zauchQueueEntry = new Regex(@"^\d+\.\s+(.+?)\s+\(\d+\)$", RegexOptions.Compiled);

        // Parser state
        private enum ParseState { None, ParsingZauch, ParsingZauchQueue, ParsingZakl }
        private ParseState _state = ParseState.None;

        private void SetState(ParseState state)
        {
            _state = state;
        }
        private int _currentZauchCircle = 0;
        private int _currentZaklCircle = 0;

        public string Uid { get; private set; }
        public ObservableCollection<SpellEntry> Spells { get; private set; }

        // Отладочный флаг: отображается в UI чтобы видеть когда конвейер считает себя внутри секции зауч/закл
        private bool _isInSection;
        public bool IsInSection
        {
            get { return _isInSection; }
            set
            {
                if (_isInSection != value)
                {
                    _isInSection = value;
                    OnPropertyChanged("IsInSection");
                }
            }
        }

        private int _counterFontSize = 11;
        public int CounterFontSize
        {
            get { return _counterFontSize; }
            set
            {
                if (_counterFontSize != value)
                {
                    _counterFontSize = value;
                    OnPropertyChanged("CounterFontSize");
                    Save();
                }
            }
        }

        public void IncreaseFontSize() { if (CounterFontSize < 24) CounterFontSize++; }
        public void DecreaseFontSize() { if (CounterFontSize > 7) CounterFontSize--; }

        // Круг → свободные слоты (только те круги, которые есть у персонажа)
        public Dictionary<int, int> FreeSlots { get; private set; }

        // Expose for the slots display string
        public string FreeSlotsDisplay
        {
            get
            {
                if (FreeSlots.Count == 0) return string.Empty;
                var parts = new List<string>();
                foreach (var kv in FreeSlots)
                    parts.Add(string.Format("{0}:{1}", kv.Key, kv.Value));
                return string.Join("  ", parts);
            }
        }

        public SpellTabModel(string uid)
        {
            Uid = uid;
            Spells = new ObservableCollection<SpellEntry>();
            FreeSlots = new Dictionary<int, int>();

            // Авто-сохранение при изменении настроек (Desired, IsTrackedInCounter)
            Spells.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (SpellEntry entry in e.NewItems)
                        entry.PropertyChanged += OnSpellPropertyChanged;
                if (e.OldItems != null)
                    foreach (SpellEntry entry in e.OldItems)
                        entry.PropertyChanged -= OnSpellPropertyChanged;
            };
        }

        private void OnSpellPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Desired" || e.PropertyName == "IsTrackedInCounter" || e.PropertyName == "IsTrackedGlobally")
                Save();
        }

        // ---- Text parsing ----

        public void HandleLine(string line, RootModel rootModel)
        {
            if (line == null) return;

            // Check for state transitions
            if (line == "Заученные заклинания:")
            {
                SetState(ParseState.ParsingZauch);
                // Reset memorizing/memorized counts but keep known spells
                foreach (var sp in Spells)
                {
                    sp.Memorized = 0;
                    sp.Memorizing = 0;
                }
                return;
            }

            // Когда нет ни одного заученного заклинания — всё равно сбрасываем счётчики
            // и переходим в состояние парсинга чтобы поймать строку "Осталось слотов"
            if (line == "У вас нет заученных заклинаний.")
            {
                SetState(ParseState.ParsingZauch);
                foreach (var sp in Spells)
                {
                    sp.Memorized = 0;
                    sp.Memorizing = 0;
                }
                return;
            }

            if (line == "Ваши заклинания:")
            {
                SetState(ParseState.ParsingZakl);
                _currentZaklCircle = 0;
                // Не очищаем список — только обновляем круги и добавляем новые заклинания.
                // Данные о заученных/учащихся (из зауч) должны сохраняться.
                return;
            }

            if (line == "Список заклинаний для запоминания:")
            {
                // Сбрасываем Memorizing — сейчас пересчитаем из очереди
                foreach (var sp in Spells)
                    sp.Memorizing = 0;
                SetState(ParseState.ParsingZauchQueue);
                return;
            }

            // Событийные сообщения обрабатываются всегда, независимо от состояния парсера.
            // Они могут прийти в любой момент, в том числе во время разбора вывода зауч.
            if (HandleEventLine(line, rootModel)) return;

            switch (_state)
            {
                case ParseState.ParsingZauch:
                    HandleZauchLine(line);
                    break;
                case ParseState.ParsingZauchQueue:
                    HandleZauchQueueLine(line);
                    break;
                case ParseState.ParsingZakl:
                    HandleZaklLine(line);
                    break;
            }
        }

        private void HandleZauchLine(string line)
        {
            // "Осталось слотов (круг-колво): 1-0 2-3 ..."
            if (line.StartsWith("Осталось слотов (круг-колво):"))
            {
                FreeSlots.Clear();
                var slotMatches = _freeSlots.Matches(line);
                foreach (Match m in slotMatches)
                {
                    int circle = int.Parse(m.Groups[1].Value);
                    int count = int.Parse(m.Groups[2].Value);
                    FreeSlots[circle] = count;
                }
                SetState(ParseState.None);
                OnPropertyChanged("FreeSlotsDisplay");
                return;
            }

            // Lines like "1: [ 3]благословение   [ 3]создать воду"
            var prefixMatch = _zauchLinePrefix.Match(line);
            if (prefixMatch.Success)
            {
                _currentZauchCircle = int.Parse(prefixMatch.Groups[1].Value);
                ParseZauchSpells(line);
                return;
            }

            // Lines like "   [ 1]духовная аура" (continuation)
            if (line.StartsWith("   [") || line.StartsWith("   "))
            {
                ParseZauchSpells(line);
                return;
            }
        }

        private void HandleZauchQueueLine(string line)
        {
            // Конец секции — строка со слотами или "Вы планируете..."
            if (line.StartsWith("Осталось слотов (круг-колво):"))
            {
                FreeSlots.Clear();
                var slotMatches = _freeSlots.Matches(line);
                foreach (Match m in slotMatches)
                {
                    int circle = int.Parse(m.Groups[1].Value);
                    int count = int.Parse(m.Groups[2].Value);
                    FreeSlots[circle] = count;
                }
                SetState(ParseState.None);
                OnPropertyChanged("FreeSlotsDisplay");
                return;
            }

            // "N. заживление (8)"
            var m2 = _zauchQueueEntry.Match(line);
            if (m2.Success)
            {
                string name = m2.Groups[1].Value.Trim().ToLowerInvariant();
                var entry = FindOrCreateSpell(name);
                entry.Memorizing++;
                return;
            }

            // Blank lines, "Вы планируете окончить занятия через X минут." и т.п. — пропускаем
        }

        private void ParseZauchSpells(string line)
        {
            var matches = _zauсhSpellEntry.Matches(line);
            foreach (Match m in matches)
            {
                int count = int.Parse(m.Groups[1].Value);
                string name = m.Groups[2].Value.Trim().ToLowerInvariant();
                // Find existing entry (from zakl), or create new
                var entry = Spells.FirstOrDefault(s => s.Name == name);
                if (entry == null)
                {
                    entry = new SpellEntry { Name = name, Circle = _currentZauchCircle };
                    Spells.Add(entry);
                }
                else if (_currentZauchCircle > 0)
                {
                    entry.Circle = _currentZauchCircle;
                }
                entry.Memorized += count;
            }
        }

        private void HandleZaklLine(string line)
        {
            // Skip blank/whitespace lines
            if (string.IsNullOrWhiteSpace(line))
                return;

            // Финальная строка вывода "закл" — завершаем парсинг
            if (line.StartsWith("Наберите"))
            {
                SetState(ParseState.None);
                return;
            }

            // Circle header: -== Круг N ==-
            var circleMatch = _circleHeader.Match(line);
            if (circleMatch.Success)
            {
                _currentZaklCircle = int.Parse(circleMatch.Groups[1].Value);
                return;
            }

            // Spell line: "     благословение 100% (совершенно)"
            var spellMatch = _spellLine.Match(line);
            if (spellMatch.Success)
            {
                string name = spellMatch.Groups[1].Value.Trim().ToLowerInvariant();
                var existing = Spells.FirstOrDefault(s => s.Name == name);
                if (existing == null)
                {
                    Spells.Add(new SpellEntry { Name = name, Circle = _currentZaklCircle });
                }
                else
                {
                    existing.Circle = _currentZaklCircle;
                }
                return;
            }

            // Остальные строки — пропускаем молча.
            // (Сброс в None происходит по явному финальному тригеру "Наберите"
            //  или при начале нового зауч/закл.)
        }

        private bool HandleEventLine(string line, RootModel rootModel)
        {
            Match m;

            m = _reReady.Match(line);
            if (m.Success)
            {
                var name = m.Groups[1].Value.ToLowerInvariant();
                var entry = FindOrCreateSpell(name);
                if (entry != null)
                {
                    entry.Memorized++;
                    if (entry.Memorizing > 0) entry.Memorizing--;
                }
                return true;
            }

            m = _reCast.Match(line);
            if (m.Success)
            {
                var name = m.Groups[1].Value.ToLowerInvariant();
                var entry = FindSpell(name);
                if (entry != null && entry.Memorized > 0)
                    entry.Memorized--;
                // Memorizing++ НЕ делаем здесь — это делает _reAdded когда придёт
                // "Вы добавили заклинание..." от авто-зауч. Иначе двойной счёт.
                return true;
            }

            m = _reForgot.Match(line);
            if (m.Success)
            {
                var name = m.Groups[1].Value.ToLowerInvariant();
                var entry = FindSpell(name);
                if (entry != null && entry.Memorized > 0)
                    entry.Memorized--;
                return true;
            }

            m = _reAdded.Match(line);
            if (m.Success)
            {
                var name = m.Groups[1].Value.ToLowerInvariant();
                var entry = FindOrCreateSpell(name);
                if (entry != null) entry.Memorizing++;
                return true;
            }

            m = _reRemoved.Match(line);
            if (m.Success)
            {
                var name = m.Groups[1].Value.ToLowerInvariant();
                var entry = FindSpell(name);
                if (entry != null && entry.Memorizing > 0) entry.Memorizing--;
                return true;
            }

            if (_reDied.IsMatch(line))
            {
                ResetAfterDeath();
                return true;
            }

            return false;
        }

        private SpellEntry FindSpell(string nameLower)
        {
            return Spells.FirstOrDefault(s => s.Name == nameLower);
        }

        private SpellEntry FindOrCreateSpell(string nameLower)
        {
            var entry = FindSpell(nameLower);
            if (entry == null)
            {
                entry = new SpellEntry { Name = nameLower, Circle = 0 };
                Spells.Add(entry);
            }
            return entry;
        }

        // ---- Actions ----

        public void ResetAfterDeath()
        {
            foreach (var sp in Spells)
                sp.Memorized = 0;
        }

        /// <summary>
        /// For each spell where Desired > (Memorized + Memorizing), send N copies of "зауч !spellname".
        /// Tracks available free slots as we schedule.
        /// </summary>
        public void ExecutePlan(RootModel rootModel)
        {
            if (rootModel == null) return;

            // Copy current free slots so we can decrement as we schedule
            var available = new Dictionary<int, int>(FreeSlots);

            foreach (var spell in Spells)
            {
                int needed = spell.Desired - spell.Total;
                if (needed <= 0) continue;

                int circle = spell.Circle;
                if (circle <= 0) continue;

                int free;
                if (!available.TryGetValue(circle, out free) || free <= 0) continue;

                int canSchedule = Math.Min(needed, free);
                for (int i = 0; i < canSchedule; i++)
                    rootModel.PushCommandToConveyor(new TextCommand("зауч !" + spell.Name));
                available[circle] -= canSchedule;
            }
            rootModel.PushCommandToConveyor(FlushOutputQueueCommand.Instance);
        }

        /// <summary>
        /// Smart rebalance:
        /// - Если Total > Desired: сначала отменяем лишние из очереди (зауч стоп),
        ///   потом забываем лишние из заученных (зауч забыть).
        /// - Если Total &lt; Desired: дозаучиваем нужное количество.
        /// - Total == Desired: ничего не делаем.
        /// </summary>
        public void ForgetAndRememorize(RootModel rootModel)
        {
            if (rootModel == null) return;

            foreach (var spell in Spells)
            {
                int diff = spell.Total - spell.Desired;
                if (diff == 0) continue;

                if (diff > 0)
                {
                    // Сначала отменяем лишнее из очереди (зауч стоп — по одной)
                    int stopCount = Math.Min(diff, spell.Memorizing);
                    for (int i = 0; i < stopCount; i++)
                        rootModel.PushCommandToConveyor(new TextCommand("зауч стоп !" + spell.Name));

                    // Остаток забываем из заученных (зауч забыть — тоже по одной)
                    int forgetCount = diff - stopCount;
                    for (int i = 0; i < forgetCount; i++)
                        rootModel.PushCommandToConveyor(new TextCommand("зауч забыть !" + spell.Name));
                }
                else
                {
                    // Нужно дозаучить
                    if (spell.Circle <= 0) continue;
                    for (int i = 0; i < -diff; i++)
                        rootModel.PushCommandToConveyor(new TextCommand("зауч !" + spell.Name));
                }
            }
            rootModel.PushCommandToConveyor(FlushOutputQueueCommand.Instance);
        }

        // ---- Persistence ----

        private string GetSavePath()
        {
            var folder = Path.Combine(SettingsHolder.Instance.Folder, "SpellManager");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, Uid + "_plan.xml");
        }

        public void Save()
        {
            try
            {
                var path = GetSavePath();
                var doc = new XDocument(
                    new XElement("SpellPlan",
                        new XAttribute("counterFontSize", _counterFontSize),
                        Spells.Select(sp =>
                            new XElement("Spell",
                                new XAttribute("name", sp.Name),
                                new XAttribute("circle", sp.Circle),
                                new XAttribute("desired", sp.Desired),
                                new XAttribute("tracked", sp.IsTrackedInCounter),
                                new XAttribute("global", sp.IsTrackedGlobally)
                            )
                        )
                    )
                );
                doc.Save(path);
            }
            catch (Exception)
            {
                // Best effort
            }
        }

        public void Load()
        {
            try
            {
                var path = GetSavePath();
                if (!File.Exists(path)) return;

                var doc = XDocument.Load(path);
                if (doc.Root == null) return;

                _counterFontSize = (int?)doc.Root.Attribute("counterFontSize") ?? 11;

                foreach (var el in doc.Root.Elements("Spell"))
                {
                    string name = (string)el.Attribute("name") ?? string.Empty;
                    int circle = (int?)el.Attribute("circle") ?? 0;
                    int desired = (int?)el.Attribute("desired") ?? 0;
                    bool tracked = (bool?)el.Attribute("tracked") ?? false;
                    bool global = (bool?)el.Attribute("global") ?? false;

                    if (string.IsNullOrEmpty(name)) continue;

                    var existing = Spells.FirstOrDefault(s => s.Name == name);
                    if (existing != null)
                    {
                        if (circle > 0) existing.Circle = circle;
                        existing.Desired = desired;
                        existing.IsTrackedInCounter = tracked;
                        existing.IsTrackedGlobally = global;
                    }
                    else
                    {
                        Spells.Add(new SpellEntry
                        {
                            Name = name,
                            Circle = circle,
                            Desired = desired,
                            IsTrackedInCounter = tracked,
                            IsTrackedGlobally = global
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Best effort
            }
        }
    }
}
