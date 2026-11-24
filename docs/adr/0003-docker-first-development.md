# ADR 0003: Docker-first development with a Dev Container

- **Status:** Accepted
- **Date:** 2026-11-05

## Context
The only host requirements should be Docker and Git. Reviewers should be able to run the system with one command,
and developers need a fast edit → run → debug → test loop.

## Decision
Two setups, for two audiences:

1. **`docker compose up --build`** (reviewers, production-like): a multi-stage Dockerfile
   (SDK build stage → slim Alpine ASP.NET runtime), non-root user, container health check,
   and PostgreSQL with a readiness health check. The API waits for the database to be healthy.
2. **Dev Container** (development): VS Code attaches to a container that has the .NET SDK.
   IntelliSense, the debugger and `dotnet watch` hot reload all run inside the container.
   - Source is **bind-mounted** from the host, so host tools see the same files.
   - Build output (`artifacts/`, via `UseArtifactsOutput`) and the NuGet cache live in **named volumes**,
     which avoids slow host file sharing on macOS.
   - The `docker-outside-of-docker` feature gives Testcontainers and the gitleaks hook access to the host Docker engine.

Health endpoints: `/health/live` has no dependency checks, so a database outage doesn't restart the API.
`/health/ready` checks the database.

## Consequences
- ✅ Same SDK version and tooling for every contributor. No local installs.
- ⚠️ First Dev Container start is slow because images and packages get pulled.
- ⚠️ Default credentials in `compose.yaml` are for local use only. Real environments must provide them through environment variables or a secret store.
