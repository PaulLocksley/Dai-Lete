using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;

namespace Dai_Lete.Services;

public class PodcastServices
{
    private readonly ILogger<PodcastServices> _logger;
    private readonly HttpClient _httpClient;
    private readonly IDatabaseService _databaseService;
    private readonly WhisperTranscriptionService _transcriptionService;

    public PodcastServices(ILogger<PodcastServices> logger, HttpClient httpClient, IDatabaseService databaseService, WhisperTranscriptionService transcriptionService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _transcriptionService = transcriptionService ?? throw new ArgumentNullException(nameof(transcriptionService));

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

            var audioFilePath = GetEpisodeFilePath(podcastId, episodeId);
            if (!File.Exists(audioFilePath))
            {
                _logger.LogWarning("Audio file not found for episode {EpisodeId}: {FilePath}", episodeId, audioFilePath);
                return -1;
            }

            var transcript = await _transcriptionService.TranscribeEpisodeAsync(audioFilePath, episodeId, podcastId.ToString());

            await SaveTranscriptAsync(transcript);

            var fileInfo = new FileInfo(audioFilePath);
            _logger.LogInformation("Completed processing episode {EpisodeId} with {SegmentCount} transcript segments",
                episodeId, transcript.Segments.Count);

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

    private async Task SaveTranscriptAsync(EpisodeTranscript transcript)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();

            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS Transcripts (
                    EpisodeId TEXT NOT NULL,
                    PodcastId TEXT NOT NULL,
                    FullText TEXT NOT NULL,
                    TimestampedText TEXT NOT NULL,
                    SegmentCount INTEGER NOT NULL,
                    Language TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    PRIMARY KEY (EpisodeId, PodcastId)
                )";

            await connection.ExecuteAsync(createTableSql);

            const string insertSql = @"
                INSERT OR REPLACE INTO Transcripts 
                (EpisodeId, PodcastId, FullText, TimestampedText, SegmentCount, Language, CreatedAt)
                VALUES (@EpisodeId, @PodcastId, @FullText, @TimestampedText, @SegmentCount, @Language, @CreatedAt)";

            var fullText = _transcriptionService.GetFullTranscriptText(transcript);
            var timestampedText = _transcriptionService.GetTranscriptWithTimestamps(transcript);

            await connection.ExecuteAsync(insertSql, new
            {
                transcript.EpisodeId,
                transcript.PodcastId,
                FullText = fullText,
                TimestampedText = timestampedText,
                SegmentCount = transcript.Segments.Count,
                transcript.Language,
                CreatedAt = transcript.CreatedAt.ToString("O")
            });

            _logger.LogInformation("Saved transcript for episode {EpisodeId} with {SegmentCount} segments",
                transcript.EpisodeId, transcript.Segments.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save transcript for episode {EpisodeId}", transcript.EpisodeId);
            throw;
        }
    }

}