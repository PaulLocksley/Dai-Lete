using Dai_Lete.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Dai_Lete.Services;

public class ValkeyEpisodeJobQueue : IEpisodeJobQueue
{
    private readonly IDatabase _database;
    private readonly ValkeyOptions _options;
    private readonly ILogger<ValkeyEpisodeJobQueue> _logger;
    private bool _groupInitialized;
    private readonly SemaphoreSlim _groupSemaphore = new(1, 1);
    private TimeSpan JobDeduplicationExpiration => TimeSpan.FromDays(7);

    public ValkeyEpisodeJobQueue(IConnectionMultiplexer redis, IOptions<ValkeyOptions> options, ILogger<ValkeyEpisodeJobQueue> logger)
    {
        _database = redis?.GetDatabase() ?? throw new ArgumentNullException(nameof(redis));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> EnqueueAsync(EpisodeJob job)
    {
        if (job is null) throw new ArgumentNullException(nameof(job));

        var dedupeKey = JobDedupeKey(job.PodcastId, job.EpisodeGuid);
        if (!await _database.StringSetAsync(dedupeKey, "1", JobDeduplicationExpiration, When.NotExists))
        {
            _logger.LogInformation("Episode job already queued for podcast {PodcastId}, episode {EpisodeGuid}", job.PodcastId, job.EpisodeGuid);
            return string.Empty;
        }

        try
        {
            var id = await _database.StreamAddAsync(_options.JobStream, new NameValueEntry[]
            {
                new("podcastId", job.PodcastId.ToString()),
                new("podcastInUri", job.PodcastInUri),
                new("episodeUrl", job.EpisodeUrl),
                new("episodeGuid", job.EpisodeGuid)
            });

            return id.ToString();
        }
        catch
        {
            await _database.KeyDeleteAsync(dedupeKey);
            throw;
        }
    }

    public async Task<IReadOnlyList<QueuedEpisodeJob>> ReadAsync(CancellationToken cancellationToken)
    {
        await EnsureConsumerGroupAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var entries = await _database.StreamReadGroupAsync(
            _options.JobStream,
            _options.ConsumerGroup,
            _options.ConsumerName,
            "0",
            _options.ReadBatchSize);

        if (entries.Length == 0)
        {
            entries = await _database.StreamReadGroupAsync(
                _options.JobStream,
                _options.ConsumerGroup,
                _options.ConsumerName,
                ">",
                _options.ReadBatchSize);
        }

        var jobs = new List<QueuedEpisodeJob>();
        foreach (var entry in entries)
        {
            var values = entry.Values.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
            if (!values.TryGetValue("podcastId", out var podcastId) || !Guid.TryParse(podcastId, out var parsedPodcastId) ||
                !values.TryGetValue("podcastInUri", out var podcastInUri) ||
                !values.TryGetValue("episodeUrl", out var episodeUrl) ||
                !values.TryGetValue("episodeGuid", out var episodeGuid))
            {
                _logger.LogWarning("Skipping malformed episode job {JobId}", entry.Id);
                await AckAsync(entry.Id.ToString());
                continue;
            }

            jobs.Add(new QueuedEpisodeJob
            {
                Id = entry.Id.ToString(),
                Job = new EpisodeJob
                {
                    PodcastId = parsedPodcastId,
                    PodcastInUri = podcastInUri,
                    EpisodeUrl = episodeUrl,
                    EpisodeGuid = episodeGuid
                }
            });
        }

        return jobs;
    }

    public async Task AckAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job ID is required", nameof(jobId));
        await _database.StreamAcknowledgeAsync(_options.JobStream, _options.ConsumerGroup, jobId);
    }

    public async Task CompleteAsync(QueuedEpisodeJob queuedJob)
    {
        if (queuedJob is null) throw new ArgumentNullException(nameof(queuedJob));

        await AckAsync(queuedJob.Id);
        await _database.KeyDeleteAsync(JobDedupeKey(queuedJob.Job.PodcastId, queuedJob.Job.EpisodeGuid));
    }

    private string JobDedupeKey(Guid podcastId, string episodeGuid) => $"{_options.JobStream}:dedupe:{podcastId}:{episodeGuid}";

    private async Task EnsureConsumerGroupAsync()
    {
        if (_groupInitialized) return;

        await _groupSemaphore.WaitAsync();
        try
        {
            if (_groupInitialized) return;

            try
            {
                await _database.StreamCreateConsumerGroupAsync(_options.JobStream, _options.ConsumerGroup, "0-0", createStream: true);
                _logger.LogInformation("Created Valkey stream consumer group {ConsumerGroup} on {JobStream}", _options.ConsumerGroup, _options.JobStream);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Valkey stream consumer group {ConsumerGroup} already exists", _options.ConsumerGroup);
            }

            _groupInitialized = true;
        }
        finally
        {
            _groupSemaphore.Release();
        }
    }
}
