using System.Text.Json;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Transport.Matches;

namespace BattleHub.Matchmaking.Tests.Transport.Matches;

public class MatchResponseMapperTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ToResponse_PreservesContractFieldsAndNormalizesUtc()
    {
        var createdAt = new DateTimeOffset(2026, 10, 2, 8, 30, 0, TimeSpan.FromHours(-6));
        var match = new Match("match-1", "Friday match", "Trivia", "owner-1", createdAt, 4);
        match.JoinParticipant("owner-1", createdAt);

        var response = match.ToResponse();

        Assert.Equal("match-1", response.Id);
        Assert.Equal("Friday match", response.Title);
        Assert.Equal("Trivia", response.GameType);
        Assert.Equal("owner-1", response.CreatedBy);
        Assert.Equal(createdAt.UtcDateTime, response.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, response.CreatedAt.Kind);
        Assert.Equal(1, response.CurrentPlayers);
        Assert.Equal(4, response.MaxPlayers);
        Assert.Equal("Waiting", response.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Response_SerializesStatusAsTextAndCreatedAtAsUtc()
    {
        var response = new MatchResponse(
            "match-1", "Friday match", "Trivia", "owner-1",
            new DateTime(2026, 10, 2, 14, 30, 0, DateTimeKind.Utc), 1, 4, "Waiting");

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var createdAt = document.RootElement.GetProperty("createdAt").GetString();

        Assert.Contains("\"status\":\"Waiting\"", json);
        Assert.DoesNotContain("\"status\":0", json);
        Assert.Contains("\"createdAt\":", json);
        Assert.EndsWith("Z", createdAt);
        Assert.False(createdAt!.EndsWith("+00:00", StringComparison.Ordinal));
        Assert.Equal("2026-10-02T14:30:00Z", createdAt);
    }
}
