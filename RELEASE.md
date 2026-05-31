# Release

## Versioning
Use tags `vX.Y.Z-private.N` for private client lines.

## Steps
1. Ensure `master` is clean and build passes.
2. Add release notes (README or notes file).
3. Tag: `git tag vX.Y.Z-private.N`.
4. Push branch and tag to private:
   - `git push private master`
   - `git push private vX.Y.Z-private.N`
5. Upload packaged client zip to GitHub Release assets.

## Artifacts policy
- Source code in git.
- Built client zips/exe/dll in GitHub Releases, not in repo history.
