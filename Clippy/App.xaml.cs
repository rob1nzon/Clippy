using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Clippy.Core.ViewModels;
using Clippy.Core.Services;
using Clippy.Services;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using WinUIEx;
using System.Threading;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Clippy
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
	{
		private const string MutexID = "Clippy2025Mutex";
		private static Mutex? SingleInstanceMutex;

		/// <summary>
		/// Gets the current <see cref="App"/> instance in use
		/// </summary>
		public new static App Current => (App)Application.Current;

        /// <summary>
        /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
        /// </summary>
        public IServiceProvider Services { get; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            Services = ConfigureServices();
            this.InitializeComponent();
            UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedException;
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;

			CheckSingleInstance();
		}

		private void CheckSingleInstance()
		{
			bool isNewInstance;
			SingleInstanceMutex = new Mutex(true, MutexID, out isNewInstance);
			if (!isNewInstance)
				System.Environment.Exit(0);
		}

		private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<IChatService, ChatService>();
            services.AddSingleton<IKeyService, KeyService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IToolService>(provider => new McpToolService(
                provider.GetRequiredService<ISettingsService>(), () => new KeyService("Clippy.MCP").GetKey()));
            services.AddSingleton<ClippyViewModel>();
            services.AddSingleton(provider => new ScreenAdviceController(provider.GetRequiredService<ISettingsService>(),
                ((ChatService)provider.GetRequiredService<IChatService>()).AnalyzeScreenAsync));

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            ShowClippy();
        }

        public void ShowClippy()
        {
			if (m_window is null)
            {
				m_window = new MainWindow();
                ((ChatService)Services.GetRequiredService<IChatService>()).ApproveToolCall = m_window.ConfirmToolCallAsync;
                m_window.Closed += (_, _) => m_window = null;
            }
			m_window.Activate();
            m_window.Show();
            m_window.SetForegroundWindow();
        }

        public void OpenSettings()
		{
			if (s_window is null)
            {
				s_window = new SettingsWindow();
                s_window.Closed += (sender, e) => { s_window = null; };
            }
			s_window.Activate();
		}

        public bool IsExiting { get; private set; }

        public Task RequestScreenAdviceAsync() => m_window?.RequestScreenAdviceAsync(true) ?? Task.CompletedTask;

        public async void ExitApplication()
        {
            if (IsExiting) return;
            IsExiting = true;
            Services.GetRequiredService<ScreenAdviceController>().Reset();
            Services.GetRequiredService<ClippyViewModel>().SendPromptCommand.Cancel();
            m_window?.DisposeTray();
            try { await ((McpToolService)Services.GetRequiredService<IToolService>()).DisposeAsync(); }
            finally { Application.Current.Exit(); }
        }

        private MainWindow m_window;

		private Window s_window;


		private static void OnUnobservedException(object? sender, UnobservedTaskExceptionEventArgs e) => e.SetObserved();

        private static void OnUnhandledException(object? sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) => e.Handled = true;

        private void CurrentDomain_FirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
        {
        }
    }
}
