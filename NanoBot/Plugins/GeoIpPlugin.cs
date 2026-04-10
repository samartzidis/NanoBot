using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace NanoBot.Plugins.Native;

public class GeoIpInfo
{
    public string Query { get; set; } = string.Empty; // IP address returned by the API
    public string Country { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public float Lat { get; set; }
    public float Lon { get; set; }
    public string Isp { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class GeoIpPlugin
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public GeoIpPlugin(ILogger<GeoIpPlugin> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    #region Current Location Functions
    [KernelFunction, Description("Get current location details")]
    public async Task<string> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var geoInfo = await LookupCurrentIpAsync(cancellationToken);
            
            var result = $"IP: {geoInfo.Query}\n" +
                        $"Location: {geoInfo.City}, {geoInfo.RegionName}, {geoInfo.Country}\n" +
                        $"Coordinates: {geoInfo.Lat}, {geoInfo.Lon}\n" +
                        $"ISP: {geoInfo.Isp}";

            _logger.LogInformation("GetCurrentLocationAsync: Retrieved current location: {Location}", 
                $"{geoInfo.City}, {geoInfo.Country}");
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentLocationAsync: Error getting current location");
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get detailed current location information")]
    public async Task<string> GetDetailedCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var geoInfo = await LookupCurrentIpAsync(cancellationToken);
            
            var result = $"IP Address: {geoInfo.Query}\n" +
                        $"Country: {geoInfo.Country}\n" +
                        $"Region: {geoInfo.RegionName}\n" +
                        $"City: {geoInfo.City}\n" +
                        $"Latitude: {geoInfo.Lat}\n" +
                        $"Longitude: {geoInfo.Lon}\n" +
                        $"ISP: {geoInfo.Isp}";

            _logger.LogInformation("GetDetailedCurrentLocationAsync: Retrieved detailed current location info");
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDetailedCurrentLocationAsync: Error getting detailed current location");
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get current country")]
    public async Task<string> GetCurrentCountryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var geoInfo = await LookupCurrentIpAsync(cancellationToken);
            
            _logger.LogInformation("GetCurrentCountryAsync: Current country is {Country}", geoInfo.Country);
            
            return geoInfo.Country;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentCountryAsync: Error getting current country");
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get current coordinates (latitude and longitude)")]
    public async Task<string> GetCurrentCoordinatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var geoInfo = await LookupCurrentIpAsync(cancellationToken);
            
            var result = $"{geoInfo.Lat}, {geoInfo.Lon}";
            
            _logger.LogInformation("GetCurrentCoordinatesAsync: Current coordinates: {Coordinates}", result);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentCoordinatesAsync: Error getting current coordinates");
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get current ISP (Internet Service Provider)")]
    public async Task<string> GetCurrentIspAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var geoInfo = await LookupCurrentIpAsync(cancellationToken);
            
            _logger.LogInformation("GetCurrentIspAsync: Current ISP: {Isp}", geoInfo.Isp);
            
            return geoInfo.Isp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentIspAsync: Error getting current ISP");
            return $"Error: {ex.Message}";
        }
    }
    #endregion

    #region Private Helper Methods
    private async Task<GeoIpInfo> LookupCurrentIpAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Use current IP address (API will detect caller's IP)
            var url = "http://ip-api.com/json";

            _logger.LogDebug("LookupCurrentIpAsync: Making request to {Url}", url);

            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var info = JsonSerializer.Deserialize<GeoIpInfo>(response, options);

            if (info == null || info.Status != "success")
            {
                var errorMsg = info?.Status ?? "Unknown error";
                _logger.LogWarning("LookupCurrentIpAsync: GeoIP lookup failed with status: {Status}", errorMsg);
                throw new Exception($"GeoIP lookup failed: {errorMsg}");
            }

            _logger.LogDebug("LookupCurrentIpAsync: Successfully retrieved info for current IP {Query}", info.Query);
            return info;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "LookupCurrentIpAsync: HTTP error during GeoIP lookup");
            throw new Exception($"Network error during GeoIP lookup: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "LookupCurrentIpAsync: Timeout during GeoIP lookup");
            throw new Exception("GeoIP lookup timed out");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "LookupCurrentIpAsync: JSON parsing error during GeoIP lookup");
            throw new Exception($"Invalid response from GeoIP service: {ex.Message}");
        }
    }
    #endregion
}
