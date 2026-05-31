# Development

## Branches
- `master`: integration branch in this repository.
- `feature/*`: work branches for isolated changes.

## Workflow
1. Sync from upstream (`origin`) and private backup (`private`).
2. Create `feature/*` branch.
3. Make focused commits.
4. Build/test locally.
5. Merge to `master`, then push to `private`.

## Remotes
- `origin`: upstream public source.
- `private`: your private mirror/work repo.

## Safety
- Do not commit secrets.
- Do not commit generated binaries/logs.
