# AGENTS.md

Permanent operational rules for any AI agent or automated tool working in this repository.
This file is the mandatory repository-wide rule source. Any Claude-specific (or other
tool-specific) instruction file must stay consistent with this document.

## Branching

- All feature work starts from the latest local `dev`.
- Never start normal feature work from `main`.
- Create a dedicated feature branch from `dev` (e.g. `feature/<short-description>`).
- Read `PROJECT_LEDGER.md` before starting a phase. Verify that the requested work is the active
  phase or the expected next phase; do not silently skip required earlier phases.
- Preserve unrelated user work exactly: never reset, stash, delete, overwrite, stage, or commit
  changes that are not part of the current task.

## Intermediate phase completion

After implementation and validation:

1. Commit the feature branch.
2. Merge the feature branch into `dev` using `git merge --no-ff`.
3. Verify the merged `dev` (tree content, `git diff --check`, `git status`).
4. Update `PROJECT_LEDGER.md`: move the phase to `COMPLETED`, record its feature and `dev` merge
   commits, and promote the next phase when appropriate. Commit this ledger-only update on `dev`
   when the newly created merge commit must be recorded.
5. Delete the local feature branch.
6. Leave the repository checked out on `dev`.

Do **not** merge `dev` into `main` after an intermediate feature or phase, even when `dev` is stable
and all tests pass.

## Final milestone integration

- `main` is a completed milestone boundary, not a per-phase integration branch.
- `PROJECT_LEDGER.md` is the persistent authority for milestone progress and finalization gates.
- Merge `dev` into `main` using `git merge --no-ff` only when the ledger's `FINALIZATION` section
  says `Status: READY FOR FINAL INTEGRATION`.
- Completing the last implementation phase is insufficient while final validation, documentation,
  package, architecture, or reliability-review gates remain pending.
- After an authorized final merge, verify the validated `dev` and `main` trees and leave the
  repository checked out on `main`.

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
