using System.Text.Json;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class ApprovalProjectionTests
{
    private static JsonElement Parse(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.Clone();
    }

    private static readonly object BaseApproval = new
    {
        id = "app-1",
        status = "pending",
        executionOrder = "anyOrder",
        minRequiredApprovers = 2,
        createdOn = "2026-07-01T00:00:00Z",
        lastModifiedOn = "2026-07-02T00:00:00Z",
    };

    [Fact]
    public void ProjectToJson_copies_the_core_scalar_fields()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(BaseApproval));

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("id").GetString().Should().Be("app-1");
        root.GetProperty("status").GetString().Should().Be("pending");
        root.GetProperty("executionOrder").GetString().Should().Be("anyOrder");
        root.GetProperty("minRequiredApprovers").GetInt32().Should().Be(2);
        root.GetProperty("createdOn").GetString().Should().Be("2026-07-01T00:00:00Z");
        root.GetProperty("lastModifiedOn").GetString().Should().Be("2026-07-02T00:00:00Z");
    }

    [Fact]
    public void ProjectToJson_includes_instructions_when_present()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(new
        {
            id = "a",
            status = "pending",
            executionOrder = "anyOrder",
            minRequiredApprovers = 1,
            createdOn = "x",
            lastModifiedOn = "y",
            instructions = "Please review the deploy",
        }));

        JsonDocument.Parse(json).RootElement
            .GetProperty("instructions").GetString().Should().Be("Please review the deploy");
    }

    [Fact]
    public void ProjectToJson_omits_instructions_when_absent()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(BaseApproval));

        JsonDocument.Parse(json).RootElement
            .TryGetProperty("instructions", out _).Should().BeFalse();
    }

    [Fact]
    public void ProjectToJson_omits_instructions_when_null()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(new
        {
            id = "a",
            status = "pending",
            executionOrder = "anyOrder",
            minRequiredApprovers = 1,
            createdOn = "x",
            lastModifiedOn = "y",
            instructions = (string?)null,
        }));

        JsonDocument.Parse(json).RootElement
            .TryGetProperty("instructions", out _).Should().BeFalse();
    }

    [Fact]
    public void ProjectToJson_emits_empty_blockedApprovers_array_when_absent()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(BaseApproval));

        var blocked = JsonDocument.Parse(json).RootElement.GetProperty("blockedApprovers");
        blocked.ValueKind.Should().Be(JsonValueKind.Array);
        blocked.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void ProjectToJson_projects_blockedApprovers_id_and_displayName()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(new
        {
            id = "a",
            status = "pending",
            executionOrder = "anyOrder",
            minRequiredApprovers = 1,
            createdOn = "x",
            lastModifiedOn = "y",
            blockedApprovers = new[]
            {
                new { id = "u1", displayName = "Alice", descriptor = "aad.xxx", uniqueName = "alice@x" },
            },
        }));

        var blocked = JsonDocument.Parse(json).RootElement.GetProperty("blockedApprovers");
        blocked.GetArrayLength().Should().Be(1);
        blocked[0].GetProperty("id").GetString().Should().Be("u1");
        blocked[0].GetProperty("displayName").GetString().Should().Be("Alice");
        blocked[0].TryGetProperty("descriptor", out _).Should().BeFalse();
    }

    [Fact]
    public void ProjectToJson_writes_null_for_blockedApprover_missing_id_and_displayName()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(new
        {
            id = "a",
            status = "pending",
            executionOrder = "anyOrder",
            minRequiredApprovers = 1,
            createdOn = "x",
            lastModifiedOn = "y",
            blockedApprovers = new[] { new { uniqueName = "only-this" } },
        }));

        var blocked = JsonDocument.Parse(json).RootElement.GetProperty("blockedApprovers");
        blocked[0].GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
        blocked[0].GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ProjectToJson_omits_steps_when_absent()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(BaseApproval));

        JsonDocument.Parse(json).RootElement.TryGetProperty("steps", out _).Should().BeFalse();
    }

    [Fact]
    public void ProjectToJson_projects_steps_with_approver_displayNames_and_comment()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(new
        {
            id = "a",
            status = "pending",
            executionOrder = "inSequence",
            minRequiredApprovers = 1,
            createdOn = "x",
            lastModifiedOn = "y",
            steps = new[]
            {
                new
                {
                    order = 1,
                    status = "approved",
                    assignedApprover = new { id = "u1", displayName = "Alice" },
                    actualApprover = new { id = "u1", displayName = "Alice" },
                    comment = "LGTM",
                },
            },
        }));

        var step = JsonDocument.Parse(json).RootElement.GetProperty("steps")[0];
        step.GetProperty("order").GetInt32().Should().Be(1);
        step.GetProperty("status").GetString().Should().Be("approved");
        step.GetProperty("assignedApprover").GetString().Should().Be("Alice");
        step.GetProperty("actualApprover").GetString().Should().Be("Alice");
        step.GetProperty("comment").GetString().Should().Be("LGTM");
    }

    [Fact]
    public void ProjectToJson_writes_null_approvers_and_omits_comment_when_absent()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(new
        {
            id = "a",
            status = "pending",
            executionOrder = "anyOrder",
            minRequiredApprovers = 1,
            createdOn = "x",
            lastModifiedOn = "y",
            // assignedApprover present but without a displayName; actualApprover and comment absent.
            steps = new[] { new { order = 2, status = "pending", assignedApprover = new { id = "u9" } } },
        }));

        var step = JsonDocument.Parse(json).RootElement.GetProperty("steps")[0];
        step.GetProperty("assignedApprover").ValueKind.Should().Be(JsonValueKind.Null);
        step.GetProperty("actualApprover").ValueKind.Should().Be(JsonValueKind.Null);
        step.TryGetProperty("comment", out _).Should().BeFalse();
    }

    [Fact]
    public void ProjectToJson_omits_step_comment_when_null()
    {
        var json = ApprovalProjection.ProjectToJson(Parse(new
        {
            id = "a",
            status = "pending",
            executionOrder = "anyOrder",
            minRequiredApprovers = 1,
            createdOn = "x",
            lastModifiedOn = "y",
            steps = new[]
            {
                new
                {
                    order = 1,
                    status = "pending",
                    assignedApprover = new { id = "u1", displayName = "Alice" },
                    comment = (string?)null,
                },
            },
        }));

        var step = JsonDocument.Parse(json).RootElement.GetProperty("steps")[0];
        step.TryGetProperty("comment", out _).Should().BeFalse();
    }
}
