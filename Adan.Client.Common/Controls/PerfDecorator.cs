namespace Adan.Client.Common.Controls
{
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;

    /// <summary>
    /// Диагностическая обёртка: меряет время Measure/Arrange вложенного дерева
    /// и пишет в perf-лог всё, что дольше 10 мс. Имя задаётся через Tag.
    /// </summary>
    public class PerfDecorator : Decorator
    {
        protected override Size MeasureOverride(Size constraint)
        {
            var sw = Stopwatch.StartNew();
            var result = base.MeasureOverride(constraint);
            sw.Stop();
            if (sw.ElapsedMilliseconds >= 10)
                Conveyor.PerfLog.WriteTotal("LAYOUT_MEASURE", sw.ElapsedMilliseconds, Tag as string ?? "?");
            return result;
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            var sw = Stopwatch.StartNew();
            var result = base.ArrangeOverride(arrangeSize);
            sw.Stop();
            if (sw.ElapsedMilliseconds >= 10)
                Conveyor.PerfLog.WriteTotal("LAYOUT_ARRANGE", sw.ElapsedMilliseconds, Tag as string ?? "?");
            return result;
        }
    }
}
