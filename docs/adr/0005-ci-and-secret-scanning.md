# ADR 0005: CI pipeline and secret protection

- **Status:** Accepted
- **Date:** 2026-11-05

## Decision
**CI (GitHub Actions)** fails fast and cheap first:

1. `build-test`: gitleaks (full history), dotnet format, build, unit and architecture tests, integration tests
   (Testcontainers), then the deployment config checks (`docker compose config`).
2. `web`: lint, typecheck, Vitest, build.
3. In the private development repository only: `publish` (images to GHCR) and `deploy`, on pushes to `main`.

**Nightly instead of per merge (2026-11-14):** Semgrep + the vulnerable-package audit (`security-scan.yml`,
03:30 UTC) run nightly and on demand. They no longer gate merges. gitleaks stays in `build-test` because a
leaked secret must block the merge.

**E2E runs locally only (2026-11-14, all projects):** the Compose smoke test and the Playwright journeys run on the
developer machine while developing (`web/e2e/README.md`), not on GitHub: the full-stack suites need more memory than
CI machines have, and the GitHub minutes were the reason to move them in the first place.

**Branches (added 2026-11-06):** feature branches (`backend/*`, `frontend/*`) open PRs into `develop`.
`develop` runs only the light gates: `dev-backend.yml` (`dotnet format` + build when backend files change) and
`dev-web.yml` (lint + `next build`, which type-checks, when `web/` changes), selected by path filters. The full pipeline above (`ci.yml`) runs only on
PRs into and pushes to `main`, which is the release branch: `develop` is merged into `main` to release.
Tests are still run locally in Docker before every PR; CI on `develop` just doesn't repeat them.

**Secret protection is layered:**
1. A gitleaks **pre-commit hook** (`.githooks/pre-commit`, run in Docker) stops secrets before they reach git history.
2. **gitleaks in CI** is the backstop when the hook was skipped.
3. **GitHub push protection and secret scanning** will be enabled when the repository becomes public (they're free for public repos).
4. Configuration comes only from environment variables. `.env` is gitignored and `.env.example` documents the variables.

**If a secret leaks:** rotate it immediately, check the provider's logs for misuse, then clean up the history.
Rewriting history alone isn't enough, because public repos are scraped within minutes.

## Consequences
- ✅ Several independent layers. No single failure exposes a secret.
- ⚠️ The pre-commit hook needs Docker running. Without it the hook refuses the commit (fail closed).
- ⚠️ Test regressions on `develop` surface only at release time (PR into `main`). Saves Actions minutes and wait time.
- ⚠️ Semgrep/vulnerability regressions surface the next morning (nightly run), after the merge.
- ⚠️ E2E and Docker smoke regressions are caught only if the suite is run locally before the release PR.
