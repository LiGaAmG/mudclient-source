namespace Adan.Client.ViewModel
{
    /// <summary>
    /// One row in the Help window's left-hand navigation list: either a
    /// non-selectable category header or a selectable topic.
    /// </summary>
    public class HelpListEntry
    {
        private HelpListEntry(string headerText, HelpTopic topic)
        {
            HeaderText = headerText;
            Topic = topic;
        }

        public static HelpListEntry ForHeader(string headerText)
        {
            return new HelpListEntry(headerText, null);
        }

        public static HelpListEntry ForTopic(HelpTopic topic)
        {
            return new HelpListEntry(null, topic);
        }

        public string HeaderText { get; private set; }

        public HelpTopic Topic { get; private set; }

        public bool IsHeader
        {
            get { return Topic == null; }
        }
    }
}
