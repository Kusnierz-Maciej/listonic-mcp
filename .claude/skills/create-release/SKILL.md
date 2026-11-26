---
name: create-release
description: Cut a new listonic-mcp release — push a signed v* tag, let CI build the GHCR image and create the raw release, then transcribe the auto-generated commit list into polished notes. Use when the user asks to release, tag, publish, or ship a new version.
disable-model-invocation: true
argument-hint: "[version, e.g. 1.11.0]"
---

# Creating a release

Cut a new release of listonic-mcp. Pushing a `v*` tag triggers the
[`Release`](../../../.github/workflows/release.yml) workflow, which:

- derives the version from the **tag** (`-p:Version=<version>`), so there is no
  csproj bump to do — the tag is the source of truth;
- builds and pushes the image to GHCR (`<tag>` and `latest`);
- on success, auto-creates a GitHub release whose notes are the **raw commit
  list** (`- <sha> <title>`) since the previous tag.

Your job is then to **transcribe** that raw release into readable notes.

> **If the build fails** the tag is left dangling: it exists, but there is no
> image and no release. For a transient failure (registry/auth) re-run the same
> tag: `gh workflow run release.yml -f tag=<tag>`. For a real code
> failure, fix forward on `main` and release the next patch version — do not
> reuse or rewrite the dangling tag.

## Versions

The target version is `$ARGUMENTS`.

**If none was given**, find the previous release with
`git describe --tags --abbrev=0`, look at `git log <prev>..HEAD --oneline`, and
propose the next version:

- **patch** — fixes, CI, docs, refactors only
- **minor** — any `feat:` commit
- **major** — breaking changes

Confirm the chosen version with the user before proceeding.

**Normalize the version.** Strip any leading `v`/`V` to get `<version>` (bare,
`1.11.0`) and derive `<tag>` (`v1.11.0`, always `v`-prefixed). Substitute both in
the commands below.

## Confirmation gates

1. **Version** — only when no version argument was given (after proposing the inferred version).
2. **Polished notes** — show the rewritten "What's changed" bullets and get the
   user's go-ahead before `gh release edit`.

All other steps run automatically without stopping.

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

### 1. Tag and push

No csproj edit, no bump commit. Create a **signed, annotated** tag on `HEAD`
and push it:

```bash
git tag -s <tag> -m "<tag>" HEAD
git merge-base --is-ancestor <tag>^{commit} main && echo "on main ✓"
git push origin <tag>
```

Pushing the tag triggers the release workflow (run name `Release <tag>`).

### 2. Wait for the build + auto-release

```bash
gh run list --workflow=release.yml --limit 1 --json databaseId,displayTitle,status
gh run watch <databaseId> --exit-status    # non-zero if the run fails
```

If the run fails, follow the dangling-tag guidance above and **stop**.

Confirm both image tags landed:

```bash
TOKEN=$(curl -s "https://ghcr.io/token?scope=repository:kusnierz-maciej/listonic-mcp:pull" \
  | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://ghcr.io/v2/kusnierz-maciej/listonic-mcp/tags/list"
```

On success the workflow has already created a release with raw commit bullets.

### 3. Transcribe the notes

The workflow produces two sections: a raw `## Commits` list (`- <sha> <title>`)
and a final, correctly-formatted `## Image` section (badges + lowercase pull
commands). **Only the commits part needs transcribing — leave `## Image` as-is.**

Read the auto-generated notes and split off the image section:

```bash
body=$(gh release view <tag> --json body -q .body)
image=$(printf '%s' "$body" | sed -n '/^## Image$/,$p')   # keep verbatim
```

Rewrite the commit bullets into a readable **## What's changed** section: group
related commits, lead with user-facing features and fixes, drop pure CI/chore
noise unless it matters to operators, and phrase each bullet around user impact
rather than implementation. Then recombine with the untouched `## Image`:

```
## What's changed

- <grouped, rewritten bullets>

<the verbatim ## Image section from $image>
```

**Show the user the polished "What's changed" bullets and wait for
confirmation**, then update the release in place (reusing the captured image
section so the badges/pull commands stay exactly as the workflow generated them):

```bash
gh release edit <tag> --notes "$(cat <<EOF
## What's changed

- <polished bullets>

${image}
EOF
)"
```

## Report

Tell the user: the version and tag, the workflow result, the GHCR tags found,
and the release URL. If any step was skipped or failed (e.g. a dangling tag),
say so.
