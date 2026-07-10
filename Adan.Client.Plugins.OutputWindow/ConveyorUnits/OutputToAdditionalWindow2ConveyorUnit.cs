namespace Adan.Client.Plugins.OutputWindow.ConveyorUnits
{
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using Common.Commands;
    using Common.Conveyor;
    using Common.ConveyorUnits;
    using Common.Messages;
    using CSLib.Net.Diagnostics;
    using Messages;

    public class OutputToAdditionalWindow2ConveyorUnit : ConveyorUnit
    {
        private readonly Regex _regexOutput = new Regex(@"#output2\s+\{?(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private AdditionalOutputWindowManager _manager;

        public OutputToAdditionalWindow2ConveyorUnit(AdditionalOutputWindowManager manager, MessageConveyor conveyor)
            : base(conveyor)
        {
            _manager = manager;
        }

        public override IEnumerable<int> HandledMessageTypes
        {
            get { return new[] { BuiltInMessageTypes.TextMessage }; }
        }

        public override IEnumerable<int> HandledCommandTypes
        {
            get { return new[] { BuiltInCommandTypes.TextCommand }; }
        }

        public override void HandleCommand(Command command, bool isImport = false)
        {
            Assert.ArgumentNotNull(command, "command");

            var textCommand = command as TextCommand;
            if (textCommand == null)
                return;

            string commandText = textCommand.CommandText.Trim();
            Match match = _regexOutput.Match(commandText);
            if (match.Success)
            {
                textCommand.Handled = true;

                if (!match.Groups[1].Success)
                    return;

                string str = match.Groups[1].Value;
                Conveyor.PushMessage(new OutputToAdditionalWindow2Message(str[str.Length - 1] == '}' ? str.Substring(0, str.Length - 1) : str));
            }
        }

        public override void HandleMessage(Message message)
        {
            var outputMessage = message as OutputToAdditionalWindow2Message;
            if (outputMessage != null)
            {
                outputMessage.Handled = true;
                _manager.AddText(Conveyor.RootModel, outputMessage);
            }
        }
    }
}
