namespace Adan.Client.Plugins.OutputWindow.Model.Actions
{
    using System;
    using System.Text;
    using System.Xml.Serialization;
    using Common.Messages;
    using Common.Model;
    using Common.Themes;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Messages;

    [Serializable]
    public class OutputToAdditionalWindow2Action : ActionWithParameters
    {
        public OutputToAdditionalWindow2Action()
        {
            TextToOutput = string.Empty;
            TextColor = TextColor.None;
            BackgroundColor = TextColor.None;
        }

        public override bool IsGlobal
        {
            get { return false; }
        }

        [NotNull]
        [XmlAttribute]
        public string TextToOutput { get; set; }

        [XmlAttribute]
        public TextColor TextColor { get; set; }

        [XmlAttribute]
        public TextColor BackgroundColor { get; set; }

        [XmlAttribute]
        public bool OutputEntireMessageKeepingColors { get; set; }

        public override void Execute(RootModel model, ActionExecutionContext context)
        {
            Assert.ArgumentNotNull(model, "model");
            Assert.ArgumentNotNull(context, "context");

            var contextMessage = context.CurrentMessage as TextMessage;
            if (OutputEntireMessageKeepingColors && contextMessage != null)
            {
                model.PushMessageToConveyor(new OutputToAdditionalWindow2Message(contextMessage) { SkipTriggers = true, SkipSubstitution = true, SkipHighlight = true });
            }
            else
            {
                string str = PostProcessString(TextToOutput + GetParametersString(model, context), model, context);
                if (OutputEntireMessageKeepingColors)
                {
                    model.PushMessageToConveyor(new OutputToAdditionalWindow2Message(str) { SkipTriggers = true, SkipSubstitution = true, SkipHighlight = true });
                }
                else
                {
                    model.PushMessageToConveyor(new OutputToAdditionalWindow2Message(str, TextColor, BackgroundColor) { SkipTriggers = true, SkipSubstitution = true, SkipHighlight = true });
                }
            }
        }

        [NotNull]
        protected override string ReplaceVariables([NotNull] string input, [NotNull] RootModel rootModel)
        {
            StringBuilder sb = new StringBuilder();
            int lastPos = 0;
            int i = 0;
            while (i < input.Length)
            {
                if (input[i] == '$')
                {
                    if (i - lastPos > 0)
                        sb.Append(input, lastPos, i - lastPos);
                    i++;
                    int startPos = i;
                    while (i < input.Length && char.IsLetterOrDigit(input[i]))
                        i++;
                    sb.Append(rootModel.GetVariableValue(input.Substring(startPos, i - startPos)));
                    lastPos = i;
                }
                i++;
            }
            if (lastPos < input.Length)
                sb.Append(input, lastPos, input.Length - lastPos);
            return sb.ToString();
        }

        protected override string ReplaceParameters(string input, ActionExecutionContext context)
        {
            StringBuilder sb = new StringBuilder();
            int lastPos = 0;
            int i = 0;
            while (i < input.Length - 1)
            {
                if (input[i] == '%')
                {
                    if (i < input.Length - 2 && input[i + 1] == '%' && char.IsDigit(input[i + 2]))
                    {
                        if (i - lastPos > 0)
                            sb.Append(input, lastPos, i - lastPos);
                        sb.Append(GetParameter((int)Char.GetNumericValue(input[i + 2]), context));
                        lastPos = i + 3;
                        i += 2;
                    }
                    else if (char.IsDigit(input[i + 1]))
                    {
                        if (i - lastPos > 0)
                            sb.Append(input, lastPos, i - lastPos);
                        sb.Append(GetParameter((int)Char.GetNumericValue(input[i + 1]), context));
                        lastPos = i + 2;
                        i++;
                    }
                }
                i++;
            }
            if (lastPos < input.Length)
                sb.Append(input, lastPos, input.Length - lastPos);
            return sb.ToString();
        }

        public override string ToString()
        {
            return new StringBuilder().Append("#output2 {").Append(TextToOutput).Append("}").ToString();
        }
    }
}
