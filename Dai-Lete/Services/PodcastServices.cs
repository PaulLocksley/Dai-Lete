using System.Xml;
using System.Diagnostics;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;

namespace Dai_Lete.Services;

public class PodcastServices
{
    private readonly ILogger<PodcastServices> _logger;
    private readonly HttpClient _httpClient;
    private readonly IDatabaseService _databaseService;
    private readonly PodcastMetricsService _metricsService;
    public PodcastServices(ILogger<PodcastServices> logger, HttpClient httpClient, IDatabaseService databaseService, PodcastMetricsService metricsService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));

        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:15.0) Gecko/20100101 Firefox/15.0.1");
    }


    public async Task<List<Podcast>> GetPodcastsAsync()
    {
        try
        {
            const string sql = @"SELECT * FROM Podcasts";
            using var connection = await _databaseService.GetConnectionAsync();
            var results = await connection.QueryAsync<Podcast>(sql);
            return results.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve podcasts");
            throw;
        }
    }

    public async Task<(string guid, string downloadLink)> GetLatestEpisodeAsync(Guid podcastId)
    {
        try
        {
            const string sql = @"SELECT * FROM Podcasts WHERE Id = @id";
            using var connection = await _databaseService.GetConnectionAsync();
            var podcast = await connection.QueryFirstOrDefaultAsync<Podcast>(sql, new { id = podcastId });

            if (podcast is null)
            {
                throw new ArgumentException($"Podcast with id {podcastId} not found", nameof(podcastId));
            }

            var rssFeed = new XmlDocument();
            using var reader = XmlReader.Create(podcast.InUri.ToString());
            rssFeed.Load(reader);

            foreach (XmlElement node in rssFeed.DocumentElement.ChildNodes)
            {
                foreach (XmlElement item in node.ChildNodes)
                {
                    if (item.Name != "item") continue;

                    string? guid = null;
                    XmlNode? enclosure = null;

                    foreach (XmlElement element in item.ChildNodes)
                    {
                        switch (element.Name)
                        {
                            case "guid":
                                guid = string.Concat(element.InnerText.Split(Path.GetInvalidFileNameChars()));
                                break;
                            case "enclosure":
                                enclosure = element;
                                break;
                        }
                    }

                    if (enclosure?.Attributes?["url"]?.Value is string downloadLink && !string.IsNullOrEmpty(guid))
                    {
                        return (guid, downloadLink);
                    }
                }
            }

            throw new InvalidOperationException($"No episodes found for podcast: {podcastId}");
        }
        catch (Exception ex) when (!(ex is ArgumentException || ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Failed to parse RSS feed for podcast {PodcastId}", podcastId);
            throw new InvalidOperationException($"Failed to parse RSS feed for podcast {podcastId}", ex);
        }
    }

    public async Task DownloadEpisodeAsync(Podcast podcast, string episodeUrl, string episodeGuid)
    {
        if (podcast is null) throw new ArgumentNullException(nameof(podcast));
        if (string.IsNullOrWhiteSpace(episodeUrl)) throw new ArgumentException("Episode URL cannot be null or empty", nameof(episodeUrl));
        if (string.IsNullOrWhiteSpace(episodeGuid)) throw new ArgumentException("Episode GUID cannot be null or empty", nameof(episodeGuid));

        try
        {
            var workingDirectory = Path.Combine(Path.GetTempPath(), podcast.Id.ToString());
            Directory.CreateDirectory(workingDirectory);

            var destinationPath = Path.Combine(workingDirectory, $"{episodeGuid}.local");

            _logger.LogInformation("Starting download for episode {EpisodeGuid} from {EpisodeUrl}", episodeGuid, episodeUrl);

            var episodeData = await _httpClient.GetByteArrayAsync(episodeUrl);
            await File.WriteAllBytesAsync(destinationPath, episodeData);

            _logger.LogInformation("Successfully downloaded episode {EpisodeGuid}", episodeGuid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download episode {EpisodeGuid} from {EpisodeUrl}", episodeGuid, episodeUrl);
            throw;
        }
    }

    public async Task<int> ProcessDownloadedEpisodeAsync(Guid podcastId, string episodeId)
    {
        if (string.IsNullOrWhiteSpace(episodeId))
            throw new ArgumentException("Episode ID cannot be null or empty", nameof(episodeId));

        try
        {
            _logger.LogInformation("Processing downloaded episode {EpisodeId} for podcast {PodcastId}", episodeId, podcastId);

            var originalFilePath = Path.Combine(Path.GetTempPath(), podcastId.ToString(), $"{episodeId}.local");
            if (!File.Exists(originalFilePath))
            {
                _logger.LogWarning("Original audio file not found for episode {EpisodeId}: {FilePath}", episodeId, originalFilePath);
                return -1;
            }

            var originalDuration = await GetAudioDurationAsync(originalFilePath);
            _logger.LogDebug("Original episode duration: {Duration} for {EpisodeId}", originalDuration, episodeId);

            await Task.Delay(5000);

            var processedFilePath = GetEpisodeFilePath(podcastId, episodeId);
            var processedFile = processedFilePath;

            if (File.Exists(processedFile))
            {
                var processedDuration = await GetAudioDurationAsync(processedFile);
                var timeSaved = originalDuration - processedDuration;

                _logger.LogDebug("Processed episode duration: {Duration} for {EpisodeId}", processedDuration, episodeId);
                _logger.LogInformation("Time saved: {TimeSaved} for episode {EpisodeId}", timeSaved, episodeId);

                var podcastName = await GetPodcastNameAsync(podcastId);
                _metricsService.RecordTimeSaved(podcastId, episodeId, timeSaved, podcastName);
            }

            var fileInfo = new FileInfo(processedFile);
            _logger.LogInformation("Completed processing episode {EpisodeId}", episodeId);

            return (int)fileInfo.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process episode {EpisodeId} for podcast {PodcastId}", episodeId, podcastId);
            throw;
        }
    }

    private string GetEpisodeFilePath(Guid podcastId, string episodeId)
    {
        var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "episodes");
        return Path.Combine(baseDir, podcastId.ToString(), $"{episodeId}.wav");
    }

    private async Task<TimeSpan> GetAudioDurationAsync(string filePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v quiet -show_entries format=duration -of csv=\"p=0\" \"{filePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start ffprobe process");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"ffprobe failed with exit code {process.ExitCode}");
            }

            if (double.TryParse(output.Trim(), out var durationSeconds))
            {
                return TimeSpan.FromSeconds(durationSeconds);
            }

            throw new InvalidOperationException($"Failed to parse duration from ffprobe output: {output}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audio duration for file: {FilePath}", filePath);
            return TimeSpan.Zero;
        }
    }

    private async Task<string?> GetPodcastNameAsync(Guid podcastId)
    {
        try
        {
            const string sql = @"SELECT * FROM Podcasts WHERE Id = @id";
            using var connection = await _databaseService.GetConnectionAsync();
            var podcast = await connection.QueryFirstOrDefaultAsync<Podcast>(sql, new { id = podcastId });
            
            if (podcast?.InUri != null)
            {
                var rssFeed = new XmlDocument();
                using var reader = XmlReader.Create(podcast.InUri.ToString());
                rssFeed.Load(reader);

                var titleNode = rssFeed.SelectSingleNode("//channel/title");
                return titleNode?.InnerText?.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get podcast name for {PodcastId}", podcastId);
        }

        return null;
    }
}