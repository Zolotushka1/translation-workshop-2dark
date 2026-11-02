using System.Windows;
using System.Windows.Controls;

namespace Workshop.App;

/// <summary>Native child windows cannot be scaled by WPF transforms; give them a real 16:9 client instead.</summary>
public sealed class AspectBox : Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(Fit(constraint));
        return new(double.IsInfinity(constraint.Width) ? 640 : constraint.Width,
            double.IsInfinity(constraint.Height) ? 360 : constraint.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = Fit(arrangeSize);
        Child?.Arrange(new Rect(new Point((arrangeSize.Width - size.Width) / 2, (arrangeSize.Height - size.Height) / 2), size));
        return arrangeSize;
    }

    private static Size Fit(Size size)
    {
        double width = double.IsInfinity(size.Width) ? 640 : size.Width;
        double height = double.IsInfinity(size.Height) ? 360 : size.Height;
        width = Math.Max(0, Math.Min(width, height * 16 / 9));
        return new(width, width * 9 / 16);
    }
}
