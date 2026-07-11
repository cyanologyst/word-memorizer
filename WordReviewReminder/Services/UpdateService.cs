using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace WordReviewReminder.Services;

public enum AppUpdateState
{
    Current,
    Available,
    Required,
    NotInstalledWithAppInstaller,
    Error
}

public sealed record AppUpdateStatus(AppUpdateState State, string Message);

public sealed class UpdateService
{
    public const string AppInstallerUri =
        "https://github.com/cyanologyst/word-memorizer/releases/latest/download/WordReviewReminder.appinstaller";

    public const string ReleasesUri =
        "https://github.com/cyanologyst/word-memorizer/releases";

    public async Task<AppUpdateStatus> CheckAsync()
    {
        try
        {
            var current = Package.Current;
            var manager = new PackageManager();
            var package = manager.FindPackageForUser(string.Empty, current.Id.FullName);
            if (package is null || package.GetAppInstallerInfo() is null)
            {
                return NotAssociated();
            }

            var result = await package.CheckUpdateAvailabilityAsync();
            return result.Availability switch
            {
                PackageUpdateAvailability.Available => new(
                    AppUpdateState.Available,
                    "A newer version is ready in Windows App Installer."),
                PackageUpdateAvailability.Required => new(
                    AppUpdateState.Required,
                    "A required update is ready. Install it before continuing."),
                PackageUpdateAvailability.NoUpdates => new(
                    AppUpdateState.Current,
                    "You have the latest version."),
                _ => new(
                    AppUpdateState.Error,
                    "Windows could not determine update availability right now.")
            };
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return NotAssociated();
        }
        catch
        {
            return new AppUpdateStatus(
                AppUpdateState.Error,
                "The update service could not be reached. Try again later.");
        }
    }

    public static Version GetCurrentVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return new Version(version.Major, version.Minor, version.Build, version.Revision);
        }
        catch
        {
            return typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        }
    }

    public static void OpenInstaller() => Open(AppInstallerUri);

    public static void OpenReleases() => Open(ReleasesUri);

    private static AppUpdateStatus NotAssociated() => new(
        AppUpdateState.NotInstalledWithAppInstaller,
        "Automatic updates activate after installing the signed .appinstaller release.");

    private static void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
