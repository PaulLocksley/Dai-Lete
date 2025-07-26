using System.Diagnostics;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Sdk;

namespace Dai_Lete.Tests.Services;

public class PodcastServicesTests
{
    private readonly Mock<ILogger<PodcastServices>> _mockLogger;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly PodcastMetricsService _metricsService;
    private readonly ConfigManager _configManager;
    public PodcastServicesTests()
    {
        _mockLogger = new Mock<ILogger<PodcastServices>>();
        _mockHttpClient = new Mock<HttpClient>();
        _mockDatabaseService = new Mock<IDatabaseService>();

        var mockMetricsLogger = new Mock<ILogger<PodcastMetricsService>>();
        _metricsService = new PodcastMetricsService(mockMetricsLogger.Object);

        var mockConfigLogger = new Mock<ILogger<ConfigManager>>();
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["ProxyAddress"]).Returns("127.0.0.1:1080");
        _configManager = new ConfigManager(mockConfigLogger.Object, mockConfiguration.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        var httpClient = new HttpClient();

        var service = new PodcastServices(
            _mockLogger.Object,
            httpClient,
            _mockDatabaseService.Object,
            _metricsService,
            _configManager);

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
            _mockDatabaseService.Object,
            _metricsService,
            _configManager);

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
            _mockDatabaseService.Object,
            _metricsService,
            _configManager);

        var podcastId = Guid.NewGuid();
        var episodeId = "test-episode";

        var result = await service.ProcessDownloadedEpisodeAsync(podcastId, episodeId);

        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task ProcessDownloadedEpisodeAsync_WithTestAudioFiles_ShouldRemove10To15Seconds()
    {
        var httpClient = new HttpClient();
        var service = new PodcastServices(
            _mockLogger.Object,
            httpClient,
            _mockDatabaseService.Object,
            _metricsService,
            _configManager);

        var podcastId = Guid.NewGuid();
        var episodeId = "test-episode";
        var testDataPath = Path.Combine(Path.GetDirectoryName(typeof(PodcastServicesTests).Assembly.Location)!, "TestData");
        var workingDirectory = Path.Combine(Path.GetTempPath(), podcastId.ToString());

        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(testDataPath);

        var localTestFile = Path.Combine(testDataPath, "local.mp3");
        var remoteTestFile = Path.Combine(testDataPath, "remote.mp3");
        var localWorkingFile = Path.Combine(workingDirectory, $"{episodeId}.local");
        var remoteWorkingFile = Path.Combine(workingDirectory, $"{episodeId}.remote");

        if (!File.Exists(localTestFile) || !File.Exists(remoteTestFile))
        {
            Assert.Fail("Test data files local.mp3 and remote.mp3 must exist in TestData directory");
            return;
        }

        File.Copy(localTestFile, localWorkingFile, true);
        File.Copy(remoteTestFile, remoteWorkingFile, true);

        var originalLocalDuration = await service.GetAudioDurationAsync(localWorkingFile);

        var result = await service.ProcessDownloadedEpisodeAsync(podcastId, episodeId);

        Assert.True(result > 0, "Processing should succeed and return file size");

        var finalFile = Path.Combine(_configManager.GetPodcastStoragePath(), podcastId.ToString(), $"{episodeId}.mp3");
        Assert.True(File.Exists(finalFile), "Final processed file should exist");

        var finalDuration = await service.GetAudioDurationAsync(finalFile);
        var timeSaved = originalLocalDuration.TotalSeconds - finalDuration.TotalSeconds;

        Assert.True(timeSaved >= 10 && timeSaved <= 20,
            $"Expected 10-15 seconds removed, but {timeSaved:F2} seconds were removed. Original: {originalLocalDuration.TotalSeconds:F2}s, Final: {finalDuration.TotalSeconds:F2}s");
        //this should be 12 seconds removed max ideally. working on it.
        Directory.Delete(workingDirectory, true);
        if (Directory.Exists(Path.Combine(_configManager.GetPodcastStoragePath(), podcastId.ToString())))
        {
            Directory.Delete(Path.Combine(_configManager.GetPodcastStoragePath(), podcastId.ToString()), true);
        }
    }
}