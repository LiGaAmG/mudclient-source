namespace Adan.Client.Common.Utils
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    /// <summary>
    /// Converts a percent value (0-100) to a scale factor (0.0-1.0) for use with ScaleTransform.
    /// Avoids layout-invalidating Width binding: RenderTransform is render-only (no Measure/Arrange).
    /// </summary>
    public class PercentToScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double percent = System.Convert.ToDouble(value, culture);
            return Math.Max(0.0, Math.Min(1.0, percent / 100.0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
