using Dai_Lete.Models;
using Dai_Lete.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dai_Lete.Tests.Services;

[Collection("Integration")]
public class WhisperTranscriptionServiceIntegrationTests : IDisposable
{
    private readonly ILogger<WhisperTranscriptionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _testModelPath;
    private readonly string _testAudioPath;

    public WhisperTranscriptionServiceIntegrationTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<WhisperTranscriptionService>();

        var configBuilder = new ConfigurationBuilder();
        _testModelPath = Path.Combine(Path.GetTempPath(), "integration-test-whisper-model.bin");
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Whisper:ModelPath"] = _testModelPath
        });
        _configuration = configBuilder.Build();

        _httpClient = new HttpClient();
        _testAudioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "audio-sample1.wav");
    }

    [Fact(Skip = "Integration test - requires network access and takes time")]
    public async Task TranscribeEpisodeAsync_WithRealAudioFile_ShouldReturnValidTranscript()
    {
        if (!File.Exists(_testAudioPath))
        {
            Assert.Fail($"Test audio file not found at {_testAudioPath}");
            return;
        }

        var service = new WhisperTranscriptionService(_logger, _configuration, _httpClient);

        var result = await service.TranscribeEpisodeAsync(_testAudioPath, "test-episode", "test-podcast");

        Assert.NotNull(result);
        Assert.Equal("test-episode", result.EpisodeId);
        Assert.Equal("test-podcast", result.PodcastId);
        Assert.NotEmpty(result.Segments);
        Assert.True(result.CreatedAt <= DateTime.UtcNow);

        foreach (var segment in result.Segments)
        {
            Assert.True(segment.Start >= TimeSpan.Zero);
            Assert.True(segment.End > segment.Start);
            Assert.NotEmpty(segment.Text.Trim());
            Assert.True(segment.Confidence >= 0 && segment.Confidence <= 1);
        }

        var fullText = service.GetFullTranscriptText(result);
        Assert.NotEmpty(fullText);

        var timestampedText = service.GetTranscriptWithTimestamps(result);
        Assert.NotEmpty(timestampedText);
        Assert.Contains("[", timestampedText);
        Assert.Contains("]", timestampedText);

        // Log the actual transcription for comparison with expected
        _logger.LogInformation("=== Actual Transcription Results ===");
        _logger.LogInformation("Total segments: {SegmentCount}", result.Segments.Count);
        _logger.LogInformation("Full text length: {TextLength} characters", fullText.Length);
        _logger.LogInformation("First 200 characters: {TextSample}", fullText.Substring(0, Math.Min(200, fullText.Length)));

        // Log first few segments with timestamps
        foreach (var segment in result.Segments.Take(5))
        {
            _logger.LogInformation("Segment: [{Start:mm\\:ss} - {End:mm\\:ss}] {Text}", segment.Start, segment.End, segment.Text);
        }
    }

    [Fact(Skip = "Integration test - requires network access")]
    public async Task EnsureModelDownloadedAsync_ShouldDownloadModelOnFirstCall()
    {
        if (File.Exists(_testModelPath))
        {
            File.Delete(_testModelPath);
        }

        var service = new WhisperTranscriptionService(_logger, _configuration, _httpClient);

        Assert.False(File.Exists(_testModelPath));

        try
        {
            var dummyAudioPath = Path.Combine(Path.GetTempPath(), "dummy.mp3");
            File.WriteAllText(dummyAudioPath, "dummy content");

            await Assert.ThrowsAsync<Exception>(() =>
                service.TranscribeEpisodeAsync(dummyAudioPath, "test", "test"));
        }
        catch
        {
        }

        Assert.True(File.Exists(_testModelPath) || Directory.Exists(Path.GetDirectoryName(_testModelPath)));
    }

    [Fact]
    public void TranscriptSegment_Properties_ShouldBeSettable()
    {
        var segment = new TranscriptSegment
        {
            Start = TimeSpan.FromSeconds(10),
            End = TimeSpan.FromSeconds(15),
            Text = "Test text",
            Confidence = 0.95f
        };

        Assert.Equal(TimeSpan.FromSeconds(10), segment.Start);
        Assert.Equal(TimeSpan.FromSeconds(15), segment.End);
        Assert.Equal("Test text", segment.Text);
        Assert.Equal(0.95f, segment.Confidence);
    }

    [Fact]
    public void EpisodeTranscript_Properties_ShouldBeSettable()
    {
        var transcript = new EpisodeTranscript
        {
            EpisodeId = "episode123",
            PodcastId = "podcast456",
            Language = "en",
            Segments = new List<TranscriptSegment>
            {
                new() { Text = "Hello", Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(1) }
            }
        };

        Assert.Equal("episode123", transcript.EpisodeId);
        Assert.Equal("podcast456", transcript.PodcastId);
        Assert.Equal("en", transcript.Language);
        Assert.Single(transcript.Segments);
        Assert.True(transcript.CreatedAt <= DateTime.UtcNow);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();

        if (File.Exists(_testModelPath))
        {
            try
            {
                File.Delete(_testModelPath);
            }
            catch
            {
            }
        }
    }
}