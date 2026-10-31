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

**2. Create a `.env` next to it** (fill in email, password, and a strong random API key):

```
Security__ApiKey=your-strong-random-key
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

**3. Put a `.env` in the same folder** (fill in email, password, and a strong random API key):

```
Security__ApiKey=your-strong-random-key
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

## Running tests

Tests hit the real Listonic API, so credentials are required. They are never stored in the repo.

### Local

```bash
dotnet user-secrets --project src/ListonicApiClient.Tests set "Listonic:Email" "your@email.com"
dotnet user-secrets --project src/ListonicApiClient.Tests set "Listonic:Password" "yourpassword"

dotnet test src/ListonicMcp.slnx
```

### GitHub Actions

Add two repository secrets:

| Secret name | Value |
|---|---|
| `LISTONIC__EMAIL` | test account email |
| `LISTONIC__PASSWORD` | test account password |

Then in your workflow:

```yaml
- run: dotnet test src/ListonicMcp.slnx
  env:
    LISTONIC__EMAIL: ${{ secrets.LISTONIC__EMAIL }}
    LISTONIC__PASSWORD: ${{ secrets.LISTONIC__PASSWORD }}
```

> Tests create lists prefixed with `[test] ` and clean them up automatically after each run.
