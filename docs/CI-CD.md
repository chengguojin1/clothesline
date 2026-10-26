# CI/CD

Jemur builds and publishes downloads using Cloudflare Workers Builds in the
**Syeana** account (`c595078d172b45beb8df900b7cbb24cb`). GitHub hosts the source;
GitHub Actions is not used. Every successful `main` build publishes a GitHub
Release automatically when its application version has not been released yet.
Pushing a version tag alone does not publish a build.

## Cloudflare settings

- Worker: `jemur-downloads`
- Repository: `revanapriyandi/jemur`
- Production branch: `main`; disable non-production branch builds
- Root directory: `/`
- Build command: `bash .cloudflare/build.sh`
- Deploy command: `bash .cloudflare/deploy.sh`
- Dependency installation: `npm ci` from the committed lockfile
- No R2 binding or application data is needed
- Build secret: `JEMUR_RELEASE_TOKEN`, a fine-grained GitHub token with
  Contents read/write for `revanapriyandi/jemur` only, plus required Metadata read

The Worker is configured in `wrangler.jsonc`. Build/deploy permissions are supplied
by Cloudflare's build integration; do not commit tokens. The `.NET 10.0.402` SDK is
installed from Microsoft's official installer when it is not available.

## Checks and packages

1. Node tests exercise streamed EXE downloads, checksum headers, missing chunks,
   HEAD/conditional requests, old standalone links and public routes. Chunks are
   fetched one at a time and piped through Cloudflare's native FixedLengthStream
   to keep asset connections bounded and enforce the response Content-Length.
2. `dotnet test tests/portable/Jemur.Portable.Tests.csproj -c Release` runs the
   existing library and localization tests against the actual linked production
   sources. No WPF types are replaced with stubs.
3. `dotnet build Jemur.sln -c Release -p:EnableWindowsTargeting=true` compiles
   the application and the Windows test assembly. WPF tests cannot run on Linux.
4. `dotnet publish` creates self-contained single-file EXEs for x64 and ARM64.
   Native libraries are bundled for extraction by .NET. Embedded assemblies are
   compressed, and ReadyToRun is disabled for cross-platform compilation.
5. `.cloudflare/nsis.sh` downloads NSIS `3.08-3+deb12u1` from Debian's official
   HTTPS mirror, checks pinned SHA-256 hashes and extracts it without root.
   Sources: [compiler](https://packages.debian.org/bookworm/amd64/nsis/download)
   and [compiler data](https://packages.debian.org/bookworm/all/nsis-common/download).
6. Python checks that each publish directory contains exactly one EXE with the
   expected PE architecture, copies the portable EXEs, and compiles
   `packaging/installer.nsi` into setup EXEs. Every EXE includes the runtime.
   Files are split into 20 MiB static assets; the Worker streams one EXE back.
   The manifest records architecture, package type, source commit and SHA-256.
7. The deploy script rejects a checkout that no longer matches `origin/main`,
   deploys the Worker, verifies the live source commit, and downloads all four
   public EXEs to check byte counts, SHA-256 and Windows executable magic. It
   retries health and manifest checks while the new deployment propagates,
   requiring both endpoints to report the expected commit before downloading.
8. `.cloudflare/release.mjs` creates a version tag at the exact manifest commit,
   creates a draft GitHub release, uploads the four EXEs, manifest and checksums,
   verifies GitHub's asset sizes and SHA-256 digests, then publishes the release.
   An upload failure leaves the release as a draft for a later retry.

Run the complete suite on Windows before publishing:

```powershell
dotnet test Jemur.sln -c Release
npm ci
npm run test:downloads
```

The portable project does not replace the Windows suite. In particular, hotkey
parsing and WPF runtime behavior still require Windows validation.

Before publishing a GitHub release, verify the Cloudflare artifacts. On Windows,
run the portable EXE, install the setup EXE, launch the installed application,
check its shortcuts and installed-apps entry, then uninstall it. Confirm that
user data is retained and repeat installation to test upgrades. ARM64 execution
requires an ARM64 Windows device.

`Jemur.exe --render-docs <folder>` provides an off-screen WPF smoke check. Set
`JEMUR_DATA` to an isolated folder with an existing `data` subfolder. This does
not replace interactive screenshot, shortcut and drag-and-drop checks.

## Installer behavior

The installer uses the current user's account and defaults to
`%LOCALAPPDATA%\Programs\Jemur`. It adds Start menu shortcuts, an optional desktop
shortcut and an entry in Windows' installed-apps list. Close Jemur from its tray
menu before upgrading or uninstalling. ARM64 setup rejects non-ARM64 Windows.
Uninstall removes the installed application and its registration while retaining
the library and settings in `%APPDATA%\Jemur`.

## Downloads and verification

- [x64 portable EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-x64)
- [x64 setup EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-x64-setup)
- [ARM64 portable EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-arm64)
- [ARM64 setup EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-arm64-setup)
- [Checksums](https://jemur-downloads.syeana-co.workers.dev/SHA256SUMS.txt)
- [Manifest](https://jemur-downloads.syeana-co.workers.dev/manifest.json)
- [Deployed commit](https://jemur-downloads.syeana-co.workers.dev/health)

`/latest/<variant>` redirects to the current package. The public manifest records
its version, full source commit, filename, size and SHA-256. Match the live commit
to the Cloudflare build and verify downloaded EXEs with `Get-FileHash -Algorithm
SHA256`. Old `-standalone` links redirect to the corresponding portable EXE.
Cloudflare tracks the latest successful `main` build. Failed builds leave the
previous deployment active.

## In-app update checks

Jemur reads the public `manifest.json` endpoint below for its manual About check
and its quiet in-process background check:

`https://jemur-downloads.syeana-co.workers.dev/manifest.json`

The client validates the semantic version, source commit and GitHub release URL
before showing an update. It checks once about 30 seconds after startup and no
more than once every 24 hours while Jemur is running. Unless the user turned off
**Update automatically** in Settings, Jemur downloads the package for its build
(installer or portable EXE, x64 or arm64) from the manifest, requires an `https`
URL on the manifest host and the manifest's size and SHA-256, and installs it
when idle or on quit. It never runs or moves a file that fails those checks.
With the setting off, or if an automatic update fails, a newer version produces
one tray notification per process and the user starts the installer from the
GitHub release page. Jemur 0.3.0 and older update by hand once.

## Automatic GitHub Releases

Bump `<Version>` in `src/Jemur.csproj`, add the corresponding changelog entry,
run the Windows checks and push to `main`. Cloudflare automatically builds,
deploys, checks public downloads and publishes `v<Version>` with all six assets.
Release notes include the changelog entry and source commit. A prerelease version
containing a hyphen is published as a GitHub prerelease.

If the version is already published, the release step reports that it was
skipped and never replaces its tag or assets. Increase the application version
to publish a new release. Cloudflare's latest-build links still update on every
successful push, so they may be newer than the last versioned release.

Never move published version tags. A pre-existing tag or draft targeting another
commit makes the build fail rather than overwrite it. A failed draft can resume
from the same commit; correct uploads are retained and incomplete draft assets
are replaced before publication.

Store `JEMUR_RELEASE_TOKEN` as a **secret build variable** on the Jemur trigger
in the Syeana account, not as a Worker binding or repository file. Check the
token's expiration in GitHub and replace the build secret before that date.
Missing or expired credentials cause the release step to fail
visibly. Other repositories and account-level permissions are not required.

Workers requests and build minutes use the account's existing plan and limits.
No paid plan or additional storage subscription is enabled by this setup.
