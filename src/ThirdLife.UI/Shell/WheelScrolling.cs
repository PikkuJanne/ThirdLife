using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ThirdLife.UI.Shell;

/// <summary>Keeps fractional wheel input proportional in the vertically scrolling audit views.</summary>
internal sealed class WheelScrolling
{
    private const int WheelDetent = 120;
    private const int MaximumLinesPerEvent = 3;
    private const double LineHeight = 16;
    private readonly Dictionary<ScrollViewer, double> _queuedOffsets = [];
    private DispatcherOperation? _clearOffsets;

    public void Scroll(ScrollViewer shell, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        int configuredLines = SystemParameters.WheelScrollLines;
        int lines = configuredLines < 0 ? MaximumLinesPerEvent : Math.Min(configuredLines, MaximumLinesPerEvent);
        double distance = -Math.Clamp(e.Delta, -WheelDetent, WheelDetent) / (double)WheelDetent * lines * LineHeight;

        for (DependencyObject? element = source; element is not null; element = ParentOf(element))
        {
            if (element is ComboBox { IsDropDownOpen: true } or ComboBox { IsKeyboardFocusWithin: true } or Calendar)
            {
                return;
            }

            if (element is not ScrollViewer viewer)
            {
                continue;
            }

            if (ReferenceEquals(viewer, shell))
            {
                TryScroll(viewer, distance);
                e.Handled = true;
                return;
            }

            if (IsPixelScrollHost(viewer) && TryScroll(viewer, distance))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private bool TryScroll(ScrollViewer viewer, double distance)
    {
        var current = _queuedOffsets.TryGetValue(viewer, out var pending) ? pending : viewer.VerticalOffset;
        var target = Math.Clamp(current + distance, 0, viewer.ScrollableHeight);
        if (target == current)
        {
            return false;
        }

        // WPF applies ScrollToVerticalOffset during layout. Preserve pulses queued before it runs.
        _queuedOffsets[viewer] = target;
        viewer.ScrollToVerticalOffset(target);
        _clearOffsets ??= viewer.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ClearOffsets));
        return true;
    }

    private void ClearOffsets()
    {
        _queuedOffsets.Clear();
        _clearOffsets = null;
    }

    private static bool IsPixelScrollHost(ScrollViewer viewer) => viewer.TemplatedParent switch
    {
        ListBox list => VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel,
        TextBox { IsReadOnly: true } => true,
        _ => false,
    };

    private static DependencyObject? ParentOf(DependencyObject element) => element switch
    {
        Visual visual => VisualTreeHelper.GetParent(visual),
        ContentElement content => ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent,
        _ => LogicalTreeHelper.GetParent(element),
    };
}
