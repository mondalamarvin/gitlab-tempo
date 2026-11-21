using Microsoft.Extensions.Options;
using Tempo.Api.GitLab;

namespace Tempo.Api.Reports;

public sealed class ReportService(GitLabClient gitLab, IOptions<TempoOptions> options, TimeProvider clock, ILogger<ReportService> logger)
{
    public async Task<WeeklyReport> GetWeeklyReportAsync(
        GitLabInstanceOptions instance, string token, string groupPath, DateOnly from, DateOnly to, bool includeMembers, CancellationToken ct)
    {
        var opts = options.Value;
        var tz = opts.ResolveTimeZone();
        var (start, end) = ReportBuilder.QueryWindow(from, to, tz);

        var logsTask = gitLab.GetGroupTimelogsAsync(instance, token, groupPath, start, end, ct);
        var membersTask = includeMembers ? TryGetMembersAsync(instance, token, groupPath, ct) : Task.FromResult<IReadOnlyList<GitLabUser>?>(null);
        await Task.WhenAll(logsTask, membersTask);

        var result = logsTask.Result;
        var timelogs = result.Timelogs.Select(l => l with { User = GitLabClient.WithAbsoluteAvatar(l.User, instance) });
        var members = membersTask.Result?.Select(u => GitLabClient.WithAbsoluteAvatar(u, instance));

        return ReportBuilder.Build(result.Group, from, to, timelogs, members, tz, opts.ResolvedWorkDays, clock.GetUtcNow());
    }

    private async Task<IReadOnlyList<GitLabUser>?> TryGetMembersAsync(GitLabInstanceOptions instance, string token, string groupPath, CancellationToken ct)
    {
        try
        {
            return await gitLab.GetGroupMembersAsync(instance, token, groupPath, ct);
        }
        catch (GitLabException ex)
        {
            // Members are a nice-to-have (shows people with zero hours); never fail the report over them.
            logger.LogWarning("Could not load members of {Group}: {Message}", groupPath, ex.Message);
            return null;
        }
    }
}
