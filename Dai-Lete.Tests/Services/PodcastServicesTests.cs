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
    public PodcastServicesTests()
    {
        _mockLogger = new Mock<ILogger<PodcastServices>>();
        _mockHttpClient = new Mock<HttpClient>();
        _mockDatabaseService = new Mock<IDatabaseService>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        var httpClient = new HttpClient();

        var service = new PodcastServices(
            _mockLogger.Object,
            httpClient,
            _mockDatabaseService.Object);

        Assert.NotNull(service);
    }



    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ProcessDownloadedEpisodeAsync_WithInvalidEpisodeId_ShouldThrowArgumentException(string? episodeId)
    {
        var httpClient = new HttpClient();
        var service = new PodcastServices(
            _mockLogger.Object,
            httpClient,
            _mockDatabaseService.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ProcessDownloadedEpisodeAsync(Guid.NewGuid(), episodeId));
    }

    [Fact]
    public async Task ProcessDownloadedEpisodeAsync_WithNonExistentAudioFile_ShouldReturnMinusOne()
    {
        var httpClient = new HttpClient();
        var service = new PodcastServices(
            _mockLogger.Object,
            httpClient,
            _mockDatabaseService.Object);

        var podcastId = Guid.NewGuid();
        var episodeId = "test-episode";

        var result = await service.ProcessDownloadedEpisodeAsync(podcastId, episodeId);

        Assert.Equal(-1, result);
    }
}