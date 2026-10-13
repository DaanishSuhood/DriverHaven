using DriverUpdater.Core.Infrastructure;
using DriverUpdater.Core.Models;
using Microsoft.Extensions.Logging;

namespace DriverUpdater.Core.Services;

/// <summary>Restores a backed-up driver: restore point → forced install → check what the device uses.</summary>
public sealed class RestoreOrchestrator(
    IRestorePointService restorePoints,
    IDriverRestoreService restore,
    ILogger<RestoreOrchestrator> logger)
{
    /// <param name="continueWithoutRestorePoint">
    /// Asked when no usable restore point exists; return false to stop before anything changes. It is invoked on the
    /// caller's synchronization context, so it may show UI.
    /// </param>
    public async Task<DriverRestoreResult> RunAsync(BackedUpDriver driver, Func<RestorePointResult, bool> continueWithoutRestorePoint)
    {
        logger.LogInformation("Restoring {Device} to driver {Version} from {Folder}",
            driver.Entry.DeviceName, driver.Entry.DriverVersion, driver.BackupFolder);

        var restorePoint = await restorePoints.CreateAsync(
            $"{ProductInfo.Name} - before restoring the {driver.Entry.DeviceName} driver");
        if (!restorePoint.HasUsableRestorePoint && !continueWithoutRestorePoint(restorePoint))
            return new DriverRestoreResult(DriverRestoreOutcome.DeclinedWithoutRestorePoint,
                "Stopped because no restore point could be created. Nothing was changed.", RestorePoint: restorePoint);

        DriverRestoreResult result;
        try
        {
            result = await restore.RestoreAsync(driver);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restoring {Device} failed unexpectedly", driver.Entry.DeviceName);
            result = new DriverRestoreResult(DriverRestoreOutcome.Failed, $"Unexpected error: {ex.Message}");
        }
        return result with { RestorePoint = restorePoint };
    }
}
