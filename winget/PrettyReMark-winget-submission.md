# PrettyReMark → winget Submission Runbook

Distilled from the actual v1.13 submission. Covers what worked and the gotchas
that caused real delays — skip anything not here, it was just a typo along the way.

## 0. One-time setup

```
winget settings --enable LocalManifestFiles
```
Run once, from an **admin** window. Lets `winget install --manifest` test local
files at all. Nothing else in this process needs admin — the app itself installs
per-user.

Fork `microsoft/winget-pkgs` on GitHub (web "Fork" button, or `gh repo fork
microsoft/winget-pkgs --clone=false`), then clone **your fork**, not the upstream:

```
git clone --depth 1 https://github.com/<you>/winget-pkgs.git
```
`--depth 1` skips the repo's history but *not* its current size — most of the
4–5GB is the live manifest snapshot (one folder per package/version, ever
submitted), not history. Real savings, but modest.

**Gotcha:** a `--depth 1` clone only tracks `master` locally. If you ever need
to `fetch`/push a *different* branch (e.g. after amending a commit on a PR
branch), a plain `git fetch origin` silently does nothing useful for that
branch. Fetch it by name explicitly, creating a real tracking ref:
```
git fetch origin <branch>:refs/remotes/origin/<branch>
```
Do this before trusting `--force-with-lease` — without it, the push will keep
rejecting with "stale info" even though nothing is actually wrong. If it still
misbehaves afterward on your own fork's branch, plain `--force` is fine.

## 1. Build the three manifest files

One folder per version: `manifests/<first-letter>/<Publisher>/<PackageName>/<Version>/`
containing:
- `<Publisher>.<PackageName>.yaml` — version manifest
- `<Publisher>.<PackageName>.installer.yaml` — installer manifest
- `<Publisher>.<PackageName>.locale.<locale>.yaml` — locale/metadata manifest

**Gotchas that cost real time:**
- `InstallerType` must match your *actual* installer tech — `inno` for Inno
  Setup, not `nullsoft` (that's NSIS, copied by mistake from eagle1's original
  template).
- `Scope` must match the installer's actual privilege level — `user` for an
  Inno Setup script with `PrivilegesRequired=lowest`, `machine` otherwise.
- If the release only publishes a `.zip` (common — many distribution sites
  reject bare `.exe` files), the manifest needs three extra fields, not just a
  different URL:
  ```yaml
  InstallerType: zip
  NestedInstallerType: inno
  NestedInstallerFiles:
  - RelativeFilePath: <exe filename as it sits inside the zip>
  ```
  And `InstallerSha256` must be the hash of the **`.zip`**, not the `.exe`
  inside it — easy to grab the wrong one if you're hashing both during testing.
- `ManifestVersion` should be the *current* schema (`1.12.0` as of this
  submission), not whatever an old template happens to say. Newer schema
  versions can carry additional requirements older ones don't enforce — in
  this case, every file's first line needs a schema-header comment matching
  its own type:
  ```
  # yaml-language-server: $schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json
  # yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json
  # yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json
  ```
  Missing this passes `winget validate` with only a warning, but is treated as
  a hard error by winget-pkgs' own CI.

## 2. Validate and test locally

```
winget validate --manifest <path-to-version-folder>
```
Checks YAML syntax and schema compliance only — no network activity.

```
winget install --manifest <path-to-version-folder>
```
Actually downloads from `InstallerUrl`, verifies the SHA256, extracts (if
zipped), and runs the real install. Requires the release to already be live.

## 3. Confirm the installer is actually silent

```
<setup>.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```
No UI at all — the real test of winget's "no interaction required" policy.

```
<setup>.exe /SILENT /SUPPRESSMSGBOXES /NORESTART
```
Shows a bare progress bar but still needs no clicks — this is what winget's
default "silent with progress" mode actually invokes.

Both pass, as long as the `.iss` only uses stock Inno wizard pages. Only
custom Pascal Script pages would risk requiring interaction, and only if they
don't check `WizardSilent()`.

## 4. Branch, stage, commit, push

```
git checkout -b add-<publisher>-<version>
```

Copy the validated manifest files into the fork clone at the required path:
```
manifests\<first-letter>\<Publisher>\<PackageName>\<Version>\
```

```
git add manifests\<...>\<Version>\
git commit -m "New package: Publisher.Name version X.Y.Z"
git push origin add-<publisher>-<version>
```

## 5. Open the PR

- Use GitHub's "Compare & pull request" banner after the push, or
  `gh pr create --repo microsoft/winget-pkgs`.
- Title: `New package: Publisher.Name version X.Y.Z` for a first submission,
  `Update: Publisher.Name to X.Y.Z` for later versions.
- Checklist: change `- [ ]` to `- [x]` only for items that are actually true.
  **Leave "Signed the CLA" unchecked** the first time — it isn't a checkbox
  process (see next step).

## 6. Sign the CLA

Reply as a normal PR comment, addressed to the bot that flags it:
```
@microsoft-github-policy-service agree
```
One-time across *all* Microsoft repos — not per-PR.

## 7. Automated checks and review

- A multi-stage pipeline runs automatically on every push to the branch:
  schema/content validation, installer download, a sandboxed silent-install
  test, and a domain/URL ownership check.
- **A "installer URL doesn't appear valid" / domain-mismatch flag is
  expected**, not a real problem, for anything hosted on a shared platform
  (GitLab, GitHub, etc.) — the bot can't verify account ownership from a URL
  alone. Reply confirming the links work (checked from a logged-out/incognito
  browser is good practice), and it routes to a human moderator.
- **"Review required" / "Merging is blocked" persists even after every check
  passes.** This is normal — a human moderator with write access has to
  approve before merge, regardless of automated status. Nothing to do but
  wait once checks are green.

## Fixing a mistake after the initial push

```
git add <corrected files>
git commit --amend --no-edit
git push --force origin <branch>
```
Folds the fix into the same commit rather than adding a second one — keeps
the PR's history to one clean commit. GitHub updates the open PR and re-runs
checks automatically; no need to touch the PR page itself.

## For next time (things worth doing differently)

- Stage manifest files locally in the same `d\Publisher\PackageName\Version\`
  shape from the start — makes the eventual copy into the fork clone a
  straight folder copy instead of a rename/restructure mid-process.
- Check the [current manifest schema version](https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema)
  before reusing any old manifest as a template — schema requirements
  (notably the header-comment rule) can change between versions.
- Before opening a new version's PR, check
  [open PRs](https://github.com/microsoft/winget-pkgs/pulls) for the same
  package to avoid duplicate submissions.
- Once this first PR merges, releasing future versions is the same shape
  (new version folder, new hash, branch, PR) and is a reasonable candidate to
  script into the project's own `Makefile` alongside the existing
  `release`/`update` targets — likely via `wingetcreate update`, which can
  compute the hash and even open the PR in one command given a GitHub token.
