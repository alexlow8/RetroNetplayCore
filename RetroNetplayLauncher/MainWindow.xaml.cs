using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32; // Needed for the ROM OpenFileDialog

namespace RetroNetplayLauncher
{
    public partial class MainWindow : Window
    {
        // Path to the emulators folder relative to the execution directory
        private readonly string _emulatorsFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "emulators");
        private MatchmakerClient? _matchmaker;

        public MainWindow()
        {
            InitializeComponent();
        }

        // --- STEP 1: SCAN THE EMULATORS FOLDER ON LOAD ---
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure the directory actually exists so it doesn't crash on startup
                if (!Directory.Exists(_emulatorsFolderPath))
                {
                    Directory.CreateDirectory(_emulatorsFolderPath);
                }

                // Scan for both compiled C++ DLLs or C# Executable assemblies in the emulators folder
                string[] files = Directory.GetFiles(_emulatorsFolderPath, "*Core*.dll");

                if (files.Length == 0)
                {
                    // Fallback check for any .dll or .exe core files if naming convention differs
                    files = Directory.GetFiles(_emulatorsFolderPath, "*.dll");
                }

                EmulatorComboBox.Items.Clear();
                foreach (string file in files)
                {
                    // Display just the clean file name (e.g., "MockLibretroCore.dll") instead of the scary full path
                    EmulatorComboBox.Items.Add(Path.GetFileName(file));
                }

                StatusTxt.Text = "Status: Select an emulator core to begin.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing emulator directory: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- STEP 2: DYNAMICALLY CHECK CORE SELECTION ---
        private void EmulatorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmulatorComboBox.SelectedItem == null) return;

            string selectedCore = EmulatorComboBox.SelectedItem.ToString() ?? "";

            // Check if our special Mock Core is selected
            if (selectedCore.Contains("MockLibretroCore", StringComparison.OrdinalIgnoreCase))
            {
                RomPathTxt.Text = "[NOT REQUIRED FOR MOCK CORE]";
                BrowseRomBtn.IsEnabled = false;

                // Instantly unlock hosting/joining buttons because no ROM blocker exists!
                HostBtn.IsEnabled = true;
                JoinBtn.IsEnabled = true;
                StatusTxt.Text = "Status: Mock Core ready. Choose Host or Join.";
            }
            else
            {
                // Reset form rules for standard game emulators that require a ROM file
                RomPathTxt.Text = "";
                BrowseRomBtn.IsEnabled = true;
                HostBtn.IsEnabled = false;
                JoinBtn.IsEnabled = false;
                StatusTxt.Text = "Status: Please browse and select a game ROM.";
            }
        }

        // --- STEP 3: ROM BROWSER (For standard emulators) ---
        private void BrowseRomBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Title = "Select Game ROM",
                Filter = "All Supported Files|*.zip;*.sfc;*.smc;*.n64;*.z64|All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                RomPathTxt.Text = openFileDialog.FileName;

                // Unlock network actions now that both requirements are met
                HostBtn.IsEnabled = true;
                JoinBtn.IsEnabled = true;
                StatusTxt.Text = "Status: Ready to connect.";
            }
        }

        // --- STEP 4: HOST BUTTON CLICK ---
        private void HostBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusTxt.Text = "Status: Connecting to matchmaking server...";
            HostBtn.IsEnabled = false;
            JoinBtn.IsEnabled = false;

            try
            {
                // Read the selected max player limit from our XAML dropdown menu
                int maxPlayers = 2;
                if (MaxPlayersComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    if (int.TryParse(selectedItem.Content.ToString(), out maxPlayers))
                    {
                        // Fail-safe default if parsing somehow breaks
                        maxPlayers = 2;
                        Console.WriteLine("⚠️ Warning: Max players drop-down parse failed. Defaulting to 2 players.");
                    }
                }

                // Snatch the target server IP straight from our new XAML text box entry
                string targetServerIp = ServerIpTxt.Text.Trim();

                // Initialize our matchmaker client with dynamic ip assignment
                _matchmaker = new MatchmakerClient(targetServerIp); // <-- PASSED IP HERE

                // Wire up our background network events to update our XAML interface safely
                _matchmaker.OnLobbyUpdated += Matchmaker_OnLobbyUpdated;
                _matchmaker.OnGameStarting += Matchmaker_OnGameStarting;

                // Send the "HOST" payload to the Node.js script and capture the 4-digit code
                string generatedCode = _matchmaker.HostSession(maxPlayers);

                // Update the XAML text displays
                DisplayRoomCodeTxt.Text = $"CODE: {generatedCode}";
                StatusTxt.Text = $"Status: Lobby open. Connected on dynamic port {_matchmaker.LocalPort}. Waiting for players to join...";

                // Only the host can press the "Start Emulation" button
                StartGameBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusTxt.Text = "Status: Connection failed.";
                HostBtn.IsEnabled = true;
                JoinBtn.IsEnabled = true;
                MessageBox.Show($"Could not host session: {ex.Message}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- STEP 5: JOIN BUTTON CLICK ---
        private void JoinBtn_Click(object sender, RoutedEventArgs e)
        {
            string codeInput = RoomCodeTxt.Text.Trim().ToUpper();

            if (string.IsNullOrEmpty(codeInput) || codeInput.Length != 4)
            {
                MessageBox.Show("Please enter a valid 4-digit room code.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusTxt.Text = $"Status: Joining room {codeInput}...";
            HostBtn.IsEnabled = false;
            JoinBtn.IsEnabled = false;

            try
            {
                _matchmaker = new MatchmakerClient(ServerIpTxt.Text.Trim()); // <-- PASSED IP HERE
                _matchmaker.OnLobbyUpdated += Matchmaker_OnLobbyUpdated;
                _matchmaker.OnGameStarting += Matchmaker_OnGameStarting;

                // Tell the client background task to start listening for the server's swap payload
                _matchmaker.JoinSession(codeInput);

                DisplayRoomCodeTxt.Text = $"CODE: {codeInput}";
                StatusTxt.Text = "Status: Connected to lobby on dynamic port {_matchmaker.LocalPort}. Waiting for Host to start...";
            }
            catch (Exception ex)
            {
                StatusTxt.Text = "Status: Join failed.";
                HostBtn.IsEnabled = true;
                JoinBtn.IsEnabled = true;
                MessageBox.Show($"Could not join session: {ex.Message}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- STEP 6: START EMULATION BUTTON (HOST ONLY) ---
        private void StartGameBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_matchmaker == null) return;

            StatusTxt.Text = "Status: Signaling game start to all players...";
            StartGameBtn.IsEnabled = false;

            // Send the 'START_GAME' command to the Node.js server
            _matchmaker.SignalStartGame();
        }

        // --- STEP 7: HANDLE BACKGROUND NETWORK LOBBY UPDATES ---
        private void Matchmaker_OnLobbyUpdated(LobbyUpdateMessage update)
        {
            // Crucial WPF Rule: Background network threads cannot touch XAML directly. 
            // We must use 'Dispatcher.Invoke' to force this update onto the main UI thread.
            Dispatcher.Invoke(() =>
            {
                PlayerListBox.Items.Clear();

                foreach (var peer in update.Peers)
                {
                    // Snap the peer data directly into our data-templated ListBox
                    PlayerListBox.Items.Add(peer);
                }

                StatusTxt.Text = $"Status: Roster updated. Total players in room: {update.Peers.Count}";
            });
        }

        // --- STEP 8: HANDLE GAME START PACKET ---
        private void Matchmaker_OnGameStarting()
        {
            Dispatcher.Invoke(() =>
            {
                if (_matchmaker == null) return;

                StatusTxt.Text = "Status: Triggering UDP Hole Punching sequence...";

                // Extract our peer list from the UI list box to feed to the hole punch executor
                System.Collections.Generic.List<PeerEndpoint> peerRoster = [];
                foreach (var item in PlayerListBox.Items)
                {
                    if (item is PeerEndpoint peer) peerRoster.Add(peer);
                }

                // Blast the 10 UDP punch packets and release the port
                _matchmaker.ExecuteHolePunch(peerRoster);

                StatusTxt.Text = "Status: Connection tunnels pierced! Booting emulation core...";

                // Gather selections from your UI controls
                string selectedCore = EmulatorComboBox.SelectedItem?.ToString() ?? "MockLibretroCore.dll";
                string relativeCorePath = System.IO.Path.Combine("Emulators", selectedCore);
                string romPath = RomPathTxt.Text;

                object launchConfig;

                // Use the newly added Matchmaker flag to determine our local network role
                if (_matchmaker.IsHost)
                {
                    // HOST CONFIGURATION: Group all incoming remote peers into matching slot indices
                    var clientsList = new System.Collections.Generic.List<object>();
                    int assignedSlot = 1;

                    foreach (var peer in peerRoster)
                    {
                        // Skip our own entry (the host is index 0, so we skip it)
                        if (peer.IsHost) continue;

                        clientsList.Add(new
                        {
                            Slot = assignedSlot,
                            IP = peer.Ip,
                            peer.Port
                        });
                        assignedSlot++;
                    }

                    launchConfig = new
                    {
                        IsHost = true,
                        _matchmaker.LocalPort,
                        CorePath = relativeCorePath,
                        RomPath = romPath,
                        Clients = clientsList
                    };
                }
                else
                {
                    // CLIENT CONFIGURATION: Extract the host connection details explicitly out of index 0
                    string hostIp = "127.0.0.1";
                    int hostPort = 8000;

                    if (peerRoster.Count > 0)
                    {
                        // Because your Node.js code puts the creator first, index 0 is guaranteed to be the Host!
                        hostIp = peerRoster[0].Ip;
                        hostPort = peerRoster[0].Port;
                    }

                    launchConfig = new
                    {
                        IsHost = false,
                        _matchmaker.LocalPort,
                        HostAddress = hostIp,
                        HostPort = hostPort,
                        CorePath = relativeCorePath,
                        RomPath = romPath
                    };
                }

                try
                {
                    // Serialize the anonymous object out into the configuration recipe layout
                    var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    string jsonString = System.Text.Json.JsonSerializer.Serialize(launchConfig, options);

                    string targetConfigPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine_config.json");
                    System.IO.File.WriteAllText(targetConfigPath, jsonString);

                    // Bootstrap your unmanaged high-performance console engine
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "RetroNetplayCore.exe",
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                        UseShellExecute = true
                    });

                    StatusTxt.Text = "Status: Emulation active. Gameplay running.";
                    this.WindowState = WindowState.Minimized;
                }
                catch (Exception ex)
                {
                    StatusTxt.Text = "Status: Core initialization crash.";
                    MessageBox.Show($"Could not boot unmanaged engine core: {ex.Message}", "Launch Failure", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }
    }
}
