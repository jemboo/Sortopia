using System.Windows;
using System.Windows.Controls;

namespace Sortopia.WPF.Views.Shared;

/// <summary>Wraps controls at their natural widths, then fills each row evenly.</summary>
public sealed class JustifiedWrapPanel : Panel
{
    private const double Gap = 6;

    private List<List<UIElement>> Rows(double width)
    {
        var rows = new List<List<UIElement>>();
        var row = new List<UIElement>();
        double used = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var next = child.DesiredSize.Width + (row.Count == 0 ? 0 : Gap);
            if (row.Count > 0 && used + next > width)
            {
                rows.Add(row); row = []; used = 0; next = child.DesiredSize.Width;
            }
            row.Add(child); used += next;
        }
        if (row.Count > 0) rows.Add(row);
        return rows;
    }

    private static double RowWidth(List<UIElement> row) => row.Sum(child => child.DesiredSize.Width) + Gap * (row.Count - 1);
    private static double RowHeight(List<UIElement> row) => row.Max(child => child.DesiredSize.Height);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var rows = Rows(availableSize.Width);
        return rows.Count == 0 ? new Size() : new Size(rows.Max(RowWidth), rows.Sum(RowHeight) + Gap * (rows.Count - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var row in Rows(finalSize.Width))
        {
            var extra = Math.Max(0, finalSize.Width - RowWidth(row)) / row.Count;
            var height = RowHeight(row);
            double x = 0;
            foreach (var child in row)
            {
                var width = child.DesiredSize.Width + extra;
                child.Arrange(new Rect(x, y, width, height)); x += width + Gap;
            }
            y += height + Gap;
        }
        return finalSize;
    }
}
