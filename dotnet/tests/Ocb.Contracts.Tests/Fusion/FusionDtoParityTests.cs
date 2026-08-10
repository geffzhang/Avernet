using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ocb.Contracts;
using Ocb.Contracts.Fusion;

namespace Ocb.Contracts.Tests.Fusion;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class FusionDtoParityTests
{
    [Fact]
    public void FusionRequestDto_UsesSnakeCaseFieldNames()
    {
        var dto = new FusionRequestDto(
            Question: "test question",
            Participants: new List<string> { "bot-1" },
            DriverBotId: null,
            Mode: "agent",
            FusionMode: "consensus",
            Options: FuseOptionsDto.Default,
            Metadata: null,
            SessionId: null
        );

        var json = JsonSerializer.Serialize(dto, OcbJsonContext.Default.FusionRequestDto);

        Assert.Contains("\"driver_bot_id\":null", json);
        Assert.Contains("\"fusion_mode\":\"consensus\"", json);
        Assert.Contains("\"question\":\"test question\"", json);
        Assert.Contains("\"participants\"", json);
    }

    [Fact]
    public void FuseResponseDto_UsesSnakeCaseFieldNames()
    {
        var dto = new FuseResponseDto(
            GroupId: "grp-1",
            FusionId: "fuse-abc",
            Question: "q",
            DriverBotId: null,
            Perspectives: Array.Empty<PerspectiveResponseDto>(),
            Recommendation: null,
            PartialSuccess: false,
            Warnings: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            Timing: new TimingResponseDto(100, 20, 50, 10, 20),
            FusionMode: "consensus"
        );

        var json = JsonSerializer.Serialize(dto, OcbJsonContext.Default.FuseResponseDto);

        Assert.Contains("\"group_id\":\"grp-1\"", json);
        Assert.Contains("\"fusion_id\":\"fuse-abc\"", json);
        Assert.Contains("\"partial_success\":false", json);
    }

    [Fact]
    public void FuseOptionsDto_HasSensibleDefaults()
    {
        var defaults = FuseOptionsDto.Default;

        Assert.Equal(5, defaults.MaxParticipants);
        Assert.Equal(30_000, defaults.TimeoutMs);
        Assert.False(defaults.RequireConsensus);
        Assert.Equal(0.0f, defaults.MinConfidence);
    }

    [Fact]
    public void FusionRequestDto_AllFields_PreserveRoundtrip()
    {
        var dto = new FusionRequestDto(
            Question: "What is the weather?",
            Participants: new List<string> { "w1:default", "w2:expert" },
            DriverBotId: "driver-1",
            Mode: "agent",
            FusionMode: "agent",
            Options: new FuseOptionsDto(3, 60_000, true, 0.7f),
            Metadata: new FuseMetadataDto("api", new List<string> { "urgent" }, null),
            SessionId: "sess-xyz"
        );

        var json = JsonSerializer.Serialize(dto, OcbJsonContext.Default.FusionRequestDto);
        var restored = JsonSerializer.Deserialize(json, OcbJsonContext.Default.FusionRequestDto);

        Assert.NotNull(restored);
        Assert.Equal(dto.Question, restored!.Question);
        Assert.Equal(dto.Participants, restored.Participants);
        Assert.Equal(dto.DriverBotId, restored.DriverBotId);
        Assert.Equal(dto.FusionMode, restored.FusionMode);
        Assert.Equal(dto.SessionId, restored.SessionId);
    }
}
