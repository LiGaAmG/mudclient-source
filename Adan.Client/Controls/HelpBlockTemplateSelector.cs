namespace Adan.Client.Controls
{
    using System.Windows;
    using System.Windows.Controls;

    using ViewModel;

    /// <summary>
    /// Picks the rendering template for a single <see cref="HelpContentBlock"/>:
    /// prose blocks render as plain wrapped text, code blocks render inside a
    /// read-only <see cref="Adan.Client.Common.Controls.LuaCodeEditor"/> so
    /// examples get the same syntax highlighting as the script editor.
    /// </summary>
    public class HelpBlockTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TextTemplate { get; set; }

        public DataTemplate CodeTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var block = item as HelpContentBlock;
            if (block != null && block.IsCode)
            {
                return CodeTemplate;
            }

            return TextTemplate;
        }
    }
}
