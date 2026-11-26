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
