using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace NanoBot.Plugins;

public sealed class WebSearchPlugin
{
    private const string SearchUrl = "https://api.firecrawl.dev/v2/search";
    private const string ScrapeUrl = "https://api.firecrawl.dev/v2/scrape";
    private const int MaxResults = 10;
    private const int MaxContentChars = 8000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public WebSearchPlugin(ILogger<WebSearchPlugin> logger, HttpClient httpClient, string apiKey)
    {
        _logger = logger;
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    [Description("Search the web. Returns a short list of results, each with a title, URL and a relevant excerpt. Often enough to answer directly; use ReadPageAsync only if more detail is needed.")]
    public async Task<string> SearchAsync(
        [Description("The search query")] string query,
        [Description("Maximum number of results (1-10). Defaults to 5.")] int limit = 5,
        [Description("Only return results from the past day. Use for news and 'latest' questions.")] bool pastDayOnly = false,
        [Description("Optional website hostname to restrict results to, e.g. 'bbc.co.uk'.")] string site = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new JsonObject
            {
                ["query"] = query,
                ["limit"] = Math.Clamp(limit, 1, MaxResults),
            };
            if (pastDayOnly)
                body["tbs"] = "qdr:d";
            if (!string.IsNullOrWhiteSpace(site))
                body["includeDomains"] = new JsonArray(site.Trim());

            var response = await PostAsync(SearchUrl, body, cancellationToken);

            var results = new JsonArray();
            if (response["data"]?["web"] is JsonArray web)
            {
                foreach (var item in web)
                {
                    results.Add(new JsonObject
                    {
                        ["title"] = item?["title"]?.DeepClone(),
                        ["url"] = item?["url"]?.DeepClone(),
                        ["description"] = item?["description"]?.DeepClone(),
                    });
                }
            }

            _logger.LogInformation("SearchAsync: '{Query}' returned {Count} results", query, results.Count);
            if (results.Count == 0)
                return JsonSerializer.Serialize(new { results = Array.Empty<object>(), instruction = "No results were found. Tell the user nothing was found. Do NOT guess." });

            return results.ToJsonString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchAsync: Error searching for '{Query}'", query);
            return ErrorResult(ex.Message);
        }
    }

    [Description("Read a web page and return its content. Only use a URL returned by SearchAsync or given by the user. Provide a question to get just the answer from the page instead of its full text.")]
    public async Task<string> ReadPageAsync(
        [Description("The URL of the page to read")] string url,
        [Description("Optional specific question to answer from the page. Strongly preferred over reading the whole page.")] string question = null,
        [Description("Maximum characters of page content to return when no question is given. Defaults to 4000.")] int maxChars = 4000,
        [Description("Force a live fetch instead of allowing recently cached content. Use for fast-changing pages.")] bool fresh = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hasQuestion = !string.IsNullOrWhiteSpace(question);
            JsonNode data = null;

            if (hasQuestion)
            {
                try
                {
                    var qBody = new JsonObject
                    {
                        ["url"] = url,
                        ["formats"] = new JsonArray(new JsonObject { ["type"] = "question", ["question"] = question }),
                    };
                    if (fresh)
                        qBody["maxAge"] = 0;

                    data = (await PostAsync(ScrapeUrl, qBody, cancellationToken))["data"];
                    var answer = data?["answer"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        _logger.LogInformation("ReadPageAsync: Answered question from {Url}", url);
                        return JsonSerializer.Serialize(new { url, answer });
                    }
                    _logger.LogWarning("ReadPageAsync: No answer returned for {Url}, falling back to page text", url);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "ReadPageAsync: Question request failed for {Url}, falling back to page text", url);
                }
            }

            var body = new JsonObject
            {
                ["url"] = url,
                ["formats"] = new JsonArray("markdown"),
                ["onlyMainContent"] = true,
            };
            if (fresh)
                body["maxAge"] = 0;

            data = (await PostAsync(ScrapeUrl, body, cancellationToken))["data"];
            _logger.LogInformation("ReadPageAsync: Read {Url} as page text", url);

            var markdown = data?["markdown"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(markdown))
                return ErrorResult("The page returned no readable content");

            var limit = Math.Clamp(maxChars, 200, MaxContentChars);
            var truncated = TruncateAtWordBoundary(markdown, limit, out var wasTruncated);

            return JsonSerializer.Serialize(new
            {
                url,
                title = data?["metadata"]?["title"]?.GetValue<string>(),
                content = truncated,
                truncated = wasTruncated,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReadPageAsync: Error reading {Url}", url);
            return ErrorResult(ex.Message);
        }
    }

    private static string ErrorResult(string message) => JsonSerializer.Serialize(new
    {
        error = message,
        instruction = "The lookup FAILED and returned no information. Tell the user plainly that you could not look it up. Do NOT guess or answer as if you had seen web results.",
    });

    private async Task<JsonNode> PostAsync(string url, JsonObject body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Firecrawl API key is not configured");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            var text = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PostAsync: {Url} returned {Status}: {Body}", url, (int)response.StatusCode, text);
                throw new Exception($"Web search service error ({(int)response.StatusCode})");
            }

            var json = JsonNode.Parse(text);
            if (json?["success"]?.GetValue<bool>() == false)
                throw new Exception(json["error"]?.GetValue<string>() ?? "Web search service reported failure");

            return json;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Exception("Web request timed out");
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Network error: {ex.Message}");
        }
    }

    private static string TruncateAtWordBoundary(string text, int maxLength, out bool truncated)
    {
        truncated = false;
        if (text.Length <= maxLength)
            return text;

        truncated = true;
        var cut = text.LastIndexOfAny([' ', '\n', '\r', '\t'], maxLength);
        return cut > 0 ? text[..cut] : text[..maxLength];
    }
}
