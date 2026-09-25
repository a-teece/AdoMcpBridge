using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Thrown when a caller-supplied tool argument is missing or malformed. The message is
/// safe to return verbatim to the caller; <see cref="CustomToolMiddleware"/> maps it to a
/// JSON-RPC <c>-32602</c> (invalid params) rather than the opaque <c>-32603</c>
/// "Internal error" a raw <see cref="KeyNotFoundException"/> would otherwise produce.
/// </summary>
internal sealed class CallerArgumentException(string message) : Exception(message);

/// <summary>
/// Reads and validates arguments from a tool-call <see cref="JsonElement"/>. Required
/// helpers throw <see cref="CallerArgumentException"/> with an actionable message when the
/// property is absent, the wrong type, or (for strings) empty; optional helpers return
/// <see langword="null"/> when the property is absent or JSON null.
/// </summary>
internal static class ToolArgs
{
    /// <summary>
    /// Returns the value of a required string argument. Throws
    /// <see cref="CallerArgumentException"/> when <paramref name="name"/> is absent, JSON
    /// null, not a string, or empty/whitespace.
    /// </summary>
    public static string RequireString(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(name, out var el) &&
            el.ValueKind == JsonValueKind.String &&
            el.GetString() is { } value &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new CallerArgumentException($"'{name}' is required and must be a non-empty string.");
    }

    /// <summary>
    /// Returns the value of a required integer argument. Throws
    /// <see cref="CallerArgumentException"/> when <paramref name="name"/> is absent or is
    /// not a JSON number expressible as an <see cref="int"/>.
    /// </summary>
    public static int RequireInt(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(name, out var el) &&
            el.ValueKind == JsonValueKind.Number &&
            el.TryGetInt32(out var value))
        {
            return value;
        }

        throw new CallerArgumentException($"'{name}' is required and must be an integer.");
    }

    /// <summary>
    /// Returns the value of an optional string argument, or <see langword="null"/> when
    /// <paramref name="name"/> is absent, JSON null, or not a string.
    /// </summary>
    public static string? GetString(JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object &&
           args.TryGetProperty(name, out var el) &&
           el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
