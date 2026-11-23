using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

internal sealed class PreviewViewport : Grid
{
    private static readonly Vector3D WorldUp = new Vector3D(0, 0, 1);
    private readonly PerspectiveCamera camera = new PerspectiveCamera { FieldOfView = 45 };
    private readonly GeometryModel3D model = new GeometryModel3D();
    private readonly TextBlock message = new TextBlock
    {
        Foreground = new SolidColorBrush(Color.FromRgb(200, 212, 228)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(24),
        FontSize = 14,
        IsHitTestVisible = false
    };
    private Point3D center;
    private Point lastPointer;
    private double radius;
    private double distance;
    private double azimuth;
    private double elevation;

    internal PreviewViewport()
    {
        Background = new SolidColorBrush(Color.FromRgb(21, 32, 49));
        ClipToBounds = true;
        Focusable = true;
        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(98, 196, 188)));
        material.Freeze();
        model.Material = material;
        model.BackMaterial = material;

        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(82, 92, 109)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(195, 195, 195), new Vector3D(-1, 1, -2)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(100, 105, 115), new Vector3D(1, -1, 0.5)));
        scene.Children.Add(model);
        var viewport = new Viewport3D { Camera = camera, IsHitTestVisible = false };
        viewport.Children.Add(new ModelVisual3D { Content = scene });
        Children.Add(viewport);
        Children.Add(message);
        ShowMessage("Select an exported model or open an STL file.");

        MouseDown += BeginDrag;
        MouseMove += Drag;
        MouseUp += delegate { ReleaseMouseCapture(); };
        MouseWheel += Zoom;
        Loaded += delegate { Fit(); };
    }

    internal void ShowMessage(string text)
    {
        model.Geometry = null;
        message.Text = text;
        message.Visibility = Visibility.Visible;
    }

    internal void ShowMesh(MeshGeometry3D mesh)
    {
        model.Geometry = mesh;
        message.Visibility = Visibility.Collapsed;
        Fit();
    }

    internal void Fit()
    {
        if (model.Geometry == null)
        {
            return;
        }
        Rect3D bounds = model.Geometry.Bounds;
        var size = new Vector3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ);
        center = bounds.Location + size * 0.5;
        radius = size.Length > 0 ? size.Length * 0.5 : 1;
        double horizontalHalfAngle = camera.FieldOfView * Math.PI / 360;
        double verticalHalfAngle = Math.Atan(Math.Tan(horizontalHalfAngle) * Math.Max(1, ActualHeight) /
            Math.Max(1, ActualWidth));
        distance = radius * 1.1 / Math.Sin(Math.Min(horizontalHalfAngle, verticalHalfAngle));
        azimuth = -Math.PI / 3;
        elevation = Math.PI / 6;
        UpdateCamera();
    }

    private void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        if (model.Geometry == null ||
            (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right))
        {
            return;
        }
        Focus();
        lastPointer = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void Drag(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured || model.Geometry == null)
        {
            return;
        }
        Point pointer = e.GetPosition(this);
        Vector delta = pointer - lastPointer;
        lastPointer = pointer;
        if (e.RightButton == MouseButtonState.Pressed)
        {
            var forward = camera.LookDirection;
            forward.Normalize();
            Vector3D right = Vector3D.CrossProduct(forward, WorldUp);
            right.Normalize();
            Vector3D up = Vector3D.CrossProduct(right, forward);
            double unitsPerPixel = 2 * distance * Math.Tan(camera.FieldOfView * Math.PI / 360) /
                Math.Max(1, ActualWidth);
            center += (up * delta.Y - right * delta.X) * unitsPerPixel;
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            azimuth = (azimuth - delta.X * 0.01) % (2 * Math.PI);
            double limit = Math.PI * 0.49;
            elevation = Math.Max(-limit, Math.Min(limit, elevation + delta.Y * 0.01));
        }
        UpdateCamera();
    }

    private void Zoom(object sender, MouseWheelEventArgs e)
    {
        if (model.Geometry == null)
        {
            return;
        }
        distance = Math.Max(radius * 0.05, Math.Min(radius * 500, distance * Math.Pow(0.86, e.Delta / 120.0)));
        UpdateCamera();
        e.Handled = true;
    }

    private void UpdateCamera()
    {
        double horizontal = distance * Math.Cos(elevation);
        var offset = new Vector3D(horizontal * Math.Cos(azimuth), horizontal * Math.Sin(azimuth),
            distance * Math.Sin(elevation));
        camera.Position = center + offset;
        camera.LookDirection = -offset;
        camera.UpDirection = WorldUp;
        camera.NearPlaneDistance = Math.Max(radius * 0.001, distance - radius * 2);
        camera.FarPlaneDistance = distance + radius * 4;
    }
}
