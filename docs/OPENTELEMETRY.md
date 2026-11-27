# OpenTelemetry

The HTTP host exports traces and logs via OTLP when `Telemetry:Enabled` is `true`.
Telemetry is **off by default** in all environments — enable it explicitly when needed. (Stdio mode
does not export telemetry.)

Without telemetry, console logs still carry a `RequestId`/`TraceId` scope on every request and the
user's email on `/mcp` requests, so a single request's lines can be grouped in `docker logs` or Loki.

## Local testing with Aspire Dashboard

Microsoft's Aspire Dashboard is a good local OTLP viewer for .NET — structured logs with scopes,
traces with per-span timings, and both correlated. It runs standalone via the Aspire CLI.

```sh
brew install --cask microsoft/aspire/aspire   # once
aspire dashboard run
```

Run the app with telemetry on (the default endpoint is already `http://localhost:4317`):

```sh
Telemetry__Enabled=true dotnet run --project src/ListonicMcp
```

Open **http://localhost:18888** → Structured Logs or Traces → service `listonic-mcp`.

## Production

Set the env vars in your compose file (both compose files have them commented out):

```yaml
- Telemetry__Enabled=true
- Telemetry__OtlpEndpoint=http://alloy:4317
```

This sends logs and traces to e.g. an Alloy collector, which forwards logs to Loki and traces to Tempo.
The container must be able to resolve the collector's name: when Alloy runs in its own compose project, join that
project's shared network. `docker-compose.synology.yml` has this ready, commented out — uncomment both blocks:

```yaml
    networks:
      - default
      - observability          # created by the observability stack
...
networks:
  observability:
    external: true
```
The endpoint must be an absolute `http(s)://` URI — otherwise the server refuses to start.

Each trace is built from four span sources:

| Source | Spans |
|---|---|
| ASP.NET Core instrumentation | incoming request (root) — tagged `user.email`; unhandled exceptions recorded. Static icons and `/.well-known/*` are not traced. `/mcp` requests are named after their MCP message, e.g. `POST /mcp [tools/call get_items]`, `POST /mcp [tools/list]`, `POST /mcp [notifications/initialized]` — from the `Mcp-Method` / `Mcp-Name` headers when the client sends them (2026-07-28 Streamable HTTP), else from the parsed JSON-RPC message (`McpSpanNames`; an OTel processor re-applies the name after the instrumentation resets it to `{method} {route}`). The SSE `GET` and session `DELETE` keep `GET /mcp/` / `DELETE /mcp/`. |
| `Experimental.ModelContextProtocol` (MCP SDK) | one per JSON-RPC request, e.g. `tools/call get_items` — tagged `gen_ai.tool.name`, `mcp.method.name`, `mcp.session.id` |
| `System.Net.Http` (.NET built-in) | each outbound Listonic API call (query strings redacted, the .NET default) |
| `Listonic` (app) | `listonic.login` — only on a token-cache miss, so it shows when (and how often) the server really logs in. **Phase spans** inside tools, so the backend calls of each step group together: `listonic.items.add`, `listonic.items.update`, `listonic.items.lookup`, `listonic.items.delete`, `listonic.items.reread`, `listonic.verify_list_exists` |

**Business tags** on the tool span (`listonic.*`):

| Tag | Set by |
|---|---|
| `list.id`, `list.name` | every tool that targets a list (create_list: the new list) |
| `list.new_name` | rename_list |
| `lists.count`, `lists.shared`, `items.unchecked` | get_lists (also `lists.count` + `completion.matches` on prompt completions) |
| `items.count` | items requested (writes) / returned (get_items) |
| `items.unchecked`, `items.checked` | get_items |
| `items.names` | add/edit/delete: the item names (first 20) |
| `items.ids`, `items.affected` | add/edit/delete: ids actually written (first 20) and how many |
| `items.missing` | delete_items: ids not on the list (nothing deleted for them) |
| `items.ticked`, `items.unticked`, `items.renamed`, `items.quantity_changed` | edit_items: what kind of change |
| `outcome` | every tool |
| `client.name`, `client.version` | every tool call: the calling MCP client (from `_meta` `io.modelcontextprotocol/clientInfo`, else the initialize handshake) |
| `client.trace_context` | every tool call: `meta` (client sent `traceparent` in `params._meta`), `http` (HTTP `traceparent` header) or `none` |
| `client.meta_keys` | every tool call: the `_meta` key **names** the client sent (never values) |

Long lists are capped at 20 values (the count tag has the full size); no passwords, tokens or payloads.

**Outcomes** — failures the tool reports as a plain error message set span status **Error**:

| `listonic.outcome` | Error? |
|---|---|
| `ok`, `created`, `added`, `edited`, `deleted`, `renamed` | no |
| `not_found`, `forbidden`, `credentials_rejected`, `rate_limited`, `unavailable`, `rejected_request` | yes |

**One trace per chat turn.** If the client propagates W3C trace context — `traceparent` in `params._meta`
(SEP-414) or the HTTP `traceparent` header — the server continues the client's trace, so all tool calls of one turn
land in **one trace**. Whether a client does this shows up as `listonic.client.trace_context` (plus a log line per
call: `tools/call <tool> from <client> <version>: trace context <meta|http|none>; _meta keys [...]`). Clients that
don't (`none`) get one trace per tool call; group those by `user.email` and time.

**Observed clients** (2026-11-27, local test with this server):

| Client | Trace context | Result |
|---|---|---|
| Claude Code 2.1.332, default | `none` | one trace per tool call |
| Claude Code 2.1.332 with its own telemetry on (`CLAUDE_CODE_ENABLE_TELEMETRY=1` + an OTLP traces exporter) | `http` (W3C `traceparent` header) | **all tool calls of one prompt share one trace**, each under its own Claude Code span |
| claude.ai | not tested yet (deploy and check `listonic.client.trace_context`) | per public reports: no trace context, no conversation id |

Claude Code also sends `claudecode/toolUseId` in `_meta`: an id per tool call, not per conversation.

```
{ span.listonic.client.trace_context = "meta" }                   # calls that joined a client trace
{ span.listonic.client.name = "claude-code" }                     # per client (name as the client reports it)
```

A `401` from Listonic adds a `listonic.token_rejected_retrying` event to the tool span (explains the
duplicate HTTP span that follows the re-login).

Useful TraceQL:

```
{ span.gen_ai.tool.name = "add_items" }
{ span.listonic.list.name = "Zakupy" }
{ span.listonic.outcome = "credentials_rejected" }
{ span.user.email = "someone@example.com" }
{ status = error }
```

> **Personal data:** traces carry the user's email and list names (and logs carry item counts and
> list names). That is intended for a self-hosted stack — don't point `Telemetry:OtlpEndpoint` at a
> shared or third-party backend without revisiting this.

Telemetry off costs effectively nothing: with no listener, `StartActivity` returns `null` and every
tag call is a null check.
