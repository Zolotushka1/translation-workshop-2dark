using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Workshop.Core;

namespace Workshop.App;

/// <summary>Present a native game in the workshop's existing top-level window.</summary>
public sealed class FullscreenWindow
{
    private readonly Window owner;
    private readonly UIElement editor;
    private GameHost? active;
    private readonly Popup label = new() { AllowsTransparency = true, StaysOpen = true, IsHitTestVisible = false, Placement = PlacementMode.AbsolutePoint };
    private readonly TextBlock text = new() { FontWeight = FontWeights.SemiBold, Padding = new Thickness(8, 3, 8, 3), IsHitTestVisible = false };
    private WorkshopSettings settings;
    private bool opened;
    private WindowStyle oldStyle;
    private WindowState oldState;
    private ResizeMode oldResize;
    private Brush oldBackground = Brushes.Black;
    public event EventHandler? Closed;
    public bool Russian { get; private set; }
    public FullscreenWindow(Window owner, WorkshopSettings options)
    {
        this.owner = owner; editor = (UIElement)owner.Content; settings = options; label.Child = text;
    }
    public void ShowGame(GameHost host, bool russian, WorkshopSettings options)
    {
        if (!host.IsAttached) throw new InvalidOperationException("Сначала запустите выбранную копию игры.");
        if (active != host && active?.IsAttached == true) active.Present(0);
        active = host; Russian = russian; settings = options;
        if (!opened)
        {
            opened = true;
            oldStyle = owner.WindowStyle; oldState = owner.WindowState; oldResize = owner.ResizeMode; oldBackground = owner.Background;
            editor.Visibility = Visibility.Collapsed;
            owner.Background = Brushes.Black; owner.WindowState = WindowState.Normal;
            owner.WindowStyle = WindowStyle.None; owner.ResizeMode = ResizeMode.NoResize; owner.WindowState = WindowState.Maximized;
            owner.SizeChanged += OwnerSizeChanged; owner.LocationChanged += OwnerLocationChanged;
            owner.Activated += OwnerActivated; owner.Deactivated += OwnerDeactivated;
        }
        active.Present(new WindowInteropHelper(owner).Handle);
        text.Text = russian ? "RU ver." : "EN ver.";
        text.Inlines.Add(new System.Windows.Documents.LineBreak());
        text.Inlines.Add(new System.Windows.Documents.Run(options.FullscreenHint(russian)) { FontSize = options.LabelSize * .65 });
        text.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(options.LabelColor));
        text.Opacity = options.LabelOpacity; text.FontSize = options.LabelSize;
        label.IsOpen = options.ShowVersionLabel; PositionLabel();
    }
    public void Close()
    {
        if (!opened) return;
        opened = false; label.IsOpen = false;
        owner.SizeChanged -= OwnerSizeChanged; owner.LocationChanged -= OwnerLocationChanged;
        owner.Activated -= OwnerActivated; owner.Deactivated -= OwnerDeactivated;
        if (active?.IsAttached == true) active.Present(0); active = null;
        editor.Visibility = Visibility.Visible; owner.WindowState = WindowState.Normal;
        owner.WindowStyle = oldStyle; owner.ResizeMode = oldResize; owner.Background = oldBackground; owner.WindowState = oldState;
        Closed?.Invoke(this, EventArgs.Empty);
    }
    private void OwnerSizeChanged(object sender, SizeChangedEventArgs e) { active?.ResizePresentation(); PositionLabel(); }
    private void OwnerLocationChanged(object? sender, EventArgs e) => PositionLabel();
    private void OwnerActivated(object? sender, EventArgs e) { label.IsOpen = settings.ShowVersionLabel; PositionLabel(); }
    private void OwnerDeactivated(object? sender, EventArgs e) => label.IsOpen = false;
    private void PositionLabel()
    {
        if (!opened || !settings.ShowVersionLabel) return;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var origin = owner.PointToScreen(new Point()); var dpi = VisualTreeHelper.GetDpi(owner);
        double x = origin.X / dpi.DpiScaleX, y = origin.Y / dpi.DpiScaleY;
        double width = owner.ActualWidth, height = owner.ActualHeight, tw = text.DesiredSize.Width, th = text.DesiredSize.Height;
        var offset = settings.LabelOffset(width, height, tw, th);
        label.HorizontalOffset = x + offset.X; label.VerticalOffset = y + offset.Y;
        if (label.IsOpen && PresentationSource.FromVisual(text) is HwndSource source)
        {
            nint style = Native.GetWindowLongPtr(source.Handle, -20);
            Native.SetWindowLongPtr(source.Handle, -20, (nint)(style.ToInt64() | 0x20 | 0x08000000));
        }
    }
    private static class Native
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    }
}
