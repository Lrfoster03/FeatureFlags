using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;

namespace FeatureFlags.Services;

public sealed record AuditFieldChange(string Path, string Kind, string Before, string After);

public static class AuditDiff
{
    // Display only: Razor encodes these strings as text, never as HTML markup.
    private static readonly JsonSerializerOptions DisplayOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static IReadOnlyList<AuditFieldChange> Compare(string? before, string? after)
    {
        var changes = new List<AuditFieldChange>();
        Walk(before is null ? null : JsonNode.Parse(before), before is not null,
            after is null ? null : JsonNode.Parse(after), after is not null, "", changes);
        return changes;
    }

    private static void Walk(JsonNode? before, bool beforeExists, JsonNode? after, bool afterExists, string path, List<AuditFieldChange> changes)
    {
        if (beforeExists == afterExists && JsonNode.DeepEquals(before, after)) return;
        if ((before is JsonObject || !beforeExists) && (after is JsonObject || !afterExists))
        {
            var left = before as JsonObject; var right = after as JsonObject;
            var keys = (left?.Select(p => p.Key) ?? []).Concat(right?.Select(p => p.Key) ?? []).Distinct().Order(StringComparer.Ordinal).ToList();
            foreach (var key in keys)
            {
                JsonNode? l = null, r = null;
                var hasLeft = left?.TryGetPropertyValue(key, out l) == true;
                var hasRight = right?.TryGetPropertyValue(key, out r) == true;
                Walk(l, hasLeft, r, hasRight, path + "/" + key.Replace("~", "~0").Replace("/", "~1"), changes);
            }
            if (keys.Count > 0) return;
        }
        changes.Add(new(path, !beforeExists ? "Added" : !afterExists ? "Removed" : "Changed",
            Format(before, beforeExists, path), Format(after, afterExists, path)));
    }

    private static string Format(JsonNode? value, bool exists, string path)
    {
        if (!exists) return "—";
        if (value is null) return "null";
        if (path == "/percentageRollout") return value + "%";
        if (path == "/isEnabled") return value.GetValue<bool>() ? "On" : "Off";
        return value.ToJsonString(DisplayOptions);
    }
    public static string Label(string path) => path switch
    {
        "/percentageRollout" => "Rollout", "/isEnabled" => "Enabled", "/name" => "Name", "/description" => "Description",
        "/role" => "Role", "/active" => "Active", "/email" => "Email", "/displayName" => "Display name",
        _ when path.StartsWith("/value/", StringComparison.Ordinal) => "Value " + path[6..],
        _ when path.StartsWith("/schema/", StringComparison.Ordinal) => "Schema " + path[7..],
        _ => string.IsNullOrEmpty(path) ? "Resource" : path
    };
    public static string Pretty(string? json) => json is null ? "No resource" : JsonNode.Parse(json)?.ToJsonString(new JsonSerializerOptions(DisplayOptions) { WriteIndented = true }) ?? "null";
}
