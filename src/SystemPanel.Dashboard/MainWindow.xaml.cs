using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SystemPanel.Dashboard.Controls;
using SystemPanel.Dashboard.Core;
using SystemPanel.Dashboard.Models;

namespace SystemPanel.Dashboard
{
    public partial class MainWindow : Window
    {
        private readonly PanelHost p1 = new();
        private readonly PanelHost p2 = new();
        private readonly PanelHost p3 = new();
        private readonly PanelHost p4 = new();

        private bool _isPanelMaximized;
        private PanelHost? _maximizedPanel;

        private bool _isAppFullscreen;
        private WindowState _oldState;
        private WindowStyle _oldStyle;
        private ResizeMode _oldResize;

        private bool _eventsHooked;
        private DispatcherTimer? _resizeDebounceTimer;

        private readonly DashboardPaths _paths = DashboardPaths.Default();
        private DashboardSettings _settings;
        private readonly DashboardConfigClient _configClient;
        private readonly DispatcherTimer _configTimer = new();
        private string? _appliedConfigJson;
        private int _consecutiveFailures;
        private bool _refreshing;

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public MainWindow()
        {
            InitializeComponent();

            DashboardLog.Initialize(_paths.LogDirectory);

            // WebView2 profil klasörü: varsayılan (exe yanındaki klasör) Program Files altında yazılamaz.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER")))
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", System.IO.Path.Combine(_paths.Root, "WebView2"));
            _settings = DashboardSettings.Load(_paths.SettingsFile);
            _configClient = new DashboardConfigClient(_http, _paths.ConfigCacheFile);
            _configTimer.Tick += ConfigTimer_Tick;

            SizeChanged += MainWindow_SizeChanged;
            StateChanged += MainWindow_StateChanged;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Focus();
                Keyboard.Focus(this);

                if (!_settings.IsComplete)
                {
                    // İlk kurulum (kullanıcı etkileşimli; otomatik yollarda pencere açılmaz).
                    var wizard = new ScreenSetupWindow(_paths, _settings);
                    if (wizard.ShowDialog() != true)
                    {
                        DashboardLog.Info("Kurulum iptal edildi. Uygulama kapanıyor.");
                        Application.Current.Shutdown();
                        return;
                    }
                    _settings = DashboardSettings.Load(_paths.SettingsFile);
                }

                DashboardLog.Info($"Başlatıldı: ekran={_settings.Screen}, sunucu={_settings.ApiBaseUrl}");
                await RefreshConfigAsync();
            }
            catch (Exception ex)
            {
                DashboardLog.Error("STARTUP ERROR", ex);
                TopStatus.Text = "Başlatma hatası – yeniden denenecek";
                ScheduleNextRefresh(RetryPolicy.Delay(1));
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Uygulamayı kapatmak istiyor musunuz?",
                "Çıkış",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAppFullscreen) return;

            if (e.ClickCount == 2)
            {
                ToggleAppFullscreen();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleAppFullscreen();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _isPanelMaximized && _maximizedPanel != null)
            {
                RestorePanelFullscreen();
                e.Handled = true;
            }
        }

        private void ToggleAppFullscreen()
        {
            if (!_isAppFullscreen)
            {
                _oldState = WindowState;
                _oldStyle = WindowStyle;
                _oldResize = ResizeMode;

                MainMenu.Visibility = Visibility.Collapsed;

                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;

                _isAppFullscreen = true;
                TopStatus.Text = "Tam ekran (F11 ile çık)";
            }
            else
            {
                WindowStyle = _oldStyle;
                ResizeMode = _oldResize;
                WindowState = _oldState;

                MainMenu.Visibility = Visibility.Visible;

                _isAppFullscreen = false;
                TopStatus.Text = "Pencere modu";
            }

            RefreshPanelsAfterWindowModeChange();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DebouncedRefreshPanels();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            DebouncedRefreshPanels();
        }

        private void DebouncedRefreshPanels()
        {
            _resizeDebounceTimer?.Stop();

            _resizeDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };

            _resizeDebounceTimer.Tick += (_, __) =>
            {
                _resizeDebounceTimer?.Stop();
                _resizeDebounceTimer = null;
                RefreshVisiblePanels();
            };

            _resizeDebounceTimer.Start();
        }

        private void RefreshPanelsAfterWindowModeChange()
        {
            ForceRelayout();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshVisiblePanels();
            }), DispatcherPriority.Background);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshVisiblePanels();
            }), DispatcherPriority.Render);
        }

        private void RefreshVisiblePanels()
        {
            if (_isPanelMaximized && _maximizedPanel != null)
            {
                _maximizedPanel.SetMaximized(true);
                return;
            }

            foreach (var child in RootGrid.Children)
            {
                if (child is PanelHost panel)
                    panel.SetMaximized(false);
            }
        }

        private void ForceRelayout()
        {
            RootGrid.InvalidateMeasure();
            RootGrid.InvalidateArrange();
            RootGrid.UpdateLayout();

            InvalidateMeasure();
            InvalidateArrange();
            UpdateLayout();
        }

        // =============================
        // CONFIG: sunucudan oku, değiştiyse uygula; hata olursa geri çekilerek yeniden dene
        // =============================
        private void ScheduleNextRefresh(TimeSpan delay)
        {
            _configTimer.Stop();
            _configTimer.Interval = delay;
            _configTimer.Start();
        }

        private async void ConfigTimer_Tick(object? sender, EventArgs e)
        {
            _configTimer.Stop();
            await RefreshConfigAsync();
        }

        private async Task RefreshConfigAsync()
        {
            if (_refreshing)
                return;
            _refreshing = true;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = await _configClient.FetchAsync(_settings, cts.Token);

                if (result.Config != null && result.Json != _appliedConfigJson)
                {
                    ApplyConfig(result.Config);
                    _appliedConfigJson = result.Json;
                }

                switch (result.Status)
                {
                    case ConfigFetchStatus.Live:
                        if (_consecutiveFailures > 0)
                            DashboardLog.Info($"Sunucu bağlantısı yeniden kuruldu ({_consecutiveFailures} başarısız denemeden sonra).");
                        _consecutiveFailures = 0;
                        TopStatus.Text = result.Config!.Panels.Count == 0
                            ? $"Ekran {_settings.Screen}: tanımlı panel yok"
                            : $"Ekran {_settings.Screen} · güncellendi {DateTime.Now:HH:mm:ss}";
                        ScheduleNextRefresh(_settings.RefreshInterval);
                        break;

                    default:
                        _consecutiveFailures++;
                        var delay = RetryPolicy.Delay(_consecutiveFailures);
                        DashboardLog.Warn($"Yapılandırma alınamadı (deneme {_consecutiveFailures}): {result.Error}");
                        TopStatus.Text = result.Status == ConfigFetchStatus.Cached
                            ? $"Sunucuya ulaşılamıyor – son bilinen yapılandırma gösteriliyor · yeniden deneme {delay.TotalSeconds:0} sn"
                            : $"Sunucuya ulaşılamıyor · yeniden deneme {delay.TotalSeconds:0} sn";
                        ScheduleNextRefresh(delay);
                        break;
                }
            }
            catch (Exception ex)
            {
                // Kiosk ekranında hata kutusu açılmaz; log + durum çubuğu + yeniden deneme.
                _consecutiveFailures++;
                DashboardLog.Error("RefreshConfigAsync", ex);
                TopStatus.Text = "Yapılandırma hatası – yeniden denenecek";
                ScheduleNextRefresh(RetryPolicy.Delay(_consecutiveFailures));
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void ApplyConfig(DashboardConfig cfg)
        {
            _isPanelMaximized = false;
            _maximizedPanel = null;

            foreach (var host in new[] { p1, p2, p3, p4 })
                host.Reset();

            BuildLayout();

            foreach (var panel in cfg.Panels)
            {
                var host = panel.Position switch
                {
                    1 => p1,
                    2 => p2,
                    3 => p3,
                    4 => p4,
                    _ => null,
                };
                host?.Load(panel.Id, panel.Title, panel.Url, panel.RefreshIntervalSec, panel.ZoomFactor);
            }

            HookEvents();
            DashboardLog.Info($"Yapılandırma uygulandı: ekran={_settings.Screen}, panel={cfg.Panels.Count}.");
        }

        private void HookEvents()
        {
            if (_eventsHooked)
                return;

            _eventsHooked = true;

            p1.OnMaximizeRequested += HandlePanelMaximize;
            p2.OnMaximizeRequested += HandlePanelMaximize;
            p3.OnMaximizeRequested += HandlePanelMaximize;
            p4.OnMaximizeRequested += HandlePanelMaximize;
        }

        private void BuildLayout()
        {
            RootGrid.Children.Clear();
            RootGrid.RowDefinitions.Clear();
            RootGrid.ColumnDefinitions.Clear();

            if (_isPanelMaximized && _maximizedPanel != null)
            {
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                _maximizedPanel.SetMaximized(true);

                Grid.SetRow(_maximizedPanel, 0);
                Grid.SetColumn(_maximizedPanel, 0);
                RootGrid.Children.Add(_maximizedPanel);

                ForceRelayout();
                return;
            }

            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            p1.SetMaximized(false);
            p2.SetMaximized(false);
            p3.SetMaximized(false);
            p4.SetMaximized(false);

            Grid.SetRow(p1, 0); Grid.SetColumn(p1, 0);
            Grid.SetRow(p2, 0); Grid.SetColumn(p2, 1);
            Grid.SetRow(p3, 1); Grid.SetColumn(p3, 0);
            Grid.SetRow(p4, 1); Grid.SetColumn(p4, 1);

            RootGrid.Children.Add(p1);
            RootGrid.Children.Add(p2);
            RootGrid.Children.Add(p3);
            RootGrid.Children.Add(p4);

            ForceRelayout();
        }

        private void HandlePanelMaximize(PanelHost panel)
        {
            if (!_isPanelMaximized)
            {
                _isPanelMaximized = true;
                _maximizedPanel = panel;
            }
            else if (_maximizedPanel == panel)
            {
                _isPanelMaximized = false;
                _maximizedPanel = null;
            }
            else
            {
                if (_maximizedPanel != null)
                    _maximizedPanel.SetMaximized(false);

                _maximizedPanel = panel;
                _isPanelMaximized = true;
            }

            BuildLayout();
            RefreshPanelsAfterWindowModeChange();
        }

        private void RestorePanelFullscreen()
        {
            _isPanelMaximized = false;

            if (_maximizedPanel != null)
                _maximizedPanel.SetMaximized(false);

            _maximizedPanel = null;

            BuildLayout();
            RefreshPanelsAfterWindowModeChange();
        }

    }
}
