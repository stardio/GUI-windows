using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace EtherCAT_Studio
{
    public partial class SimulationWindow : Window
    {
        public class SimulationStep
        {
            public string Id { get; set; } = "";
            public string Type { get; set; } = "";
            public string Json { get; set; } = "{}";
            public string Label => $"{Id}  {Type}";
        }

        private readonly DispatcherTimer _timer;
        private List<Point3D> _points = new();
        private List<int> _pointStepIndex = new();
        private List<SimulationStep> _steps = new();
        private int _currentStepIndex = -1;
        private double _currentSpeedFactor = 1.0;
        private int _segmentIndex;
        private double _segmentT;
        private readonly Model3DGroup _scene;
        private readonly GeometryModel3D _pathModel;
        private readonly GeometryModel3D _trailModel;
        private readonly Model3DGroup _keyPointGroup;
        private readonly GeometryModel3D _cursorModel;
        private readonly TranslateTransform3D _cursorTransform;
        private readonly List<Point3D> _trailPoints = new();
        private PerspectiveCamera _camera;
        private double _yaw = 45;
        private double _pitch = 35;
        private double _distance = 1200;
        private Point3D _target = new Point3D(0, 0, 0);
        private bool _isDragging;
        private Point _lastMouse;

        public SimulationWindow()
        {
            InitializeComponent();

            _camera = MainCamera;
            _target = new Point3D(0, 0, 0);
            UpdateCamera();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _timer.Tick += Timer_Tick;
            SpeedSlider.ValueChanged += (s, e) => SpeedText.Text = $"{SpeedSlider.Value:0.0}x";

            _scene = new Model3DGroup();
            _scene.Children.Add(new AmbientLight(Color.FromRgb(80, 80, 80)));
            _scene.Children.Add(new DirectionalLight(Color.FromRgb(200, 200, 200), new Vector3D(-1, 1, -1)));

            // Axes and ground
            _scene.Children.Add(CreateAxisModel());
            _scene.Children.Add(CreateGroundPlane(2000));
            _scene.Children.Add(CreateGridLinesModel(2000, 50, 200));

            // Path
            _pathModel = new GeometryModel3D
            {
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0, 180, 255))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0, 180, 255)))
            };
            _scene.Children.Add(_pathModel);

            _trailModel = new GeometryModel3D
            {
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0, 255, 180))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0, 255, 180)))
            };
            _scene.Children.Add(_trailModel);

            // Key points group
            _keyPointGroup = new Model3DGroup();
            _scene.Children.Add(_keyPointGroup);

            // Cursor
            var cursorMesh = CreateSphereMesh(new Point3D(0, 0, 0), 6, 12, 8);
            _cursorTransform = new TranslateTransform3D();
            _cursorModel = new GeometryModel3D
            {
                Geometry = cursorMesh,
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 80, 80))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 80, 80))),
                Transform = _cursorTransform
            };
            _scene.Children.Add(_cursorModel);

            var modelVisual = new ModelVisual3D { Content = _scene };
            Viewport.Children.Add(modelVisual);
        }

        public void SetSimulationData(IReadOnlyList<Point3D> points, IReadOnlyList<int> pointStepIndex, IReadOnlyList<SimulationStep> steps)
        {
            _points = points?.ToList() ?? new List<Point3D>();
            _pointStepIndex = pointStepIndex?.ToList() ?? new List<int>();
            _steps = steps?.ToList() ?? new List<SimulationStep>();
            StepList.ItemsSource = _steps.Select(s => s.Label).ToList();
            ResetTrail();

            // Path mesh (full path for visibility)
            _pathModel.Geometry = CreatePathMesh(_points, 1.2);

            // Key points as small spheres
            _keyPointGroup.Children.Clear();
            for (int i = 0; i < _points.Count; i++)
            {
                if (i == 0 || i == _points.Count - 1 || i % 5 == 0)
                {
                    var mesh = CreateSphereMesh(_points[i], 4, 8, 6);
                    var model = new GeometryModel3D
                    {
                        Geometry = mesh,
                        Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 200, 80))),
                        BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 200, 80)))
                    };
                    _keyPointGroup.Children.Add(model);
                }
            }

            _segmentIndex = 0;
            _segmentT = 0;

            if (_points.Count > 0)
            {
                _cursorTransform.OffsetX = _points[0].X;
                _cursorTransform.OffsetY = _points[0].Y;
                _cursorTransform.OffsetZ = _points[0].Z;
                UpdateCoordText(_points[0]);
            }

            AutoCenterCamera();

            UpdateCurrentStepByPointIndex(0);
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_points.Count < 2) return;
            if (_segmentIndex >= _points.Count - 1)
            {
                _timer.Stop();
                return;
            }

            var p0 = _points[_segmentIndex];
            var p1 = _points[_segmentIndex + 1];

            _segmentT += SpeedSlider.Value * 0.03 * _currentSpeedFactor;
            if (_segmentT >= 1.0)
            {
                _segmentIndex++;
                _segmentT = 0;
                if (_segmentIndex >= _points.Count - 1)
                {
                    var last = _points.Last();
                    _cursorTransform.OffsetX = last.X;
                    _cursorTransform.OffsetY = last.Y;
                    _cursorTransform.OffsetZ = last.Z;
                    _timer.Stop();
                    return;
                }
                p0 = _points[_segmentIndex];
                p1 = _points[_segmentIndex + 1];
            }

            var cx = p0.X + (p1.X - p0.X) * _segmentT;
            var cy = p0.Y + (p1.Y - p0.Y) * _segmentT;
            var cz = p0.Z + (p1.Z - p0.Z) * _segmentT;
            _cursorTransform.OffsetX = cx;
            _cursorTransform.OffsetY = cy;
            _cursorTransform.OffsetZ = cz;
            UpdateTrailPoint(new Point3D(cx, cy, cz));
            UpdateCoordText(new Point3D(cx, cy, cz));
            UpdateCurrentStepByPointIndex(_segmentIndex + 1);
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (_points.Count < 2) return;
            ResetTrail();
            _timer.Start();
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _segmentIndex = 0;
            _segmentT = 0;
            if (_points.Count > 0)
            {
                _cursorTransform.OffsetX = _points[0].X;
                _cursorTransform.OffsetY = _points[0].Y;
                _cursorTransform.OffsetZ = _points[0].Z;
                UpdateCoordText(_points[0]);
            }

            UpdateCurrentStepByPointIndex(0);
        }

        private void UpdateCoordText(Point3D point)
        {
            CoordText.Text = $"X: {point.X:0.##}  Y: {point.Y:0.##}  Z: {point.Z:0.##}";
        }

        private void UpdateCurrentStepByPointIndex(int pointIndex)
        {
            if (_pointStepIndex.Count == 0 || _steps.Count == 0) return;
            if (pointIndex < 0 || pointIndex >= _pointStepIndex.Count) return;

            int stepIndex = _pointStepIndex[pointIndex];
            if (stepIndex < 0 || stepIndex >= _steps.Count) return;
            if (stepIndex == _currentStepIndex) return;

            _currentStepIndex = stepIndex;
            StepList.SelectedIndex = stepIndex;
            var step = _steps[stepIndex];
            CurrentStepText.Text = step.Label;
            StepJsonText.Text = TryFormatJson(step.Json);
            _currentSpeedFactor = GetSpeedFactor(step.Json);
        }

        private static double GetSpeedFactor(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("speed", out var speedEl)) return 1.0;

                double speed = 0;
                if (speedEl.ValueKind == System.Text.Json.JsonValueKind.Object && speedEl.TryGetProperty("value", out var v))
                {
                    if (v.ValueKind == System.Text.Json.JsonValueKind.Number) speed = v.GetDouble();
                    else if (v.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(v.GetString(), out var ds)) speed = ds;
                }
                else if (speedEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    speed = speedEl.GetDouble();
                }
                else if (speedEl.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(speedEl.GetString(), out var ds))
                {
                    speed = ds;
                }

                if (speed <= 0) return 1.0;
                return Math.Max(0.1, Math.Min(5.0, speed / 1000.0));
            }
            catch
            {
                return 1.0;
            }
        }

        private static string TryFormatJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return json ?? "{}";
            }
        }

        private void ResetTrail()
        {
            _trailPoints.Clear();
            var start = new Point3D(_cursorTransform.OffsetX, _cursorTransform.OffsetY, _cursorTransform.OffsetZ);
            _trailPoints.Add(start);
            _trailModel.Geometry = CreatePathMesh(_trailPoints, 1.6);
        }

        private void UpdateTrailPoint(Point3D point)
        {
            if (_trailPoints.Count == 0)
            {
                _trailPoints.Add(point);
                _trailModel.Geometry = CreatePathMesh(_trailPoints, 1.6);
                return;
            }

            var last = _trailPoints[_trailPoints.Count - 1];
            if ((point - last).Length < 0.1) return;

            _trailPoints.Add(point);
            _trailModel.Geometry = CreatePathMesh(_trailPoints, 1.6);
        }

        private void AutoCenterCamera()
        {
            if (_points.Count == 0) return;

            double minX = _points[0].X, maxX = _points[0].X;
            double minY = _points[0].Y, maxY = _points[0].Y;
            double minZ = _points[0].Z, maxZ = _points[0].Z;

            for (int i = 1; i < _points.Count; i++)
            {
                var p = _points[i];
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
            }

            _target = new Point3D((minX + maxX) / 2.0, (minY + maxY) / 2.0, (minZ + maxZ) / 2.0);
            double dx = maxX - minX;
            double dy = maxY - minY;
            double dz = maxZ - minZ;
            double span = Math.Max(dx, Math.Max(dy, dz));
            if (span < 1) span = 1;
            _distance = Math.Max(200, Math.Min(5000, span * 2.0));
            UpdateCamera();
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0) _distance *= 0.9;
            else _distance *= 1.1;
            _distance = Math.Max(100, Math.Min(5000, _distance));
            UpdateCamera();
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastMouse = e.GetPosition(Viewport);
            Mouse.Capture(Viewport);
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            Mouse.Capture(null);
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var pos = e.GetPosition(Viewport);
            var dx = pos.X - _lastMouse.X;
            var dy = pos.Y - _lastMouse.Y;
            _lastMouse = pos;

            _yaw += dx * 0.4;
            _pitch -= dy * 0.4;
            _pitch = Math.Max(-89, Math.Min(89, _pitch));
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            double yawRad = _yaw * Math.PI / 180.0;
            double pitchRad = _pitch * Math.PI / 180.0;

            double x = _target.X + _distance * Math.Cos(pitchRad) * Math.Cos(yawRad);
            double y = _target.Y + _distance * Math.Cos(pitchRad) * Math.Sin(yawRad);
            double z = _target.Z + _distance * Math.Sin(pitchRad);

            _camera.Position = new Point3D(x, y, z);
            _camera.LookDirection = new Vector3D(_target.X - x, _target.Y - y, _target.Z - z);
            _camera.UpDirection = new Vector3D(0, 0, 1);
        }

        private static Model3DGroup CreateAxisModel()
        {
            var group = new Model3DGroup();

            group.Children.Add(new GeometryModel3D
            {
                Geometry = CreateCylinderMesh(new Point3D(0, 0, 0), new Point3D(400, 0, 0), 2),
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 80, 80))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 80, 80)))
            });

            group.Children.Add(new GeometryModel3D
            {
                Geometry = CreateCylinderMesh(new Point3D(0, 0, 0), new Point3D(0, 400, 0), 2),
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 255, 120))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 255, 120)))
            });

            group.Children.Add(new GeometryModel3D
            {
                Geometry = CreateCylinderMesh(new Point3D(0, 0, 0), new Point3D(0, 0, 400), 2),
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 160, 255))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 160, 255)))
            });

            return group;
        }

        private static GeometryModel3D CreateGroundPlane(double size)
        {
            double half = size / 2;
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection
                {
                    new Point3D(-half, -half, 0),
                    new Point3D(half, -half, 0),
                    new Point3D(half, half, 0),
                    new Point3D(-half, half, 0)
                },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
            };

            return new GeometryModel3D
            {
                Geometry = mesh,
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(35, 120, 120, 120))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(35, 120, 120, 120)))
            };
        }

        private static Model3DGroup CreateGridLinesModel(double size, double minorStep, double majorStep)
        {
            var group = new Model3DGroup();
            double half = size / 2;

            var minorMesh = new MeshGeometry3D();
            var majorMesh = new MeshGeometry3D();

            for (double i = -half; i <= half; i += minorStep)
            {
                bool isMajor = Math.Abs(i % majorStep) < 0.001;
                var mesh = isMajor ? majorMesh : minorMesh;

                AppendCylinder(mesh, new Point3D(-half, i, 0.2), new Point3D(half, i, 0.2), isMajor ? 0.6 : 0.3, 6);
                AppendCylinder(mesh, new Point3D(i, -half, 0.2), new Point3D(i, half, 0.2), isMajor ? 0.6 : 0.3, 6);
            }

            if (minorMesh.Positions.Count > 0)
            {
                group.Children.Add(new GeometryModel3D
                {
                    Geometry = minorMesh,
                    Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(80, 90, 90, 90))),
                    BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(80, 90, 90, 90)))
                });
            }

            if (majorMesh.Positions.Count > 0)
            {
                group.Children.Add(new GeometryModel3D
                {
                    Geometry = majorMesh,
                    Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(140, 120, 120, 120))),
                    BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(140, 120, 120, 120)))
                });
            }

            return group;
        }

        private static MeshGeometry3D CreatePathMesh(IReadOnlyList<Point3D> points, double radius)
        {
            var mesh = new MeshGeometry3D();
            if (points == null || points.Count < 2) return mesh;

            int slices = 8;
            for (int i = 0; i < points.Count - 1; i++)
            {
                AppendCylinder(mesh, points[i], points[i + 1], radius, slices);
            }
            return mesh;
        }

        private static MeshGeometry3D CreateSphereMesh(Point3D center, double radius, int slices, int stacks)
        {
            var mesh = new MeshGeometry3D();

            for (int stack = 0; stack <= stacks; stack++)
            {
                double phi = Math.PI * stack / stacks;
                double y = Math.Cos(phi);
                double scale = Math.Sin(phi);

                for (int slice = 0; slice <= slices; slice++)
                {
                    double theta = 2 * Math.PI * slice / slices;
                    double x = scale * Math.Cos(theta);
                    double z = scale * Math.Sin(theta);
                    mesh.Positions.Add(new Point3D(
                        center.X + radius * x,
                        center.Y + radius * z,
                        center.Z + radius * y));
                }
            }

            int vertCount = slices + 1;
            for (int stack = 0; stack < stacks; stack++)
            {
                for (int slice = 0; slice < slices; slice++)
                {
                    int a = stack * vertCount + slice;
                    int b = a + vertCount;
                    mesh.TriangleIndices.Add(a);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(a + 1);

                    mesh.TriangleIndices.Add(a + 1);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(b + 1);
                }
            }

            return mesh;
        }

        private static MeshGeometry3D CreateCylinderMesh(Point3D p0, Point3D p1, double radius)
        {
            var mesh = new MeshGeometry3D();
            AppendCylinder(mesh, p0, p1, radius, 12);
            return mesh;
        }

        private static void AppendCylinder(MeshGeometry3D mesh, Point3D p0, Point3D p1, double radius, int slices)
        {
            var dir = p1 - p0;
            if (dir.Length < 0.0001) return;
            dir.Normalize();

            Vector3D up = new Vector3D(0, 0, 1);
            if (Math.Abs(Vector3D.DotProduct(dir, up)) > 0.9)
            {
                up = new Vector3D(0, 1, 0);
            }

            var u = Vector3D.CrossProduct(dir, up);
            u.Normalize();
            var v = Vector3D.CrossProduct(dir, u);
            v.Normalize();

            int baseIndex = mesh.Positions.Count;
            for (int i = 0; i <= slices; i++)
            {
                double angle = 2 * Math.PI * i / slices;
                var offset = (u * Math.Cos(angle) + v * Math.Sin(angle)) * radius;
                mesh.Positions.Add(p0 + offset);
                mesh.Positions.Add(p1 + offset);
            }

            for (int i = 0; i < slices; i++)
            {
                int idx = baseIndex + i * 2;
                mesh.TriangleIndices.Add(idx);
                mesh.TriangleIndices.Add(idx + 1);
                mesh.TriangleIndices.Add(idx + 2);

                mesh.TriangleIndices.Add(idx + 2);
                mesh.TriangleIndices.Add(idx + 1);
                mesh.TriangleIndices.Add(idx + 3);
            }
        }
    }
}
