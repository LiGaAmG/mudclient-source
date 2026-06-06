namespace Adan.Client.Common.Model
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Serialization;
    using System.Text;
    using System.Text.RegularExpressions;
    using Commands;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Messages;

    using Utils.PatternMatching;

    /// <summary>
    /// Trigger that handles text messages from server.
    /// </summary>
    [Serializable]
    public class TextTrigger : TriggerBase
    {
        [NonSerialized]
        private readonly IList<string> _matchingResults = new List<string>(Enumerable.Repeat(string.Empty, 10));

        [NonSerialized]
        private PatternToken _rootPatternToken;

        [NonSerialized]
        private ActionExecutionContext _context;

        [NonSerialized]
        private string _matchingPattern;

        [NonSerialized]
        private Regex _compiledRegex = null;

        // Кэш первой константной строки для быстрого pre-check (не-regex триггеры)
        [NonSerialized]
        private string _firstConstant = null;
        [NonSerialized]
        private bool _firstConstantResolved = false;

        // Кэш буквального префикса regex-паттерна для быстрого pre-check (regex триггеры).
        [NonSerialized]
        private string _regexLiteralPrefix = null;
        [NonSerialized]
        private bool _regexLiteralPrefixResolved = false;

        // Кэш обязательного литерала внутри паттерна (первый ≥4-символьный
        // литерал вне контекста чередования). Используется когда префикс пустой
        // (паттерн начинается с метасимвола, напр. ^(.+? )(попыталось...).
        [NonSerialized]
        private string _regexRequiredLiteral = null;
        [NonSerialized]
        private bool _regexRequiredLiteralResolved = false;

        // Кэш скомпилированного Regex для триггеров с переменными ($groupmate1 и т.п.).
        // Перекомпилируется только когда реально меняется resolved-паттерн.
        [NonSerialized]
        private Regex _cachedVarRegex = null;
        [NonSerialized]
        private string _cachedVarPattern = null;

        private readonly Regex _wildRegex = new Regex(@"%[0-9]", RegexOptions.Compiled);

        // Ищет первую обязательную группу чередования из чистых слов (кириллица/пробелы).
        // Например: (ударил|ударила|пнул|пнула|...) не перед '?'
        // Используется для any-of pre-check в TriggerUnit.
        [NonSerialized]
        private static readonly Regex _altGroupExtractor = new Regex(
            @"\(([а-яёА-ЯЁ][а-яёА-ЯЁ\s|]{4,})\)(?!\?|\*|\{0)",
            RegexOptions.Compiled);

        [NonSerialized]
        private string[] _anyOfLiterals = null;
        [NonSerialized]
        private bool _anyOfLiteralsResolved = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextTrigger"/> class.
        /// </summary>
        public TextTrigger()
        {
            MatchingPattern = string.Empty;
            _context = new ActionExecutionContext();
            IsRegExp = false;
        }

        /// <summary>
        /// Is Regular Expression
        /// </summary>
        [NotNull]
        [XmlAttribute]
        public bool IsRegExp
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the matching pattern.
        /// </summary>
        /// <value>
        /// The matching pattern.
        /// </value>
        [NotNull]
        [XmlAttribute]
        public string MatchingPattern
        {
            get
            {
                return _matchingPattern;
            }

            set
            {
                Assert.ArgumentNotNull(value, "value");

                if (value.Length > 2 && value[0] == '/' && value[value.Length - 1] == '/')
                {
                    _matchingPattern = value.Substring(1, value.Length - 2);
                    IsRegExp = true;
                }
                else
                {
                    _matchingPattern = value;
                }

                if (IsRegExp && (value.IndexOf("$") == -1 || value.IndexOf("$") == value.Length - 1))
                    _compiledRegex = new Regex(_matchingPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                else
                    _compiledRegex = null;

                _rootPatternToken = null;
                _firstConstant = null;
                _firstConstantResolved = false;
                _regexLiteralPrefix = null;
                _regexLiteralPrefixResolved = false;
                _regexRequiredLiteral = null;
                _regexRequiredLiteralResolved = false;
                _cachedVarRegex = null;
                _cachedVarPattern = null;
                _anyOfLiterals = null;
                _anyOfLiteralsResolved = false;
            }
        }

        [NotNull]
        private ActionExecutionContext Context
        {
            get
            {
                return _context ?? (_context = new ActionExecutionContext());
            }
        }

        public bool MatchMessage(TextMessage textMessage, RootModel rootModel)
        {
            ClearMatchingResults();

            if (IsRegExp)
            {
                // Pre-check для regex-триггеров: извлекаем буквальный префикс до первого
                // метасимвола и делаем быстрый IndexOf. Если префикс не найден — regex
                // точно не сработает, пропускаем. Применяется только к compiled regex.
                if (_compiledRegex != null)
                {
                    if (!_regexLiteralPrefixResolved)
                    {
                        _regexLiteralPrefix = ExtractRegexLiteralPrefix(MatchingPattern);
                        _regexLiteralPrefixResolved = true;
                    }
                    if (_regexLiteralPrefix.Length > 0)
                    {
                        if (textMessage.InnerText.IndexOf(_regexLiteralPrefix, StringComparison.OrdinalIgnoreCase) < 0)
                            return false;
                    }
                    else
                    {
                        // Префикс пустой (паттерн начинается с метасимвола).
                        // Ищем обязательный литерал внутри паттерна.
                        if (!_regexRequiredLiteralResolved)
                        {
                            _regexRequiredLiteral = ExtractRegexRequiredLiteral(MatchingPattern);
                            _regexRequiredLiteralResolved = true;
                        }
                        if (_regexRequiredLiteral != null && _regexRequiredLiteral.Length > 0 &&
                            textMessage.InnerText.IndexOf(_regexRequiredLiteral, StringComparison.OrdinalIgnoreCase) < 0)
                            return false;
                    }
                }

                Match match;
                if (_compiledRegex != null)
                    match = _compiledRegex.Match(textMessage.InnerText);
                else
                {
                    var varReplace = rootModel.ReplaceVariables(MatchingPattern);
                    if (!varReplace.IsAllVariables)
                        return false;

                    // Кешируем скомпилированный Regex: перекомпилируем только если
                    // resolved-паттерн изменился (т.е. значение переменной сменилось).
                    if (_cachedVarRegex == null || _cachedVarPattern != varReplace.Value)
                    {
                        _cachedVarPattern = varReplace.Value;
                        _cachedVarRegex = new Regex(_cachedVarPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                    }
                    match = _cachedVarRegex.Match(textMessage.InnerText);
                }

                if (!match.Success)
                    return false;

                for (int i = 0; i < 10; i++)
                {
                    if (i + 1 < match.Groups.Count)
                        _matchingResults[i] = match.Groups[i + 1].ToString();
                }

                return true;
            }

            // Pre-check для не-regex триггеров
            if (!_firstConstantResolved)
            {
                var rootToken = GetRootPatternToken(rootModel) as Utils.PatternMatching.ConstantStringToken;
                _firstConstant = rootToken?.SearchValue;
                _firstConstantResolved = true;
            }
            if (_firstConstant != null && textMessage.InnerText.IndexOf(_firstConstant, StringComparison.Ordinal) < 0)
                return false;

            var res = GetRootPatternToken(rootModel).Match(textMessage.InnerText, 0, _matchingResults);
            return res.IsSuccess;
        }

        /// <summary>
        /// Handles the message.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="rootModel">The RootModel.</param>
        public override void HandleMessage(Message message, RootModel rootModel)
        {
            Assert.ArgumentNotNull(message, "message");
            Assert.ArgumentNotNull(rootModel, "rootModel");

            var textMessage = message as TextMessage;
            if (textMessage == null || string.IsNullOrEmpty(MatchingPattern) || string.IsNullOrEmpty(textMessage.InnerText))
            {
                return;
            }

            if (!MatchMessage(textMessage, rootModel))
                return;

            for (int i = 0; i < 10; i++)
            {
                if (i < _matchingResults.Count)
                    Context.Parameters[i] = _matchingResults[i];
            }

            Context.CurrentMessage = message;

            foreach (var action in Actions)
            {
                action.Execute(rootModel, Context);
            }

            rootModel.PushCommandToConveyor(FlushOutputQueueCommand.Instance);

            if (StopProcessingTriggersAfterThis)
            {
                textMessage.SkipTriggers = true;
            }

            if (DoNotDisplayOriginalMessage)
            {
                textMessage.Handled = true;
            }
        }

        public override string GetPatternString()
        {
            return MatchingPattern;
        }

        /// <summary>
        /// Возвращает обязательный литерал для быстрого IndexOf-фильтра в TriggerUnit.
        /// Переиспользует уже кэшированные поля. null = всегда запускать.
        /// </summary>
        public override string GetRequiredLiteral()
        {
            if (string.IsNullOrEmpty(MatchingPattern))
                return null;

            if (!IsRegExp)
            {
                // Не-regex: текст до первого %N
                int wildIdx = MatchingPattern.IndexOf('%');
                string lit = wildIdx < 0 ? MatchingPattern : MatchingPattern.Substring(0, wildIdx);
                return lit.Length >= 2 ? lit : null;
            }

            // Regex без переменных — используем скомпилированный regex
            if (_compiledRegex != null)
            {
                // Сначала пробуем literal prefix
                if (!_regexLiteralPrefixResolved)
                {
                    _regexLiteralPrefix = ExtractRegexLiteralPrefix(MatchingPattern);
                    _regexLiteralPrefixResolved = true;
                }
                if (_regexLiteralPrefix != null && _regexLiteralPrefix.Length >= 2)
                    return _regexLiteralPrefix;

                // Потом required literal внутри паттерна
                if (!_regexRequiredLiteralResolved)
                {
                    _regexRequiredLiteral = ExtractRegexRequiredLiteral(MatchingPattern);
                    _regexRequiredLiteralResolved = true;
                }
                if (_regexRequiredLiteral != null && _regexRequiredLiteral.Length >= 2)
                    return _regexRequiredLiteral;
            }

            // Regex с переменными ($groupmate1 и т.п.) — литерал ненадёжен, всегда запускать
            return null;
        }

        /// <summary>
        /// Для regex-триггеров без фиксированного префикса ищет обязательную группу чередования
        /// из чистых кириллических слов. Например: (ударил|ударила|пнул|пнула|...).
        /// Если хоть одно из этих слов есть в тексте — regex может совпасть, иначе точно нет.
        /// </summary>
        public override string[] GetAnyOfLiterals()
        {
            if (!IsRegExp || string.IsNullOrEmpty(MatchingPattern))
                return null;

            if (_anyOfLiteralsResolved)
                return _anyOfLiterals;

            _anyOfLiteralsResolved = true;

            // Только для regex без фиксированного литерала
            var m = _altGroupExtractor.Match(MatchingPattern);
            while (m.Success)
            {
                var parts = m.Groups[1].Value.Split('|');
                var words = new System.Collections.Generic.List<string>(parts.Length);
                bool allGood = true;
                foreach (var p in parts)
                {
                    var w = p.Trim();
                    if (w.Length >= 3)
                        words.Add(w);
                    else
                    {
                        // Слишком короткое — ненадёжно для фильтрации
                        allGood = false;
                        break;
                    }
                }
                if (allGood && words.Count >= 2)
                {
                    _anyOfLiterals = words.ToArray();
                    return _anyOfLiterals;
                }
                m = m.NextMatch();
            }

            return null;
        }

        public override string UndoInfo()
        {
            var sb = new StringBuilder();
            sb.Append("#Триггер: ").Append("#action {").Append(GetPatternString()).Append("} ");
            switch (Operation)
            {
                case UndoOperation.Add:
                    sb.Append("восстановлен");
                    break;
                case UndoOperation.Remove:
                    sb.Append("удален");
                    break;
            }

            return sb.ToString();
        }

        public override void Undo(RootModel rootModel)
        {
            if (Group != null && Operation != UndoOperation.None)
            {
                switch (Operation)
                {
                    case UndoOperation.Add:
                        Group.Triggers.Add(this);
                        break;
                    case UndoOperation.Remove:
                        Group.Triggers.Remove(this);
                        break;
                }

                rootModel.RecalculatedEnabledTriggersPriorities();
            }
        }

        private void ClearMatchingResults()
        {
            for (int i = 0; i < _matchingResults.Count; i++)
            {
                _matchingResults[i] = string.Empty;
            }
        }

        /// <summary>
        /// Ищет первый "обязательный" литерал длиной ≥4 символов внутри regex-паттерна.
        /// Литерал считается обязательным если он не находится в контексте чередования (|)
        /// ни на одном уровне вложенности.
        /// Примеры:
        ///   "^(.+? )(попыталось ударить)"  → "попыталось"
        ///   "^[А-Я]{3,40} издал странный"  → "издал"  (после закрытия класса символов)
        ///   "^(клевер|розмарин) растёт"    → "" (alternation → ничего не возвращаем)
        /// </summary>
        private static string ExtractRegexRequiredLiteral(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return string.Empty;

            const int MaxDepth = 20;
            var alternationAtDepth = new bool[MaxDepth];
            int depth = 0;
            var current = new System.Text.StringBuilder();
            string best = string.Empty;

            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];

                if (c == '\\')
                {
                    // Экранированный символ — не литерал, сбрасываем текущий буфер
                    TryUpdateBest(current, alternationAtDepth, depth, ref best);
                    current.Clear();
                    i++; // пропускаем следующий символ
                    continue;
                }

                switch (c)
                {
                    case '(':
                        TryUpdateBest(current, alternationAtDepth, depth, ref best);
                        current.Clear();
                        if (depth < MaxDepth - 1) depth++;
                        alternationAtDepth[depth] = false;
                        break;
                    case ')':
                        TryUpdateBest(current, alternationAtDepth, depth, ref best);
                        current.Clear();
                        if (depth > 0) depth--;
                        break;
                    case '|':
                        alternationAtDepth[depth] = true;
                        TryUpdateBest(current, alternationAtDepth, depth, ref best);
                        current.Clear();
                        break;
                    case '.': case '*': case '+': case '?':
                    case '[': case ']': case '{': case '}':
                    case '^': case '$':
                        TryUpdateBest(current, alternationAtDepth, depth, ref best);
                        current.Clear();
                        break;
                    default:
                        // Проверяем: следующий символ — квантификатор? Тогда этот символ необязателен
                        if (i + 1 < pattern.Length)
                        {
                            char next = pattern[i + 1];
                            if (next == '*' || next == '?' || next == '{')
                            {
                                TryUpdateBest(current, alternationAtDepth, depth, ref best);
                                current.Clear();
                                break;
                            }
                        }
                        current.Append(c);
                        // Если нашли достаточно длинный литерал — можно вернуть сразу
                        if (current.Length >= 6 && !HasAlternation(alternationAtDepth, depth))
                            return current.ToString();
                        break;
                }
            }

            TryUpdateBest(current, alternationAtDepth, depth, ref best);
            return best;
        }

        private static void TryUpdateBest(System.Text.StringBuilder sb, bool[] alternation, int depth, ref string best)
        {
            if (sb.Length >= 4 && !HasAlternation(alternation, depth) && sb.Length > best.Length)
                best = sb.ToString();
        }

        private static bool HasAlternation(bool[] alternation, int depth)
        {
            for (int i = 0; i <= depth; i++)
                if (alternation[i]) return true;
            return false;
        }

        /// <summary>
        /// Извлекает буквальную строку из начала regex-паттерна — всё до первого
        /// неэкранированного метасимвола (. * + ? [ ] ( ) { } ^ $ | \).
        /// Примеры:
        ///   "нанесли \d+ урона"  → "нанесли "
        ///   "атакует (вас|тебя)" → "атакует "
        ///   "[Вв]ы атакуете"     → ""   (сразу метасимвол → pre-check не применяется)
        ///   "вы убили монстра"   → "вы убили монстра"  (нет метасимволов)
        /// </summary>
        private static string ExtractRegexLiteralPrefix(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '\\')
                {
                    // Экранированный символ — стоп (следующий символ может быть \d, \w и т.п.)
                    break;
                }
                if (c == '.' || c == '*' || c == '+' || c == '?' ||
                    c == '[' || c == ']' || c == '(' || c == ')' ||
                    c == '{' || c == '}' || c == '^' || c == '$' || c == '|')
                {
                    break;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        [NotNull]
        private PatternToken GetRootPatternToken([NotNull] RootModel rootModel)
        {
            Assert.ArgumentNotNull(rootModel, "rootModel");

            if (_rootPatternToken == null)
            {
                _rootPatternToken = WildcardParser.ParseWildcardString(MatchingPattern, rootModel);
            }

            return _rootPatternToken;
        }

        /// <summary>
        /// Represent trigger as string (similar to JMC)
        /// </summary>
        /// <returns>String representation of the trigger</returns>
        public override string ToString()
        {
            var patternString = this.GetPatternString();
            if (this.IsRegExp)
                patternString = "/" + patternString + "/";
            if (this.Group == null)
                return string.Format("#action {{{0}}} {{{1}}} {{{2}}}", patternString, this.Actions[0].ToString(), this.Priority);
            else
                return string.Format("#action {{{0}}} {{{1}}} {{{2}}} {{{3}}}", patternString, this.Actions[0].ToString(), this.Priority, this.Group.Name);
        }
    }
}
