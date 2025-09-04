using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.Generic;
using System.Threading.Tasks;

using ExodusHubKillTrackerWPF;

namespace ExodusHubKillTrackerWPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Load saved settings using new manager
        var settings = UserSettingsManager.Load();
        LogPathTextBox.Text = settings.GameLogPath ?? "";
        UsernameTextBox.Text = settings.Username ?? "";
        TokenTextBox.Text = settings.Token ?? "";

        // Set version number in UI
        VersionTextBlock.Text = $"v{ExodusHub_Kill_Tracker.HTTPClient.Version}";

        // Subscribe to the Loaded event to auto-start tracking
        this.Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Attempt to auto-start tracking if all settings are available
        await TryAutoStartTracking();
    }

    private async Task TryAutoStartTracking()
    {
        try
        {
            string logPath = LogPathTextBox.Text.Trim();
            string username = UsernameTextBox.Text.Trim();
            string token = TokenTextBox.Text.Trim();

            // Check if all required fields have values
            if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
            {
                StatusTextBlock.Text = "Auto-start skipped: Please fill in all fields and restart the application.";
                return;
            }

            if (!System.IO.File.Exists(logPath))
            {
                StatusTextBlock.Text = "Auto-start skipped: Game.log file not found.";
                return;
            }

            // Check if starcitizen.exe is running
            var procs = System.Diagnostics.Process.GetProcessesByName("starcitizen");
            if (procs.Length == 0)
            {
                StatusTextBlock.Text = "Auto-start skipped: Star Citizen is not running.";
                return;
            }

            StatusTextBlock.Text = "Auto-starting... Testing server connection...";
            
            // Setup HTTPClient
            _httpClient = new ExodusHub_Kill_Tracker.HTTPClient(authToken: token);
            
            // Test server connection
            var (success, message) = await _httpClient.VerifyApiConnectionAsync();
            if (!success)
            {
                StatusTextBlock.Text = $"Auto-start failed: Server connection failed - {message}";
                return;
            }

            StatusTextBlock.Text = "Auto-started! Monitoring log...";
            StartLogMonitoring(logPath, username);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Auto-start error: {ex.Message}";
        }
    }

    private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Optional: Double-click to maximize/restore
            if (WindowState == WindowState.Normal)
                WindowState = WindowState.Maximized;
            else
                WindowState = WindowState.Normal;
        }
        else
        {
            DragMove();
        }
    }
    private void BrowseLogButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog();
        dlg.Title = "Select Star Citizen Game.log";
        dlg.Filter = "Log files (*.log)|*.log|All files (*.*)|*.*";
        if (dlg.ShowDialog() == true)
        {
            LogPathTextBox.Text = dlg.FileName;
        }
    }
    private ExodusHub_Kill_Tracker.HTTPClient _httpClient;

    // Add fallback error handling to StartButton_Click
    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string logPath = LogPathTextBox.Text.Trim();
            string username = UsernameTextBox.Text.Trim();
            string token = TokenTextBox.Text.Trim();

            // Defensive: Ensure StatusTextBlock is visible and enabled
            StatusTextBlock.Visibility = Visibility.Visible;
            StatusTextBlock.IsEnabled = true;

            // Save settings using new manager
            UserSettingsManager.Save(new UserSettings
            {
                GameLogPath = logPath,
                Username = username,
                Token = token
            });

            if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
            {
                StatusTextBlock.Text = "Please fill in all fields.";
                return;
            }
            if (!System.IO.File.Exists(logPath))
            {
                StatusTextBlock.Text = "Game.log file not found.";
                return;
            }
            // Check if starcitizen.exe is running
            var procs = System.Diagnostics.Process.GetProcessesByName("starcitizen");
            if (procs.Length == 0)
            {
                StatusTextBlock.Text = "Star Citizen is not running.";
                return;
            }
            // Setup HTTPClient
            _httpClient = new ExodusHub_Kill_Tracker.HTTPClient(authToken: token);
            // Test server connection
            StatusTextBlock.Text = "Testing server connection...";
            var (success, message) = await _httpClient.VerifyApiConnectionAsync();
            if (!success)
            {
                StatusTextBlock.Text = $"Server connection failed: {message}";
                return;
            }
            StatusTextBlock.Text = "Connected! Monitoring log...";
            // Start log monitoring (to be implemented next)
            StartLogMonitoring(logPath, username);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Unexpected error: {ex.Message}";
            StatusTextBlock.Visibility = Visibility.Visible;
        }
    }

    private System.Threading.CancellationTokenSource _logMonitorCts;

    private void StartLogMonitoring(string logPath, string username)
    {
        _logMonitorCts?.Cancel();
        _logMonitorCts = new System.Threading.CancellationTokenSource();
        var token = _logMonitorCts.Token;
        System.Threading.Tasks.Task.Run(async () =>
        {
            long lastPosition = new System.IO.FileInfo(logPath).Length;
            var regex = new System.Text.RegularExpressions.Regex(
                @"<(?<timestamp>[\d\-T:.Z]+)> \[Notice\] <Actor Death> CActor::Kill: '(?<victim>[^']+)' \[\d+\] in zone '(?<zone>[^']+)' killed by '(?<killer>[^']+)' \[\d+\] using '(?<weapon>[^']+)' \[Class unknown\] with damage type '(?<damageType>[^']+)' from direction",
                System.Text.RegularExpressions.RegexOptions.Compiled);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var fs = new System.IO.FileStream(logPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    {
                        fs.Seek(lastPosition, System.IO.SeekOrigin.Begin);
                        using (var sr = new System.IO.StreamReader(fs))
                        {
                            string line;
                            while ((line = await sr.ReadLineAsync()) != null)
                            {
                                lastPosition += Encoding.UTF8.GetByteCount(line + Environment.NewLine);
                                var match = regex.Match(line);
                                if (match.Success)
                                {
                                    var killData = new ExodusHub_Kill_Tracker.KillData
                                    {
                                        Killer = match.Groups["killer"].Value,
                                        Victim = match.Groups["victim"].Value,
                                        Weapon = match.Groups["weapon"].Value,
                                        Location = match.Groups["zone"].Value,
                                        Timestamp = match.Groups["timestamp"].Value,
                                        EventId = "default-event",
                                        Details = $"DamageType: {match.Groups["damageType"].Value}; Username: {username}"
                                    };
                                    // Validation step
                                    if (string.IsNullOrWhiteSpace(killData.Killer) ||
                                        string.IsNullOrWhiteSpace(killData.Victim) ||
                                        string.IsNullOrWhiteSpace(killData.Weapon) ||
                                        string.IsNullOrWhiteSpace(killData.Location) ||
                                        string.IsNullOrWhiteSpace(killData.Timestamp) ||
                                        string.IsNullOrWhiteSpace(killData.EventId))
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            StatusTextBlock.Text = "Kill event skipped: missing required field(s).";
                                        });
                                        continue;
                                    }
                                    var (success, msg) = await _httpClient.SendKillDataWithDetailsAsync(killData);
                                    Dispatcher.Invoke(() =>
                                    {
                                        StatusTextBlock.Text = success ? $"Kill sent: {killData.Killer} -> {killData.Victim}" : $"Error: {msg}";
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusTextBlock.Text = $"Log monitor error: {ex.Message}";
                    });
                }
                await System.Threading.Tasks.Task.Delay(1000, token);
            }
        }, token);
    }

    private void TestStatusButton_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = $"StatusTextBlock test at {DateTime.Now:T}";
        StatusTextBlock.Visibility = Visibility.Visible;
        StatusTextBlock.IsEnabled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        // Cancel any ongoing log monitoring
        _logMonitorCts?.Cancel();
        Close();
    }
}