using RetroNetplayLauncher.MatchmakerClient;
using System;
using System.Windows;

namespace RetroNetplayLauncher
{
    public partial class MainWindow : Window
    {
        private void ChooseJoinBtn_Click(object sender, RoutedEventArgs e)
        {
            HostConfigPanel.Visibility = Visibility.Collapsed;
            JoinConfigPanel.Visibility = Visibility.Visible;
            StatusTxt.Text = "Status: Enter your host friend's 4-digit code.";
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

                // Instantiate Guest Client variant
                _matchmaker = new MatchmakerGuestClient(targetServerIp);
                _matchmaker.OnLobbyUpdated += Matchmaker_OnLobbyUpdated;
                _matchmaker.OnGameStarting += Matchmaker_OnGameStarting;

                // Safely cast to access the guest-specific initialization logic
                ((MatchmakerGuestClient)_matchmaker).JoinSession(codeInput);

                DisplayRoomCodeTxt.Text = $"CODE: {codeInput}";

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
    }
}
