using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Tempo.Api.Auth;

/// <summary>The GitLab account behind a signed-in session.</summary>
public sealed record SignedInUser(long Id, string Username, string Name, string? Email, string? AvatarUrl, string? State)
{
    public const string AvatarClaim = "avatar_url";
    public const string DisplayNameClaim = "display_name";

    public IEnumerable<Claim> ToClaims()
    {
        yield return new Claim(ClaimTypes.NameIdentifier, Id.ToString(CultureInfo.InvariantCulture));
        yield return new Claim(ClaimTypes.Name, Username);
        yield return new Claim(DisplayNameClaim, Name);
        if (!string.IsNullOrEmpty(Email)) yield return new Claim(ClaimTypes.Email, Email);
        if (!string.IsNullOrEmpty(AvatarUrl)) yield return new Claim(AvatarClaim, AvatarUrl);
    }
}

/// <summary>Tokens returned by GitLab's /oauth/token endpoint.</summary>
public sealed record TokenSet(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);

/// <summary>Thrown during sign-in when the GitLab account is not allowed to use the app.</summary>
public sealed class SignInDeniedException(string message) : Exception(message);

/// <summary>
/// GitLab REST calls used by sign-in: who the user is, whether they may use the app,
/// refreshing the short-lived OAuth access token, and revoking it on sign-out.
/// </summary>
public sealed class GitLabAccess(IHttpClientFactory httpFactory, IMemoryCache cache, TimeProvider time, ILogger<GitLabAccess> logger)
{
    public const string HttpClientName = "gitlab-auth";

    public async Task<SignedInUser> GetUserAsync(GitLabInstanceOptions instance, string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"{instance.BaseUrl}/api/v4/user", accessToken, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitLab returned HTTP {(int)response.StatusCode} for /api/v4/user.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var u = doc.RootElement;
        var username = Str(u, "username") ?? throw new InvalidOperationException("GitLab returned a user without a username.");
        var avatar = Str(u, "avatar_url");
        if (avatar is not null && avatar.StartsWith('/')) avatar = instance.BaseUrl + avatar;

        return new SignedInUser(
            u.GetProperty("id").GetInt64(),
            username,
            Str(u, "name") ?? username,
            Str(u, "email"),
            avatar,
            Str(u, "state"));
    }

    /// <summary>Null when the user may use the app, otherwise the reason they may not.</summary>
    public async Task<string?> CheckAccessAsync(GitLabInstanceOptions instance, AuthOptions rules, string accessToken, SignedInUser user, CancellationToken ct)
    {
        const string denied = "Your GitLab account doesn't have access to gitlab-tempo. Ask the person who runs it to add you.";

        if (user.State is { } state && !state.Equals("active", StringComparison.OrdinalIgnoreCase))
            return "Your GitLab account is not active.";
        if (rules.AllowsEveryone || IsListedUser(rules, user)) return null;

        foreach (var group in rules.AllowedGroups.Select(g => g.Trim().Trim('/')).Where(g => g.Length > 0))
        {
            if (await IsGroupMemberAsync(instance, accessToken, group, user.Id, ct)) return null;
        }

        logger.LogInformation("Sign-in denied for {Username}: not in AllowedUsers or AllowedGroups", user.Username);
        return denied;
    }

    /// <summary>True when the username or email is in <see cref="AuthOptions.AllowedUsers"/>.</summary>
    internal static bool IsListedUser(AuthOptions rules, SignedInUser user) =>
        rules.AllowedUsers
            .Select(a => a.Trim().TrimStart('@'))
            .Where(a => a.Length > 0)
            .Any(a => a.Equals(user.Username, StringComparison.OrdinalIgnoreCase)
                      || (user.Email is { } email && a.Equals(email, StringComparison.OrdinalIgnoreCase)));

    private async Task<bool> IsGroupMemberAsync(GitLabInstanceOptions instance, string accessToken, string groupPath, long userId, CancellationToken ct)
    {
        // members/all includes inherited members. A 404 means "not a member" or "can't see the group".
        var url = $"{instance.BaseUrl}/api/v4/groups/{Uri.EscapeDataString(groupPath)}/members/all/{userId}";
        using var response = await SendAsync(HttpMethod.Get, url, accessToken, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return false;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitLab returned HTTP {(int)response.StatusCode} checking membership of '{groupPath}'.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        // Members waiting for approval ("awaiting") or blocked don't count.
        return Str(doc.RootElement, "state") is not { } state || state.Equals("active", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Swaps a refresh token for new tokens. GitLab rotates refresh tokens on every use, so when
    /// several requests with the same cookie arrive together they share one refresh call.
    /// Returns null when GitLab refuses (revoked, expired, app deleted).
    /// </summary>
    public Task<TokenSet?> RefreshAsync(GitLabInstanceOptions instance, AuthOptions auth, string refreshToken, string? redirectUri, CancellationToken ct)
    {
        var lazy = cache.GetOrCreate($"gitlab-refresh:{refreshToken}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return new Lazy<Task<TokenSet?>>(() => RefreshCoreAsync(instance, auth, refreshToken, redirectUri));
        })!;
        return lazy.Value.WaitAsync(ct);
    }

    private async Task<TokenSet?> RefreshCoreAsync(GitLabInstanceOptions instance, AuthOptions auth, string refreshToken, string? redirectUri)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = auth.ClientId!,
            ["client_secret"] = auth.ClientSecret ?? "",
        };
        if (!string.IsNullOrEmpty(redirectUri)) form["redirect_uri"] = redirectUri;

        try
        {
            using var response = await httpFactory.CreateClient(HttpClientName)
                .PostAsync($"{instance.BaseUrl}/oauth/token", new FormUrlEncodedContent(form));
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("GitLab refused to refresh a session token (HTTP {Status})", (int)response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (Str(root, "access_token") is not { } access) return null;
            DateTimeOffset? expiresAt = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds)
                ? time.GetUtcNow().AddSeconds(seconds)
                : null;
            return new TokenSet(access, Str(root, "refresh_token"), expiresAt);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not reach GitLab to refresh a session token");
            return null;
        }
    }

    /// <summary>Best effort: tells GitLab to forget the token. Sign-out succeeds either way.</summary>
    public async Task RevokeAsync(GitLabInstanceOptions instance, AuthOptions auth, string token, CancellationToken ct)
    {
        try
        {
            using var response = await httpFactory.CreateClient(HttpClientName).PostAsync(
                $"{instance.BaseUrl}/oauth/revoke",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = token,
                    ["client_id"] = auth.ClientId!,
                    ["client_secret"] = auth.ClientSecret ?? "",
                }),
                ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogInformation(ex, "Could not revoke the GitLab token on sign-out");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
    }

    private static string? Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
