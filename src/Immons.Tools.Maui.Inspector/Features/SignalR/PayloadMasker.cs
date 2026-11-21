using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.SignalR;

/// <summary>
/// Turns a hub message into the short, masked JSON the panel shows. Masking happens before
/// anything is stored: values of fields whose name looks like a secret (token, password,
/// authorization…), bearer strings and JWTs are replaced with <see cref="Mask"/>, so the
/// inspector's buffer — and its HTTP API — never hold them.
/// </summary>
internal sealed class PayloadMasker
{
    public const string Mask = "***";

    /// <summary>Frames above this size are not parsed for a preview at all (only counted).</summary>
    public const int MaxParsedBytes = 512 * 1024;

    /// <summary>Name fragments matched anywhere in a normalized key (lowercase, no _ - .).</summary>
    static readonly string[] DefaultFragments =
    [
        "token", "password", "passwd", "secret", "authorization", "apikey", "cookie",
        "credential", "privatekey", "accesskey", "sessionid", "refreshtoken",
    ];

    /// <summary>Short names that would cause false positives as fragments ("pin" in "shipping").</summary>
    static readonly string[] DefaultExactNames = ["pwd", "pin", "otp", "cvv", "cvc", "auth", "jwt", "sid"];

    readonly string[] _fragments;
    readonly HashSet<string> _exact;

    public PayloadMasker(IEnumerable<string>? extraFields = null)
    {
        var extra = (extraFields ?? []).Select(Normalize).Where(f => f.Length > 0).ToArray();
        _fragments = [.. DefaultFragments, .. extra];
        _exact = new HashSet<string>(DefaultExactNames, StringComparer.Ordinal);
    }

    public bool IsSensitive(string key)
    {
        var normalized = Normalize(key);
        if (normalized.Length == 0)
            return false;
        if (_exact.Contains(normalized))
            return true;
        foreach (var fragment in _fragments)
        {
            if (normalized.Contains(fragment, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Masks in place: sensitive keys at any depth, bearer strings and JWT-looking values.</summary>
    public JsonNode? MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    // A whole object/array under a secret-looking key goes too ("credentials": {...}).
                    if (IsSensitive(key) && obj[key] is not null)
                        obj[key] = Mask;
                    else
                    {
                        var child = obj[key];
                        var masked = MaskNode(child);
                        if (!ReferenceEquals(masked, child))
                            obj[key] = masked;
                    }
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    var masked = MaskNode(child);
                    if (!ReferenceEquals(masked, child))
                        array[i] = masked;
                }
                return array;
            case JsonValue value when value.TryGetValue<string>(out var text) && LooksSecret(text):
                return JsonValue.Create(Mask);
            default:
                return node;
        }
    }

    /// <summary>Masks the values of sensitive query parameters (access_token=…) in a URL.</summary>
    public string MaskUrl(string url)
    {
        var q = url.IndexOf('?');
        if (q < 0)
            return url;
        var hash = url.IndexOf('#', q);
        var query = hash < 0 ? url[(q + 1)..] : url[(q + 1)..hash];
        var parts = query.Split('&');
        for (var i = 0; i < parts.Length; i++)
        {
            var eq = parts[i].IndexOf('=');
            if (eq > 0 && IsSensitive(Uri.UnescapeDataString(parts[i][..eq])))
                parts[i] = parts[i][..(eq + 1)] + Mask;
        }
        return url[..(q + 1)] + string.Join('&', parts) + (hash < 0 ? "" : url[hash..]);
    }

    /// <summary>
    /// Preview of a UTF-8 JSON frame (JSON and Newtonsoft protocols put the whole message on the
    /// wire as one JSON object). The "type" field is dropped — the event's kind already says it.
    /// </summary>
    public (string? Payload, bool Truncated) FromJsonUtf8(ReadOnlySpan<byte> utf8, int maxChars)
    {
        if (utf8.Length > 0 && utf8[^1] == 0x1E)
            utf8 = utf8[..^1];
        if (utf8.Length == 0)
            return (null, false);
        if (utf8.Length > MaxParsedBytes)
            return ($"({utf8.Length} bytes — too large to preview)", true);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(utf8);
        }
        catch (JsonException)
        {
            // Never echo unparsed text: it would bypass masking.
            return ($"(not valid JSON, {utf8.Length} bytes)", false);
        }

        if (node is JsonObject obj)
            obj.Remove("type");
        return FromNode(node, maxChars);
    }

    public (string? Payload, bool Truncated) FromNode(JsonNode? node, int maxChars)
    {
        if (node == null)
            return (null, false);
        var masked = MaskNode(node);
        return Truncate(masked?.ToJsonString(DisplayOptions) ?? "null", maxChars);
    }

    /// <summary>Readable text: "zażółć" and emoji stay as they are instead of \uXXXX (shown as text, never as HTML).</summary>
    static readonly JsonSerializerOptions DisplayOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static (string Text, bool Truncated) Truncate(string text, int maxChars)
    {
        if (maxChars <= 0 || text.Length <= maxChars)
            return (text, false);
        var cut = maxChars;
        if (char.IsHighSurrogate(text[cut - 1]))
            cut--;
        return (text[..cut] + "…", true);
    }

    static bool LooksSecret(string value)
    {
        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return true;
        // JWT: three base64url segments, header starting with {" → "eyJ".
        if (value.Length > 20 && value.StartsWith("eyJ", StringComparison.Ordinal))
        {
            var dots = 0;
            foreach (var c in value)
            {
                if (c == '.')
                    dots++;
                else if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '='))
                    return false;
            }
            return dots == 2;
        }
        return false;
    }

    static string Normalize(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            if (c is '_' or '-' or '.' or ' ')
                continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
