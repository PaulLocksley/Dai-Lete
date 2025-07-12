using System.Text.RegularExpressions;

namespace Dai_Lete.Tests.Utilities;

public static class TranscriptAccuracyHelper
{
    public static ExpectedTranscript ParseExpectedTranscript(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Expected transcript file not found: {filePath}");

        var lines = File.ReadAllLines(filePath);
        var segments = new List<ExpectedTranscriptSegment>();
        
        for (int i = 0; i < lines.Length; i += 2)
        {
            if (i + 1 >= lines.Length) break;
            
            var text = lines[i].Trim();
            var timestampLine = lines[i + 1].Trim();
            
            if (string.IsNullOrEmpty(text) || text.StartsWith("[")) continue;
            
            if (TimeSpan.TryParse($"00:{timestampLine}", out var timestamp))
            {
                segments.Add(new ExpectedTranscriptSegment
                {
                    Text = text,
                    Timestamp = timestamp
                });
            }
        }

        return new ExpectedTranscript { Segments = segments };
    }

    public static string ExtractTextOnly(ExpectedTranscript transcript)
    {
        return string.Join(" ", transcript.Segments.Select(s => s.Text));
    }

    public static double CalculateWordAccuracy(string expected, string actual)
    {
        var expectedWords = NormalizeText(expected).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var actualWords = NormalizeText(actual).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var matchingWords = 0;
        var totalWords = Math.Max(expectedWords.Length, actualWords.Length);
        
        if (totalWords == 0) return 1.0;
        
        // Simple word matching - could be enhanced with edit distance
        var expectedSet = new HashSet<string>(expectedWords);
        var actualSet = new HashSet<string>(actualWords);
        
        matchingWords = expectedSet.Intersect(actualSet).Count();
        
        return (double)matchingWords / totalWords;
    }

    public static double CalculateCharacterAccuracy(string expected, string actual)
    {
        var normalizedExpected = NormalizeText(expected);
        var normalizedActual = NormalizeText(actual);
        
        var distance = LevenshteinDistance(normalizedExpected, normalizedActual);
        var maxLength = Math.Max(normalizedExpected.Length, normalizedActual.Length);
        
        if (maxLength == 0) return 1.0;
        
        return 1.0 - ((double)distance / maxLength);
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        
        // Convert to lowercase, remove extra whitespace and punctuation
        text = text.ToLowerInvariant();
        text = Regex.Replace(text, @"[^\w\s]", "");
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    private static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target)) return source.Length;

        var matrix = new int[source.Length + 1, target.Length + 1];

        for (int i = 0; i <= source.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= target.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= source.Length; i++)
        {
            for (int j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[source.Length, target.Length];
    }
}

public class ExpectedTranscript
{
    public List<ExpectedTranscriptSegment> Segments { get; set; } = new();
}

public class ExpectedTranscriptSegment
{
    public string Text { get; set; } = string.Empty;
    public TimeSpan Timestamp { get; set; }
}