using System.Diagnostics;
using System.Management;
using DriverUpdater.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DriverUpdater.Core.Services;

/// <summary>Creates restore points through root\default:SystemRestore (the same API Checkpoint-Computer uses).</summary>
public sealed class WmiRestorePointService(TimeProvider time, ILogger<WmiRestorePointService> logger) : IRestorePointService
{
    private const int DeviceDriverInstall = 10;  // RestorePointType
    private const int BeginSystemChange = 100;   // EventType
    private const int MaxDescriptionLength = 256;
    private const string SystemRestoreKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    // A new point shows up in the SystemRestore enumeration a few seconds after CreateRestorePoint returns.
    private static readonly TimeSpan NewPointTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExpectedSkipTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    private static readonly ManagementScope Scope = new(@"\\.\root\default");

    public Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateCoreAsync(description, cancellationToken), cancellationToken);

    private async Task<RestorePointResult> CreateCoreAsync(string description, CancellationToken cancellationToken)
    {
        if (DisabledByPolicy())
        {
            logger.LogWarning("System Restore is disabled by policy (DisableSR)");
            return new(RestorePointOutcome.Disabled, "System Restore is disabled by policy on this PC.");
        }

        if (description.Length > MaxDescriptionLength)
            description = description[..MaxDescriptionLength];

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var frequency = GetCreationFrequency();
            var before = GetLatestRestorePoint();
            var expectSkip = RestorePointInterpreter.IsWithinFrequencyWindow(before, time.GetUtcNow(), frequency);
            logger.LogInformation(
                "Creating restore point \"{Description}\" (latest #{Sequence} from {CreatedAt}, frequency {Frequency}, skip expected: {ExpectSkip})",
                description, before?.SequenceNumber, before?.CreatedAt, frequency, expectSkip);

            var returnValue = InvokeCreateRestorePoint(description);

            var after = returnValue == 0
                ? await WaitForNewerPointAsync(before, expectSkip ? ExpectedSkipTimeout : NewPointTimeout, cancellationToken)
                : GetLatestRestorePoint();
            var result = RestorePointInterpreter.Interpret(returnValue, before, after, time.GetUtcNow(), frequency);

            logger.Log(result.HasUsableRestorePoint ? LogLevel.Information : LogLevel.Warning,
                "Restore point outcome {Outcome} (return value {ReturnValue}, latest #{Sequence}) in {ElapsedMs} ms: {Message}",
                result.Outcome, returnValue, after?.SequenceNumber, stopwatch.ElapsedMilliseconds, result.Message);
            return result;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            logger.LogError(ex, "Restore point creation failed");
            return new(RestorePointOutcome.Failed, $"Windows could not create a restore point: {ex.Message}");
        }
    }

    private static uint InvokeCreateRestorePoint(string description)
    {
        using var systemRestore = new ManagementClass(Scope, new ManagementPath("SystemRestore"), null);
        using var inParams = systemRestore.GetMethodParameters("CreateRestorePoint");
        inParams["Description"] = description;
        inParams["RestorePointType"] = DeviceDriverInstall;
        inParams["EventType"] = BeginSystemChange;

        using var outParams = systemRestore.InvokeMethod("CreateRestorePoint", inParams, null);
        return Convert.ToUInt32(outParams["ReturnValue"]);
    }

    /// <summary>Polls until a point newer than <paramref name="before"/> appears, or the timeout passes.</summary>
    private async Task<RestorePointInfo?> WaitForNewerPointAsync(RestorePointInfo? before, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + timeout;
        while (true)
        {
            var latest = GetLatestRestorePoint();
            if (latest is not null && (before is null || latest.SequenceNumber > before.SequenceNumber))
                return latest;
            if (time.GetUtcNow() >= deadline)
                return latest;

            await Task.Delay(PollInterval, time, cancellationToken);
        }
    }

    private static RestorePointInfo? GetLatestRestorePoint()
    {
        using var searcher = new ManagementObjectSearcher(Scope,
            new ObjectQuery("SELECT SequenceNumber, Description, CreationTime FROM SystemRestore"));
        using var results = searcher.Get();

        RestorePointInfo? latest = null;
        foreach (var obj in results)
        {
            using (obj)
            {
                var sequence = Convert.ToUInt32(obj["SequenceNumber"]);
                if (latest is not null && sequence <= latest.SequenceNumber)
                    continue;

                var created = obj["CreationTime"] is string cim
                    ? new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(cim))
                    : DateTimeOffset.MinValue;
                latest = new RestorePointInfo(sequence, obj["Description"] as string ?? "", created);
            }
        }
        return latest;
    }

    /// <summary>SystemRestorePointCreationFrequency (minutes; 0 = no limit), defaulting to 24 hours.</summary>
    private static TimeSpan GetCreationFrequency()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SystemRestoreKey);
        return key?.GetValue("SystemRestorePointCreationFrequency") is int minutes && minutes >= 0
            ? TimeSpan.FromMinutes(minutes)
            : RestorePointInterpreter.DefaultCreationFrequency;
    }

    private static bool DisabledByPolicy()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore");
        return key?.GetValue("DisableSR") is int value && value == 1;
    }
}
