using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Pure projection of an ADO pipelines Approval JSON object into the compact
/// shape returned by the <c>ado_bridge_approvals_*</c> tools. Kept free of IO so
/// it can be unit-tested directly (like <c>WitGetSlimTool.BuildSlimJson</c>).
/// </summary>
internal static class ApprovalProjection
{
    /// <summary>Projects a single approval element to its compact JSON string.</summary>
    public static string ProjectToJson(JsonElement approval)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
            WriteApproval(writer, approval);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Writes the compact projection of a single approval to an open writer.</summary>
    internal static void WriteApproval(Utf8JsonWriter writer, JsonElement approval)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("id");
        approval.GetProperty("id").WriteTo(writer);
        writer.WritePropertyName("status");
        approval.GetProperty("status").WriteTo(writer);

        if (approval.TryGetProperty("instructions", out var instructions) &&
            instructions.ValueKind != JsonValueKind.Null)
        {
            writer.WritePropertyName("instructions");
            instructions.WriteTo(writer);
        }

        writer.WritePropertyName("executionOrder");
        approval.GetProperty("executionOrder").WriteTo(writer);
        writer.WritePropertyName("minRequiredApprovers");
        approval.GetProperty("minRequiredApprovers").WriteTo(writer);
        writer.WritePropertyName("createdOn");
        approval.GetProperty("createdOn").WriteTo(writer);
        writer.WritePropertyName("lastModifiedOn");
        approval.GetProperty("lastModifiedOn").WriteTo(writer);

        writer.WritePropertyName("blockedApprovers");
        writer.WriteStartArray();
        if (approval.TryGetProperty("blockedApprovers", out var blocked))
            foreach (var identity in blocked.EnumerateArray())
                WriteIdentitySummary(writer, identity);
        writer.WriteEndArray();

        if (approval.TryGetProperty("steps", out var steps))
        {
            writer.WritePropertyName("steps");
            writer.WriteStartArray();
            foreach (var step in steps.EnumerateArray())
                WriteStep(writer, step);
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteStep(Utf8JsonWriter writer, JsonElement step)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("order");
        step.GetProperty("order").WriteTo(writer);
        writer.WritePropertyName("status");
        step.GetProperty("status").WriteTo(writer);
        WriteApproverDisplayName(writer, "assignedApprover", step, "assignedApprover");
        WriteApproverDisplayName(writer, "actualApprover", step, "actualApprover");
        if (step.TryGetProperty("comment", out var comment) &&
            comment.ValueKind != JsonValueKind.Null)
            writer.WriteString("comment", comment.GetString());
        writer.WriteEndObject();
    }

    private static void WriteApproverDisplayName(
        Utf8JsonWriter writer, string name, JsonElement step, string prop)
    {
        if (step.TryGetProperty(prop, out var identity))
            writer.WriteString(name, GetStringOrNull(identity, "displayName"));
        else
            writer.WriteNull(name);
    }

    private static void WriteIdentitySummary(Utf8JsonWriter writer, JsonElement identity)
    {
        writer.WriteStartObject();
        writer.WriteString("id", GetStringOrNull(identity, "id"));
        writer.WriteString("displayName", GetStringOrNull(identity, "displayName"));
        writer.WriteEndObject();
    }

    private static string? GetStringOrNull(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.GetString() : null;
}
