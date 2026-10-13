using DriverUpdater.Core.Infrastructure;

namespace DriverUpdater.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void Default_root_is_under_ProgramData()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        Assert.Equal(Path.Combine(programData, "DriverHaven"), AppPaths.Default.Root);
    }

    [Fact]
    public void BackupFolderFor_uses_sortable_timestamp()
    {
        var paths = new AppPaths(@"C:\Root");

        var folder = paths.BackupFolderFor(new DateTime(2026, 10, 11, 14, 30, 5));

        Assert.Equal(@"C:\Root\Backups\20261011-143005", folder);
    }

    [Fact]
    public void EnsureCreated_creates_logs_and_backups()
    {
        var root = Path.Combine(Path.GetTempPath(), "DriverUpdaterTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);

            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.Logs));
            Assert.True(Directory.Exists(paths.Backups));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_blank_root(string root)
    {
        Assert.Throws<ArgumentException>(() => new AppPaths(root));
    }
}
