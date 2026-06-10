// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SubstitutionUnit.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the SubstitutionUnit type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using Adan.Client.Common.Conveyor;

namespace Adan.Client.ConveyorUnits
{
    using System.Collections.Generic;
    using System.Linq;
    using Common.ConveyorUnits;
    using Common.Messages;
    using Common.Model;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// A <see cref="ConveyorUnit"/> implementation that handles substitutions.
    /// </summary>
    public class SubstitutionUnit : ConveyorUnit
    {
        // ---------------------------------------------------------------------------
        // Индекс для быстрого поиска сабсов.
        //
        // Идея: вместо 1200+ вызовов HandleMessage на каждой строке, сначала
        // определяем какие «первые константы» присутствуют в тексте (N × IndexOf),
        // затем запускаем только совпавшие сабсы. Порядок исполнения сохраняется.
        //
        // Структура:
        //   _orderedSubs  — плоский список (constant, sub) в правильном порядке.
        //                   constant == null → сабс без константы, запускается всегда.
        //   _uniqueConstants — уникальные константы для быстрого перебора.
        //
        // Инвалидация: сравниваем суммарное кол-во сабсов включённых групп.
        // Если изменилось — перестраиваем индекс.
        // ---------------------------------------------------------------------------

        private struct SubEntry
        {
            public string Constant; // null = нет константы (wildcard-начало)
            public Substitution Sub;
        }

        private List<SubEntry> _orderedSubs = null;
        // _twoCharGroups: вместо O(N×L) перебора N констант на каждом сообщении длины L —
        // делаем один проход по тексту (O(L)), на каждой паре символов находим совпавшие
        // константы через словарь. Итог: O(L + K×avgLen) где K — кол-во совпавших констант.
        private Dictionary<(char, char), string[]> _twoCharGroups = null;
        private int _lastIndexedSubCount = -1;      // для обнаружения изменений

        /// <summary>
        /// Initializes a new instance of the <see cref="SubstitutionUnit"/> class.
        /// </summary>
        public SubstitutionUnit(MessageConveyor conveyor)
            : base(conveyor)
        {
        }

        #region Overrides of ConveyorUnit

        /// <summary>
        /// Gets a set of message types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledMessageTypes
        {
            get
            {
                return new[] { BuiltInMessageTypes.TextMessage };
            }
        }

        /// <summary>
        /// Gets a set of command types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledCommandTypes
        {
            get
            {
                return Enumerable.Empty<int>();
            }
        }

        public override void HandleMessage(Message message)
        {
            Assert.ArgumentNotNull(message, "message");

            var textMessage = message as TextMessage;
            if (textMessage == null || textMessage.SkipSubstitution)
                return;

            var rootModel = Conveyor.RootModel;

            // Проверяем актуальность индекса
            int currentSubCount = CountEnabledSubs(rootModel);
            if (_orderedSubs == null || currentSubCount != _lastIndexedSubCount)
            {
                RebuildIndex(rootModel);
                _lastIndexedSubCount = currentSubCount;
            }

            string text = textMessage.InnerText;
            if (string.IsNullOrEmpty(text))
                return;

            // Определяем какие константы присутствуют в тексте.
            // Используем 2-символьное группирование: проходим по тексту один раз,
            // для каждой пары (text[i], text[i+1]) смотрим только константы с таким префиксом.
            // O(textLength × avgGroupSize) вместо O(numConstants × textLength).
            HashSet<string> presentConstants = null;
            if (_twoCharGroups != null && text.Length >= 2)
            {
                presentConstants = new HashSet<string>(System.StringComparer.Ordinal);
                for (int i = 0; i < text.Length - 1; i++)
                {
                    if (_twoCharGroups.TryGetValue((text[i], text[i + 1]), out var candidates))
                    {
                        foreach (var c in candidates)
                        {
                            if (!presentConstants.Contains(c)
                                && text.IndexOf(c, System.StringComparison.Ordinal) >= 0)
                            {
                                presentConstants.Add(c);
                            }
                        }
                    }
                }
            }

            var _subSw = System.Diagnostics.Stopwatch.StartNew();
            int _subRunCount = 0;
            // Запускаем сабсы в правильном порядке, пропуская те, чья константа не найдена
            foreach (var entry in _orderedSubs)
            {
                bool run = entry.Constant == null
                    || (presentConstants != null && presentConstants.Contains(entry.Constant));
                if (!run) continue;
                _subRunCount++;
                entry.Sub.HandleMessage(textMessage, rootModel);
            }
            _subSw.Stop();
            if (_subSw.ElapsedMilliseconds >= 10)
            {
                var logText = text.Length > 80 ? text.Substring(0, 80) + "..." : text;
                Common.Conveyor.PerfLog.Write("SubstitutionUnit [ran=" + _subRunCount + "/" + (_orderedSubs != null ? _orderedSubs.Count : 0) + "]", logText, _subSw.ElapsedMilliseconds);
            }
        }

        #endregion

        // ---------------------------------------------------------------------------
        // Вспомогательные методы
        // ---------------------------------------------------------------------------

        private static int CountEnabledSubs(Common.Model.RootModel rootModel)
        {
            int count = 0;
            foreach (var g in rootModel.Groups)
                if (g.IsEnabled)
                    count += g.Substitutions.Count;
            return count;
        }

        private void RebuildIndex(Common.Model.RootModel rootModel)
        {
            var ordered = new List<SubEntry>(512);
            var uniqueSet = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var group in rootModel.Groups)
            {
                if (!group.IsEnabled)
                    continue;

                foreach (var sub in group.Substitutions)
                {
                    string constant = ExtractFirstConstant(sub);
                    ordered.Add(new SubEntry { Constant = constant, Sub = sub });
                    if (constant != null)
                        uniqueSet.Add(constant);
                }
            }

            _orderedSubs = ordered;

            // Строим 2-символьный индекс для быстрого поиска совпавших констант.
            if (uniqueSet.Count > 0)
            {
                var tempGroups = new Dictionary<(char, char), List<string>>();
                foreach (var c in uniqueSet)
                {
                    if (c.Length >= 2)
                    {
                        var key = (c[0], c[1]);
                        if (!tempGroups.TryGetValue(key, out var list))
                            tempGroups[key] = list = new List<string>();
                        list.Add(c);
                    }
                }
                var groups2 = new Dictionary<(char, char), string[]>(tempGroups.Count);
                foreach (var kv in tempGroups)
                    groups2[kv.Key] = kv.Value.ToArray();
                _twoCharGroups = groups2;
            }
            else
            {
                _twoCharGroups = null;
            }
        }

        /// <summary>
        /// Извлекает первую константную строку из паттерна сабса.
        /// Для не-regex сабсов — всё до первого %N или конец строки.
        /// Для regex сабсов — буквальный префикс до первого метасимвола.
        /// Возвращает null если нет надёжной константы (пустой паттерн, начинается с wildcard/метасимвола).
        /// </summary>
        private static string ExtractFirstConstant(Substitution sub)
        {
            if (string.IsNullOrEmpty(sub.Pattern))
                return null;

            string pattern = sub.Pattern;

            if (sub.IsRegExp)
            {
                // Для regex: берём буквальный префикс до первого метасимвола
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < pattern.Length; i++)
                {
                    char c = pattern[i];
                    if (c == '\\' || c == '.' || c == '*' || c == '+' || c == '?' ||
                        c == '[' || c == ']' || c == '(' || c == ')' ||
                        c == '{' || c == '}' || c == '^' || c == '$' || c == '|')
                        break;
                    sb.Append(c);
                }
                string prefix = sb.ToString();
                return prefix.Length >= 2 ? prefix : null;
            }
            else
            {
                // Для wildcard-паттернов: берём текст до первого %N
                // Пропускаем ведущий ^ (якорь начала строки — не буквальный символ)
                int startIdx = (pattern.Length > 0 && pattern[0] == '^') ? 1 : 0;
                int wildIdx = pattern.IndexOf('%', startIdx);
                string literal = wildIdx < 0 ? pattern.Substring(startIdx) : pattern.Substring(startIdx, wildIdx - startIdx);
                if (literal.Length >= 2)
                    return literal;

                // Паттерн начинается с %N (нет константы перед wildcard).
                // Берём первый литерал ПОСЛЕ wildcard — он тоже обязателен.
                // Например: "%1 оглушен." → "оглушен", "%1 взял%2 %3" → "взял"
                if (wildIdx >= 0 && wildIdx + 2 < pattern.Length)
                {
                    int afterWild = wildIdx + 2; // пропускаем % и цифру
                    // Пропускаем пробелы/знаки после wildcard
                    while (afterWild < pattern.Length &&
                           (pattern[afterWild] == ' ' || pattern[afterWild] == ',' || pattern[afterWild] == '.'))
                        afterWild++;
                    // Находим следующий сегмент до следующего %N или конца
                    int nextWild = pattern.IndexOf('%', afterWild);
                    string afterLiteral = nextWild < 0
                        ? pattern.Substring(afterWild)
                        : pattern.Substring(afterWild, nextWild - afterWild);
                    // Обрезаем хвостовые знаки препинания
                    afterLiteral = afterLiteral.TrimEnd('.', '!', ',', ':', ';', ' ');
                    if (afterLiteral.Length >= 3)
                        return afterLiteral;
                }

                return null;
            }
        }
    }
}
