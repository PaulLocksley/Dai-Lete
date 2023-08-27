using System.IO.Compression;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;

namespace Dai_Lete.ScheduledTasks;

public class ConvertNewEpisodes : IHostedService, IDisposable
{
    private int executionCount = 0;
    private readonly ILogger<ConvertNewEpisodes> _logger;
    private Timer? _timer = null;

    public ConvertNewEpisodes(ILogger<ConvertNewEpisodes> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Timed Hosted Service running.");

        _timer = new Timer(DoWork, null, TimeSpan.Zero,
            TimeSpan.FromHours(1));

        return Task.CompletedTask;
    }

    private void DoWork(object? state)
    {
        var count = Interlocked.Increment(ref executionCount);
        
        
        var plist = PodcastServices.GetPodcasts();
        
        foreach (var podcast in plist)
        {
            var sql = "Select Id FROM Episodes WHERE Id = @eid";
            var latest = PodcastServices.GetLatestEpsiode(podcast.Id);
            var episodeList = SqLite.Connection().Query<string>(sql, new {eid = latest.guid });

            if (episodeList.Any())
            {
                Console.WriteLine($"No new episodes of {podcast.InUri}");
                continue;
            }
            
            PodcastServices.downloadEpsisode(podcast, latest.downloadLink);
            //while (!t.IsCompleted){ Thread.Sleep(10);}
            var filesize = PodcastServices.processLatest(podcast.Id,latest.guid);
            sql = @"INSERT INTO Episodes (Id,PodcastId,FileSize) VALUES (@id,@pid,@fs)";
            SqLite.Connection().Execute(sql, new { id = latest.guid, pid = podcast.Id, fs = filesize });
            Console.WriteLine("Outerdone.");
            
        }
        _logger.LogInformation(
            "Timed Hosted Service is working. Count: {Count}", count);
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Timed Hosted Service is stopping.");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}