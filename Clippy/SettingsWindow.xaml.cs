using Clippy.Core.Services;
using Clippy.Services;
using Clippy.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;
using WinUIEx;
using WinRT;
using Windows.ApplicationModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Clippy
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsWindow : WindowEx
    {
        private SettingsService Settings = (SettingsService)App.Current.Services.GetService<ISettingsService>();
        private ScreenAdviceController Advice = App.Current.Services.GetRequiredService<ScreenAdviceController>();

        private void ScreenAdvice_Toggled(object sender, RoutedEventArgs e)
        {
            // Switching off takes effect immediately, even before Save is clicked.
            if (ScreenAdviceSwitch != null && !ScreenAdviceSwitch.IsOn) Settings.ScreenAdviceEnabled = false;
        }

        private async void SaveScreenAdvice_Click(object sender, RoutedEventArgs e)
        {
            if (double.IsNaN(ScreenAdviceIntervalBox.Value)) return;
            if (ScreenAdviceSwitch.IsOn && !Settings.ScreenAdviceEnabled)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = SettingsPanel.XamlRoot, Title = "Allow automatic screenshots?",
                    Content = "Clippy will periodically capture the monitor it sits on and send the image to:\n" + Settings.ServerUrl +
                        "\n\nThe screenshot can include passwords, messages, documents and other private data. There is no automatic redaction. The server may retain data. Images stay in memory on this PC and are not saved to disk or added to chat history. A vision model is required. You can disable this at any time; changing the server URL disables it automatically.",
                    PrimaryButtonText = "Allow screenshots", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                { ScreenAdviceSwitch.IsOn = false; return; }
            }
            Settings.ScreenAdviceIntervalMinutes = (int)ScreenAdviceIntervalBox.Value;
            Settings.ScreenAdviceEnabled = ScreenAdviceSwitch.IsOn;
        }

        private async void AdviceNow_Click(object sender, RoutedEventArgs e)
        {
            AdviceNowButton.IsEnabled = false;
            try { await App.Current.RequestScreenAdviceAsync(); }
            finally { AdviceNowButton.IsEnabled = true; }
        }

        public SettingsWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            ServerKeyBox.Password = App.Current.Services.GetRequiredService<IKeyService>().GetKey();
            McpKeyBox.Password = new KeyService("Clippy.MCP").GetKey();
            StartupToggle.IsEnabled = false;
            StartupErrorText.Text = "For this EXE, add a shortcut to shell:startup to run on login.";
            StartupErrorText.Visibility = Visibility.Visible;
		}

        private async void SaveMcp_Click(object sender, RoutedEventArgs e)
        {
            var url = McpUrlBox.Text.Trim();
            if (McpEnabledSwitch.IsOn && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")))
            {
                McpStatus.Text = "Enter a valid MCP HTTP endpoint.";
                return;
            }
            McpSaveButton.IsEnabled = false;
            try
            {
                new KeyService("Clippy.MCP").SetKey(McpKeyBox.Password);
                Settings.McpServerUrl = url;
                Settings.McpEnabled = McpEnabledSwitch.IsOn;
                if (!Settings.McpEnabled)
                {
                    await ((McpToolService)App.Current.Services.GetRequiredService<IToolService>()).DisposeAsync();
                    McpStatus.Text = "MCP disabled.";
                    return;
                }
                McpStatus.Text = "Connecting...";
                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                var tools = await App.Current.Services.GetRequiredService<IToolService>().ListToolsAsync(timeout.Token);
                McpStatus.Text = $"Connected: {tools.Count} tools. " + string.Join(", ", tools.Select(t => t.Name));
            }
            catch (Exception error) { McpStatus.Text = "MCP connection error: " + error.Message; }
            finally { McpSaveButton.IsEnabled = true; }
        }

        private void SaveConnection_Click(object sender, RoutedEventArgs e)
        {
            var url = ServerUrlBox.Text.Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                ConnectionStatus.Text = "Enter a valid HTTP server URL, for example http://192.168.1.10:8080/v1.";
                return;
            }
            if (string.IsNullOrWhiteSpace(ModelBox.Text) || double.IsNaN(MaxTokensBox.Value))
            {
                ConnectionStatus.Text = "Enter a model name and maximum response tokens.";
                return;
            }
            try
            {
                App.Current.Services.GetRequiredService<IKeyService>().SetKey(ServerKeyBox.Password);
                Settings.ServerUrl = url;
                Settings.Model = ModelBox.Text.Trim();
                Settings.Tokens = (int)MaxTokensBox.Value;
                ConnectionStatus.Text = "Connection saved. New messages will use these settings.";
            }
            catch (Exception error) { ConnectionStatus.Text = "Could not save connection: " + error.Message; }
        }

		private async void Star_Click(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri("ms-windows-store://review/?ProductId=9NWK37S35V5T"));

		private async void Hub_Click(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri("https://discord.gg/3WYcKat"));

        private async void GitHub_Click(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri("https://github.com/FireCubeStudios/Clippy"));

        private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApplication();

	}
}
