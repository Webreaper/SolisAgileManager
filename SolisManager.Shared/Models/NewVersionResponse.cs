using System.Reflection;

namespace SolisManager.Shared.Models;

public class NewVersionResponse
{
    public Version? CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version;

    public Version? NewVersion { get; set; } = Assembly.GetExecutingAssembly().GetName().Version;
    public string? NewReleaseName { get; set; }
    public string? ReleaseUrl { get; set; }

    public string? LatestReleaseUrl => "https://github.com/webreaper/SolisAgileManager/releases/latest";

    public bool UpgradeAvailable
    {
        get
        {
            if (CurrentVersion == null)
                return false;

            if (CurrentVersion.Major == 1 &&
                CurrentVersion.Minor == 0 &&
                CurrentVersion.Build == 0 &&
                CurrentVersion.Revision == 0)
            {
                // Dev build, so don't warn about new versions.
                return false;
            }

            if (NewVersion != null)
            {
                return NewVersion > CurrentVersion;
            }

            return false;
        }
    }
}