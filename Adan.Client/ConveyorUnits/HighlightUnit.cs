// --------------------------------------------------------------------------------------------------------------------
// <copyright file="HighlightUnit.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the HighlightUnit type.
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
    /// A <see cref="ConveyorUnit"/> implementation that highlights text.
    /// </summary>
    public class HighlightUnit : ConveyorUnit
    {
        // ---------------------------------------------------------------------------
        // Индекс по первой константе — та же схема что в SubstitutionUnit.
        // 75 хайлайтов → только те чья константа найдена в тексте (~5-10).
        // ---------------------------------------------------------------------------

        private struct HighlightEntry
        {
            public string Constant; // null = нет константы, запускать всегда
            public Highlight Highlight;
        }

        private List<HighlightEntry> _orderedHighlights = null;
        private Dictionary<(char, char), string[]> _twoCharGroups = null;
        private int _lastIndexedHighlightCount = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="HighlightUnit"/> class.
        /// </summary>
        public HighlightUnit(MessageConveyor conveyor)
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
            if (textMessage == null || textMessage.SkipHighlight)
                return;

            var rootModel = Conveyor.RootModel;

            // Проверяем актуальность индекса
            int currentCount = CountEnabledHighlights(rootModel);
            if (_orderedHighlights == null || currentCount != _lastIndexedHighlightCount)
            {
                RebuildIndex(rootModel);
                _lastIndexedHighlightCount = currentCount;
            }

            string text = textMessage.InnerText;
            if (string.IsNullOrEmpty(text))
                return;

            // Определяем какие константы присутствуют в тексте через 2-символьный индекс.
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

            // Запускаем хайлайты в правильном порядке
            foreach (var entry in _orderedHighlights)
            {
                if (entry.Constant == null
                    || (presentConstants != null && presentConstants.Contains(entry.Constant)))
                {
                    entry.Highlight.ProcessMessage(textMessage, rootModel);
                }
            }
        }

        #endregion

        private static int CountEnabledHighlights(Common.Model.RootModel rootModel)
        {
            int count = 0;
            foreach (var g in rootModel.Groups)
                if (g.IsEnabled)
                    count += g.Highlights.Count;
            return count;
        }

        private void RebuildIndex(Common.Model.RootModel rootModel)
        {
            var ordered = new List<HighlightEntry>(128);
            var uniqueSet = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var group in rootModel.Groups)
            {
                if (!group.IsEnabled)
                    continue;

                foreach (var highlight in group.Highlights)
                {
                    string constant = ExtractFirstConstant(highlight);
                    ordered.Add(new HighlightEntry { Constant = constant, Highlight = highlight });
                    if (constant != null)
                        uniqueSet.Add(constant);
                }
            }

            _orderedHighlights = ordered;

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
                _twoCharGroups = groups2.Count > 0 ? groups2 : null;
            }
            else
            {
                _twoCharGroups = null;
            }
        }

        private static string ExtractFirstConstant(Highlight highlight)
        {
            if (string.IsNullOrEmpty(highlight.TextToHighlight))
                return null;

            string pattern = highlight.TextToHighlight;

            if (highlight.IsRegExp)
            {
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
                // Пропускаем ведущий ^ (якорь начала строки — не буквальный символ)
                int startIdx = (pattern.Length > 0 && pattern[0] == '^') ? 1 : 0;
                int wildIdx = pattern.IndexOf('%', startIdx);
                string literal = wildIdx < 0 ? pattern.Substring(startIdx) : pattern.Substring(startIdx, wildIdx - startIdx);
                if (literal.Length >= 2)
                    return literal;

                // Паттерн начинается с %N — берём первый литерал после wildcard
                if (wildIdx >= 0 && wildIdx + 2 < pattern.Length)
                {
                    int afterWild = wildIdx + 2;
                    while (afterWild < pattern.Length &&
                           (pattern[afterWild] == ' ' || pattern[afterWild] == ',' || pattern[afterWild] == '.'))
                        afterWild++;
                    int nextWild = pattern.IndexOf('%', afterWild);
                    string afterLiteral = nextWild < 0
                        ? pattern.Substring(afterWild)
                        : pattern.Substring(afterWild, nextWild - afterWild);
                    afterLiteral = afterLiteral.TrimEnd('.', '!', ',', ':', ';', ' ');
                    if (afterLiteral.Length >= 3)
                        return afterLiteral;
                }

                return null;
            }
        }
    }
}
