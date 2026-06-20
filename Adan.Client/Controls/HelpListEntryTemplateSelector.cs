namespace Adan.Client.Controls
{
    using System.Windows;
    using System.Windows.Controls;

    using ViewModel;

    /// <summary>
    /// Picks the rendering template for a single <see cref="HelpListEntry"/>:
    /// category headers render as bold non-selectable labels, topics render
    /// as plain selectable rows showing the topic title.
    /// </summary>
    public class HelpListEntryTemplateSelector : DataTemplateSelector
    {
        public DataTemplate HeaderTemplate { get; set; }

        public DataTemplate TopicTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var entry = item as HelpListEntry;
            if (entry != null && entry.IsHeader)
            {
                return HeaderTemplate;
            }

            return TopicTemplate;
        }
    }
}
