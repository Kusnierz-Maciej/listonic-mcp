using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace Listonic.Sandbox.Dumping;

/// <summary>
/// Innermost pipeline handler for the sandbox: dumps every PHYSICAL HTTP send — the fully
/// decorated request (app headers + bearer) and the response — to a per-run <c>.http</c> file.
/// Because it sits inside <c>ListonicAuthHandler</c>, it fires once per <c>base.SendAsync</c>,
/// i.e. twice when the auth handler re-sends on 401, so a retry shows up as two files.
///
/// Bodies are buffered and restored so capturing them does not consume the stream the real
/// send needs.
/// </summary>
public sealed class DumpingHttpHandler(DumpRun run) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var seq = run.NextSequence();
        var started = DateTimeOffset.UtcNow;

        var requestBody = await BufferAndRestoreAsync(request.Content, cancellationToken);
        if (requestBody.Replacement is not null) request.Content = requestBody.Replacement;

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Capture transport-level failures (DNS, TLS, timeout) too — they never reach the
            // response path but are exactly the kind of thing we want on record.
            WriteTransportFailure(seq, started, sw, request, requestBody, ex);
            throw;
        }
        sw.Stop();

        var responseBody = await BufferAndRestoreAsync(response.Content, cancellationToken);
        if (responseBody.Replacement is not null) response.Content = responseBody.Replacement;
        Write(seq, started, sw, request, requestBody, response, responseBody);

        return response;
    }

    private void Write(
        int seq, DateTimeOffset started, Stopwatch sw,
        HttpRequestMessage request, BodyCapture requestBody,
        HttpResponseMessage response, BodyCapture responseBody)
    {
        var sb = new StringBuilder();
        sb.Append("### ").Append(Pad(seq)).Append(' ')
          .Append(started.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
          .Append("  (elapsed ").Append(sw.ElapsedMilliseconds).AppendLine(" ms)");

        sb.Append(request.Method).Append(' ').Append(request.RequestUri)
          .Append(" HTTP/").AppendLine(request.Version.ToString());
        AppendHeaders(sb, request.Headers, request.Content?.Headers);
        sb.AppendLine();
        AppendBody(sb, requestBody);
        sb.AppendLine();

        sb.Append("--- RESPONSE ").Append((int)response.StatusCode).Append(' ')
          .Append(response.StatusCode).AppendLine(" ---");
        AppendHeaders(sb, response.Headers, response.Content?.Headers);
        sb.AppendLine();
        AppendBody(sb, responseBody);

        var file = Path.Combine(run.Directory, FileName(seq, request));
        File.WriteAllText(file, sb.ToString());
        run.Log($"#{Pad(seq)} {request.Method} {request.RequestUri?.AbsolutePath} -> " +
                $"{(int)response.StatusCode} ({sw.ElapsedMilliseconds} ms)  [{Path.GetFileName(file)}]");
    }

    private void WriteTransportFailure(
        int seq, DateTimeOffset started, Stopwatch sw,
        HttpRequestMessage request, BodyCapture requestBody, Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append("### ").Append(Pad(seq)).Append(' ')
          .Append(started.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
          .Append("  (elapsed ").Append(sw.ElapsedMilliseconds).AppendLine(" ms)");
        sb.Append(request.Method).Append(' ').Append(request.RequestUri)
          .Append(" HTTP/").AppendLine(request.Version.ToString());
        AppendHeaders(sb, request.Headers, request.Content?.Headers);
        sb.AppendLine();
        AppendBody(sb, requestBody);
        sb.AppendLine();
        sb.AppendLine("--- TRANSPORT FAILURE (no response) ---");
        sb.AppendLine(ex.ToString());

        var file = Path.Combine(run.Directory, FileName(seq, request));
        File.WriteAllText(file, sb.ToString());
        run.Log($"#{Pad(seq)} {request.Method} {request.RequestUri?.AbsolutePath} -> " +
                $"TRANSPORT FAILURE: {ex.Message}  [{Path.GetFileName(file)}]");
    }

    private static void AppendHeaders(StringBuilder sb, HttpHeaders headers, HttpContentHeaders? contentHeaders)
    {
        // One line per header name. .NET exposes multi-token headers (e.g. User-Agent) as several
        // values; join them the way they travel on the wire so the dump reads like the real request.
        foreach (var (name, values) in headers)
            sb.Append(name).Append(": ").AppendLine(JoinHeaderValues(name, values));
        if (contentHeaders is not null)
            foreach (var (name, values) in contentHeaders)
                sb.Append(name).Append(": ").AppendLine(JoinHeaderValues(name, values));
    }

    // User-Agent / Server tokens are space-separated on the wire; every other header is comma-separated.
    private static string JoinHeaderValues(string name, IEnumerable<string> values) =>
        name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Server", StringComparison.OrdinalIgnoreCase)
            ? string.Join(' ', values)
            : string.Join(", ", values);

    private static void AppendBody(StringBuilder sb, BodyCapture body)
    {
        if (body.IsEmpty) { sb.AppendLine("(no body)"); return; }
        if (body.IsText) { sb.AppendLine(body.Text); return; }
        sb.Append("(binary body, ").Append(body.ByteLength).AppendLine(" bytes, base64)");
        sb.AppendLine(body.Base64);
    }

    private static async Task<BodyCapture> BufferAndRestoreAsync(HttpContent? content, CancellationToken ct)
    {
        if (content is null) return BodyCapture.Empty;

        var bytes = await content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0) return BodyCapture.Empty;

        // Rebuild the content from the buffered bytes so the downstream send still has a readable
        // stream, preserving the original content headers.
        var replacement = new ByteArrayContent(bytes);
        foreach (var (name, values) in content.Headers)
            replacement.Headers.TryAddWithoutValidation(name, values);

        var mediaType = content.Headers.ContentType?.MediaType;
        var isText = IsTextMediaType(mediaType);
        return new BodyCapture(
            bytes.Length,
            isText ? SafeDecode(bytes) : null,
            isText ? null : Convert.ToBase64String(bytes),
            replacement);
    }

    private static bool IsTextMediaType(string? mediaType) =>
        mediaType is null
        || mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

    private static string SafeDecode(byte[] bytes)
    {
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return $"(undecodable {bytes.Length} bytes)"; }
    }

    private static string Pad(int seq) => seq.ToString("D4");

    private static string FileName(int seq, HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? "unknown";
        var slug = path.Trim('/').Replace('/', '-');
        foreach (var c in Path.GetInvalidFileNameChars())
            slug = slug.Replace(c, '_');
        if (slug.Length > 80) slug = slug[..80];
        if (slug.Length == 0) slug = "root";
        return $"{Pad(seq)}_{request.Method.Method}_{slug}.http";
    }

    private sealed record BodyCapture(int ByteLength, string? Text, string? Base64, HttpContent? Replacement)
    {
        public static readonly BodyCapture Empty = new(0, null, null, null);
        public bool IsEmpty => ByteLength == 0;
        public bool IsText => Text is not null;
    }
}
