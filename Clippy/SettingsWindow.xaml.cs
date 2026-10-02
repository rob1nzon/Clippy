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

        public SettingsWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            ServerKeyBox.Password = App.Current.Services.GetRequiredService<IKeyService>().GetKey();
            StartupToggle.IsEnabled = false;
            StartupErrorText.Text = "For this EXE, add a shortcut to shell:startup to run on login.";
            StartupErrorText.Visibility = Visibility.Visible;
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
