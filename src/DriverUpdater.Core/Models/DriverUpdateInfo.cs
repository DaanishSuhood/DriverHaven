namespace DriverUpdater.Core.Models;

/// <summary>A driver update offered by Windows Update (an IWindowsDriverUpdate).</summary>
public sealed record DriverUpdateInfo
{
    public required string UpdateId { get; init; }

    public int RevisionNumber { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public string? DriverProvider { get; init; }

    public string? DriverManufacturer { get; init; }

    public string? DriverModel { get; init; }

    public string? DriverClass { get; init; }

    /// <summary>Parsed from the title; WUA exposes the driver date but not its version.</summary>
    public string? DriverVersion { get; init; }

    public DateOnly? DriverDate { get; init; }

    /// <summary>Every hardware/compatible ID this update targets on this PC.</summary>
    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    public long MaxDownloadSize { get; init; }

    public bool IsDownloaded { get; init; }
}
