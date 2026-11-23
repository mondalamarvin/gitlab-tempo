using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Tempo.Api.Auth;

/// <summary>
/// "Sign in with GitLab": an OAuth code flow (with PKCE) against one configured GitLab instance,
/// kept in an encrypted, HTTP-only cookie. The GitLab access token in that cookie is what the
/// API uses to read reports for the signed-in person, and it is refreshed before it expires.
/// </summary>
public static class GitLabSignIn
{
    public const string Scheme = "GitLab";
    public const string CallbackPath = "/signin-gitlab";
    public const string SignInRequiredTitle = "Sign in required";

    private const string RedirectUriItem = "tempo.redirect_uri";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    /// <summary>Registers sign-in when Tempo:Auth:ClientId is set. Returns whether it is on.</summary>
    private const string ReportScopeKey = "Tempo:Auth:ReportScope";

    /// <summary>Binds the Tempo settings, stopping the app on a ReportScope it doesn't know.</summary>
    private static TempoOptions BindOptions(IConfiguration configuration)
    {
        var raw = configuration[ReportScopeKey];
        string Invalid() => $"{ReportScopeKey} must be {nameof(ReportScope.OwnTime)} or {nameof(ReportScope.WholeGroup)} (got '{raw}').";

        TempoOptions options;
        try
        {
            options = configuration.GetSection(TempoOptions.SectionName).Get<TempoOptions>() ?? new();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(ReportScopeKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Invalid(), ex);
        }

        // The binder accepts any number for an enum, so "7" would otherwise slip through.
        if (!Enum.IsDefined(options.Auth.ReportScope))
            throw new InvalidOperationException(Invalid());
        return options;
    }

    public static bool AddGitLabSignIn(this WebApplicationBuilder builder)
    {
        var options = BindOptions(builder.Configuration);
        var auth = options.Auth;
        if (!auth.Enabled) return false;

        var instance = options.SignInInstance
            ?? throw new InvalidOperationException($"Tempo:Auth:InstanceId '{auth.InstanceId}' does not match any configured GitLab instance.");
        if (string.IsNullOrWhiteSpace(auth.ClientSecret))
            throw new InvalidOperationException("Tempo:Auth:ClientSecret is required when Tempo:Auth:ClientId is set.");

        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient(GitLabAccess.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("gitlab-tempo/1.0");
        });
        builder.Services.AddSingleton<GitLabAccess>();
        builder.Services.AddAuthorization();

        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // API calls without a session get a 401 (below), not a redirect to GitLab.
                o.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(o =>
            {
                o.Cookie.Name = "tempo.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromHours(auth.SessionHours);
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx => WriteProblem(ctx.HttpContext, 401, SignInRequiredTitle, "Sign in with GitLab to continue.");
                o.Events.OnRedirectToAccessDenied = ctx => WriteProblem(ctx.HttpContext, 403, "Forbidden", "You don't have access to this.");
                o.Events.OnValidatePrincipal = ctx => RefreshIfNeededAsync(ctx, instance, auth);
            })
            .AddOAuth(Scheme, "GitLab", o =>
            {
                o.ClientId = auth.ClientId!;
                o.ClientSecret = auth.ClientSecret!;
                o.CallbackPath = CallbackPath;
                o.AuthorizationEndpoint = $"{instance.BaseUrl}/oauth/authorize";
                o.TokenEndpoint = $"{instance.BaseUrl}/oauth/token";
                o.UserInformationEndpoint = $"{instance.BaseUrl}/api/v4/user";
                o.Scope.Add("read_api");
                o.UsePkce = true;
                o.SaveTokens = true;
                // GitLab sends the browser back with a top-level GET, so Lax is enough and works on plain http://localhost.
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events.OnCreatingTicket = async ctx =>
                {
                    var access = ctx.HttpContext.RequestServices.GetRequiredService<GitLabAccess>();
                    var token = ctx.AccessToken!;
                    var user = await access.GetUserAsync(instance, token, ctx.HttpContext.RequestAborted);
                    if (await access.CheckAccessAsync(instance, auth, token, user, ctx.HttpContext.RequestAborted) is { } reason)
                        throw new SignInDeniedException(reason);

                    ctx.Identity!.AddClaims(user.ToClaims());
                    var request = ctx.HttpContext.Request;
                    ctx.Properties.Items[RedirectUriItem] = $"{request.Scheme}://{request.Host}{request.PathBase}{CallbackPath}";
                };
                o.Events.OnRemoteFailure = ctx =>
                {
                    var message = ctx.Failure as SignInDeniedException is { } denied
                        ? denied.Message
                        : "Signing in with GitLab didn't work. Try again.";
                    if (ctx.Failure is not SignInDeniedException)
                        ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger(typeof(GitLabSignIn)).LogWarning(ctx.Failure, "GitLab sign-in failed");
                    ctx.Response.Redirect("/?signin_error=" + Uri.EscapeDataString(message));
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        return true;
    }

    public static void MapGitLabSignIn(this WebApplication app)
    {
        app.MapGet("/auth/login", (string? returnUrl) =>
            Results.Challenge(new AuthenticationProperties { RedirectUri = LocalUrlOrRoot(returnUrl) }, [Scheme]));

        app.MapPost("/auth/logout", async (HttpContext http, GitLabAccess access, IOptions<TempoOptions> o) =>
        {
            var token = await http.GetTokenAsync("access_token");
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (token is not null && o.Value.SignInInstance is { } instance)
                await access.RevokeAsync(instance, o.Value.Auth, token, http.RequestAborted);
            return Results.NoContent();
        });
    }

    /// <summary>What the UI needs to decide between the sign-in screen and the app. Always anonymous.</summary>
    public static object Session(HttpContext http, TempoOptions options)
    {
        if (options.SignInInstance is not { } instance)
            return new { authEnabled = false, signInInstance = (object?)null, user = (object?)null };

        var p = http.User;
        var user = p.Identity?.IsAuthenticated == true
            ? new
            {
                username = p.FindFirstValue(ClaimTypes.Name),
                name = p.FindFirstValue(SignedInUser.DisplayNameClaim) ?? p.FindFirstValue(ClaimTypes.Name),
                avatarUrl = p.FindFirstValue(SignedInUser.AvatarClaim),
            }
            : null;
        return new { authEnabled = true, signInInstance = new { id = instance.Id, name = instance.Name }, user };
    }

    /// <summary>Only same-site paths, so /auth/login can't be used as an open redirect.</summary>
    internal static string LocalUrlOrRoot(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
            ? url
            : "/";

    /// <summary>Refreshes the GitLab access token shortly before it expires, and re-checks access when it does.</summary>
    private static async Task RefreshIfNeededAsync(CookieValidatePrincipalContext ctx, GitLabInstanceOptions instance, AuthOptions auth)
    {
        var services = ctx.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        if (!DateTimeOffset.TryParse(ctx.Properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt)
            || expiresAt - now > RefreshMargin)
            return;

        var access = services.GetRequiredService<GitLabAccess>();
        var ct = ctx.HttpContext.RequestAborted;
        var refreshToken = ctx.Properties.GetTokenValue("refresh_token");
        ctx.Properties.Items.TryGetValue(RedirectUriItem, out var redirectUri);

        var fresh = refreshToken is null ? null : await access.RefreshAsync(instance, auth, refreshToken, redirectUri, ct);
        string? denial = "expired";
        if (fresh is not null && ctx.Principal is { } principal
            && long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var id))
        {
            try
            {
                // Access is re-checked on every refresh (about every 2 hours), so removing someone
                // from the allowed group or list locks them out without waiting for the cookie to expire.
                var user = await access.GetUserAsync(instance, fresh.AccessToken, ct);
                denial = user.Id == id ? await access.CheckAccessAsync(instance, auth, fresh.AccessToken, user, ct) : "different user";
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                denial = ex.Message;
            }
        }

        if (denial is not null || fresh is null)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        ctx.Properties.UpdateTokenValue("access_token", fresh.AccessToken);
        if (fresh.RefreshToken is not null) ctx.Properties.UpdateTokenValue("refresh_token", fresh.RefreshToken);
        if (fresh.ExpiresAt is { } exp) ctx.Properties.UpdateTokenValue("expires_at", exp.ToString("o", CultureInfo.InvariantCulture));
        ctx.ShouldRenew = true;
    }

    private static Task WriteProblem(HttpContext http, int status, string title, string detail) =>
        Results.Problem(detail, statusCode: status, title: title).ExecuteAsync(http);
}
