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
        private string[] _uniqueConstants = null;  // только ненулевые, для IndexOf-перебора
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

            // Определяем какие константы присутствуют в тексте
            // (одна проверка на уникальную константу вместо N проверок на каждый сабс)
            HashSet<string> presentConstants = null;
            if (_uniqueConstants != null && _uniqueConstants.Length > 0)
            {
                presentConstants = new HashSet<string>();
                foreach (var c in _uniqueConstants)
                {
                    if (text.IndexOf(c, System.StringComparison.Ordinal) >= 0)
                        presentConstants.Add(c);
                }
            }

            // Запускаем сабсы в правильном порядке, пропуская те, чья константа не найдена
            foreach (var entry in _orderedSubs)
            {
                if (entry.Constant == null)
                {
                    // Нет константы — запускаем всегда (wildcard-паттерн или regex)
                    entry.Sub.HandleMessage(textMessage, rootModel);
                }
                else if (presentConstants != null && presentConstants.Contains(entry.Constant))
                {
                    // Константа найдена в тексте — запускаем
                    entry.Sub.HandleMessage(textMessage, rootModel);
                }
                // Иначе — пропускаем, экономим вызов метода
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
            _uniqueConstants = uniqueSet.Count > 0 ? uniqueSet.ToArray() : null;
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
                int wildIdx = pattern.IndexOf('%');
                string literal = wildIdx < 0 ? pattern : pattern.Substring(0, wildIdx);
                return literal.Length >= 2 ? literal : null;
            }
        }
    }
}
