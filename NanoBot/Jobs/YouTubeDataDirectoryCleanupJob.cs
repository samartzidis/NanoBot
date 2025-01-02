using FluentScheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NanoBot.Jobs;

public class YouTubeDataDirectoryCleanupRegistry : Registry
{
    public YouTubeDataDirectoryCleanupRegistry(IServiceProvider serviceProvider)
    {
        Schedule(() => {
                using var scope = serviceProvider.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<YouTubeDataDirectoryCleanupJob>();
                job.Execute();
            })
            .ToRunNow()
            .AndEvery(15).Minutes();
    }
}

public class YouTubeDataDirectoryCleanupJob : IJob
{
    private readonly ILogger<YouTubeDataDirectoryCleanupJob> _logger;

    private readonly string _directoryPath;
    private readonly double _triggerSizeMB;
    private readonly double _targetSizeMB;

    public YouTubeDataDirectoryCleanupJob(ILogger<YouTubeDataDirectoryCleanupJob> logger, string directoryPath, double triggerSizeMB, double targetSizeMB)
    {
        _logger = logger;

        _directoryPath = directoryPath;
        _triggerSizeMB = triggerSizeMB;
        _targetSizeMB = targetSizeMB;
    }

    public void Execute()
    {
        _logger.LogDebug($"Executing: {nameof(YouTubeDataDirectoryCleanupJob)}");

        ManageDirectorySize(_directoryPath, _triggerSizeMB, _targetSizeMB);
    }

    private void ManageDirectorySize(string directoryPath, double triggerSizeMB, double targetSizeMB)
    {
        // Check if the directory exists
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("The specified directory does not exist. Nothing to do.");
            return;
        }

        // Get all files in the directory
        var files = new DirectoryInfo(directoryPath).GetFiles();

        // Calculate the total size of the directory
        long totalSizeBytes = files.Sum(f => f.Length);
        long totalSizeMB = totalSizeBytes / (1024 * 1024);

        _logger.LogDebug($"Initial directory size: {totalSizeMB} MB");

        // If the directory size exceeds the trigger size, start deleting files
        if (totalSizeMB > triggerSizeMB)
        {
            // Order files by creation time (oldest first)
            var filesByAge = files.OrderBy(f => f.CreationTime).ToList();

            foreach (var file in filesByAge)
            {
                try
                {
                    // Delete the file
                    _logger.LogDebug($"Deleting file: {file.Name}, Size: {file.Length / (1024 * 1024)} MB");
                    file.Delete();

                    // Update the total size
                    totalSizeBytes -= file.Length;
                    totalSizeMB = totalSizeBytes / (1024 * 1024);

                    // Break if the total size is within the target size
                    if (totalSizeMB <= targetSizeMB)
                        break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error deleting file {file.Name}");
                }
            }
        }
        else
        {
            _logger.LogDebug($"Directory size ({totalSizeMB} MB) is below the trigger threshold ({triggerSizeMB} MB). No files deleted.");
        }
    }
}