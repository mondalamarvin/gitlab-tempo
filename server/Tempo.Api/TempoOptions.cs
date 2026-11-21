namespace Tempo.Api;

/// <summary>
/// App settings, bound from the "Tempo" section of appsettings.json / environment variables
/// (e.g. Tempo__DefaultGroupPath=my-company).
/// </summary>
public sealed class TempoOptions
{
    public const string SectionName = "Tempo";

    /// <summary>Base URL of the GitLab instance. gitlab.com by default; set it for self-managed.</summary>
    public string GitLabUrl { get; set; } = "https://gitlab.com";

    /// <summary>Group full path pre-filled in the UI, e.g. "my-company" or "my-company/dev".</summary>
    public string? DefaultGroupPath { get; set; }

    /// <summary>
    /// Optional server-side token used when the browser does not send one.
    /// Leave empty when every user brings their own Personal Access Token.
    /// </summary>
    public string? ServerToken { get; set; }

    /// <summary>
    /// Several GitLab instances the UI can switch between (e.g. gitlab.com and a self-managed one).
    /// When empty, a single instance is built from <see cref="GitLabUrl"/>, <see cref="ServerToken"/>
    /// and <see cref="DefaultGroupPath"/>.
    /// </summary>
    public List<GitLabInstanceOptions> Instances { get; set; } = [];

    /// <summary>The configured instances; the first one is the default.</summary>
    public IReadOnlyList<GitLabInstanceOptions> ResolvedInstances =>
        Instances.Count > 0
            ? Instances
            : [new GitLabInstanceOptions
            {
                Id = "default",
                Name = Uri.TryCreate(GitLabUrl, UriKind.Absolute, out var uri) ? uri.Authority : GitLabUrl,
                Url = GitLabUrl,
                ServerToken = ServerToken,
                DefaultGroupPath = DefaultGroupPath,
            }];

    /// <summary>Finds an instance by id; no id means the default (first) one. Null if the id is unknown.</summary>
    public GitLabInstanceOptions? FindInstance(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? ResolvedInstances[0]
            : ResolvedInstances.FirstOrDefault(i => string.Equals(i.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>"Sign in with GitLab". Off until <see cref="AuthOptions.ClientId"/> is set.</summary>
    public AuthOptions Auth { get; set; } = new();

    /// <summary>The instance people sign in with, or null when sign-in is off.</summary>
    public GitLabInstanceOptions? SignInInstance => Auth.Enabled ? FindInstance(Auth.InstanceId) : null;

    /// <summary>
    /// The instances the app offers. With sign-in on, only the sign-in instance: people work with the
    /// GitLab they signed in to, without picking an instance or pasting a token.
    /// </summary>
    public IReadOnlyList<GitLabInstanceOptions> AvailableInstances => SignInInstance is { } signIn ? [signIn] : ResolvedInstances;

    /// <summary>Like <see cref="FindInstance"/>, but only among <see cref="AvailableInstances"/>.</summary>
    public GitLabInstanceOptions? FindAvailableInstance(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? AvailableInstances[0]
            : AvailableInstances.FirstOrDefault(i => string.Equals(i.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>IANA time zone used to decide which day a time entry belongs to.</summary>
    public string TimeZone { get; set; } = "Asia/Manila";

    /// <summary>Days that count as work days. Null = Monday to Friday.</summary>
    public DayOfWeek[]? WorkDays { get; set; }

    /// <summary>Upper limit on the number of weeks a single report may span.</summary>
    public int MaxWeeks { get; set; } = 26;

    public IReadOnlyList<DayOfWeek> ResolvedWorkDays =>
        WorkDays is { Length: > 0 }
            ? WorkDays.Distinct().OrderBy(d => ((int)d + 6) % 7).ToArray()
            : [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        }
        catch (Exception) when (TimeZone == "Asia/Manila")
        {
            // Hosts without tz data: Manila has no DST, so a fixed +08:00 zone is exact.
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Manila", TimeSpan.FromHours(8), "Asia/Manila", "PHT");
        }
    }
}

/// <summary>
/// "Sign in with GitLab" settings (Tempo:Auth). Register an OAuth application in GitLab with the
/// redirect URI https://your-host/signin-gitlab and the read_api scope.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>The GitLab OAuth application ID. Setting it turns sign-in on.</summary>
    public string? ClientId { get; set; }

    /// <summary>The GitLab OAuth application secret. Keep it in user secrets or an environment variable.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Id of the instance people sign in with. Empty = the first (default) instance.</summary>
    public string? InstanceId { get; set; }

    /// <summary>Group full paths. Members of any of them (direct or inherited) may sign in.</summary>
    public List<string> AllowedGroups { get; set; } = [];

    /// <summary>Usernames or emails that may sign in, in addition to the group members.</summary>
    public List<string> AllowedUsers { get; set; } = [];

    /// <summary>How long a session lasts without activity.</summary>
    public double SessionHours { get; set; } = 8;

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>True when anyone who can sign in to the GitLab instance gets in.</summary>
    public bool AllowsEveryone => AllowedGroups.All(string.IsNullOrWhiteSpace) && AllowedUsers.All(string.IsNullOrWhiteSpace);
}

/// <summary>One GitLab instance the app can read from.</summary>
public sealed class GitLabInstanceOptions
{
    /// <summary>Short id the browser uses to pick this instance, e.g. "gitlab-com" or "local".</summary>
    public string Id { get; set; } = "";

    /// <summary>Label shown in the UI.</summary>
    public string Name { get; set; } = "";

    /// <summary>Base URL, e.g. https://gitlab.com or http://localhost:8929.</summary>
    public string Url { get; set; } = "https://gitlab.com";

    /// <summary>Optional token used when the browser does not send one. Keep it out of git.</summary>
    public string? ServerToken { get; set; }

    /// <summary>Group path pre-filled in the UI.</summary>
    public string? DefaultGroupPath { get; set; }

    public string BaseUrl => Url.TrimEnd('/');
}
