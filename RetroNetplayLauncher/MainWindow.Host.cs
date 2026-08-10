using RetroNetplayLauncher.MatchmakerClient;
using System;
using System.Windows;
using System.Windows.Controls;

namespace RetroNetplayLauncher
{
    public partial class MainWindow : Window
    {
        private void ChooseHostBtn_Click(object sender, RoutedEventArgs e)
        {
            HostConfigPanel.Visibility = Visibility.Visible;
            JoinConfigPanel.Visibility = Visibility.Collapsed;
            StatusTxt.Text = "Status: Configure your local game and open the lobby.";
        }

        private void EmulatorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmulatorComboBox.SelectedItem == null) return;
            string selectedCore = EmulatorComboBox.SelectedItem.ToString() ?? "";

            bool isMock = selectedCore.Contains("MockLibretroCore", StringComparison.OrdinalIgnoreCase);
            RomPathTxt.Text = isMock ? "[NOT REQUIRED FOR MOCK CORE]" : "";
            BrowseRomBtn.IsEnabled = !isMock;
            HostFinalBtn.IsEnabled = isMock;
            StatusTxt.Text = isMock ? "Status: Mock setup ready. Open your lobby." : "Status: Select a game ROM file to proceed.";
        }

        private void BrowseRomBtn_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new() { Filter = "All Supported Files|*.zip;*.sfc;*.smc;*.n64;*.z64|All Files (*.*)|*.*" };
            if (openFileDialog.ShowDialog() == true)
            {
                RomPathTxt.Text = openFileDialog.FileName;
                HostFinalBtn.IsEnabled = true;
                StatusTxt.Text = "Status: Selection complete. Open your lobby.";
            }
        }

        private void HostFinalBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusTxt.Text = "Status: Reaching matching server...";
            HostFinalBtn.IsEnabled = ServerIpTxt.IsEnabled = RoleSelectionGrid.IsEnabled = false;

            try
            {
                int maxPlayers = 2;
                if (MaxPlayersComboBox.SelectedItem is ComboBoxItem item)
                {
                    if (!int.TryParse(item.Content.ToString(), out maxPlayers))
                    {
                        maxPlayers = 2;
                        Console.WriteLine("⚠️ Warning: Max players drop-down parse failed. Defaulting to 2 players.");
                    }
                }

                string targetServerIp = ServerIpTxt.Text.Trim();
                _appConfig.LastUsedServerIP = targetServerIp;
                _appConfig.Save();

                string selectedCore = EmulatorComboBox.SelectedItem?.ToString() ?? "MockLibretroCore.dll";
                string romPath = RomPathTxt.Text;

                // Instantiate Host Client variant
                _matchmaker = new MatchmakerHostClient(targetServerIp);
                _matchmaker.OnLobbyUpdated += Matchmaker_OnLobbyUpdated;
                _matchmaker.OnGameStarting += Matchmaker_OnGameStarting;

                // Safely cast to access the host-specific initialization logic
                DisplayRoomCodeTxt.Text = $"CODE: {((MatchmakerHostClient)_matchmaker).HostSession(maxPlayers, selectedCore, romPath)}";

                HostConfigPanel.Visibility = Visibility.Collapsed;
                LobbyPanel.Visibility = Visibility.Visible;
                StartGameBtn.Visibility = Visibility.Visible;

                StatusTxt.Text = $"Status: Lobby running on port {_matchmaker.LocalPort}. Awaiting peers...";
            }
            catch (Exception ex)
            {
                HostFinalBtn.IsEnabled = ServerIpTxt.IsEnabled = RoleSelectionGrid.IsEnabled = true;
                MessageBox.Show($"Could not create room: {ex.Message}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartGameBtn_Click(object sender, RoutedEventArgs e)
        {
            StartGameBtn.IsEnabled = false;
            if (_matchmaker is MatchmakerHostClient hostClient)
            {
                hostClient.SignalStartGame();
            }
        }
    }
}
