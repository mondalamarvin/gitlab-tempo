using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Api.Auth;

namespace Tempo.Api.Tests;

public class SignInEndpointTests
{
    private static WebApplicationFactory<Program> App(bool signIn) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        // Not "Development", so the developer's user secrets are not loaded into the tests.
        b.UseEnvironment("Testing");
        b.UseSetting("Tempo:Instances:0:Id", "main");
        b.UseSetting("Tempo:Instances:0:Name", "Main GitLab");
        b.UseSetting("Tempo:Instances:0:Url", "https://git.example.com");
        b.UseSetting("Tempo:Instances:1:Id", "other");
        b.UseSetting("Tempo:Instances:1:Url", "https://other.example.com");
        if (signIn)
        {
            b.UseSetting("Tempo:Auth:ClientId", "app-id");
            b.UseSetting("Tempo:Auth:ClientSecret", "app-secret");
        }
    });

    private static HttpClient Client(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Without_a_client_id_the_api_stays_open_and_session_says_so()
    {
        using var app = App(signIn: false);
        var http = Client(app);

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/config")).StatusCode);
        using var session = JsonDocument.Parse(await http.GetStringAsync("/api/session"));
        Assert.False(session.RootElement.GetProperty("authEnabled").GetBoolean());
    }

    [Fact]
    public async Task With_sign_in_on_the_api_needs_a_session()
    {
        using var app = App(signIn: true);
        var http = Client(app);

        foreach (var url in new[] { "/api/config", "/api/me", "/api/report?from=2026-11-16" })
        {
            var response = await http.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(GitLabSignIn.SignInRequiredTitle, body.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task Session_is_public_and_names_the_sign_in_instance()
    {
        using var app = App(signIn: true);
        using var session = JsonDocument.Parse(await Client(app).GetStringAsync("/api/session"));

        var root = session.RootElement;
        Assert.True(root.GetProperty("authEnabled").GetBoolean());
        Assert.Equal("main", root.GetProperty("signInInstance").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("user").ValueKind);
    }

    [Fact]
    public async Task The_web_app_itself_is_served_without_a_session()
    {
        using var app = App(signIn: true);
        // No wwwroot in tests, so the SPA fallback is a 404 rather than 200 — but never a 401.
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await Client(app).GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Login_redirects_to_gitlab_with_pkce()
    {
        using var app = App(signIn: true);
        var response = await Client(app).GetAsync("/auth/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https://git.example.com/oauth/authorize", location.GetLeftPart(UriPartial.Path));
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("app-id", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("read_api", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.EndsWith("/signin-gitlab", query["redirect_uri"]);
    }

    [Fact]
    public void Unknown_sign_in_instance_fails_at_startup()
    {
        using var app = App(signIn: true).WithWebHostBuilder(b => b.UseSetting("Tempo:Auth:InstanceId", "nope"));
        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("nope", ex.ToString());
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/reports?x=1", "/reports?x=1")]
    [InlineData("//evil.example.com", "/")]
    [InlineData("/\\evil.example.com", "/")]
    [InlineData("https://evil.example.com", "/")]
    public void Return_url_must_be_local(string? input, string expected)
    {
        Assert.Equal(expected, GitLabSignIn.LocalUrlOrRoot(input));
    }
}

public class AccessRuleTests
{
    private static readonly GitLabInstanceOptions Instance = new() { Id = "main", Url = "https://git.example.com" };
    private static readonly SignedInUser Alice = new(42, "alice", "Alice", "Alice@Example.com", null, "active");

    private static GitLabAccess Access(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new StubFactory(respond), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, NullLogger<GitLabAccess>.Instance);

    private static readonly GitLabAccess NoNetwork = Access(r => throw new InvalidOperationException($"Unexpected call to {r.RequestUri}"));

    [Fact]
    public async Task No_rules_lets_any_active_user_in()
    {
        Assert.Null(await NoNetwork.CheckAccessAsync(Instance, new AuthOptions(), "t", Alice, default));
    }

    [Fact]
    public async Task Blocked_users_are_refused_even_without_rules()
    {
        Assert.NotNull(await NoNetwork.CheckAccessAsync(Instance, new AuthOptions(), "t", Alice with { State = "blocked" }, default));
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("@ALICE")]
    [InlineData(" alice@example.com ")]
    public async Task Listed_usernames_and_emails_get_in(string entry)
    {
        var rules = new AuthOptions { AllowedUsers = [entry], AllowedGroups = ["team"] };
        Assert.Null(await NoNetwork.CheckAccessAsync(Instance, rules, "t", Alice, default));
    }

    [Fact]
    public async Task Members_of_an_allowed_group_get_in()
    {
        var calls = new List<string>();
        var access = Access(r =>
        {
            calls.Add(r.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer t", r.Headers.Authorization!.ToString());
            return r.RequestUri.AbsolutePath.Contains("acme%2Fmanagers")
                ? Json("""{ "id": 42, "state": "active" }""")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var rules = new AuthOptions { AllowedGroups = ["other", "/acme/managers/"] };

        Assert.Null(await access.CheckAccessAsync(Instance, rules, "t", Alice, default));
        Assert.Equal(
            ["https://git.example.com/api/v4/groups/other/members/all/42", "https://git.example.com/api/v4/groups/acme%2Fmanagers/members/all/42"],
            calls);
    }

    [Fact]
    public async Task Non_members_and_pending_members_are_refused()
    {
        var rules = new AuthOptions { AllowedGroups = ["team"], AllowedUsers = ["bob"] };

        Assert.NotNull(await Access(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).CheckAccessAsync(Instance, rules, "t", Alice, default));
        Assert.NotNull(await Access(_ => Json("""{ "id": 42, "state": "awaiting" }""")).CheckAccessAsync(Instance, rules, "t", Alice, default));
    }

    [Fact]
    public async Task A_gitlab_error_during_the_group_check_is_not_treated_as_allowed()
    {
        var rules = new AuthOptions { AllowedGroups = ["team"] };
        var access = Access(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<InvalidOperationException>(() => access.CheckAccessAsync(Instance, rules, "t", Alice, default));
    }

    [Fact]
    public async Task Relative_avatars_are_made_absolute()
    {
        var access = Access(_ => Json("""{ "id": 7, "username": "bob", "name": "Bob", "avatar_url": "/uploads/b.png", "state": "active" }"""));
        var user = await access.GetUserAsync(Instance, "t", default);
        Assert.Equal(new SignedInUser(7, "bob", "Bob", null, "https://git.example.com/uploads/b.png", "active"), user);
    }

    [Fact]
    public async Task Concurrent_refreshes_of_the_same_token_share_one_call()
    {
        var calls = 0;
        var access = Access(_ =>
        {
            Interlocked.Increment(ref calls);
            Thread.Sleep(50);
            return Json("""{ "access_token": "new", "refresh_token": "r2", "expires_in": 7200 }""");
        });
        var auth = new AuthOptions { ClientId = "id", ClientSecret = "s" };

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => access.RefreshAsync(Instance, auth, "r1", null, default)));

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal("new", r!.AccessToken));
    }

    [Fact]
    public async Task A_refused_refresh_returns_null()
    {
        var access = Access(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        Assert.Null(await access.RefreshAsync(Instance, new AuthOptions { ClientId = "id" }, "r1", null, default));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(respond));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.Run(() => respond(request), ct);
    }
}
