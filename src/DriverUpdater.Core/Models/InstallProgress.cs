namespace DriverUpdater.Core.Models;

public enum InstallStage
{
    RestorePoint,
    Backup,
    Download,
    Install,
    Finished,
}

/// <summary>One progress report from the install pipeline; each is also a line for the log pane.</summary>
/// <param name="CompletedSteps">Steps finished so far, out of <paramref name="TotalSteps"/> (restore point, backup, then a download and an install per update).</param>
public sealed record InstallProgress(InstallStage Stage, int CompletedSteps, int TotalSteps, string Message)
{
    public double Fraction => TotalSteps == 0 ? 1 : (double)CompletedSteps / TotalSteps;
}
