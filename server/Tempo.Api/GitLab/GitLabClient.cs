using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tempo.Api.GitLab;

/// <summary>
/// Thin GitLab GraphQL client. Every call takes the caller's token so the server never
/// stores user credentials.
/// </summary>
public sealed class GitLabClient(HttpClient http, ILogger<GitLabClient> logger)
{
    private const int PageSize = 100;
    private const int MaxPages = 500; // 50k entries: a safety stop, not a real limit

    private const string CurrentUserQuery = """
        query { currentUser { username name avatarUrl } }
        """;

    private const string TimelogsQuery = """
        query Timelogs($fullPath: ID!, $start: Time, $end: Time, $after: String, $first: Int) {
          group(fullPath: $fullPath) {
            name
            fullPath
            webUrl
            timelogs(startTime: $start, endTime: $end, first: $first, after: $after) {
              pageInfo { hasNextPage endCursor }
              nodes {
                spentAt
                timeSpent
                summary
                user { username name avatarUrl }
                issue { iid title webUrl }
                mergeRequest { iid }
                project { fullPath }
              }
            }
          }
        }
        """;

    private const string MembersQuery = """
        query Members($fullPath: ID!, $after: String, $first: Int) {
          group(fullPath: $fullPath) {
            groupMembers(relations: [DIRECT, INHERITED], first: $first, after: $after) {
              pageInfo { hasNextPage endCursor }
              nodes { user { username name avatarUrl bot state } }
            }
          }
        }
        """;

    public async Task<GitLabUser> GetCurrentUserAsync(GitLabInstanceOptions instance, string token, CancellationToken ct)
    {
        var data = await QueryAsync(instance, token, CurrentUserQuery, new { }, ct);
        if (!data.TryGetProperty("currentUser", out var u) || u.ValueKind != JsonValueKind.Object)
            throw new GitLabException(401, "GitLab did not accept the access token.");
        return WithAbsoluteAvatar(ReadUser(u)!, instance);
    }

    /// <summary>GitLab returns uploaded avatars as paths relative to the instance; make them absolute.</summary>
    internal static GitLabUser WithAbsoluteAvatar(GitLabUser user, GitLabInstanceOptions instance) =>
        user.AvatarUrl is { } url && url.StartsWith('/')
            ? user with { AvatarUrl = instance.BaseUrl + url }
            : user;

    public async Task<GroupTimelogs> GetGroupTimelogsAsync(
        GitLabInstanceOptions instance, string token, string groupPath, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var logs = new List<Timelog>();
        GroupInfo? group = null;
        string? after = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var data = await QueryAsync(instance, token, TimelogsQuery, new
            {
                fullPath = groupPath,
                start = start.ToString("o", CultureInfo.InvariantCulture),
                end = end.ToString("o", CultureInfo.InvariantCulture),
                after,
                first = PageSize,
            }, ct);

            if (!data.TryGetProperty("group", out var g) || g.ValueKind != JsonValueKind.Object)
                throw new GitLabException(404, $"Group '{groupPath}' was not found, or your token cannot see it.");

            group ??= new GroupInfo(Str(g, "fullPath") ?? groupPath, Str(g, "name") ?? groupPath, Str(g, "webUrl"));

            var conn = g.GetProperty("timelogs");
            foreach (var node in conn.GetProperty("nodes").EnumerateArray())
            {
                var log = ReadTimelog(node);
                if (log is not null) logs.Add(log);
            }

            (var hasNext, after) = ReadPageInfo(conn);
            if (!hasNext) return new GroupTimelogs(group, logs);
        }

        logger.LogWarning("Stopped paging timelogs for {Group} after {Pages} pages", groupPath, MaxPages);
        return new GroupTimelogs(group!, logs);
    }

    /// <summary>Active, human members of the group (direct and inherited).</summary>
    public async Task<IReadOnlyList<GitLabUser>> GetGroupMembersAsync(GitLabInstanceOptions instance, string token, string groupPath, CancellationToken ct)
    {
        var users = new Dictionary<string, GitLabUser>(StringComparer.OrdinalIgnoreCase);
        string? after = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var data = await QueryAsync(instance, token, MembersQuery, new { fullPath = groupPath, after, first = PageSize }, ct);
            if (!data.TryGetProperty("group", out var g) || g.ValueKind != JsonValueKind.Object) break;

            var conn = g.GetProperty("groupMembers");
            foreach (var node in conn.GetProperty("nodes").EnumerateArray())
            {
                if (!node.TryGetProperty("user", out var u) || u.ValueKind != JsonValueKind.Object) continue;
                if (u.TryGetProperty("bot", out var bot) && bot.ValueKind == JsonValueKind.True) continue;
                if (Str(u, "state") is { } state && !state.Equals("active", StringComparison.OrdinalIgnoreCase)) continue;
                var user = ReadUser(u);
                if (user is not null) users.TryAdd(user.Username, user);
            }

            (var hasNext, after) = ReadPageInfo(conn);
            if (!hasNext) break;
        }

        return users.Values.ToList();
    }

    private async Task<JsonElement> QueryAsync(GitLabInstanceOptions instance, string token, string query, object variables, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{instance.BaseUrl}/api/graphql")
        {
            Content = JsonContent.Create(new { query, variables }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new GitLabException(502, $"Could not reach GitLab at {instance.BaseUrl}: {ex.Message}");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new GitLabException(401,
                    $"{instance.Name} rejected the access token. Check that it was created on {instance.BaseUrl}, is not expired, and has the read_api scope.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new GitLabException(429, "GitLab rate limit reached. Wait a minute and try again.");
            if (!response.IsSuccessStatusCode)
                throw new GitLabException(502, $"GitLab returned HTTP {(int)response.StatusCode}.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var messages = errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .Where(m => !string.IsNullOrWhiteSpace(m));
                var text = string.Join("; ", messages);
                if (text.Contains("invalid_token", StringComparison.OrdinalIgnoreCase))
                    throw new GitLabException(401, "GitLab rejected the access token.");
                throw new GitLabException(502, $"GitLab GraphQL error: {text}");
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new GitLabException(502, "GitLab returned an empty response.");

            return data.Clone();
        }
    }

    internal static Timelog? ReadTimelog(JsonElement node)
    {
        if (!node.TryGetProperty("spentAt", out var spentAtEl) || spentAtEl.ValueKind != JsonValueKind.String) return null;
        if (!DateTimeOffset.TryParse(spentAtEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var spentAt)) return null;
        if (!node.TryGetProperty("user", out var u) || ReadUser(u) is not { } user) return null;

        var seconds = node.TryGetProperty("timeSpent", out var ts) ? ReadLong(ts) : 0;
        var projectPath = node.TryGetProperty("project", out var p) && p.ValueKind == JsonValueKind.Object
            ? Str(p, "fullPath") ?? "" : "";

        IssueRef? issue = null;
        if (node.TryGetProperty("issue", out var i) && i.ValueKind == JsonValueKind.Object)
            issue = new IssueRef(projectPath, Str(i, "iid") ?? "?", Str(i, "title") ?? "(untitled)", Str(i, "webUrl"));

        var isMr = node.TryGetProperty("mergeRequest", out var mr) && mr.ValueKind == JsonValueKind.Object;

        return new Timelog(spentAt, seconds, user, issue, isMr, projectPath, Str(node, "summary"));
    }

    private static GitLabUser? ReadUser(JsonElement u)
    {
        if (u.ValueKind != JsonValueKind.Object) return null;
        var username = Str(u, "username");
        if (string.IsNullOrEmpty(username)) return null;
        return new GitLabUser(username, Str(u, "name") ?? username, Str(u, "avatarUrl"));
    }

    private static (bool HasNext, string? Cursor) ReadPageInfo(JsonElement connection)
    {
        var info = connection.GetProperty("pageInfo");
        var hasNext = info.TryGetProperty("hasNextPage", out var h) && h.ValueKind == JsonValueKind.True;
        return (hasNext, Str(info, "endCursor"));
    }

    private static long ReadLong(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : (long)el.GetDouble(),
        JsonValueKind.String when long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
        _ => 0,
    };

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
