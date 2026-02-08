using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
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

        public class EditableParam
        {
            public string Path { get; set; } = "";
            public string Value { get; set; } = "";
            public JsonValueKind Kind { get; set; } = JsonValueKind.String;
        }

        public event Action<int, string>? StepJsonUpdated;
        public Func<bool>? SaveCurrentFileRequested;
        public Func<bool>? SaveAsFileRequested;

        private readonly DispatcherTimer _timer;
        private List<Point3D> _points = new();
        private List<int> _pointStepIndex = new();
        private List<SimulationStep> _steps = new();
        private int _currentStepIndex = -1;
        private double _currentSpeedFactor = 1.0;
        private int _segmentIndex;
        private double _segmentT;
        private readonly Model3DGroup _scene;
        private readonly ScaleTransform3D _sceneScale;
        private readonly GeometryModel3D _pathModel;
        private readonly GeometryModel3D _trailModel;
        private readonly Model3DGroup _keyPointGroup;
        private readonly GeometryModel3D _cursorModel;
        private readonly TranslateTransform3D _cursorTransform;
        private readonly List<Point3D> _trailPoints = new();
        private readonly List<Viewport2DVisual3D> _labelVisuals = new();
        private readonly ObservableCollection<EditableParam> _editParams = new();
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
            ViewScaleSlider.ValueChanged += (s, e) =>
            {
                _sceneScale.ScaleX = ViewScaleSlider.Value;
                _sceneScale.ScaleY = ViewScaleSlider.Value;
                _sceneScale.ScaleZ = ViewScaleSlider.Value;
                ViewScaleText.Text = $"{ViewScaleSlider.Value:0.00}";
            };

            _scene = new Model3DGroup();
            _sceneScale = new ScaleTransform3D(1, 1, 1);
            _scene.Transform = _sceneScale;
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

            AddAxisLabels(2000, 200);
            AddAxisXYZLabels();
        }

        public void SetSimulationData(IReadOnlyList<Point3D> points, IReadOnlyList<int> pointStepIndex, IReadOnlyList<SimulationStep> steps, IReadOnlyList<Point3D> keyPoints)
        {
            _points = points?.ToList() ?? new List<Point3D>();
            _pointStepIndex = pointStepIndex?.ToList() ?? new List<int>();
            _steps = steps?.ToList() ?? new List<SimulationStep>();
            StepList.ItemsSource = _steps.Select(s => s.Label).ToList();
            ParamGrid.ItemsSource = _editParams;
            ResetTrail();

            // Path mesh (do not pre-render; show trail only while moving)
            _pathModel.Geometry = new MeshGeometry3D();

            // Key points as small spheres
            _keyPointGroup.Children.Clear();
            var keys = keyPoints?.ToList() ?? new List<Point3D>();
            for (int i = 0; i < keys.Count; i++)
            {
                var mesh = CreateSphereMesh(keys[i], 4, 8, 6);
                var model = new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 200, 80))),
                    BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 200, 80)))
                };
                _keyPointGroup.Children.Add(model);
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

        private void Stop_Click(object sender, RoutedEventArgs e)
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
            CoordValueX.Text = $"{point.X:0.##}";
            CoordValueY.Text = $"{point.Y:0.##}";
            CoordValueZ.Text = $"{point.Z:0.##}";
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

            BuildEditableParams(step.Json);
        }

        private void UpdateCurrentStepByIndex(int stepIndex)
        {
            if (_steps.Count == 0) return;
            if (stepIndex < 0 || stepIndex >= _steps.Count) return;
            if (stepIndex == _currentStepIndex) return;

            _currentStepIndex = stepIndex;
            StepList.SelectedIndex = stepIndex;
            var step = _steps[stepIndex];
            CurrentStepText.Text = step.Label;
            StepJsonText.Text = TryFormatJson(step.Json);
            _currentSpeedFactor = GetSpeedFactor(step.Json);
            BuildEditableParams(step.Json);
        }

        private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StepList.SelectedIndex >= 0)
            {
                UpdateCurrentStepByIndex(StepList.SelectedIndex);
            }
        }

        private void BuildEditableParams(string json)
        {
            _editParams.Clear();
            try
            {
                var node = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                if (node == null) return;

                void Walk(JsonNode? n, string path)
                {
                    if (n == null) return;
                    if (n is JsonValue val)
                    {
                        JsonValueKind kind = JsonValueKind.String;
                        try
                        {
                            using var doc = JsonDocument.Parse(val.ToJsonString());
                            kind = doc.RootElement.ValueKind;
                        }
                        catch { }

                        string valueText = val.ToJsonString();
                        if (kind == JsonValueKind.String)
                        {
                            valueText = valueText.Trim('"');
                        }

                        _editParams.Add(new EditableParam
                        {
                            Path = path,
                            Value = valueText,
                            Kind = kind
                        });
                        return;
                    }

                    if (n is JsonObject obj)
                    {
                        foreach (var kv in obj)
                        {
                            string childPath = string.IsNullOrEmpty(path) ? kv.Key : $"{path}.{kv.Key}";
                            Walk(kv.Value, childPath);
                        }
                        return;
                    }

                    if (n is JsonArray arr)
                    {
                        for (int i = 0; i < arr.Count; i++)
                        {
                            string childPath = $"{path}[{i}]";
                            Walk(arr[i], childPath);
                        }
                    }
                }

                Walk(node, "");
            }
            catch
            {
                // ignore parse errors
            }
        }

        private void ApplyJson_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStepIndex < 0 || _currentStepIndex >= _steps.Count) return;

            var step = _steps[_currentStepIndex];
            try
            {
                var node = JsonNode.Parse(string.IsNullOrWhiteSpace(step.Json) ? "{}" : step.Json);
                if (node == null) return;

                foreach (var param in _editParams)
                {
                    SetJsonValue(node, param.Path, param.Value, param.Kind);
                }

                step.Json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                StepJsonText.Text = step.Json;
                _currentSpeedFactor = GetSpeedFactor(step.Json);
                StepJsonUpdated?.Invoke(_currentStepIndex, step.Json);
            }
            catch
            {
                // ignore apply errors
            }
        }

        private void SaveJson_Click(object sender, RoutedEventArgs e)
        {
            if (SaveCurrentFileRequested?.Invoke() == true) return;
            SaveAsFileRequested?.Invoke();
        }

        private void SaveAsJson_Click(object sender, RoutedEventArgs e)
        {
            SaveAsFileRequested?.Invoke();
        }

        private static void SetJsonValue(JsonNode node, string path, string value, JsonValueKind kind)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            JsonNode? current = node;
            string[] parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                int bracket = part.IndexOf('[');
                if (bracket >= 0)
                {
                    string propName = part.Substring(0, bracket);
                    if (!string.IsNullOrEmpty(propName))
                    {
                        current = (current as JsonObject)?[propName];
                    }

                    while (bracket >= 0 && current is JsonArray arr)
                    {
                        int end = part.IndexOf(']', bracket + 1);
                        if (end < 0) return;
                        string idxStr = part.Substring(bracket + 1, end - bracket - 1);
                        if (!int.TryParse(idxStr, out int idx) || idx < 0 || idx >= arr.Count) return;

                        bool isLastPart = (i == parts.Length - 1) && (part.IndexOf('[', end + 1) < 0);
                        if (isLastPart)
                        {
                            arr[idx] = CreateValueNode(value, kind);
                            return;
                        }

                        current = arr[idx];
                        bracket = part.IndexOf('[', end + 1);
                    }
                }
                else
                {
                    if (i == parts.Length - 1 && current is JsonObject obj)
                    {
                        obj[part] = CreateValueNode(value, kind);
                        return;
                    }
                    current = (current as JsonObject)?[part];
                }
            }
        }

        private static JsonNode CreateValueNode(string value, JsonValueKind kind)
        {
            return kind switch
            {
                JsonValueKind.Number => double.TryParse(value, out var d) ? JsonValue.Create(d) : JsonValue.Create(0),
                JsonValueKind.True => JsonValue.Create(true),
                JsonValueKind.False => JsonValue.Create(false),
                JsonValueKind.Null => JsonValue.Create((string?)null),
                _ => JsonValue.Create(value)
            };
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

        private void AddAxisLabels(double size, double step)
        {
            ClearAxisLabels();

            double half = size / 2.0;
            double offset = 12;

            for (double x = -half; x <= half; x += step)
            {
                var pos = new Point3D(x, -offset, 0);
                AddLabelVisual(pos, new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), 40, 16, x.ToString("0"));
            }

            for (double y = -half; y <= half; y += step)
            {
                var pos = new Point3D(offset, y, 0);
                AddLabelVisual(pos, new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), 40, 16, y.ToString("0"));
            }
        }

        private void AddAxisXYZLabels()
        {
            AddLabelVisual(new Point3D(430, -14, 0), new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), 20, 16, "X");
            AddLabelVisual(new Point3D(0, 430, 0), new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), 20, 16, "Y");
            AddLabelVisual(new Point3D(0, -14, 430), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), 20, 16, "Z");
        }

        private void ClearAxisLabels()
        {
            foreach (var visual in _labelVisuals)
            {
                Viewport.Children.Remove(visual);
            }
            _labelVisuals.Clear();
        }

        private void AddLabelVisual(Point3D origin, Vector3D right, Vector3D up, double width, double height, string text)
        {
            var p0 = origin;
            var p1 = origin + right * width;
            var p2 = origin + right * width + up * height;
            var p3 = origin + up * height;

            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { p0, p1, p2, p3 },
                TextureCoordinates = new PointCollection
                {
                    new Point(0, 1),
                    new Point(1, 1),
                    new Point(1, 0),
                    new Point(0, 0)
                },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
            };

            var labelText = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(140, 20, 20, 20)),
                FontSize = 10,
                Padding = new Thickness(2, 0, 2, 0)
            };

            var material = new DiffuseMaterial(Brushes.White);
            material.SetValue(Viewport2DVisual3D.IsVisualHostMaterialProperty, true);

            var visual = new Viewport2DVisual3D
            {
                Geometry = mesh,
                Material = material,
                Visual = labelText,
                Transform = _sceneScale
            };

            _labelVisuals.Add(visual);
            Viewport.Children.Add(visual);
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                double step = 0.05;
                if (e.Delta > 0) ViewScaleSlider.Value = Math.Min(ViewScaleSlider.Maximum, ViewScaleSlider.Value + step);
                else ViewScaleSlider.Value = Math.Max(ViewScaleSlider.Minimum, ViewScaleSlider.Value - step);
                e.Handled = true;
                return;
            }

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

        private void SetCameraView(double yaw, double pitch)
        {
            _yaw = yaw;
            _pitch = Math.Max(-89, Math.Min(89, pitch));
            UpdateCamera();
        }

        private void CamFront_Click(object sender, RoutedEventArgs e) => SetCameraView(90, 0);
        private void CamBack_Click(object sender, RoutedEventArgs e) => SetCameraView(270, 0);
        private void CamLeft_Click(object sender, RoutedEventArgs e) => SetCameraView(180, 0);
        private void CamRight_Click(object sender, RoutedEventArgs e) => SetCameraView(0, 0);
        private void CamTop_Click(object sender, RoutedEventArgs e) => SetCameraView(45, 89);
        private void CamBottom_Click(object sender, RoutedEventArgs e) => SetCameraView(45, -89);
        private void CamIso_Click(object sender, RoutedEventArgs e) => SetCameraView(45, 35);
        private void CamFit_Click(object sender, RoutedEventArgs e) => AutoCenterCamera();

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
                Geometry = CreateConeMesh(new Point3D(360, 0, 0), new Vector3D(1, 0, 0), 20, 6, 16),
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
                Geometry = CreateConeMesh(new Point3D(0, 360, 0), new Vector3D(0, 1, 0), 20, 6, 16),
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 255, 120))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 255, 120)))
            });

            group.Children.Add(new GeometryModel3D
            {
                Geometry = CreateCylinderMesh(new Point3D(0, 0, 0), new Point3D(0, 0, 400), 2),
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 160, 255))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 160, 255)))
            });

            group.Children.Add(new GeometryModel3D
            {
                Geometry = CreateConeMesh(new Point3D(0, 0, 360), new Vector3D(0, 0, 1), 20, 6, 16),
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

        private static MeshGeometry3D CreateConeMesh(Point3D baseCenter, Vector3D direction, double height, double radius, int slices)
        {
            var mesh = new MeshGeometry3D();
            if (direction.Length < 0.0001) return mesh;
            direction.Normalize();

            Vector3D up = new Vector3D(0, 0, 1);
            if (Math.Abs(Vector3D.DotProduct(direction, up)) > 0.9)
            {
                up = new Vector3D(0, 1, 0);
            }

            var u = Vector3D.CrossProduct(direction, up);
            u.Normalize();
            var v = Vector3D.CrossProduct(direction, u);
            v.Normalize();

            Point3D tip = baseCenter + direction * height;
            int baseIndex = 0;

            for (int i = 0; i < slices; i++)
            {
                double theta = 2 * Math.PI * i / slices;
                double cx = Math.Cos(theta) * radius;
                double cy = Math.Sin(theta) * radius;
                var point = baseCenter + u * cx + v * cy;
                mesh.Positions.Add(point);
            }

            mesh.Positions.Add(tip);
            int tipIndex = mesh.Positions.Count - 1;

            for (int i = 0; i < slices; i++)
            {
                int next = (i + 1) % slices;
                mesh.TriangleIndices.Add(i + baseIndex);
                mesh.TriangleIndices.Add(next + baseIndex);
                mesh.TriangleIndices.Add(tipIndex);
            }

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
