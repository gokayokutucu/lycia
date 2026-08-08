# AGENTS.md

Permanent operational rules for any AI agent or automated tool working in this repository.
This file is the mandatory repository-wide rule source. Any Claude-specific (or other
tool-specific) instruction file must stay consistent with this document.

## Branching

- All feature work starts from the latest local `dev`.
- Never start normal feature work from `main`.
- Create a dedicated feature branch from `dev` (e.g. `feature/<short-description>`).
- Preserve unrelated user work exactly: never reset, stash, delete, overwrite, stage, or commit
  changes that are not part of the current task.

## Completion

After implementation and validation:

1. Commit the feature branch.
2. Merge the feature branch into `dev` using `git merge --no-ff`.
3. Verify the merged `dev` (tree content, `git diff --check`, `git status`).
4. Merge `dev` into `main` using `git merge --no-ff`.
5. Verify tree equality between `main` and the validated `dev` state when applicable.
6. Delete the local feature branch.
7. Leave the repository checked out on `main`.
8. Do not push unless the user explicitly asks.

## Attribution

Never add Claude, Anthropic, OpenAI, Codex, or any AI tool/model as author, co-author, committer
identity override, signer, reviewer, generated-by attribution, assisted-by attribution, or
commit-message trailer (including but not limited to `Co-Authored-By`, `Generated-By`,
`Assisted-By`, `Reviewed-By`).

Use only the repository user's existing, already-configured Git author and committer identity.
Do not modify the user's configured Git identity.

## Release behavior

- Ordinary feature completion does not create a release tag.
- Ordinary merges/pushes to `main` do not publish NuGet packages.
- NuGet publication is release-driven: the publish job in
  `.github/workflows/dotnet.yml` only runs when the triggering ref is a release tag matching
  `v*` (`refs/tags/v*`). A normal push/merge to `main` or `dev`, or a pull request, cannot trigger
  publication.
- Agents must never create or push a release tag unless the user explicitly requests a release.

## Remote operations

- Never push (including `dev`, `main`, or feature branches) unless the user explicitly asks.
- Never force-push.
- Never create or push tags unless the user explicitly requests a release.
- Never publish packages.
- The user controls all remote operations and releases.
