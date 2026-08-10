using Microsoft.Win32;
using RetroNetplayLauncher.MatchmakerClient;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RetroNetplayLauncher
{
    public partial class MainWindow : Window
    {
        private MatchmakerClientBase? _matchmaker;
        private AppConfig _appConfig = null!;

        // An explicit, thread-safe memory registry cache for player list updates
        private readonly List<PeerEndpoint> _cachedPeerRoster = [];

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
            // Thread-safely jump onto the WPF UI core thread dispatcher
            Dispatcher.Invoke(() =>
            {
                lock (_cachedPeerRoster)
                {
                    _cachedPeerRoster.Clear();
                    _cachedPeerRoster.AddRange(update.Peers);
                }

                PlayerListBox.Items.Clear();
                foreach (var peer in update.Peers)
                {
                    // Render simple descriptive string rows into the UI view listbox
                    PlayerListBox.Items.Add($"{peer.Ip}:{peer.Port} (LAN: {peer.InternalIp}:{peer.InternalPort}) {(peer.IsHost ? "[Host]" : "[Guest]")}");
                }
                StatusTxt.Text = $"Status: Roster updated. Total players: {update.Peers.Count}";
            });
        }

        private void Matchmaker_OnGameStarting()
        {
            // Pre-declare and extract UI component text values directly on the UI thread configuration boundary
            string selectedCore = "MockLibretroCore.dll";
            string romPath = "";

            Dispatcher.Invoke(() =>
            {
                selectedCore = EmulatorComboBox.SelectedItem?.ToString() ?? "MockLibretroCore.dll";
                romPath = RomPathTxt.Text;
                StatusTxt.Text = "Status: Emulation active. Gameplay running.";
                this.WindowState = WindowState.Minimized;
            });

            // Offload the hole punch and execution handover to a clean async background thread task
            Task.Run(async () =>
            {
                if (_matchmaker == null) return;

                // Create a completely detached structural copy of the player listing registry cache
                List<PeerEndpoint> rosterSnapshot;
                lock (_cachedPeerRoster)
                {
                    rosterSnapshot = [.. _cachedPeerRoster];
                }

                // 1. Run the targeted UDP socket punch burst rules
                await _matchmaker.ExecuteHolePunchAsync(rosterSnapshot);

                // 2. Adjust core file bindings if evaluating a Guest participant instance profile
                if (!_matchmaker.IsHost)
                {
                    selectedCore = _matchmaker.TargetCoreName;
                    romPath = _matchmaker.TargetRomPath;
                }

                // 3. Extract the socket handle safely, stopping all background client activities
                Socket dynamicHandoverSocket = _matchmaker.ArrearsAndExtractSocket();

                // 4. Save deployment properties setup details straight into config files
                ConfigWriter.WriteLaunchConfig(_matchmaker, rosterSnapshot, selectedCore, romPath);

                // 5. Duplicate the live socket kernel handle descriptor straight into the engine process space
                SessionManager.BootUnmanagedCore(dynamicHandoverSocket);

                // 6. Shutdown the WPF launcher program context window environment safely
                await Application.Current.Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
            });
        }
    }
}
