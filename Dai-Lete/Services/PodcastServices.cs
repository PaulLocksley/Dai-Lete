using System.Buffers;
using System.Xml;
using System.Diagnostics;
using System.Net;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Utilities;
using Dapper;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Text;

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
            _logger.LogInformation("Processing downloaded episode {EpisodeId} for podcast {PodcastId} - 0% complete", episodeId, podcastId);

            var workingDirectory = Path.Combine(Path.GetTempPath(), podcastId.ToString());
            var originalFilePath = Path.Combine(workingDirectory, $"{episodeId}.local");
            if (!File.Exists(originalFilePath))
            {
                _logger.LogWarning("Original audio file not found for episode {EpisodeId}: {FilePath}", episodeId, originalFilePath);
                return -1;
            }

            var originalDuration = await GetAudioDurationAsync(originalFilePath);
            _logger.LogDebug("Original episode duration: {Duration} for {EpisodeId}", originalDuration, episodeId);

            _logger.LogInformation("Starting to process {EpisodeId}", episodeId);

            var preLocal = Path.Combine(workingDirectory, $"{episodeId}.local");
            var workingLocal = $"{preLocal}.wav";
            var qualityLocal = $"{preLocal}.high.wav";
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
                _logger.LogWarning("Episodes are identical {EpisodeId} - 100% complete: No processing needed", episodeId);
                File.Move(preLocal, finalFile);
                File.Delete(preRemote);
                return (int)new FileInfo(finalFile).Length;
            }



            string ffmpegArgsL = $" -y -i \"{preLocal}\" -ar 22050 -ac 1 -acodec pcm_s16le \"{workingLocal}\"";
            string ffmpegArgsR = $" -y -i \"{preRemote}\" -ar 22050 -ac 1 -acodec pcm_s16le \"{workingRemote}\"";
            string ffmpegArgsQualityL = $" -y -i \"{preLocal}\" -ar 48000 -ac 2 -acodec pcm_s16le \"{qualityLocal}\"";
            string ffmpegArgsFinal = $"-y -i \"{processedFile}\" -c:a mp3 -b:a 256k -ar 48000 -ac 2 \"{finalFile}\"";


            foreach (var arguments in new string[] { ffmpegArgsL, ffmpegArgsQualityL, ffmpegArgsR })
            {
                var ffmpeg_result = await RunFfmpegAsync(arguments, $"Working on {podcastId}:{episodeId}");
                if (ffmpeg_result == 0)
                {
                    continue;
                }
                _logger.LogError("FFmpeg failed to convert local file with exit code: {ExitCode}", ffmpeg_result);
                return -1;
            }



            var qualityL = File.OpenRead(qualityLocal);
            var qualityLLength = new FileInfo(qualityLocal).Length;
            var workingL = File.OpenRead(workingLocal);
            var workingLLength = new FileInfo(workingLocal).Length;
            var workingR = File.OpenRead(workingRemote);
            var workingRLength = new FileInfo(workingRemote).Length;
            var outStream = File.Create(processedFile);

            var headerBytes = 1024; //read hopefully all meta data and file headers. 
            workingR.Seek(headerBytes, SeekOrigin.Begin);
            workingL.Seek(headerBytes, SeekOrigin.Begin);
            var audioLength = await GetAudioDurationAsync(workingLocal);
            var bytesPerSecond = workingLLength / audioLength.TotalSeconds;
            var threeSecondByteWindow = (int)Math.Ceiling(bytesPerSecond * 3);
            threeSecondByteWindow = (threeSecondByteWindow / 2) * 2; //align to samples, hopefully not far off.

            var qualityBytesPerSecond = qualityLLength / audioLength.TotalSeconds;
            var qualityThreeSecondByteWindow = (int)Math.Ceiling(qualityBytesPerSecond * 3);
            qualityThreeSecondByteWindow = (qualityThreeSecondByteWindow / 4) * 4; // as above.

            var headers = new byte[headerBytes];
            qualityL.ReadExactly(headers, 0, headerBytes);
            outStream.Write(headers);
            var dropedFramesSinceLastHit = 0;

            var bufferL = new byte[threeSecondByteWindow];
            var bufferR = new byte[threeSecondByteWindow];
            var qualityBufferL = new byte[qualityThreeSecondByteWindow];
            var chunkCount = 0;

            while (workingL.Position + threeSecondByteWindow <= workingLLength &&
                    workingR.Position + threeSecondByteWindow <= workingRLength &&
                    qualityL.Position + qualityThreeSecondByteWindow <= qualityLLength)
            {
                bufferL.AsSpan().Clear();
                bufferR.AsSpan().Clear();
                qualityBufferL.AsSpan().Clear();
                var initialR = workingR.Position;
                workingL.ReadExactly(bufferL, 0, threeSecondByteWindow);
                workingR.ReadExactly(bufferR, 0, threeSecondByteWindow);
                qualityL.ReadExactly(qualityBufferL, 0, qualityThreeSecondByteWindow);
                var spanL = bufferL.AsSpan();
                var spanR = bufferR.AsSpan();
                chunkCount++;
                if (chunkCount % 10 == 0)
                {
                    var percentComplete = (double)workingL.Position / workingLLength * 100;
                    _logger.LogInformation("Episode {EpisodeId} - {Percent:F1}% complete: Processed {BytesProcessed:N0}/{TotalBytes:N0} bytes",
                        episodeId, percentComplete, workingL.Position, workingLLength);
                }

                if (spanL.SequenceEqual(spanR) || bufferL.All(x => x == 0))
                {
                    outStream.Write(qualityBufferL);
                    dropedFramesSinceLastHit = 0;
                    continue;
                }
                // look ahead four minutes plus byte window since last hit or end of file, whatever is smaller.
                var lookAheadCap = Math.Min(initialR + ((threeSecondByteWindow / 3) * _configManager.GetLookAheadDistance()) + (threeSecondByteWindow * dropedFramesSinceLastHit), workingRLength);
                var readDistance = (int)(lookAheadCap - initialR);
                var bigBufferR = new byte[readDistance];
                workingR.Seek(initialR + 1, SeekOrigin.Begin);
                var bytesRead = workingR.Read(bigBufferR, 0, readDistance);
                var lookAheadSPan = bigBufferR.AsSpan(0, bytesRead);
                var matchFound = false;
                for (var offset = 1; offset < lookAheadSPan.Length - threeSecondByteWindow; offset++)
                {
                    var candidate = lookAheadSPan.Slice(offset, threeSecondByteWindow);
                    if (!spanL.SequenceEqual(candidate)) continue;
                    outStream.Write(qualityBufferL);
                    dropedFramesSinceLastHit = 0;
                    matchFound = true;
                    workingR.Seek(initialR + offset, SeekOrigin.Begin);
                    break;
                }

                if (matchFound) { continue; }
                dropedFramesSinceLastHit++;
                workingR.Seek(initialR, SeekOrigin.Begin);
            }
            outStream.Close();
            workingL.Close();
            workingR.Close();
            qualityL.Close();



            var result = await RunFfmpegAsync(ffmpegArgsFinal, $"final processing for {podcastId}:{episodeId}");
            if (result != 0)
            {
                _logger.LogError("FFmpeg failed to convert local file with exit code: {ExitCode}", result);
                return -1;
            }

            // Calculate processed duration and record metrics
            var processedDuration = await GetAudioDurationAsync(finalFile);
            var timeSaved = originalDuration - processedDuration;
            if (timeSaved < TimeSpan.FromSeconds(originalDuration.TotalSeconds * 0.7))
            {
                _logger.LogWarning("Less than 70% of the original file remains, something went wrong :(");
                File.Move(preLocal, finalFile, overwrite: true);
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    File.Copy(preLocal, $"{preLocal}-failed-local");
                    File.Copy(preRemote, $"{preRemote}-failed-remote");
                }
                timeSaved = TimeSpan.Zero;
            }
            if (timeSaved > TimeSpan.Zero)
            {
                var podcastName = await GetPodcastNameAsync(podcastId);
                _metricsService.RecordTimeSaved(podcastId, episodeId, timeSaved, podcastName);
                _logger.LogInformation("Recorded {TimeSaved:F1} seconds saved for episode {EpisodeId}",
                    timeSaved.TotalSeconds, episodeId);
            }

            File.Delete(preLocal);
            File.Delete(qualityLocal);
            File.Delete(workingLocal);
            File.Delete(preRemote);
            File.Delete(workingRemote);
            File.Delete(processedFile);

            _logger.LogInformation("Episode {EpisodeId} - 100% complete: Processing finished successfully", episodeId);
            return (int)new FileInfo(finalFile).Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process episode {EpisodeId} for podcast {PodcastId}", episodeId, podcastId);
            throw;
        }
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

    private async Task<int> RunFfmpegAsync(string arguments, string description)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        process.StartInfo.FileName = "ffmpeg";
        process.EnableRaisingEvents = false;
        process.StartInfo.Arguments = arguments;

        _logger.LogInformation("Starting FFmpeg for {Description} with arguments: {Arguments}", description, arguments);
        var errorBuilder = new StringBuilder();

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        var errorOutput = errorBuilder.ToString();

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg failed to convert {Description} file with exit code: {ExitCode}", description, process.ExitCode);
            throw new Exception($"Failed to process ffmpeg command with arguments: {arguments}, {errorOutput.ToString()}");
        }

        _logger.LogInformation("FFmpeg completed {Description} conversion successfully.", description);
        return 0;
    }
}