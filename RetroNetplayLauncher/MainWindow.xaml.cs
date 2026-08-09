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

        public MainWindow() => InitializeComponent();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                EmulatorComboBox.Items.Clear();
                foreach (string core in CoreScanner.ScanForCores()) EmulatorComboBox.Items.Add(core);
                StatusTxt.Text = "Status: Select an emulator core to begin.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing cores: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EmulatorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmulatorComboBox.SelectedItem == null) return;
            string selectedCore = EmulatorComboBox.SelectedItem.ToString() ?? "";

            bool isMock = selectedCore.Contains("MockLibretroCore", StringComparison.OrdinalIgnoreCase);
            RomPathTxt.Text = isMock ? "[NOT REQUIRED FOR MOCK CORE]" : "";
            BrowseRomBtn.IsEnabled = !isMock;
            HostBtn.IsEnabled = isMock;
            JoinBtn.IsEnabled = isMock;
            StatusTxt.Text = isMock ? "Status: Mock Core ready. Choose Host or Join." : "Status: Please select a game ROM.";
        }

        private void BrowseRomBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new() { Filter = "All Supported Files|*.zip;*.sfc;*.smc;*.n64;*.z64|All Files (*.*)|*.*" };
            if (openFileDialog.ShowDialog() == true)
            {
                RomPathTxt.Text = openFileDialog.FileName;
                HostBtn.IsEnabled = JoinBtn.IsEnabled = true;
                StatusTxt.Text = "Status: Ready to connect.";
            }
        }

        private void HostBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusTxt.Text = "Status: Connecting to matchmaking server...";
            HostBtn.IsEnabled = JoinBtn.IsEnabled = false;

            try
            {
                // CA1806 & LOGIC BUG FIX: Explicitly evaluate the boolean result of TryParse.
                // If it fails, default to 2 players safely.
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

                _matchmaker = new MatchmakerClient(targetServerIp);
                _matchmaker.OnLobbyUpdated += Matchmaker_OnLobbyUpdated;
                _matchmaker.OnGameStarting += Matchmaker_OnGameStarting;

                DisplayRoomCodeTxt.Text = $"CODE: {_matchmaker.HostSession(maxPlayers)}";
                StatusTxt.Text = $"Status: Lobby open on port {_matchmaker.LocalPort}. Waiting for players...";
                StartGameBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                HostBtn.IsEnabled = JoinBtn.IsEnabled = true;
                MessageBox.Show($"Could not host session: {ex.Message}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void JoinBtn_Click(object sender, RoutedEventArgs e)
        {
            string codeInput = RoomCodeTxt.Text.Trim().ToUpper();
            if (string.IsNullOrEmpty(codeInput) || codeInput.Length != 4) return;

            StatusTxt.Text = $"Status: Joining room {codeInput}...";
            HostBtn.IsEnabled = JoinBtn.IsEnabled = false;

            try
            {
                _matchmaker = new MatchmakerClient(ServerIpTxt.Text.Trim());
                _matchmaker.OnLobbyUpdated += Matchmaker_OnLobbyUpdated;
                _matchmaker.OnGameStarting += Matchmaker_OnGameStarting;
                _matchmaker.JoinSession(codeInput);

                DisplayRoomCodeTxt.Text = $"CODE: {codeInput}";
                StatusTxt.Text = $"Status: Connected on port {_matchmaker.LocalPort}. Waiting for Host...";
            }
            catch (Exception ex)
            {
                HostBtn.IsEnabled = JoinBtn.IsEnabled = true;
                MessageBox.Show($"Could not join session: {ex.Message}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

                string selectedCore = EmulatorComboBox.SelectedItem?.ToString() ?? "MockLibretroCore.dll";
                ConfigWriter.WriteLaunchConfig(_matchmaker, peerRoster, selectedCore, RomPathTxt.Text);
                SessionManager.BootUnmanagedCore();

                StatusTxt.Text = "Status: Emulation active. Gameplay running.";
                this.WindowState = WindowState.Minimized;
            });
        }
    }
}
