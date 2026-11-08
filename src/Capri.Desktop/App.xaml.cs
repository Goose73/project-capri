using System.Globalization;
using System.IO;
using System.Windows;
using Capri.Automation.DependencyInjection;
using Capri.Core.Abstractions;
using Capri.Core.DependencyInjection;
using Capri.Desktop.DependencyInjection;
using Capri.Desktop.Services;
using Capri.Desktop.Views;
using Capri.Providers.DependencyInjection;
using Capri.Security.DependencyInjection;
using Capri.Voice.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Capri.Desktop;

/// <summary>
/// WPF application host and composition root.
/// </summary>
public partial class App : Application, IDisposable
{
    private const string MutexName = @"Local\Capri.Phase1";

    private IHost? _host;
    private Mutex? _singleInstanceMutex;
    private bool _shutdownRequested;
    private bool _disposed;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
                // Mutex may not be owned.
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                // Another instance is already running.
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            _host = CreateHost();
            ConfigureSerilogFileSink(_host.Services);

            await _host.StartAsync().ConfigureAwait(true);

            var tray = _host.Services.GetRequiredService<TrayIconService>();
            tray.Initialize();

            // Create the transparent character companion (hidden by default). Resolving it wires avatar presentation.
            _ = _host.Services.GetRequiredService<CharacterWindow>();

            var settingsStore = _host.Services.GetRequiredService<IAppSettingsStore>();
            if (!settingsStore.Current.Ui.StartMinimized)
            {
                _host.Services.GetRequiredService<IOverlayController>().ShowOverlay();
            }
        }
        catch (Exception ex)
        {
            try
            {
                Log.Fatal(ex, "Capri failed during startup.");
                Log.CloseAndFlush();
            }
            catch
            {
                // Logging must not prevent the user-visible error.
            }

            MessageBox.Show(
                "Capri could not start. Please check logs under %LOCALAPPDATA%\\Capri\\Logs for details.",
                "Capri",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                var companion = _host.Services.GetService<CharacterWindow>();
                companion?.AllowShutdownClose();
                _host.Services.GetService<ChatPanelWindow>()?.AllowShutdownClose();

                var tray = _host.Services.GetService<TrayIconService>();
                tray?.Dispose();

                await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                _host.Dispose();
                _host = null;
            }
        }
        catch (Exception ex)
        {
            try
            {
                Log.Warning(ex, "Error during Capri shutdown.");
            }
            catch
            {
                // Ignore logging failures during shutdown.
            }
        }
        finally
        {
            try
            {
                Log.CloseAndFlush();
            }
            catch
            {
                // Ignore.
            }

            Dispose();
        }

        base.OnExit(e);
    }

    /// <summary>Requests application shutdown (used by tray Exit).</summary>
    public void RequestShutdown()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _host?.Services.GetService<CharacterWindow>()?.AllowShutdownClose();
        _host?.Services.GetService<ChatPanelWindow>()?.AllowShutdownClose();
        Shutdown();
    }

    private static IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory
        });

        // Bootstrap logger captures startup messages; file path is finalized after DI resolves paths.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        builder.Services.AddSerilog();

#if DEBUG
        // DI validation is helpful during development; keep it Debug-only to avoid startup surprises.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
#endif

        builder.Services
            .AddCapriCore()
            .AddCapriSecurity()
            .AddCapriProviders()
            .AddCapriAutomation()
            .AddCapriVoice()
            .AddCapriDesktop();

        return builder.Build();
    }

    private static void ConfigureSerilogFileSink(IServiceProvider services)
    {
        try
        {
            var paths = services.GetRequiredService<ICapriPaths>();
            Directory.CreateDirectory(paths.Logs);

            var logPath = Path.Combine(paths.Logs, "capri-.json");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
                .WriteTo.File(
                    formatter: new CompactJsonFormatter(),
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    restrictedToMinimumLevel: LogEventLevel.Debug)
                .CreateLogger();

            // Structured JSON logging is enabled. Do not log secrets or message content at Information;
            // this is enforced by application logging discipline (no sensitive payloads in log calls).
            Log.Information("Capri logging initialized.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to configure file logging; continuing with debug/bootstrap logging.");
        }
    }
}
