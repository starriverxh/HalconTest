using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VisionSystem.Engineering.Wpf.Models;

namespace VisionSystem.Engineering.Wpf.Controls;

public sealed class RoiCanvas : FrameworkElement
{
    private const double ImageWidth = 1000;
    private const double ImageHeight = 600;
    private RoiVertex? _dragStart;
    private RoiVertex? _dragCurrent;
    private readonly List<RoiVertex> _polygonDraft = [];

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(RoiCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedRoiProperty = DependencyProperty.Register(
        nameof(SelectedRoi),
        typeof(RoiDefinition),
        typeof(RoiCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ActiveToolProperty = DependencyProperty.Register(
        nameof(ActiveTool),
        typeof(RoiShapeKind),
        typeof(RoiCanvas),
        new FrameworkPropertyMetadata(RoiShapeKind.Rectangle1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RoiCreatedCommandProperty = DependencyProperty.Register(
        nameof(RoiCreatedCommand),
        typeof(ICommand),
        typeof(RoiCanvas));

    public static readonly DependencyProperty IsDrawingEnabledProperty = DependencyProperty.Register(
        nameof(IsDrawingEnabled),
        typeof(bool),
        typeof(RoiCanvas),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public RoiDefinition? SelectedRoi
    {
        get => (RoiDefinition?)GetValue(SelectedRoiProperty);
        set => SetValue(SelectedRoiProperty, value);
    }

    public RoiShapeKind ActiveTool
    {
        get => (RoiShapeKind)GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value);
    }

    public ICommand? RoiCreatedCommand
    {
        get => (ICommand?)GetValue(RoiCreatedCommandProperty);
        set => SetValue(RoiCreatedCommandProperty, value);
    }

    public bool IsDrawingEnabled
    {
        get => (bool)GetValue(IsDrawingEnabledProperty);
        set => SetValue(IsDrawingEnabledProperty, value);
    }

    public RoiCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        MouseRightButtonDown += (_, _) => FinishPolygon();
    }

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var canvas = (RoiCanvas)dependencyObject;
        canvas.Unsubscribe(eventArgs.OldValue as IEnumerable);
        canvas.Subscribe(eventArgs.NewValue as IEnumerable);
        canvas.InvalidateVisual();
    }

    private void Subscribe(IEnumerable? source)
    {
        if (source is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnCollectionChanged;
        }
        if (source is not null)
        {
            foreach (var roi in source.OfType<RoiDefinition>()) Subscribe(roi);
        }
    }

    private void Unsubscribe(IEnumerable? source)
    {
        if (source is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged -= OnCollectionChanged;
        }
        if (source is not null)
        {
            foreach (var roi in source.OfType<RoiDefinition>()) Unsubscribe(roi);
        }
    }

    private void Subscribe(RoiDefinition roi)
    {
        roi.PropertyChanged += OnRoiPropertyChanged;
        roi.Vertices.CollectionChanged += OnVerticesChanged;
    }

    private void Unsubscribe(RoiDefinition roi)
    {
        roi.PropertyChanged -= OnRoiPropertyChanged;
        roi.Vertices.CollectionChanged -= OnVerticesChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.OldItems is not null)
        {
            foreach (var roi in eventArgs.OldItems.OfType<RoiDefinition>()) Unsubscribe(roi);
        }
        if (eventArgs.NewItems is not null)
        {
            foreach (var roi in eventArgs.NewItems.OfType<RoiDefinition>()) Subscribe(roi);
        }
        InvalidateVisual();
    }

    private void OnRoiPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs) => InvalidateVisual();
    private void OnVerticesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var transform = GetDisplayTransform();
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(4, 10, 18)), null, new Rect(RenderSize));
        DrawSyntheticProduct(drawingContext, transform);

        if (ItemsSource is not null)
        {
            foreach (var roi in ItemsSource.OfType<RoiDefinition>().Where(item => item.IsEnabled))
            {
                DrawRoi(drawingContext, transform, roi, ReferenceEquals(roi, SelectedRoi));
            }
        }

        DrawDraft(drawingContext, transform);
        var text = IsDrawingEnabled
            ? $"工具 {ActiveTool} · 图像坐标 Row/Column · 多边形右键结束"
            : "只读叠加 · 工程离线";
        drawingContext.DrawText(
            new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 12,
                new SolidColorBrush(Color.FromRgb(148, 170, 192)), VisualTreeHelper.GetDpi(this).PixelsPerDip),
            new Point(transform.OffsetX + 12, transform.OffsetY + transform.Height - 28));
    }

    private void DrawSyntheticProduct(DrawingContext context, DisplayTransform transform)
    {
        var imageRect = transform.ToDisplay(new Rect(0, 0, ImageWidth, ImageHeight));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(19, 48, 50)), new Pen(new SolidColorBrush(Color.FromRgb(38, 82, 88)), 1), imageRect);
        for (var column = 0d; column <= ImageWidth; column += 100)
        {
            var start = transform.ToDisplay(new RoiVertex(0, column));
            var end = transform.ToDisplay(new RoiVertex(ImageHeight, column));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(40, 125, 211, 252)), 1), start, end);
        }
        for (var row = 0d; row <= ImageHeight; row += 100)
        {
            var start = transform.ToDisplay(new RoiVertex(row, 0));
            var end = transform.ToDisplay(new RoiVertex(row, ImageWidth));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(40, 125, 211, 252)), 1), start, end);
        }

        var board = transform.ToDisplay(new Rect(90, 90, 820, 420));
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(31, 92, 63)), new Pen(new SolidColorBrush(Color.FromRgb(74, 222, 128)), 2), board, 10, 10);
        foreach (var column in new[] { 220d, 360d, 500d, 640d, 780d })
        {
            var center = transform.ToDisplay(new RoiVertex(300, column));
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(7, 16, 28)), new Pen(Brushes.Gold, 2), center, 19 * transform.Scale, 19 * transform.Scale);
        }
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(251, 146, 60)), 3), transform.ToDisplay(new RoiVertex(245, 155)), transform.ToDisplay(new RoiVertex(245, 845)));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(251, 146, 60)), 3), transform.ToDisplay(new RoiVertex(355, 155)), transform.ToDisplay(new RoiVertex(355, 845)));
    }

    private static void DrawRoi(DrawingContext context, DisplayTransform transform, RoiDefinition roi, bool selected)
    {
        var pen = new Pen(new SolidColorBrush(selected ? Color.FromRgb(45, 212, 191) : Color.FromRgb(56, 189, 248)), selected ? 3 : 2);
        var fill = new SolidColorBrush(selected ? Color.FromArgb(36, 45, 212, 191) : Color.FromArgb(24, 56, 189, 248));
        switch (roi.ShapeKind)
        {
            case RoiShapeKind.Rectangle1:
                context.DrawRectangle(fill, pen, transform.ToDisplay(new Rect(roi.Column1, roi.Row1, roi.Column2 - roi.Column1, roi.Row2 - roi.Row1)));
                break;
            case RoiShapeKind.Rectangle2:
                var center = transform.ToDisplay(new RoiVertex(roi.CenterRow, roi.CenterColumn));
                context.PushTransform(new RotateTransform(-roi.PhiDeg, center.X, center.Y));
                context.DrawRectangle(fill, pen, new Rect(center.X - roi.Length1 * transform.Scale, center.Y - roi.Length2 * transform.Scale, roi.Length1 * 2 * transform.Scale, roi.Length2 * 2 * transform.Scale));
                context.Pop();
                break;
            case RoiShapeKind.Circle:
                var circleCenter = transform.ToDisplay(new RoiVertex(roi.CenterRow, roi.CenterColumn));
                context.DrawEllipse(fill, pen, circleCenter, roi.Radius * transform.Scale, roi.Radius * transform.Scale);
                break;
            case RoiShapeKind.Polygon:
                if (roi.Vertices.Count >= 2)
                {
                    var geometry = CreatePolygon(transform, roi.Vertices);
                    context.DrawGeometry(fill, pen, geometry);
                }
                break;
        }

        var labelPoint = roi.ShapeKind switch
        {
            RoiShapeKind.Rectangle1 => transform.ToDisplay(new RoiVertex(roi.Row1, roi.Column1)),
            _ => transform.ToDisplay(new RoiVertex(roi.CenterRow, roi.CenterColumn))
        };
        context.DrawText(new FormattedText(
                $"{roi.Name} · {roi.CoordinateSpace}{(roi.FollowLocalization ? " · 跟随" : string.Empty)}",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), 12, pen.Brush, 1),
            new Point(labelPoint.X + 5, labelPoint.Y + 5));
    }

    private void DrawDraft(DrawingContext context, DisplayTransform transform)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(251, 191, 36)), 2) { DashStyle = DashStyles.Dash };
        if (_dragStart is { } start && _dragCurrent is { } current)
        {
            if (ActiveTool == RoiShapeKind.Circle)
            {
                var center = transform.ToDisplay(start);
                var radius = Distance(start, current) * transform.Scale;
                context.DrawEllipse(null, pen, center, radius, radius);
            }
            else
            {
                var rect = NormalizedRect(start, current);
                context.DrawRectangle(null, pen, transform.ToDisplay(rect));
            }
        }
        if (_polygonDraft.Count >= 2)
        {
            context.DrawGeometry(null, pen, CreateOpenPolygon(transform, _polygonDraft));
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!IsDrawingEnabled || !TryToImage(eventArgs.GetPosition(this), out var point))
        {
            return;
        }

        Focus();
        if (ActiveTool == RoiShapeKind.Polygon)
        {
            if (eventArgs.ClickCount >= 2)
            {
                FinishPolygon();
            }
            else
            {
                _polygonDraft.Add(point);
                InvalidateVisual();
            }
            return;
        }

        _dragStart = point;
        _dragCurrent = point;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_dragStart is not null && eventArgs.LeftButton == MouseButtonState.Pressed && TryToImage(eventArgs.GetPosition(this), out var point))
        {
            _dragCurrent = point;
            InvalidateVisual();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_dragStart is not { } start || !TryToImage(eventArgs.GetPosition(this), out var end))
        {
            ReleaseMouseCapture();
            return;
        }

        var roi = CreateDraggedRoi(start, end);
        _dragStart = null;
        _dragCurrent = null;
        ReleaseMouseCapture();
        Submit(roi);
    }

    private RoiDefinition CreateDraggedRoi(RoiVertex start, RoiVertex end)
    {
        var index = ItemsSource?.Cast<object>().Count() + 1 ?? 1;
        var rect = NormalizedRect(start, end);
        return ActiveTool switch
        {
            RoiShapeKind.Rectangle1 => new RoiDefinition($"ROI_{index:00}_搜索区", ActiveTool)
            {
                Row1 = rect.Y,
                Column1 = rect.X,
                Row2 = rect.Bottom,
                Column2 = rect.Right
            },
            RoiShapeKind.Rectangle2 => new RoiDefinition($"ROI_{index:00}_旋转区", ActiveTool)
            {
                CenterRow = (start.Row + end.Row) / 2,
                CenterColumn = (start.Column + end.Column) / 2,
                Length1 = Math.Abs(end.Column - start.Column) / 2,
                Length2 = Math.Abs(end.Row - start.Row) / 2,
                PhiDeg = 0
            },
            RoiShapeKind.Circle => new RoiDefinition($"ROI_{index:00}_圆区", ActiveTool)
            {
                CenterRow = start.Row,
                CenterColumn = start.Column,
                Radius = Distance(start, end)
            },
            _ => throw new InvalidOperationException("Polygon is created point by point.")
        };
    }

    private void FinishPolygon()
    {
        if (_polygonDraft.Count < 3)
        {
            _polygonDraft.Clear();
            InvalidateVisual();
            return;
        }

        var index = ItemsSource?.Cast<object>().Count() + 1 ?? 1;
        var roi = new RoiDefinition($"ROI_{index:00}_多边形", RoiShapeKind.Polygon)
        {
            CenterRow = _polygonDraft.Average(point => point.Row),
            CenterColumn = _polygonDraft.Average(point => point.Column)
        };
        foreach (var vertex in _polygonDraft)
        {
            roi.Vertices.Add(vertex);
        }
        _polygonDraft.Clear();
        Submit(roi);
    }

    private void Submit(RoiDefinition roi)
    {
        if (roi.IsValid && RoiCreatedCommand?.CanExecute(roi) == true)
        {
            RoiCreatedCommand.Execute(roi);
            SelectedRoi = roi;
        }
        InvalidateVisual();
    }

    private bool TryToImage(Point display, out RoiVertex image)
    {
        var transform = GetDisplayTransform();
        var column = (display.X - transform.OffsetX) / transform.Scale;
        var row = (display.Y - transform.OffsetY) / transform.Scale;
        if (column < 0 || row < 0 || column > ImageWidth || row > ImageHeight)
        {
            image = default;
            return false;
        }
        image = new RoiVertex(Math.Clamp(row, 0, ImageHeight), Math.Clamp(column, 0, ImageWidth));
        return true;
    }

    private DisplayTransform GetDisplayTransform()
    {
        var scale = Math.Max(0.01, Math.Min(ActualWidth / ImageWidth, ActualHeight / ImageHeight));
        var width = ImageWidth * scale;
        var height = ImageHeight * scale;
        return new DisplayTransform(scale, (ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
    }

    private static Rect NormalizedRect(RoiVertex first, RoiVertex second) => new(
        Math.Min(first.Column, second.Column), Math.Min(first.Row, second.Row),
        Math.Abs(first.Column - second.Column), Math.Abs(first.Row - second.Row));

    private static double Distance(RoiVertex first, RoiVertex second) =>
        Math.Sqrt(Math.Pow(first.Row - second.Row, 2) + Math.Pow(first.Column - second.Column, 2));

    private static StreamGeometry CreatePolygon(DisplayTransform transform, IEnumerable<RoiVertex> vertices)
    {
        var points = vertices.Select(transform.ToDisplay).ToArray();
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(points[0], true, true);
        context.PolyLineTo(points.Skip(1).ToArray(), true, true);
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateOpenPolygon(DisplayTransform transform, IEnumerable<RoiVertex> vertices)
    {
        var points = vertices.Select(transform.ToDisplay).ToArray();
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(points[0], false, false);
        context.PolyLineTo(points.Skip(1).ToArray(), true, false);
        geometry.Freeze();
        return geometry;
    }

    private readonly record struct DisplayTransform(double Scale, double OffsetX, double OffsetY, double Width, double Height)
    {
        public Point ToDisplay(RoiVertex point) => new(OffsetX + point.Column * Scale, OffsetY + point.Row * Scale);
        public Rect ToDisplay(Rect rect) => new(OffsetX + rect.X * Scale, OffsetY + rect.Y * Scale, rect.Width * Scale, rect.Height * Scale);
    }
}
