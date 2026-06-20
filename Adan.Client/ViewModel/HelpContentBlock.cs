namespace Adan.Client.ViewModel
{
    /// <summary>
    /// One paragraph of help content: either a prose block (<see cref="Text"/>
    /// set, <see cref="Code"/> null) or a Lua code example (<see cref="Code"/>
    /// set, <see cref="Text"/> null). Never both set.
    /// </summary>
    public class HelpContentBlock
    {
        private HelpContentBlock(string text, string code)
        {
            Text = text;
            Code = code;
        }

        public static HelpContentBlock ForText(string text)
        {
            return new HelpContentBlock(text, null);
        }

        public static HelpContentBlock ForCode(string code)
        {
            return new HelpContentBlock(null, code);
        }

        public string Text { get; private set; }

        public string Code { get; private set; }

        public bool IsCode
        {
            get { return Code != null; }
        }
    }
}
