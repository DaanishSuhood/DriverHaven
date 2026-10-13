using DriverUpdater.App.Services;
using DriverUpdater.Core.Models;
using DriverUpdater.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DriverUpdater.App.Sample;

/// <summary>Swaps in the sample services. Registered after the real ones, so these win.</summary>
public static class SampleServices
{
    public static void AddTo(IServiceCollection services, AppOptions options)
    {
        services.AddSingleton<IDriverUpdateService, SampleDriverUpdateService>();
        services.AddSingleton<IRestorePointService>(sp =>
            new SampleRestorePointService(options.SampleRestorePointFails, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IDriverBackupService, SampleDriverBackupService>();
        services.AddSingleton<IPendingRestartService>(new SamplePendingRestartService(options.SampleRestartPending));
        services.AddSingleton<IDriverBackupCatalog, SampleDriverBackupCatalog>();
        services.AddSingleton<IDriverRestoreService, SampleDriverRestoreService>();
        services.AddSingleton<IGpuDriverCheckService, SampleGpuDriverCheckService>();
        services.AddSingleton<ISystemRestartService, SampleRestartService>();
    }
}

/// <summary>Offers a made-up newer NVIDIA driver, and links for AMD and Intel, without contacting anyone.</summary>
public sealed class SampleGpuDriverCheckService : IGpuDriverCheckService
{
    public async Task<IReadOnlyList<GpuDriverNotice>> CheckAsync(IReadOnlyCollection<DeviceDriverInfo> devices,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        return GpuDetector.FindGpus(devices).Select(gpu => gpu.Vendor == GpuVendor.Nvidia
                ? new GpuDriverNotice(GpuDriverNoticeKind.NewerAvailable, GpuVendor.Nvidia, gpu.Device.DeviceName,
                    NvidiaDriver.FromWindowsVersion(gpu.Device.DriverVersion), "999.99 (sample)",
                    DateOnly.FromDateTime(DateTime.Today.AddDays(-2)), VendorGpuDriverCheckService.NvidiaDriversPage)
                : new GpuDriverNotice(GpuDriverNoticeKind.LinkOnly, gpu.Vendor, gpu.Device.DeviceName,
                    gpu.Device.DriverVersion, null, null,
                    gpu.Vendor == GpuVendor.Amd ? VendorGpuDriverCheckService.AmdDriversPage : VendorGpuDriverCheckService.IntelDriversPage))
            .ToList();
    }
}

/// <summary>Reports no pending restart, or (with --sample-restart-pending) one from Windows Update and a device.</summary>
public sealed class SamplePendingRestartService(bool pending) : IPendingRestartService
{
    public Task<PendingRestartSignals> GetSignalsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(pending
            ? new PendingRestartSignals(WindowsUpdateRebootRequired: true, DevicesNeedingRestart: ["Contoso Sample Sensor"])
            : PendingRestartSignals.None);
}

/// <summary>Pretends to create a restore point, or (with --sample-no-restore-point) that System Protection is off.</summary>
public sealed class SampleRestorePointService(bool fail, TimeProvider time) : IRestorePointService
{
    public async Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        return fail
            ? new RestorePointResult(RestorePointOutcome.Disabled,
                "Sample mode: simulating System Protection being turned off, so no restore point can be created.")
            : new RestorePointResult(RestorePointOutcome.Created,
                "Sample mode: restore point simulated (none was created).",
                new RestorePointInfo(0, description, time.GetLocalNow()));
    }
}

/// <summary>Reports what a backup would contain without running pnputil or writing anything.</summary>
public sealed class SampleDriverBackupService : IDriverBackupService
{
    public async Task<DriverBackupResult> BackupAsync(IReadOnlyCollection<DeviceDriverInfo> devices, CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        var entries = devices.Select(d => new DriverBackupEntry(
                d.DeviceName, d.DeviceId, d.InfName, d.DriverProvider, d.DriverVersion, d.DriverDate, d.HardwareIds,
                d.IsThirdPartyDriver ? DriverBackupStatus.Exported
                : d.HasDriver ? DriverBackupStatus.SkippedInbox
                : DriverBackupStatus.SkippedNoDriver,
                "Sample mode", null))
            .ToList();
        return new DriverBackupResult("(sample mode: nothing was written)", "", entries);
    }
}

public sealed class SampleRestartService(IDialogService dialogs) : ISystemRestartService
{
    public Task RestartNowAsync()
    {
        dialogs.ShowInfo("Restart", "Sample mode: Windows would restart now (shutdown /r /t 0). Nothing was done.");
        return Task.CompletedTask;
    }
}
