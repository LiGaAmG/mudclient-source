namespace Adan.Client.ViewModel
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// One entry in the static Lua scripting help list.
    /// </summary>
    public class HelpTopic
    {
        public HelpTopic(string category, string title, List<HelpContentBlock> blocks)
        {
            Category = category;
            Title = title;
            Blocks = blocks;
        }

        public string Category { get; private set; }

        public string Title { get; private set; }

        public List<HelpContentBlock> Blocks { get; private set; }

        /// <summary>
        /// Flattened text used for search matching across both prose and code blocks.
        /// </summary>
        public string SearchableText
        {
            get { return Title + " " + string.Join(" ", Blocks.Select(b => b.Text ?? b.Code ?? string.Empty)); }
        }
    }
}
