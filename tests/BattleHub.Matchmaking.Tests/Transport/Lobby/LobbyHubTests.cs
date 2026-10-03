using System.Security.Claims;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Transport.Lobby;
using BattleHub.Matchmaking.Tests.Support;
using Microsoft.AspNetCore.Authorization;

namespace BattleHub.Matchmaking.Tests.Transport.Lobby;

public class LobbyHubTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void OnlyHeartbeat_RequiresAuthorization()
    {
        var publicMethods = new[]
        {
            nameof(LobbyHub.JoinLobby),
            nameof(LobbyHub.JoinMatch),
            nameof(LobbyHub.LeaveMatch)
        };

        Assert.NotNull(typeof(LobbyHub).GetMethod(nameof(LobbyHub.Heartbeat))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());
        Assert.All(publicMethods, method => Assert.Empty(typeof(LobbyHub).GetMethod(method)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)));
        Assert.Empty(typeof(LobbyHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task JoinLobby_AddsConnectionToLobbyGroup()
    {
        var (hub, groups) = CreateHub();

        await hub.JoinLobby();

        Assert.Equal([("connection-1", "lobby")], groups.Added);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task JoinMatch_AddsConnectionToMatchGroup()
    {
        var (hub, groups) = CreateHub();

        await hub.JoinMatch("match-1");

        Assert.Equal([("connection-1", "match:match-1")], groups.Added);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task LeaveMatch_RemovesConnectionFromMatchGroup()
    {
        var (hub, groups) = CreateHub();

        await hub.LeaveMatch("match-1");

        Assert.Equal([("connection-1", "match:match-1")], groups.Removed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task JoinMatch_WithoutId_Throws()
    {
        var (hub, _) = CreateHub();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => hub.JoinMatch(" "));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task LeaveMatch_WithoutId_Throws()
    {
        var (hub, _) = CreateHub();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => hub.LeaveMatch(string.Empty));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Heartbeat_UsesNameIdentifierBeforeSub()
    {
        var user = CreateUser(
            new Claim("sub", "sub-user"),
            new Claim(ClaimTypes.NameIdentifier, "name-user"));
        var match = CreateMatch("name-user");
        var (hub, groups, clients, store) = CreateHub(user, match);

        await hub.Heartbeat("match-1");

        Assert.Equal(DatabaseNow, Assert.Single(store.Current!.Participants).LastHeartbeatAt);
        Assert.Empty(groups.Added);
        Assert.Empty(groups.Removed);
        Assert.Empty(clients.Proxy.Sends);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Heartbeat_UsesSubFallback()
    {
        var match = CreateMatch("sub-user");
        var (hub, _, _, store) = CreateHub(CreateUser(new Claim("sub", "sub-user")), match);

        await hub.Heartbeat("match-1");

        Assert.Equal(DatabaseNow, Assert.Single(store.Current!.Participants).LastHeartbeatAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Heartbeat_WithoutIdentity_IsRejected()
    {
        var (hub, _, _, store) = CreateHub(null, null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => hub.Heartbeat("match-1"));

        Assert.Equal(0, store.ReplaceCalls);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Heartbeat_WhenUserIsNotParticipant_ThrowsWithoutSideEffects()
    {
        var user = CreateUser(new Claim(ClaimTypes.NameIdentifier, "other-user"));
        var (hub, groups, clients, store) = CreateHub(user, CreateMatch("participant"));

        await Assert.ThrowsAsync<MatchParticipantNotFoundException>(() => hub.Heartbeat("match-1"));

        Assert.Equal(0, store.ReplaceCalls);
        Assert.Empty(groups.Added);
        Assert.Empty(groups.Removed);
        Assert.Empty(clients.Proxy.Sends);
    }

    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DatabaseNow = CreatedAt.AddMinutes(5);

    private static (LobbyHub Hub, RecordingGroupManager Groups) CreateHub()
    {
        var (hub, groups, _, _) = CreateHub(null, null);
        return (hub, groups);
    }

    private static (LobbyHub Hub, RecordingGroupManager Groups, RecordingHubClients Clients, HubMatchStore Store)
        CreateHub(ClaimsPrincipal? user, Match? match)
    {
        var groups = new RecordingGroupManager();
        var clients = new RecordingHubClients();
        var store = new HubMatchStore(DatabaseNow, match);
        var hub = new LobbyHub(new MatchService(store, new NoOpEventPublisher()))
        {
            Context = new TestHubCallerContext("connection-1", user),
            Groups = groups,
            Clients = clients
        };
        return (hub, groups, clients, store);
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    private static Match CreateMatch(string userId)
    {
        var match = new Match("match-1", "Friday match", "Chess", "owner-1", CreatedAt, 4);
        match.JoinParticipant(userId, CreatedAt);
        return match;
    }

    private sealed class HubMatchStore(DateTimeOffset databaseNow, Match? match) : IMatchStore
    {
        public Match? Current { get; private set; } = match is null ? null : Clone(match);
        public int ReplaceCalls { get; private set; }

        public Task<DateTimeOffset> GetDatabaseTimeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(databaseNow);

        public Task<StoredMatch?> GetAsync(string matchId, CancellationToken cancellationToken) =>
            Task.FromResult(Current is null ? null : new StoredMatch(Clone(Current), 0));

        public Task<IReadOnlyList<StoredMatch>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoredMatch>>([]);

        public Task InsertAsync(Match matchToInsert, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryReplaceAsync(
            Match replacement,
            long expectedRevision,
            MatchStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            ReplaceCalls++;
            Current = Clone(replacement);
            return Task.FromResult(true);
        }

        private static Match Clone(Match source) => Match.Rehydrate(
            source.Id,
            source.Title,
            source.GameType,
            source.CreatedBy,
            source.CreatedAt,
            source.LastActivityAt,
            source.MaxPlayers,
            source.Status,
            source.Participants.Select(participant => new MatchParticipant(
                participant.UserId, participant.JoinedAt, participant.LastHeartbeatAt)));
    }

    private sealed class NoOpEventPublisher : IMatchEventPublisher
    {
        public Task PublishAsync(string eventName, Match match, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Heartbeat must not publish events.");
    }
}
