using CubeKit.UI.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinUIEx;
using Windows.Win32;
using WinRT.Interop;
using Clippy.Windows;
using WinUIEx.Messaging;
using Windows.Win32.UI.WindowsAndMessaging;
using Microsoft.UI;
using Clippy.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Clippy.Services;
using Clippy.Helpers;
using Clippy.Core.Services;
using Windows.UI.Input.Preview.Injection;
using Windows.UI.Input;
using Windows.Devices.Input;
using Windows.System;
using Windows.UI.Core;
using System.Diagnostics.Eventing.Reader;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.WS;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Interop.Windows.GWL;
using static TerraFX.Interop.Windows.SWP;
using static TerraFX.Interop.Windows.SW;
using System.Reflection.Metadata;
using Clippy.Core.Classes;
using System.ComponentModel;
using Windows.Graphics;
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Clippy
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : WindowEx
    {
        private SettingsService Settings = (SettingsService)App.Current.Services.GetService<ISettingsService>();
        private ClippyViewModel Clippy = App.Current.Services.GetService<ClippyViewModel>();
        WindowMessageMonitor m;
        private TrayService tray;

        public MainWindow()
        {
            this.InitializeComponent();
            m = new(this);
            unsafe
            {

				var hwnd = (HWND)this.GetWindowHandle();
				int lExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
				SetWindowLong(hwnd, GWL_EXSTYLE, lExStyle | WS_EX_LAYERED);
			}
            m.WindowMessageReceived += WindowMessageReceived;

            SystemBackdrop = new TransparentBackdrop();
            Content.Background = new SolidColorBrush(Colors.Red);
            Content.Background = new SolidColorBrush(Colors.Transparent);

            ClippyKeyboardListener.Setup(this);

            Collapse();

            this.BringToFront();
			if (Clippy.IsPinned) Pin();
			else Unpin();
            Clippy.PropertyChanged += (object sender, System.ComponentModel.PropertyChangedEventArgs e) =>
            {
                if(e.PropertyName == "IsPinned")
                {
                    if (Clippy.IsPinned) Pin();
                    else Unpin();
                }
            };
            tray = new TrayService(this.GetWindowHandle(), () => App.Current.ShowClippy(),
                () => this.Hide(), () => App.Current.OpenSettings(), () => App.Current.ExitApplication());
            tray.SetVisible(Settings.TrayClippy);
            Settings.PropertyChanged += SettingsChanged;
            AppWindow.Closing += (_, args) =>
            {
                if (Settings.TrayClippy && !App.Current.IsExiting)
                {
                    args.Cancel = true;
                    this.Hide();
                }
            };
            Closed += (_, _) =>
            {
                Settings.PropertyChanged -= SettingsChanged;
                DisposeTray();
                m.Dispose();
            };
        }

        public void DisposeTray() => tray?.Dispose();

        private void SettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.TrayClippy)) tray.SetVisible(Settings.TrayClippy);
            if (e.PropertyName == nameof(Settings.ClippySize)) PositionInCorner();
        }

		private unsafe void Pin()
        {
			var presenter = this.AppWindow.Presenter as OverlappedPresenter;
            var hwnd = (HWND)this.GetWindowHandle();
			if (presenter is not null) presenter.IsAlwaysOnTop = true;

			// Add the extended window styles for always on top
			int lExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
			SetWindowLong(hwnd, GWL_EXSTYLE, lExStyle | WS_EX_TOPMOST);

			// Move window to top
			SetWindowPos(hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
		}

        private unsafe void Unpin()
        {
			var presenter = this.AppWindow.Presenter as OverlappedPresenter;
			var hwnd = (HWND)this.GetWindowHandle();
			if (presenter is not null) presenter.IsAlwaysOnTop = false;

			// Add the extended window styles for always on top
			int lExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
			SetWindowLong(hwnd, GWL_EXSTYLE, lExStyle & ~WS_EX_TOPMOST);

			this.BringToFront();
		}

        private void WindowMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (tray?.HandleMessage(e.Message.MessageId, e.Message.LParam) == true)
            {
                e.Handled = true;
                e.Result = 0;
                return;
            }
            if (e.Message.MessageId == 0x02e0 || e.Message.MessageId == 0x007e || e.Message.MessageId == 0x001a)
                DispatcherQueue.TryEnqueue(PositionInCorner);
            if (e.Message.MessageId == PInvoke.WM_ERASEBKGND)
            {
                e.Handled = true;
                e.Result = 1;
            }
        }

        public void PositionInCorner()
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            var work = display.WorkArea;
            var scale = this.GetDpiForWindow() / 96d;
            var bounds = CornerPlacement.Calculate(work.X, work.Y, work.Width, work.Height, scale,
                Clippy.IsClippyEnabled ? Math.Max(380, Settings.ClippySize + 24) : Settings.ClippySize + 24,
                Clippy.IsClippyEnabled ? 1000 : Settings.ClippySize + 16);
            AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height), display);
            Content.MaxHeight = bounds.Height / scale;
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            App.Current.OpenSettings();
        }

        private Visibility BtoV(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

        private void Clippy_Checked(object sender, RoutedEventArgs e) => Expand();

        private void Clippy_Unchecked(object sender, RoutedEventArgs e) => Collapse();

        private void Collapse()
        {
            PositionInCorner();
        }

        private void Expand()
        {
            PositionInCorner();
        }

		// Bool to Visibility
		public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

		// Bool to inverted visibility
		public Visibility InvertBoolToVis(bool b) => b ? Visibility.Collapsed : Visibility.Visible;

		private void Background_PointerPressed(object sender, PointerRoutedEventArgs e) => ClippyInputHelper.PointerPress(this.GetWindowHandle());

        private void Background_PointerMoved(object sender, PointerRoutedEventArgs e) => ClippyInputHelper.PointerHover(this.GetWindowHandle());

		private void TextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
		{
			// NOTE - AcceptsReturn is set to true in XAML.
			/*if (e.Key == VirtualKey.Enter)
			{
				// If SHIFT is pressed, this next IF is skipped over, so the default behavior of "AcceptsReturn" is used.
				var keyState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Shift);
				if ((keyState & CoreVirtualKeyStates.Down) != CoreVirtualKeyStates.Down)
					e.Handled = true; // Mark the event as handled
			} */
		}

		private void TextBox_KeyUp(object sender, KeyRoutedEventArgs e)
		{
			/*if (e.Key == VirtualKey.Enter)
			{
				// If SHIFT is pressed, this next IF is skipped over, so the default behavior of "AcceptsReturn" is used.
				var keyState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Shift);
				if ((keyState & CoreVirtualKeyStates.Down) != CoreVirtualKeyStates.Down)
				{
					// Force update x:Bind text
					Clippy.CurrentText = (sender as TextBox).Text;

					if (Clippy.SendPromptCommand.CanExecute(this))
						Clippy.SendPromptCommand.Execute(this);
				}
			} */
		}

		private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApplication();

		private void Hide_Click(object sender, RoutedEventArgs e)
		{
            this.Hide();
		}
	}
}
