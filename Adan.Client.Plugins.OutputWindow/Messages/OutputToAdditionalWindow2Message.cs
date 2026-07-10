namespace Adan.Client.Plugins.OutputWindow.Messages
{
    using Common.Messages;
    using Common.Themes;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    public class OutputToAdditionalWindow2Message : TextMessage
    {
        public OutputToAdditionalWindow2Message([NotNull] TextMessage originalMessage)
            : base(originalMessage)
        {
            Assert.ArgumentNotNull(originalMessage, "originalMessage");
            this.SkipSubstitution = true;
            this.SkipTriggers = true;
        }

        public OutputToAdditionalWindow2Message(string text)
            : base(text)
        {
            this.SkipSubstitution = true;
            this.SkipTriggers = true;
        }

        public OutputToAdditionalWindow2Message([NotNull] string text, TextColor foregroundColor, TextColor backgroundColor)
            : base(text, foregroundColor, backgroundColor)
        {
            Assert.ArgumentNotNull(text, "text");
            this.SkipSubstitution = true;
            this.SkipTriggers = true;
        }

        public override int MessageType
        {
            get { return BuiltInMessageTypes.TextMessage; }
        }

        public override TextMessage Clone()
        {
            return new OutputToAdditionalWindow2Message(this);
        }
    }
}
