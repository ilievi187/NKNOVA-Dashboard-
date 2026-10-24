using Avalonia.Interactivity;
using Avalonia.Controls;
using Nolvus.Core.Enums;
using Nolvus.Core.Frames;
using Nolvus.Core.Interfaces;
using Nolvus.Core.Services;
using Nolvus.Dashboard.Controls;
using Nolvus.Services.Globals;
using Nolvus.Services.Settings;
using Avalonia.Threading;
using System.Diagnostics;
using Avalonia;

namespace Nolvus.Dashboard.Frames.Settings
{
    public partial class GlobalSettingsFrame : DashboardFrame
    {
        public GlobalSettingsFrame(IDashboard Dashboard, FrameParameters Params) :base(Dashboard, Params)
        {
            InitializeComponent();

            BtnBack.Click += BtnBack_Click;
            BtnSaveMegaInfo.Click += BtnSaveMegaInfo_Click;
        }

        protected override void OnLoad()
        {
            ServiceSingleton.Dashboard.Title("Nolvus Dashboard - [Settings]");
            ServiceSingleton.Dashboard.Info("Global Settings");
            TglBtnAnonymous.IsChecked = true;
        }

        protected override async Task OnLoadedAsync()
        {
            try
            {
                var settings = await Task.Run(() => new
                {
                    MegaEmail = ServiceSingleton.Globals.MegaEmail,
                    MegaPassword = ServiceSingleton.Globals.MegaPassword,
                    MegaAnonymousConnection = ServiceSingleton.Globals.MegaAnonymousConnection,
                    NexusAutoClick = ServiceSingleton.Settings.NexusAutoClick
                });

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TxtBxUserName.Text = settings.MegaEmail;
                    TxtBxPassword.Text = settings.MegaPassword;
                    TglBtnAnonymous.IsChecked = settings.MegaAnonymousConnection;

                    DrpDwnLstProcessCount.ItemsSource = GetProcessorCores();
                    DrpDwnLstProcessCount.SelectedIndex = CoreIndex(GetProcessorCores());
                    TglBtnAutoClick.IsChecked = settings.NexusAutoClick;
                });
            }
            catch
            {
                await ServiceSingleton.Dashboard.Error(
                    "Error during global settings loading",
                    "Saved credentials could not be loaded securely. On Linux, verify that the user keyring is installed and unlocked.");
            }
        }

        private int CoreIndex(List<int> Cores)
        {
            var Index = Cores.FindIndex(x => x == ServiceSingleton.Settings.ProcessCount);

            return Index == -1 ? 0 : Index;
        }

        protected List<int> GetProcessorCores()
        {
            List<int> Result = new List<int>();

            for (var i = 0; i < Environment.ProcessorCount; i++)
            {
                Result.Add(i + 1);
            }

            if (Result.Where(x => x == ServiceSingleton.Settings.ProcessCount).ToList().Count == 0)
            {
                Result.Add(ServiceSingleton.Settings.ProcessCount);
            }

            return Result;
        }

        private async void BtnBack_Click(object? sender, RoutedEventArgs e)
        {
            var window = TopLevel.GetTopLevel(this) as DashboardWindow;
            var returnType = window?.SettingsReturnFrameType;
            var returnParameters = window?.SettingsReturnFrameParameters;

            if (returnType != null && typeof(DashboardFrame).IsAssignableFrom(returnType))
            {
                var method = typeof(IDashboard).GetMethod("LoadFrame")!.MakeGenericMethod(returnType);
                method.Invoke(ServiceSingleton.Dashboard, new object?[] { returnParameters });
            }
            else
            {
                await ServiceSingleton.Dashboard.LoadFrameAsync<StartFrame>();
            }
        }

        private async void BtnSaveMegaInfo_Click(object? sender, RoutedEventArgs e)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (TglBtnAnonymous.IsChecked == true && TxtBxUserName.Text.Trim() == string.Empty && TxtBxPassword.Text.Trim() == string.Empty)
            {
                NolvusMessageBox.Show(owner, "Error", "Please enter your credentials!", MessageBoxType.Error);
            }
            else
            {
                try
                {
                    if (TglBtnAnonymous.IsChecked == false)
                    {
                        await UpdateButton("Validating...");
                        await ServiceSingleton.Files.AuthenticateToMegaApi(TxtBxUserName.Text, TxtBxPassword.Text);
                    }
                }
                catch
                {
                    await UpdateButton("Save");
                    NolvusMessageBox.Show(owner, "Error", "Unable to connect to mega.nz! Please review your credentials", MessageBoxType.Error);
                    return;
                }

                try
                {
                    bool anonymousConnection = TglBtnAnonymous.IsChecked == true;
                    string email = TxtBxUserName.Text ?? string.Empty;
                    string password = TxtBxPassword.Text ?? string.Empty;
                    await UpdateButton("Saving securely...");

                    await Task.Run(() =>
                    {
                        string protectedPassword = string.IsNullOrEmpty(password)
                            ? string.Empty
                            : ServiceSingleton.Lib.EncryptString(password);

                        SettingsService.UpdateIniFileAtomically(SettingsService.IniFile, data =>
                        {
                            data[GlobalsService.MegaSection][GlobalsService.MegaAnonymous] = anonymousConnection.ToString();
                            if (!string.IsNullOrEmpty(email))
                                data[GlobalsService.MegaSection][GlobalsService.MegaUserName] = email;
                            if (!string.IsNullOrEmpty(password))
                                data[GlobalsService.MegaSection][GlobalsService.MegaPswd] = protectedPassword;
                        });
                    });

                    NolvusMessageBox.Show(owner, "Information", "Your mega.nz configuration has been validating and saved", MessageBoxType.Info);
                    await UpdateButton("Save");
                }
                catch
                {
                    await UpdateButton("Save");
                    NolvusMessageBox.Show(
                        owner,
                        "Error",
                        "Unable to save Mega settings securely. On Linux, verify that the user keyring is installed and unlocked. Existing settings were left unchanged.",
                        MessageBoxType.Error);
                }
            }
        }

        private void OpenMega(object? sender, RoutedEventArgs e)
        {
            var url = "https://mega.nz/register";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private async Task UpdateButton(string Text)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                BtnSaveMegaInfo.Content = Text;
            });
        }

        private void TglBtnAnonymous_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ToggleSwitch.IsCheckedProperty)
            {
                bool anonymous = TglBtnAnonymous.IsChecked == true;

                // Disable when anonymous, enable when not anonymous
                TxtBxUserName.IsEnabled = !anonymous;
                TxtBxPassword.IsEnabled = !anonymous;
                PnlMessage.IsEnabled = !anonymous;
            }
        }

        private void OnProcessorCountChanged(object? sender, RoutedEventArgs e)
        {
            ServiceSingleton.Settings.ProcessCount = (int)DrpDwnLstProcessCount.SelectedItem!;
        }

        private void TglBtnAutoClick_Changed(object? sender, RoutedEventArgs e)
        {
            ServiceSingleton.Settings.NexusAutoClick = TglBtnAutoClick.IsChecked == true;
        }
    }
}
