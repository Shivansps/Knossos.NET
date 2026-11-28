using System;
using Avalonia;
using Avalonia.Controls;

namespace Knossos.NET.Views;

public partial class ModIngameSettingsView : UserControl
{
    public ModIngameSettingsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 480;
            Classes.Set("compact", compact);
            SettingsLayout.Margin = new Thickness(compact ? 12 : 24, 20);
        };
    }
}

public sealed class IngameSettingsPanel : Panel
{
    private const double MinimumColumnWidth = 380;
    private const double ColumnGap = 16;

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = GetColumnCount(availableSize.Width);
        var columnWidth = GetColumnWidth(availableSize.Width, columns);
        var height = 0d;
        var rowHeight = 0d;
        var desiredWidth = 0d;
        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if ((index + 1) % columns == 0 || index + 1 == Children.Count)
            {
                height += rowHeight;
                rowHeight = 0;
            }
        }

        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : desiredWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = GetColumnCount(finalSize.Width);
        var columnWidth = GetColumnWidth(finalSize.Width, columns);
        var top = 0d;
        for (var index = 0; index < Children.Count; index += columns)
        {
            var cells = Math.Min(columns, Children.Count - index);
            var rowHeight = Children[index].DesiredSize.Height;
            if (cells == 2) rowHeight = Math.Max(rowHeight, Children[index + 1].DesiredSize.Height);
            for (var column = 0; column < cells; column++)
                Children[index + column].Arrange(new Rect(column * (columnWidth + ColumnGap), top, columnWidth, rowHeight));
            top += rowHeight;
        }

        return finalSize;
    }

    private int GetColumnCount(double width) => Children.Count > 1 && double.IsFinite(width) &&
        width >= 2 * MinimumColumnWidth + ColumnGap ? 2 : 1;

    private static double GetColumnWidth(double width, int columns) => (width - (columns - 1) * ColumnGap) / columns;
}
