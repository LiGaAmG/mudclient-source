namespace Adan.Client.ViewModel
{
    /// <summary>
    /// One entry in the static Lua scripting help list.
    /// </summary>
    public class HelpTopic
    {
        public HelpTopic(string title, string content)
        {
            Title = title;
            Content = content;
        }

        public string Title
        {
            get;
            private set;
        }

        public string Content
        {
            get;
            private set;
        }
    }
}
