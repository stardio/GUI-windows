using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Text.Json;

namespace EtherCAT_Studio
{
    public partial class MainWindow : Window
    {
        public enum PortType { Input, Output }

        public class PortInfo
        {
            public PortType Type { get; set; }
            public NodeControl? Node { get; set; }
            public Ellipse? Ellipse { get; set; }
        }

        // 연결 정보
        public class Connection
        {
            public PortInfo? From { get; set; }
            public PortInfo? To { get; set; }
            public Path? Path { get; set; }
        }

        private readonly List<Connection> _connections = new();
        private NodeControl? _selectedNode = null;
        private Connection? _selectedConnection = null;
        private PortInfo? _draggingFromPort = null;
        private Path? _tempWire = null;
        private string? _currentFilePath;
        private bool _currentFileIsSequence;

        public MainWindow()
        {
            InitializeComponent();
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            MainCanvas.MouseLeftButtonDown += MainCanvas_MouseLeftButtonDown_ClearSelection;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private static string GetAppDataDir()
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtherCAT_Studio");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetLastSessionPath()
        {
            return System.IO.Path.Combine(GetAppDataDir(), "last_session.ethercat");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var path = GetLastSessionPath();
            if (!System.IO.File.Exists(path)) return;

            try
            {
                var json = System.IO.File.ReadAllText(path);
                LoadProjectFromJson(json);
            }
            catch
            {
                // ignore restore errors
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (MainCanvas.Children.OfType<NodeControl>().Any())
                {
                    SaveProjectToPath(GetLastSessionPath());
                }
            }
            catch
            {
                // ignore autosave errors
            }
        }

        private void MainCanvas_MouseLeftButtonDown_ClearSelection(object? sender, MouseButtonEventArgs e)
        {
            // click on canvas background clears selection
            if (e.Source == MainCanvas)
            {
                ClearSelection();
            }
        }

        public void SelectNode(NodeControl node)
        {
            ClearSelection();
            _selectedNode = node;
            _selectedNode.IsSelected = true;
        }

        public void SelectConnection(Connection conn)
        {
            ClearSelection();
            _selectedConnection = conn;
            if (conn.Path != null)
            {
                conn.Path.Stroke = Brushes.Cyan;
                conn.Path.StrokeThickness = 4;
            }
        }

        private void ClearSelection()
        {
            if (_selectedNode != null)
            {
                _selectedNode.IsSelected = false;
                _selectedNode = null;
            }
            if (_selectedConnection != null)
            {
                if (_selectedConnection.Path != null)
                {
                    _selectedConnection.Path.Stroke = Brushes.Yellow;
                    _selectedConnection.Path.StrokeThickness = 3;
                }
                _selectedConnection = null;
            }
        }

        private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (_selectedConnection != null)
                {
                    // remove path and connection
                    if (_selectedConnection.Path != null)
                        MainCanvas.Children.Remove(_selectedConnection.Path);
                    _connections.Remove(_selectedConnection);
                    _selectedConnection = null;
                    e.Handled = true;
                    return;
                }
                if (_selectedNode != null)
                {
                    // remove any connections attached to node
                    var toRemove = _connections.Where(c => c.From?.Node == _selectedNode || c.To?.Node == _selectedNode).ToList();
                    foreach (var c in toRemove)
                    {
                        if (c.Path != null) MainCanvas.Children.Remove(c.Path);
                        _connections.Remove(c);
                    }
                    // remove node
                    MainCanvas.Children.Remove(_selectedNode);
                    _selectedNode = null;
                    e.Handled = true;
                    return;
                }
            }
        }

        private void AddAbsMoveNode_Click(object sender, RoutedEventArgs e)
        {
            AddNode("ABS MOVE", Brushes.CornflowerBlue, nodeType: "ABS MOVE");
        }

        private void AddWaitNode_Click(object sender, RoutedEventArgs e)
        {
            AddNode("WAIT", Brushes.Orange, nodeType: "WAIT");
        }
        private void AddSetDo_Click(object sender, RoutedEventArgs e)
        {
            AddNode("SET DO", Brushes.LimeGreen, nodeType: "SET_DO");
        }

        private void AddCheckDi_Click(object sender, RoutedEventArgs e)
        {
            AddNode("CHECK DI", Brushes.Gold, nodeType: "CHECK_DI");
        }

        private void AddGoto_Click(object sender, RoutedEventArgs e)
        {
            AddNode("GOTO", Brushes.IndianRed, nodeType: "GOTO");
        }

        private void AddJump_Click(object sender, RoutedEventArgs e)
        {
            AddNode("JUMP", Brushes.MediumPurple, nodeType: "JUMP");
        }

        private void AddStartNode_Click(object sender, RoutedEventArgs e)
        {
            // START node: only output port on right
            AddNode("START", Brushes.LimeGreen, 16, 140, 48, hasInput: false, hasOutput: true, nodeType: "START");
        }

        private void AddEndNode_Click(object sender, RoutedEventArgs e)
        {
            // END node: only input port on left
            AddNode("END", Brushes.Red, 16, 140, 48, hasInput: true, hasOutput: false, nodeType: "END");
        }

        private void AddLinearMoveNode_Click(object sender, RoutedEventArgs e)
        {
            AddNode("LINEAR MOVE", Brushes.Blue, 16, 140, 48, hasInput: true, hasOutput: true, "LINEAR_MOVE");
        }

        private void AddRelMoveNode_Click(object sender, RoutedEventArgs e)
        {
            AddNode("REL MOVE", new SolidColorBrush(Color.FromRgb(30, 136, 229)), nodeType: "REL_MOVE");
        }

        private void AddCircularMoveNode_Click(object sender, RoutedEventArgs e)
        {
            AddNode("CIRCULAR MOVE", new SolidColorBrush(Color.FromRgb(94, 53, 177)), nodeType: "CIRCULAR_MOVE");
        }

        private void AddCounterNode_Click(object sender, RoutedEventArgs e)
        {
            AddNode("COUNTER", new SolidColorBrush(Color.FromRgb(171, 71, 188)), nodeType: "COUNTER");
        }

        private void AddNode(string text, Brush color, double portSize = 16, double width = 140, double height = 48, bool hasInput = true, bool hasOutput = true, string nodeType = "")
        {
            // NodeControl 인스턴스 생성 (색상 전달)
            var node = new NodeControl(text, color, this, portSize, width, height, hasInput, hasOutput, nodeType);

            int nextNumber = MainCanvas.Children.OfType<NodeControl>().Count() + 1;
            node.SetNodeNumber(nextNumber);

            // 캔버스에 임의의 위치(계단식)로 배치
            double offset = 20 + (MainCanvas.Children.Count * 20);
            Canvas.SetLeft(node, offset);
            Canvas.SetTop(node, offset);

            MainCanvas.Children.Add(node);
        }

        public void StartWireDrag(PortInfo port, MouseButtonEventArgs e)
        {
            if (port.Type != PortType.Output) return;
            _draggingFromPort = port;

            // 임시 와이어 Path 생성
            _tempWire = new Path
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                IsHitTestVisible = false
            };
            MainCanvas.Children.Add(_tempWire);

            MainCanvas.MouseMove += MainCanvas_MouseMove;
            MainCanvas.MouseLeftButtonUp += MainCanvas_MouseLeftButtonUp;
        }

        private void MainCanvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_draggingFromPort == null || _tempWire == null) return;
            if (_draggingFromPort.Node != null && _tempWire != null)
            {
                var start = _draggingFromPort.Node.GetPortPosition(_draggingFromPort, MainCanvas);
                var end = e.GetPosition(MainCanvas);
                _tempWire.Data = MakeBezierGeometry(start, end);
            }
        }

        private void MainCanvas_MouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            if (_draggingFromPort == null || _tempWire == null) return;

            // 입력 포트 위에 있으면 연결
            var pos = e.GetPosition(MainCanvas);
            foreach (var child in MainCanvas.Children)
            {
                if (child is NodeControl node)
                {
                    var inputPort = node.InputPortInfo;
                    var portPos = node.GetPortPosition(inputPort, MainCanvas);
                    if ((portPos - pos).Length < 16)
                    {
                        // 연결 생성
                        var conn = new Connection
                        {
                            From = _draggingFromPort,
                            To = inputPort,
                            Path = new Path
                            {
                                Stroke = Brushes.Yellow,
                                StrokeThickness = 3
                            }
                        };
                        if (conn.Path != null)
                        {
                            // make path clickable for selection
                            conn.Path.IsHitTestVisible = true;
                            conn.Path.MouseLeftButtonDown += (s, args) => { SelectConnection(conn); args.Handled = true; };
                            MainCanvas.Children.Add(conn.Path);
                        }
                        _connections.Add(conn);
                        UpdateConnectionPath(conn);
                        break;
                    }
                }
            }

            // 임시 와이어 제거
            MainCanvas.Children.Remove(_tempWire);
            _tempWire = null;
            _draggingFromPort = null;
            MainCanvas.MouseMove -= MainCanvas_MouseMove;
            MainCanvas.MouseLeftButtonUp -= MainCanvas_MouseLeftButtonUp;
        }

        private PathGeometry MakeBezierGeometry(Point start, Point end)
        {
            var geom = new PathGeometry();
            var seg = new BezierSegment
            {
                Point1 = new Point(start.X + 60, start.Y),
                Point2 = new Point(end.X - 60, end.Y),
                Point3 = end
            };
            var fig = new PathFigure { StartPoint = start, Segments = { seg } };
            geom.Figures.Add(fig);
            return geom;
        }

        private void UpdateConnectionPath(Connection conn)
        {
            if (conn.From?.Node != null && conn.To?.Node != null && conn.Path != null)
            {
                var start = conn.From.Node.GetPortPosition(conn.From, MainCanvas);
                var end = conn.To.Node.GetPortPosition(conn.To, MainCanvas);
                conn.Path.Data = MakeBezierGeometry(start, end);
            }
        }

        public void NodeMoved(NodeControl node)
        {
            // 노드 이동 시 연결된 와이어 업데이트
            foreach (var conn in _connections)
            {
                if ((conn.From?.Node == node) || (conn.To?.Node == node))
                    UpdateConnectionPath(conn);
            }
        }

        // 프로젝트 파일 저장 (.ethercat 형식)
        private void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "project",
                DefaultExt = ".ethercat",
                Filter = "EtherCAT Project (.ethercat)|*.ethercat"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    SaveProjectToPath(dlg.FileName);
                    _currentFilePath = dlg.FileName;
                    _currentFileIsSequence = false;
                    MessageBox.Show($"프로젝트를 저장했습니다:\n{dlg.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"저장 실패: {ex.Message}");
                }
            }
        }

        // 프로젝트 파일 불러오기 (.ethercat 형식)
        private void LoadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".ethercat",
                Filter = "All Supported (*.ethercat;*.json)|*.ethercat;*.json|EtherCAT Project (*.ethercat)|*.ethercat|JSON Sequence (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(dlg.FileName);
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // 파일 형식 자동 감지
                    if (root.TryGetProperty("steps", out _))
                    {
                        // JSON Sequence 형식 (.json - Export JSON/AI 생성)
                        LoadSequenceJsonIntoCanvas(json);
                        _currentFilePath = dlg.FileName;
                        _currentFileIsSequence = true;
                        MessageBox.Show($"JSON 시퀀스를 불러왔습니다:\n{dlg.FileName}");
                    }
                    else if (root.TryGetProperty("nodes", out var nodesArray))
                    {
                        // EtherCAT Project 형식 (.ethercat)
                        LoadEthercatProject(root, nodesArray);
                        _currentFilePath = dlg.FileName;
                        _currentFileIsSequence = false;
                        MessageBox.Show($"프로젝트를 불러왔습니다:\n{dlg.FileName}");
                    }
                    else
                    {
                        MessageBox.Show("지원하지 않는 파일 형식입니다.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"불러오기 실패:\n{ex.Message}\n\n상세:\n{ex.StackTrace}");
                }
            }
        }

        private void LoadEthercatProject(System.Text.Json.JsonElement root, System.Text.Json.JsonElement nodesArray)
        {
            // 기존 노드와 연결 삭제
            MainCanvas.Children.Clear();
            _connections.Clear();

            var loadedNodes = new List<NodeControl>();

            // 노드 복원
            foreach (var nodeData in nodesArray.EnumerateArray())
            {
                string nodeType = nodeData.TryGetProperty("nodeType", out var nt) ? nt.GetString() ?? "" : "";
                string originalLabel = nodeData.TryGetProperty("originalLabel", out var ol) ? ol.GetString() ?? "" : "";
                double x = nodeData.TryGetProperty("x", out var xp) && xp.TryGetDouble(out var xd) ? xd : 0;
                double y = nodeData.TryGetProperty("y", out var yp) && yp.TryGetDouble(out var yd) ? yd : 0;
                string? jsonData = nodeData.TryGetProperty("jsonData", out var jd) ? jd.GetString() : null;

                // 노드 타입에 맞는 색상 결정
                Brush color = nodeType switch
                {
                    "ABS MOVE" => Brushes.CornflowerBlue,
                    "WAIT" => Brushes.Orange,
                    "SET_DO" => Brushes.LimeGreen,
                    "CHECK_DI" => Brushes.Gold,
                    "GOTO" => Brushes.IndianRed,
                    "JUMP" => Brushes.MediumPurple,
                    "START" => Brushes.LimeGreen,
                    "END" => Brushes.Red,
                    "LINEAR_MOVE" => Brushes.Blue,
                    "REL_MOVE" => new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),
                    "CIRCULAR_MOVE" => new SolidColorBrush(Color.FromRgb(0x5E, 0x35, 0xB1)),
                    "COUNTER" => new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC)),
                    _ => Brushes.Gray
                };

                bool hasInput = nodeType != "START";
                bool hasOutput = nodeType != "END";

                var node = new NodeControl(originalLabel, color, this, 16, 140, 48, hasInput, hasOutput, nodeType);
                node.SetNodeNumber(loadedNodes.Count + 1);
                node.JsonData = jsonData;
                Canvas.SetLeft(node, x);
                Canvas.SetTop(node, y);
                MainCanvas.Children.Add(node);
                loadedNodes.Add(node);
            }

            // 레이아웃 업데이트를 강제로 수행하여 노드 위치가 확정되도록 함
            MainCanvas.UpdateLayout();

            // 연결 복원
            if (root.TryGetProperty("connections", out var connsArray))
            {
                foreach (var connData in connsArray.EnumerateArray())
                {
                    int fromIdx = connData.TryGetProperty("fromNodeIndex", out var fi) && fi.TryGetInt32(out var fii) ? fii : -1;
                    int toIdx = connData.TryGetProperty("toNodeIndex", out var ti) && ti.TryGetInt32(out var tii) ? tii : -1;

                    if (fromIdx >= 0 && fromIdx < loadedNodes.Count && toIdx >= 0 && toIdx < loadedNodes.Count)
                    {
                        var fromNode = loadedNodes[fromIdx];
                        var toNode = loadedNodes[toIdx];

                        var conn = new Connection
                        {
                            From = fromNode.OutputPortInfo,
                            To = toNode.InputPortInfo,
                            Path = new Path
                            {
                                Stroke = Brushes.Yellow,
                                StrokeThickness = 3,
                                IsHitTestVisible = true
                            }
                        };
                        conn.Path.MouseLeftButtonDown += (s, args) => { SelectConnection(conn); args.Handled = true; };
                        MainCanvas.Children.Add(conn.Path);
                        _connections.Add(conn);
                        UpdateConnectionPath(conn);
                    }
                }
            }
        }

        private void SaveProjectToPath(string filePath)
        {
            var nodesList = MainCanvas.Children.OfType<NodeControl>().ToList();
            var projectData = new
            {
                nodes = nodesList.Select(n => new
                {
                    nodeType = n.NodeType,
                    originalLabel = n.OriginalLabel,
                    x = Canvas.GetLeft(n),
                    y = Canvas.GetTop(n),
                    jsonData = n.JsonData
                }).ToList(),
                connections = _connections.Select(c => new
                {
                    fromNodeIndex = c.From?.Node != null ? nodesList.IndexOf(c.From.Node) : -1,
                    toNodeIndex = c.To?.Node != null ? nodesList.IndexOf(c.To.Node) : -1
                }).Where(c => c.fromNodeIndex >= 0 && c.toNodeIndex >= 0).ToList()
            };

            var json = System.Text.Json.JsonSerializer.Serialize(projectData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(filePath, json);
        }

        private bool SaveCurrentOpenedFile()
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                _currentFilePath = GetLastSessionPath();
                _currentFileIsSequence = false;
            }

            var result = MessageBox.Show(
                $"현재 열린 파일을 저장할까요?\n\n{_currentFilePath}",
                "저장 확인",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.OK)
            {
                return false;
            }

            try
            {
                if (_currentFileIsSequence)
                {
                    var seqJson = BuildSequenceJsonString();
                    if (string.IsNullOrEmpty(seqJson)) return false;
                    System.IO.File.WriteAllText(_currentFilePath, seqJson);
                }
                else
                {
                    SaveProjectToPath(_currentFilePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}");
                return false;
            }
        }

        private bool SaveCurrentOpenedFileAs()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = _currentFileIsSequence ? "sequence" : "project",
                DefaultExt = _currentFileIsSequence ? ".json" : ".ethercat",
                Filter = _currentFileIsSequence
                    ? "JSON Sequence (*.json)|*.json|All Files (*.*)|*.*"
                    : "EtherCAT Project (*.ethercat)|*.ethercat|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                _currentFilePath = dlg.FileName;
                _currentFileIsSequence = System.IO.Path.GetExtension(dlg.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase);
                return SaveCurrentOpenedFile();
            }

            return false;
        }

        private void LoadProjectFromJson(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("nodes", out var nodesArray))
            {
                LoadEthercatProject(root, nodesArray);
            }
        }

        // 저장 버튼 클릭 시 모든 노드를 시퀀스 JSON 구조로 파일로 저장
        private void SaveJson_Click(object sender, RoutedEventArgs e)
        {
            var outJson = BuildSequenceJsonString();
            if (string.IsNullOrEmpty(outJson)) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
                FileName = "sequence.json"
            };
            if (dlg.ShowDialog() == true)
            {
                System.IO.File.WriteAllText(dlg.FileName, outJson);
                _currentFilePath = dlg.FileName;
                _currentFileIsSequence = true;
                MessageBox.Show("저장 완료: " + dlg.FileName);
            }
        }

        private string? BuildSequenceJsonString()
        {
            var steps = new List<object>();
            int idx = 1;
            var allNodes = new List<NodeControl>();
            foreach (var child in MainCanvas.Children)
            {
                if (child is NodeControl node)
                    allNodes.Add(node);
            }
            if (allNodes.Count == 0)
            {
                MessageBox.Show("저장할 노드가 없습니다.");
                return null;
            }

            // Find START node and build sequence based on connections
            var startNode = allNodes.FirstOrDefault(n => n.NodeType == "START");
            var nodes = new List<NodeControl>();
            if (startNode != null)
            {
                var current = startNode;
                var visited = new HashSet<NodeControl>();
                while (current != null && !visited.Contains(current))
                {
                    visited.Add(current);
                    nodes.Add(current);
                    // Find next node via output connection
                    var nextConn = _connections.FirstOrDefault(c => c.From?.Node == current);
                    current = nextConn?.To?.Node;
                }
                // Add any unconnected nodes at the end
                foreach (var node in allNodes.Where(n => !visited.Contains(n)))
                {
                    nodes.Add(node);
                }
            }
            else
            {
                // No START, use canvas order
                nodes = allNodes;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                string id = $"node_{idx:00}";
                idx++;
                string originalType = (node.NodeType ?? "").ToUpperInvariant();
                string type = originalType switch
                {
                    "ABS MOVE" => "ABS MOVE",
                    "WAIT" => "WAIT",
                    "IO" => "IO",
                    "SET DO" => "IO",
                    "CHECK DI" => "IO",
                    "FLOW" => "FLOW",
                    "GOTO" => "FLOW",
                    "JUMP" => "GOTO",
                    "START" => "START",
                    "END" => "END",
                    "LINEAR MOVE" => "LINEAR_MOVE",
                    "LINEAR_MOVE" => "LINEAR_MOVE",
                    "REL_MOVE" => "REL_MOVE",
                    "REL MOVE" => "REL_MOVE",  // 호환성
                    "CIRCULAR_MOVE" => "CIRCULAR_MOVE",
                    "COUNTER" => "COUNTER",
                    "SYSTEM" => "SYSTEM",
                    _ => originalType
                };

                object paramsObj = new { };
                if (!string.IsNullOrEmpty(node.JsonData))
                {
                        try
                        {
                            var doc = System.Text.Json.JsonDocument.Parse(node.JsonData);
                            var root = doc.RootElement;

                            static string GetPropString(System.Text.Json.JsonElement el, string name, string def)
                            {
                                if (!el.TryGetProperty(name, out var p)) return def;
                                if (p.ValueKind == System.Text.Json.JsonValueKind.String)
                                    return p.GetString() ?? def;
                                var raw = p.GetRawText();
                                return string.IsNullOrWhiteSpace(raw) ? def : raw.Trim('"');
                            }

                            static double GetPropDouble(System.Text.Json.JsonElement el, string name, double def)
                            {
                                if (!el.TryGetProperty(name, out var p)) return def;
                                if (p.ValueKind == System.Text.Json.JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
                                if (p.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(p.GetString(), out var d2)) return d2;
                                return def;
                            }

                            if (type == "ABS MOVE")
                            {
                                string axis = GetPropString(root, "axis", "X");
                                double pos = 0;
                                double speed = 0;
                                
                                // Handle target_position which could be an object with "value" field
                                if (root.TryGetProperty("target_position", out var tpProp))
                                {
                                    if (tpProp.ValueKind == System.Text.Json.JsonValueKind.Object && tpProp.TryGetProperty("value", out var tpVal))
                                    {
                                        pos = tpVal.TryGetDouble(out var tpd) ? tpd : 0;
                                    }
                                    else
                                    {
                                        pos = GetPropDouble(root, "target_position", 0);
                                    }
                                }
                                else
                                {
                                    pos = GetPropDouble(root, "position", GetPropDouble(root, "pos", 0));
                                }
                                
                                // Handle speed which could be an object with "value" field
                                if (root.TryGetProperty("speed", out var speedProp))
                                {
                                    if (speedProp.ValueKind == System.Text.Json.JsonValueKind.Object && speedProp.TryGetProperty("value", out var speedVal))
                                    {
                                        speed = speedVal.TryGetDouble(out var spd) ? spd : 0;
                                    }
                                    else
                                    {
                                        speed = GetPropDouble(root, "speed", 0);
                                    }
                                }
                                
                                paramsObj = new { axis, pos, speed };
                            }
                            else if (type == "WAIT")
                            {
                                int delay = 0;
                                // Check if delay is an object with value property
                                if (root.TryGetProperty("delay", out var delayProp))
                                {
                                    if (delayProp.ValueKind == System.Text.Json.JsonValueKind.Object && delayProp.TryGetProperty("value", out var valProp))
                                    {
                                        delay = (int)GetPropDouble(delayProp, "value", 0);
                                    }
                                    else
                                    {
                                        delay = (int)GetPropDouble(root, "delay", 0);
                                    }
                                }
                                else
                                {
                                    delay = (int)GetPropDouble(root, "delay_ms", 0);
                                }
                                paramsObj = new { delay_ms = delay };
                            }
                            else if (type == "LINEAR_MOVE")
                            {
                                var target = new { X = 0.0, Y = 0.0, Z = 0.0 };
                                if (root.TryGetProperty("target", out var t))
                                {
                                    double x = GetPropDouble(t, "X", 0);
                                    double y = GetPropDouble(t, "Y", 0);
                                    double z = GetPropDouble(t, "Z", 0);
                                    target = new { X = x, Y = y, Z = z };
                                }
                                double speed = GetPropDouble(root, "speed", 0);
                                paramsObj = new { target, speed };
                            }
                            else if (type == "GOTO")
                            {
                                string to = GetPropString(root, "to", "");
                                string description = GetPropString(root, "description", "");
                                paramsObj = new { to, description };
                            }
                            else if (originalType == "GOTO" || originalType == "JUMP")
                            {
                                string to = GetPropString(root, "to", "");
                                string description = GetPropString(root, "description", "");
                                paramsObj = new { to, description };
                            }
                            else if (type == "REL_MOVE")
                            {
                                string axis = GetPropString(root, "axis", "X");
                                double distance = 0, speed = 0;
                                
                                if (root.TryGetProperty("distance", out var distProp))
                                {
                                    if (distProp.ValueKind == System.Text.Json.JsonValueKind.Object && distProp.TryGetProperty("value", out var distVal))
                                    {
                                        distance = distVal.TryGetDouble(out var d) ? d : 0;
                                    }
                                    else
                                    {
                                        distance = GetPropDouble(root, "distance", 0);
                                    }
                                }
                                
                                if (root.TryGetProperty("speed", out var speedProp))
                                {
                                    if (speedProp.ValueKind == System.Text.Json.JsonValueKind.Object && speedProp.TryGetProperty("value", out var speedVal))
                                    {
                                        speed = speedVal.TryGetDouble(out var s) ? s : 0;
                                    }
                                    else
                                    {
                                        speed = GetPropDouble(root, "speed", 0);
                                    }
                                }
                                
                                paramsObj = new { axis, distance, speed };
                            }
                            else if (type == "CIRCULAR_MOVE")
                            {
                                var pass = new { X = 0.0, Y = 0.0 };
                                var end = new { X = 0.0, Y = 0.0 };
                                
                                if (root.TryGetProperty("pass", out var passProp))
                                {
                                    double px = GetPropDouble(passProp, "X", 0);
                                    double py = GetPropDouble(passProp, "Y", 0);
                                    pass = new { X = px, Y = py };
                                }
                                
                                if (root.TryGetProperty("end", out var endProp))
                                {
                                    double ex = GetPropDouble(endProp, "X", 0);
                                    double ey = GetPropDouble(endProp, "Y", 0);
                                    end = new { X = ex, Y = ey };
                                }
                                
                                string direction = GetPropString(root, "direction", "CW");
                                double speed = GetPropDouble(root, "speed", 0);
                                string plane = GetPropString(root, "plane", "XY");
                                paramsObj = new { pass, end, direction, speed, plane };
                            }
                            else if (type == "COUNTER")
                            {
                                string name = GetPropString(root, "name", "");
                                int initial = (int)GetPropDouble(root, "initial", 0);
                                int target = (int)GetPropDouble(root, "target", 0);
                                int increment = (int)GetPropDouble(root, "increment", 1);
                                string gotoNode = GetPropString(root, "gotoNode", "");
                                paramsObj = new { name, initial, target, increment, gotoNode };
                            }
                            else
                            {
                                if (root.TryGetProperty("params", out var p))
                                {
                                    paramsObj = System.Text.Json.JsonSerializer.Deserialize<object>(p.GetRawText()) ?? new { };
                                }
                            }
                        }
                        catch { }
                }

                string? next = (i < nodes.Count - 1) ? $"node_{(i + 2):00}" : null;
                var step = new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["type"] = type,
                    ["params"] = paramsObj,
                    ["next"] = next
                };
                steps.Add(step);
            }

            var rootObj = new Dictionary<string, object>
            {
                ["sequence_name"] = "Sequence",
                ["steps"] = steps
            };

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string outJson = System.Text.Json.JsonSerializer.Serialize(rootObj, options);
            return outJson;
        }

        private void RunSequence_Click(object sender, RoutedEventArgs e)
        {
            var allNodes = new List<NodeControl>();
            foreach (var child in MainCanvas.Children)
            {
                if (child is NodeControl node)
                    allNodes.Add(node);
            }
            if (allNodes.Count == 0)
            {
                MessageBox.Show("No nodes to execute.");
                return;
            }

            var warnings = new List<string>();
            var errors = new List<string>();
            var nodeTypeCount = new Dictionary<string, int>();
            var output = new System.Text.StringBuilder();

            output.AppendLine(new string('=', 65));
            output.AppendLine("    ETHERCAT STUDIO - ENHANCED SEQUENCE DEBUG OUTPUT");
            output.AppendLine(new string('=', 65) + "\n");

            // Find START node and build sequence based on connections
            var startNode = allNodes.FirstOrDefault(n => n.NodeType == "START");
            var nodes = new List<NodeControl>();
            if (startNode != null)
            {
                var current = startNode;
                var visited = new HashSet<NodeControl>();
                while (current != null && !visited.Contains(current))
                {
                    visited.Add(current);
                    nodes.Add(current);
                    var nextConn = _connections.FirstOrDefault(c => c.From?.Node == current);
                    current = nextConn?.To?.Node;
                }
                // Add any unconnected nodes
                foreach (var node in allNodes.Where(n => !visited.Contains(n)))
                {
                    nodes.Add(node);
                    errors.Add($"Unconnected node: {node.Label.Text} ({node.NodeType})");
                }
            }
            else
            {
                nodes = allNodes;
                warnings.Add("No START node found - using canvas order");
            }

            output.AppendLine($"[INFO] Total Nodes: {allNodes.Count}");
            output.AppendLine();

            // Process each node
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                string nodeId = $"node_{(i + 1):00}";
                string originalType = (node.NodeType ?? "").ToUpperInvariant();
                string type = originalType switch
                {
                    "ABS MOVE" => "ABS MOVE",
                    "WAIT" => "WAIT",
                    "IO" => "IO",
                    "SET DO" => "IO",
                    "CHECK DI" => "IO",
                    "FLOW" => "FLOW",
                    "GOTO" => "FLOW",
                    "JUMP" => "GOTO",
                    "START" => "START",
                    "END" => "END",
                    "LINEAR MOVE" => "LINEAR_MOVE",
                    "SYSTEM" => "SYSTEM",
                    _ => originalType
                };

                // Count node types
                if (!nodeTypeCount.ContainsKey(type)) nodeTypeCount[type] = 0;
                nodeTypeCount[type]++;

                string nextId = (i < nodes.Count - 1) ? $"node_{(i + 2):00}" : "NONE";
                var nextConn = _connections.FirstOrDefault(c => c.From?.Node == node);
                bool hasConnection = nextConn != null;

                output.AppendLine($"[{nodeId}] {type}");
                output.AppendLine($"  Label: {node.Label.Text}");
                output.AppendLine($"  Original Template: {originalType}");
                output.AppendLine($"  Has JsonData: {(!string.IsNullOrEmpty(node.JsonData) ? "YES" : "NO")}");
                if (!string.IsNullOrEmpty(node.JsonData))
                {
                    output.AppendLine($"  JsonData Length: {node.JsonData.Length} chars");
                    output.AppendLine($"  JsonData Content: {node.JsonData}");
                }
                output.AppendLine($"  Connected to Next: {(hasConnection ? "YES (" + nextId + ")" : "NO")}");

                // Parse and display parameters
                object paramsObj = new { };
                string paramStr = "";
                if (!string.IsNullOrEmpty(node.JsonData))
                {
                    try
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(node.JsonData);
                        var root = doc.RootElement;

                        static string GetPropString(System.Text.Json.JsonElement el, string name, string def)
                        {
                            if (!el.TryGetProperty(name, out var p)) return def;
                            if (p.ValueKind == System.Text.Json.JsonValueKind.String)
                                return p.GetString() ?? def;
                            var raw = p.GetRawText();
                            return string.IsNullOrWhiteSpace(raw) ? def : raw.Trim('"');
                        }

                        static double GetPropDouble(System.Text.Json.JsonElement el, string name, double def)
                        {
                            if (!el.TryGetProperty(name, out var p)) return def;
                            if (p.ValueKind == System.Text.Json.JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
                            if (p.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(p.GetString(), out var d2)) return d2;
                            return def;
                        }

                        var paramPairs = new List<(string key, object value)>();

                        if (type == "ABS MOVE")
                        {
                            string axis = GetPropString(root, "axis", "");
                            double pos = 0;
                            double speed = 0;
                            
                            // Handle target_position which could be an object with "value" field
                            if (root.TryGetProperty("target_position", out var tpProp))
                            {
                                if (tpProp.ValueKind == System.Text.Json.JsonValueKind.Object && tpProp.TryGetProperty("value", out var tpVal))
                                {
                                    pos = tpVal.TryGetDouble(out var tpd) ? tpd : 0;
                                }
                                else
                                {
                                    pos = GetPropDouble(root, "target_position", 0);
                                }
                            }
                            else
                            {
                                // Check if position exists and is an object with "value" field
                                if (root.TryGetProperty("position", out var posProp))
                                {
                                    if (posProp.ValueKind == System.Text.Json.JsonValueKind.Object && posProp.TryGetProperty("value", out var posVal))
                                    {
                                        pos = posVal.TryGetDouble(out var posd) ? posd : 0;
                                    }
                                    else
                                    {
                                        pos = GetPropDouble(root, "position", GetPropDouble(root, "pos", 0));
                                    }
                                }
                                else
                                {
                                    pos = GetPropDouble(root, "pos", 0);
                                }
                            }
                            
                            // Handle speed which could be an object with "value" field
                            if (root.TryGetProperty("speed", out var speedProp))
                            {
                                if (speedProp.ValueKind == System.Text.Json.JsonValueKind.Object && speedProp.TryGetProperty("value", out var speedVal))
                                {
                                    speed = speedVal.TryGetDouble(out var spd) ? spd : 0;
                                }
                                else
                                {
                                    speed = GetPropDouble(root, "speed", 0);
                                }
                            }
                            
                            if (string.IsNullOrEmpty(axis)) warnings.Add($"ABS MOVE {nodeId}: Missing 'axis'");
                            if (pos == 0) warnings.Add($"ABS MOVE {nodeId}: position is 0");
                            if (speed == 0) warnings.Add($"ABS MOVE {nodeId}: speed is 0");
                            
                            paramPairs.Add(("axis", axis));
                            paramPairs.Add(("position", pos));
                            paramPairs.Add(("speed", speed));
                            paramsObj = new { axis, pos, speed };
                        }
                        else if (type == "WAIT")
                        {
                            int delay = 0;
                            if (root.TryGetProperty("delay", out var delayProp))
                            {
                                if (delayProp.ValueKind == System.Text.Json.JsonValueKind.Object && delayProp.TryGetProperty("value", out var valProp))
                                {
                                    delay = (int)GetPropDouble(delayProp, "value", 0);
                                }
                                else
                                {
                                    delay = (int)GetPropDouble(root, "delay", 0);
                                }
                            }
                            else
                            {
                                delay = (int)GetPropDouble(root, "delay_ms", 0);
                            }
                            
                            if (delay == 0) warnings.Add($"WAIT {nodeId}: delay is 0");
                            
                            paramPairs.Add(("delay_ms", delay));
                            paramsObj = new { delay_ms = delay };
                        }
                        else if (type == "LINEAR_MOVE")
                        {
                            var target = new { X = 0.0, Y = 0.0, Z = 0.0 };
                            if (root.TryGetProperty("target", out var t))
                            {
                                double x = GetPropDouble(t, "X", 0);
                                double y = GetPropDouble(t, "Y", 0);
                                double z = GetPropDouble(t, "Z", 0);
                                target = new { X = x, Y = y, Z = z };
                            }
                            double speed = GetPropDouble(root, "speed", 0);
                            
                            paramPairs.Add(("target.X", target.X));
                            paramPairs.Add(("target.Y", target.Y));
                            paramPairs.Add(("target.Z", target.Z));
                            paramPairs.Add(("speed", speed));
                            paramsObj = new { target, speed };
                        }
                        else if (originalType == "GOTO" || originalType == "JUMP")
                        {
                            string to = GetPropString(root, "to", "");
                            string description = GetPropString(root, "description", "");
                            
                            if (string.IsNullOrEmpty(to)) errors.Add($"{type} {nodeId}: Missing 'to' target");
                            
                            paramPairs.Add(("to", to));
                            paramPairs.Add(("description", description));
                            paramsObj = new { to, description };
                        }
                        else if (originalType == "REL_MOVE")
                        {
                            string axis = GetPropString(root, "axis", "");
                            double distance = 0, speed = 0;
                            
                            if (root.TryGetProperty("distance", out var distProp))
                            {
                                if (distProp.ValueKind == System.Text.Json.JsonValueKind.Object && distProp.TryGetProperty("value", out var distVal))
                                {
                                    distance = distVal.TryGetDouble(out var d) ? d : 0;
                                }
                                else
                                {
                                    distance = GetPropDouble(root, "distance", 0);
                                }
                            }
                            
                            if (root.TryGetProperty("speed", out var speedProp))
                            {
                                if (speedProp.ValueKind == System.Text.Json.JsonValueKind.Object && speedProp.TryGetProperty("value", out var speedVal))
                                {
                                    speed = speedVal.TryGetDouble(out var s) ? s : 0;
                                }
                                else
                                {
                                    speed = GetPropDouble(root, "speed", 0);
                                }
                            }
                            
                            paramPairs.Add(("axis", axis));
                            paramPairs.Add(("distance", distance));
                            paramPairs.Add(("speed", speed));
                            paramsObj = new { axis, distance, speed };
                        }
                        else if (originalType == "CIRCULAR_MOVE")
                        {
                            var pass = new { X = 0.0, Y = 0.0 };
                            var end = new { X = 0.0, Y = 0.0 };
                            string direction = "";
                            double speed = 0;
                            string plane = "XY";
                            
                            if (root.TryGetProperty("pass", out var passProp))
                            {
                                double px = GetPropDouble(passProp, "X", 0);
                                double py = GetPropDouble(passProp, "Y", 0);
                                pass = new { X = px, Y = py };
                            }

                            if (root.TryGetProperty("end", out var endProp))
                            {
                                double ex = GetPropDouble(endProp, "X", 0);
                                double ey = GetPropDouble(endProp, "Y", 0);
                                end = new { X = ex, Y = ey };
                            }
                            
                            direction = GetPropString(root, "direction", "CW");
                            speed = GetPropDouble(root, "speed", 0);
                            plane = GetPropString(root, "plane", "XY");
                            
                            paramPairs.Add(("pass.X", pass.X));
                            paramPairs.Add(("pass.Y", pass.Y));
                            paramPairs.Add(("end.X", end.X));
                            paramPairs.Add(("end.Y", end.Y));
                            paramPairs.Add(("direction", direction));
                            paramPairs.Add(("speed", speed));
                            paramPairs.Add(("plane", plane));
                            paramsObj = new { pass, end, direction, speed, plane };
                        }
                        else if (originalType == "COUNTER")
                        {
                            string name = GetPropString(root, "name", "");
                            int initial = (int)GetPropDouble(root, "initial", 0);
                            int target = (int)GetPropDouble(root, "target", 0);
                            int increment = (int)GetPropDouble(root, "increment", 1);
                            
                            if (string.IsNullOrEmpty(name)) warnings.Add($"COUNTER {nodeId}: Missing counter name");
                            
                            paramPairs.Add(("name", name));
                            paramPairs.Add(("initial", initial));
                            paramPairs.Add(("target", target));
                            paramPairs.Add(("increment", increment));
                            paramsObj = new { name, initial, target, increment };
                        }
                        else if (type == "IO" || type == "START" || type == "END")
                        {
                            if (root.TryGetProperty("params", out var p))
                            {
                                var paramsDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(p.GetRawText());
                                if (paramsDict != null)
                                {
                                    foreach (var kv in paramsDict)
                                    {
                                        paramPairs.Add((kv.Key, kv.Value ?? "null"));
                                    }
                                }
                            }
                        }

                        if (paramPairs.Count > 0)
                        {
                            output.AppendLine($"  Parameters:");
                            foreach (var (key, value) in paramPairs)
                            {
                                output.AppendLine($"    * {key} = {value}");
                            }
                            paramStr = string.Join(", ", paramPairs.Select(p => $"{p.key}={p.value}"));
                        }
                        else
                        {
                            output.AppendLine($"  Parameters: (none)");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{type} {nodeId}: Failed to parse JsonData - {ex.Message}");
                        output.AppendLine($"  [ERROR] parsing JsonData: {ex.Message}");
                    }
                }
                else
                {
                    if (type != "START" && type != "END")
                    {
                        warnings.Add($"{type} {nodeId}: No JsonData - parameters will be default");
                    }
                    output.AppendLine($"  Parameters: (no data saved)");
                }

                output.AppendLine();
            }

            // Print statistics
            output.AppendLine(new string('=', 65));
            output.AppendLine("                         SUMMARY");
            output.AppendLine(new string('=', 65) + "\n");

            output.AppendLine("[STATISTICS]");
            output.AppendLine($"  Total Nodes: {allNodes.Count}");
            foreach (var (type, count) in nodeTypeCount.OrderBy(x => x.Key))
            {
                output.AppendLine($"  {type}: {count}");
            }

            if (warnings.Count > 0)
            {
                output.AppendLine($"\n[WARNING] Count: {warnings.Count}");
                foreach (var w in warnings)
                {
                    output.AppendLine($"  [!] {w}");
                }
            }

            if (errors.Count > 0)
            {
                output.AppendLine($"\n[ERROR] Count: {errors.Count}");
                foreach (var err in errors)
                {
                    output.AppendLine($"  [X] {err}");
                }
            }

            if (warnings.Count == 0 && errors.Count == 0)
            {
                output.AppendLine($"\n[OK] Status: All checks passed!");
            }

            output.AppendLine("\n" + new string('=', 65));

            // 3D 시뮬레이션: 궤적 생성 및 표시
            var simulationData = BuildSimulationData(nodes);
            if (simulationData.Points.Count > 1)
            {
                var simWindow = new SimulationWindow
                {
                    Owner = this
                };
                simWindow.SetSimulationData(simulationData.Points, simulationData.PointStepIndex, simulationData.Steps);
                simWindow.SaveCurrentFileRequested = SaveCurrentOpenedFile;
                simWindow.SaveAsFileRequested = SaveCurrentOpenedFileAs;
                simWindow.StepJsonUpdated += (index, json) =>
                {
                    if (index >= 0 && index < nodes.Count)
                    {
                        nodes[index].ApplyJsonData(json);
                    }
                };
                simWindow.Show();
            }

            // Show debug output in popup window
            var debugWindow = new DebugOutputWindow(output.ToString())
            {
                Owner = this
            };
            debugWindow.ShowDialog();
        }

        private void RunSimulation_Click(object sender, RoutedEventArgs e)
        {
            var allNodes = new List<NodeControl>();
            foreach (var child in MainCanvas.Children)
            {
                if (child is NodeControl node)
                    allNodes.Add(node);
            }
            if (allNodes.Count == 0)
            {
                MessageBox.Show("No nodes to simulate.");
                return;
            }

            // Find START node and build sequence based on connections
            var startNode = allNodes.FirstOrDefault(n => n.NodeType == "START");
            var nodes = new List<NodeControl>();
            if (startNode != null)
            {
                var current = startNode;
                var visited = new HashSet<NodeControl>();
                while (current != null && !visited.Contains(current))
                {
                    visited.Add(current);
                    nodes.Add(current);
                    var nextConn = _connections.FirstOrDefault(c => c.From?.Node == current);
                    current = nextConn?.To?.Node;
                }
                foreach (var node in allNodes.Where(n => !visited.Contains(n)))
                {
                    nodes.Add(node);
                }
            }
            else
            {
                nodes = allNodes;
            }

            var simulationData = BuildSimulationData(nodes);
            if (simulationData.Points.Count > 1)
            {
                var simWindow = new SimulationWindow
                {
                    Owner = this
                };
                simWindow.SetSimulationData(simulationData.Points, simulationData.PointStepIndex, simulationData.Steps);
                simWindow.SaveCurrentFileRequested = SaveCurrentOpenedFile;
                simWindow.SaveAsFileRequested = SaveCurrentOpenedFileAs;
                simWindow.StepJsonUpdated += (index, json) =>
                {
                    if (index >= 0 && index < nodes.Count)
                    {
                        nodes[index].ApplyJsonData(json);
                    }
                };
                simWindow.Show();
            }
            else
            {
                MessageBox.Show("시뮬레이션할 이동 경로가 없습니다.");
            }
        }

        private class SimulationData
        {
            public List<Point3D> Points { get; } = new();
            public List<int> PointStepIndex { get; } = new();
            public List<SimulationWindow.SimulationStep> Steps { get; } = new();
        }

        private SimulationData BuildSimulationData(List<NodeControl> nodes)
        {
            var data = new SimulationData();
            double x = 0, y = 0, z = 0;
            data.Points.Add(new Point3D(x, y, z));
            data.PointStepIndex.Add(-1);

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                string id = node.NodeNumber > 0 ? $"node_{node.NodeNumber:00}" : $"node_{i + 1:00}";
                string type = (node.NodeType ?? string.Empty).ToUpperInvariant();
                string json = string.IsNullOrWhiteSpace(node.JsonData) ? "{}" : node.JsonData;
                data.Steps.Add(new SimulationWindow.SimulationStep { Id = id, Type = type, Json = json });
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var type = (node.NodeType ?? string.Empty).ToUpperInvariant();
                if (type == "LINEAR MOVE") type = "LINEAR_MOVE";
                if (type == "REL MOVE") type = "REL_MOVE";

                if (string.IsNullOrWhiteSpace(node.JsonData))
                {
                    continue;
                }

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(node.JsonData);
                    var root = doc.RootElement;

                    static double GetNumber(System.Text.Json.JsonElement el)
                    {
                        if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetDouble(out var d)) return d;
                        if (el.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(el.GetString(), out var s)) return s;
                        if (el.ValueKind == System.Text.Json.JsonValueKind.Object && el.TryGetProperty("value", out var v)) return GetNumber(v);
                        return 0;
                    }

                    static double GetPropNumber(System.Text.Json.JsonElement el, params string[] names)
                    {
                        foreach (var name in names)
                        {
                            if (el.TryGetProperty(name, out var p))
                            {
                                return GetNumber(p);
                            }
                        }
                        return 0;
                    }

                    static string GetPropString(System.Text.Json.JsonElement el, string name, string def)
                    {
                        if (!el.TryGetProperty(name, out var p)) return def;
                        if (p.ValueKind == System.Text.Json.JsonValueKind.String) return p.GetString() ?? def;
                        var raw = p.GetRawText();
                        return string.IsNullOrWhiteSpace(raw) ? def : raw.Trim('"');
                    }

                    if (type == "ABS MOVE")
                    {
                        string axis = GetPropString(root, "axis", "X").ToUpperInvariant();
                        double pos = GetPropNumber(root, "target_position", "position", "pos");

                        if (axis.Contains("X")) x = pos;
                        else if (axis.Contains("Y")) y = pos;
                        else if (axis.Contains("Z")) z = pos;

                        data.Points.Add(new Point3D(x, y, z));
                        data.PointStepIndex.Add(i);
                    }
                    else if (type == "REL_MOVE")
                    {
                        string axis = GetPropString(root, "axis", "X").ToUpperInvariant();
                        double dist = GetPropNumber(root, "distance");

                        if (axis.Contains("X")) x += dist;
                        else if (axis.Contains("Y")) y += dist;
                        else if (axis.Contains("Z")) z += dist;

                        data.Points.Add(new Point3D(x, y, z));
                        data.PointStepIndex.Add(i);
                    }
                    else if (type == "LINEAR_MOVE")
                    {
                        if (root.TryGetProperty("target", out var t))
                        {
                            x = GetPropNumber(t, "X");
                            y = GetPropNumber(t, "Y");
                            z = GetPropNumber(t, "Z");
                            data.Points.Add(new Point3D(x, y, z));
                            data.PointStepIndex.Add(i);
                        }
                    }
                    else if (type == "CIRCULAR_MOVE")
                    {
                        string dir = GetPropString(root, "direction", "CW").ToUpperInvariant();
                        string plane = GetPropString(root, "plane", "XY").ToUpperInvariant();

                        bool hasPass = false;
                        bool hasEnd = false;
                        double passU = 0, passV = 0;
                        double endU = 0, endV = 0;

                        if (root.TryGetProperty("pass", out var pass))
                        {
                            if (plane == "XZ")
                            {
                                passU = GetPropNumber(pass, "X");
                                passV = GetPropNumber(pass, "Z", "Y");
                            }
                            else if (plane == "YZ")
                            {
                                passU = GetPropNumber(pass, "Y", "X");
                                passV = GetPropNumber(pass, "Z", "Y");
                            }
                            else
                            {
                                passU = GetPropNumber(pass, "X");
                                passV = GetPropNumber(pass, "Y");
                            }
                            hasPass = true;
                        }

                        if (root.TryGetProperty("end", out var end))
                        {
                            if (plane == "XZ")
                            {
                                endU = GetPropNumber(end, "X");
                                endV = GetPropNumber(end, "Z", "Y");
                            }
                            else if (plane == "YZ")
                            {
                                endU = GetPropNumber(end, "Y", "X");
                                endV = GetPropNumber(end, "Z", "Y");
                            }
                            else
                            {
                                endU = GetPropNumber(end, "X");
                                endV = GetPropNumber(end, "Y");
                            }
                            hasEnd = true;
                        }

                        if (hasPass && hasEnd)
                        {
                            double startU, startV;
                            if (plane == "XZ")
                            {
                                startU = x; startV = z;
                            }
                            else if (plane == "YZ")
                            {
                                startU = y; startV = z;
                            }
                            else
                            {
                                startU = x; startV = y;
                            }

                            if (!TryComputeCircleCenter2D(startU, startV, passU, passV, endU, endV, out var centerU, out var centerV))
                            {
                                if (plane == "XZ")
                                {
                                    x = endU; z = endV;
                                }
                                else if (plane == "YZ")
                                {
                                    y = endU; z = endV;
                                }
                                else
                                {
                                    x = endU; y = endV;
                                }
                                data.Points.Add(new Point3D(x, y, z));
                                data.PointStepIndex.Add(i);
                                continue;
                            }

                            double startAngle = Math.Atan2(startV - centerV, startU - centerU);
                            double passAngle = Math.Atan2(passV - centerV, passU - centerU);
                            double endAngle = Math.Atan2(endV - centerV, endU - centerU);

                            double twoPi = Math.PI * 2;
                            double NormalizeAngle(double a)
                            {
                                a %= twoPi;
                                if (a < 0) a += twoPi;
                                return a;
                            }

                            if (dir == "CW")
                            {
                                double s = NormalizeAngle(startAngle);
                                double p = NormalizeAngle(passAngle);
                                double e = NormalizeAngle(endAngle);

                                if (e > s) e -= twoPi;
                                if (p > s) p -= twoPi;
                                if (p < e) e -= twoPi;

                                startAngle = s;
                                endAngle = e;
                            }
                            else
                            {
                                double s = NormalizeAngle(startAngle);
                                double p = NormalizeAngle(passAngle);
                                double e = NormalizeAngle(endAngle);

                                if (e < s) e += twoPi;
                                if (p < s) p += twoPi;
                                if (p > e) e += twoPi;

                                startAngle = s;
                                endAngle = e;
                            }

                            double radius = Math.Sqrt(Math.Pow(startU - centerU, 2) + Math.Pow(startV - centerV, 2));
                            if (radius > 0.0001)
                            {
                                int steps = 30;
                                for (int j = 1; j <= steps; j++)
                                {
                                    double t = j / (double)steps;
                                    double a = startAngle + (endAngle - startAngle) * t;
                                    double u = centerU + radius * Math.Cos(a);
                                    double v = centerV + radius * Math.Sin(a);

                                    if (plane == "XZ")
                                    {
                                        data.Points.Add(new Point3D(u, y, v));
                                    }
                                    else if (plane == "YZ")
                                    {
                                        data.Points.Add(new Point3D(x, u, v));
                                    }
                                    else
                                    {
                                        data.Points.Add(new Point3D(u, v, z));
                                    }
                                    data.PointStepIndex.Add(i);
                                }
                            }

                            if (plane == "XZ")
                            {
                                x = endU; z = endV;
                            }
                            else if (plane == "YZ")
                            {
                                y = endU; z = endV;
                            }
                            else
                            {
                                x = endU; y = endV;
                            }
                        }
                        else if (root.TryGetProperty("center", out var c))
                        {
                            double cx = GetPropNumber(c, "X");
                            double cy = GetPropNumber(c, "Y");
                            double cz = GetPropNumber(c, "Z");

                            double angleDeg = GetPropNumber(root, "angle", "angle_deg");
                            double radiusParam = GetPropNumber(root, "radius");

                            double startU, startV, centerU, centerV;
                            double? legacyEndU = null, legacyEndV = null;
                            if (plane == "XZ")
                            {
                                startU = x; startV = z;
                                centerU = cx; centerV = cz;
                            }
                            else if (plane == "YZ")
                            {
                                startU = y; startV = z;
                                centerU = cy; centerV = cz;
                            }
                            else
                            {
                                startU = x; startV = y;
                                centerU = cx; centerV = cy;
                            }

                            if (root.TryGetProperty("end", out var legacyEnd))
                            {
                                if (plane == "XZ")
                                {
                                    legacyEndU = GetPropNumber(legacyEnd, "X");
                                    legacyEndV = GetPropNumber(legacyEnd, "Z");
                                }
                                else if (plane == "YZ")
                                {
                                    legacyEndU = GetPropNumber(legacyEnd, "Y");
                                    legacyEndV = GetPropNumber(legacyEnd, "Z");
                                }
                                else
                                {
                                    legacyEndU = GetPropNumber(legacyEnd, "X");
                                    legacyEndV = GetPropNumber(legacyEnd, "Y");
                                }
                            }

                            double startAngle = Math.Atan2(startV - centerV, startU - centerU);
                            double radius = Math.Sqrt(Math.Pow(startU - centerU, 2) + Math.Pow(startV - centerV, 2));

                            if (radius <= 0.0001)
                            {
                                if (radiusParam > 0)
                                {
                                    radius = radiusParam;
                                }
                                else if (legacyEndU.HasValue && legacyEndV.HasValue)
                                {
                                    radius = Math.Sqrt(Math.Pow(legacyEndU.Value - centerU, 2) + Math.Pow(legacyEndV.Value - centerV, 2));
                                }
                                else
                                {
                                    continue;
                                }
                            }

                            double endAngle;
                            if (Math.Abs(angleDeg) > 0.0001)
                            {
                                double angleRad = angleDeg * Math.PI / 180.0;
                                endAngle = dir == "CW" ? startAngle - Math.Abs(angleRad) : startAngle + Math.Abs(angleRad);
                            }
                            else if (legacyEndU.HasValue && legacyEndV.HasValue)
                            {
                                endAngle = Math.Atan2(legacyEndV.Value - centerV, legacyEndU.Value - centerU);
                            }
                            else
                            {
                                continue;
                            }

                            if (radius > 0.0001)
                            {
                                if (dir == "CW")
                                {
                                    if (endAngle > startAngle) endAngle -= Math.PI * 2;
                                }
                                else
                                {
                                    if (endAngle < startAngle) endAngle += Math.PI * 2;
                                }

                                int steps = 30;
                                for (int j = 1; j <= steps; j++)
                                {
                                    double t = j / (double)steps;
                                    double a = startAngle + (endAngle - startAngle) * t;
                                    double u = centerU + radius * Math.Cos(a);
                                    double v = centerV + radius * Math.Sin(a);

                                    if (plane == "XZ")
                                    {
                                        data.Points.Add(new Point3D(u, y, v));
                                    }
                                    else if (plane == "YZ")
                                    {
                                        data.Points.Add(new Point3D(x, u, v));
                                    }
                                    else
                                    {
                                        data.Points.Add(new Point3D(u, v, z));
                                    }
                                    data.PointStepIndex.Add(i);
                                }
                            }

                            if (plane == "XZ")
                            {
                                x = centerU + radius * Math.Cos(endAngle);
                                z = centerV + radius * Math.Sin(endAngle);
                            }
                            else if (plane == "YZ")
                            {
                                y = centerU + radius * Math.Cos(endAngle);
                                z = centerV + radius * Math.Sin(endAngle);
                            }
                            else
                            {
                                x = centerU + radius * Math.Cos(endAngle);
                                y = centerV + radius * Math.Sin(endAngle);
                            }
                        }
                    }
                }
                catch
                {
                    // ignore parse errors for simulation path
                }
            }

            return data;
        }

        private static bool TryComputeCircleCenter2D(
            double x1, double y1,
            double x2, double y2,
            double x3, double y3,
            out double cx, out double cy)
        {
            double d = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2));
            if (Math.Abs(d) < 1e-8)
            {
                cx = 0;
                cy = 0;
                return false;
            }

            double x1sq = x1 * x1 + y1 * y1;
            double x2sq = x2 * x2 + y2 * y2;
            double x3sq = x3 * x3 + y3 * y3;

            cx = (x1sq * (y2 - y3) + x2sq * (y3 - y1) + x3sq * (y1 - y2)) / d;
            cy = (x1sq * (x3 - x2) + x2sq * (x1 - x3) + x3sq * (x2 - x1)) / d;

            return true;
        }

        // =============== AI SEQUENCE GENERATION ===============
        private string _ollamaBaseUrl = "http://localhost:11434";

        private async void MenuItem_AIGenerate_Click(object sender, RoutedEventArgs e)
        {
            var promptWindow = new Window
            {
                Title = "AI 프롬프트로 시퀀스 생성",
                Width = 600,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45))
            };

            var promptControl = new AIPromptWindow
            {
                OllamaBaseUrl = _ollamaBaseUrl
            };
            promptWindow.Content = promptControl;

            promptWindow.ShowDialog();

            if (promptControl.IsConfirmed && !string.IsNullOrEmpty(promptControl.GeneratedJson))
            {
                try
                {
                    // JSON 프리뷰 창 띄우기
                    var previewWindow = new JSONPreviewWindow(promptControl.GeneratedJson)
                    {
                        Owner = this
                    };
                    previewWindow.ShowDialog();

                    // 사용자 선택에 따라 처리
                    if (previewWindow.IsConverted)
                    {
                        LoadSequenceJsonIntoCanvas(promptControl.GeneratedJson);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"처리 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadSequenceJsonIntoCanvas(string jsonStr)
        {
            // 기존 노드와 연결선 모두 삭제
            MainCanvas.Children.Clear();
            _connections.Clear();
            _selectedNode = null;
            _selectedConnection = null;

            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            if (root.TryGetProperty("steps", out var stepsArray))
            {
                var nodeMap = new Dictionary<string, NodeControl>();
                var nodes = new List<(int index, NodeControl node, string? id)>();
                int nodeCount = 0;
                int yOffset = 50;

                // 첫 번째 패스: 모든 노드 생성
                foreach (var stepData in stepsArray.EnumerateArray())
                {
                    try
                    {
                        string type = stepData.TryGetProperty("type", out var typeEl) 
                            ? typeEl.GetString() ?? "ABS MOVE" 
                            : "ABS MOVE";
                        
                        string? stepId = stepData.TryGetProperty("id", out var idEl)
                            ? idEl.GetString()
                            : null;
                        
                        var paramData = stepData.TryGetProperty("params", out var paramsEl) 
                            ? paramsEl 
                            : stepData;
                        string jsonDataStr = paramData.GetRawText();

                        // 노드 타입에 맞는 색상 결정
                        Brush color = type switch
                        {
                            "ABS MOVE" => Brushes.CornflowerBlue,
                            "REL MOVE" => new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),
                            "WAIT" => Brushes.Orange,
                            "SET_DO" => Brushes.LimeGreen,
                            "CHECK_DI" => Brushes.Gold,
                            "GOTO" => Brushes.IndianRed,
                            "JUMP" => Brushes.MediumPurple,
                            "START" => Brushes.LimeGreen,
                            "END" => Brushes.Red,
                            "LINEAR_MOVE" => Brushes.Blue,
                            "CIRCULAR_MOVE" => new SolidColorBrush(Color.FromRgb(0x5E, 0x35, 0xB1)),
                            "COUNTER" => new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC)),
                            _ => Brushes.Gray
                        };

                        bool hasInput = type != "START";
                        bool hasOutput = type != "END";

                        var node = new NodeControl(type, color, this, 16, 140, 48, hasInput, hasOutput, type)
                        {
                            JsonData = jsonDataStr
                        };
                        node.Label.Text = type;
                        node.SetNodeNumber(nodeCount + 1);

                        // 자동 배치 (위에서 아래로, 열 3개)
                        Canvas.SetLeft(node, 100 + (nodeCount % 3) * 220);
                        Canvas.SetTop(node, yOffset + (nodeCount / 3) * 150);

                        MainCanvas.Children.Add(node);
                        nodes.Add((nodeCount, node, stepId));
                        if (!string.IsNullOrEmpty(stepId))
                        {
                            nodeMap[stepId] = node;
                        }
                        nodeCount++;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"노드 파싱 오류: {ex.Message}\n\n{stepData.GetRawText()}");
                    }
                }

                // 두 번째 패스: 순차적으로 와이어 연결
                for (int i = 0; i < nodes.Count - 1; i++)
                {
                    var fromNode = nodes[i].node;
                    var toNode = nodes[i + 1].node;

                    if (fromNode.OutputPort != null && toNode.InputPort != null)
                    {
                        // 와이어 자동 연결
                        ConnectNodes(fromNode, toNode);
                    }
                }

                MessageBox.Show($"✓ {nodeCount}개의 노드가 생성되었습니다! ({nodeCount - 1}개 와이어 연결됨)", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("JSON에 'steps' 배열이 없습니다.\n\n원본:\n" + jsonStr);
            }
        }

        private void ConnectNodes(NodeControl fromNode, NodeControl toNode)
        {
            if (fromNode.OutputPort == null || toNode.InputPort == null)
                return;

            var from = new PortInfo { Type = PortType.Output, Node = fromNode, Ellipse = fromNode.OutputPort };
            var to = new PortInfo { Type = PortType.Input, Node = toNode, Ellipse = toNode.InputPort };

            var path = new Path
            {
                Stroke = Brushes.White,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 5 }
            };

            var pathData = new PathGeometry();
            var connection = new Connection { From = from, To = to, Path = path };
            _connections.Add(connection);

            path.Data = pathData;
            MainCanvas.Children.Add(path);

            UpdateWire(connection);
        }

        private void UpdateWire(Connection conn)
        {
            if (conn.From?.Ellipse == null || conn.To?.Ellipse == null || conn.Path?.Data is not PathGeometry pg)
                return;

            var from = conn.From.Ellipse;
            var to = conn.To.Ellipse;

            double x1 = Canvas.GetLeft(from) + from.Width / 2;
            double y1 = Canvas.GetTop(from) + from.Height / 2;
            double x2 = Canvas.GetLeft(to) + to.Width / 2;
            double y2 = Canvas.GetTop(to) + to.Height / 2;

            pg.Clear();
            var fig = new PathFigure { StartPoint = new Point(x1, y1) };
            fig.Segments.Add(new LineSegment { Point = new Point(x2, y2) });
            pg.Figures.Add(fig);
        }

        private void MenuItem_OllamaSettings_Click(object sender, RoutedEventArgs e)
        {
            string? result = InputBoxDialog("Ollama 서버 설정", "URL (예: http://localhost:11434):", _ollamaBaseUrl);
            if (!string.IsNullOrEmpty(result))
            {
                _ollamaBaseUrl = result;
                MessageBox.Show($"✓ Ollama 설정 완료: {_ollamaBaseUrl}", "설정됨", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuItem_Exit(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string? InputBoxDialog(string title, string prompt, string defaultValue = "")
        {
            var window = new Window
            {
                Title = title,
                Width = 450,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI")
            };

            var panel = new StackPanel { Margin = new Thickness(15) };

            var lblPrompt = new TextBlock
            {
                Text = prompt,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 12
            };
            panel.Children.Add(lblPrompt);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(8),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11
            };
            panel.Children.Add(textBox);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var okBtn = new Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                Foreground = Brushes.White,
                Padding = new Thickness(8, 4, 8, 4)
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
                Foreground = Brushes.White,
                Padding = new Thickness(8, 4, 8, 4)
            };

            string? result = null;
            okBtn.Click += (s, e) => { result = textBox.Text; window.Close(); };
            cancelBtn.Click += (s, e) => window.Close();

            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(btnPanel);

            window.Content = panel;
            window.ShowDialog();
            return result;
        }
    }
}