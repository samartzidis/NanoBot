using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace NanoBot.Plugins;

public sealed class DateTimePlugin
{
    private readonly ILogger _logger;

    public DateTimePlugin(ILogger<DateTimePlugin> logger)
    {
        _logger = logger;
    }

    [Description("Get the current local date.")]
    public async Task<string> GetCurrentDateAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var formatted = now.ToString("yyyy-MM-dd (dddd)");

        _logger.LogInformation("GetCurrentDateAsync: {Date}", formatted);
        return formatted;
    }

    [Description("Get the current local time.")]
    public async Task<string> GetCurrentTimeAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var formatted = now.ToString("HH:mm:ss");

        _logger.LogInformation("GetCurrentTimeAsync: {Time}", formatted);
        return formatted;
    }
}
