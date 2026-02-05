using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EtherCAT_Studio
{
    // 노드 컨트롤 (포트 포함)
    public class NodeControl : Canvas
    {
        private Border _backgroundBorder;
        private TextBlock _numberBadge;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                if (_backgroundBorder != null)
                {
                    _backgroundBorder.BorderBrush = _isSelected ? Brushes.Cyan : Brushes.White;
                    _backgroundBorder.BorderThickness = _isSelected ? new Thickness(3) : new Thickness(2);
                }
            }
        }
        public Ellipse? InputPort { get; }
        public Ellipse? OutputPort { get; }
        public TextBlock Label { get; }
        public MainWindow Main { get; }
        bool _isDragging;
        Point _dragOffset;

        public MainWindow.PortInfo InputPortInfo { get; }
        public MainWindow.PortInfo OutputPortInfo { get; }
        public string NodeType { get; }
        public string OriginalLabel { get; } // 노드 타입 식별용 원래 라벨

        public int NodeNumber { get; private set; }

        public string? JsonData { get; set; } // 노드별 JSON 데이터 저장

        public NodeControl(string text, Brush color, MainWindow main, double portSize = 16, double width = 140, double height = 48, bool hasInput = true, bool hasOutput = true, string nodeType = "")
        {
            Main = main;
            NodeType = nodeType;
            OriginalLabel = text; // 원래 라벨 저장
            Width = width; Height = height;

            // 배경
            var border = new Border
            {
                Width = width, Height = height,
                Background = color,
                CornerRadius = new CornerRadius(8),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2)
            };
            _backgroundBorder = border;
            Children.Add(_backgroundBorder);

            // 노드 번호 표시 배지
            _numberBadge = new TextBlock
            {
                Text = string.Empty,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(4, 1, 4, 1)
            };
            SetLeft(_numberBadge, 6);
            SetTop(_numberBadge, 4);
            Children.Add(_numberBadge);

            // 입력 포트 (옵션)
            if (hasInput)
            {
                InputPort = new Ellipse
                {
                    Width = portSize, Height = portSize,
                    Fill = Brushes.Gray,
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5,
                    Cursor = Cursors.Hand
                };
                SetLeft(InputPort, -portSize / 2); SetTop(InputPort, (Height - portSize) / 2);
                Children.Add(InputPort);
            }
            else
            {
                InputPort = null;
            }

            // 출력 포트
            if (hasOutput)
            {
                OutputPort = new Ellipse
                {
                    Width = portSize, Height = portSize,
                    Fill = Brushes.Gray,
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5,
                    Cursor = Cursors.Hand
                };
                SetLeft(OutputPort, Width - (portSize / 2)); SetTop(OutputPort, (Height - portSize) / 2);
                Children.Add(OutputPort);
            }
            else
            {
                OutputPort = null;
            }

            // 라벨
            // centered text inside a Grid so the text baseline is vertically centered
            var contentGrid = new Grid
            {
                Width = width,
                Height = height
            };
            Label = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            contentGrid.Children.Add(Label);
            SetLeft(contentGrid, 0);
            SetTop(contentGrid, 0);
            Children.Add(contentGrid);

            // 노드 타입 (`nodeType` parameter set earlier) - do not overwrite with display text

            // 포트 정보
            InputPortInfo = new MainWindow.PortInfo { Type = MainWindow.PortType.Input, Node = this, Ellipse = InputPort };
            OutputPortInfo = new MainWindow.PortInfo { Type = MainWindow.PortType.Output, Node = this, Ellipse = OutputPort };

            // 포트 하이라이트
            if (InputPort != null)
            {
                InputPort.MouseEnter += (s, e) => InputPort.Fill = Brushes.LimeGreen;
                InputPort.MouseLeave += (s, e) => InputPort.Fill = Brushes.Gray;
            }
            if (OutputPort != null)
            {
                OutputPort.MouseEnter += (s, e) => OutputPort.Fill = Brushes.Orange;
                OutputPort.MouseLeave += (s, e) => OutputPort.Fill = Brushes.Gray;
            }

            // 와이어 드래그 (출력 포트가 있는 경우)
            if (OutputPort != null)
            {
                OutputPort.MouseLeftButtonDown += (s, e) => {
                    Main.StartWireDrag(OutputPortInfo, e);
                    e.Handled = true;
                };
            }

            // 노드 드래그
            MouseLeftButtonDown += Node_MouseLeftButtonDown;
            MouseMove += Node_MouseMove;
            MouseLeftButtonUp += Node_MouseLeftButtonUp;
            // 노드 더블클릭: 속성 입력창
            MouseLeftButtonDown += Node_DoubleClick;
        }

        public void SetNodeNumber(int number)
        {
            NodeNumber = number;
            _numberBadge.Text = number > 0 ? number.ToString() : string.Empty;
        }

        // 더블클릭 시 PropertyWindow 띄우기
        private void Node_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Log NodeType before opening PropertyWindow
                try
                {
                    var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "EtherCAT_Studio");
                    System.IO.Directory.CreateDirectory(dir);
                    string logFilePath = System.IO.Path.Combine(dir, "debug_log.txt");
                    System.IO.File.AppendAllText(logFilePath, $"NodeControl double-click nodeType: {NodeType}\n");
                    System.Diagnostics.Debug.WriteLine($"NodeControl double-click nodeType: {NodeType}");
                }
                catch { }

                var win = new PropertyWindow(JsonData, NodeType, OriginalLabel);
                if (win.ShowDialog() == true && !string.IsNullOrEmpty(win.JsonResult))
                {
                        JsonData = win.JsonResult;
                        // Log saved JsonData
                        try
                        {
                            var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "EtherCAT_Studio");
                            System.IO.Directory.CreateDirectory(dir);
                            string logFilePath = System.IO.Path.Combine(dir, "debug_log.txt");
                            System.IO.File.AppendAllText(logFilePath, $"Saved JsonData for {NodeType}: {JsonData}\n");
                        }
                        catch { }
                        try
                        {
                            var doc = System.Text.Json.JsonDocument.Parse(JsonData);
                            if (doc.RootElement.TryGetProperty("label", out var lbl) && lbl.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                var newLabel = lbl.GetString();
                                if (!string.IsNullOrEmpty(newLabel)) Label.Text = newLabel;
                            }
                        }
                        catch { }
                }
            }
        }

        // 포트의 Canvas 내 실제 위치 반환
        public Point GetPortPosition(MainWindow.PortInfo port, Canvas canvas)
        {
            if (port?.Ellipse == null)
                return new Point(0, 0);
            return port.Ellipse.TranslatePoint(new Point(port.Ellipse.Width / 2, port.Ellipse.Height / 2), canvas);
        }

        // 노드 드래그
            void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
                Main?.SelectNode(this);
            _isDragging = true;
            _dragOffset = e.GetPosition(this);
            CaptureMouse();
        }
        void Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var parentCanvas = Parent as Canvas;
                if (parentCanvas == null) return;
                var pos = e.GetPosition(parentCanvas);
                Canvas.SetLeft(this, pos.X - _dragOffset.X);
                Canvas.SetTop(this, pos.Y - _dragOffset.Y);
                Main?.NodeMoved(this);
            }
        }
        void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }

        // Update visuals after property edit
        public void UpdateVisuals()
        {
            // Update label and optionally color
            // (For now, just update label)
            // If you want to update color, add logic here
        }
    }
}
