namespace Adan.Client.ConveyorUnits
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Common.Conveyor;
    using Common.Commands;
    using Common.ConveyorUnits;
    using Common.Model;
    using CSLib.Net.Diagnostics;
    using Model.Actions;
    using Adan.Client.Common.Messages;
    /// <summary>
    /// A <see cref="ConveyorUnit"/> that processes aliases.
    /// </summary>
    public class AliasUnit : ConveyorUnit
    {
        // ВАЖНО: контекст параметров (%0-%9) создаётся ЗАНОВО на каждое разворачивание
        // алиаса (см. HandleAlias). Раньше был один переиспользуемый (а до того — вообще
        // синглтон на все окна): при вложенном разворачивании (алиас рассылает другой
        // алиас через #sendall) внутренний затирал параметры внешнего, и в остальные
        // окна уезжали пустые строки вместо команды.

        // Защита от циклических алиасов — ПО-ОКОННО. Раньше флаг IsHandling жил на самом
        // объекте действия, а действия глобал-профиля общие для всех окон: вызов алиаса
        // "во всех окнах" срабатывал только в первом, остальные ложно ловили "цикл".
        // AliasUnit у каждого окна свой, поэтому счётчик глубины здесь — честная детекция
        // рекурсии в пределах одного окна. Конечная рекурсия (алиас вызывает сам себя с
        // уменьшающимся счётчиком) разрешена; бесконечная режется на глубине MaxAliasDepth.
        private const int MaxAliasDepth = 20;
        private readonly Dictionary<ActionBase, int> _executingActions = new Dictionary<ActionBase, int>();

        private readonly Regex _whiteSpaceRegex = new Regex(@" {2,}", RegexOptions.Compiled);
        // For optimization.
        private readonly char[] _paramsSeparatorArray = new[] { ' ' };

        /// <summary>
        /// Initializes a new instance of the <see cref="AliasUnit"/> class.
        /// </summary>
        public AliasUnit(MessageConveyor conveyor)
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
                return Enumerable.Empty<int>();
            }
        }

        /// <summary>
        /// Gets a set of command types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledCommandTypes
        {
            get
            {
                return new[] { BuiltInCommandTypes.TextCommand };
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

            var commandText = _whiteSpaceRegex.Replace(textCommand.CommandText.Trim(), " ");
            foreach (var group in Conveyor.RootModel.Groups.Where(g => g.IsEnabled))
            {
                foreach (var alias in group.Aliases)
                {
                    if (commandText.StartsWith(alias.Command, StringComparison.OrdinalIgnoreCase)
                        && (commandText.Count() == alias.Command.Count() || commandText[alias.Command.Count()] == ' '))
                    {
                        HandleAlias(commandText, alias);
                        textCommand.Handled = true;
                        return;
                    }
                }
            }
        }

        #endregion

        #region Private Methods

        private void HandleAlias(string commandText, CommandAlias alias)
        {
            // Свежий контекст на каждое разворачивание — вложенные алиасы не портят %0-%9 родителя
            var context = new ActionExecutionContext();

            int ind = commandText.IndexOf(' ');
            if (ind != -1)
                context.Parameters[0] = commandText.Substring(ind + 1);
            else
                context.Parameters[0] = String.Empty;

            var parts = context.Parameters[0].Split(_paramsSeparatorArray, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < 10; ++i)
            {
                if (i - 1 < parts.Length)
                    context.Parameters[i] = parts[i - 1];
                else
                    context.Parameters[i] = string.Empty;
            }

            //If we have only 1 parameter then %1 = %0 like in jmc
            var allCommandText = String.Join(";", alias.Actions.OfType<SendTextAction>().Select(a => a.CommandText).ToArray());
            if (allCommandText.Contains("%1") && !allCommandText.Contains("%0") && !allCommandText.Contains("%2"))
            {
                context.Parameters[1] = context.Parameters[0];
            }

            var aliasContainsParams = false;
            int lastSendTextAction = -1;
            for (var i = 0; i < alias.Actions.Count; ++i)
            {
                var act = alias.Actions[i] as SendTextAction;
                if (act != null)
                {
                    lastSendTextAction = i;
                    if (act.CommandText.Contains("%0") || act.CommandText.Contains("%1"))
                        aliasContainsParams = true;
                }
            }

            for (var i = 0; i < alias.Actions.Count; i++)
            {
                var action = alias.Actions[i];
                int currentDepth;
                _executingActions.TryGetValue(action, out currentDepth);
                if (currentDepth >= MaxAliasDepth)
                {
                    Conveyor.RootModel.PushMessageToConveyor(new ErrorMessage(string.Format("#Обнаружен циклический алиас {{{0}}} (глубина > {1})", action.ToString(), MaxAliasDepth)));
                    Conveyor.RootModel.PushMessageToConveyor(new ErrorMessage("#Работа прерывается"));
                    return;
                }

                try
                {
                    _executingActions[action] = currentDepth + 1;

                    if (i == lastSendTextAction && !aliasContainsParams)
                    {
                        var sendTextAction = action as SendTextAction;
                        if (sendTextAction == null)
                        {
                            continue;
                        }

                        if (sendTextAction.Parameters.Any())
                        {
                            sendTextAction.Execute(Conveyor.RootModel, context);
                        }
                        else
                        {
                            new SendTextAction
                            {
                                CommandText = sendTextAction.CommandText + " " + context.Parameters[0]
                            }.Execute(Conveyor.RootModel, context);
                        }
                    }
                    else
                    {
                        action.Execute(Conveyor.RootModel, context);
                    }
                }
                finally
                {
                    if (--_executingActions[action] == 0)
                        _executingActions.Remove(action);
                }
            }

            Conveyor.RootModel.PushCommandToConveyor(FlushOutputQueueCommand.Instance);
        }

        #endregion
    }
}
