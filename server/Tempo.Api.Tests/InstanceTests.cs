using Tempo.Api;
using Tempo.Api.GitLab;

namespace Tempo.Api.Tests;

public class InstanceTests
{
    private static TempoOptions TwoInstances() => new()
    {
        Instances =
        [
            new() { Id = "gitlab-com", Name = "GitLab.com", Url = "https://gitlab.com/" },
            new() { Id = "local", Name = "Local", Url = "http://localhost:8929", ServerToken = "t" },
        ],
    };

    [Fact]
    public void Without_instances_the_single_legacy_settings_are_used()
    {
        var options = new TempoOptions { GitLabUrl = "https://git.example.com/", ServerToken = "s", DefaultGroupPath = "team" };

        var only = Assert.Single(options.ResolvedInstances);
        Assert.Equal("default", only.Id);
        Assert.Equal("git.example.com", only.Name);
        Assert.Equal("https://git.example.com", only.BaseUrl);
        Assert.Equal("s", only.ServerToken);
        Assert.Equal("team", only.DefaultGroupPath);
    }

    [Fact]
    public void No_id_picks_the_first_instance()
    {
        Assert.Equal("gitlab-com", TwoInstances().FindInstance(null)!.Id);
        Assert.Equal("gitlab-com", TwoInstances().FindInstance("  ")!.Id);
    }

    [Fact]
    public void Id_lookup_is_case_insensitive()
    {
        Assert.Equal("http://localhost:8929", TwoInstances().FindInstance("LOCAL")!.BaseUrl);
    }

    [Fact]
    public void Unknown_id_is_rejected_rather_than_falling_back()
    {
        Assert.Null(TwoInstances().FindInstance("https://evil.example.com"));
    }

    [Fact]
    public void Relative_avatar_urls_are_made_absolute_for_the_instance()
    {
        var local = TwoInstances().FindInstance("local")!;

        var user = GitLabClient.WithAbsoluteAvatar(new GitLabUser("a", "A", "/uploads/a.png"), local);
        Assert.Equal("http://localhost:8929/uploads/a.png", user.AvatarUrl);

        var external = GitLabClient.WithAbsoluteAvatar(new GitLabUser("b", "B", "https://gravatar.com/b"), local);
        Assert.Equal("https://gravatar.com/b", external.AvatarUrl);
    }
}
