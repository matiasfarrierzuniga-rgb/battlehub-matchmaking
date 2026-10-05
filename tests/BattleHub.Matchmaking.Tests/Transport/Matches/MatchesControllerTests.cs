using System.Security.Claims;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Transport.Matches;
using BattleHub.Matchmaking.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BattleHub.Matchmaking.Tests.Transport.Matches;

public class MatchesControllerTests
{
    private const string OwnerId = "owner-1";

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Create_WithoutUser_ReturnsUnauthorized()
    {
        var (controller, _) = CreateController();

        var result = await controller.Create(new("Friday", "Trivia", 4), default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    [Trait("Category", "Unit")]
    public async Task Create_WithUserClaim_UsesIdentityAndReturnsCreated(string claimType)
    {
        var (controller, _) = CreateController(OwnerId, claimType);

        var result = await controller.Create(new("Friday", "Trivia", 4), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<MatchResponse>(created.Value);
        Assert.Equal(OwnerId, response.CreatedBy);
        Assert.Equal(nameof(MatchesController.Get), created.ActionName);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task List_ReturnsOk()
    {
        var (controller, store) = CreateController();
        store.Add(CreateMatch());

        var result = await controller.List(null, null, default);

        Assert.Single(Assert.IsType<MatchResponse[]>(Assert.IsType<OkObjectResult>(result.Result).Value));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task List_WithValidStatus_ReturnsOk()
    {
        var (controller, store) = CreateController();
        store.Add(CreateMatch());

        var result = await controller.List(null, "waiting", default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task List_WithInvalidStatus_ReturnsBadRequestProblem()
    {
        var (controller, _) = CreateController();

        var result = await controller.List(null, "invalid", default);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Get_Existing_ReturnsOk()
    {
        var (controller, store) = CreateController();
        store.Add(CreateMatch());

        var result = await controller.Get("match-1", default);

        Assert.IsType<MatchResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Theory]
    [InlineData("join")]
    [InlineData("leave")]
    [InlineData("start")]
    [InlineData("delete")]
    [Trait("Category", "Unit")]
    public async Task UserActions_WithoutUser_ReturnUnauthorized(string action)
    {
        var (controller, _) = CreateController();

        var result = action switch
        {
            "join" => await controller.Join("match-1", default),
            "leave" => await controller.Leave("match-1", default),
            "start" => await controller.Start("match-1", default),
            _ => await controller.Delete("match-1", default)
        };

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Join_Success_ReturnsOk()
    {
        var (controller, store) = CreateController("user-2");
        store.Add(CreateMatch());

        var result = await controller.Join("match-1", default);

        Assert.Equal(1, ResponseFrom(result).CurrentPlayers);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Leave_Success_ReturnsOk()
    {
        var (controller, store) = CreateController("user-2");
        var match = CreateMatch();
        match.JoinParticipant("user-2", store.Now);
        store.Add(match);

        var result = await controller.Leave("match-1", default);

        Assert.Equal(0, ResponseFrom(result).CurrentPlayers);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Start_Success_ReturnsOk()
    {
        var (controller, store) = CreateController(OwnerId);
        store.Add(CreateMatch());

        var result = await controller.Start("match-1", default);

        Assert.Equal("Started", ResponseFrom(result).Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Cancel_Success_ReturnsOk()
    {
        var (controller, store) = CreateController(OwnerId);
        store.Add(CreateMatch());

        var result = await controller.Delete("match-1", default);

        Assert.Equal("Cancelled", ResponseFrom(result).Status);
    }

    private static MatchResponse ResponseFrom(ActionResult<MatchResponse> result) =>
        Assert.IsType<MatchResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static (MatchesController Controller, TestMatchStore Store) CreateController(
        string? userId = null,
        string claimType = ClaimTypes.NameIdentifier)
    {
        var store = new TestMatchStore();
        var controller = new MatchesController(new MatchService(store, new TestEventPublisher()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        if (userId is not null)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(claimType, userId)], "Test"));
        }

        return (controller, store);
    }

    private static Match CreateMatch() =>
        new("match-1", "Friday", "Trivia", OwnerId,
            new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero), 4);
}
