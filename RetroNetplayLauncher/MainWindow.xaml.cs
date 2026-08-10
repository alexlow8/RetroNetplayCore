using Microsoft.Win32;
using RetroNetplayLauncher.MatchmakerClient;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RetroNetplayLauncher
{
    public partial class MainWindow : Window
    {
        private MatchmakerClientBase? _matchmaker; // Using the abstract parent type
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
