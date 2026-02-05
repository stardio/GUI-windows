using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EtherCAT_Studio
{
    public partial class PropertyWindow : Window
    {
        public string? JsonResult { get; private set; }
        private string _nodeType = string.Empty;
        private string _nodeLabel = string.Empty;

        public PropertyWindow(string? initialJson = null, string nodeType = "", string nodeLabel = "")
        {
            InitializeComponent();
            _nodeType = nodeType;
            _nodeLabel = nodeLabel ?? string.Empty;
            System.Diagnostics.Debug.WriteLine($"PropertyWindow initialized with nodeType: {_nodeType}");
            // Log nodeType to a file for debugging (use LocalApplicationData for writable location)
            try
            {
                var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "EtherCAT_Studio");
                System.IO.Directory.CreateDirectory(dir);
                string logFilePath = System.IO.Path.Combine(dir, "debug_log.txt");
                System.IO.File.AppendAllText(logFilePath, $"PropertyWindow initialized with nodeType: {_nodeType}\n");
                System.Diagnostics.Debug.WriteLine($"WROTE LOG: {logFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write debug log: {ex.Message}");
            }

            // node-specific control will be created below and initialized from JSON if provided
            // host appropriate control. For an ABS MOVE node use the specialized AbsMoveControl
            UserControl nodeControl;
            if ("ABS MOVE".Equals(_nodeLabel, System.StringComparison.OrdinalIgnoreCase)
                || "ABS MOVE".Equals(_nodeType, System.StringComparison.OrdinalIgnoreCase))
            {
                nodeControl = new AbsMoveControl();
            }
            else
            {
                nodeControl = (_nodeType ?? "").ToUpperInvariant() switch
                {
                    "ABS MOVE" => new AbsMoveControl(),
                    "LINEAR_MOVE" => new LinearMoveControl(),
                    "REL_MOVE" => new RelMoveControl(),
                    "CIRCULAR_MOVE" => new CircularMoveControl(),
                    "WAIT" => new WaitControl(),
                    "COUNTER" => new CounterControl(),
                    "GOTO" => new GotoControl(),
                    "JUMP" => new GotoControl(),
                    _ => new GenericControl()
                };
            }
            NodeSpecificContent.Content = nodeControl;

            // Debug log which control was loaded for which nodeType
            try
            {
                var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "EtherCAT_Studio");
                System.IO.Directory.CreateDirectory(dir);
                string logFilePath = System.IO.Path.Combine(dir, "debug_log.txt");
                string ctrlName = nodeControl.GetType().Name;
                string entry = $"PropertyWindow nodeType: {_nodeType} -> loaded control: {ctrlName}\n";
                System.IO.File.AppendAllText(logFilePath, entry);
                System.Diagnostics.Debug.WriteLine(entry);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write debug log (load control): {ex.Message}");
            }

            // If we have initial JSON, and this is an ABS MOVE, remove unwanted keys then pass root to control's Load
            if (!string.IsNullOrEmpty(initialJson))
            {
                // Log initial JSON being loaded
                try
                {
                    var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "EtherCAT_Studio");
                    System.IO.Directory.CreateDirectory(dir);
                    string logFilePath = System.IO.Path.Combine(dir, "debug_log.txt");
                    System.IO.File.AppendAllText(logFilePath, $"Loading initialJson for {_nodeType} ({_nodeLabel}): {initialJson}\n");
                }
                catch { }
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(initialJson);
                    var root = doc.RootElement;
                    
                    // AI 생성 JSON은 params 필드에 데이터를 포함하므로 추출
                    JsonElement dataToLoad = root;
                    if (root.TryGetProperty("params", out var paramsEl))
                    {
                        dataToLoad = paramsEl;
                    }
                    
                    bool isAbsMove = ("ABS MOVE".Equals(_nodeType, System.StringComparison.OrdinalIgnoreCase)
                        && "ABS MOVE".Equals(_nodeLabel, System.StringComparison.OrdinalIgnoreCase));
                    if (isAbsMove)
                    {
                        try
                        {
                            var node = JsonNode.Parse(initialJson) as JsonObject;
                            if (node != null)
                            {
                                node.Remove("speed_history");
                                node.Remove("description");
                                // replace initialJson with cleaned JSON for loading
                                initialJson = node.ToJsonString();
                            }
                        }
                        catch { }
                    }

                    // populate common fields (label, comment) so they persist
                    try
                    {
                        if (root.TryGetProperty("label", out var lbl) && lbl.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            LabelBox.Text = lbl.GetString() ?? string.Empty;
                        }
                        else if (dataToLoad.TryGetProperty("label", out var lbl2) && lbl2.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            LabelBox.Text = lbl2.GetString() ?? string.Empty;
                        }
                        
                        if (root.TryGetProperty("jump_target", out var jt) && jt.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            JumpTargetBox.Text = jt.GetString() ?? string.Empty;
                        }
                        else if (dataToLoad.TryGetProperty("jump_target", out var jt2) && jt2.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            JumpTargetBox.Text = jt2.GetString() ?? string.Empty;
                        }
                        else if (root.TryGetProperty("comment", out var com) && com.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            JumpTargetBox.Text = com.GetString() ?? string.Empty;
                        }
                        else if (dataToLoad.TryGetProperty("comment", out var com2) && com2.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            JumpTargetBox.Text = com2.GetString() ?? string.Empty;
                        }
                    }
                    catch { }
                    
                    // call Load if available - params 데이터 사용
                    switch (nodeControl)
                    {
                        case LinearMoveControl lmc:
                            lmc.Load(dataToLoad);
                            break;
                        case RelMoveControl rmc:
                            rmc.Load(dataToLoad);
                            break;
                        case CircularMoveControl cmc:
                            cmc.Load(dataToLoad);
                            break;
                        case AbsMoveControl amc:
                            amc.Load(dataToLoad);
                            break;
                        case WaitControl wc:
                            wc.Load(dataToLoad);
                            break;
                        case CounterControl cc:
                            cc.Load(dataToLoad);
                            break;
                        case GotoControl gc:
                            gc.Load(dataToLoad);
                            break;
                        case GenericControl gn:
                            gn.Load(dataToLoad);
                            break;
                    }
                }
                catch { }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var obj = new Dictionary<string, object>();
            string label = LabelBox.Text ?? string.Empty;
            string comment = JumpTargetBox.Text ?? string.Empty;
            bool isAbsMove = ("ABS MOVE".Equals(_nodeType, System.StringComparison.OrdinalIgnoreCase)
                && "ABS MOVE".Equals(_nodeLabel, System.StringComparison.OrdinalIgnoreCase));
            // Include label normally; for ABS MOVE we'll also emit the label inside the ABS JSON
            if (!string.IsNullOrWhiteSpace(label)) obj["label"] = label;
            if (!string.IsNullOrWhiteSpace(comment)) obj["comment"] = comment;
            // collect node-specific parameters from the hosted control
            var hosted = NodeSpecificContent.Content;
            if (hosted is LinearMoveControl lmc)
            {
                foreach (var kv in lmc.Collect()) obj[kv.Key] = kv.Value;
            }
            else if (hosted is RelMoveControl rmc)
            {
                foreach (var kv in rmc.Collect()) obj[kv.Key] = kv.Value;
            }
            else if (hosted is CircularMoveControl cmc)
            {
                foreach (var kv in cmc.Collect()) obj[kv.Key] = kv.Value;
            }
            else if (hosted is AbsMoveControl amc)
            {
                foreach (var kv in amc.Collect()) obj[kv.Key] = kv.Value;
            }
            else if (hosted is WaitControl wc)
            {
                foreach (var kv in wc.Collect()) obj[kv.Key] = kv.Value;
            }
            else if (hosted is CounterControl cc)
            {
                foreach (var kv in cc.Collect()) obj[kv.Key] = kv.Value;
            }
            else if (hosted is GotoControl gotoc)
            {
                foreach (var kv in gotoc.Collect()) obj[kv.Key] = kv.Value;
            }
            else if (hosted is GenericControl gc2)
            {
                foreach (var kv in gc2.Collect()) obj[kv.Key] = kv.Value;
            }
            // If this is an ABS MOVE node, shape the JSON to only include the four requested fields
            if (isAbsMove && NodeSpecificContent.Content is AbsMoveControl)
            {
                var abs = new Dictionary<string, object>();
                abs["type"] = "ABS MOVE";
                // axis
                if (obj.TryGetValue("axis", out var axis)) abs["axis"] = axis ?? "X";
                // position may be present under either `target_position` (new) or `position` (legacy)
                if (obj.TryGetValue("target_position", out var tpos)) abs["position"] = tpos!;
                else if (obj.TryGetValue("position", out var position)) abs["position"] = position!;
                // speed
                if (obj.TryGetValue("speed", out var speed)) abs["speed"] = speed!;
                // include label/comment inside ABS MOVE JSON if present
                if (obj.TryGetValue("label", out var lbl)) abs["label"] = lbl!;
                if (obj.TryGetValue("comment", out var cmt)) abs["comment"] = cmt!;
                JsonResult = System.Text.Json.JsonSerializer.Serialize(abs, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                JsonResult = System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // node-specific UI is hosted inside `NodeSpecificContent` as UserControls
    }
}