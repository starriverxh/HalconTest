using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionSystem.Engineering.Wpf.Models;

public enum RoiShapeKind
{
    Rectangle1,
    Rectangle2,
    Circle,
    Polygon
}

public enum RoiCoordinateSpace
{
    Image,
    ProductPose
}

public readonly record struct RoiVertex(double Row, double Column);

public sealed partial class RoiDefinition : ObservableObject
{
    public RoiDefinition(string name, RoiShapeKind shapeKind)
    {
        Name = name;
        ShapeKind = shapeKind;
    }

    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private RoiShapeKind _shapeKind;

    [ObservableProperty]
    private RoiCoordinateSpace _coordinateSpace = RoiCoordinateSpace.ProductPose;

    [ObservableProperty]
    private bool _followLocalization = true;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private double _row1;

    [ObservableProperty]
    private double _column1;

    [ObservableProperty]
    private double _row2;

    [ObservableProperty]
    private double _column2;

    [ObservableProperty]
    private double _centerRow;

    [ObservableProperty]
    private double _centerColumn;

    [ObservableProperty]
    private double _phiDeg;

    [ObservableProperty]
    private double _length1;

    [ObservableProperty]
    private double _length2;

    [ObservableProperty]
    private double _radius;

    public ObservableCollection<RoiVertex> Vertices { get; } = [];

    public bool IsValid => ShapeKind switch
    {
        RoiShapeKind.Rectangle1 => Row2 > Row1 && Column2 > Column1,
        RoiShapeKind.Rectangle2 => Length1 > 0 && Length2 > 0,
        RoiShapeKind.Circle => Radius > 0,
        RoiShapeKind.Polygon => Vertices.Count >= 3,
        _ => false
    };

    public RoiDefinition Clone(string name)
    {
        var clone = new RoiDefinition(name, ShapeKind)
        {
            CoordinateSpace = CoordinateSpace,
            FollowLocalization = FollowLocalization,
            IsEnabled = IsEnabled,
            Row1 = Row1,
            Column1 = Column1,
            Row2 = Row2,
            Column2 = Column2,
            CenterRow = CenterRow,
            CenterColumn = CenterColumn,
            PhiDeg = PhiDeg,
            Length1 = Length1,
            Length2 = Length2,
            Radius = Radius
        };
        foreach (var vertex in Vertices)
        {
            clone.Vertices.Add(vertex);
        }

        return clone;
    }
}
