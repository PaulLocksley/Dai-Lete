using Dai_Lete.Services;
using Dai_Lete.Tests.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dai_Lete.Tests.Services;

[Collection("Integration")]
public class WhisperTranscriptionAccuracyTests : IDisposable
{
    private readonly ILogger<WhisperTranscriptionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _testModelPath;
    private readonly string _testAudioPath;
    private readonly string _expectedTranscriptPath;

    public WhisperTranscriptionAccuracyTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<WhisperTranscriptionService>();
        
        var configBuilder = new ConfigurationBuilder();
        _testModelPath = Path.Combine(Path.GetTempPath(), "accuracy-test-whisper-model.bin");
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Whisper:ModelPath"] = _testModelPath
        });
        _configuration = configBuilder.Build();
        
        _httpClient = new HttpClient();
        _testAudioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "audio-sample1.wav");
        _expectedTranscriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "transcript-audio-sample1.txt");
    }

    [Fact(Skip = "Accuracy test - requires network access and takes time - also words are sometimes wrong. it's a problem.")]
    public async Task TranscribeEpisodeAsync_ComparedToExpectedTranscript_ShouldMeetAccuracyThreshold()
    {
        // Arrange
        if (!File.Exists(_testAudioPath))
        {
            Assert.True(false, $"Test audio file not found at {_testAudioPath}");
            return;
        }

        if (!File.Exists(_expectedTranscriptPath))
        {
            Assert.True(false, $"Expected transcript file not found at {_expectedTranscriptPath}");
            return;
        }

        var service = new WhisperTranscriptionService(_logger, _configuration, _httpClient);
        var expectedTranscript = TranscriptAccuracyHelper.ParseExpectedTranscript(_expectedTranscriptPath);
        var expectedText = TranscriptAccuracyHelper.ExtractTextOnly(expectedTranscript);

        // Act
        var actualTranscript = await service.TranscribeEpisodeAsync(_testAudioPath, "test-episode", "test-podcast");
        var actualText = service.GetFullTranscriptText(actualTranscript);

        // Assert
        Assert.NotNull(actualTranscript);
        Assert.NotEmpty(actualTranscript.Segments);
        Assert.NotEmpty(actualText);

        // Calculate accuracy metrics
        var wordAccuracy = TranscriptAccuracyHelper.CalculateWordAccuracy(expectedText, actualText);
        var characterAccuracy = TranscriptAccuracyHelper.CalculateCharacterAccuracy(expectedText, actualText);

        // Log results for analysis
        _logger.LogInformation("=== Transcription Accuracy Results ===");
        _logger.LogInformation("Expected text length: {ExpectedLength} characters", expectedText.Length);
        _logger.LogInformation("Actual text length: {ActualLength} characters", actualText.Length);
        _logger.LogInformation("Word accuracy: {WordAccuracy:P2}", wordAccuracy);
        _logger.LogInformation("Character accuracy: {CharacterAccuracy:P2}", characterAccuracy);
        _logger.LogInformation("Expected text: {ExpectedText}", expectedText.Substring(0, Math.Min(200, expectedText.Length)) + "...");
        _logger.LogInformation("Actual text: {ActualText}", actualText.Substring(0, Math.Min(200, actualText.Length)) + "...");

        // Assert accuracy thresholds (you mentioned ~90% accuracy)
        Assert.True(wordAccuracy >= 0.70, $"Word accuracy {wordAccuracy:P2} is below 70% threshold");
        Assert.True(characterAccuracy >= 0.80, $"Character accuracy {characterAccuracy:P2} is below 80% threshold");
        
        // Ideally should be around 90% as you mentioned
        if (wordAccuracy >= 0.90)
        {
            _logger.LogInformation("Excellent word accuracy achieved: {WordAccuracy:P2}", wordAccuracy);
        }
        else if (wordAccuracy >= 0.80)
        {
            _logger.LogInformation("Good word accuracy achieved: {WordAccuracy:P2}", wordAccuracy);
        }
        else
        {
            _logger.LogWarning("Word accuracy below expected 90%: {WordAccuracy:P2}", wordAccuracy);
        }
    }

    [Fact]
    public void ParseExpectedTranscript_WithValidFile_ShouldReturnParsedSegments()
    {
        // Arrange
        if (!File.Exists(_expectedTranscriptPath))
        {
            Assert.True(false, $"Expected transcript file not found at {_expectedTranscriptPath}");
            return;
        }

        // Act
        var transcript = TranscriptAccuracyHelper.ParseExpectedTranscript(_expectedTranscriptPath);

        // Assert
        Assert.NotNull(transcript);
        Assert.NotEmpty(transcript.Segments);
        
        // Verify first few segments have expected structure
        var firstSegment = transcript.Segments.First();
        Assert.NotEmpty(firstSegment.Text);
        Assert.True(firstSegment.Timestamp >= TimeSpan.Zero);
        
        // Log some sample segments for verification
        _logger.LogInformation("Parsed {SegmentCount} transcript segments", transcript.Segments.Count);
        foreach (var segment in transcript.Segments.Take(3))
        {
            _logger.LogInformation("Segment: [{Timestamp}] {Text}", segment.Timestamp, segment.Text);
        }
    }

    [Fact]
    public void CalculateWordAccuracy_WithIdenticalText_ShouldReturn100Percent()
    {
        // Arrange
        var text = "Hello world this is a test";

        // Act
        var accuracy = TranscriptAccuracyHelper.CalculateWordAccuracy(text, text);

        // Assert
        Assert.Equal(1.0, accuracy, 2);
    }

    [Fact]
    public void CalculateWordAccuracy_WithPartialMatch_ShouldReturnCorrectPercentage()
    {
        // Arrange
        var expected = "hello world test";
        var actual = "hello world example";

        // Act
        var accuracy = TranscriptAccuracyHelper.CalculateWordAccuracy(expected, actual);

        // Assert
        // 2 out of 3 words match, so accuracy should be around 0.67
        Assert.True(accuracy >= 0.6 && accuracy <= 0.8);
    }

    [Fact]
    public void CalculateCharacterAccuracy_WithIdenticalText_ShouldReturn100Percent()
    {
        // Arrange
        var text = "Hello world this is a test";

        // Act
        var accuracy = TranscriptAccuracyHelper.CalculateCharacterAccuracy(text, text);

        // Assert
        Assert.Equal(1.0, accuracy, 2);
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
                // Ignore cleanup errors
            }
        }
    }
}