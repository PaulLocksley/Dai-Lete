using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Dai_Lete.Tests.Services;

public class PodcastServicesTests
{
    private readonly Mock<ILogger<PodcastServices>> _mockLogger;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<WhisperTranscriptionService> _mockTranscriptionService;

    public PodcastServicesTests()
    {
        _mockLogger = new Mock<ILogger<PodcastServices>>();
        _mockHttpClient = new Mock<HttpClient>();
        _mockDatabaseService = new Mock<IDatabaseService>();
        _mockTranscriptionService = new Mock<WhisperTranscriptionService>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        var httpClient = new HttpClient();
        var transcriptionService = CreateMockTranscriptionService();

        var service = new PodcastServices(
            _mockLogger.Object,
            httpClient,
            _mockDatabaseService.Object,
            transcriptionService);

        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullTranscriptionService_ShouldThrowArgumentNullException()
    {
        var httpClient = new HttpClient();

        Assert.Throws<ArgumentNullException>(() =>
            new PodcastServices(_mockLogger.Object, httpClient, _mockDatabaseService.Object, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ProcessDownloadedEpisodeAsync_WithInvalidEpisodeId_ShouldThrowArgumentException(string? episodeId)
    {
        var httpClient = new HttpClient();
        var transcriptionService = CreateMockTranscriptionService();
        var service = new PodcastServices(
            _mockLogger.Object,
            httpClient,
            _mockDatabaseService.Object,
            transcriptionService);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ProcessDownloadedEpisodeAsync(Guid.NewGuid(), episodeId));
    }

    [Fact]
    public async Task ProcessDownloadedEpisodeAsync_WithNonExistentAudioFile_ShouldReturnMinusOne()
    {
        var httpClient = new HttpClient();
        var transcriptionService = CreateMockTranscriptionService();
        var service = new PodcastServices(
            _mockLogger.Object,
            httpClient,
            _mockDatabaseService.Object,
            transcriptionService);

        var podcastId = Guid.NewGuid();
        var episodeId = "test-episode";

        var result = await service.ProcessDownloadedEpisodeAsync(podcastId, episodeId);

        Assert.Equal(-1, result);
    }

    private WhisperTranscriptionService CreateMockTranscriptionService()
    {
        var mockLogger = new Mock<ILogger<WhisperTranscriptionService>>();
        var mockConfig = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        var httpClient = new HttpClient();

        mockConfig.Setup(c => c["Whisper:ModelPath"]).Returns(Path.GetTempPath());

        return new WhisperTranscriptionService(mockLogger.Object, mockConfig.Object, httpClient);
    }
}