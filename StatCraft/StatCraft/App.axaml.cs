using System;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
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
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static IServiceProvider BuildServiceProvider()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StatCraft");
            string dbPath = Path.Combine(appDataDir, "statcraft.db");
            string keyPath = Path.Combine(appDataDir, "statcraft.key");

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

            services.AddSingleton(new HttpClient());
            services.AddSingleton<BattleNetAuthService>();
            services.AddSingleton<StarCraft2ProfileService>();

            services.AddTransient<BuildsPageViewModel>();
            services.AddTransient<DataPageViewModel>();
            services.AddTransient<AccountPickerViewModel>();
            services.AddTransient<LinkAccountViewModel>();

            return services.BuildServiceProvider();
        }
    }
}
