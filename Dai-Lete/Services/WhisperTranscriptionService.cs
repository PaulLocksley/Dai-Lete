using System.Diagnostics;
using Dai_Lete.Models;
using Microsoft.CSharp.RuntimeBinder;
using Whisper.net;
using Whisper.net.Ggml;

namespace Dai_Lete.Services;

public class WhisperTranscriptionService
{
    private readonly ILogger<WhisperTranscriptionService> _logger;
    private readonly string _modelPath;
    private readonly HttpClient _httpClient;

    public WhisperTranscriptionService(ILogger<WhisperTranscriptionService> logger, IConfiguration configuration, HttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _modelPath = configuration["Whisper:ModelPath"] ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "ggml-base.bin");
    }

    public async Task<EpisodeTranscript> TranscribeEpisodeAsync(string audioFilePath, string episodeId, string podcastId)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath))
            throw new ArgumentException("Audio file path cannot be null or empty", nameof(audioFilePath));
        if (string.IsNullOrWhiteSpace(episodeId))
            throw new ArgumentException("Episode ID cannot be null or empty", nameof(episodeId));
        if (string.IsNullOrWhiteSpace(podcastId))
            throw new ArgumentException("Podcast ID cannot be null or empty", nameof(podcastId));

        if (!File.Exists(audioFilePath))
            throw new FileNotFoundException($"Audio file not found: {audioFilePath}");

        _logger.LogInformation("Starting transcription for episode {EpisodeId} from file {AudioFile}", episodeId, audioFilePath);

        try
        {
            await EnsureModelDownloadedAsync();
            await CreateWhisperCompatibleAudioFile(audioFilePath);
            using var whisperFactory = WhisperFactory.FromPath(_modelPath);
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage("auto")
                .Build();

            var transcript = new EpisodeTranscript
            {
                EpisodeId = episodeId,
                PodcastId = podcastId
            };
             
            using var fileStream = File.OpenRead($"{audioFilePath}.low.wav");
            await foreach (var result in processor.ProcessAsync(fileStream))
            {
                var segment = new TranscriptSegment
                {
                    Start = result.Start,
                    End = result.End,
                    Text = result.Text.Trim(),
                    Confidence = result.Probability
                };

                if (!string.IsNullOrWhiteSpace(segment.Text))
                {
                    transcript.Segments.Add(segment);
                }
            }

            _logger.LogInformation("Transcription completed for episode {EpisodeId}. Generated {SegmentCount} segments", 
                episodeId, transcript.Segments.Count);
            CleanupWhisperCompatibleAudioFile(audioFilePath);
            return transcript;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe episode {EpisodeId} from file {AudioFile}", episodeId, audioFilePath);
            throw;
        }
    }

    private void CleanupWhisperCompatibleAudioFile(string audioFilePath)
    {
        if(!audioFilePath.EndsWith(".low.wav")){
            audioFilePath = $"{audioFilePath}.low.wav";
        }
        File.Delete(audioFilePath);
    }

    private async Task CreateWhisperCompatibleAudioFile(string audioFilePath)
    {
        // Whisper requires mono 16000khz
        _logger.LogDebug($"Preparing {audioFilePath} for transcription.");
        var ffmpegArguments = $"-i {audioFilePath} -ar 16000 -ac 1 {audioFilePath}.low.wav";
        var ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        ffmpegProcess.Start();
        var error = await ffmpegProcess.StandardError.ReadToEndAsync();
        await ffmpegProcess.WaitForExitAsync();
        if (ffmpegProcess.ExitCode != 0)
        {
            throw new System.Exception($"FFMPEG exited with code {ffmpegProcess.ExitCode}. Error: {error}");
        }
    }

    private async Task EnsureModelDownloadedAsync()
    {
        if (File.Exists(_modelPath))
        {
            _logger.LogDebug("Whisper model already exists at {ModelPath}", _modelPath);
            return;
        }

        _logger.LogInformation("Downloading Whisper model to {ModelPath}", _modelPath);
        
        var modelDir = Path.GetDirectoryName(_modelPath);
        if (!string.IsNullOrEmpty(modelDir) && !Directory.Exists(modelDir))
        {
            Directory.CreateDirectory(modelDir);
        }

        try
        {
            var downloader = new WhisperGgmlDownloader(_httpClient);
            using var modelStream = await downloader.GetGgmlModelAsync(GgmlType.Base);
            using var fileStream = new FileStream(_modelPath, FileMode.Create, FileAccess.Write);
            await modelStream.CopyToAsync(fileStream);
            
            _logger.LogInformation("Successfully downloaded Whisper model to {ModelPath}", _modelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download Whisper model to {ModelPath}", _modelPath);
            throw;
        }
    }

    public string GetFullTranscriptText(EpisodeTranscript transcript)
    {
        if (transcript?.Segments == null || !transcript.Segments.Any())
            return string.Empty;

        return string.Join(" ", transcript.Segments.Select(s => s.Text));
    }

    public string GetTranscriptWithTimestamps(EpisodeTranscript transcript)
    {
        if (transcript?.Segments == null || !transcript.Segments.Any())
            return string.Empty;

        var lines = transcript.Segments.Select(segment =>
            $"[{segment.Start:mm\\:ss} - {segment.End:mm\\:ss}] {segment.Text}");

        return string.Join(Environment.NewLine, lines);
    }
}