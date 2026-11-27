# listonic-mcp

## Git

- Pull from remote using fast-forward only (`pull.ff = only`); never create merge commits when syncing main.

## Configuration conventions

When adding or changing any setting:

1. **Default in `appsettings.json`** — every key has a sensible built-in default here. These files are
   build-time defaults, not something operators edit.
2. **Dev overrides in `appsettings.Development.json`** — only values that differ from the default.
3. **`appsettings.Production.json`** — only values that differ from the default; omit anything that matches.
4. **`docker/docker-compose.prod.yml`** (clean, generic) and **`docker/docker-compose.synology.yml`**
   (prod + human-readable `simple` console logging block) — operators configure the server here, via
   environment variables (`Section__Key`). Keep a commented reference block with the default values so
   every knob is visible in one place. Secrets go in `.env` (see `docker/.env.example`), never in compose.
5. **Document in README** — every operator-facing setting should be described.

## Debugging API / MCP issues — use Listonic.Sandbox

When diagnosing an API or MCP problem (unexpected status codes, odd response bodies, auth issues,
retry behaviour), use the **`src/Listonic.Sandbox`** project. It drives the real API client (same
handler chain as the app, rate limit included) and dumps **every HTTP exchange in full** — request
line, all headers (including the bearer token), request body (including the login password),
response status, headers and body — so wire behaviour can be inspected instead of guessed.

```sh
dotnet run --project src/Listonic.Sandbox                         # list scenarios
dotnet run --project src/Listonic.Sandbox -- dump-lists           # run one
dotnet run --project src/Listonic.Sandbox -- dump-items "Zakupy"  # scenario with an argument
```

Each run writes `src/Listonic.Sandbox/out/<UTC-timestamp>/` containing one `NNNN_METHOD_path.http`
file per exchange, plus `run.log` (free-form notes and tool output) and `meta.txt`. The run prints the
dump directory at the end — read the `.http` files there to analyse the issue.

- Credentials: the live tests' user-secrets store (`Listonic:Email` / `Listonic:Password`, set with
  `dotnet user-secrets --project src/ListonicApiClient.Tests set …`) or `Listonic__Email` /
  `Listonic__Password` env vars.
- Add a probe by implementing `IScenario` (in `Scenarios/`) and registering it in `ScenarioRegistry`.
  Each scenario carries a WHAT/WHY/FOUND doc header. Read-only: `dump-lists`, `dump-items`,
  `dump-categories`, `tools-smoke`. `login-probe` touches no data (run sparingly). `roundtrip` and
  `category-assign` write, but only to a `[test] …` list they create and delete.
- The dump handler is attached innermost on both the API and login clients via the optional
  `configureClient` hook on `AddListonicApiClient`, so it captures each physical send — including the
  auth handler's 401 retry re-send.
- Dumps contain **live tokens and passwords**; `out/` is gitignored and must never be committed.

### Safety

This is a real account. Keep casual probing read-only; write tests only on lists prefixed `[test] `
that the scenario/test creates and deletes itself.

## Checking the MCP server — MCP Inspector CLI

Use the official MCP Inspector in CLI mode (`npx -y @modelcontextprotocol/inspector --cli …`, needs Node) to
check the server as a client sees it: tool list, schemas, annotations, real tool calls. Run it after any
change to tools, descriptions, parameters or auth.

Start the HTTP host with dev settings (the key and secret are throwaway dev values):

```sh
Security__AllowedApiKeys__0=dev-key \
Security__TokenSecret=dev-token-secret-at-least-32-chars-long \
ASPNETCORE_URLS=http://127.0.0.1:5199 dotnet run --project src/ListonicMcp
```

Authenticate with the header path; build the Basic value from the live tests' user-secrets
(`Listonic:Email` / `Listonic:Password` in `src/ListonicApiClient.Tests`) without printing it:

```sh
LB=$(dotnet user-secrets list --project src/ListonicApiClient.Tests | python3 -c 'import sys,base64
d=dict(l.rstrip("\n").split(" = ",1) for l in sys.stdin if " = " in l)
print(base64.b64encode((d["Listonic:Email"]+":"+d["Listonic:Password"]).encode()).decode())')
I() { npx -y @modelcontextprotocol/inspector --cli http://127.0.0.1:5199/mcp \
        --header "X-Api-Key: dev-key" --header "Authorization: Basic $LB" "$@"; }

I --method tools/list --strict          # schema portability: full report on stderr, exit 6 on errors
I --method tools/call --tool-name get_lists
I --method tools/call --tool-name get_items --tool-args-json '{"listId":"123","listName":"Zakupy"}'
```

- `--strict` must stay clean (empty stderr, exit 0) — `ToolSchemaTests` guards the same in unit tests.
- `--tool-arg k=v` sends numeric-looking values as JSON **numbers**; the server accepts them for string
  parameters, but use `--tool-args-json` when you need exact types.
- A tool error shows as `"isError": true` with the message in `content[0].text` and a `tool_is_error` line;
  an opaque "An error occurred invoking '<tool>'" with nothing after the colon means an exception escaped
  `ToolErrors.GuardAsync` or argument binding — check the server log.
- stdio: `npx -y @modelcontextprotocol/inspector --cli -e Listonic__Email=… -e Listonic__Password=… dotnet run --project src/ListonicMcp -- --stdio --method tools/list`.
- Same safety rules as the sandbox: read-only calls freely; write calls only against a `[test] ` list.
- For the OAuth sign-in page and other UI, use the Inspector web UI (`npx @modelcontextprotocol/inspector`,
  transport Streamable HTTP) — see README.

## Live API tests = the API contract

The live tests (`[Category("LiveApi")]` in `src/ListonicApiClient.Tests`) are the contract check against
the unofficial Listonic API; CI runs them nightly against the latest release.

- **Every API behaviour the code relies on needs a live test** — status codes (including silent `200`s
  the tools work around), payload shapes and encodings. When code starts depending on a new behaviour,
  add the test in `LiveApiContractTests` and a row in the contract table in `docs/API-NOTES.md`.
  Verify the behaviour with the sandbox first.
- **Live tests never run in parallel**: every live test class carries `[NotInParallel(LiveApi.Serial)]`
  (one real account, one rate limit, shared `[test] ` cleanup).
- Write only to lists the test creates, named with the `[test] ` prefix, and delete them in `finally`.
- Every live test class also has `[Before(HookType.Class)]` → `LiveApi.SweepOnceAtStartAsync()` (removes
  `[test] ` lists left by a crashed previous run; runs once per test run, whichever class starts first)
  and `[After(HookType.Class)]` → `LiveApi.SweepTestListsAsync()` (normal cleanup).
