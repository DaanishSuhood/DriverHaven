using System.IO;
using System.Reflection;
using System.Windows;
using DriverUpdater.App.Services;
using DriverUpdater.App.ViewModels;
using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Services;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DriverUpdater.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppPaths.Default;
        paths.EnsureCreated();
        ConfigureLogging(paths);
        RegisterGlobalExceptionHandlers();

        var options = AppOptions.Parse(e.Args);
        Log.Information(ProductInfo.Name + " {Version} starting (elevated: {Elevated}, sample mode: {SampleMode})",
            Assembly.GetExecutingAssembly().GetName().Version, Elevation.IsAdministrator(), options.SampleMode);

        _services = ConfigureServices(paths, options);

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information(ProductInfo.Name + " exiting (code {ExitCode})", e.ApplicationExitCode);
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureLogging(AppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(paths.Logs, "driverhaven-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        // A layout error repeats on every render pass, so show at most one dialog at a time instead of a stack of them.
        var showingError = false;
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
            if (showingError)
                return;
            showingError = true;
            try
            {
                MessageBox.Show(args.Exception.Message, $"{ProductInfo.Name} - unexpected error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                showingError = false;
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{ProductInfo.Name}/{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");
        return http;
    }

    private static ServiceProvider ConfigureServices(AppPaths paths, AppOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: false));
        services.AddSingleton(paths);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IDialogService, MessageBoxDialogService>();
        services.AddSingleton<WindowPlacementStore>();
        services.AddSingleton<AppSettingsStore>();

        services.AddSingleton<IDeviceInventoryService, WmiDeviceInventoryService>();
        services.AddSingleton<IDriverUpdateService, WindowsUpdateDriverService>();
        services.AddSingleton<IPendingRestartService, WindowsPendingRestartService>();
        services.AddSingleton<IRestorePointService, WmiRestorePointService>();
        services.AddSingleton<IDriverBackupService, PnpUtilDriverBackupService>();
        services.AddSingleton<ISystemRestartService, ShutdownRestartService>();
        services.AddSingleton<InstallOrchestrator>();
        services.AddSingleton<IDriverBackupCatalog, DriverBackupCatalog>();
        services.AddSingleton<IDriverRestoreService, PnpDriverRestoreService>();
        services.AddSingleton<RestoreOrchestrator>();
        services.AddSingleton<IGpuDriverCheckService>(sp => new VendorGpuDriverCheckService(
            CreateHttpClient(), sp.GetRequiredService<ILogger<VendorGpuDriverCheckService>>()));

#if DEBUG
        if (options.SampleMode)
            Sample.SampleServices.AddTo(services, options);
#endif

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
