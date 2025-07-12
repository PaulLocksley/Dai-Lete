using LLama.Common;
using LLama;
using LLama.Sampling;

namespace Dai_Lete.Services;

public class LanguageModelService
{
    private readonly ILogger<LanguageModelService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;
    private readonly SemaphoreSlim _modelLock = new(1, 1);

    public LanguageModelService(ILogger<LanguageModelService> logger, IConfiguration configuration, HttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> PromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        await _modelLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureModelLoadedAsync(cancellationToken);
            
            if (_executor == null)
                throw new InvalidOperationException("Model executor not initialized");

            var inferParams = new InferenceParams()
            {
                MaxTokens = 200
            };

            var response = new List<string>();
            await foreach (var token in _executor.InferAsync(prompt, inferParams, cancellationToken))
            {
                response.Add(token);
                if (response.Count > 150) // Allow more tokens for quality but still prevent runaway
                    break;
            }

            return string.Join("", response).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response for prompt");
            throw;
        }
        finally
        {
            _modelLock.Release();
        }
    }

    private async Task EnsureModelLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_model != null && _context != null && _executor != null)
            return;

        var modelPath = await GetModelPathAsync(cancellationToken);
        
        _logger.LogInformation("Loading language model from {ModelPath}", modelPath);

        var parameters = new ModelParams(modelPath)
        {
            ContextSize = 1024, // Balanced for Pi hardware and functionality
            GpuLayerCount = 0 // CPU only for now
        };

        _model = LLamaWeights.LoadFromFile(parameters);
        _context = _model.CreateContext(parameters);
        _executor = new InteractiveExecutor(_context);

        _logger.LogInformation("Language model loaded successfully");
    }

    private async Task<string> GetModelPathAsync(CancellationToken cancellationToken = default)
    {
        var modelUrl = _configuration["LanguageModel:ModelUrl"] ?? 
                      "https://huggingface.co/TheBloke/phi-2-GGUF/resolve/main/phi-2.Q5_K_M.gguf";
        var modelDir = _configuration["LanguageModel:ModelPath"] ?? "Podcasts/models";
        
        Directory.CreateDirectory(modelDir);
        
        var fileName = Path.GetFileName(new Uri(modelUrl).LocalPath);
        var localPath = Path.Combine(modelDir, fileName);

        if (File.Exists(localPath))
        {
            _logger.LogInformation("Using cached model at {LocalPath}", localPath);
            return localPath;
        }

        _logger.LogInformation("Downloading model from {ModelUrl} to {LocalPath}", modelUrl, localPath);
        
        using var response = await _httpClient.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var downloadedBytes = 0L;

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[8192];
        int bytesRead;
        
        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            downloadedBytes += bytesRead;
            
            if (totalBytes > 0 && downloadedBytes % (1024 * 1024 * 10) == 0) // Log every 10MB
            {
                var progress = (double)downloadedBytes / totalBytes * 100;
                _logger.LogInformation("Download progress: {Progress:F1}% ({DownloadedMB:F1}/{TotalMB:F1} MB)", 
                    progress, downloadedBytes / 1024.0 / 1024.0, totalBytes / 1024.0 / 1024.0);
            }
        }

        _logger.LogInformation("Model downloaded successfully to {LocalPath}", localPath);
        return localPath;
    }

    public void Dispose()
    {
        _context?.Dispose();
        _model?.Dispose();
        _modelLock?.Dispose();
    }
}