namespace Adan.Client.Map.Utils
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    /// <summary>
    /// Converts an enum value to bool by comparing it with ConverterParameter.
    /// Used for RadioButton binding to enum properties.
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
                return Enum.Parse(targetType, parameter.ToString());
            return System.Windows.DependencyProperty.UnsetValue;
        }
    }
}
