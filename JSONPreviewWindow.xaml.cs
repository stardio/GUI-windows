using System.Windows;
using System.IO;
using Microsoft.Win32;

namespace EtherCAT_Studio
{
    public partial class JSONPreviewWindow : Window
    {
        public string JsonContent { get; set; }
        public bool IsConverted { get; private set; }
        public bool IsSaved { get; private set; }

        public JSONPreviewWindow(string jsonContent)
        {
            InitializeComponent();
            JsonContent = jsonContent;
            JsonDisplay.Text = FormatJson(jsonContent);
        }

        private string FormatJson(string json)
        {
            try
            {
                // 간단한 JSON 포맷팅 (indentation)
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, options);
            }
            catch
            {
                return json;
            }
        }

        private void ConvertBtn_Click(object sender, RoutedEventArgs e)
        {
            IsConverted = true;
            IsSaved = false;
            Close();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    FileName = $"sequence_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    InitialDirectory = Path.Combine(Directory.GetCurrentDirectory(), "File")
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveDialog.FileName, JsonContent);
                    MessageBox.Show($"✓ 파일 저장 완료!\n{saveDialog.FileName}", "저장됨", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    IsSaved = true;
                    IsConverted = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            IsConverted = false;
            IsSaved = false;
            Close();
        }
    }
}
