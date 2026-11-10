using Capri.Core.Autonomy;
using Capri.Core.Autonomy.Authority;
using Capri.Desktop.Services;

namespace Capri.Tests.Desktop;

public sealed class DesktopAuthorityApprovalTests
{
    [Fact]
    public void IsApprovalAvailable_RequiresIdentityHashAndSteps()
    {
        var plan = new TaskPlan([new TaskStep(TaskTool.LaunchApp, "notepad")]);
        var ok = new ApprovalRequestSnapshot(Guid.NewGuid(), Guid.NewGuid(), "goal", "p", "m", plan, "hash", DateTimeOffset.UtcNow);
        Assert.True(WpfTaskAuthorityUiBridge.IsApprovalAvailable(ok));

        Assert.False(WpfTaskAuthorityUiBridge.IsApprovalAvailable(null));
        Assert.False(WpfTaskAuthorityUiBridge.IsApprovalAvailable(ok with { TaskId = Guid.Empty }));
        Assert.False(WpfTaskAuthorityUiBridge.IsApprovalAvailable(ok with { ApprovalId = Guid.Empty }));
        Assert.False(WpfTaskAuthorityUiBridge.IsApprovalAvailable(ok with { PlanHash = "" }));
        Assert.False(WpfTaskAuthorityUiBridge.IsApprovalAvailable(new ApprovalRequestSnapshot(ok.TaskId, ok.ApprovalId, ok.Goal, ok.ProviderId, ok.ModelId, new TaskPlan([]), ok.PlanHash, ok.RequestedUtc)));
    }

    [Fact]
    public void BuildPlanText_IncludesExactBindingAndSteps()
    {
        var plan = new TaskPlan([
            new TaskStep(TaskTool.CreateNote, "hello.txt", "content"),
            new TaskStep(TaskTool.OpenNote, "hello.txt"),
        ]);
        var snap = new ApprovalRequestSnapshot(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Create note", "ollama", "qwen", plan, "abc123hash", DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var text = WpfTaskAuthorityUiBridge.BuildPlanText(snap);
        Assert.Contains("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", text);
        Assert.Contains("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", text);
        Assert.Contains("abc123hash", text);
        Assert.Contains("ollama", text);
        Assert.Contains("CreateNote", text);
        Assert.Contains("hello.txt", text);
        Assert.Contains("NO actions occur without your explicit approval", text);
    }

    [Fact]
    public void BuildPlanText_DisplaysCompleteNoteContentWithoutTruncation()
    {
        var noteText = new string('a', 350) + "END_OF_APPROVAL_NOTE";
        var plan = new TaskPlan([new TaskStep(TaskTool.CreateNote, "review.txt", noteText)]);
        var snapshot = new ApprovalRequestSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Review note",
            "ollama", "local", plan, "plan-hash", DateTimeOffset.UtcNow);

        var display = WpfTaskAuthorityUiBridge.BuildPlanText(snapshot);

        Assert.Contains(noteText, display);
        Assert.Contains("END_OF_APPROVAL_NOTE", display);
        Assert.DoesNotContain(new string('a', 300) + "...", display);
    }

    [Fact]
    public void DescribeRisks_FlagsModifyingActions()
    {
        var plan = new TaskPlan([
            new TaskStep(TaskTool.CreateNote, "a.txt", "x"),
            new TaskStep(TaskTool.OpenUrl, "https://example.com"),
        ]);
        var snap = new ApprovalRequestSnapshot(Guid.NewGuid(), Guid.NewGuid(), "g", null, null, plan, "h", DateTimeOffset.UtcNow);
        var risks = WpfTaskAuthorityUiBridge.DescribeRisks(snap);
        Assert.Contains("Creates/writes file", risks);
        Assert.Contains("Opens URL", risks);
    }
}
