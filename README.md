# listonic-mcp

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
