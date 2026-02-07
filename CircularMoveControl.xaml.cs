using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Controls;

namespace EtherCAT_Studio
{
    public partial class CircularMoveControl : UserControl
    {
        public CircularMoveControl()
        {
            InitializeComponent();
            DirectionBox.SelectedIndex = 0;
            PlaneBox.SelectedIndex = 0;
        }

        public void Load(JsonElement? root)
        {
            if (root == null) return;
            
            if (root.Value.TryGetProperty("direction", out var dir))
            {
                DirectionBox.Text = dir.GetString() ?? "CW";
            }

            if (root.Value.TryGetProperty("speed", out var speed))
            {
                if (speed.ValueKind == JsonValueKind.Object && speed.TryGetProperty("value", out var sv))
                    SpeedBox.Text = sv.GetRawText();
                else
                    SpeedBox.Text = speed.GetRawText();
            }

            if (root.Value.TryGetProperty("plane", out var plane))
            {
                PlaneBox.Text = plane.GetString() ?? "XY";
            }

            if (root.Value.TryGetProperty("pass", out var pass))
            {
                if (pass.TryGetProperty("X", out var px)) PassXBox.Text = px.GetRawText();
                if (pass.TryGetProperty("Y", out var py)) PassYBox.Text = py.GetRawText();
            }

            if (root.Value.TryGetProperty("end", out var end))
            {
                if (end.TryGetProperty("X", out var ex)) EndXBox.Text = ex.GetRawText();
                if (end.TryGetProperty("Y", out var ey)) EndYBox.Text = ey.GetRawText();
            }
        }

        public Dictionary<string, object> Collect()
        {
            double passX = 0, passY = 0;
            double endX = 0, endY = 0;
            double.TryParse(PassXBox.Text, out passX);
            double.TryParse(PassYBox.Text, out passY);
            double.TryParse(EndXBox.Text, out endX);
            double.TryParse(EndYBox.Text, out endY);
            double speed = 0;
            double.TryParse(SpeedBox.Text, out speed);
            
            string direction = DirectionBox.Text ?? "CW";
            string plane = PlaneBox.Text ?? "XY";
            
            return new Dictionary<string, object>
            {
                ["pass"] = new { X = passX, Y = passY },
                ["end"] = new { X = endX, Y = endY },
                ["direction"] = direction,
                ["speed"] = speed,
                ["plane"] = plane
            };
        }
    }
}
