namespace Adan.Client.Map.ConveyorUnits
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Common.Commands;
    using Common.Conveyor;
    using Common.ConveyorUnits;
    using Common.Messages;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Model;

    /// <summary>
    /// Handles 'травник' commands for herb-gathering automation.
    /// </summary>
    public class HerbUnit : ConveyorUnit
    {
        private readonly HerbManager _herbManager;
        private readonly char[] _splitChars = { ' ' };

        // Matches "травничество 125%" (anywhere in line)
        private static readonly Regex _herbSkillRegex =
            new Regex(@"травничество\s+(\d+)%", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Matches "X растет здесь" or "X виднеется здесь" (with optional trailing period/space)
        private static readonly Regex _herbRoomRegex =
            new Regex(@"^(.+?)\s+(растет|виднеется)\s+здесь\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Command words
        private const string CmdPrefix           = "травник";
        private const string CmdStart            = "старт";
        private const string CmdStop             = "стоп";
        private const string CmdHome             = "домой";
        private const string CmdList             = "список";
        private const string CmdAuto             = "авто";
        private const string CmdAutoStop         = "авто стоп";
        private const string CmdInvis            = "невидимость";
        private const string CmdInvisOff         = "невидимость выкл";
        private const string ArgDangerous        = "опасно";
        private const string ArgVeryDangerous    = "очень опасно";

        public HerbUnit([NotNull] HerbManager herbManager, [NotNull] MessageConveyor conveyor)
            : base(conveyor)
        {
            Assert.ArgumentNotNull(herbManager, "herbManager");
            _herbManager = herbManager;
        }

        public override IEnumerable<int> HandledMessageTypes
            => Enumerable.Repeat(BuiltInMessageTypes.TextMessage, 1);

        public override void HandleMessage(Message message)
        {
            var textMessage = message as TextMessage;
            if (textMessage == null || textMessage.InnerText == null) return;

            var text = textMessage.InnerText;

            // "Вы успешно собрали..."
            if (text.StartsWith("Вы успешно собрали", StringComparison.OrdinalIgnoreCase))
            {
                _herbManager.OnHerbCollected();
                return;
            }

            // "травничество 125%"
            if (text.IndexOf("травничество", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var skillMatch = _herbSkillRegex.Match(text);
                if (skillMatch.Success)
                {
                    int skill;
                    if (int.TryParse(skillMatch.Groups[1].Value, out skill))
                        _herbManager.OnHerbingSkillDetected(skill);
                    return;
                }
            }

            // "Клевер растет здесь." / "Земляной корень виднеется здесь."
            if (text.IndexOf("здесь", StringComparison.OrdinalIgnoreCase) < 0) return;
            var herbMatch = _herbRoomRegex.Match(text);
            if (herbMatch.Success)
            {
                // normalized: lowercase, verb collapsed to "растет/виднеется здесь"
                string subject = herbMatch.Groups[1].Value.Trim();
                string verb    = herbMatch.Groups[2].Value.ToLowerInvariant();
                string normalized = subject.ToLowerInvariant() + " " + verb + " здесь";
                _herbManager.OnRoomMessageReceived(normalized);
            }
        }

        public override IEnumerable<int> HandledCommandTypes
            => Enumerable.Repeat(BuiltInCommandTypes.TextCommand, 1);

        public override void HandleCommand(Command command, bool isImport = false)
        {
            Assert.ArgumentNotNull(command, "command");

            var textCommand = command as TextCommand;
            if (textCommand == null) return;

            var text = textCommand.CommandText.Trim();

            // Exact match: "травник"
            if (string.Equals(text, CmdPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                _herbManager.ShowHelp();
                command.Handled = true;
                return;
            }

            if (!text.StartsWith(CmdPrefix + " ", System.StringComparison.OrdinalIgnoreCase))
                return;

            var args = text.Substring(CmdPrefix.Length).Trim();

            // "травник список"
            if (string.Equals(args, CmdList, System.StringComparison.OrdinalIgnoreCase))
            {
                _herbManager.ShowList();
                command.Handled = true;
                return;
            }

            // "травник стоп"
            if (string.Equals(args, CmdStop, System.StringComparison.OrdinalIgnoreCase))
            {
                _herbManager.StopGathering();
                command.Handled = true;
                return;
            }

            // "травник домой"
            if (string.Equals(args, CmdHome, System.StringComparison.OrdinalIgnoreCase))
            {
                _herbManager.ReturnHome();
                command.Handled = true;
                return;
            }

            // "травник авто стоп"
            if (string.Equals(args, CmdAutoStop, System.StringComparison.OrdinalIgnoreCase))
            {
                _herbManager.DisableAutoRepeat();
                command.Handled = true;
                return;
            }

            // "травник авто"
            if (string.Equals(args, CmdAuto, System.StringComparison.OrdinalIgnoreCase))
            {
                _herbManager.EnableAutoRepeat();
                command.Handled = true;
                return;
            }

            // "травник невидимость выкл"
            if (string.Equals(args, CmdInvisOff, System.StringComparison.OrdinalIgnoreCase))
            {
                _herbManager.DisableInvisibility();
                command.Handled = true;
                return;
            }

            // "травник невидимость <команда>"
            if (args.StartsWith(CmdInvis + " ", System.StringComparison.OrdinalIgnoreCase))
            {
                var invisCmd = args.Substring(CmdInvis.Length).Trim();
                if (!string.IsNullOrEmpty(invisCmd))
                {
                    _herbManager.SetInvisibilityCommand(invisCmd);
                    command.Handled = true;
                }
                return;
            }

            // "травник старт [опасно | очень опасно]"
            if (args.StartsWith(CmdStart, System.StringComparison.OrdinalIgnoreCase))
            {
                var dangerArg = args.Substring(CmdStart.Length).Trim();

                HerbDangerLevel level;
                if (string.Equals(dangerArg, ArgVeryDangerous, System.StringComparison.OrdinalIgnoreCase))
                    level = HerbDangerLevel.VeryDangerous;
                else if (string.Equals(dangerArg, ArgDangerous, System.StringComparison.OrdinalIgnoreCase))
                    level = HerbDangerLevel.Dangerous;
                else
                    level = HerbDangerLevel.Safe;

                _herbManager.StartGathering(level);
                command.Handled = true;
                return;
            }
        }
    }
}
