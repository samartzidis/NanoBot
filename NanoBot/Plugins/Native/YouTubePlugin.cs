using YoutubeExplode;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using NanoBot.Services;
using System.Text.Json;
using YoutubeExplode.Videos.Streams;
using System.Web;
using NanoBot.Configuration;

namespace NanoBot.Plugins.Native;

public class YouTubeVideo
{
    public string Id { get; set; }
    public string ChannelTitle { get; set; }
    public string Duration { get; set; }
    public string Title { get; set; }
    public string Url { get; set; }
    public bool IsCached { get; set; }
}

public class YouTubePlugin
{
    public const string DownloadFolder = "YouTubePluginData";

    private readonly ILogger _logger;
    private readonly IExternalAudioPlayerService _player;
    private readonly string _dataDir;
    private readonly AgentConfig _agentConfig;

    public YouTubePlugin(
        ILogger<SystemManagerPlugin> logger, 
        IExternalAudioPlayerService player, 
        AgentConfig agentConfig)
    {
        _logger = logger;
        _player = player;
        _agentConfig = agentConfig;

        _dataDir = Path.Combine(AppContext.BaseDirectory, DownloadFolder);
        if (!Directory.Exists(_dataDir))
            Directory.CreateDirectory(_dataDir);
    }

    [KernelFunction(nameof(IsPlaying))]
    [Description("Checks if a youtube video is currently playing.")]
    [return: Description("True if playing, false otherwise.")]
    public async Task<bool> IsPlaying()
    {
        return _player.IsPlaying;
    }

    [KernelFunction(nameof(StopPlaying))]
    [Description("Stops playback.")]
    public async Task StopPlaying()
    {
        _player.Stop();            
    }    

    [KernelFunction(nameof(PlayYoutubeVideo))]
    [Description("Plays a YouTube video.")]
    public async Task PlayYoutubeVideo(
        Kernel kernel,
        [Description("The YouTube video to play")] YouTubeVideo video)
    {
        _logger.LogDebug($"PlayYoutubeVideo: {JsonSerializer.Serialize(video)}");

        var rawFilePath = GetRawFilePath(video);

        if (!File.Exists(rawFilePath))
        {
            _logger.LogDebug($"Video URL: {video.Url}");

            var youtube = new YoutubeClient();
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Url);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            _logger.LogDebug($"Title: {video.Title}, Url: {video.Url}, Container: {streamInfo.Container.Name}, Bitrate: {streamInfo.Bitrate}");

            _logger.LogDebug($"Downloading {rawFilePath}...");
            await youtube.Videos.Streams.DownloadAsync(streamInfo, rawFilePath);
            _logger.LogDebug("Finished downloading.");
        }

        _logger.LogDebug($"Playing: {rawFilePath}.");
        var playbackTask = _player.PlayAsync(rawFilePath);
    }

    [KernelFunction(nameof(DeleteYoutubeVideo))]
    [Description("Deletes a locally cached YouTube video.")]
    public async Task<bool> DeleteYoutubeVideo([Description("The cached video to delete")] YouTubeVideo video)
    {
        var rawFilePath = GetRawFilePath(video);
        if (!File.Exists(rawFilePath))
            return true;

        File.Delete(rawFilePath);

        return true;
    }

    [KernelFunction(nameof(SearchYoutube))]
    [Description("Searches YouTube for videos/songs.")]
    [return: Description("The found YouTube video/song.")]
    public async Task<List<YouTubeVideo>> SearchYoutube(
        Kernel kernel,
        [Description("The search query string")] string query,
        [Description("Max results to return")] int maxResults = 3)
    {
        var youtube = new YoutubeClient();
        var videos = new List<YouTubeVideo>();
        var resultCount = 0;
        await foreach (var result in youtube.Search.GetVideosAsync(query))
        {
            if (resultCount >= maxResults)
                break;

            var videoItem = new YouTubeVideo
            {
                Id = result.Id,
                ChannelTitle = result.Author.ChannelTitle,
                Title = result.Title,
                Url = result.Url,
                Duration = result.Duration?.ToString()
            };

            // Skip items with no channel title or no duration (streams)
            if (string.IsNullOrEmpty(videoItem.ChannelTitle) || string.IsNullOrEmpty(videoItem.Duration))
            {
                _logger.LogWarning($"Skipping: {JsonSerializer.Serialize(videoItem)}");
                continue;
            }

            var rawFilePath = GetRawFilePath(videoItem);
            if (File.Exists(rawFilePath))
                videoItem.IsCached = true;

            _logger.LogDebug($"Adding: {JsonSerializer.Serialize(videoItem)}");
            videos.Add(videoItem);

            resultCount++;
        }

        // If nothing found, just return empty list - otherwise we do get hallucinations
        if (!videos.Any())
            return [];
        
        return videos;
    }

    [KernelFunction(nameof(SearchLocalCache))]
    [Description("Searches for locally stored YouTube videos/songs.")]
    [return: Description("The list of locally cached YouTube videos/songs.")]
    public async Task<List<YouTubeVideo>> SearchLocalCache([Description("The search query string")] string query)
    {
        var videos = new List<YouTubeVideo>();

        // Get all files in the download directory
        var files = Directory.GetFiles(_dataDir, "*.webm");
        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var decodedTitle = DecodeFilename(fileName); // Use DecodeFilename method

            // Check if the query matches the file name
            if (!string.IsNullOrEmpty(query) && !decodedTitle.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Create a YouTubeVideo object for the cached file
            var video = new YouTubeVideo
            {
                Title = decodedTitle,
                Url = "N/A (local file)", // Local files won't have a URL
                IsCached = true
            };

            videos.Add(video);
        }

        return videos;
    }

    private string GetRawFilePath(YouTubeVideo video)
    {
        var fileName = EncodeFilename(video.Title);
        var rawFilePath = Path.Combine(_dataDir, $"{fileName}.webm");

        return rawFilePath;
    }

    private static string EncodeFilename(string filename)
    {
        return HttpUtility.UrlEncode(filename);//.Replace("+", "%20"); // Replace "+" with space equivalent
    }

    private static string DecodeFilename(string encodedFilename)
    {
        return HttpUtility.UrlDecode(encodedFilename);
    }
}