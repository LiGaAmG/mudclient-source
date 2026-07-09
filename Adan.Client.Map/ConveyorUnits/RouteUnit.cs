namespace Adan.Client.Map.ConveyorUnits
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Common.Commands;
    using Common.Conveyor;
    using Common.ConveyorUnits;
    using Common.Messages;
    using Common.Themes;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Properties;

    /// <summary>
    /// <see cref="ConveyorUnit"/> implementation to handle route command.
    /// </summary>
    public class RouteUnit : ConveyorUnit
    {
        private readonly RouteManager _routeManager;

        public RouteUnit([NotNull] RouteManager routeManager, MessageConveyor conveyor)
            : base(conveyor)
        {
            Assert.ArgumentNotNull(routeManager, "routeManager");
            _routeManager = routeManager;
        }

        public override IEnumerable<int> HandledMessageTypes => Enumerable.Empty<int>();

        public override IEnumerable<int> HandledCommandTypes
            => Enumerable.Repeat(BuiltInCommandTypes.TextCommand, 1);

        public override void HandleCommand(Command command, bool isImport = false)
        {
            Assert.ArgumentNotNull(command, "command");

            var textCommand = command as TextCommand;
            if (textCommand == null) return;

            var text = textCommand.CommandText.Trim();

            // "идти <комната>" — внутризонная навигация (без # и без префикса маршрут)
            var gotoCmd = Resources.RouteCommandGoto;
            if (text.StartsWith(gotoCmd + " ", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith(Resources.RouteCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var room = text.Substring(gotoCmd.Length).Trim();
                _routeManager.NavigateToRoom(room);
                command.Handled = true;
                return;
            }

            // Strip optional leading '#' so both "маршрут X" and "#маршрут X" work.
            var stripped = text.TrimStart('#');
            var prefix = Resources.RouteCommandPrefix;

            // Must start with "маршрут " or be exactly "маршрут"
            if (!stripped.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(stripped, prefix, StringComparison.OrdinalIgnoreCase))
                return;

            var args = stripped.Length > prefix.Length
                ? stripped.Substring(prefix.Length).Trim()
                : string.Empty;

            // "#route help"
            if (string.Equals(args, Resources.RouteCommandHelp, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(args))
            {
                PrintHelp();
                command.Handled = true;
                return;
            }

            // "#route start [name]"
            if (args.StartsWith(Resources.RouteCommandStartRecording, StringComparison.OrdinalIgnoreCase))
            {
                var name = args.Length > Resources.RouteCommandStartRecording.Length
                    ? args.Substring(Resources.RouteCommandStartRecording.Length).Trim()
                    : string.Empty;

                if (!string.IsNullOrEmpty(name))
                    _routeManager.StartNewRouteRecording(name);
                else
                    _routeManager.StartNewRouteRecording();

                command.Handled = true;
                return;
            }

            // "#route stoprecording [name]"
            if (args.StartsWith(Resources.RouteCommandStopRecording, StringComparison.OrdinalIgnoreCase))
            {
                var name = args.Length > Resources.RouteCommandStopRecording.Length
                    ? args.Substring(Resources.RouteCommandStopRecording.Length).Trim()
                    : string.Empty;

                if (!string.IsNullOrEmpty(name))
                    _routeManager.StopRouteRecording(name);
                else
                    _routeManager.StopRouteRecording();

                command.Handled = true;
                return;
            }

            // "#route cancel"
            if (string.Equals(args, Resources.RouteCommandCancelRecording, StringComparison.OrdinalIgnoreCase))
            {
                _routeManager.CancelRouteRecording();
                command.Handled = true;
                return;
            }

            // "#route goto [dest]"
            if (args.StartsWith(Resources.RouteCommandGoto, StringComparison.OrdinalIgnoreCase))
            {
                var dest = args.Length > Resources.RouteCommandGoto.Length
                    ? args.Substring(Resources.RouteCommandGoto.Length).Trim()
                    : string.Empty;

                if (!string.IsNullOrEmpty(dest))
                    _routeManager.GotoDestination(dest);
                else
                    _routeManager.GotoDestination();

                command.Handled = true;
                return;
            }

            // "#route stop"
            if (string.Equals(args, Resources.RouteCommandStop, StringComparison.OrdinalIgnoreCase))
            {
                _routeManager.StopRoutingToDestination();
                command.Handled = true;
                return;
            }

            // "маршрут лог" / "маршрут лог стоп"
            if (string.Equals(args, "лог", StringComparison.OrdinalIgnoreCase))
            {
                _routeManager.EnableRouteLog();
                command.Handled = true;
                return;
            }
            if (string.Equals(args, "лог стоп", StringComparison.OrdinalIgnoreCase))
            {
                _routeManager.DisableRouteLog();
                command.Handled = true;
                return;
            }

            // "#route lookahead [N|off]"
            if (args.StartsWith(Resources.RouteCommandLookahead, StringComparison.OrdinalIgnoreCase))
            {
                var lookaheadArg = args.Length > Resources.RouteCommandLookahead.Length
                    ? args.Substring(Resources.RouteCommandLookahead.Length).Trim()
                    : string.Empty;

                if (string.IsNullOrEmpty(lookaheadArg))
                {
                    PrintLookaheadStatus();
                }
                else if (string.Equals(lookaheadArg, "off", StringComparison.OrdinalIgnoreCase)
                         || lookaheadArg == "0")
                {
                    _routeManager.SetLookahead(0);
                    Say("Лукахед маршрута выключен.");
                }
                else if (int.TryParse(lookaheadArg, out int depth) && depth > 0)
                {
                    _routeManager.SetLookahead(depth);
                    Say(string.Format("Лукахед маршрута: {0} комнат(ы) вперёд.", _routeManager.LookaheadSize));
                }

                command.Handled = true;
                return;
            }
        }

        private void PrintHelp()
        {
            Say(Resources.RouteHelpGoto);
            Say(Resources.RouteHelpStartRecording);
            Say(Resources.RouteHelpStopRecording);
            Say(Resources.RouteHelpCancelRecording);
            Say(Resources.RouteHelpRoute);
            Say(Resources.RouteHelpStopRoute);
            Say(Resources.RouteHelpLookahead);
            Say(Resources.RouteHelpHelp);
        }

        private void PrintLookaheadStatus()
        {
            if (_routeManager.LookaheadSize > 0)
                Say(string.Format("Лукахед: включён, {0} комнат(ы) вперёд.", _routeManager.LookaheadSize));
            else
                Say("Лукахед: выключен. Включить: #route lookahead N");
        }

        private void Say(string text)
        {
            base.PushMessageToConveyor(new InfoMessage(text, TextColor.BrightYellow));
        }
    }
}
