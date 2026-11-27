# listonic-mcp

Unofficial MCP server for [Listonic](https://listonic.com) grocery lists, built on .NET 10 and ASP.NET Core.
Not affiliated with or endorsed by Listonic.

## Listonic API compatibility
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/Kusnierz-Maciej/listonic-mcp/verify-api-contract.yml?branch=main&label=Listonic%20api%20compatibility)  
![Dynamic JSON Badge](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fapi.github.com%2Frepos%2FKusnierz-Maciej%2Flistonic-mcp%2Factions%2Fworkflows%2F403783468%2Fruns%3Fstatus%3Dcompleted%26per_page%3D1&query=%24.workflow_runs%5B0%5D.run_started_at&label=Listonic%20api%20compatibility&cacheSeconds=300)    

## MCP tools

| Tool | Description |
|---|---|
| `get_lists` | All shopping lists (own and shared) with their IDs |
| `get_items` | Items on a list — `[ ]` still to buy first, then `[x]` bought |
| `add_items` | Add one or many items in a single call |
| `edit_items` | Tick off / untick, rename, or change quantity (batched) |
| `delete_items` | Permanently remove items |
| `create_list` | Create a new, empty list |
| `rename_list` | Rename a list |
| `delete_list` | Move a list to the Listonic trash |

Reads are marked read-only and deletes destructive, so clients can auto-approve the former and ask before
the latter.

**Prompts** — shortcuts you pick yourself (slash commands in Claude Code, the "+" menu in claude.ai); they
never run on their own, and the `list` argument autocompletes from your lists:

| Prompt | Does |
|---|---|
| `what_to_buy` | Only the items still to buy |
| `quick_add` | Add free-text items (`2 l mleka, chleb`) without duplicates |
| `bought` | Tick off what you bought (or everything, after confirming) |
| `clear_bought` | Delete the ticked-off items, after confirming |
| `share_as_text` | The list as plain text for a chat app |

They are defaults: if you have your own instructions or skills for Listonic, those should win (the prompt
text asks the model to prefer them — a hint, not enforced). Turn prompts off with `Prompts__Enabled=false`. Details, inputs and example outputs: [`docs/MCP-TOOLS.md`](docs/MCP-TOOLS.md). Notes on the
underlying API: [`docs/API-NOTES.md`](docs/API-NOTES.md).

## Transports & authentication

| Transport | Used by | How a user signs in | Listonic account |
|---|---|---|---|
| **HTTP** (default) — `…/mcp` | remote clients (claude.ai, Claude Code, ChatGPT…) | **OAuth 2.1** sign-in page (recommended), or legacy `X-Api-Key` + `Authorization: Basic` headers | each user's own |
| **stdio** (`--stdio`) | clients that start the server as a subprocess | none — local process | one, from `Listonic__Email` / `Listonic__Password` |

- **OAuth** — the client discovers the endpoints and opens a sign-in page where the user enters an
  **MCP API key** (from `Security__AllowedApiKeys`) and their **Listonic email + password**. The
  credentials are checked against Listonic, then kept only inside an encrypted token held by the client —
  never stored on the server. Sessions refresh silently (by default access tokens last an hour, refresh
  tokens a year); removing a key and restarting ends every session created with it.
- **Headers (legacy)** — `X-Api-Key: <key>` plus `Authorization: Basic base64(email:password)`, for clients
  that support custom headers. Still supported, but prefer OAuth: with headers your Listonic password sits in
  the client's config.
- There is **no server-side default account** in HTTP mode.
- **No API key at all:** set `Security__RequireMcpApiKey=false` for a trusted private deployment —
  Listonic email + password are still required.
- Sign-in uses a Listonic **email + password**. An account that only signs in with Google/Facebook has no
  Listonic password, so it most likely can't be used here (not tested).
- Every setting is validated at startup; on bad configuration the server exits with code 1 and lists
  each problem.

## Running with Docker

The image is published to GHCR on every release for `linux/amd64` and `linux/arm64`.

[![Docker images](https://img.shields.io/badge/Docker_images-latest-blue?logo=docker)](https://github.com/Kusnierz-Maciej/listonic-mcp/pkgs/container/listonic-mcp?tag=latest)

    docker pull ghcr.io/kusnierz-maciej/listonic-mcp:latest

Each release is also tagged with its version (e.g. `v1.11.0`) — see the
[releases](https://github.com/Kusnierz-Maciej/listonic-mcp/releases) page.

1. Copy a compose file from [`src/ListonicMcp/docker/`](src/ListonicMcp/docker/) as `docker-compose.yml`:
   - [`docker-compose.prod.yml`](src/ListonicMcp/docker/docker-compose.prod.yml) — generic, any host.
   - [`docker-compose.synology.yml`](src/ListonicMcp/docker/docker-compose.synology.yml) — the same, with
     readable single-line logs for Synology Container Manager (Project → Create → pick the folder).

   Each lists every non-secret setting with its default value (mostly commented out) — edit or uncomment to change.
2. Copy [`.env.example`](src/ListonicMcp/docker/.env.example) next to it as `.env` and set the
   secrets: at least one `Security__AllowedApiKeys__N` and a long, random, **stable**
   `Security__TokenSecret` (`openssl rand -base64 48`; changing it signs everyone out). Escape `$` as `$$`.
3. `docker compose up -d` — listening on host port `9011`.

The server must be reachable over **HTTPS** for remote clients — e.g. through
[Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/),
ngrok, or a reverse proxy. The proxy must forward `X-Forwarded-Proto` / `X-Forwarded-Host` (most do by
default); if the OAuth metadata at `/.well-known/oauth-authorization-server` shows `http://` URLs,
claude.ai's sign-in will fail.

## Connecting a client

**claude.ai** → Settings → Connectors → **Add custom connector** → URL `https://your-host/mcp` → **Add**,
then sign in on the page that opens (API key + Listonic email/password).

**Claude Code** (`.mcp.json` or `~/.claude.json`):

```json
{ "mcpServers": { "listonic": { "type": "http", "url": "https://your-host/mcp" } } }
```

or, with the legacy headers instead of OAuth:

```json
{
  "mcpServers": {
    "listonic": {
      "type": "http",
      "url": "https://your-host/mcp",
      "headers": { "X-Api-Key": "<key>", "Authorization": "Basic <base64(email:password)>" }
    }
  }
}
```

> After updating the server, refresh the connector's tool list (claude.ai: Settings → Connectors →
> Listonic → Refresh tool list) so the client picks up changed tool descriptions.

**stdio:**

```json
{
  "mcpServers": {
    "listonic": {
      "command": "dotnet",
      "args": ["run", "--project", "src/ListonicMcp", "--", "--stdio"],
      "env": { "Listonic__Email": "your@email.com", "Listonic__Password": "yourpassword" }
    }
  }
}
```

## Settings

Set in the compose file (or `.env` for secrets) using the `Section__Key` convention.

| Setting | Default | |
|---|---|---|
| `Security__AllowedApiKeys__N` | — | MCP API keys (secret). At least one while keys are required. |
| `Security__TokenSecret` | — | Required (secret). ≥ 32 chars, stable; secures OAuth tokens. |
| `Security__RequireMcpApiKey` | `true` | `false` drops the API-key gate. |
| `Prompts__Enabled` | `true` | `false` removes the MCP prompts (and list-name completions). |
| `RateLimit__TokenLimit` / `RateLimit__ReplenishmentPeriod` | `15` / `00:00:00.5` | Client-side limit on Listonic calls: a burst, then one call per period (extra calls wait). Loosen at your own risk. |
| `OAuth__AuthorizationCodeLifetime` / `__AccessTokenLifetime` / `__RefreshTokenLifetime` | `00:01:00` / `01:00:00` / `365.00:00:00` | Token lifetimes; the refresh lifetime is how long a client may stay idle before signing in again. |
| `Telemetry__Enabled` | `false` | OpenTelemetry export of traces + logs — see [`docs/OPENTELEMETRY.md`](docs/OPENTELEMETRY.md). |
| `Telemetry__OtlpEndpoint` | `http://localhost:4317` | Absolute http(s) OTLP endpoint. |
| `Logging__…` | Information, single-line console | Standard ASP.NET Core logging settings. |
| `Listonic__Email` / `Listonic__Password` | — | stdio mode only. |

Calls to Listonic are rate-limited client-side (by default bursts of 15, then 2 per second; extra calls wait).

## Local development

```bash
dotnet user-secrets --project src/ListonicMcp set "Security:AllowedApiKeys:0" "dev-key"
dotnet user-secrets --project src/ListonicMcp set "Security:TokenSecret" "dev-token-secret-at-least-32-chars-long"
dotnet run --project src/ListonicMcp      # http://localhost:5100/mcp
```

Test it with the official [MCP Inspector](https://github.com/modelcontextprotocol/inspector)
(`npx @modelcontextprotocol/inspector`, needs Node): transport **Streamable HTTP**, URL
`http://localhost:5100/mcp`, **Connect** — the sign-in page opens; use `dev-key` + your Listonic account.
Claude Code works the same way, or send the headers. For stdio:
`npx @modelcontextprotocol/inspector -e Listonic__Email=… -e Listonic__Password=… dotnet run --project src/ListonicMcp -- --stdio`.

To see exactly what goes over the wire, use the diagnostic sandbox — see `CLAUDE.md`
(`dotnet run --project src/Listonic.Sandbox`).

## Tests

```bash
# Unit + offline tests — no account, no network (what CI runs)
dotnet test --solution src/ListonicMcp.slnx --treenode-filter '/**[Category!=LiveApi]'

# Live API tests — real account (see "Live tests" below)
dotnet user-secrets --project src/ListonicApiClient.Tests set "Listonic:Email" "your@email.com"
dotnet user-secrets --project src/ListonicApiClient.Tests set "Listonic:Password" "yourpassword"
dotnet test --project src/ListonicApiClient.Tests --treenode-filter '/**[Category=LiveApi]'
```

Credentials can also come from `Listonic__Email` / `Listonic__Password` environment variables.

**Live tests** are the contract check for the unofficial Listonic API: every API behaviour the server
relies on has one (mapped to the code in the table in [`docs/API-NOTES.md`](docs/API-NOTES.md#contracts-guarded-by-live-tests)).
They run nightly in CI against the latest release, and an issue is opened when they fail. They:

- run **one at a time** (never in parallel) — one real account, one rate limit;
- write only to lists they create, named `[test] …`, and delete them;
- delete every `[test] ` list once when the run starts (so a crashed or cancelled run is cleaned up by
  the next one) and after each test class.

Pull requests run only the unit + offline tests.
