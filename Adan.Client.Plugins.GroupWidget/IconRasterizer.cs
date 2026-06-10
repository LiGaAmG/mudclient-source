namespace Adan.Client.Plugins.GroupWidget
{
    using System;
    using System.Collections.Generic;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    /// <summary>
    /// Однократная растеризация VisualBrush-иконок в bitmap-кисти.
    /// VisualBrush держит живой Visual и перерисовывает его при каждом рендере —
    /// с десятками иконок в виджетах это давало кадры по 200-2800 мс.
    /// Bitmap рисуется в 2x разрешении, визуально иконки не меняются.
    /// </summary>
    public static class IconRasterizer
    {
        private static readonly Dictionary<VisualBrush, ImageBrush> _cache = new Dictionary<VisualBrush, ImageBrush>();

        /// <summary>
        /// Проходит по словарю ресурсов и заменяет VisualBrush-кисти
        /// внутри GeometryDrawing на растеризованные ImageBrush.
        /// Вызывать на UI-потоке после InitializeComponent.
        /// </summary>
        private static int _replacedCount;
        private static int _failedCount;

        public static void RasterizeIcons(ResourceDictionary resources)
        {
            try
            {
                ProcessDictionary(resources);
                Common.Conveyor.PerfLog.WriteTotal("ICONS", 0,
                    string.Format("rasterized={0} failed={1}", _replacedCount, _failedCount));
            }
            catch (Exception ex)
            {
                Common.Utils.ErrorLogger.Instance.Write(string.Format("IconRasterizer error: {0}", ex));
            }
        }

        private static void ProcessDictionary(ResourceDictionary rd)
        {
            foreach (var merged in rd.MergedDictionaries)
                ProcessDictionary(merged);

            foreach (var key in rd.Keys)
            {
                object value;
                try { value = rd[key]; }
                catch { continue; }

                var gd = value as GeometryDrawing;
                if (gd != null)
                {
                    ReplaceBrush(gd);
                    continue;
                }

                var di = value as DrawingImage;
                if (di != null)
                {
                    ProcessDrawing(di.Drawing);
                }
            }
        }

        private static void ProcessDrawing(Drawing drawing)
        {
            var gd = drawing as GeometryDrawing;
            if (gd != null)
            {
                ReplaceBrush(gd);
                return;
            }

            var group = drawing as DrawingGroup;
            if (group != null)
            {
                foreach (var child in group.Children)
                    ProcessDrawing(child);
            }
        }

        private static void ReplaceBrush(GeometryDrawing gd)
        {
            var vb = gd.Brush as VisualBrush;
            if (vb == null) return;

            var bitmap = Rasterize(vb);
            if (bitmap != null)
            {
                gd.Brush = bitmap;
                _replacedCount++;
            }
            else
            {
                _failedCount++;
            }
        }

        private static ImageBrush Rasterize(VisualBrush vb)
        {
            ImageBrush cached;
            if (_cache.TryGetValue(vb, out cached))
                return cached;

            var element = vb.Visual as UIElement;
            if (element == null) return null;

            double width = 26, height = 26;
            var fe = element as FrameworkElement;
            if (fe != null)
            {
                if (!double.IsNaN(fe.Width) && fe.Width > 0) width = fe.Width;
                if (!double.IsNaN(fe.Height) && fe.Height > 0) height = fe.Height;
            }

            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();

            // 2x разрешение (192 dpi) — чтобы при масштабировании иконки оставались чёткими
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2),
                192, 192, PixelFormats.Pbgra32);
            rtb.Render(element);
            rtb.Freeze();

            var brush = new ImageBrush(rtb)
            {
                Stretch = vb.Stretch,
                TileMode = vb.TileMode,
                Viewport = vb.Viewport,
                ViewportUnits = vb.ViewportUnits,
                Viewbox = vb.Viewbox,
                ViewboxUnits = vb.ViewboxUnits
            };
            brush.Freeze();

            _cache[vb] = brush;
            return brush;
        }
    }
}
