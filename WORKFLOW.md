# Workflow

## Remotes
- `origin`: upstream source
- `private`: personal mirror and release host

## Release Source of Truth
Releases are published to `LiGaAmG/mudclient-source`.
Build artifacts are attached to GitHub Releases, not committed.

## One-command release
Run in `C:\bot\repos\adan-refactor-clients-workspace`:
`powershell -ExecutionPolicy Bypass -File .\scripts\release.ps1 -Version 1.6.6`
