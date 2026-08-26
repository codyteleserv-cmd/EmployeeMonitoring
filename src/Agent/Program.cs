using EmployeeMonitoring.Agent.Services;
using EmployeeMonitoring.Agent.UI;
using EmployeeMonitoring.Common.Extensions;
using EmployeeMonitoring.Common.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Diagnostics;
using System.Reflection;

namespace EmployeeMonitoring.Agent;

/// <summary>
/// Employee Monitoring Agent - Transparent, Consensual, Auditable
/// 
/// This agent runs as a Windows Service with a visible system tray UI.
/// Users can pause monitoring (with admin notification).
/// All activities are logged and auditable.
/// </summary>
internal sealed class Program
{
    private static readonly string AgentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    private static readonly string UserAgent = $"EmployeeMonitoring.Agent/{AgentVersion}";

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        // Initialize Serilog early for bootstrapping logs
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/bootstrap-.log", rollingInterval: RollingInterval.Day)
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting Employee Monitoring Agent v{Version}", AgentVersion);
            
            // Check for command line arguments
            if (args.Length > 0)
            {
                return await HandleCommandLineArgs(args);
            }

            // Build host
            var host = CreateHostBuilder(args).Build();

            // Run as Windows Service with tray UI
            if (OperatingSystem.IsWindows() && Environment.UserInteractive)
            {
                // Interactive mode - show tray UI
                return await RunInteractiveMode(host);
            }
            else
            {
                // Service mode
                await host.RunAsync();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Agent terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "EmployeeMonitoringAgent";
            })
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables("EMA_");
                
                if (args.Length > 0)
                {
                    config.AddCommandLine(args);
                }
            })
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.Configure<AgentConfiguration>(context.Configuration.GetSection("Agent"));
                services.Configure<ScreenshotConfiguration>(context.Configuration.GetSection("Screenshot"));
                services.Configure<ActivityConfiguration>(context.Configuration.GetSection("Activity"));
                services.Configure<DlpConfiguration>(context.Configuration.GetSection("Dlp"));
                services.Configure<WorkScheduleConfiguration>(context.Configuration.GetSection("WorkSchedule"));
                services.Configure<PrivacyConfiguration>(context.Configuration.GetSection("Privacy"));
                services.Configure<ConsentConfiguration>(context.Configuration.GetSection("Consent"));

                // Core services
                services.AddSingleton<IAgentIdentityProvider, AgentIdentityProvider>();
                services.AddSingleton<IAgentHealthProvider, AgentHealthProvider>();
                services.AddSingleton<IConfigurationManager, ConfigurationManager>();
                services.AddSingleton<IConsentManager, ConsentManager>();
                services.AddSingleton<IPauseManager, PauseManager>();
                services.AddSingleton<IAuditLogger, AuditLogger>();

                // Monitoring modules
                services.AddSingleton<IScreenshotService, ScreenshotService>();
                services.AddSingleton<IActivityService, ActivityService>();
                services.AddSingleton<IDlpService, DlpService>();

                // Communication
                services.AddSingleton<IGrpcClient, GrpcClient>();
                services.AddSingleton<ISignalRClient, SignalRClient>();
                services.AddSingleton<IMessageDispatcher, MessageDispatcher>();

                // Hosted services
                services.AddHostedService<AgentHostedService>();
                services.AddHostedService<HealthReportingService>();

                // Health checks
                services.AddHealthChecks()
                    .AddCheck<AgentHealthCheck>("agent");

                // UI (only in interactive mode)
                if (Environment.UserInteractive)
                {
                    services.AddSingleton<TrayApplicationContext>();
                    services.AddSingleton<MainForm>();
                }
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                logging.AddSerilog(dispose: true);
            })
            .UseSerilog((context, services, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("AgentVersion", AgentVersion)
                    .Enrich.WithProperty("MachineName", Environment.MachineName)
                    .Enrich.WithProcessId()
                    .Enrich.WithThreadId();
            });

    private static async Task<int> HandleCommandLineArgs(string[] args)
    {
        switch (args[0].ToLowerInvariant())
        {
            case "--install":
                return await InstallServiceAsync();
            case "--uninstall":
                return await UninstallServiceAsync();
            case "--register":
                return await RegisterAgentAsync(args);
            case "--consent":
                return await ShowConsentDialogAsync(args);
            case "--version":
                Console.WriteLine($"Employee Monitoring Agent v{AgentVersion}");
                return 0;
            case "--help":
                ShowHelp();
                return 0;
            default:
                Console.Error.WriteLine($"Unknown argument: {args[0]}");
                ShowHelp();
                return 1;
        }
    }

    private static async Task<int> RunInteractiveMode(IHost host)
    {
        // Start the host
        await host.StartAsync();

        // Get the tray application context
        var trayContext = host.Services.GetRequiredService<TrayApplicationContext>();
        
        // Run the application context (this blocks until exit)
        Application.Run(trayContext);

        // Graceful shutdown
        await host.StopAsync(TimeSpan.FromSeconds(10));
        return 0;
    }

    private static Task<int> InstallServiceAsync()
    {
        Log.Information("Installing Windows Service...");
        // Implementation would use ServiceController or sc.exe
        Console.WriteLine("Service installation not implemented in this build. Use 'sc create' or installutil.");
        return Task.FromResult(0);
    }

    private static Task<int> UninstallServiceAsync()
    {
        Log.Information("Uninstalling Windows Service...");
        Console.WriteLine("Service uninstallation not implemented in this build. Use 'sc delete' or installutil /u.");
        return Task.FromResult(0);
    }

    private static Task<int> RegisterAgentAsync(string[] args)
    {
        Log.Information("Registering agent with server...");
        Console.WriteLine("Agent registration not implemented in this build.");
        return Task.FromResult(0);
    }

    private static Task<int> ShowConsentDialogAsync(string[] args)
    {
        Log.Information("Showing consent dialog...");
        Console.WriteLine("Consent dialog not implemented in this build.");
        return Task.FromResult(0);
    }

    private static void ShowHelp()
    {
        Console.WriteLine($"""
            Employee Monitoring Agent v{AgentVersion}
            
            Usage:
              EmployeeMonitoring.Agent.exe [options]
            
            Options:
              --install       Install as Windows Service
              --uninstall     Uninstall Windows Service
              --register      Register agent with server
              --consent       Show consent dialog
              --version       Show version
              --help          Show this help
            
            Interactive Mode:
              Run without arguments to start with system tray UI.
            
            Service Mode:
              Runs as Windows Service 'EmployeeMonitoringAgent'.
            """);
    }
}