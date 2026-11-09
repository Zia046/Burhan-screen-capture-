using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace BurhanScreenCapture.Editor;

/// <summary>Immutable annotation records; the document keeps a list and re-renders it. Undo = previous list.</summary>
public abstract record Annotation(Color Color, double Thickness)
{
    public abstract Rect Bounds { get; }
    public abstract Annotation Offset(Vector v);
    public abstract UIElement Build(BitmapSource baseImage);

    protected Brush Stroke => Freeze(new SolidColorBrush(Color));
    protected static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

public sealed record ArrowAnnotation(Point A, Point B, Color Color, double Thickness) : Annotation(Color, Thickness)
{
    public override Rect Bounds => new(A, B);
    public override Annotation Offset(Vector v) => this with { A = A + v, B = B + v };
    public override UIElement Build(BitmapSource _)
    {
        var dir = B - A;
        double len = dir.Length;
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        if (len < 1) return new Path();
        dir.Normalize();
        double head = Math.Max(10, Thickness * 4);
        var baseP = B - dir * head;
        var normal = new Vector(-dir.Y, dir.X) * head * 0.55;
        g.Children.Add(new LineGeometry(A, baseP));
        var tri = new PathGeometry(new[] { new PathFigure(B, new PathSegment[] { new LineSegment(baseP + normal, true), new LineSegment(baseP - normal, true) }, true) });
        g.Children.Add(tri);
        return new Path { Data = g, Stroke = Stroke, Fill = Stroke, StrokeThickness = Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
    }
}

public sealed record LineAnnotation(Point A, Point B, Color Color, double Thickness) : Annotation(Color, Thickness)
{
    public override Rect Bounds => new(A, B);
    public override Annotation Offset(Vector v) => this with { A = A + v, B = B + v };
    public override UIElement Build(BitmapSource _) =>
        new Line { X1 = A.X, Y1 = A.Y, X2 = B.X, Y2 = B.Y, Stroke = Stroke, StrokeThickness = Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
}

public sealed record RectAnnotation(Rect R, Color Color, double Thickness, bool Filled) : Annotation(Color, Thickness)
{
    public override Rect Bounds => R;
    public override Annotation Offset(Vector v) => this with { R = Rect.Offset(R, v) };
    public override UIElement Build(BitmapSource _)
    {
        var r = new Rectangle { Width = R.Width, Height = R.Height, Stroke = Stroke, StrokeThickness = Thickness, RadiusX = 2, RadiusY = 2 };
        if (Filled) r.Fill = Stroke;
        Canvas.SetLeft(r, R.X); Canvas.SetTop(r, R.Y);
        return r;
    }
}

public sealed record EllipseAnnotation(Rect R, Color Color, double Thickness) : Annotation(Color, Thickness)
{
    public override Rect Bounds => R;
    public override Annotation Offset(Vector v) => this with { R = Rect.Offset(R, v) };
    public override UIElement Build(BitmapSource _)
    {
        var e = new Ellipse { Width = R.Width, Height = R.Height, Stroke = Stroke, StrokeThickness = Thickness };
        Canvas.SetLeft(e, R.X); Canvas.SetTop(e, R.Y);
        return e;
    }
}

public sealed record HighlightAnnotation(Rect R, Color Color) : Annotation(Color, 0)
{
    public override Rect Bounds => R;
    public override Annotation Offset(Vector v) => this with { R = Rect.Offset(R, v) };
    public override UIElement Build(BitmapSource _)
    {
        var r = new Rectangle { Width = R.Width, Height = R.Height, Fill = Freeze(new SolidColorBrush(Color.FromArgb(100, Color.R, Color.G, Color.B))) };
        Canvas.SetLeft(r, R.X); Canvas.SetTop(r, R.Y);
        return r;
    }
}

public sealed record TextAnnotation(Point P, string Text, Color Color, double FontSize, bool Background) : Annotation(Color, 0)
{
    public override Rect Bounds
    {
        get
        {
            var ft = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), FontSize, Brushes.Black, 1.0);
            return new Rect(P, new Size(ft.WidthIncludingTrailingWhitespace + 12, ft.Height + 8));
        }
    }
    public override Annotation Offset(Vector v) => this with { P = P + v };
    public override UIElement Build(BitmapSource _)
    {
        var tb = new TextBlock { Text = Text, FontSize = FontSize, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold, Foreground = Stroke };
        var b = new Border { Child = tb, Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(4) };
        if (Background)
        {
            bool dark = Color.R * 0.299 + Color.G * 0.587 + Color.B * 0.114 < 140;
            b.Background = new SolidColorBrush(dark ? Color.FromArgb(230, 255, 255, 255) : Color.FromArgb(220, 20, 20, 20));
        }
        Canvas.SetLeft(b, P.X); Canvas.SetTop(b, P.Y);
        return b;
    }
}

public sealed record StepAnnotation(Point Center, int Number, Color Color, double Size) : Annotation(Color, 0)
{
    public override Rect Bounds => new(Center.X - Size / 2, Center.Y - Size / 2, Size, Size);
    public override Annotation Offset(Vector v) => this with { Center = Center + v };
    public override UIElement Build(BitmapSource _)
    {
        var g = new Grid { Width = Size, Height = Size };
        g.Children.Add(new Ellipse { Fill = Stroke, Stroke = Brushes.White, StrokeThickness = Math.Max(2, Size / 14) });
        g.Children.Add(new TextBlock { Text = Number.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = Size * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Canvas.SetLeft(g, Center.X - Size / 2); Canvas.SetTop(g, Center.Y - Size / 2);
        return g;
    }
}

/// <summary>Pro: circular lens showing <see cref="Source"/> enlarged at <see cref="Lens"/>.</summary>
public sealed record MagnifierAnnotation(Rect Source, Rect Lens, Color Color) : Annotation(Color, 3)
{
    public override Rect Bounds => Lens;
    public override Annotation Offset(Vector v) => this with { Lens = Rect.Offset(Lens, v) };
    public override UIElement Build(BitmapSource baseImage)
    {
        var brush = new ImageBrush(baseImage) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = Source, Stretch = Stretch.Fill };
        var g = new Canvas();
        var connector = new Line
        {
            X1 = Source.X + Source.Width / 2, Y1 = Source.Y + Source.Height / 2,
            X2 = Lens.X + Lens.Width / 2, Y2 = Lens.Y + Lens.Height / 2,
            Stroke = Stroke, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 }
        };
        var src = new Ellipse { Width = Source.Width, Height = Source.Height, Stroke = Stroke, StrokeThickness = 2 };
        Canvas.SetLeft(src, Source.X); Canvas.SetTop(src, Source.Y);
        var lens = new Ellipse { Width = Lens.Width, Height = Lens.Height, Fill = brush, Stroke = Stroke, StrokeThickness = Thickness };
        Canvas.SetLeft(lens, Lens.X); Canvas.SetTop(lens, Lens.Y);
        g.Children.Add(connector); g.Children.Add(src); g.Children.Add(lens);
        return g;
    }
}

/// <summary>Pro: dims everything outside the rectangle.</summary>
public sealed record SpotlightAnnotation(Rect R, double Opacity) : Annotation(Colors.Black, 0)
{
    public override Rect Bounds => R;
    public override Annotation Offset(Vector v) => this with { R = Rect.Offset(R, v) };
    public override UIElement Build(BitmapSource baseImage)
    {
        var full = new RectangleGeometry(new Rect(0, 0, baseImage.PixelWidth, baseImage.PixelHeight));
        var hole = new RectangleGeometry(R, 8, 8);
        return new Path
        {
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole),
            Fill = Freeze(new SolidColorBrush(Color.FromArgb((byte)(255 * Opacity), 0, 0, 0))),
            IsHitTestVisible = false
        };
    }
}

/// <summary>Pro: brand watermark anchored to the bottom-right corner.</summary>
public sealed record WatermarkAnnotation(string Text, double Opacity) : Annotation(Colors.White, 0)
{
    public override Rect Bounds => Rect.Empty;
    public override Annotation Offset(Vector v) => this;
    public override UIElement Build(BitmapSource baseImage)
    {
        double size = Math.Clamp(baseImage.PixelWidth / 40.0, 12, 48);
        var tb = new TextBlock
        {
            Text = Text, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Opacity = Opacity,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8 }
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, baseImage.PixelWidth - tb.DesiredSize.Width - size * 0.8);
        Canvas.SetTop(tb, baseImage.PixelHeight - tb.DesiredSize.Height - size * 0.6);
        tb.IsHitTestVisible = false;
        return tb;
    }
}
