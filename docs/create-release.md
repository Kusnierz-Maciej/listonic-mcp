# Creating a release

How to cut a new release of listonic-mcp. The container image is published to
GHCR automatically by the [`Publish Docker image to GHCR`](../.github/workflows/docker-publish.yml)
workflow when a `v*` tag is pushed.

## Prerequisites

- A clean working tree on `main`, in sync with `origin/main`.
- A GPG signing key configured (`git config user.signingkey`) — tags are signed.
- `gh` CLI authenticated with the repo.

## Steps

### 1. Bump the version

The image tag is derived from `<Version>` in the project file
(`ContainerImageTags` is set to `v$(Version);latest`), so the version must be
bumped **before** tagging.

Edit `src/ListonicMcp/ListonicMcp.csproj`:

```xml
<Version>1.3.3</Version>
```

Commit and push:

```bash
git add src/ListonicMcp/ListonicMcp.csproj
git commit -m "chore: bump version to 1.3.3"
git push
```

### 2. Tag and push

Create a **signed, annotated** tag on the version-bump commit and push it:

```bash
git tag -s v1.3.3 -m "v1.3.3" HEAD
git push origin v1.3.3
```

Pushing the tag triggers the GHCR publish workflow automatically. The run is
named `Build & Publish v1.3.3`.

> **Keep tags on the main line.** Always tag a commit that is reachable from
> `main` (normally `HEAD`). Do not rewrite a published tag to point at an
> off-branch commit — GitHub's "unreleased commits" indicator and tag-compare
> tooling expect every tag to be an ancestor of `main`, and a diverged tag
> produces confusing counters. Verify with:
>
> ```bash
> git merge-base --is-ancestor v1.3.3^{commit} main && echo "on main ✓"
> ```

### 3. Wait for the image build and verify

The workflow builds and pushes two tags to GHCR: the version and `latest`.

```bash
# find the run and watch it — exits non-zero if it fails
gh run list --workflow=docker-publish.yml --limit 1
gh run watch <run-id> --exit-status
```

Confirm the tags landed:

```bash
TOKEN=$(curl -s "https://ghcr.io/token?scope=repository:kusnierz-maciej/listonic-mcp:pull" \
  | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://ghcr.io/v2/kusnierz-maciej/listonic-mcp/tags/list"
```

You can also trigger a build manually without a new tag:

```bash
gh workflow run docker-publish.yml -f tag=v1.3.3
```

Package page: <https://github.com/Kusnierz-Maciej/listonic-mcp/pkgs/container/listonic-mcp>

The package must be **public** for anonymous pulls (one-time setting; it stays
public for all future pushes). Check at the package settings page under
*Danger Zone → Change visibility*.

### 4. Create the GitHub release

Only after the GHCR workflow is green and both tags are confirmed present.

```bash
gh release create v1.3.3 --title "v1.3.3" --notes "$(cat <<'EOF'
## What's changed

- ...

## Image

### Release v1.3.3

[![Docker images](https://img.shields.io/badge/Docker_images-v1.3.3-blue?logo=docker)](https://github.com/Kusnierz-Maciej/listonic-mcp/pkgs/container/listonic-mcp?tag=v1.3.3)

    docker pull ghcr.io/kusnierz-maciej/listonic-mcp:v1.3.3

### Latest

[![Docker images](https://img.shields.io/badge/Docker_images-latest-blue?logo=docker)](https://github.com/Kusnierz-Maciej/listonic-mcp/pkgs/container/listonic-mcp?tag=latest)

    docker pull ghcr.io/kusnierz-maciej/listonic-mcp:latest
EOF
)"
```

## Checklist

- [ ] `<Version>` bumped in `ListonicMcp.csproj`
- [ ] Version-bump commit pushed to `main`
- [ ] Signed `v<version>` tag pushed, and it is an ancestor of `main`
- [ ] GHCR workflow green; `v<version>` and `latest` tags present
- [ ] GitHub release created with notes
