using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Tempo.Api;
using Tempo.Api.Auth;
using Tempo.Api.GitLab;
using Tempo.Api.Reports;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TempoOptions>(builder.Configuration.GetSection(TempoOptions.SectionName));
builder.Services.AddHttpClient<GitLabClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("gitlab-tempo/1.0");
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ReportService>();
builder.Services.AddProblemDetails();
var signInEnabled = builder.AddGitLabSignIn();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles(); // the built React app is copied to wwwroot for production

var api = app.MapGroup("/api");
if (signInEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapGitLabSignIn();
    api.RequireAuthorization(); // the static UI stays public so it can show the sign-in screen
}
else
{
    app.Logger.LogWarning("Sign in with GitLab is off (Tempo:Auth:ClientId is not set). Anyone who can reach this app can use it.");
}

api.MapGet("/session", (HttpContext http, IOptions<TempoOptions> o) => Results.Ok(GitLabSignIn.Session(http, o.Value)))
    .AllowAnonymous();

api.MapGet("/config", (IOptions<TempoOptions> o) => Results.Ok(new
{
    instances = o.Value.AvailableInstances.Select(i => new
    {
        // Reports on this instance use the signed-in person's GitLab session instead of a pasted token.
        usesSignIn = UsesSignIn(i, o.Value),
        id = i.Id,
        name = i.Name,
        gitLabUrl = i.BaseUrl,
        defaultGroupPath = i.DefaultGroupPath,
        serverTokenConfigured = !string.IsNullOrWhiteSpace(i.ServerToken),
    }),
    timeZone = o.Value.TimeZone,
    workDays = o.Value.ResolvedWorkDays.Select(d => d.ToString()),
    maxWeeks = o.Value.MaxWeeks,
}));

api.MapGet("/me", async (HttpContext http, GitLabClient gitLab, IOptions<TempoOptions> o, CancellationToken ct) =>
{
    if (ResolveInstance(http, o.Value) is not { } instance) return UnknownInstance(http);
    if (await ResolveTokenAsync(http, instance, o.Value) is not { } token) return MissingToken();
    return await Run(async () => Results.Ok(await gitLab.GetCurrentUserAsync(instance, token, ct)));
});

api.MapGet("/report", async (
    HttpContext http,
    ReportService reports,
    IOptions<TempoOptions> o,
    string? group,
    DateOnly from,
    DateOnly? to,
    bool? includeMembers,
    CancellationToken ct) =>
{
    if (ResolveInstance(http, o.Value) is not { } instance) return UnknownInstance(http);
    if (await ResolveTokenAsync(http, instance, o.Value) is not { } token) return MissingToken();

    var groupPath = (string.IsNullOrWhiteSpace(group) ? instance.DefaultGroupPath : group)?.Trim().Trim('/');
    if (string.IsNullOrWhiteSpace(groupPath))
        return Results.Problem("Pass ?group=<group full path> or set a default group path for this GitLab instance.", statusCode: 400);

    var end = to ?? from;
    if (end < from) return Results.Problem("'to' must be on or after 'from'.", statusCode: 400);

    var weeks = (ReportBuilder.WeekStart(end).DayNumber - ReportBuilder.WeekStart(from).DayNumber) / 7 + 1;
    if (weeks > o.Value.MaxWeeks)
        return Results.Problem($"A report can span at most {o.Value.MaxWeeks} weeks (asked for {weeks}).", statusCode: 400);

    return await Run(async () =>
        Results.Ok(await reports.GetWeeklyReportAsync(instance, token, groupPath, from, end, includeMembers ?? true, ct)));
});

app.MapFallbackToFile("index.html");

app.Run();

// The browser picks a configured instance by id. It can never supply a URL itself, so tokens
// only ever go to GitLab servers listed in the server's configuration. With sign-in on, only the
// sign-in instance is available.
static GitLabInstanceOptions? ResolveInstance(HttpContext http, TempoOptions options) =>
    options.FindAvailableInstance(http.Request.Headers["X-GitLab-Instance"].ToString());

static IResult UnknownInstance(HttpContext http) =>
    Results.Problem($"Unknown GitLab instance '{http.Request.Headers["X-GitLab-Instance"]}'. Reload the page.", statusCode: 400);

static bool UsesSignIn(GitLabInstanceOptions instance, TempoOptions options) =>
    options.SignInInstance is { } signIn && string.Equals(signIn.Id, instance.Id, StringComparison.OrdinalIgnoreCase);

// On the sign-in instance the signed-in person's own GitLab token is always used, so they see
// exactly what they can see in GitLab. Other instances keep the pasted token / server token.
static async Task<string?> ResolveTokenAsync(HttpContext http, GitLabInstanceOptions instance, TempoOptions options)
{
    if (UsesSignIn(instance, options)) return await http.GetTokenAsync("access_token");

    var header = http.Request.Headers["X-GitLab-Token"].ToString();
    if (!string.IsNullOrWhiteSpace(header)) return header.Trim();
    return string.IsNullOrWhiteSpace(instance.ServerToken) ? null : instance.ServerToken;
}

static IResult MissingToken() =>
    Results.Problem("Add your GitLab Personal Access Token (read_api scope) in Settings.", statusCode: 401);

static async Task<IResult> Run(Func<Task<IResult>> action)
{
    try
    {
        return await action();
    }
    catch (GitLabException ex)
    {
        return Results.Problem(ex.Message, statusCode: ex.StatusCode);
    }
}

public partial class Program;
