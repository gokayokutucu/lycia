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
- After successful implementation and validation, merge the feature branch into `dev` with
  `--no-ff`, then merge `dev` into `main` with `--no-ff`.
- Delete the local feature branch after both merges succeed. Leave the repository on `main`.
- Never push (any branch) unless the user explicitly requests it.
- Never create or push a release tag unless the user explicitly requests a release.
- NuGet publishing is tag-driven only (`v*` tags via `.github/workflows/dotnet.yml`); a normal
  merge or push to `main`/`dev` must never publish packages.
