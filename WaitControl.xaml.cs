using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Controls;

namespace EtherCAT_Studio
{
    public partial class WaitControl : UserControl
    {
        public WaitControl()
        {
            InitializeComponent();
        }

        public void Load(JsonElement? root)
        {
            if (root == null) return;
            // Support both "delay" and "delay_ms"
            if (root.Value.TryGetProperty("delay", out var d))
            {
                DelayBox.Text = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("value", out var vd) ? vd.GetRawText() : d.GetRawText();
            }
            else if (root.Value.TryGetProperty("delay_ms", out var dm))
            {
                DelayBox.Text = dm.ValueKind == JsonValueKind.Object && dm.TryGetProperty("value", out var vdm) ? vdm.GetRawText() : dm.GetRawText();
            }
        }

        public Dictionary<string, object> Collect()
        {
            double delay = 0;
            double.TryParse(DelayBox.Text, out delay);
            return new Dictionary<string, object>
            {
                ["delay"] = new { value = delay, unit = "Puls" }
            };
        }
    }
}
