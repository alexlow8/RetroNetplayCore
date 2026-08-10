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

        // Changed to async void to handle the UI thread safely during network requests
        private async void HostFinalBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusTxt.Text = "Status: Reaching matching server...";
            HostFinalBtn.IsEnabled = ServerIpTxt.IsEnabled = RoleSelectionGrid.IsEnabled = false;

            try
            {
                int maxPlayers = 2;
                if (MaxPlayersComboBox.SelectedItem is ComboBoxItem item)
                {
                    // Check the boolean return value. If it fails, explicitly fall back to a safe default.
                    if (!int.TryParse(item.Content.ToString(), out maxPlayers))
                    {
                        maxPlayers = 2;
                    }
                }

                string targetServerIp = ServerIpTxt.Text.Trim();
                _appConfig.LastUsedServerIP = targetServerIp;
                _appConfig.Save();

                string selectedCore = EmulatorComboBox.SelectedItem?.ToString() ?? "MockLibretroCore.dll";
                string romPath = RomPathTxt.Text;

                var hostClient = new MatchmakerHostClient(targetServerIp);
                _matchmaker = hostClient;
                _matchmaker.OnLobbyUpdated += Matchmaker_OnLobbyUpdated;
                _matchmaker.OnGameStarting += Matchmaker_OnGameStarting;

                // Non-blocking asynchronous network handshake call
                string code = await hostClient.HostSessionAsync(maxPlayers, selectedCore, romPath);
                DisplayRoomCodeTxt.Text = $"CODE: {code}";

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

        private async void StartGameBtn_Click(object sender, RoutedEventArgs e)
        {
            StartGameBtn.IsEnabled = false;
            if (_matchmaker is MatchmakerHostClient hostClient)
            {
                await hostClient.SignalStartGameAsync();
            }
        }
    }
}
