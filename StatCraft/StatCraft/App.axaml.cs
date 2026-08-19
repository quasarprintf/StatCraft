using System;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.ViewModels;
using StatCraft.Views;
using System.Linq;
using StatCraft.Services.BattlenetApi;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DataParsing;
using StatCraft.Models.Util;

namespace StatCraft
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Avalonia opens a MenuItem's submenu on hover only after this delay (400ms by default).
            // BuildPathPicker's nested-submenu build picker feels laggy with any delay, so remove it
            // app-wide — this is a global static, not something a per-control Style can override.
            DefaultMenuInteractionHandler.MenuShowDelay = TimeSpan.Zero;

            Services = BuildServiceProvider();

            // Last-resort safety net: without this, an exception that escapes a binding, command, or
            // property-changed handler (i.e. almost everything the UI thread runs) takes the whole
            // process down silently — nothing here logs on its own. Handled = true keeps the app running
            // rather than crashing the user's whole session over what's usually one isolated bad action.
            Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;

            // Catches whatever the dispatcher hook above doesn't — e.g. an exception on a background
            // thread that isn't awaited anywhere. Purely informational: by the time this fires the
            // runtime is already terminating the process, so all it can do is get the exception logged
            // before that happens.
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                SettingsRepository settingsRepository = Services.GetRequiredService<SettingsRepository>();
                AppSettingsData settings = settingsRepository.Load();

                if (string.IsNullOrEmpty(settings.BaseReplayFolderPath))
                {
                    SettingsPromptViewModel promptVm = Services.GetRequiredService<SettingsPromptViewModel>();
                    SettingsPromptWindow promptWindow = new SettingsPromptWindow(promptVm);
                    promptWindow.Closed += (_, _) => OnSettingsPromptClosed(desktop, settingsRepository);
                    desktop.MainWindow = promptWindow;
                }
                else
                {
                    ShowMainWindow(desktop);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ILogger logger = Services.GetRequiredService<ILogger>();
            logger.LogError($"Unhandled exception on UI thread: {e.Exception}");
            logger.Flush();
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is not Exception ex)
                return;

            ILogger logger = Services.GetRequiredService<ILogger>();
            logger.LogError($"Unhandled exception (process is terminating, IsTerminating={e.IsTerminating}): {ex}");
            logger.Flush();
        }

        private static void OnSettingsPromptClosed(IClassicDesktopStyleApplicationLifetime desktop, SettingsRepository settingsRepository)
        {
            AppSettingsData settings = settingsRepository.Load();
            if (!string.IsNullOrEmpty(settings.BaseReplayFolderPath))
                ShowMainWindow(desktop);
            else
                desktop.Shutdown();
        }

        private static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        }

        private static IServiceProvider BuildServiceProvider()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StatCraft");
            string dbPath = Path.Combine(appDataDir, "statcraft.db");
            string keyPath = Path.Combine(appDataDir, "statcraft.key");
            string settingsPath = Path.Combine(appDataDir, "Settings.json");

            ServiceCollection services = new ServiceCollection();

            services.AddSingleton<BuildRepository>(sp =>
            {
                BuildRepository repository = new BuildRepository(dbPath, sp.GetRequiredService<ILogger>());
                repository.Initialize();
                return repository;
            });

            services.AddSingleton<AccountRepository>(sp =>
            {
                AccountRepository repository = new AccountRepository(dbPath, sp.GetRequiredService<ILogger>());
                repository.Initialize();
                return repository;
            });

            services.AddSingleton<MapRepository>(sp =>
            {
                MapRepository repository = new MapRepository(dbPath, sp.GetRequiredService<ILogger>());
                repository.Initialize();
                return repository;
            });

            services.AddSingleton<GameDataRepository>(sp =>
            {
                GameDataRepository repository = new GameDataRepository(dbPath, sp.GetRequiredService<ILogger>());
                repository.Initialize();
                return repository;
            });

            services.AddSingleton<TokenProtector>(_ =>
            {
                TokenProtector protector = new TokenProtector(keyPath);
                protector.Initialize();
                return protector;
            });

            services.AddSingleton(_ => new SettingsRepository(settingsPath));
            services.AddSingleton<ReplayDataExtractor>();
            services.AddSingleton<ReplayWatcherService>();
            services.AddSingleton<ReplayImportService>();
            services.AddSingleton<ILogger>(_ => new LoggingService(Path.Combine(appDataDir, "Logs")));

            services.AddSingleton(new HttpClient());
            services.AddSingleton<BattleNetAuthService>();
            services.AddSingleton<StarCraft2ProfileService>();
            services.AddSingleton<BlizzardAppTokenProvider>();
            services.AddSingleton<Sc2LadderService>();

            services.AddTransient<BuildsPageViewModel>();
            services.AddTransient<MapsPageViewModel>();
            services.AddTransient<DataPageViewModel>();
            services.AddTransient<AccountPickerViewModel>();
            services.AddTransient<LinkAccountViewModel>();
            services.AddTransient<SettingsPromptViewModel>();
            services.AddTransient<SettingsPageViewModel>();

            return services.BuildServiceProvider();
        }
    }
}
