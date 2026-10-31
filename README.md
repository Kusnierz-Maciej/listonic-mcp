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

**1. Pull the container image from GHCR:**

```bash
docker pull ghcr.io/kusnierz-maciej/listonic-mcp:latest
# or pin to a specific version
docker pull ghcr.io/kusnierz-maciej/listonic-mcp:v1.3.2
```

Images are published automatically on every release via GitHub Actions.

<details>
<summary>Build the image locally instead</summary>

```bash
dotnet publish src/ListonicMcp/ListonicMcp.csproj \
  /t:PublishContainer \
  -c Release \
  -r linux-x64 \
  --no-self-contained
# Produces: listonic-mcp:v1.3.2 and listonic-mcp:latest
```

To transfer to a NAS without a registry:

```bash
docker save listonic-mcp:latest | gzip > listonic-mcp-v1.3.2.tar.gz
scp listonic-mcp-v1.3.2.tar.gz user@nas:/path/to/deploy/
docker load < listonic-mcp-v1.3.2.tar.gz
```
</details>

**2. Configure credentials and API key:**

```bash
cp src/ListonicMcp/docker/.env.example src/ListonicMcp/docker/.env
# Edit .env — fill in email, password, and a strong random API key
# If your password contains $, escape each one as $$  (Docker Compose variable interpolation)
# Example: p@$$w0rd! → p@$$$$w0rd!
```

**3. Start the container:**

```bash
docker compose \
  -f src/ListonicMcp/docker/docker-compose.prod.yml \
  --env-file src/ListonicMcp/docker/.env \
  up -d
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

### Synology NAS (Docker Compose from GHCR, no login)

Works on Intel/AMD Synology models. The published image is `linux/amd64`.

**Prerequisite — make the GHCR package public** (one-time, so the NAS can pull anonymously):
[Package settings](https://github.com/users/Kusnierz-Maciej/packages/container/listonic-mcp/settings) → **Danger Zone → Change visibility → Public**. Once public it stays public for all future pushes.

**1. Create a deploy directory on the NAS:**

```bash
mkdir -p /volume1/docker/listonic-mcp && cd /volume1/docker/listonic-mcp
```

**2. Create `docker-compose.yml`:**

```yaml
services:
  listonic-mcp:
    container_name: listonic-mcp
    image: ghcr.io/kusnierz-maciej/listonic-mcp:latest
    ports:
      - "9011:8080"
    env_file:
      - .env
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    restart: unless-stopped
```

**3. Create `.env`** (fill in email, password, and a strong random API key):

```
Security__ApiKey=your-strong-random-key
Listonic__Email=your@email.com
Listonic__Password=yourpassword
```

**4. Start it:**

```bash
sudo docker compose up -d
```

`sudo` is required for DSM's Docker socket — it is not a registry login. No `docker login` is needed as long as the package is public.

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
