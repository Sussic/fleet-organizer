using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FleetOrganizer.App.Services;
using FleetOrganizer.App.ViewModels;
using FleetOrganizer.Infrastructure;
using FleetOrganizer.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;

namespace FleetOrganizer.App;

public partial class App : Application
{
    private IHost? host;
    private bool isHandlingUnhandledException;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.SetBasePath(AppContext.BaseDirectory);
                configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
                configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddFleetOrganizerInfrastructure(context.Configuration);
                services.AddSingleton<IUserInteractionService, WpfUserInteractionService>();
                services.AddSingleton<IFileDialogService, WpfFileDialogService>();
                services.AddSingleton<ILiveFleetRunCoordinator, LiveFleetRunCoordinator>();
                services.AddSingleton<ProfilesViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .Build();

        try
        {
            await host.StartAsync();

            var databaseInitializer = host.Services
                .GetRequiredService<SqliteDatabaseInitializer>();
            await databaseInitializer.InitializeAsync();

            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            var viewModel = host.Services.GetRequiredService<MainWindowViewModel>();
            await viewModel.InitializeAsync();
            mainWindow.Show();
            mainWindow.ApplyStartupPreferences();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Fleet Desk could not start.\n\n{exception.Message}",
                "Fleet Desk",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync(TimeSpan.FromSeconds(5));
            host.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object _,
        DispatcherUnhandledExceptionEventArgs e)
    {
        if (isHandlingUnhandledException)
        {
            return;
        }

        isHandlingUnhandledException = true;
        e.Handled = true;

        var crashLogPath = TryWriteCrashLog(e.Exception);
        var logMessage = crashLogPath is null
            ? "Fleet Desk could not save a crash report."
            : $"A crash report was saved to:\n{crashLogPath}";
        MessageBox.Show(
            "Fleet Desk encountered an unexpected error and must close.\n\n" +
            $"{e.Exception.Message}\n\n{logMessage}\n\n" +
            "Any active fleet operation remains saved and can be resumed after reopening.",
            "Fleet Desk error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(-1);
    }

    private static string? TryWriteCrashLog(Exception exception)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var logsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FleetOrganizer",
                "logs");
            Directory.CreateDirectory(logsDirectory);
            var path = Path.Combine(
                logsDirectory,
                $"crash-{now.ToUnixTimeMilliseconds()}.log");
            File.WriteAllText(
                path,
                $"Fleet Desk unhandled UI exception{Environment.NewLine}" +
                $"UTC: {now.ToString("O", CultureInfo.InvariantCulture)}" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                exception.ToString());
            return path;
        }
        catch (Exception loggingException) when (
            loggingException is not OutOfMemoryException and
            not StackOverflowException)
        {
            return null;
        }
    }
}
