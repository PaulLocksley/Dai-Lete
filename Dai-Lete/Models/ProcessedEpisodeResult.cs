namespace Dai_Lete.Models;

public class ProcessedEpisodeResult
{
    public int FileSize;
    public double? InitialLengthSeconds;
    public double? ProcessedLengthSeconds;

    public ProcessedEpisodeResult(int fileSize, TimeSpan? initialLength = null, TimeSpan? processedLength = null)
    {
        FileSize = fileSize;
        InitialLengthSeconds = initialLength?.TotalSeconds;
        ProcessedLengthSeconds = processedLength?.TotalSeconds;
    }
}
