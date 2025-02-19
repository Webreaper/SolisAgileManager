using System.Diagnostics;
using System.Reflection;
using SolisManager.Shared;

namespace SolisManager.Services;

public class RestartService(IHostApplicationLifetime appLifetime, ILogger<RestartService> logger) : IToolsService
{
    public Task RestartApplication()
    {
        var stopping = appLifetime.ApplicationStopped;

        stopping.Register(RestartCallback);

        appLifetime.StopApplication();

        return Task.CompletedTask;
    }

    private void RestartCallback()
    {
        var cmdLineArgs = Environment.GetCommandLineArgs();

        if (cmdLineArgs.Length > 0)
        {
            var psi = new ProcessStartInfo(
                cmdLineArgs[0],
                cmdLineArgs[1]
            );

            psi.WorkingDirectory = Directory.GetCurrentDirectory();

            try
            {
                logger.LogWarning("Starting new instance of SolisManager...");
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occured while trying to restart Solis Manager");
            }
        }

        logger.LogWarning("Exiting application...");
        Environment.Exit(0);
    }
}
