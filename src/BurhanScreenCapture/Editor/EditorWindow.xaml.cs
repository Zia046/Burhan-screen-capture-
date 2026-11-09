using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BurhanScreenCapture.Licensing;
using BurhanScreenCapture.Native;
using BurhanScreenCapture.Services;
using BurhanScreenCapture.Views;
using Microsoft.Win32;

namespace BurhanScreenCapture.Editor;

public enum EditorTool { Select, Arrow, Line, Rect, Ellipse, Text, Crop, Highlight, Filled, Step, Blur, Pixelate, Magnifier, Spotlight }

public partial class EditorWindow : Window
{
    private const int MaxUndo = 25;

    private BitmapSource? _base;
    private List<Annotation> _annots = new();
    private readonly Stack<(BitmapSource Img, List<Annotation> A)> _undo = new();
    private readonly Stack<(BitmapSource Img, List<Annotation> A)> _redo = new();
    private string? _path;
    private bool _dirty;

    private EditorTool _tool = EditorTool.Arrow;
    private EditorTool _prevTool = EditorTool.Arrow;
    private Color _color = Color.FromRgb(0xE5, 0x48, 0x4D);
    private Point _start;
    private bool _dragging;
    private Annotation? _draft;
    private Rect? _marquee;
    private int _selected = -1;
    private Annotation? _moveOriginal;
    private TextBox? _textInput;

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0xF9, 0x73, 0x16), Color.FromRgb(0xFA, 0xCC, 0x15), Color.FromRgb(0x22, 0xC5, 0x5E),
        Color.FromRgb(0x14, 0x63, 0xE6), Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x11, 0x11, 0x11), Color.FromRgb(0xFF, 0xFF, 0xFF)
    };

    public EditorWindow(BitmapSource? image, string? path)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        BuildSwatches();
        TArrow.IsChecked = true;
        PreviewKeyDown += OnKey;
        Drop += OnDropFile;
        Scroller.PreviewMouseWheel += OnWheel;
        Closing += OnClosing;
        Loaded += (_, _) => { if (image is not null) Load(image, path); else UpdateUi(); };
    }

    // ================================================================= document

    private void Load(BitmapSource img, string? path)
    {
        _base = ImageExportService.Normalize96(img);
        _annots = new();
        _undo.Clear(); _redo.Clear();
        _path = path;
        _dirty = false;
        _selected = -1;
        BaseImage.Source = _base;
        Layer.Width = _base.PixelWidth;
        Layer.Height = _base.PixelHeight;
        Render();
        ZoomToFit();
        UpdateUi();
    }

    private void Snapshot()
    {
        if (_base is null) return;
        _undo.Push((_base, _annots.ToList()));
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            foreach (var k in keep) _undo.Push(k);
        }
        _redo.Clear();
        _dirty = true;
    }

    private void SetBase(BitmapSource img, bool clearAnnotations)
    {
        _base = img;
        if (clearAnnotations) _annots = new();
        BaseImage.Source = _base;
        Layer.Width = _base.PixelWidth;
        Layer.Height = _base.PixelHeight;
        _selected = -1;
        Render();
        UpdateUi();
    }

    private void Restore((BitmapSource Img, List<Annotation> A) s)
    {
        _annots = s.A.ToList();
        SetBase(s.Img, false);
    }

    private void OnUndo(object? sender, RoutedEventArgs? e)
    {
        if (_undo.Count == 0 || _base is null) return;
        _redo.Push((_base, _annots.ToList()));
        Restore(_undo.Pop());
        _dirty = true;
    }

    private void OnRedo(object? sender, RoutedEventArgs? e)
    {
        if (_redo.Count == 0 || _base is null) return;
        _undo.Push((_base, _annots.ToList()));
        Restore(_redo.Pop());
        _dirty = true;
    }

    /// <summary>Base image with every annotation burned in, at exact pixel size.</summary>
    public BitmapSource? Flatten()
    {
        if (_base is null) return null;
        if (_annots.Count == 0) return _base;
        int w = _base.PixelWidth, h = _base.PixelHeight;
        var canvas = new Canvas { Width = w, Height = h };
        foreach (var a in _annots) canvas.Children.Add(a.Build(_base));
        var root = new Grid { Width = w, Height = h };
        root.Children.Add(new Image { Source = _base, Stretch = Stretch.None });
        root.Children.Add(canvas);
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        return rtb;
    }

    private void Render()
    {
        Layer.Children.Clear();
        if (_base is null) return;
        for (int i = 0; i < _annots.Count; i++)
        {
            var el = _annots[i].Build(_base);
            if (el is FrameworkElement fe) fe.Tag = i;
            Layer.Children.Add(el);
        }
        if (_draft is not null) Layer.Children.Add(_draft.Build(_base));
        if (_marquee is { } m)
        {
            var r = new Rectangle
            {
                Width = m.Width, Height = m.Height, Stroke = Brushes.White, StrokeThickness = 1.5 / Zoom.ScaleX,
                StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(40, 61, 139, 255)), IsHitTestVisible = false
            };
            Canvas.SetLeft(r, m.X); Canvas.SetTop(r, m.Y);
            Layer.Children.Add(r);
        }
        if (_selected >= 0 && _selected < _annots.Count && !_annots[_selected].Bounds.IsEmpty)
        {
            var b = _annots[_selected].Bounds;
            b.Inflate(4, 4);
            var sel = new Rectangle
            {
                Width = b.Width, Height = b.Height, Stroke = new SolidColorBrush(Color.FromRgb(61, 139, 255)),
                StrokeThickness = 1.5 / Zoom.ScaleX, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false
            };
            Canvas.SetLeft(sel, b.X); Canvas.SetTop(sel, b.Y);
            Layer.Children.Add(sel);
        }
    }

    private void UpdateUi()
    {
        bool has = _base is not null;
        EmptyState.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        Scroller.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        UndoBtn.IsEnabled = _undo.Count > 0;
        RedoBtn.IsEnabled = _redo.Count > 0;
        StatusText.Text = has ? $"{_base!.PixelWidth} × {_base.PixelHeight} px" + (_path is null ? "  •  not saved yet" : $"  •  {_path}") : "No image";
        ZoomText.Text = $"{Zoom.ScaleX * 100:0}%";
        Title = (_path is null ? "Untitled" : System.IO.Path.GetFileName(_path)) + (_dirty ? " •" : "") + " — Image Editor";
        ToolHelp.Text = _tool switch
        {
            EditorTool.Select => "Click an annotation to select it. Drag to move. Delete to remove.",
            EditorTool.Text => "Click where the text should go, type, then press Enter.",
            EditorTool.Crop => "Drag the area to keep.",
            EditorTool.Blur or EditorTool.Pixelate => "Drag over the area to hide.",
            EditorTool.Magnifier => "Drag over the detail to enlarge.",
            EditorTool.Spotlight => "Drag over the area to keep bright.",
            EditorTool.Step => "Click to place numbered steps 1, 2, 3…",
            _ => "Drag on the image to draw."
        };
    }

    // ================================================================= tools & colors

    private void BuildSwatches()
    {
        foreach (var c in Palette)
        {
            var b = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 6), Background = new SolidColorBrush(c),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)), Tag = c, Cursor = Cursors.Hand, ToolTip = "Color"
            };
            b.Template = SwatchTemplate();
            b.Click += (_, _) => { _color = (Color)b.Tag; HighlightSwatch(); ApplyColorToSelection(); };
            Swatches.Children.Add(b);
        }
        HighlightSwatch();
    }

    private static ControlTemplate SwatchTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        t.VisualTree = bd;
        return t;
    }

    private void HighlightSwatch()
    {
        foreach (Button b in Swatches.Children)
            b.BorderThickness = new Thickness((Color)b.Tag == _color ? 3 : 1);
    }

    private void ApplyColorToSelection()
    {
        if (_selected < 0 || _selected >= _annots.Count) return;
        var a = _annots[_selected];
        Annotation? changed = a switch
        {
            ArrowAnnotation x => x with { Color = _color },
            LineAnnotation x => x with { Color = _color },
            RectAnnotation x => x with { Color = _color },
            EllipseAnnotation x => x with { Color = _color },
            TextAnnotation x => x with { Color = _color },
            StepAnnotation x => x with { Color = _color },
            HighlightAnnotation x => x with { Color = _color },
            _ => null
        };
        if (changed is null) return;
        Snapshot();
        _annots[_selected] = changed;
        Render();
        UpdateUi();
    }

    private static bool IsPro(EditorTool t) => t is EditorTool.Highlight or EditorTool.Filled or EditorTool.Step or EditorTool.Blur
        or EditorTool.Pixelate or EditorTool.Magnifier or EditorTool.Spotlight;

    private static Feature FeatureOf(EditorTool t) => t switch
    {
        EditorTool.Blur => Feature.Blur,
        EditorTool.Pixelate => Feature.Pixelate,
        EditorTool.Magnifier => Feature.Magnifier,
        EditorTool.Spotlight => Feature.Spotlight,
        EditorTool.Highlight or EditorTool.Filled or EditorTool.Step => Feature.AdvancedAnnotation,
        EditorTool.Crop => Feature.Crop,
        EditorTool.Text => Feature.TextAnnotation,
        EditorTool.Arrow => Feature.Arrow,
        EditorTool.Line => Feature.Line,
        EditorTool.Rect => Feature.Rectangle,
        EditorTool.Ellipse => Feature.Ellipse,
        _ => Feature.Crop
    };

    private void OnTool(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        var t = Enum.Parse<EditorTool>(tag);
        if (IsPro(t) && !Entitlements.Require(FeatureOf(t), this))
        {
            SelectToolButton(_prevTool);
            return;
        }
        CommitText();
        _tool = t;
        _prevTool = t;
        _selected = -1;
        Layer.Cursor = t == EditorTool.Select ? Cursors.Arrow : t == EditorTool.Text ? Cursors.IBeam : Cursors.Cross;
        Render();
        UpdateUi();
    }

    private void SelectToolButton(EditorTool t)
    {
        if (FindName("T" + t) is RadioButton rb) rb.IsChecked = true;
    }

    // ================================================================= mouse

    private Point Clamp(Point p) => _base is null ? p : new Point(Math.Clamp(p.X, 0, _base.PixelWidth), Math.Clamp(p.Y, 0, _base.PixelHeight));

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_base is null) return;
        CommitText();
        var p = Clamp(e.GetPosition(Layer));
        _start = p;
        _dragging = true;
        Layer.CaptureMouse();

        switch (_tool)
        {
            case EditorTool.Select:
                _selected = HitTest(p);
                _moveOriginal = _selected >= 0 ? _annots[_selected] : null;
                if (_moveOriginal is not null) Snapshot();
                Render();
                break;
            case EditorTool.Text:
                _dragging = false;
                Layer.ReleaseMouseCapture();
                BeginText(p);
                e.Handled = true;
                break;
            case EditorTool.Step:
                _dragging = false;
                Layer.ReleaseMouseCapture();
                Snapshot();
                int n = _annots.OfType<StepAnnotation>().Count() + 1;
                _annots.Add(new StepAnnotation(p, n, _color, Math.Max(26, ThicknessSlider.Value * 7)));
                Render(); UpdateUi();
                break;
        }
    }

    private int HitTest(Point p)
    {
        for (int i = _annots.Count - 1; i >= 0; i--)
        {
            var b = _annots[i].Bounds;
            if (b.IsEmpty) continue;
            b.Inflate(6, 6);
            if (b.Contains(p)) return i;
        }
        return -1;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _base is null) return;
        var p = Clamp(e.GetPosition(Layer));
        double th = ThicknessSlider.Value;
        var rect = new Rect(_start, p);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _tool is EditorTool.Rect or EditorTool.Ellipse or EditorTool.Filled)
        {
            double s = Math.Max(rect.Width, rect.Height);
            rect = new Rect(_start.X <= p.X ? _start.X : _start.X - s, _start.Y <= p.Y ? _start.Y : _start.Y - s, s, s);
        }
        _draft = null;
        _marquee = null;
        switch (_tool)
        {
            case EditorTool.Select when _moveOriginal is not null && _selected >= 0:
                _annots[_selected] = _moveOriginal.Offset(p - _start);
                break;
            case EditorTool.Arrow: _draft = new ArrowAnnotation(_start, p, _color, th); break;
            case EditorTool.Line:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    p = Math.Abs(p.X - _start.X) > Math.Abs(p.Y - _start.Y) ? new Point(p.X, _start.Y) : new Point(_start.X, p.Y);
                _draft = new LineAnnotation(_start, p, _color, th);
                break;
            case EditorTool.Rect: _draft = new RectAnnotation(rect, _color, th, false); break;
            case EditorTool.Filled: _draft = new RectAnnotation(rect, _color, th, true); break;
            case EditorTool.Ellipse: _draft = new EllipseAnnotation(rect, _color, th); break;
            case EditorTool.Highlight: _draft = new HighlightAnnotation(rect, _color); break;
            case EditorTool.Crop:
            case EditorTool.Blur:
            case EditorTool.Pixelate:
            case EditorTool.Magnifier:
            case EditorTool.Spotlight:
                _marquee = rect;
                break;
        }
        Render();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || _base is null) return;
        _dragging = false;
        Layer.ReleaseMouseCapture();
        var p = Clamp(e.GetPosition(Layer));
        var rect = _marquee ?? new Rect(_start, p);
        bool big = rect.Width >= 4 && rect.Height >= 4;

        switch (_tool)
        {
            case EditorTool.Select:
                if (_moveOriginal is not null && (p - _start).Length < 1) { _undo.Pop(); }
                _moveOriginal = null;
                break;
            case EditorTool.Crop when big:
                Snapshot();
                SetBase(ImageEffects.Crop(Flatten()!, rect), true);
                break;
            case EditorTool.Blur when big:
                Snapshot();
                SetBase(ImageEffects.Blur(_base, rect, (int)StrengthSlider.Value), false);
                break;
            case EditorTool.Pixelate when big:
                Snapshot();
                SetBase(ImageEffects.Pixelate(_base, rect, (int)StrengthSlider.Value), false);
                break;
            case EditorTool.Magnifier when big:
            {
                Snapshot();
                double size = Math.Min(Math.Max(rect.Width, rect.Height) * 2.5, Math.Min(_base.PixelWidth, _base.PixelHeight) * 0.6);
                double lx = rect.Right + 24, ly = rect.Y - size * 0.25;
                if (lx + size > _base.PixelWidth) lx = rect.X - size - 24;
                lx = Math.Clamp(lx, 0, Math.Max(0, _base.PixelWidth - size));
                ly = Math.Clamp(ly, 0, Math.Max(0, _base.PixelHeight - size));
                _annots.Add(new MagnifierAnnotation(rect, new Rect(lx, ly, size, size), _color));
                break;
            }
            case EditorTool.Spotlight when big:
                Snapshot();
                _annots.Add(new SpotlightAnnotation(rect, 0.6));
                break;
            default:
                if (_draft is not null && ((_draft is ArrowAnnotation || _draft is LineAnnotation) ? (p - _start).Length >= 4 : big))
                {
                    Snapshot();
                    _annots.Add(_draft);
                }
                break;
        }
        _draft = null;
        _marquee = null;
        Render();
        UpdateUi();
    }

    // ================================================================= text

    private void BeginText(Point p)
    {
        _textInput = new TextBox
        {
            MinWidth = 120, FontSize = FontSlider.Value, Foreground = new SolidColorBrush(_color), AcceptsReturn = true,
            Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), BorderThickness = new Thickness(1), Tag = p
        };
        Canvas.SetLeft(_textInput, p.X); Canvas.SetTop(_textInput, p.Y);
        Layer.Children.Add(_textInput);
        _textInput.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { CommitText(); ke.Handled = true; }
            else if (ke.Key == Key.Escape) { Layer.Children.Remove(_textInput); _textInput = null; ke.Handled = true; }
        };
        _textInput.LostKeyboardFocus += (_, _) => CommitText();
        Dispatcher.BeginInvoke(() => { _textInput?.Focus(); Keyboard.Focus(_textInput); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitText()
    {
        var tb = _textInput;
        if (tb is null) return;
        _textInput = null;
        Layer.Children.Remove(tb);
        if (!string.IsNullOrWhiteSpace(tb.Text))
        {
            Snapshot();
            _annots.Add(new TextAnnotation((Point)tb.Tag, tb.Text.TrimEnd(), _color, FontSlider.Value, TextBgBox.IsChecked == true));
        }
        Render();
        UpdateUi();
    }

    // ================================================================= commands

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_textInput is not null && _textInput.IsKeyboardFocusWithin) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: OnUndo(null, null); e.Handled = true; return;
                case Key.Y: OnRedo(null, null); e.Handled = true; return;
                case Key.S: if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) OnSaveAs(null, null); else OnSave(null, null); e.Handled = true; return;
                case Key.C: OnCopy(null, null); e.Handled = true; return;
                case Key.V: OnPaste(null, null); e.Handled = true; return;
                case Key.O: OnOpen(null, null); e.Handled = true; return;
                case Key.D0: OnZoomFit(null, null); e.Handled = true; return;
                case Key.D1: OnZoom100(null, null); e.Handled = true; return;
            }
            return;
        }
        if (e.Key == Key.Delete && _selected >= 0 && _selected < _annots.Count)
        {
            Snapshot();
            _annots.RemoveAt(_selected);
            _selected = -1;
            Render(); UpdateUi();
            e.Handled = true;
            return;
        }
        EditorTool? t = e.Key switch
        {
            Key.V => EditorTool.Select, Key.A => EditorTool.Arrow, Key.L => EditorTool.Line, Key.R => EditorTool.Rect,
            Key.E => EditorTool.Ellipse, Key.T => EditorTool.Text, Key.C => EditorTool.Crop, _ => null
        };
        if (t is { } tool) { SelectToolButton(tool); e.Handled = true; }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        SetZoom(Zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    private void SetZoom(double z)
    {
        z = Math.Clamp(z, 0.05, 8);
        Zoom.ScaleX = Zoom.ScaleY = z;
        Render();
        UpdateUi();
    }

    private void ZoomToFit()
    {
        if (_base is null) return;
        UpdateLayout();
        double vw = Math.Max(100, Scroller.ActualWidth - 60), vh = Math.Max(100, Scroller.ActualHeight - 60);
        SetZoom(Math.Min(1, Math.Min(vw / _base.PixelWidth, vh / _base.PixelHeight)));
    }

    private void OnZoomFit(object? s, RoutedEventArgs? e) => ZoomToFit();
    private void OnZoom100(object? s, RoutedEventArgs? e) => SetZoom(1);

    private bool ConfirmDiscard()
    {
        if (!_dirty || _base is null) return true;
        var r = MessageBox.Show(this, "Save changes to this image?", App.AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return Save(_path);
        return true;
    }

    private void OnOpen(object? s, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        var f = Dialogs.PickImage();
        if (f is not null) OpenFile(f);
    }

    public void OpenFile(string f)
    {
        try { Load(ImageExportService.Load(f), f); }
        catch (Exception ex) { MessageBox.Show(this, "This image could not be opened: " + ex.Message, App.AppName, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnPaste(object? s, RoutedEventArgs? e)
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
            {
                if (!ConfirmDiscard()) return;
                Load(img, null);
                _dirty = true;
                UpdateUi();
            }
            else if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList() is { Count: > 0 } files)
            {
                if (ConfirmDiscard()) OpenFile(files[0]!);
            }
            else MessageBox.Show(this, "There is no image on the clipboard.", App.AppName);
        }
        catch (Exception ex) { MessageBox.Show(this, "The clipboard could not be read: " + ex.Message, App.AppName); }
    }

    private void OnDropFile(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && ConfirmDiscard()) OpenFile(files[0]);
    }

    private async void OnNewCapture(object? s, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        (PixelRect, BitmapSource)? r;
        using (await Workflows.HideOurWindowsAsync()) r = Workflows.SelectRegion(RegionSelectMode.Free);
        Activate();
        if (r is { } res) { Load(res.Item2, null); _dirty = true; UpdateUi(); }
    }

    private void OnCopy(object? s, RoutedEventArgs? e)
    {
        var img = Flatten();
        if (img is null) return;
        if (ImageExportService.CopyToClipboard(img)) StatusText.Text = "Copied to clipboard";
        else MessageBox.Show(this, "The clipboard is being used by another app. Try again.", App.AppName);
    }

    private void OnSave(object? s, RoutedEventArgs? e) => Save(_path);
    private void OnSaveAs(object? s, RoutedEventArgs? e) => Save(null);

    private bool Save(string? path)
    {
        var img = Flatten();
        if (img is null) return false;
        if (path is null)
        {
            bool adv = Entitlements.IsAllowed(Feature.AdvancedExport);
            var dlg = new SaveFileDialog
            {
                Filter = adv ? "PNG|*.png|JPG|*.jpg|BMP|*.bmp|TIFF|*.tiff|GIF|*.gif" : "PNG|*.png|JPG|*.jpg",
                FileName = ImageExportService.BuildFileName(SettingsService.Current.FileNamePattern, DateTime.Now),
                InitialDirectory = Directory.Exists(SettingsService.Current.ScreenshotFolder) ? SettingsService.Current.ScreenshotFolder : null
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        var fmt = ImageExportService.FormatFromPath(path);
        if (ImageExportService.IsProFormat(fmt) && !Entitlements.Require(Feature.AdvancedExport, this)) return false;
        try
        {
            int q = Entitlements.IsAllowed(Feature.AdvancedCompression) ? SettingsService.Current.JpgQuality : 90;
            ImageExportService.Save(img, path, fmt, q);
            bool isNew = !string.Equals(path, _path, StringComparison.OrdinalIgnoreCase) || HistoryService.Entries.All(h => !string.Equals(h.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (isNew) HistoryService.Add(path, img);
            _path = path;
            _dirty = false;
            UpdateUi();
            StatusText.Text = "Saved " + path;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Editor save failed", ex);
            MessageBox.Show(this, "The image could not be saved: " + ex.Message, App.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void Transform(Func<BitmapSource, BitmapSource> op)
    {
        var flat = Flatten();
        if (flat is null) return;
        Snapshot();
        SetBase(op(flat), true);
    }

    private void OnRotateRight(object s, RoutedEventArgs e) => Transform(b => ImageEffects.Rotate(b, 90));
    private void OnRotateLeft(object s, RoutedEventArgs e) => Transform(b => ImageEffects.Rotate(b, 270));
    private void OnFlip(object s, RoutedEventArgs e) => Transform(b => ImageEffects.Flip(b, true));

    private void OnResize(object s, RoutedEventArgs e)
    {
        if (_base is null) return;
        var size = ResizeDialog.Ask(this, _base.PixelWidth, _base.PixelHeight);
        if (size is { } sz) Transform(b => ImageEffects.Resize(b, sz.Width, sz.Height));
    }

    private async void OnOcr(object s, RoutedEventArgs e)
    {
        if (_base is null || !Entitlements.Require(Feature.Ocr, this)) return;
        var img = Flatten()!;
        StatusText.Text = "Reading text…";
        var (text, error) = await OcrService.RecognizeAsync(img);
        UpdateUi();
        if (error is not null) { MessageBox.Show(this, error, App.AppName, MessageBoxButton.OK, MessageBoxImage.Information); return; }
        new OcrResultWindow(text) { Owner = this }.Show();
        if (_path is not null && HistoryService.Entries.FirstOrDefault(h => string.Equals(h.FilePath, _path, StringComparison.OrdinalIgnoreCase)) is { } entry)
            HistoryService.UpdateOcr(entry, text);
    }

    private void OnTemplate(object s, RoutedEventArgs e)
    {
        if (_base is null || !Entitlements.Require(Feature.ScreenshotTemplates, this)) return;
        var t = TemplatePicker.Pick(this);
        if (t is null) return;
        SettingsService.Current.TemplateName = t.Name;
        SettingsService.Save();
        Transform(b => ImageEffects.ApplyTemplate(b, t));
        ZoomToFit();
    }

    private void OnWatermark(object s, RoutedEventArgs e)
    {
        if (_base is null || !Entitlements.Require(Feature.Watermark, this)) return;
        var text = InputDialog.Ask(this, "Watermark", "Text to stamp in the bottom-right corner:", SettingsService.Current.WatermarkText);
        if (string.IsNullOrWhiteSpace(text)) return;
        SettingsService.Current.WatermarkText = text;
        SettingsService.Save();
        Snapshot();
        _annots.RemoveAll(a => a is WatermarkAnnotation);
        _annots.Add(new WatermarkAnnotation(text, SettingsService.Current.WatermarkOpacity));
        Render(); UpdateUi();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CommitText();
        if (!ConfirmDiscard()) e.Cancel = true;
    }
}
