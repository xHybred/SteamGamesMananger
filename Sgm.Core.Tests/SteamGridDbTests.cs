using System.Net;
using System.Net.Http;
using System.Text;
using Sgm.Core.Artwork;

namespace Sgm.Core.Tests;

public class SteamGridDbTests
{
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<string, (HttpStatusCode Status, string Body)> _route;

        public RoutingHandler(Func<string, (HttpStatusCode, string)> route) => _route = route;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var (status, body) = _route(url);
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private static SteamGridDbClient Client(Func<string, (HttpStatusCode, string)> route)
        => new("test-key", new HttpClient(new RoutingHandler(route)));

    [Fact]
    public void ValidateSucceedsOnSuccessBody()
    {
        var client = Client(_ => (HttpStatusCode.OK, "{\"success\":true,\"data\":[]}"));
        Assert.True(client.Validate());
    }

    [Fact]
    public void ValidateThrowsOnUnauthorized()
    {
        var client = Client(_ => (HttpStatusCode.Unauthorized, "{\"success\":false,\"errors\":[\"bad key\"]}"));
        var ex = Assert.Throws<SteamGridDbException>(() => client.Validate());
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public void ValidateThrowsOnRateLimit()
    {
        var client = Client(_ => (HttpStatusCode.TooManyRequests, "{}"));
        Assert.Throws<SteamGridDbException>(() => client.Validate());
    }

    [Fact]
    public void PickGamePrefersExactCaseInsensitiveName()
    {
        var body = "{\"success\":true,\"data\":[" +
                   "{\"id\":1,\"name\":\"Portal\"}," +
                   "{\"id\":2,\"name\":\"Portal 2\"}]}";
        var client = Client(_ => (HttpStatusCode.OK, body));

        var picked = client.PickGame("portal");
        Assert.NotNull(picked);
        Assert.Equal(1, picked!.Value.GetProperty("id").GetInt32());
    }

    [Fact]
    public void PickGameExactOnlyRejectsFuzzyMatch()
    {
        var body = "{\"success\":true,\"data\":[{\"id\":2,\"name\":\"Portal 2\"}]}";
        var client = Client(_ => (HttpStatusCode.OK, body));

        Assert.Null(client.PickGame("Portal", exactOnly: true));
        Assert.NotNull(client.PickGame("Portal", exactOnly: false));
    }

    [Fact]
    public void AssetsThrowOnFailureBody()
    {
        var client = Client(_ => (HttpStatusCode.OK, "{\"success\":false,\"errors\":[\"nope\"]}"));
        Assert.Throws<SteamGridDbException>(() => client.Assets(1, "grids"));
    }

    [Fact]
    public void AssetsIncludeDimensionQuery()
    {
        string? seen = null;
        var client = Client(url =>
        {
            seen = url;
            return (HttpStatusCode.OK, "{\"success\":true,\"data\":[]}");
        });
        client.Assets(42, "grids");
        Assert.Contains("/games/id/42/grids", seen);
        Assert.Contains("dimensions=600x900", seen);
    }
}
