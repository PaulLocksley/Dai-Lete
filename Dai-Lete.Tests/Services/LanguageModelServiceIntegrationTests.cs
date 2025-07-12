using Dai_Lete.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Dai_Lete.Tests.Services;

public class LanguageModelServiceIntegrationTests
{
    private readonly Mock<ILogger<LanguageModelService>> _mockLogger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public LanguageModelServiceIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<LanguageModelService>>();
        
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LanguageModel:ModelUrl"] = "https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/resolve/main/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf",
                ["LanguageModel:ModelPath"] = "TestData/models"
            });
        _configuration = configBuilder.Build();
        
        _httpClient = new HttpClient();
    }

    [Fact]//(Skip = "Integration test - requires model download and significant compute time")]
    public async Task PromptAsync_WithSampleTranscript_ShouldGenerateSummary()
    {
        var service = new LanguageModelService(_mockLogger.Object, _configuration, _httpClient);
        
        var transcript = await File.ReadAllTextAsync("TestData/transcript-audio-sample1.txt");
        
        var prompt = $@"Question: Does this mention weather?
Text: ""It's raining today""
Answer: {{""answer"": ""TRUE"", ""confidence"": 95}}

Question: Does this mention weather?
Text: ""I like pizza""
Answer: {{""answer"": ""FALSE"", ""confidence"": 90}}

Question: Does this transcript mention a magazine article?
Text: ""{transcript.Substring(0, Math.Min(500, transcript.Length))}""
Answer:";
        
        var response = await service.PromptAsync(prompt);
        
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        // Try to parse JSON response
        try
        {
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var json = System.Text.Json.JsonDocument.Parse(jsonStr);
                
                Assert.True(json.RootElement.TryGetProperty("answer", out var answerProp));
                Assert.True(json.RootElement.TryGetProperty("confidence", out var confidenceProp));
                
                var answer = answerProp.GetString();
                var confidence = confidenceProp.GetInt32();
                
                Assert.True(answer == "TRUE" || answer == "FALSE", $"Answer should be TRUE or FALSE, got: {answer}");
                Assert.True(confidence >= 0 && confidence <= 100, $"Confidence should be 0-100, got: {confidence}");
                
                // Since transcript mentions "Jacobin magazine", answer should be TRUE
                Assert.Equal("TRUE", answer);
            }
            else
            {
                // Fallback to simple text check
                var responseLower = response.ToLower().Trim();
                Assert.True(responseLower.Contains("true") || responseLower.Contains("false"), 
                    $"Response should contain TRUE or FALSE, but got: {response}");
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Fallback to simple text check if JSON parsing fails
            var responseLower = response.ToLower().Trim();
            Assert.True(responseLower.Contains("true") || responseLower.Contains("false"), 
                $"Response should contain TRUE or FALSE, but got: {response}");
        }
    }

    [Fact(Skip = "Integration test - requires model download")]
    public async Task PromptAsync_WithSimpleQuestion_ShouldRespond()
    {
        var service = new LanguageModelService(_mockLogger.Object, _configuration, _httpClient);
        
        var response = await service.PromptAsync("What is 2 + 2?");
        
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        Assert.True(response.Contains("4") || response.Contains("four"), 
            "Response should contain the correct answer");
    }

    [Fact]//(Skip = "Integration test - requires model download")]
    public async Task PromptAsync_WithFewShotBooleanQuestion_ShouldReturnStructuredResponse()
    {
        var service = new LanguageModelService(_mockLogger.Object, _configuration, _httpClient);
        
        var prompt = @"Question: Does this mention food?
Text: ""I ate pizza for lunch""
Answer: {""answer"": ""TRUE"", ""confidence"": 95}

Question: Does this mention food?
Text: ""The weather is nice today""
Answer: {""answer"": ""FALSE"", ""confidence"": 90}

Question: Does this mention politics?
Text: ""The prime minister spoke in parliament about housing""
Answer:";
        
        var response = await service.PromptAsync(prompt);
        
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        // Should contain JSON-like structure
        Assert.True(response.Contains("answer") && response.Contains("confidence"), 
            $"Response should contain structured answer, got: {response}");
    }
}