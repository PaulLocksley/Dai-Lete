using Dai_Lete.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;

namespace Dai_Lete.Tests.Services;

public class LanguageModelServiceTests
{
    private readonly Mock<ILogger<LanguageModelService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;

    public LanguageModelServiceTests()
    {
        _mockLogger = new Mock<ILogger<LanguageModelService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => 
            new LanguageModelService(null!, _mockConfiguration.Object, _httpClient));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => 
            new LanguageModelService(_mockLogger.Object, null!, _httpClient));
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => 
            new LanguageModelService(_mockLogger.Object, _mockConfiguration.Object, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_DoesNotThrow()
    {
        var service = new LanguageModelService(_mockLogger.Object, _mockConfiguration.Object, _httpClient);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task PromptAsync_WithoutModelLoaded_ThrowsInvalidOperationException()
    {
        _mockConfiguration.Setup(c => c["LanguageModel:ModelUrl"]).Returns("http://test.com/model.gguf");
        _mockConfiguration.Setup(c => c["LanguageModel:ModelPath"]).Returns("test/models");

        var service = new LanguageModelService(_mockLogger.Object, _mockConfiguration.Object, _httpClient);

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", 
                ItExpr.IsAny<HttpRequestMessage>(), 
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.PromptAsync("test prompt"));
    }
}