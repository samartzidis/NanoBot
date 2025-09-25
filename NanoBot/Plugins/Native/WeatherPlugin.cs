using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace NanoBot.Plugins.Native;


public sealed class WeatherPlugin
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public WeatherPlugin(ILogger<WeatherPlugin> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    #region Current Weather Functions
    [KernelFunction, Description("Get current weather for specific coordinates")]
    public async Task<string> GetCurrentWeatherAsync(
        [Description("Latitude coordinate")] double latitude,
        [Description("Longitude coordinate")] double longitude,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await GetWeatherDataAsync(latitude, longitude, cancellationToken);

            _logger.LogInformation("GetCurrentWeatherAsync: Retrieved current weather for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentWeatherAsync: Error getting current weather for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [KernelFunction, Description("Get current temperature for specific coordinates")]
    public async Task<string> GetCurrentTemperatureAsync(
        [Description("Latitude coordinate")] double latitude,
        [Description("Longitude coordinate")] double longitude,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await GetWeatherDataAsync(latitude, longitude, cancellationToken);
            
            _logger.LogInformation("GetCurrentTemperatureAsync: Retrieved temperature data for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentTemperatureAsync: Error getting temperature for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [KernelFunction, Description("Get current wind information for specific coordinates")]
    public async Task<string> GetCurrentWindAsync(
        [Description("Latitude coordinate")] double latitude,
        [Description("Longitude coordinate")] double longitude,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await GetWeatherDataAsync(latitude, longitude, cancellationToken);
            
            _logger.LogInformation("GetCurrentWindAsync: Retrieved wind data for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentWindAsync: Error getting wind info for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
    #endregion

    #region Forecast Functions
    [KernelFunction, Description("Get hourly temperature forecast for the next 24 hours")]
    public async Task<string> GetHourlyTemperatureForecastAsync(
        [Description("Latitude coordinate")] double latitude,
        [Description("Longitude coordinate")] double longitude,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await GetWeatherDataAsync(latitude, longitude, cancellationToken);
            
            _logger.LogInformation("GetHourlyTemperatureForecastAsync: Retrieved hourly forecast for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHourlyTemperatureForecastAsync: Error getting hourly forecast for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [KernelFunction, Description("Get detailed hourly weather forecast for the next 12 hours")]
    public async Task<string> GetDetailedHourlyForecastAsync(
        [Description("Latitude coordinate")] double latitude,
        [Description("Longitude coordinate")] double longitude,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await GetWeatherDataAsync(latitude, longitude, cancellationToken);
            
            _logger.LogInformation("GetDetailedHourlyForecastAsync: Retrieved detailed forecast for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDetailedHourlyForecastAsync: Error getting detailed forecast for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [KernelFunction, Description("Get weather summary with current conditions and next few hours")]
    public async Task<string> GetWeatherSummaryAsync(
        [Description("Latitude coordinate")] double latitude,
        [Description("Longitude coordinate")] double longitude,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await GetWeatherDataAsync(latitude, longitude, cancellationToken);
            
            _logger.LogInformation("GetWeatherSummaryAsync: Retrieved weather summary for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetWeatherSummaryAsync: Error getting weather summary for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
    #endregion

    #region Private Helper Methods
    private async Task<string> GetWeatherDataAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        try
        {
            // Get current weather + hourly temperature, humidity, precipitation, and wind speed
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current_weather=true&hourly=temperature_2m,relativehumidity_2m,precipitation,windspeed_10m";

            _logger.LogDebug("GetWeatherDataAsync: Making request to {Url}", url);

            var response = await _httpClient.GetStringAsync(url, cancellationToken);

            _logger.LogDebug("GetWeatherDataAsync: Successfully retrieved weather data for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetWeatherDataAsync: HTTP error during weather lookup for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            throw new Exception($"Network error during weather lookup: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "GetWeatherDataAsync: Timeout during weather lookup for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            throw new Exception("Weather lookup timed out");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "GetWeatherDataAsync: JSON parsing error during weather lookup for coordinates {Lat}, {Lon}", 
                latitude, longitude);
            throw new Exception($"Invalid response from weather service: {ex.Message}");
        }
    }
    #endregion
}
