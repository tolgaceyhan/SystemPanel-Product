using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SystemPanel.Dashboard.Core;

namespace SystemPanel.Dashboard.Controls
{
    public partial class PanelHost : UserControl
    {
        public event Action<PanelHost>? OnMaximizeRequested;

        public int PanelId { get; private set; }
        public bool IsEditMode { get; private set; }
        public bool IsMaximized { get; private set; }

        private string? _url;
        private int? _refreshSec;

        private DispatcherTimer? _refreshTimer;
        private bool _isInitialized;
        private bool _scriptInjected;

        private Point _dragStart;

        private double? _zoomFactor;
        private double _baseZoomFactor = 1.0;

        // Tasarım referans panel boyutu
        private const double BaseWidth = 1920.0;
        private const double BaseHeight = 1080.0;
        private const double TopBarHeight = 32.0;

        public PanelHost()
        {
            InitializeComponent();

            Loaded += PanelHost_Loaded;
            Unloaded += PanelHost_Unloaded;
            SizeChanged += PanelHost_SizeChanged;
        }

        // =============================
        // LOAD PANEL
        // =============================
        public void Load(int id, string title, string url, int? refreshSec, double? zoomFactor)
        {
            PanelId = id;
            _url = url;
            _refreshSec = refreshSec;
            _zoomFactor = zoomFactor;
            _baseZoomFactor = zoomFactor ?? 1.0;

            TitleText.Text = title;

            _ = InitWebViewSafe();
            SetupRefreshTimer();
        }

        // =============================
        // RESET (layout rebuild öncesi)
        // =============================
        public void Reset()
        {
            try
            {
                if (Web.CoreWebView2 != null)
                    Web.Stop();

                Web.Source = null;
            }
            catch
            {
            }

            HideError();
            SetMaximized(false);
        }

        // =============================
        // EDIT MODE
        // =============================
        public void SetEditMode(bool enabled)
        {
            IsEditMode = enabled;
            EditModeBorder.Visibility = enabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // =============================
        // MAXIMIZE STATE
        // =============================
        public void SetMaximized(bool maximized)
        {
            IsMaximized = maximized;
            MaximizeButton.Content = maximized ? "❐" : "⛶";

            Dispatcher.BeginInvoke(new Action(ApplyResponsiveZoom), DispatcherPriority.Background);
        }

        // =============================
        // WEBVIEW SAFE INIT
        // =============================
        private async Task InitWebViewSafe()
        {
            try
            {
                HideError();
                SetStatus("● Yükleniyor", "LightBlue");

                if (!_isInitialized)
                {
                    await Web.EnsureCoreWebView2Async();

                    if (Web.CoreWebView2 == null)
                        throw new InvalidOperationException("WebView2 başlatılamadı.");

                    // Güvenlik / kiosk
                    Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    Web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                    Web.CoreWebView2.Settings.IsZoomControlEnabled = true;

                    // Yatay scroll kapalı, dikey scroll açık
                    if (!_scriptInjected)
                    {
                        await Web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                            (function () {
                                try {
                                    document.documentElement.style.overflowX = 'hidden';
                                    document.documentElement.style.overflowY = 'auto';
                                    document.body.style.overflowX = 'hidden';
                                    document.body.style.overflowY = 'auto';
                                    document.documentElement.style.margin = '0';
                                    document.body.style.margin = '0';
                                } catch (e) { }
                            })();
                        ");

                        _scriptInjected = true;
                    }

                    Web.NavigationCompleted -= Web_NavigationCompleted;
                    Web.NavigationCompleted += Web_NavigationCompleted;

                    _isInitialized = true;
                }

                ApplyResponsiveZoom();

                if (!string.IsNullOrWhiteSpace(_url))
                {
                    var targetUri = new Uri(_url);

                    if (Web.Source == null || Web.Source.AbsoluteUri != targetUri.AbsoluteUri)
                        Web.Source = targetUri;
                    else
                        Web.Reload();
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                Log("InitWebViewSafe ERROR: " + ex);
            }
        }

        private void Web_NavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                SetStatus("● Canlı", "LightGreen");
                ApplyResponsiveZoom();
            }
            else
            {
                ShowError(e.WebErrorStatus.ToString());
                Log("Navigation ERROR: " + e.WebErrorStatus);
            }
        }

        // =============================
        // RESPONSIVE ZOOM
        // =============================
        private void PanelHost_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyResponsiveZoom();
        }

        private void PanelHost_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _refreshTimer?.Stop();
            }
            catch
            {
            }
        }

        private void PanelHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveZoom();
        }

        private void ApplyResponsiveZoom()
        {
            try
            {
                if (!_isInitialized || Web.CoreWebView2 == null)
                    return;

                var usableWidth = ActualWidth;
                var usableHeight = ActualHeight - TopBarHeight;

                if (usableWidth <= 0 || usableHeight <= 0)
                    return;

                var scaleX = usableWidth / BaseWidth;
                var scaleY = usableHeight / BaseHeight;
                var scale = Math.Min(scaleX, scaleY);

                var finalZoom = _baseZoomFactor * scale;

                if (finalZoom < 0.40) finalZoom = 0.40;
                if (finalZoom > 1.40) finalZoom = 1.40;

                if (Math.Abs(Web.ZoomFactor - finalZoom) > 0.01)
                    Web.ZoomFactor = finalZoom;
            }
            catch (Exception ex)
            {
                Log("ApplyResponsiveZoom ERROR: " + ex.Message);
            }
        }

        // =============================
        // REFRESH
        // =============================
        private void SetupRefreshTimer()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;

            if (_refreshSec == null || _refreshSec <= 0)
                return;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(_refreshSec.Value)
            };

            _refreshTimer.Tick += async (_, __) =>
            {
                try
                {
                    if (Web.CoreWebView2 != null)
                        Web.Reload();
                    else
                        await InitWebViewSafe();
                }
                catch (Exception ex)
                {
                    Log("Refresh timer ERROR: " + ex.Message);
                }
            };

            _refreshTimer.Start();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await InitWebViewSafe();
        }

        private async void Retry_Click(object sender, RoutedEventArgs e)
        {
            await InitWebViewSafe();
        }

        // =============================
        // STATUS / ERROR
        // =============================
        private void SetStatus(string text, string color)
        {
            StatusText.Text = text;
            StatusText.Foreground =
                (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter()
                    .ConvertFromString(color)!;
        }

        private void ShowError(string message)
        {
            SetStatus("● Hata", "OrangeRed");
            ErrorText.Text = message;
            ErrorOverlay.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorOverlay.Visibility = Visibility.Collapsed;
        }

        // =============================
        // MAXIMIZE
        // =============================
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            OnMaximizeRequested?.Invoke(this);
        }

        // =============================
        // LOG (ortak günlük; bkz. Core/DashboardLog)
        // =============================
        private void Log(string message) => DashboardLog.Warn($"[Panel {PanelId}] {message}");

        // =============================
        // DRAG SOURCE (Edit Mode only)
        // =============================
        private void Panel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEditMode || IsMaximized) return;

            _dragStart = e.GetPosition(this);
        }

        private void Panel_MouseMove(object sender, MouseEventArgs e)
        {
            if (!IsEditMode || IsMaximized) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _dragStart.X) < 6 &&
                Math.Abs(pos.Y - _dragStart.Y) < 6)
                return;

            DragDrop.DoDragDrop(this, this, DragDropEffects.Move);
        }
    }
}