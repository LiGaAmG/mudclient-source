// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TriggerUnit.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the TriggerUnit type.
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
    /// A <see cref="ConveyorUnit"/> that processes triggers.
    /// </summary>
    public class TriggerUnit : ConveyorUnit
    {
        // ---------------------------------------------------------------------------
        // Индекс для быстрого пропуска триггеров.
        //
        // Та же идея что и в SubstitutionUnit: вместо 389 вызовов HandleMessage
        // на каждую строку сначала определяем какие литералы присутствуют в тексте
        // (N × IndexOf), затем запускаем только совпавшие триггеры в правильном порядке.
        //
        // Особенности по сравнению с SubstitutionUnit:
        //   - Триггеры упорядочены по Priority (не по группам)
        //   - Нужно соблюдать StopProcessingTriggersAfterThis / message.SkipTriggers
        //   - Используем EnabledTriggersOrderedByPriority как источник порядка
        // ---------------------------------------------------------------------------

        private struct TriggerEntry
        {
            public string Literal;       // одна константа — присутствие гарантирует совпадение
            public string[] AnyOfLiterals; // любая из альтернатив должна присутствовать
            public TriggerBase Trigger;
        }

        private List<TriggerEntry> _orderedTriggers = null;
        private Dictionary<(char, char), string[]> _twoCharLiteralGroups = null;
        private int _lastIndexedTriggerCount = -1;
        private int _lastEnabledGroupCount = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="TriggerUnit"/> class.
        /// </summary>
        public TriggerUnit(MessageConveyor conveyor)
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

            var textMsg = message as TextMessage;

            // Проверяем актуальность индекса по кол-ву включённых групп и суммарному кол-ву триггеров
            var rootModel = Conveyor.RootModel;
            int enabledGroupCount = 0;
            int totalTriggerCount = 0;
            foreach (var g in rootModel.Groups)
            {
                if (g.IsEnabled)
                {
                    enabledGroupCount++;
                    totalTriggerCount += g.Triggers.Count;
                }
            }

            if (_orderedTriggers == null
                || totalTriggerCount != _lastIndexedTriggerCount
                || enabledGroupCount != _lastEnabledGroupCount)
            {
                RebuildIndex(rootModel.EnabledTriggersOrderedByPriority);
                _lastIndexedTriggerCount = totalTriggerCount;
                _lastEnabledGroupCount = enabledGroupCount;
            }

            string text = textMsg?.InnerText ?? string.Empty;

            // Определяем какие литералы присутствуют в тексте через 2-символьный индекс.
            HashSet<string> presentLiterals = null;
            if (_twoCharLiteralGroups != null && text.Length >= 2)
            {
                presentLiterals = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < text.Length - 1; i++)
                {
                    // Проверяем как lowercase-пару (OrdinalIgnoreCase)
                    char c0 = char.ToLowerInvariant(text[i]);
                    char c1 = char.ToLowerInvariant(text[i + 1]);
                    if (_twoCharLiteralGroups.TryGetValue((c0, c1), out var candidates))
                    {
                        foreach (var lit in candidates)
                        {
                            if (!presentLiterals.Contains(lit)
                                && text.IndexOf(lit, System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                presentLiterals.Add(lit);
                            }
                        }
                    }
                }
            }

            // Запускаем триггеры в правильном порядке, пропуская те, чей литерал не найден
            foreach (var entry in _orderedTriggers)
            {
                if (message.SkipTriggers)
                    break;

                bool shouldRun;
                if (entry.Literal != null)
                {
                    // Одиночный литерал — проверяем через предвычисленный HashSet
                    shouldRun = presentLiterals != null && presentLiterals.Contains(entry.Literal);
                }
                else if (entry.AnyOfLiterals != null)
                {
                    // Any-of: хотя бы одна альтернатива должна быть в тексте
                    shouldRun = false;
                    foreach (var alt in entry.AnyOfLiterals)
                    {
                        if (text.IndexOf(alt, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            shouldRun = true;
                            break;
                        }
                    }
                }
                else
                {
                    // Нет никакой информации — запускаем всегда
                    shouldRun = true;
                }

                if (!shouldRun)
                    continue;

#if DEBUG
                var sw = System.Diagnostics.Stopwatch.StartNew();
                entry.Trigger.HandleMessage(message, Conveyor.RootModel);
                sw.Stop();
                if (sw.ElapsedMilliseconds >= 3)
                {
                    var pattern = entry.Trigger.GetPatternString();
                    if (pattern != null && pattern.Length > 60) pattern = pattern.Substring(0, 60) + "...";
                    var logText = text.Length > 60 ? text.Substring(0, 60) + "..." : text;
                    Common.Conveyor.PerfLog.Write(
                        string.Format("  Trigger[{0}]", pattern),
                        logText,
                        sw.ElapsedMilliseconds);
                }
#else
                entry.Trigger.HandleMessage(message, Conveyor.RootModel);
#endif
            }
        }

        #endregion

        private void RebuildIndex(IEnumerable<TriggerBase> triggers)
        {
            var ordered = new List<TriggerEntry>(512);
            var uniqueSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var trigger in triggers)
            {
                string literal = trigger.GetRequiredLiteral();
                string[] anyOf = literal == null ? trigger.GetAnyOfLiterals() : null;
                ordered.Add(new TriggerEntry { Literal = literal, AnyOfLiterals = anyOf, Trigger = trigger });
                if (literal != null)
                    uniqueSet.Add(literal);
            }

            _orderedTriggers = ordered;

            // Строим 2-символьный индекс по lowercase-парам (т.к. OrdinalIgnoreCase).
            if (uniqueSet.Count > 0)
            {
                var tempGroups = new Dictionary<(char, char), List<string>>();
                foreach (var lit in uniqueSet)
                {
                    if (lit.Length >= 2)
                    {
                        var key = (char.ToLowerInvariant(lit[0]), char.ToLowerInvariant(lit[1]));
                        if (!tempGroups.TryGetValue(key, out var list))
                            tempGroups[key] = list = new List<string>();
                        list.Add(lit);
                    }
                }
                var groups2 = new Dictionary<(char, char), string[]>(tempGroups.Count);
                foreach (var kv in tempGroups)
                    groups2[kv.Key] = kv.Value.ToArray();
                _twoCharLiteralGroups = groups2.Count > 0 ? groups2 : null;
            }
            else
            {
                _twoCharLiteralGroups = null;
            }
        }
    }
}
