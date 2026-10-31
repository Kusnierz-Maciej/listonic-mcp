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

## Running the MCP server

### Local development

Set credentials via user secrets (never stored in the repo):

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

### Docker (production / NAS)

**1. Build the container image** (no Dockerfile — the SDK builds it):

```bash
dotnet publish src/ListonicMcp/ListonicMcp.csproj \
  /t:PublishContainer \
  -c Release \
  -r linux-x64 \
  --no-self-contained
# Produces: listonic-mcp:latest and listonic-mcp:v1.0.0
```

**1b. (Optional) Export the image as a tarball for transfer to a NAS:**

```bash
# Save with version suffix
docker save listonic-mcp:latest | gzip > listonic-mcp-v1.0.0.tar.gz

# Copy to NAS
scp listonic-mcp-v1.0.0.tar.gz user@nas:/path/to/deploy/

# Load on NAS
docker load < listonic-mcp-v1.0.0.tar.gz
```

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
        "Authorization": "Bearer your-api-key-from-.env"
      }
    }
  }
}
```

The `Authorization: Bearer` value must match `Security__ApiKey` in your `.env` file.

### Connecting from claude.ai

1. Go to **claude.ai** → Settings → **Connectors** → **Add custom connector**
2. Enter your server URL: `http://your-host:9011/mcp`
3. Under **Authentication**, select **No sign-in**
4. Under **Request headers**, add:
   - Header name: `Authorization`
   - Value: `Bearer your-api-key`
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
