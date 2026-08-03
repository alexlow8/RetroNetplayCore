using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace RetroNetplayLauncher
{
    public partial class MainWindow : Window
    {
        private const string ConfigFileName = "engine_config.json";

        public MainWindow()
        {
            InitializeComponent();
            LoadExistingConfig();
        }

        private void BrowseCore_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Dynamic Libraries (*.dll)|*.dll|All Files (*.*)|*.*" };
            if (dialog.ShowDialog() == true) TxtCorePath.Text = dialog.FileName;
        }

        private void BrowseRom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "ROM Files (*.z64;*.n64;*.bin)|*.z64;*.n64;*.bin|All Files (*.*)|*.*" };
            if (dialog.ShowDialog() == true) TxtRomPath.Text = dialog.FileName;
        }

        private void LoadExistingConfig()
        {
            if (!File.Exists(ConfigFileName)) return;
            try
            {
                string json = File.ReadAllText(ConfigFileName);
                var config = JsonSerializer.Deserialize<EngineConfig>(json);
                if (config == null) return;

                TxtLocalPort.Text = config.LocalPort.ToString();
                TxtFriendPort.Text = config.FriendPort.ToString();
                TxtCorePath.Text = config.CorePath;
                TxtRomPath.Text = config.RomPath;
            }
            catch { /* Fallback to UI defaults safely on read failures */ }
        }

        private void LaunchEngine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Package current UI configurations
                var config = new EngineConfig
                {
                    LocalPort = int.Parse(TxtLocalPort.Text),
                    FriendPort = int.Parse(TxtFriendPort.Text),
                    CorePath = TxtCorePath.Text,
                    RomPath = TxtRomPath.Text
                };

                // 2. Serialize to clean JSON string
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

                // 3. Directly write the file to disk (This will safely create or overwrite it)
                File.WriteAllText(ConfigFileName, json);

                // 4. Spool up the high-performance C# backend process sitting in the same flat folder
                string backendExeName = "RetroNetplayCore.exe";

                if (File.Exists(backendExeName))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = backendExeName,
                        UseShellExecute = true // Spawns an independent hardware terminal window
                    });
                    Application.Current.Shutdown(); // Close launcher panel seamlessly
                }
                else
                {
                    MessageBox.Show($"Could not find target backend runtime binary: {backendExeName}\nEnsure the solution has been fully built.", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration or boot engine: {ex.Message}", "System Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class EngineConfig
    {
        public int LocalPort { get; set; }
        public int FriendPort { get; set; }
        public string CorePath { get; set; } = "";
        public string RomPath { get; set; } = "";
    }
}
