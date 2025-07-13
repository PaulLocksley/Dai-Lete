using System.Xml;
using System.Diagnostics;
using System.Net;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Utilities;
using Dapper;

namespace Dai_Lete.Services;

public class PodcastServices
{
    private readonly ILogger<PodcastServices> _logger;
    private readonly HttpClient _httpClient;
    private readonly IDatabaseService _databaseService;
    private readonly PodcastMetricsService _metricsService;
    private readonly ConfigManager _configManager;
    public PodcastServices(ILogger<PodcastServices> logger, HttpClient httpClient, IDatabaseService databaseService, PodcastMetricsService metricsService, ConfigManager configManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));

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
            var remoteHttpClientHandler = new HttpClientHandler
            {
                Proxy = new WebProxy($"socks5://{_configManager.GetProxyAddress()}")
            };
            var remoteHttpClient = new HttpClient(remoteHttpClientHandler);
            var localHttpClient = new HttpClient();

            localHttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:15.0) Gecko/20100101 Firefox/15.0.1");
            localHttpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            localHttpClient.DefaultRequestHeaders.Add("Referer", "https://podcasts.apple.com/");
            localHttpClient.DefaultRequestHeaders.Add("Accept", "audio/mpeg,audio/*;q=0.9,*/*;q=0.8");

            remoteHttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.10 Safari/605.1.1");
            remoteHttpClient.DefaultRequestHeaders.Add("Accept-Language", "en-GB,en;q=0.8");
            remoteHttpClient.DefaultRequestHeaders.Add("Referer", "https://open.spotify.com/");
            remoteHttpClient.DefaultRequestHeaders.Add("Accept", "audio/webm,audio/ogg,audio/*;q=0.9");

            var workingDirectory = Path.Combine(Path.GetTempPath(), podcast.Id.ToString());
            Directory.CreateDirectory(workingDirectory);
            var destinationLocal = Path.Combine(workingDirectory, $"{episodeGuid}.local");
            var destinationRemote = Path.Combine(workingDirectory, $"{episodeGuid}.remote");

            _logger.LogInformation("Starting dual download for episode {EpisodeGuid} from {EpisodeUrl}", episodeGuid, episodeUrl);

            var d1 = localHttpClient.GetByteArrayAsync(episodeUrl).ContinueWith(task =>
            {
                _logger.LogInformation("local download started");
                if (task.IsFaulted)
                {
                    _logger.LogError(task.Exception, "Local download failed");
                    throw task.Exception ?? new Exception("Local download failed");
                }
                File.WriteAllBytes(destinationLocal, task.Result);
            });

            await d1;

            _logger.LogInformation("Local download completed, waiting 45 seconds before remote download to increase ad differentiation");
            await Task.Delay(TimeSpan.FromSeconds(45));

            var d2 = remoteHttpClient.GetByteArrayAsync(episodeUrl).ContinueWith(task =>
            {
                _logger.LogInformation("remote download started");
                if (task.IsFaulted)
                {
                    _logger.LogError("remote download failed: {AggregateException}", task.Exception);
                    throw task.Exception ?? new Exception("Remote download failed");
                }
                File.WriteAllBytes(destinationRemote, task.Result);
            });

            await d2;

            if (d1.Status != TaskStatus.RanToCompletion || d2.Status != TaskStatus.RanToCompletion)
            {
                throw new Exception($"{d1.Exception} \n {d2.Exception}");
            }

            _logger.LogInformation("Successfully downloaded episode {EpisodeGuid} via both local and remote connections", episodeGuid);

            localHttpClient.Dispose();
            remoteHttpClient.Dispose();
            remoteHttpClientHandler.Dispose();
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

            var workingDirectory = Path.Combine(Path.GetTempPath(), podcastId.ToString());
            var originalFilePath = Path.Combine(workingDirectory, $"{episodeId}.local");
            if (!File.Exists(originalFilePath))
            {
                _logger.LogWarning("Original audio file not found for episode {EpisodeId}: {FilePath}", episodeId, originalFilePath);
                return -1;
            }

            var originalDuration = await GetAudioDurationAsync(originalFilePath);
            _logger.LogDebug("Original episode duration: {Duration} for {EpisodeId}", originalDuration, episodeId);

            _logger.LogInformation($"Starting to process {episodeId}");

            var preLocal = Path.Combine(workingDirectory, $"{episodeId}.local");
            var workingLocal = $"{preLocal}.wav";
            var preRemote = Path.Combine(workingDirectory, $"{episodeId}.remote");
            var workingRemote = $"{preRemote}.wav";
            var processedFile = Path.Combine(workingDirectory, $"{episodeId}processed.wav");

            var finalFolder = Path.Combine(_configManager.GetPodcastStoragePath(), podcastId.ToString());
            var finalFile = Path.Combine(finalFolder, $"{episodeId}.mp3");

            if (!Directory.Exists(finalFolder))
            {
                DirectoryInfo di = Directory.CreateDirectory(finalFolder);
            }

            if (!File.Exists(preRemote))
            {
                _logger.LogError("Remote audio file not found for episode {EpisodeId}: {FilePath}", episodeId, preRemote);
                throw new FileNotFoundException($"Remote audio file not found: {preRemote}");
            }

            if (FileUtilities.GetMd5Sum(preLocal) == FileUtilities.GetMd5Sum(preRemote))
            {
                _logger.LogWarning($"Episodes are identical {episodeId}");
                File.Move(preLocal, finalFile);
                File.Delete(preRemote);
                return (int)new FileInfo(finalFile).Length;
            }

            // Convert to consistent low-quality mono WAV to reduce compression variance
            string ffmpegArgsL = $" -y -i \"{preLocal}\" -ar 22050 -ac 1 -acodec pcm_s16le \"{workingLocal}\"";
            string ffmpegArgsR = $" -y -i \"{preRemote}\" -ar 22050 -ac 1 -acodec pcm_s16le \"{workingRemote}\"";
            string ffmpegArgsFinal = $"-y -i \"{processedFile}\" \"{finalFile}\"";


            Process process = new Process();
            process.StartInfo.FileName = "ffmpeg";
            process.StartInfo.Arguments = ffmpegArgsL;
            process.EnableRaisingEvents = false;
            process.Start();
            while (!process.HasExited)
            {
                Thread.Sleep(100);
            }

            process.StartInfo.Arguments = ffmpegArgsR;
            process.Start();
            while (!process.HasExited)
            {
                Thread.Sleep(100);
            }
            //this is taking forever or not matching. think about it overnight.
                    process.Kill();
        var workingL = File.OpenRead(workingLocal);
        var workingR = File.OpenRead(workingRemote);
        var workingLLength = new FileInfo(workingLocal).Length;
        var workingRLength = new FileInfo(workingRemote).Length;
        var outStream = File.Create(processedFile);

        var oneP = 1024; //read hopefully all meta data and file headers. 
        var twoP = oneP;
        workingR.Seek(oneP, SeekOrigin.Begin);
        var byteWindow = 120000;

        var headers = new byte[oneP];
        workingL.Read(headers, 0, oneP);
        outStream.Write(headers);

        while (workingL.Position + byteWindow < workingLLength && workingR.Position + byteWindow < workingRLength)
        {
            var bufferL = new byte[byteWindow];
            var bufferR = new byte[byteWindow];
            var initialR = workingR.Position;
            workingL.Read(bufferL, 0, byteWindow);
            workingR.Read(bufferR, 0, byteWindow);
            if (bufferL.SequenceEqual(bufferR) || bufferL.All(x => x == 0))
            {
                outStream.Write(bufferL);
                continue;
            }

            for (long i = initialR + 1; i + byteWindow < workingRLength; i++)//todo: time based window match
            {
                workingR.Seek(i, SeekOrigin.Begin);
                workingR.Read(bufferR, 0, byteWindow);
                if (bufferL.SequenceEqual(bufferR))
                {
                    outStream.Write(bufferL);
                    break;
                }
            }

            workingR.Seek(initialR, SeekOrigin.Begin);
        }
        outStream.Close();
        workingL.Close();
        workingR.Close();
        //one more process :) 
        
        process.StartInfo.Arguments = ffmpegArgsFinal;
        process.Start();
        while (!process.HasExited) { Thread.Sleep(100); }
        process.Kill();
        
        File.Delete(preLocal);
        File.Delete(workingLocal);
        File.Delete(preRemote);
        File.Delete(workingRemote);
        File.Delete(processedFile);
        
        _logger.LogInformation($"Completed processing {episodeId}");
        return (int)new FileInfo(finalFile).Length;
            // process.Kill();
            // var workingL = File.OpenRead(workingLocal);
            // var workingR = File.OpenRead(workingRemote);
            // var workingLLength = new FileInfo(workingLocal).Length;
            // var workingRLength = new FileInfo(workingRemote).Length;
            // var outStream = File.Create(processedFile);
            //
            // var oneP = 1024; //read hopefully all meta data and file headers. 
            //
            // var twoP = oneP;
            // workingR.Seek(oneP, SeekOrigin.Begin);
            // var byteWindow = 8192; // Smaller window for better sync detection
            //
            // var headers = new byte[oneP];
            // workingL.ReadExactly(headers, 0, oneP);
            // outStream.Write(headers);
            //
            // // Calculate bytes per second for time-based positioning
            // var localDuration = await GetAudioDurationAsync(workingLocal);
            // var remoteDuration = await GetAudioDurationAsync(workingRemote);
            // var localBytesPerSecond = (workingLLength - oneP) / localDuration.TotalSeconds;
            // var remoteBytesPerSecond = (workingRLength - oneP) / remoteDuration.TotalSeconds;
            //
            // // Track segments to remove (start and end times in seconds)
            // var segmentsToRemove = new List<(double start, double end)>();
            // var currentSegmentStart = -1.0;
            // var baseSearchWindowSeconds = 60.0; // 1 minute base search window for better precision
            // var lastMatchTime = 0.0;
            //
            // while (workingL.Position + byteWindow < workingLLength && workingR.Position + byteWindow < workingRLength)
            // {
            //     var bufferL = new byte[byteWindow];
            //     var bufferR = new byte[byteWindow];
            //     var initialR = workingR.Position;
            //     workingL.Read(bufferL, 0, byteWindow);
            //     workingR.Read(bufferR, 0, byteWindow);
            //     
            //     // Calculate current time position in local file
            //     var currentTimeL = (workingL.Position - oneP - byteWindow) / localBytesPerSecond;
            //     
            //     if (bufferL.SequenceEqual(bufferR) || bufferL.All(x => x == 0) || CalculateByteSimilarity(bufferL, bufferR) > 0.85)
            //     {
            //         // Found exact match - close any open removal segment
            //         if (currentSegmentStart >= 0)
            //         {
            //             segmentsToRemove.Add((currentSegmentStart, currentTimeL));
            //             currentSegmentStart = -1.0;
            //         }
            //         lastMatchTime = 0.0;
            //         continue;
            //     }
            //
            //     // Search forward for match within time window
            //     var searchWindowSeconds = baseSearchWindowSeconds + lastMatchTime;
            //     var maxSearchBytes = (long)(searchWindowSeconds * remoteBytesPerSecond);
            //     var searchEnd = Math.Min(workingRLength - byteWindow, initialR + maxSearchBytes);
            //     
            //     bool matchFound = false;
            //     // Smaller step size for better sync detection
            //     for (long i = initialR + 1; i <= searchEnd; i += byteWindow / 4)
            //     {
            //         workingR.Seek(i, SeekOrigin.Begin);
            //         workingR.Read(bufferR, 0, byteWindow);
            //         if (bufferL.SequenceEqual(bufferR) || CalculateByteSimilarity(bufferL, bufferR) > 0.85)
            //         {
            //             // Found match - close any open removal segment
            //             if (currentSegmentStart >= 0)
            //             {
            //                 segmentsToRemove.Add((currentSegmentStart, currentTimeL));
            //                 currentSegmentStart = -1.0;
            //             }
            //             workingR.Seek(i + byteWindow, SeekOrigin.Begin);
            //             lastMatchTime = 0.0;
            //             matchFound = true;
            //             break;
            //         }
            //     }
            //     
            //     if (!matchFound)
            //     {
            //         // No match found - start tracking removal segment if not already started
            //         if (currentSegmentStart < 0)
            //         {
            //             currentSegmentStart = currentTimeL;
            //         }
            //         lastMatchTime += byteWindow / localBytesPerSecond;
            //         
            //         // If we've been searching for a while, try a wider search to find re-sync
            //         if (lastMatchTime > 20.0) // After 20 seconds of no matches, do wider search
            //         {
            //             var wideSearchEnd = Math.Min(workingRLength - byteWindow, initialR + (long)(180.0 * remoteBytesPerSecond)); // 3 minute wide search
            //             for (long j = initialR + byteWindow; j <= wideSearchEnd; j += byteWindow)
            //             {
            //                 workingR.Seek(j, SeekOrigin.Begin);
            //                 workingR.Read(bufferR, 0, byteWindow);
            //                 if (bufferL.SequenceEqual(bufferR) || CalculateByteSimilarity(bufferL, bufferR) > 0.85)
            //                 {
            //                     // Found re-sync! Close current removal segment and continue from here
            //                     segmentsToRemove.Add((currentSegmentStart, currentTimeL));
            //                     currentSegmentStart = -1.0;
            //                     workingR.Seek(j + byteWindow, SeekOrigin.Begin);
            //                     lastMatchTime = 0.0;
            //                     matchFound = true;
            //                     break;
            //                 }
            //             }
            //         }
            //         
            //         if (!matchFound)
            //         {
            //             workingR.Seek(initialR, SeekOrigin.Begin);
            //         }
            //     }
            // }
            //
            // // Close any final open removal segment
            // if (currentSegmentStart >= 0)
            // {
            //     var finalTime = (workingL.Position - oneP) / localBytesPerSecond;
            //     segmentsToRemove.Add((currentSegmentStart, finalTime));
            // }
            //
            // workingL.Close();
            // workingR.Close();
            // outStream.Close();
            //
            // // Log the segments we're removing for debugging
            // _logger.LogInformation("Found {Count} segments to remove:", segmentsToRemove.Count);
            // foreach (var (start, end) in segmentsToRemove)
            // {
            //     _logger.LogInformation("  Remove: {Start:F2}s - {End:F2}s ({Duration:F2}s)", start, end, end - start);
            // }
            //
            // // Now edit the original file to remove the identified segments
            // await RemoveSegmentsFromOriginalFile(preLocal, finalFile, segmentsToRemove);
            //
            //
            // File.Delete(preLocal);
            // File.Delete(workingLocal);
            // File.Delete(preRemote);
            // File.Delete(workingRemote);
            // File.Delete(processedFile);
            //
            // _logger.LogInformation($"Completed processing {episodeId}");
            // return (int)new FileInfo(finalFile).Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process episode {EpisodeId} for podcast {PodcastId}", episodeId, podcastId);
            throw;
        }
    }

    private string GetEpisodeFilePath(Guid podcastId, string episodeId)
    {
        var baseDir = _configManager.GetPodcastStoragePath();
        return Path.Combine(baseDir, podcastId.ToString(), $"{episodeId}.mp3");
    }

    public async Task<TimeSpan> GetAudioDurationAsync(string filePath)
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

    private static double CalculateByteSimilarity(byte[] buffer1, byte[] buffer2)
    {
        if (buffer1.Length != buffer2.Length) return 0.0;
        
        // Fast sampling approach - check every 4th byte for better accuracy
        int sampleStep = 4;
        int matchingBytes = 0;
        int totalSamples = 0;
        
        for (int i = 0; i < buffer1.Length; i += sampleStep)
        {
            // Allow moderate differences (within 6 values) to account for encoding differences
            if (Math.Abs(buffer1[i] - buffer2[i]) <= 6)
            {
                matchingBytes++;
            }
            totalSamples++;
        }
        
        return totalSamples > 0 ? (double)matchingBytes / totalSamples : 0.0;
    }

    private async Task RemoveSegmentsFromOriginalFile(string inputFile, string outputFile, List<(double start, double end)> segmentsToRemove)
    {
        if (segmentsToRemove.Count == 0)
        {
            // No segments to remove, just copy the original file
            File.Copy(inputFile, outputFile, true);
            return;
        }

        // Build ffmpeg filter to remove segments
        var filterParts = new List<string>();
        var currentTime = 0.0;
        
        foreach (var (start, end) in segmentsToRemove.OrderBy(s => s.start))
        {
            if (start > currentTime)
            {
                // Include segment from currentTime to start
                filterParts.Add($"between(t,{currentTime:F3},{start:F3})");
            }
            currentTime = Math.Max(currentTime, end);
        }
        
        // Include final segment if there's audio after the last removal
        var totalDuration = await GetAudioDurationAsync(inputFile);
        if (currentTime < totalDuration.TotalSeconds)
        {
            filterParts.Add($"between(t,{currentTime:F3},{totalDuration.TotalSeconds:F3})");
        }
        
        if (filterParts.Count == 0)
        {
            // Everything was removed, create empty file
            File.WriteAllBytes(outputFile, Array.Empty<byte>());
            return;
        }
        
        var filter = string.Join("+", filterParts);
        var ffmpegArgs = $"-y -i \"{inputFile}\" -af \"aselect='{filter}',asetpts=N/SR/TB\" \"{outputFile}\"";
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };
        
        process.Start();
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"ffmpeg failed to remove segments: {error}");
        }
    }
}