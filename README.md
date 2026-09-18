# Sabby Optimizer

Sabby Optimizer uses this repository for its permanent update pipeline.

## Normal update flow

1. Sabby checks `update/SabbyOptimizer-Stable-manifest.json` when it opens.
2. If the online version is newer, Sabby shows an update prompt.
3. Sabby downloads the matching Setup.exe from GitHub Releases.
4. It verifies SHA-256, backs up user data, runs the update, and reopens.
5. Settings under `%LOCALAPPDATA%\SabbyOptimizer\UserData` are preserved.

## One-time source bootstrap

Upload the compact `SabbySource.zip` once to:

`bootstrap/SabbySource.zip`

The GitHub Actions workflow imports it into `src/`, deletes the bootstrap ZIP from the repository, builds the Windows installer, creates a GitHub Release, and updates the stable manifest.

After that, normal source edits under `src/` automatically build and publish the next Sabby update.
