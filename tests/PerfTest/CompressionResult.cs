using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerfTest;

[JsonSerializable(typeof(CompressionResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default)]
public partial class CompressionResultJsonContext : JsonSerializerContext
{
}

public class CompressionResult
{
    public string Implementation { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public int Level { get; set; }
    public long OriginalSizeBytes { get; set; }
    public long CompressedSizeBytes { get; set; }
    public double CompressionRatio => OriginalSizeBytes > 0 ? (double)CompressedSizeBytes / OriginalSizeBytes : 0;
    public double ElapsedMilliseconds { get; set; }

    public static void PrintResult(CompressionResult result)
    {
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            TypeInfoResolver = CompressionResultJsonContext.Default
        };
        var json = JsonSerializer.Serialize(result, options);
        Console.WriteLine(json);
    }

    public static void PrintResultCompact(CompressionResult result)
    {
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = false,
            TypeInfoResolver = CompressionResultJsonContext.Default
        };
        var json = JsonSerializer.Serialize(result, options);
        Console.WriteLine(json);
    }
}
