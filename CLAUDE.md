# Genie4

## Branch Strategy

- **Base branch is `genie-5`**, not `main`.
- All feature branches must be created from `genie-5`.
- All PRs must target `genie-5` as the base branch — never `main`.
- When creating worktrees, branch off `genie-5`.
- **Never commit directly to `genie-5`**. All changes must go through a PR.

## Additional Read-Only Directories

- `../Genie4-Plugins` — compiled plugin DLLs (read-only reference). Used to verify plugin compatibility when modifying core APIs or changing what is exposed/excluded from builds.
