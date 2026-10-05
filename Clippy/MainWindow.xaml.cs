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
using System.Threading;
using System.Threading.Tasks;
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
        private WindowInputRegion inputRegion;
        private bool toolDialogOpen;
        private bool contextMenuOpen;
        private readonly ScreenAdviceController advice = App.Current.Services.GetRequiredService<ScreenAdviceController>();
        private readonly DispatcherTimer adviceTimer = new() { Interval = TimeSpan.FromSeconds(30) };
        private readonly DispatcherTimer adviceDismissTimer = new() { Interval = TimeSpan.FromSeconds(25) };
        private CancellationTokenSource screenshotCapture;
        private bool isClosed;

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
            inputRegion = new WindowInputRegion(this.GetWindowHandle());
            Background.LayoutUpdated += (_, _) => UpdateInputRegion();
            Flyout.Opened += (_, _) => { contextMenuOpen = true; UpdateInputRegion(); };
            Flyout.Closed += (_, _) => { contextMenuOpen = false; UpdateInputRegion(); };

            SystemBackdrop = new TransparentBackdrop();
            Content.Background = new SolidColorBrush(Colors.Transparent);

            ClippyKeyboardListener.Setup(this);

            PositionInCorner();

            this.BringToFront();
			if (Clippy.IsPinned) Pin();
			else Unpin();
            Clippy.PropertyChanged += ClippyChanged;
            advice.PropertyChanged += AdviceChanged;
            advice.Reset();
            adviceTimer.Tick += async (_, _) => await RequestScreenAdviceAsync(false);
            adviceDismissTimer.Tick += (_, _) => advice.Dismiss();
            adviceTimer.Start();
            Activated += (_, args) =>
            {
                if (args.WindowActivationState == WindowActivationState.Deactivated || toolDialogOpen || contextMenuOpen) return;
                FocusPrompt();
            };
            tray = new TrayService(this.GetWindowHandle(), () => App.Current.ShowClippy(),
                HideClippy, () => App.Current.OpenSettings(), () => App.Current.ExitApplication());
            tray.SetVisible(Settings.TrayClippy);
            Settings.PropertyChanged += SettingsChanged;
            AppWindow.Closing += (_, args) =>
            {
                if (Settings.TrayClippy && !App.Current.IsExiting)
                {
                    args.Cancel = true;
                    HideClippy();
                }
            };
            Closed += (_, _) =>
            {
                isClosed = true;
                screenshotCapture?.Cancel();
                Settings.PropertyChanged -= SettingsChanged;
                Clippy.PropertyChanged -= ClippyChanged;
                advice.PropertyChanged -= AdviceChanged;
                adviceTimer.Stop();
                adviceDismissTimer.Stop();
                advice.Reset();
                DisposeTray();
                m.Dispose();
            };
        }

        public void DisposeTray() => tray?.Dispose();

        public void OpenChatAndFocus()
        {
            Clippy.IsClippyEnabled = true;
            FocusPrompt();
        }

        public Task RequestScreenAdviceAsync(bool manual)
        {
            var handle = this.GetWindowHandle();
            var allowed = !App.Current.IsExiting && ScreenCaptureService.IsVisible(handle) &&
                (manual || !Clippy.IsClippyEnabled) && !Clippy.SendPromptCommand.IsRunning && !Clippy.IsCapturingScreenshot &&
                Clippy.CurrentScreenshot == null && string.IsNullOrEmpty(Clippy.CurrentText) &&
                !toolDialogOpen && !contextMenuOpen && (manual || !ScreenCaptureService.IsOurForegroundWindow());
            return advice.RunAsync(token => ScreenCaptureService.CaptureJpegAsync(handle, token), allowed, manual);
        }

        private void AdviceChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(advice.Tip)) return;
            adviceDismissTimer.Stop();
            AdviceText.Text = advice.Tip;
            AdviceBubble.Visibility = string.IsNullOrEmpty(advice.Tip) ? Visibility.Collapsed : Visibility.Visible;
            PositionInCorner();
            if (AdviceBubble.Visibility == Visibility.Visible) adviceDismissTimer.Start();
        }

        private void DismissAdvice_Click(object sender, RoutedEventArgs e) => advice.Dismiss();

        private bool CanRequestAdvice(bool enabled, bool adviceRunning, bool chatRunning, string draft, bool capturing, byte[] screenshot) =>
            enabled && !adviceRunning && !chatRunning && !capturing && screenshot == null && string.IsNullOrEmpty(draft);

        private bool CanAttachScreenshot(bool chatRunning, bool capturing, bool adviceRunning) => !chatRunning && !capturing && !adviceRunning;

        private async void AdviceNow_Click(object sender, RoutedEventArgs e) => await RequestScreenAdviceAsync(true);

        private void FocusPrompt() => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!isClosed && !App.Current.IsExiting && Clippy.IsClippyEnabled && !toolDialogOpen && !contextMenuOpen)
                PromptBox.Focus(FocusState.Programmatic);
        });

        private async void AttachScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAttachScreenshot(Clippy.SendPromptCommand.IsRunning, Clippy.IsCapturingScreenshot, advice.IsRunning)) return;
            var destination = Settings.ServerUrl;
            var revision = Clippy.ConversationRevision;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            screenshotCapture = cancellation;
            Clippy.IsCapturingScreenshot = true;
            ScreenshotStatus.Text = "Снимаю экран…";
            ScreenshotStatus.Visibility = Visibility.Visible;
            try
            {
                var jpeg = await ScreenCaptureService.CaptureJpegAsync(this.GetWindowHandle(), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (isClosed || App.Current.IsExiting || destination != Settings.ServerUrl || revision != Clippy.ConversationRevision) return;
                if (jpeg == null)
                {
                    ScreenshotStatus.Text = "Экран недоступен: блокировка или полноэкранное приложение.";
                    return;
                }
                using var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
                using var writer = new global::Windows.Storage.Streams.DataWriter(stream);
                writer.WriteBytes(jpeg);
                await writer.StoreAsync().AsTask(cancellation.Token);
                writer.DetachStream();
                stream.Seek(0);
                var preview = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await preview.SetSourceAsync(stream).AsTask(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (isClosed || App.Current.IsExiting || destination != Settings.ServerUrl || revision != Clippy.ConversationRevision) return;
                ScreenshotPreview.Source = preview;
                Clippy.CurrentScreenshot = jpeg;
                ScreenshotPreviewPanel.Visibility = Visibility.Visible;
                ScreenshotStatus.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException) { if (!isClosed) ScreenshotStatus.Visibility = Visibility.Collapsed; }
            catch (Exception) { if (!isClosed) ScreenshotStatus.Text = "Не удалось снять экран. Попробуйте ещё раз."; }
            finally
            {
                screenshotCapture = null;
                Clippy.IsCapturingScreenshot = false;
                if (!isClosed) FocusPrompt();
            }
        }

        private void RemoveScreenshot_Click(object sender, RoutedEventArgs e)
        {
            screenshotCapture?.Cancel();
            Clippy.CurrentScreenshot = null;
            ScreenshotStatus.Visibility = Visibility.Collapsed;
            FocusPrompt();
        }

        private void RefreshChat_Click(object sender, RoutedEventArgs e)
        {
            screenshotCapture?.Cancel();
            ScreenshotStatus.Visibility = Visibility.Collapsed;
            FocusPrompt();
        }

        private void HideClippy()
        {
            screenshotCapture?.Cancel();
            advice.Reset();
            this.Hide();
        }

        private void ClippyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Clippy.CurrentScreenshot) && Clippy.CurrentScreenshot == null)
            {
                ScreenshotPreview.Source = null;
                ScreenshotPreviewPanel.Visibility = Visibility.Collapsed;
            }
            if ((e.PropertyName == nameof(Clippy.CurrentText) && !string.IsNullOrEmpty(Clippy.CurrentText)) ||
                (e.PropertyName == nameof(Clippy.IsClippyEnabled) && Clippy.IsClippyEnabled)) advice.Reset();
            if (e.PropertyName == nameof(Clippy.IsPinned))
            {
                if (Clippy.IsPinned) Pin(); else Unpin();
            }
            // x:Bind updates the VM after Checked/Unchecked can fire. Resize only
            // once the actual state changed, otherwise the collapsed UI leaves a large HWND.
            if (e.PropertyName == nameof(Clippy.IsClippyEnabled))
            {
                PositionInCorner();
                if (Clippy.IsClippyEnabled) FocusPrompt();
            }
        }

        public async Task<bool> ConfirmToolCallAsync(string name, string arguments, CancellationToken cancellationToken)
        {
            App.Current.ShowClippy();
            Clippy.IsClippyEnabled = true;
            var dialog = new ContentDialog
            {
                XamlRoot = Background.XamlRoot,
                Title = "Run MCP tool: " + name,
                Content = new ScrollViewer
                {
                    MaxHeight = 300,
                    Content = new TextBlock { Text = arguments, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
                },
                PrimaryButtonText = "Run tool", CloseButtonText = "Decline",
                DefaultButton = ContentDialogButton.Close
            };
            toolDialogOpen = true;
            UpdateInputRegion();
            try
            {
                using var registration = cancellationToken.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
                var result = await dialog.ShowAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return result == ContentDialogResult.Primary;
            }
            finally { toolDialogOpen = false; UpdateInputRegion(); }
        }

        private void SettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.ServerUrl))
            {
                screenshotCapture?.Cancel();
                Clippy.CurrentScreenshot = null;
            }
            if (e.PropertyName == nameof(Settings.ScreenAdviceEnabled) || e.PropertyName == nameof(Settings.ScreenAdviceIntervalMinutes) ||
                e.PropertyName == nameof(Settings.ServerUrl) || e.PropertyName == nameof(Settings.Model)) advice.Reset();
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
                Clippy.IsClippyEnabled || AdviceBubble.Visibility == Visibility.Visible ? Math.Max(380, Settings.ClippySize + 24) : Settings.ClippySize + 24,
                Clippy.IsClippyEnabled ? 1000 : Settings.ClippySize + 16 + (AdviceBubble.Visibility == Visibility.Visible ? 148 : 0));
            AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height), display);
            Content.MaxHeight = bounds.Height / scale;
            DispatcherQueue.TryEnqueue(UpdateInputRegion);
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            App.Current.OpenSettings();
        }

        private Visibility BtoV(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        private Visibility ChatBackgroundVisibility(bool expanded, bool translucent) => BtoV(expanded && translucent);

        private Rect VisualBounds(FrameworkElement element) => element.TransformToVisual(Background)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        private void UpdateInputRegion()
        {
            if (isClosed || inputRegion == null || ClippyButton.ActualWidth <= 0 || Background.ActualWidth <= 0) return;
            if (toolDialogOpen || contextMenuOpen)
            {
                inputRegion.RestoreFullWindow();
                return;
            }
            var scale = this.GetDpiForWindow() / 96d;
            var areas = new List<WindowInputRegion.PixelArea>();
            void AddArea(Rect rect, double padding = 0)
            {
                var left = Math.Max(0, rect.Left - padding);
                var top = Math.Max(0, rect.Top - padding);
                var right = Math.Min(Background.ActualWidth, rect.Right + padding);
                var bottom = Math.Min(Background.ActualHeight, rect.Bottom + padding);
                if (right <= left || bottom <= top) return;
                var x = (int)Math.Floor(left * scale);
                var y = (int)Math.Floor(top * scale);
                areas.Add(new WindowInputRegion.PixelArea(x, y, (int)Math.Ceiling(right * scale) - x, (int)Math.Ceiling(bottom * scale) - y));
            }
            AddArea(VisualBounds(ClippyButton));
            if (AdviceBubble.Visibility == Visibility.Visible) AddArea(VisualBounds(AdviceBubble), 4);
            if (Clippy.IsClippyEnabled)
            {
                if (ChatInputPanel.ActualHeight > 0) AddArea(VisualBounds(ChatInputPanel), 8);
                var viewport = VisualBounds(MessagesList);
                var top = double.PositiveInfinity;
                var bottom = double.NegativeInfinity;
                // Don't include the ListView's large, empty viewport above the messages.
                for (var index = 0; index < MessagesList.Items.Count; index++)
                {
                    if (MessagesList.ContainerFromIndex(index) is not FrameworkElement item || item.ActualHeight <= 0) continue;
                    var rect = VisualBounds(item);
                    var visibleTop = Math.Max(viewport.Top, rect.Top);
                    var visibleBottom = Math.Min(viewport.Bottom, rect.Bottom);
                    if (visibleBottom <= visibleTop) continue;
                    top = Math.Min(top, visibleTop);
                    bottom = Math.Max(bottom, visibleBottom);
                }
                if (bottom > top) AddArea(new Rect(viewport.X, top, viewport.Width, bottom - top), 8);
            }
            inputRegion.SetAreas(areas);
        }

		// Bool to Visibility
		public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

		// Bool to inverted visibility
		public Visibility InvertBoolToVis(bool b) => b ? Visibility.Collapsed : Visibility.Visible;

        private async void TextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            if ((shift & CoreVirtualKeyStates.Down) != 0) return;
            e.Handled = true; // Enter sends; never inserts a newline or triggers Stop.
            if (Clippy.SendPromptCommand.IsRunning || sender is not TextBox input || (string.IsNullOrWhiteSpace(input.Text) && Clippy.CurrentScreenshot == null)) return;
            Clippy.CurrentText = input.Text; // Flush the latest edit even before x:Bind updates.
            if (Clippy.SendPromptCommand.CanExecute(null))
                await Clippy.SendPromptCommand.ExecuteAsync(null);
        }

		private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApplication();

		private void Hide_Click(object sender, RoutedEventArgs e)
		{
            HideClippy();
		}
	}
}
