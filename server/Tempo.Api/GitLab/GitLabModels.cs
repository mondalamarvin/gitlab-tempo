namespace Tempo.Api.GitLab;

/// <summary>One time entry as returned by GitLab, flattened.</summary>
public sealed record Timelog(
    DateTimeOffset SpentAt,
    long Seconds,
    GitLabUser User,
    IssueRef? Issue,
    bool IsMergeRequest,
    string ProjectPath,
    string? Summary);

public sealed record GitLabUser(string Username, string Name, string? AvatarUrl);

public sealed record IssueRef(string ProjectPath, string Iid, string Title, string? WebUrl);

public sealed record GroupInfo(string FullPath, string Name, string? WebUrl);

public sealed record GroupTimelogs(GroupInfo Group, IReadOnlyList<Timelog> Timelogs);

public sealed class GitLabException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
