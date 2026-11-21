using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Auth;
using Tempo.Api.GitLab;

namespace Tempo.Api.Tests;

/// <summary>The whole "Sign in with GitLab" round trip, against a fake GitLab.</summary>
public class SignInFlowTests
{
    /// <summary>Fake GitLab: OAuth token endpoint, REST user + membership, GraphQL currentUser.</summary>
    private sealed class FakeGitLab : HttpMessageHandler
    {
        public bool IsTeamMember { get; set; } = true;
        public bool AllowRefresh { get; set; } = true;
        public int Refreshes { get; private set; }
        public List<string> GraphQlTokens { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var auth = r.Headers.Authorization?.Parameter;
            var body = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
            var validToken = auth is "user-token" or "refreshed-token";
            return path switch
            {
                "/oauth/token" when body.Contains("code_verifier=") =>
                    Json("""{ "access_token": "user-token", "refresh_token": "rt", "expires_in": 7200, "token_type": "Bearer" }"""),
                "/oauth/token" when body.Contains("grant_type=refresh_token") && body.Contains("refresh_token=rt&") && AllowRefresh =>
                    Refreshed(),
                "/api/v4/user" when validToken =>
                    Json("""{ "id": 42, "username": "alice", "name": "Alice", "email": "alice@example.com", "state": "active" }"""),
                "/api/v4/groups/team/members/all/42" when validToken =>
                    IsTeamMember ? Json("""{ "id": 42, "state": "active" }""") : new HttpResponseMessage(HttpStatusCode.NotFound),
                "/api/graphql" => GraphQl(auth),
                _ => new HttpResponseMessage(HttpStatusCode.BadRequest),
            };
        }

        private HttpResponseMessage Refreshed()
        {
            Refreshes++;
            return Json("""{ "access_token": "refreshed-token", "refresh_token": "rt2", "expires_in": 7200, "token_type": "Bearer" }""");
        }

        private HttpResponseMessage GraphQl(string? token)
        {
            GraphQlTokens.Add(token ?? "");
            return Json("""{ "data": { "currentUser": { "username": "alice", "name": "Alice", "avatarUrl": null } } }""");
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    /// <summary>A clock the test can move forward.</summary>
    private sealed class TestClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
    }

    private static (WebApplicationFactory<Program> App, FakeGitLab GitLab) Create(TestClock? clock = null)
    {
        var gitLab = new FakeGitLab();
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Tempo:Instances:0:Id", "main");
            b.UseSetting("Tempo:Instances:0:Url", "https://git.example.com");
            b.UseSetting("Tempo:Instances:0:ServerToken", "server-token");
            // A second instance that is not used for sign-in.
            b.UseSetting("Tempo:Instances:1:Id", "other");
            b.UseSetting("Tempo:Instances:1:Url", "https://other.example.com");
            b.UseSetting("Tempo:Instances:1:ServerToken", "other-token");
            b.UseSetting("Tempo:Auth:ClientId", "app-id");
            b.UseSetting("Tempo:Auth:ClientSecret", "app-secret");
            b.UseSetting("Tempo:Auth:AllowedGroups:0", "team");
            b.ConfigureServices(s =>
            {
                s.PostConfigure<OAuthOptions>(GitLabSignIn.Scheme, o => o.Backchannel = new HttpClient(gitLab, disposeHandler: false));
                s.AddHttpClient(GitLabAccess.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => gitLab);
                s.AddHttpClient<GitLabClient>().ConfigurePrimaryHttpMessageHandler(() => gitLab);
                if (clock is not null) s.AddSingleton<TimeProvider>(clock);
            });
        });
        return (app, gitLab);
    }

    /// <summary>Starts a login, then plays GitLab sending the browser back with a code.</summary>
    private static async Task<HttpResponseMessage> SignInAsync(HttpClient http)
    {
        var challenge = await http.GetAsync("/auth/login?returnUrl=%2Freports");
        var state = HttpUtility.ParseQueryString(challenge.Headers.Location!.Query)["state"];
        return await http.GetAsync($"/signin-gitlab?code=abc&state={Uri.EscapeDataString(state!)}");
    }

    [Fact]
    public async Task An_allowed_user_signs_in_and_the_api_uses_their_own_gitlab_token()
    {
        var (app, gitLab) = Create();
        using var _ = app;
        var http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var callback = await SignInAsync(http);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/reports", callback.Headers.Location!.OriginalString);

        using var session = JsonDocument.Parse(await http.GetStringAsync("/api/session"));
        Assert.Equal("alice", session.RootElement.GetProperty("user").GetProperty("username").GetString());

        var me = await http.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        // The signed-in user's token, not the server token, and a pasted header token is ignored too.
        var withHeader = new HttpRequestMessage(HttpMethod.Get, "/api/me") { Headers = { { "X-GitLab-Token", "someone-else" } } };
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(withHeader)).StatusCode);
        Assert.Equal(["user-token", "user-token"], gitLab.GraphQlTokens);

        using var config = JsonDocument.Parse(await http.GetStringAsync("/api/config"));
        Assert.True(config.RootElement.GetProperty("instances")[0].GetProperty("usesSignIn").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task With_sign_in_on_only_the_sign_in_instance_is_offered_and_usable()
    {
        var (app, gitLab) = Create();
        using var _ = app;
        var http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(http);

        // The UI gets one instance, so it shows no instance picker and no token field.
        using var config = JsonDocument.Parse(await http.GetStringAsync("/api/config"));
        var instances = config.RootElement.GetProperty("instances");
        Assert.Equal(1, instances.GetArrayLength());
        Assert.Equal("main", instances[0].GetProperty("id").GetString());
        Assert.True(instances[0].GetProperty("usesSignIn").GetBoolean());

        // The other instance can't be reached, not even with a pasted token.
        var other = new HttpRequestMessage(HttpMethod.Get, "/api/me")
        {
            Headers = { { "X-GitLab-Instance", "other" }, { "X-GitLab-Token", "pasted" } },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(other)).StatusCode);
        Assert.Empty(gitLab.GraphQlTokens);
    }

    [Fact]
    public async Task A_user_outside_the_allowed_group_is_sent_back_with_a_reason_and_no_session()
    {
        var (app, gitLab) = Create();
        using var _ = app;
        gitLab.IsTeamMember = false;
        var http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var callback = await SignInAsync(http);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var error = HttpUtility.ParseQueryString(callback.Headers.Location!.OriginalString.Split('?')[1])["signin_error"];
        Assert.Contains("doesn't have access", error);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/me")).StatusCode);
        Assert.Empty(gitLab.GraphQlTokens);
    }

    [Fact]
    public async Task An_expiring_gitlab_token_is_refreshed_and_the_new_one_is_used()
    {
        var clock = new TestClock();
        var (app, gitLab) = Create(clock);
        using var _ = app;
        var http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(http);

        clock.Offset = TimeSpan.FromHours(2); // GitLab access tokens last 2 hours
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/me")).StatusCode);
        // The renewed cookie carries the new token, so the next call doesn't refresh again.
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/me")).StatusCode);

        Assert.Equal(1, gitLab.Refreshes);
        Assert.Equal(["refreshed-token", "refreshed-token"], gitLab.GraphQlTokens);
    }

    [Fact]
    public async Task Someone_removed_from_the_group_is_signed_out_at_the_next_refresh()
    {
        var clock = new TestClock();
        var (app, gitLab) = Create(clock);
        using var _ = app;
        var http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(http);

        gitLab.IsTeamMember = false;
        clock.Offset = TimeSpan.FromHours(2);

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/me")).StatusCode);
        Assert.Empty(gitLab.GraphQlTokens);
    }

    [Fact]
    public async Task A_refused_refresh_ends_the_session()
    {
        var clock = new TestClock();
        var (app, gitLab) = Create(clock);
        using var _ = app;
        var http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(http);

        gitLab.AllowRefresh = false;
        clock.Offset = TimeSpan.FromHours(2);

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/me")).StatusCode);
        using var session = JsonDocument.Parse(await http.GetStringAsync("/api/session"));
        Assert.Equal(JsonValueKind.Null, session.RootElement.GetProperty("user").ValueKind);
    }
}
