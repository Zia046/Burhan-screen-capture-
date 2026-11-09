using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BurhanScreenCapture.Native;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views;

public enum RegionSelectMode { Free, FixedSize }

/// <summary>
/// Full virtual-screen overlay showing a frozen screenshot. Drag to select, click to pick the window under the
/// cursor, Enter/double-click for the hovered window, Esc or right-click to cancel. Works in physical pixels.
/// </summary>
public sealed class RegionSelectorWindow : Window
{
    private readonly BitmapSource _frozen;
    private readonly PixelRect _vs;
    private readonly RegionSelectMode _mode;
    private readonly Canvas _canvas = new();
    private readonly Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle _border = new() { Stroke = new SolidColorBrush(Color.FromRgb(61, 139, 255)), StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Border _sizeTag = new() { Background = new SolidColorBrush(Color.FromArgb(220, 20, 24, 32)), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), IsHitTestVisible = false };
    private readonly TextBlock _sizeText = new() { Foreground = Brushes.White, FontSize = 12 };
    private readonly List<(IntPtr Handle, PixelRect Bounds)> _windows;
    private Point? _dragStart;
    private PixelRect _current;
    private bool _dragging;

    public PixelRect? Result { get; private set; }

    public RegionSelectorWindow(BitmapSource frozen, PixelRect virtualScreen, RegionSelectMode mode, string? hint)
    {
        _frozen = frozen;
        _vs = virtualScreen;
        _mode = mode;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;
        Title = "Select area";

        var ourHandles = Application.Current.Windows.OfType<Window>().Select(WindowEffects.Handle).ToHashSet();
        _windows = mode == RegionSelectMode.Free ? WindowEnum.VisibleWindows(ourHandles) : new();

        var img = new Image { Source = frozen, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        _canvas.Children.Add(img);
        _canvas.Children.Add(_shade);
        _canvas.Children.Add(_border);
        _sizeTag.Child = _sizeText;
        _canvas.Children.Add(_sizeTag);

        var hintBox = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 24, 32)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 13,
                Text = hint ?? (mode == RegionSelectMode.FixedSize
                    ? $"Move to position the {SettingsService.Current.FixedCaptureWidth}×{SettingsService.Current.FixedCaptureHeight} frame, click to capture • Esc to cancel"
                    : "Drag to select an area • Click to capture a window • Esc to cancel")
            }
        };
        _canvas.Children.Add(hintBox);

        Content = _canvas;
        SourceInitialized += (_, _) => WindowEffects.SetPhysicalBounds(this, _vs.X, _vs.Y, _vs.Width, _vs.Height);
        Loaded += (_, _) =>
        {
            img.Width = ActualWidth; img.Height = ActualHeight;
            var monitor = ScreenCaptureService.PrimaryMonitor.Bounds;
            hintBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var center = ToDip(new PixelRect(monitor.X - _vs.X, monitor.Y - _vs.Y, monitor.Width, monitor.Height));
            Canvas.SetLeft(hintBox, center.X + (center.Width - hintBox.DesiredSize.Width) / 2);
            Canvas.SetTop(hintBox, center.Y + 24);
            Activate();
            Focus();
            UpdateFromMouse(Mouse.GetPosition(_canvas));
        };
        SizeChanged += (_, _) => { img.Width = ActualWidth; img.Height = ActualHeight; Redraw(); };

        MouseLeftButtonDown += OnDown;
        MouseMove += (_, e) => UpdateFromMouse(e.GetPosition(_canvas));
        MouseLeftButtonUp += OnUp;
        MouseRightButtonUp += (_, _) => Close();
        KeyDown += OnKey;
    }

    private double ScaleX => _frozen.PixelWidth / Math.Max(1, ActualWidth);
    private double ScaleY => _frozen.PixelHeight / Math.Max(1, ActualHeight);

    private (int X, int Y) ToPixel(Point p) =>
        ((int)Math.Round(Math.Clamp(p.X * ScaleX, 0, _frozen.PixelWidth)), (int)Math.Round(Math.Clamp(p.Y * ScaleY, 0, _frozen.PixelHeight)));

    private Rect ToDip(PixelRect local) => new(local.X / ScaleX, local.Y / ScaleY, local.Width / ScaleX, local.Height / ScaleY);

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode == RegionSelectMode.FixedSize) { Accept(); return; }
        if (e.ClickCount == 2 && !_current.IsEmpty) { Accept(); return; }
        _dragStart = e.GetPosition(_canvas);
        _dragging = false;
        CaptureMouse();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        ReleaseMouseCapture();
        _dragStart = null;
        if (!_dragging)
        {
            // Simple click: use hovered window, or the monitor under the cursor
            if (_current.IsEmpty)
            {
                var (px, py) = ToPixel(e.GetPosition(_canvas));
                var m = ScreenCaptureService.Monitors().FirstOrDefault(mm => mm.Bounds.Contains(px + _vs.X, py + _vs.Y));
                if (m is not null) _current = new PixelRect(m.Bounds.X - _vs.X, m.Bounds.Y - _vs.Y, m.Bounds.Width, m.Bounds.Height);
            }
        }
        if (_current.Width >= 3 && _current.Height >= 3) Accept();
    }

    private void UpdateFromMouse(Point p)
    {
        var (px, py) = ToPixel(p);
        if (_mode == RegionSelectMode.FixedSize)
        {
            int w = Math.Min(SettingsService.Current.FixedCaptureWidth, _frozen.PixelWidth);
            int h = Math.Min(SettingsService.Current.FixedCaptureHeight, _frozen.PixelHeight);
            int x = Math.Clamp(px - w / 2, 0, _frozen.PixelWidth - w);
            int y = Math.Clamp(py - h / 2, 0, _frozen.PixelHeight - h);
            _current = new PixelRect(x, y, w, h);
        }
        else if (_dragStart is { } s)
        {
            var (sx, sy) = ToPixel(s);
            if (!_dragging && (Math.Abs(px - sx) > 3 || Math.Abs(py - sy) > 3)) _dragging = true;
            if (_dragging)
                _current = new PixelRect(Math.Min(sx, px), Math.Min(sy, py), Math.Abs(px - sx), Math.Abs(py - sy));
        }
        else
        {
            // Hover: highlight the top-most window under the cursor
            _current = default;
            foreach (var (_, b) in _windows)
            {
                if (b.Contains(px + _vs.X, py + _vs.Y))
                {
                    _current = new PixelRect(b.X - _vs.X, b.Y - _vs.Y, b.Width, b.Height);
                    break;
                }
            }
        }
        Redraw();
    }

    private void Redraw()
    {
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        if (_current.IsEmpty)
        {
            _shade.Data = full;
            _border.Visibility = Visibility.Collapsed;
            _sizeTag.Visibility = Visibility.Collapsed;
            return;
        }
        var r = ToDip(_current);
        _shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(r));
        _border.Visibility = Visibility.Visible;
        _border.Width = r.Width; _border.Height = r.Height;
        Canvas.SetLeft(_border, r.X); Canvas.SetTop(_border, r.Y);
        _sizeTag.Visibility = Visibility.Visible;
        _sizeText.Text = $"{_current.Width} × {_current.Height}";
        Canvas.SetLeft(_sizeTag, r.X);
        Canvas.SetTop(_sizeTag, r.Y > 26 ? r.Y - 24 : r.Y + 4);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Enter && !_current.IsEmpty) Accept();
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _current = new PixelRect(0, 0, _frozen.PixelWidth, _frozen.PixelHeight);
            Accept();
        }
    }

    private void Accept()
    {
        var local = _current.Intersect(new PixelRect(0, 0, _frozen.PixelWidth, _frozen.PixelHeight));
        if (local.IsEmpty) return;
        Result = new PixelRect(local.X + _vs.X, local.Y + _vs.Y, local.Width, local.Height);
        Close();
    }
}
