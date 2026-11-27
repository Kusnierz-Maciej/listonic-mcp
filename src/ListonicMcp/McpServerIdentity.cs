using System.Reflection;
using ModelContextProtocol.Protocol;

namespace ListonicMcp;

/// <summary>
/// The server's MCP <c>serverInfo</c>: name, version, display title and icons. Icons are inlined as
/// <c>data:</c> URIs (embedded from <c>wwwroot</c>) so clients can show them without knowing the
/// server's public URL — it usually sits behind a proxy, and stdio mode has no URL at all. The artwork is
/// the Listonic app icon from its Google Play listing (512 px), with rounded corners for the favicon and
/// the 192/512 px icons.
/// </summary>
internal static class McpServerIdentity
{
    private static readonly Assembly Self = typeof(McpServerIdentity).Assembly;

    public static Implementation Create() => new()
    {
        Name = "ListonicMcp",
        Title = "Listonic (unofficial)",
        Version = Self.GetName().Version?.ToString(3) ?? "unknown",
        Icons =
        [
            DataUriIcon("icon-192.png", "image/png", "192x192"),
            DataUriIcon("icon-512.png", "image/png", "512x512"),
        ],
    };

    private static Icon DataUriIcon(string file, string mimeType, string size)
    {
        using var stream = Self.GetManifestResourceStream($"ListonicMcp.Icons.{file}")
            ?? throw new InvalidOperationException($"Embedded icon '{file}' not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new Icon
        {
            Source = $"data:{mimeType};base64,{Convert.ToBase64String(buffer.ToArray())}",
            MimeType = mimeType,
            Sizes = [size],
        };
    }
}
