# CLAUDE.md

Claude-specific pointer to this repository's permanent agent rules. The full, authoritative rule
set lives in [AGENTS.md](AGENTS.md) — read it first. This file only restates the rules Claude must
never violate:

- Never add Claude or Anthropic attribution to any commit.
- Never add `Co-Authored-By`, `Generated-By`, `Assisted-By`, `Reviewed-By`, or any equivalent AI
  trailer to a commit message.
- Use only the repository user's existing, already-configured Git author and committer identity.
  Do not modify the user's configured Git identity.
- Feature branches must start from the latest local `dev`, never from `main`.
- Read [PROJECT_LEDGER.md](PROJECT_LEDGER.md) before starting or completing a phase.
- For an intermediate phase: merge the feature branch into `dev` with `--no-ff`, update the ledger,
  delete the local feature branch, and leave the repository on `dev`. Do not merge `dev` into `main`.
- `main` is a final milestone boundary. Merge `dev` into `main` with `--no-ff` only when the ledger's
  `FINALIZATION` section says `Status: READY FOR FINAL INTEGRATION`; then leave the repository on
  `main`.
- Never push (any branch) unless the user explicitly requests it.
- Never create or push a release tag unless the user explicitly requests a release.
- NuGet publishing is tag-driven only (`v*` tags via `.github/workflows/dotnet.yml`); a normal
  merge or push to `main`/`dev` must never publish packages.

`AGENTS.md` and `PROJECT_LEDGER.md` are authoritative if this summary is ever incomplete.
