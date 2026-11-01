# listonic-mcp

Unofficial MCP server for [Listonic](https://listonic.com) grocery lists, built on .NET 10 and ASP.NET Core.

## MCP tools

| Tool | Description |
|---|---|
| `get_lists` | All shopping lists with their IDs |
| `get_items` | Items on a list — `[x]` already have, `[ ]` still to buy |
| `add_items` | Add one or many items in a single call |
| `edit_items` | Check/uncheck, rename, or change quantity (batched) |
| `delete_items` | Permanently remove items |
| `rename_list` | Rename a shopping list |
| `delete_list` | Move a list to trash (soft delete) |

## Running the MCP server

### Docker (production / NAS)

The image is published to GHCR on every release — no build or clone required.

**1. Create a `docker-compose.yml`:**

```yaml
services:
  listonic-mcp:
    container_name: listonic-mcp
    image: ghcr.io/kusnierz-maciej/listonic-mcp:latest   # or pin a version, e.g. :v1.3.2
    ports:
      - "9011:8080"
    env_file:
      - .env
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    restart: unless-stopped
```

**2. Create a `.env` next to it:**

```
# Required: gates access to the server. Any long random string works — a GUID is a good choice.
# Generate one with:  uuidgen   (macOS/Linux)   or   [guid]::NewGuid()  (PowerShell)
Security__ApiKey=00000000-0000-0000-0000-000000000000

# Optional: a default Listonic account, used only when a client does not send its
# own Authorization: Basic header. Omit both lines if every client passes credentials.
Listonic__Email=your@email.com
Listonic__Password=yourpassword
```

> If your password contains `$`, escape each one as `$$` (Docker Compose variable interpolation). Example: `p@$w0rd!` → `p@$$w0rd!`

**3. Start it:**

```bash
docker compose up -d
# Exposed on host port 9011
```

**4. Connect Claude Code:**

```json
{
  "mcpServers": {
    "listonic": {
      "type": "http",
      "url": "http://your-host:9011/mcp",
      "headers": {
        "X-Api-Key": "your-api-key-from-.env",
        "Authorization": "Basic <base64(email:password)>"
      }
    }
  }
}
```

`X-Api-Key` must match `Security__ApiKey` in your `.env` file.

`Authorization: Basic` carries Listonic credentials encoded as `base64(email:password)`. If omitted, the server falls back to `Listonic__Email`/`Listonic__Password` from `.env`. This means multiple users can share one server instance, each passing their own credentials — no server restart required to add a new account.

### Synology NAS (Container Manager)

The published image is `linux/amd64` — use an Intel/AMD Synology model.

**1. Create a folder** for the project, e.g. via File Station: `/docker/listonic-mcp`.

**2. Put a `docker-compose.yml` in that folder:**

```yaml
services:
  listonic-mcp:
    container_name: listonic-mcp
    image: ghcr.io/kusnierz-maciej/listonic-mcp:latest   # or pin a version, e.g. :v1.3.2
    ports:
      - "9011:8080"
    env_file:
      - .env
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    restart: unless-stopped
```

**3. Put a `.env` in the same folder:**

```
# Required: gates access to the server. Any long random string works — a GUID is a good choice.
# Generate one with:  uuidgen   (macOS/Linux)   or   [guid]::NewGuid()  (PowerShell)
Security__ApiKey=00000000-0000-0000-0000-000000000000

# Optional: a default Listonic account, used only when a client does not send its
# own Authorization: Basic header. Omit both lines if every client passes credentials.
Listonic__Email=your@email.com
Listonic__Password=yourpassword
```

**4. In Container Manager → Project → Create:**
- **Project name:** `listonic-mcp`
- **Path:** select the `/docker/listonic-mcp` folder you created.
- **Source:** Container Manager detects the existing `docker-compose.yml` in that folder — use it.
- Click through to build/run. It pulls the image from GHCR and starts the container on host port `9011`.

### Connecting from claude.ai

1. Go to **claude.ai** → Settings → **Connectors** → **Add custom connector**
2. Enter your server URL: `http://your-host:9011/mcp`
3. Under **Authentication**, select **No sign-in**
4. Under **Request headers**, add:
   - `X-Api-Key`: `your-api-key`
   - `Authorization`: `Basic <base64(email:password)>`
5. Click **Add**

The server must be publicly reachable from the internet. If it's on a local NAS, expose it via a tunnel such as [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) or ngrok.

> **After updating the server to a new version**, go to **Settings → Connectors → Listonic (Custom MCP) → Refresh tool list** so Claude picks up any changed tool descriptions.

### Local development

Credentials can be supplied either via user secrets (server-side fallback) or per-request headers. For local dev the simplest approach is user secrets:

```bash
dotnet user-secrets --project src/ListonicMcp set "Listonic:Email" "your@email.com"
dotnet user-secrets --project src/ListonicMcp set "Listonic:Password" "yourpassword"

dotnet run --project src/ListonicMcp
# Server starts at http://localhost:5100
```

No API key is required in development — `Security:ApiKey` is unset so the middleware lets all requests through.

Connect Claude Code by adding to your MCP config (`.mcp.json` in the project root or `~/.claude/mcp.json`):

```json
{
  "mcpServers": {
    "listonic": {
      "type": "http",
      "url": "http://localhost:5100/mcp"
    }
  }
}
```

Alternatively, skip user secrets entirely and pass credentials as a Basic auth header (useful when testing multi-account):

```json
{
  "mcpServers": {
    "listonic": {
      "type": "http",
      "url": "http://localhost:5100/mcp",
      "headers": {
        "Authorization": "Basic <base64(email:password)>"
      }
    }
  }
}
```

## Stdio mode

For MCP clients that launch the server as a subprocess, run it with `--stdio`. The server then talks MCP over stdin/stdout instead of HTTP, and logs go to stderr. There is no HTTP endpoint, so `Security__ApiKey` is not used. The Listonic account is read from environment variables:

```bash
Listonic__Email=your@email.com Listonic__Password=yourpassword \
  dotnet run --project src/ListonicMcp -- --stdio
```

Example client configuration:

```json
{
  "mcpServers": {
    "listonic": {
      "command": "dotnet",
      "args": ["run", "--project", "src/ListonicMcp", "--", "--stdio"],
      "env": {
        "Listonic__Email": "your@email.com",
        "Listonic__Password": "yourpassword"
      }
    }
  }
}
```

## Running tests

The tests run against the live Listonic API to detect whether the unofficial API has changed in a way that would break this server. They hit the real API, so credentials are required. They are never stored in the repo.

```bash
dotnet user-secrets --project src/ListonicApiClient.Tests set "Listonic:Email" "your@email.com"
dotnet user-secrets --project src/ListonicApiClient.Tests set "Listonic:Password" "yourpassword"

dotnet test src/ListonicMcp.slnx
```

> Tests create lists prefixed with `[test] ` and clean them up automatically after each run.
