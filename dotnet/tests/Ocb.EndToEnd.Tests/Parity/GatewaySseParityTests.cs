using System.Text.Json;

namespace Ocb.EndToEnd.Tests.Parity;

/// <summary>
/// SSE parity: ensure the SSE corpus is valid and event ordering
/// matches the Python baseline.
///
/// Full end-to-end SSE parity requires a running Gateway server;
/// these tests validate the corpus structure and loadability.
/// </summary>
public sealed class GatewaySseParityTests
{
    private static string SseCorpusPath =>
        Path.Combine(AppContext.BaseDirectory, "Parity", "gateway-sse-corpus.json");

    [Fact]
    public void SseCorpusFileExistsAndIsValidJson()
    {
        Assert.True(File.Exists(SseCorpusPath),
            $"SSE corpus file not found: {SseCorpusPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(SseCorpusPath));
        var root = doc.RootElement;

        Assert.Equal("ocb-sse-parity-v1", root.GetProperty("version").GetString());
        Assert.True(root.GetProperty("cases").GetArrayLength() > 0,
            "SSE corpus must contain at least one test case.");
    }

    [Fact]
    public void EachSseTestCaseHasRequiredFields()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SseCorpusPath));

        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            Assert.NotNull(c.GetProperty("id").GetString());
            Assert.NotNull(c.GetProperty("path").GetString());
            Assert.True(c.GetProperty("expectedEventTypes").GetArrayLength() > 0,
                $"SSE case '{c.GetProperty("id").GetString()}' must have expected events.");
        }
    }

    /// <summary>
    /// Verify the "chat-stream-basic" case exists — it is the baseline
    /// for SSE event-order parity.
    /// </summary>
    [Fact]
    public void SseCorpusIncludesChatStreamBasicCase()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SseCorpusPath));

        var hasCase = doc.RootElement.GetProperty("cases")
            .EnumerateArray()
            .Any(c => c.GetProperty("id").GetString() == "chat-stream-basic");

        Assert.True(hasCase,
            "SSE corpus must include the 'chat-stream-basic' parity case.");
    }

    /// <summary>
    /// The event types in each case must only use allowed values
    /// (message, end, error, heartbeat).
    /// </summary>
    [Fact]
    public void AllSseEventTypesAreAllowedValues()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
            { "message", "end", "error", "heartbeat" };

        using var doc = JsonDocument.Parse(File.ReadAllText(SseCorpusPath));

        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var caseId = c.GetProperty("id").GetString()!;
            foreach (var eventType in c.GetProperty("expectedEventTypes").EnumerateArray())
            {
                var type = eventType.GetString()!;
                Assert.True(allowed.Contains(type),
                    $"SSE case '{caseId}': event type '{type}' is not allowed. " +
                    $"Allowed: {string.Join(", ", allowed.Order())}");
            }
        }
    }
}
