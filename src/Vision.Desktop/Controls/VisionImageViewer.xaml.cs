using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Vision.Desktop.Models;

namespace Vision.Desktop.Controls;

public partial class VisionImageViewer : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(VisionImageViewer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualPropertyChanged));

    public static readonly DependencyProperty RoiProperty = DependencyProperty.Register(
        nameof(Roi), typeof(RectangleRoiData), typeof(VisionImageViewer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualPropertyChanged));

    public static readonly DependencyProperty OverlaysProperty = DependencyProperty.Register(
        nameof(Overlays), typeof(IEnumerable), typeof(VisionImageViewer),
        new FrameworkPropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsRoiDrawingEnabledProperty = DependencyProperty.Register(
        nameof(IsRoiDrawingEnabled), typeof(bool), typeof(VisionImageViewer), new PropertyMetadata(false));

    public static readonly DependencyProperty SelectedPointProperty = DependencyProperty.Register(
        nameof(SelectedPoint), typeof(ImageCoordinate), typeof(VisionImageViewer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualPropertyChanged));

    public static readonly DependencyProperty CursorTextProperty = DependencyProperty.Register(
        nameof(CursorText), typeof(string), typeof(VisionImageViewer), new PropertyMetadata("row: --  column: --"));

    private ImageCoordinate? _dragStart;

    public VisionImageViewer()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public RectangleRoiData? Roi
    {
        get => (RectangleRoiData?)GetValue(RoiProperty);
        set => SetValue(RoiProperty, value);
    }

    public IEnumerable? Overlays
    {
        get => (IEnumerable?)GetValue(OverlaysProperty);
        set => SetValue(OverlaysProperty, value);
    }

    public bool IsRoiDrawingEnabled
    {
        get => (bool)GetValue(IsRoiDrawingEnabledProperty);
        set => SetValue(IsRoiDrawingEnabledProperty, value);
    }

    public ImageCoordinate? SelectedPoint
    {
        get => (ImageCoordinate?)GetValue(SelectedPointProperty);
        set => SetValue(SelectedPointProperty, value);
    }

    public string CursorText
    {
        get => (string)GetValue(CursorTextProperty);
        set => SetValue(CursorTextProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is VisionImageViewer viewer && viewer.IsLoaded)
        {
            viewer.Redraw();
        }
    }

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs eventArgs) => Redraw();

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        Focus();
        if (!TryDisplayToImage(eventArgs.GetPosition(ViewerRoot), out var point))
        {
            return;
        }

        if (IsRoiDrawingEnabled)
        {
            _dragStart = point;
            Roi = new RectangleRoiData(point.Row, point.Column, point.Row, point.Column);
            ViewerRoot.CaptureMouse();
        }
        else
        {
            SelectedPoint = point;
        }

        Redraw();
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (!TryDisplayToImage(eventArgs.GetPosition(ViewerRoot), out var point))
        {
            CursorText = "row: --  column: --";
            return;
        }

        CursorText = $"row: {point.Row:F1}  column: {point.Column:F1}";
        if (_dragStart is not null && eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            Roi = new RectangleRoiData(_dragStart.Row, _dragStart.Column, point.Row, point.Column).Normalize();
            Redraw();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_dragStart is not null && TryDisplayToImage(eventArgs.GetPosition(ViewerRoot), out var point))
        {
            Roi = new RectangleRoiData(_dragStart.Row, _dragStart.Column, point.Row, point.Column).Normalize();
        }

        _dragStart = null;
        ViewerRoot.ReleaseMouseCapture();
        Redraw();
    }

    private void OnMouseLeave(object sender, MouseEventArgs eventArgs)
    {
        if (_dragStart is null)
        {
            CursorText = "row: --  column: --";
        }
    }

    private void Redraw()
    {
        if (OverlayCanvas is null)
        {
            return;
        }

        OverlayCanvas.Children.Clear();
        if (!TryGetTransform(out var transform))
        {
            return;
        }

        if (Roi is not null)
        {
            var roi = Roi.Normalize();
            var topLeft = transform.ToDisplay(roi.Row1, roi.Column1);
            var bottomRight = transform.ToDisplay(roi.Row2, roi.Column2);
            var rectangle = new Rectangle
            {
                Width = Math.Max(1, bottomRight.X - topLeft.X),
                Height = Math.Max(1, bottomRight.Y - topLeft.Y),
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(28, 56, 189, 248))
            };
            Canvas.SetLeft(rectangle, topLeft.X);
            Canvas.SetTop(rectangle, topLeft.Y);
            OverlayCanvas.Children.Add(rectangle);
            AddHandle(topLeft);
            AddHandle(new Point(bottomRight.X, topLeft.Y));
            AddHandle(new Point(topLeft.X, bottomRight.Y));
            AddHandle(bottomRight);
        }

        if (Overlays is not null)
        {
            foreach (var item in Overlays.OfType<VisionOverlay>())
            {
                DrawOverlay(item, transform);
            }
        }

        if (SelectedPoint is not null)
        {
            DrawOverlay(new VisionOverlay(
                SelectedPoint.Row, SelectedPoint.Column, 8, "示教点", "#F59E0B", "cross"), transform);
        }
    }

    private void DrawOverlay(VisionOverlay overlay, DisplayTransform transform)
    {
        var center = transform.ToDisplay(overlay.Row, overlay.Column);
        var brush = (Brush)new BrushConverter().ConvertFromString(overlay.Color)!;
        var radius = Math.Max(4, overlay.Radius * transform.Scale);
        if (overlay.Kind.Equals("cross", StringComparison.OrdinalIgnoreCase))
        {
            AddLine(center.X - radius, center.Y, center.X + radius, center.Y, brush);
            AddLine(center.X, center.Y - radius, center.X, center.Y + radius, brush);
        }
        else
        {
            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = brush,
                StrokeThickness = 2
            };
            Canvas.SetLeft(ellipse, center.X - radius);
            Canvas.SetTop(ellipse, center.Y - radius);
            OverlayCanvas.Children.Add(ellipse);
        }

        if (!string.IsNullOrWhiteSpace(overlay.Label))
        {
            var label = new TextBlock
            {
                Text = overlay.Label,
                Foreground = brush,
                Background = new SolidColorBrush(Color.FromArgb(190, 5, 10, 18)),
                Padding = new Thickness(3, 1, 3, 1),
                FontSize = 12
            };
            Canvas.SetLeft(label, center.X + radius + 3);
            Canvas.SetTop(label, center.Y - radius);
            OverlayCanvas.Children.Add(label);
        }
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush) =>
        OverlayCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 2
        });

    private void AddHandle(Point point)
    {
        var handle = new Rectangle
        {
            Width = 7,
            Height = 7,
            Fill = Brushes.DeepSkyBlue,
            Stroke = Brushes.White,
            StrokeThickness = 1
        };
        Canvas.SetLeft(handle, point.X - 3.5);
        Canvas.SetTop(handle, point.Y - 3.5);
        OverlayCanvas.Children.Add(handle);
    }

    private bool TryDisplayToImage(Point displayPoint, out ImageCoordinate imagePoint)
    {
        imagePoint = new ImageCoordinate(0, 0);
        if (!TryGetTransform(out var transform) ||
            displayPoint.X < transform.OffsetX || displayPoint.Y < transform.OffsetY ||
            displayPoint.X > transform.OffsetX + transform.DisplayWidth ||
            displayPoint.Y > transform.OffsetY + transform.DisplayHeight)
        {
            return false;
        }

        imagePoint = new ImageCoordinate(
            Math.Clamp((displayPoint.Y - transform.OffsetY) / transform.Scale, 0, transform.PixelHeight - 1),
            Math.Clamp((displayPoint.X - transform.OffsetX) / transform.Scale, 0, transform.PixelWidth - 1));
        return true;
    }

    private bool TryGetTransform(out DisplayTransform transform)
    {
        transform = default;
        if (Source is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0 ||
            ViewerRoot.ActualWidth <= 0 || ViewerRoot.ActualHeight <= 0)
        {
            return false;
        }

        var scale = Math.Min(ViewerRoot.ActualWidth / bitmap.PixelWidth, ViewerRoot.ActualHeight / bitmap.PixelHeight);
        var displayWidth = bitmap.PixelWidth * scale;
        var displayHeight = bitmap.PixelHeight * scale;
        transform = new DisplayTransform(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            scale,
            (ViewerRoot.ActualWidth - displayWidth) / 2.0,
            (ViewerRoot.ActualHeight - displayHeight) / 2.0,
            displayWidth,
            displayHeight);
        return true;
    }

    private readonly record struct DisplayTransform(
        int PixelWidth,
        int PixelHeight,
        double Scale,
        double OffsetX,
        double OffsetY,
        double DisplayWidth,
        double DisplayHeight)
    {
        public Point ToDisplay(double row, double column) =>
            new(OffsetX + column * Scale, OffsetY + row * Scale);
    }
}
