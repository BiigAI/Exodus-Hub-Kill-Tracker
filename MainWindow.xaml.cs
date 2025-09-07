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
using System.Diagnostics; // Add if not already present
using System.Threading; // Add for Timer
using System.Media; // For SoundPlayer

using ExodusHubKillTrackerWPF;

namespace ExodusHubKillTrackerWPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _advancedViewEnabled = false;
    private readonly StringBuilder _advancedLog = new();
    private bool _isTracking = false;
    private Timer _apiHealthCheckTimer;
    // Set health check interval to a realistic value (e.g., 2 minutes)
    private readonly TimeSpan _apiHealthCheckInterval = TimeSpan.FromMinutes(2);

    // Add this field to track kill sound toggle
    private bool _playKillSound = true;

    public MainWindow()
    {
        InitializeComponent();
        LogAdvanced("App", "Application started."); // Log app start

        // Load saved settings using new manager
        var settings = UserSettingsManager.Load();
        LogAdvanced("Settings", "Loaded user settings."); // Log settings load

        LogPathTextBox.Text = settings.GameLogPath ?? "";
        UsernameTextBox.Text = settings.Username ?? "";
        TokenTextBox.Text = settings.Token ?? "";

        // Set version number in UI
        VersionTextBlock.Text = $"v{ExodusHub_Kill_Tracker.HTTPClient.Version}";
        LogAdvanced("Version", $"Set version text: v{ExodusHub_Kill_Tracker.HTTPClient.Version}");

        // Subscribe to the Loaded event to auto-start tracking
        this.Loaded += MainWindow_Loaded;

#if DEBUG
        TestKillSoundButton.Visibility = Visibility.Visible;
#else
        TestKillSoundButton.Visibility = Visibility.Collapsed;
#endif

        AdvancedViewBorder.Visibility = Visibility.Collapsed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Use correct icons: \uE767 (speaker), \uE198 (speaker with line)
        KillSoundButton.Content = _playKillSound ? "\uE767" : "\uE198";
        KillSoundButton.ToolTip = _playKillSound ? "Disable Kill Sound" : "Enable Kill Sound";
        LogAdvanced("App", "MainWindow loaded.");
        // Attempt to auto-start tracking if all settings are available
        await TryAutoStartTracking();
    }

    private async Task TryAutoStartTracking()
    {
        try
        {
            LogAdvanced("Tracking", "Attempting auto-start tracking.");
            string logPath = LogPathTextBox.Text.Trim();
            string username = UsernameTextBox.Text.Trim();
            string token = TokenTextBox.Text.Trim(); // FIXED: was Token.TextBox.Text.Trim()

            // Check if all required fields have values
            if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
            {
                StatusTextBlock.Text = "Auto-start skipped: Please fill in all fields and restart the application.";
                LogAdvanced("Tracking", "Auto-start skipped: missing fields.");
                return;
            }

            if (!System.IO.File.Exists(logPath))
            {
                StatusTextBlock.Text = "Auto-start skipped: Game.log file not found.";
                LogAdvanced("Tracking", "Auto-start skipped: log file not found.");
                return;
            }

            // Check if starcitizen.exe is running
            var procs = System.Diagnostics.Process.GetProcessesByName("starcitizen");
            if (procs.Length == 0)
            {
                StatusTextBlock.Text = "Auto-start skipped: Star Citizen is not running.";
                LogAdvanced("Tracking", "Auto-start skipped: Star Citizen not running.");
                return;
            }

            StatusTextBlock.Text = "Auto-starting... Testing server connection...";
            LogAdvanced("Tracking", "Testing server connection (auto-start).");

            // Setup HTTPClient
            _httpClient = new ExodusHub_Kill_Tracker.HTTPClient(authToken: token);

            // Test server connection
            var (success, message) = await _httpClient.VerifyApiConnectionAsync();
            LogAdvanced("API Health", $"VerifyApiConnectionAsync: success={success}, message={message}");
            if (!success)
            {
                StatusTextBlock.Text = $"Auto-start failed: Server connection failed - {message}";
                LogAdvanced("Tracking", $"Auto-start failed: {message}");
                return;
            }

            StatusTextBlock.Text = "Auto-start success: SC running and API Connected! Monitoring log...";
            LogAdvanced("Tracking", "Auto-start success: monitoring log.");
            StartLogMonitoring(logPath, username);

            // Change button to "Stop Tracking" and make it red
            _isTracking = true;
            Dispatcher.Invoke(() =>
            {
                StartTrackingButton.Content = "Stop Tracking";
                StartTrackingButton.Background = new SolidColorBrush(Colors.Red);
            });

            // Start health check timer
            StartApiHealthCheckTimer();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Auto-start error: {ex.Message}";
            LogAdvanced("Error", $"Auto-start error: {ex.Message}");
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
        if (_isTracking)
        {
            LogAdvanced("Tracking", "Stop tracking requested by user.");
            // Stop tracking
            _logMonitorCts?.Cancel();
            _isTracking = false;
            StartTrackingButton.Content = "Start Tracking";
            StartTrackingButton.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)); // Default blue
            StatusTextBlock.Text = "Tracking stopped.";

            // Stop health check timer
            _apiHealthCheckTimer?.Dispose();
            _apiHealthCheckTimer = null;
            LogAdvanced("Tracking", "Tracking stopped.");
            return;
        }

        try
        {
            LogAdvanced("Tracking", "Start tracking requested by user.");
            string logPath = LogPathTextBox.Text.Trim();
            string username = UsernameTextBox.Text.Trim();
            string token = TokenTextBox.Text.Trim(); // FIXED: was Token.TextBox.Text.Trim()

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
            LogAdvanced("Settings", "Saved user settings.");

            if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
            {
                StatusTextBlock.Text = "Please fill in all fields.";
                LogAdvanced("Tracking", "Start tracking failed: missing fields.");
                return;
            }
            if (!System.IO.File.Exists(logPath))
            {
                StatusTextBlock.Text = "Game.log file not found.";
                LogAdvanced("Tracking", "Start tracking failed: log file not found.");
                return;
            }
            // Check if starcitizen.exe is running
            var procs = System.Diagnostics.Process.GetProcessesByName("starcitizen");
            if (procs.Length == 0)
            {
                StatusTextBlock.Text = "Star Citizen is not running.";
                LogAdvanced("Tracking", "Start tracking failed: Star Citizen not running.");
                return;
            }
            // Setup HTTPClient
            _httpClient = new ExodusHub_Kill_Tracker.HTTPClient(authToken: token);
            // Test server connection
            StatusTextBlock.Text = "Testing server connection...";
            LogAdvanced("Tracking", "Testing server connection (manual start).");
            var (success, message) = await _httpClient.VerifyApiConnectionAsync();
            LogAdvanced("API Health", $"VerifyApiConnectionAsync: success={success}, message={message}");
            if (!success)
            {
                StatusTextBlock.Text = $"Server connection failed: {message}";
                LogAdvanced("Tracking", $"Start tracking failed: {message}");
                return;
            }
            StatusTextBlock.Text = "Connected! Monitoring log...";
            LogAdvanced("Tracking", "Tracking started: monitoring log.");
            // Start log monitoring (to be implemented next)
            StartLogMonitoring(logPath, username);

            // Change button to "Stop Tracking" and make it red
            _isTracking = true;
            StartTrackingButton.Content = "Stop Tracking";
            StartTrackingButton.Background = new SolidColorBrush(Colors.Red);

            // Start health check timer
            StartApiHealthCheckTimer();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Unexpected error: {ex.Message}";
            StatusTextBlock.Visibility = Visibility.Visible;
            LogAdvanced("Error", $"Start tracking error: {ex.Message}");
        }
    }

    private System.Threading.CancellationTokenSource _logMonitorCts;

    /// <summary>
    /// Plays the killsound.wav from the resources folder using MediaPlayer for async playback.
    /// </summary>
    private void PlayKillSound()
    {
        try
        {
            string soundPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "killsound.wav");
            LogAdvanced("Sound", $"Attempting to play: {soundPath}");

            if (!System.IO.File.Exists(soundPath))
            {
                LogAdvanced("Sound", "killsound.wav not found at expected path.");
                StatusTextBlock.Text = "Kill sound file not found.";
                return;
            }

            // Use MediaPlayer for non-blocking playback in WPF
            var player = new MediaPlayer();
            player.Open(new Uri(soundPath, UriKind.Absolute));
            player.Volume = 1.0;
            player.Play();

            // Optionally, release resources after playback (short sound)
            player.MediaEnded += (s, e) => player.Close();
        }
        catch (Exception ex)
        {
            LogAdvanced("Sound", $"Failed to play killsound.wav: {ex.Message}");
            StatusTextBlock.Text = $"Sound error: {ex.Message}";
        }
    }

    private void StartLogMonitoring(string logPath, string username)
    {
        LogAdvanced("LogMonitor", $"Started log monitoring for {logPath} as {username}.");
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
                                        LogAdvanced("LogMonitor", "Kill event skipped: missing required field(s).");
                                        continue;
                                    }
                                    var (success, msg) = await SendKillDataWithDetailsAndLogAsync(killData);

                                    Dispatcher.Invoke(() =>
                                    {
                                        StatusTextBlock.Text = success ? $"Kill sent: {killData.Killer} -> {killData.Victim}" : $"Error: {msg}";
                                    });
                                    LogAdvanced("LogMonitor", success ? $"Kill sent: {killData.Killer} -> {killData.Victim}" : $"Error: {msg}");

                                    // Play sound only if kill was successfully sent, on UI thread
                                    if (success && _playKillSound)
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            LogAdvanced("Sound", "Playing kill sound from log monitor (UI thread).");
                                            PlayKillSound();
                                        });
                                    }
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
                    LogAdvanced("Error", $"Log monitor error: {ex.Message}");
                }
                await System.Threading.Tasks.Task.Delay(1000, token);
            }
            LogAdvanced("LogMonitor", "Log monitoring stopped.");
        }, token);
    }

    private async Task<(bool, string)> SendKillDataWithDetailsAndLogAsync(ExodusHub_Kill_Tracker.KillData killData)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(killData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        LogAdvanced("API Request", json);
        var (success, response) = await _httpClient.SendKillDataWithDetailsAsync(killData);
        LogAdvanced("API Response", response ?? "(null)");
        return (success, response);
    }

    private void LogAdvanced(string label, string data)
    {
        if (_advancedLog.Length > 8000)
            _advancedLog.Clear(); // Prevent unbounded growth
        _advancedLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {label}:");
        _advancedLog.AppendLine(data);
        _advancedLog.AppendLine(new string('-', 40));
        // Always keep the log up to date, but only update the textbox if enabled
        if (_advancedViewEnabled)
        {
            Dispatcher.Invoke(() => {
                AdvancedViewTextBox.Text = _advancedLog.ToString();
                AdvancedViewTextBox.ScrollToEnd();
            });
        }
    }

    private void AdvancedViewButton_Click(object sender, RoutedEventArgs e)
    {
        _advancedViewEnabled = !_advancedViewEnabled;
        AdvancedViewBorder.Visibility = _advancedViewEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_advancedViewEnabled)
            AdvancedViewTextBox.Text = _advancedLog.ToString(); // Always show all logs from app start
    }

    private void CloseAdvancedViewButton_Click(object sender, RoutedEventArgs e)
    {
        _advancedViewEnabled = false;
        AdvancedViewBorder.Visibility = Visibility.Collapsed;
    }

    // Optional: Allow Escape key to close advanced view
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_advancedViewEnabled && e.Key == Key.Escape)
        {
            _advancedViewEnabled = false;
            AdvancedViewBorder.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private void TestStatusButton_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = $"StatusTextBlock test at {DateTime.Now:T}";
        StatusTextBlock.Visibility = Visibility.Visible;
        StatusTextBlock.IsEnabled = true;
        LogAdvanced("Test", "StatusTextBlock test button clicked.");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        // Cancel any ongoing log monitoring
        _logMonitorCts?.Cancel();
        // Stop health check timer
        _apiHealthCheckTimer?.Dispose();
        _apiHealthCheckTimer = null;
        LogAdvanced("App", "Application exiting.");
        Close();
    }

    private void LogoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "https://sc.exoduspmc.org/kills",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            // Optionally handle error (e.g., show a message)
        }
    }

    private void StartApiHealthCheckTimer()
    {
        // Dispose previous timer if any
        _apiHealthCheckTimer?.Dispose();

        // Timer callback runs on a ThreadPool thread, so use Dispatcher for UI updates
        _apiHealthCheckTimer = new Timer(async _ =>
        {
            if (!_isTracking || _httpClient == null)
                return;

            var (success, message) = await _httpClient.VerifyApiConnectionAsync();
            LogAdvanced("API Health", $"Health check: success={success}, message={message}");
            if (!success)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = $"API health check failed: {message}. Tracking stopped.";
                    StartTrackingButton.Content = "Start Tracking";
                    StartTrackingButton.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)); // Default blue
                });
                LogAdvanced("API Health", $"API health check failed: {message}. Tracking stopped.");
                _logMonitorCts?.Cancel();
                _isTracking = false;
                _apiHealthCheckTimer?.Dispose();
                _apiHealthCheckTimer = null;
            }
            else
            {
                LogAdvanced("API Health", $"API health check OK at {DateTime.Now:T}");
            }
        }, null, _apiHealthCheckInterval, _apiHealthCheckInterval);
    }

    private void TestKillSoundButton_Click(object sender, RoutedEventArgs e)
    {
        PlayKillSound();
        StatusTextBlock.Text = "Test kill sound played.";
        LogAdvanced("Test", "TestKillSoundButton clicked: kill sound played.");
    }

    // KillSound toggle button event handler
    private void KillSoundButton_Click(object sender, RoutedEventArgs e)
    {
        _playKillSound = !_playKillSound;
        KillSoundButton.Content = _playKillSound ? "\uE767" : "\uE198"; // Speaker/Speaker with line icons
        KillSoundButton.ToolTip = _playKillSound ? "Disable Kill Sound" : "Enable Kill Sound";
    }
}