using System.Diagnostics;
using System.Reflection;
using Blazored.LocalStorage;
using SolisManager.APIWrappers;
using SolisManager.Components;
using Coravel;
using MudBlazor;
using MudBlazor.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SolisManager.Client.Constants;
using SolisManager.Services;
using SolisManager.Shared;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.InverterConfigs;
using SolisManager.Shared.Models;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace SolisManager;

public class Program
{
    private const int solisManagerPort = 5169;

    public static string ConfigFolder => configFolder;
    private static string configFolder = "config";

    public static async Task Main(string[] args)
    {
        // From Scott: https://www.hanselman.com/blog/detecting-that-a-net-core-app-is-running-in-a-docker-container-and-skippablefacts-in-xunit
        bool isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        var envConfigFolder = Environment.GetEnvironmentVariable("SOLISMANAGER_CONFIGFOLDER");
        
        if (!string.IsNullOrEmpty(envConfigFolder))
        {
            if (!SetupConfigFolder(envConfigFolder))
                return;
        }
        else if (isDocker)
        {
            // If we're in docker, always use the appdata folder
            if (!SetupConfigFolder("/appdata"))
                return;
        }
        else if (args.Length > 0)
        {
            var folder = args[0];
            if (!string.IsNullOrEmpty(folder))
            {
                if (!SetupConfigFolder(folder))
                    return;
            }

            if (string.IsNullOrEmpty(folder))
            {
                Console.WriteLine($"Using default folder \"{configFolder}\".");
                if (!SetupConfigFolder(configFolder))
                    return;
            }
        }

        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((hostContext, services, configuration) =>
        {
            InitLogConfiguration(configuration, ConfigFolder);
        });

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveWebAssemblyComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.Expiration = TimeSpan.Zero;
            options.SuppressXFrameOptionsHeader = true;
            options.SuppressReadingTokenFromFormBody = true;
        });

        builder.Services.AddDataProtection();

        builder.Services.AddSingleton<IUserAgentProvider, UserAgentProvider>();
        builder.Services.AddSingleton<SolisManagerConfig>();
        builder.Services.AddSingleton<InverterManager>();
        builder.Services.AddSingleton<IInverterManagerService>(x => x.GetRequiredService<InverterManager>());
        builder.Services.AddSingleton<IInverterRefreshService>(x => x.GetRequiredService<InverterManager>());
        builder.Services.AddSingleton<IToolsService, RestartService>();

        builder.Services.AddSingleton<InverterStateScheduler>();
        builder.Services.AddSingleton<RatesScheduler>();
        builder.Services.AddSingleton<AutoOverrideScheduler>();
        builder.Services.AddSingleton<SolcastScheduler>();
        builder.Services.AddSingleton<SolcastExtraScheduler>();
        builder.Services.AddSingleton<VersionCheckScheduler>();
        builder.Services.AddSingleton<TariffScheduler>();
        builder.Services.AddSingleton<InverterTimeAdjustScheduler>();

        builder.Services.AddSingleton<RestartService>();
        builder.Services.AddSingleton<SolcastAPI>();
        builder.Services.AddSingleton<OctopusAPI>();

        builder.Services.AddSingleton<InverterFactory>();

        builder.Services.AddScheduler();
        builder.Services.AddBlazoredLocalStorage();
        builder.Services.AddMemoryCache();
        builder.Services.InitMudServices();

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        if (!Debugger.IsAttached)
        {
            // Use Kestrel options to set the port. Using .Urls.Add breaks WASM debugging.
            // This line also breaks wasm debugging in Rider.
            // See https://github.com/dotnet/aspnetcore/issues/43703
            builder.WebHost.UseKestrel(serverOptions => { serverOptions.ListenAnyIP(solisManagerPort); });
        }

        var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        logger.LogInformation("===========================================================");
        logger.LogInformation("Application started. Build version v{V} Logs being written to {C}", version, ConfigFolder);
        if(isDocker)
            logger.LogInformation("Running in Docker");
        
        string timeZoneName;
        if (TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now))
            timeZoneName = TimeZoneInfo.Local.DaylightName;
        else
            timeZoneName = TimeZoneInfo.Local.StandardName;

        logger.LogInformation("Current timezone: {Tz}", timeZoneName);
        
        // First, load the config
        var config = app.Services.GetRequiredService<SolisManagerConfig>();
        if (!config.ReadFromFile(ConfigFolder))
        {
            config.OctopusProductCode = "E-1R-AGILE-24-10-01-J";
            config.SlotsForFullBatteryCharge = 6;
            config.AlwaysChargeBelowPrice = 10;

            logger.LogInformation("Default config initialised");
        }
        else
        {
            if (config.InverterConfig == null)
            {
                await UpgradeConfig(config, logger);
            }

            logger.LogInformation("Config loaded");
            if( ! string.IsNullOrEmpty(config.OctopusAccountNumber))
                logger.LogInformation("  Octopus Account number was specified");
            if( ! string.IsNullOrEmpty(config.OctopusAPIKey))
                logger.LogInformation("  Octopus API Key was specified");
            if( ! string.IsNullOrEmpty(config.OctopusProductCode))
                logger.LogInformation("  Octopus Product Code: {C}", config.OctopusProductCode);
            if( config.ScheduledActions != null )
                logger.LogInformation("  Scheduled actions configured: {C}", config.ScheduledActions.Count);
        }
        
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseWebAssemblyDebugging();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseExceptionHandler();
        // app.UseHttpsRedirection();

        app.UseRouting();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddInteractiveServerRenderMode()
            .AddAdditionalAssemblies(typeof(Client._Imports).Assembly);

        app.ConfigureAPIEndpoints();

        // We query at 9am to check the coming day's forecast, and at
        // just before 11pm so we've got the most up-to-date calculation for
        // when the no-overnight-charge rule kicks in.
        // Get the solcast data on the4 13th minute, because that reduces load
        // (half of the world runs their solcast ingestion on the hour). Don't
        // run at first startup.
        app.Services.UseScheduler(s => s
            .Schedule<SolcastScheduler>()
            .Cron("13 9 * * *")
            .Zoned(TimeZoneInfo.Local));

        app.Services.UseScheduler(s => s
            .Schedule<SolcastScheduler>()
            .Cron("53 22 * * *")
            .Zoned(TimeZoneInfo.Local));

        // An additional scheduler for a midday solcast updated. This will
        // give better forecasting accuracy, but at the cost of risking
        // hitting the rate limit. So the execution of this scheduler
        // depends on the config setting.
        app.Services.UseScheduler(s => s
            .Schedule<SolcastExtraScheduler>()
            .Cron("13 12 * * *")
            .Zoned(TimeZoneInfo.Local));

        // Scheduler for updating the inverter date/time to avoid drift
        // Once a day, at 2am
        app.Services.UseScheduler(s => s
            .Schedule<InverterTimeAdjustScheduler>()
            .Cron("0 3 * * *")
            .Zoned(TimeZoneInfo.Local)
            .RunOnceAtStart());

        // Update the intverter state every minute. The actual inverter
        // data only gets updated in SolisCloud every 5 minutes, but requesting
        // it regularly means we won't end up with 10-minute stale data
        // Don't run at startup - this will be executed the very first plan
        // evaluation when RatesScheduler is executed at startup.
        app.Services.UseScheduler(s => s
            .Schedule<InverterStateScheduler>()
            .Cron("*/1 * * * *"));

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_TARIFF_REFRESH")))
        {
            // Check if the Octopus tariff has changed every 4 hours
            app.Services.UseScheduler(s => s
                .Schedule<TariffScheduler>()
                .Cron("3 */4 * * *")
                .RunOnceAtStart());
        }

        // Check for a new version periodically - every 3 hours
        app.Services.UseScheduler(s => s
            .Schedule<VersionCheckScheduler>()
            .Cron("15 */3 * * *")
            .RunOnceAtStart());

        // Recalculate the slot plan every 30 minutes 
        app.Services.UseScheduler(s => s
            .Schedule<RatesScheduler>()
            .Cron("0,30 * * * *")
            .RunOnceAtStart());

        // Every 5 minutes check for the SOC and IOG slots to apply
        // to the current slot plan as overrides
        // No point running this at startup because slots may
        // not be available. So wait for the first 5 mins period.
        app.Services.UseScheduler(s => s
            .Schedule<AutoOverrideScheduler>()
            .Cron("0,5,10,15,20,25,30,35,40,45,50,55 * * * *"));

        var solcastAPI = app.Services.GetRequiredService<SolcastAPI>();
        await solcastAPI.InitialiseSolcastCache();

        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected exception in app.RunAdync!");
        }
    }

    private static bool SetupConfigFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Console.WriteLine($"Config folder set to {folder}.");
            configFolder = folder;
            return true;
        }
        
        if (SafeCreateFolder(folder))
        {
            configFolder = folder;
            Console.WriteLine($"Created config folder: {ConfigFolder}.");
            return true;
        }

        Console.WriteLine($"Config folder {folder} did not exist and unable to create it. Exiting...");
        return false;
    }
    
    private static async Task UpgradeConfig(SolisManagerConfig config, ILogger logger)
    {
#pragma warning disable 612,618
        config.InverterConfig = new InverterConfigSolis
        {
            SolisAPIKey = config.SolisAPIKey ?? string.Empty,
            SolisInverterSerial = config.SolisInverterSerial ?? string.Empty,
            SolisAPISecret = config.SolisAPISecret ?? string.Empty,
            MaxChargeRateAmps = config.MaxChargeRateAmps ?? 50
        };

        logger.LogInformation("Upgrading config to new format...");
        config.SolisAPIKey = null;
        config.SolisInverterSerial = null;
        config.SolisAPISecret = null;
        config.MaxChargeRateAmps = null;
#pragma warning restore 612,618

        await config.SaveToFile(Program.ConfigFolder);
    }

    private const string template = "[{Timestamp:HH:mm:ss.fff}-{ThreadID}-{Level:u3}] {Message:lj}{NewLine}{Exception}";
    private static readonly LoggingLevelSwitch logLevel = new();

    private static bool SafeCreateFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Initialise logging and add the thread enricher.
    /// </summary>
    /// <returns></returns>
    public static void InitLogConfiguration(LoggerConfiguration config, string logFolder)
    {
        try
        {
            if ( !Directory.Exists(logFolder) )
            {
                Console.WriteLine($"Creating log folder {logFolder}");
                Directory.CreateDirectory(logFolder);
            }
            
            logLevel.MinimumLevel = LogEventLevel.Information;
            var logFilePattern = Path.Combine(logFolder, "SolisManager-.log");

            config.WriteTo.Console(outputTemplate: template,
                    levelSwitch: logLevel)
                .WriteTo.File(logFilePattern,
                    outputTemplate: template,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 104857600,
                    retainedFileCountLimit: 10,
                    levelSwitch: logLevel)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Fatal)
                .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Fatal)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning);

        }
        catch ( Exception ex )
        {
            Console.WriteLine($"Unable to initialise logs: {ex}");
        }
    }
}