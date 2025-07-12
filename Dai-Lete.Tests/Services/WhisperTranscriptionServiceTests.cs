using Dai_Lete.Models;
using Dai_Lete.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Dai_Lete.Tests.Services;

public class WhisperTranscriptionServiceTests : IDisposable
{
    private readonly Mock<ILogger<WhisperTranscriptionService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly string _testModelPath;
    private readonly string _testAudioPath;

    public WhisperTranscriptionServiceTests()
    {
        _mockLogger = new Mock<ILogger<WhisperTranscriptionService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockHttpClient = new Mock<HttpClient>();

        _testModelPath = Path.Combine(Path.GetTempPath(), "test-whisper-model.bin");
        _testAudioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "audio-sample1.wav");

        _mockConfiguration.Setup(c => c["Whisper:ModelPath"]).Returns(_testModelPath);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        var httpClient = new HttpClient();
        var service = new WhisperTranscriptionService(_mockLogger.Object, _mockConfiguration.Object, httpClient);

        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        var httpClient = new HttpClient();

        Assert.Throws<ArgumentNullException>(() =>
            new WhisperTranscriptionService(null!, _mockConfiguration.Object, httpClient));
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WhisperTranscriptionService(_mockLogger.Object, _mockConfiguration.Object, null!));
    }

    [Theory]
    [InlineData("", "episode1", "podcast1")]
    [InlineData(null, "episode1", "podcast1")]
    [InlineData("audio.mp3", "", "podcast1")]
    [InlineData("audio.mp3", null, "podcast1")]
    [InlineData("audio.mp3", "episode1", "")]
    [InlineData("audio.mp3", "episode1", null)]
    public async Task TranscribeEpisodeAsync_WithInvalidParameters_ShouldThrowArgumentException(
        string? audioFilePath, string? episodeId, string? podcastId)
    {
        var httpClient = new HttpClient();
        var service = new WhisperTranscriptionService(_mockLogger.Object, _mockConfiguration.Object, httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.TranscribeEpisodeAsync(audioFilePath, episodeId, podcastId));
    }

    [Fact]
    public async Task TranscribeEpisodeAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        var httpClient = new HttpClient();
        var service = new WhisperTranscriptionService(_mockLogger.Object, _mockConfiguration.Object, httpClient);
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.mp3");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.TranscribeEpisodeAsync(nonExistentPath, "episode1", "podcast1"));
    }

    [Fact]
    public void GetFullTranscriptText_WithNullTranscript_ShouldReturnEmptyString()
    {
        var httpClient = new HttpClient();
        var service = new WhisperTranscriptionService(_mockLogger.Object, _mockConfiguration.Object, httpClient);

        var result = service.GetFullTranscriptText(null!);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetFullTranscriptText_WithEmptySegments_ShouldReturnEmptyString()
    {
        var httpClient = new HttpClient();
        var service = new WhisperTranscriptionService(_mockLogger.Object, _mockConfiguration.Object, httpClient);
        var transcript = new EpisodeTranscript
        {
            EpisodeId = "test",
            PodcastId = "test",
            Segments = new List<TranscriptSegment>()
        };

        var result = service.GetFullTranscriptText(transcript);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetFullTranscriptText_WithValidSegments_ShouldReturnConcatenatedText()
    {
        var httpClient = new HttpClient();
        var service = new WhisperTranscriptionService(_mockLogger.Object, _mockConfiguration.Object, httpClient);
        var transcript = new EpisodeTranscript
        {
            EpisodeId = "test",
            PodcastId = "test",
            Segments = new List<TranscriptSegment>
            {
                new() { Text = "Hello", Start = TimeSpan.FromSeconds(0), End = TimeSpan.FromSeconds(1) },
                new() { Text = "world", Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(2) }
            }
        };

        var result = service.GetFullTranscriptText(transcript);

        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void GetTranscriptWithTimestamps_WithValidSegments_ShouldReturnFormattedText()
    {
        var httpClient = new HttpClient();
        var service = new WhisperTranscriptionService(_mockLogger.Object, _mockConfiguration.Object, httpClient);
        var transcript = new EpisodeTranscript
        {
            EpisodeId = "test",
            PodcastId = "test",
            Segments = new List<TranscriptSegment>
            {
                new() { Text = "Hello", Start = TimeSpan.FromSeconds(0), End = TimeSpan.FromSeconds(1) },
                new() { Text = "world", Start = TimeSpan.FromSeconds(61), End = TimeSpan.FromSeconds(62) }
            }
        };

        var result = service.GetTranscriptWithTimestamps(transcript);

        var expected = "[00:00 - 00:01] Hello" + Environment.NewLine + "[01:01 - 01:02] world";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetTranscriptWithTimestamps_WithNullTranscript_ShouldReturnEmptyString()
    {
        var httpClient = new HttpClient();
        var service = new WhisperTranscriptionService(_mockLogger.Object, _mockConfiguration.Object, httpClient);

        var result = service.GetTranscriptWithTimestamps(null!);

        Assert.Equal(string.Empty, result);
    }

    public void Dispose()
    {
        if (File.Exists(_testModelPath))
        {
            File.Delete(_testModelPath);
        }
    }
}