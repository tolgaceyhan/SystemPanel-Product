using System;
using System.IO;
using System.Windows;
using SystemPanel.Dashboard.Core;

namespace SystemPanel.Dashboard
{
    /// <summary>
    /// İlk kurulum: sunucu adresi + ekran kodu. Değerler doğrulanır ve
    /// %ProgramData%\SystemPanel\Dashboard\appsettings.json dosyasına yazılır.
    /// (Toplu dağıtımda dosya önceden hazırlanırsa bu pencere hiç açılmaz.)
    /// </summary>
    public partial class ScreenSetupWindow : Window
    {
        private readonly DashboardPaths _paths;
        private readonly DashboardSettings _settings;

        public ScreenSetupWindow(DashboardPaths paths, DashboardSettings current)
        {
            InitializeComponent();
            _paths = paths;
            _settings = current;

            ApiBaseUrlBox.Text = current.ApiBaseUrl ?? "https://";
            ScreenBox.Text = string.IsNullOrWhiteSpace(current.Screen) ? "A" : current.Screen;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _settings.ApiBaseUrl = ApiBaseUrlBox.Text.Trim();
            _settings.Screen = ScreenBox.Text.Trim();

            var error = _settings.Validate();
            if (error != null)
            {
                ShowError(error);
                return;
            }

            try
            {
                _settings.Save(_paths.SettingsFile);
                DashboardLog.Info($"Kurulum kaydedildi: ekran={_settings.Screen}, sunucu={_settings.ApiBaseUrl}");
                DialogResult = true;
                Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DashboardLog.Error("Kurulum kaydedilemedi", ex);
                ShowError("Ayar dosyası yazılamadı: " + ex.Message + Environment.NewLine + _paths.SettingsFile);
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
