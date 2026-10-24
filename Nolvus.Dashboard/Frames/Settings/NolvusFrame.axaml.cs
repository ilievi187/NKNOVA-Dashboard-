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
        private const string IniFile = @"[Path]
GamePath={0}

[Nexus]
ApiKey={1}
UserAgent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.75 Safari/537.36

[Nolvus]
Version=v1
UserName={2}
Password={3}
UserAgent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.75 Safari/537.36 Edge/18.19582

[Process]
Count = {4}
Retry = 3
ErrorsThreshold = 50

[Misc]
ForceAA=False
RefreshInterval=10
";

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

            TxtBxUserName.Text = SettingsCache.NolvusUser;
            TxtBxPassword.Text = SettingsCache.NolvusPassword;
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

                try
                {
                    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NolvusDashboard.ini");
                    string gameDirectory = SettingsCache.GameDirectory;
                    string nexusApiKey = SettingsCache.NexusApiKey;
                    string nolvusUser = SettingsCache.NolvusUser;
                    string nolvusPassword = SettingsCache.NolvusPassword;

                    await Task.Run(() =>
                    {
                        string settingsContents = string.Format(
                            IniFile,
                            gameDirectory,
                            ServiceSingleton.Lib.EncryptString(nexusApiKey),
                            nolvusUser,
                            ServiceSingleton.Lib.EncryptString(nolvusPassword),
                            Environment.ProcessorCount);

                        Nolvus.Services.Settings.SettingsService.WriteTextAtomically(settingsPath, settingsContents);
                    });
                }
                catch
                {
                    HideLoading();
                    SetCheck(
                        "Could not save credentials securely. On Linux, verify that a Secret Service-compatible keyring is installed and unlocked. Existing settings were not changed.",
                        true);
                    return;
                }

                await ServiceSingleton.Dashboard.LoadFrameAsync<StartFrame>();
            });
        }

        private void BtnPrevious_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ServiceSingleton.Dashboard.LoadFrame<NexusFrame>();
        }
    }
}
