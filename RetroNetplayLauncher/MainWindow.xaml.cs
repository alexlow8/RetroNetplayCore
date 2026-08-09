using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using Microsoft.Win32;

namespace RetroNetplayLauncher
{
    public partial class MainWindow : Window
    {
        private MatchmakerClient? _matchmaker;
        private AppConfig _appConfig = null!;

        public MainWindow() => InitializeComponent();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _appConfig = AppConfig.Load();
                ServerIpTxt.Text = _appConfig.LastUsedServerIP;

                EmulatorComboBox.Items.Clear();
                foreach (string core in CoreScanner.ScanForCores()) EmulatorComboBox.Items.Add(core);

                StatusTxt.Text = "Status: Welcome. Choose your session role.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization error: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- BUTTONS TO TOGGLE INITIAL UI ROLE PANELS ---
        private void ChooseHostBtn_Click(object sender, RoutedEventArgs e)
        {
            HostConfigPanel.Visibility = Visibility.Visible;
            JoinConfigPanel.Visibility = Visibility.Collapsed;
            StatusTxt.Text = "Status: Configure your local game and open the lobby.";
        }

        private void ChooseJoinBtn_Click(object sender, RoutedEventArgs e)
        {
            HostConfigPanel.Visibility = Visibility.Collapsed;
            JoinConfigPanel.Visibility = Visibility.Visible;
            StatusTxt.Text = "Status: Enter your host friend's 4-digit code.";
        }

        // --- HOST CORE SELECTION FLOW ---
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
            OpenFileDialog openFileDialog = new() { Filter = "All Supported Files|*.zip;*.sfc;*.smc;*.n64;*.z64|All Files (*.*)|*.*" };
            if (openFileDialog.ShowDialog() == true)
            {
                RomPathTxt.Text = openFileDialog.FileName;
                HostFinalBtn.IsEnabled = true;
                StatusTxt.Text = "Status: Selection complete. Open your lobby.";
            }
        }

        // --- NETWORK ACTIONS (REVEAL LOBBY PANEL ON CONNECT) ---
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
                        maxPlayers = 2; // Fallback if parsing somehow fails
                        Console.WriteLine("⚠️ Warning: Max players drop-down parse failed. Defaulting to 2 players.");
                    }
                }

                string targetServerIp = ServerIpTxt.Text.Trim();
                _appConfig.LastUsedServerIP = targetServerIp;
                _appConfig.Save();

                // 🔥 EXTRACT THE LOCAL SELECTIONS TO TRANSMIT
                string selectedCore = EmulatorComboBox.SelectedItem?.ToString() ?? "MockLibretroCore.dll";
                string romPath = RomPathTxt.Text;

                _matchmaker = new MatchmakerClient(targetServerIp);
                _matchmaker.OnLobbyUpdated += Matchmaker_OnLobbyUpdated;
                _matchmaker.OnGameStarting += Matchmaker_OnGameStarting;

                // 🔥 HAND THE ARGUMENTS OVER TO THE CLIENT HANDSHAKE METHOD
                DisplayRoomCodeTxt.Text = $"CODE: {_matchmaker.HostSession(maxPlayers, selectedCore, romPath)}";

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

        private void JoinFinalBtn_Click(object sender, RoutedEventArgs e)
        {
            string codeInput = RoomCodeTxt.Text.Trim().ToUpper();
            if (string.IsNullOrEmpty(codeInput) || codeInput.Length != 4)
            {
                MessageBox.Show("Please enter a valid 4-digit room code.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusTxt.Text = $"Status: Accessing room {codeInput}...";
            JoinFinalBtn.IsEnabled = ServerIpTxt.IsEnabled = RoleSelectionGrid.IsEnabled = false;

            try
            {
                string targetServerIp = ServerIpTxt.Text.Trim();
                _appConfig.LastUsedServerIP = targetServerIp;
                _appConfig.Save();

                _matchmaker = new MatchmakerClient(targetServerIp);
                _matchmaker.OnLobbyUpdated += Matchmaker_OnLobbyUpdated;
                _matchmaker.OnGameStarting += Matchmaker_OnGameStarting;
                _matchmaker.JoinSession(codeInput);

                DisplayRoomCodeTxt.Text = $"CODE: {codeInput}";

                // Switch client screen over to the live roster view
                JoinConfigPanel.Visibility = Visibility.Collapsed;
                LobbyPanel.Visibility = Visibility.Visible;

                StatusTxt.Text = $"Status: Joined room. Waiting for host to initiate launch...";
            }
            catch (Exception ex)
            {
                JoinFinalBtn.IsEnabled = ServerIpTxt.IsEnabled = RoleSelectionGrid.IsEnabled = true;
                MessageBox.Show($"Could not connect to room: {ex.Message}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartGameBtn_Click(object sender, RoutedEventArgs e)
        {
            StartGameBtn.IsEnabled = false;
            _matchmaker?.SignalStartGame();
        }

        private void Matchmaker_OnLobbyUpdated(LobbyUpdateMessage update)
        {
            Dispatcher.Invoke(() =>
            {
                PlayerListBox.Items.Clear();
                foreach (var peer in update.Peers) PlayerListBox.Items.Add(peer);
                StatusTxt.Text = $"Status: Roster updated. Total players: {update.Peers.Count}";
            });
        }

        private void Matchmaker_OnGameStarting()
        {
            Dispatcher.Invoke(() =>
            {
                if (_matchmaker == null) return;

                List<PeerEndpoint> peerRoster = [];
                foreach (var item in PlayerListBox.Items) if (item is PeerEndpoint peer) peerRoster.Add(peer);

                _matchmaker.ExecuteHolePunch(peerRoster);

                // Safe defaults: Clients use placeholders because their JSON is overwritten by the host configuration anyway
                string selectedCore = "MockLibretroCore.dll";
                string romPath = "";

                if (_matchmaker.IsHost)
                {
                    selectedCore = EmulatorComboBox.SelectedItem?.ToString() ?? "MockLibretroCore.dll";
                    romPath = RomPathTxt.Text;
                }

                ConfigWriter.WriteLaunchConfig(_matchmaker, peerRoster, selectedCore, romPath);
                SessionManager.BootUnmanagedCore();

                StatusTxt.Text = "Status: Emulation active. Gameplay running.";
                this.WindowState = WindowState.Minimized;
            });
        }
    }
}
