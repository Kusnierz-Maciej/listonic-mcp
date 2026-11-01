---
name: create-release
description: Cut a new listonic-mcp release — bump the version in the csproj, push a signed v* tag, verify the GHCR image build, and create the GitHub release. Use when the user asks to release, tag, publish, or ship a new version.
disable-model-invocation: true
argument-hint: "[version, e.g. 1.9.2]"
---

# Creating a release

Cut a new release of listonic-mcp. The container image is published to GHCR
automatically by the [`Publish Docker image to GHCR`](../../../.github/workflows/docker-publish.yml)
workflow when a `v*` tag is pushed.

## Versions

The target version is `$ARGUMENTS`.

**If none was given**, find the previous release with
`git describe --tags --abbrev=0`, look at `git log <prev>..HEAD --oneline`, and
propose the next version:

- **patch** — fixes, CI, docs, refactors only
- **minor** — any `feat:` commit
- **major** — breaking changes

Always confirm the chosen version with the user.

**Normalize the version.** The user may or may not type a leading `v`
(`1.9.2` or `v1.9.2`). Strip any leading `v`/`V` to get the bare version, then
derive both forms:

- `<version>` (bare, `1.9.2`) — used for `<Version>` in the csproj and the commit message.
- `<tag>` (`v`-prefixed, `v1.9.2`) — used for the git tag, release title,
  workflow run name and image tag. Always add the `v`, even if the user omitted it.

Substitute `<version>` and `<tag>` in every command below.

## Confirmation gates

Pushing, tagging and releasing are outward-facing and hard to undo. Show the
exact commands and get the user's go-ahead before each of: the `git push` in
step 1, the tag push in step 2, and `gh release create` in step 4.

## Steps

### 0. Preflight

Run these and **stop and report** if any fails:

```bash
git branch --show-current                  # must be: main
git status --porcelain                     # must be empty (clean tree)
git fetch origin && git rev-list --left-right --count origin/main...HEAD   # must be: 0	0
git config user.signingkey                 # must print a key — tags are signed
gh auth status                             # must be authenticated
git tag -l <tag>                           # must be empty (tag doesn't exist yet)
```

### 1. Bump the version

The image tag is derived from `<Version>` in the project file
(`ContainerImageTags` is `v$(Version);latest`), so the version must be bumped
**before** tagging.

Edit `src/ListonicMcp/ListonicMcp.csproj`:

```xml
<Version><version></Version>
```

Commit and push (after confirmation):

```bash
git add src/ListonicMcp/ListonicMcp.csproj
git commit -m "chore: bump version to <version>"
git push
```

### 2. Tag and push

Create a **signed, annotated** tag on the version-bump commit and push it
(after confirmation):

```bash
git tag -s <tag> -m "<tag>" HEAD
git push origin <tag>
```

Pushing the tag triggers the GHCR publish workflow. The run is named
`Build & Publish <tag>`.

> **Keep tags on the main line.** Always tag a commit that is reachable from
> `main` (normally `HEAD`). Do not rewrite a published tag to point at an
> off-branch commit — GitHub's "unreleased commits" indicator and tag-compare
> tooling expect every tag to be an ancestor of `main`. Verify with:
>
> ```bash
> git merge-base --is-ancestor <tag>^{commit} main && echo "on main ✓"
> ```

Side effect: the scheduled `Verify Listonic API contract compatibility`
workflow tests the latest `v*` tag, so from now on it will test `<tag>`.

### 3. Wait for the image build and verify

The workflow builds and pushes two tags to GHCR: `<tag>` and `latest`.

Find the run — its `displayTitle` must be `Build & Publish <tag>`, otherwise
the run hasn't appeared yet (retry after a few seconds) — and watch it:

```bash
gh run list --workflow=docker-publish.yml --limit 1 --json databaseId,displayTitle,status
gh run watch <databaseId> --exit-status    # exits non-zero if the run fails
```

Confirm both tags landed:

```bash
TOKEN=$(curl -s "https://ghcr.io/token?scope=repository:kusnierz-maciej/listonic-mcp:pull" \
  | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://ghcr.io/v2/kusnierz-maciej/listonic-mcp/tags/list"
```

To rebuild without a new tag: `gh workflow run docker-publish.yml -f tag=<tag>`.

Package page: <https://github.com/Kusnierz-Maciej/listonic-mcp/pkgs/container/listonic-mcp>
(it must stay **public** for anonymous pulls — a one-time setting under
*Danger Zone → Change visibility*).

### 4. Create the GitHub release

Only after the workflow is green and both tags are confirmed present.

Draft the "What's changed" bullets from `git log <prev>..<tag> --oneline`
(`<prev>` = the previous release tag): group related commits, drop noise like
CI-only tweaks unless they matter to users, and **show the draft to the user**
before publishing. Then (after confirmation):

```bash
gh release create <tag> --title "<tag>" --notes "$(cat <<'EOF'
## What's changed

- <bullets from the commit log>

## Image

### Release <tag>

[![Docker images](https://img.shields.io/badge/Docker_images-<tag>-blue?logo=docker)](https://github.com/Kusnierz-Maciej/listonic-mcp/pkgs/container/listonic-mcp?tag=<tag>)

    docker pull ghcr.io/kusnierz-maciej/listonic-mcp:<tag>

### Latest

[![Docker images](https://img.shields.io/badge/Docker_images-latest-blue?logo=docker)](https://github.com/Kusnierz-Maciej/listonic-mcp/pkgs/container/listonic-mcp?tag=latest)

    docker pull ghcr.io/kusnierz-maciej/listonic-mcp:latest
EOF
)"
```

## Report

When done, tell the user: the version and tag, the workflow result, the GHCR
tags found, and the release URL. If any step was skipped or failed, say so.
