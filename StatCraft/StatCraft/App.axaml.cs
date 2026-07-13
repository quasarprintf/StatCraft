using System;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.Models;
using StatCraft.Services;
using StatCraft.ViewModels;
using StatCraft.Views;
using System.Linq;

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
            Services = BuildServiceProvider();

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

            services.AddSingleton<BuildRepository>(_ =>
            {
                BuildRepository repository = new BuildRepository(dbPath);
                repository.Initialize();
                return repository;
            });

            services.AddSingleton<AccountRepository>(_ =>
            {
                AccountRepository repository = new AccountRepository(dbPath);
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
            services.AddSingleton<ReplayWatcherService>();

            services.AddSingleton(new HttpClient());
            services.AddSingleton<BattleNetAuthService>();
            services.AddSingleton<StarCraft2ProfileService>();

            services.AddTransient<BuildsPageViewModel>();
            services.AddTransient<DataPageViewModel>();
            services.AddTransient<AccountPickerViewModel>();
            services.AddTransient<LinkAccountViewModel>();
            services.AddTransient<SettingsPromptViewModel>();

            return services.BuildServiceProvider();
        }
    }
}
