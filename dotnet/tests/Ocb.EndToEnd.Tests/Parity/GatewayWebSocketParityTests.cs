using System.Text.Json;

namespace Ocb.EndToEnd.Tests.Parity;

/// <summary>
/// WebSocket parity: ensure the WS corpus is valid and the
/// .NET Gateway implements the required frame contracts.
///
/// Full end-to-end WS parity requires a running Gateway server;
/// these tests validate the corpus structure and loadability.
/// </summary>
public sealed class GatewayWebSocketParityTests
{
    private static string WsCorpusPath =>
        Path.Combine(AppContext.BaseDirectory, "Parity", "gateway-ws-corpus.json");

    [Fact]
    public void WsCorpusFileExistsAndIsValidJson()
    {
        Assert.True(File.Exists(WsCorpusPath),
            $"WS corpus file not found: {WsCorpusPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(WsCorpusPath));
        var root = doc.RootElement;

        Assert.Equal("ocb-ws-parity-v1", root.GetProperty("version").GetString());
        Assert.True(root.GetProperty("cases").GetArrayLength() > 0,
            "WS corpus must contain at least one test case.");
    }

    [Fact]
    public void EachWsTestCaseHasRequiredFields()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(WsCorpusPath));

        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            Assert.NotNull(c.GetProperty("id").GetString());
            Assert.NotNull(c.GetProperty("path").GetString());
            Assert.True(c.GetProperty("expectedFrameTypes").GetArrayLength() > 0,
                $"WS case '{c.GetProperty("id").GetString()}' must have expected frames.");
        }
    }

    /// <summary>
    /// Verify the "bots-messages-basic" case exists — it is the baseline
    /// for WS frame-order parity.
    /// </summary>
    [Fact]
    public void WsCorpusIncludesBotsMessagesBasicCase()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(WsCorpusPath));

        var hasCase = doc.RootElement.GetProperty("cases")
            .EnumerateArray()
            .Any(c => c.GetProperty("id").GetString() == "bots-messages-basic");

        Assert.True(hasCase,
            "WS corpus must include the 'bots-messages-basic' parity case.");
    }
}
