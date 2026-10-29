using Avalonia.Controls;
using Nolvus.Core.Frames;
using Nolvus.Core.Services;
using Nolvus.Core.Interfaces;
using Nolvus.Dashboard.Core;
using Vcc.Nolvus.Api.Installer.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Nolvus.Dashboard.Frames.Settings
{
    public partial class NolvusFrame : DashboardFrame
    {
        public NolvusFrame(IDashboard dashboard, FrameParameters parameters)
            : base(dashboard, parameters)
        {
            InitializeComponent();
            HookEvents();
        }

        private void HookEvents()
        {
            BtnContinue.Click += BtnContinue_Click;
            BtnPrevious.Click += BtnPrevious_Click;
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            LblCheck.IsVisible = false;

            if (string.IsNullOrWhiteSpace(SettingsCache.NolvusUser))
            {
                try { SettingsCache.NolvusUser = ServiceSingleton.Globals.NolvusUserName; }
                catch { SettingsCache.NolvusUser = string.Empty; }
            }

            if (string.IsNullOrWhiteSpace(SettingsCache.GameDirectory))
            {
                try { SettingsCache.GameDirectory = ServiceSingleton.Settings.GetIniValue("Path", "GamePath") ?? string.Empty; }
                catch { SettingsCache.GameDirectory = string.Empty; }
            }

            TxtBxUserName.Text = SettingsCache.NolvusUser;
            TxtBxPassword.Text = SettingsCache.NolvusPassword;

            if (SettingsCache.NolvusCredentialReentryRequired)
                SetCheck(SettingsCache.NolvusCredentialReentryMessage, true);
        }

        private void SetCheck(string value, bool error)
        {
            LblCheck.Foreground = error
                ? Avalonia.Media.Brushes.IndianRed
                : Avalonia.Media.Brushes.White;

            LblCheck.Text = value;
            LblCheck.IsVisible = true;
        }

        private void ShowLoading()
        {
            BtnContinue.Content = "Validating...";
            BtnContinue.IsEnabled = false;
        }

        private void HideLoading()
        {
            BtnContinue.Content = "Next";
            BtnContinue.IsEnabled = true;
        }

        private async Task<bool> NolvusAuthenticate()
        {
            string userName = TxtBxUserName.Text ?? string.Empty;
            string password = TxtBxPassword.Text ?? string.Empty;
            ApiManager.Init(ServiceSingleton.Globals.ApiUrl, "v1", userName, password);

            return await ApiManager.Service.Installer.Authenticate(userName, password);
        }

        private async void BtnContinue_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtBxUserName.Text) ||
                string.IsNullOrWhiteSpace(TxtBxPassword.Text))
            {
                SetCheck("You must enter your user name/password!", true);
                return;
            }

            ShowLoading();
            SetCheck("Connecting to Nolvus...", false);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!await NolvusAuthenticate())
                {
                    HideLoading();
                    SetCheck("Invalid user name / password or your account has not been activated!", true);
                    return;
                }

                SettingsCache.NolvusUser = TxtBxUserName.Text;
                SettingsCache.NolvusPassword = TxtBxPassword.Text;

                string? nexusReference = null;
                string? nolvusReference = null;
                try
                {
                    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NolvusDashboard.ini");
                    string gameDirectory = SettingsCache.GameDirectory;
                    string nexusApiKey = SettingsCache.NexusApiKey;
                    string nolvusUser = SettingsCache.NolvusUser;
                    string nolvusPassword = SettingsCache.NolvusPassword;

                    await Task.Run(() =>
                    {
                        string newNexusReference = ServiceSingleton.Lib.StoreCredential(nexusApiKey);
                        nexusReference = newNexusReference;
                        string newNolvusReference = ServiceSingleton.Lib.StoreCredential(nolvusPassword);
                        nolvusReference = newNolvusReference;

                        Nolvus.Services.Settings.SettingsService.SaveNolvusCredentialsAtomically(
                            settingsPath,
                            gameDirectory,
                            newNexusReference,
                            nolvusUser,
                            newNolvusReference,
                            Environment.ProcessorCount);
                    });
                }
                catch
                {
                    try
                    {
                        await Task.Run(() =>
                        {
                            if (!string.IsNullOrEmpty(nexusReference))
                                ServiceSingleton.Lib.DeleteCredentialReference(nexusReference);
                            if (!string.IsNullOrEmpty(nolvusReference))
                                ServiceSingleton.Lib.DeleteCredentialReference(nolvusReference);
                        });
                    }
                    catch
                    {
                        // Keep the original INI and never expose storage errors or credential data in logs.
                    }

                    HideLoading();
                    SetCheck(
                        "Could not save credentials securely. On Linux, verify that a Secret Service-compatible keyring is installed and unlocked. Existing settings were not changed.",
                        true);
                    return;
                }

                SettingsCache.NexusApiKey = string.Empty;
                SettingsCache.NolvusPassword = string.Empty;
                SettingsCache.NolvusCredentialReentryRequired = false;
                SettingsCache.NolvusCredentialReentryMessage = string.Empty;
                TxtBxPassword.Text = string.Empty;
                await ServiceSingleton.Dashboard.LoadFrameAsync<StartFrame>();
            });
        }

        private void BtnPrevious_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ServiceSingleton.Dashboard.LoadFrame<NexusFrame>();
        }
    }
}
