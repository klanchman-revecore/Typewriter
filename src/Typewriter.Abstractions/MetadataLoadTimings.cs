using System.Collections.Concurrent;
using System.Globalization;

namespace Typewriter.Abstractions;

/// <summary>
/// Debug-only per-project breakdown of the metadata load phase. Reported through
/// the generation performance trace when <c>TYPEWRITER_TRACE_PERF</c> is enabled.
/// </summary>
public static class MetadataLoadTimings
{
    private static readonly ConcurrentQueue<string> Entries = new();

    public static void Record(string projectPath, int sourceFiles, int types, IReadOnlyList<(string Stage, TimeSpan Elapsed)> stages)
    {
        ArgumentNullException.ThrowIfNull(argument: stages);
        var total = TimeSpan.FromTicks(value: stages.Sum(selector: static stage => stage.Elapsed.Ticks));
        var parts = stages.Select(selector: static stage => string.Create(provider: CultureInfo.InvariantCulture, handler: $"{stage.Stage}={stage.Elapsed.TotalMilliseconds:F0}ms"));
        Entries.Enqueue(
            item: string.Create(
                provider: CultureInfo.InvariantCulture,
                handler: $"Metadata project ({Path.GetFileName(path: projectPath)}): files={sourceFiles} types={types} own={total.TotalMilliseconds:F0}ms [{string.Join(separator: ", ", values: parts)}]"));
    }

    public static IReadOnlyList<string> Drain()
    {
        var result = new List<string>();
        while (Entries.TryDequeue(result: out var entry))
        {
            result.Add(item: entry);
        }

        return result;
    }
}
