# FlowUI Docker & Docker Compose Guide

This guide explains how to build, configure, and run **FlowUI** in Docker using either **Linux containers** (default) or **Windows containers**, across all supported deployment topologies.

> **Architecture Note:** `FlowUI` communicates **strictly via `go-flowAPI`** (`FlowApiClient`) to load, save, upload, delete, and execute pipelines, configs, and options. `FlowUI` has **no direct database connection** and **no internal database**.

---

## 1. Overview of Docker Files

| File | Target OS | Services Included | Description |
| :--- | :--- | :--- | :--- |
| [`Dockerfile`](./Dockerfile) | **Linux** (Default) | `FlowUI` | Multi-stage build using `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0` (runs as non-root `$APP_UID`). |
| [`Dockerfile.windows`](./Dockerfile.windows) | **Windows** (`ltsc2022`) | `FlowUI` | Multi-stage build using `windowsservercore-ltsc2022` for Windows Integrated Authentication (`FLOW_UI_AUTH_MODE=Windows`). |
| [`docker-compose.yml`](./docker-compose.yml) | Linux (or Windows via env) | `FlowUI` | **Combination 1 (Default)**: Runs `FlowUI` only; connects to an existing external `go-flowAPI`. |
| [`docker-compose.api.yml`](./docker-compose.api.yml) | Linux | `FlowUI` + `go-flow-api` | **Combination 2**: Builds and runs both `FlowUI` and `../go-flowAPI` (with `../go-flow` build context); `go-flow-api` connects to an external SQL Server. |
| [`docker-compose.full.yml`](./docker-compose.full.yml) | Linux | `FlowUI` + `go-flow-api` + `sqlserver` + `sqlserver-init` | **Combination 3 (Full Stack)**: Self-contained stack with SQL Server 2022, automatic `EXAMPLE_DB` database & table initialization for `go-flowAPI`, `go-flowAPI`, and `FlowUI`. |
| [`docker-compose.windows.yml`](./docker-compose.windows.yml) | Windows | `FlowUI` | **Windows Container Mode**: Builds `Dockerfile.windows` with `FLOW_UI_AUTH_MODE=Windows`. |
| [`docker/init/sqlserver-init.sql`](./docker/init/sqlserver-init.sql) | SQL Server | `sqlserver-init` | Idempotent SQL script that creates the `EXAMPLE_DB` database and core `flow_*` / `pipeline_*` tables for `go-flowAPI` on startup. |
| [`.env.example`](./.env.example) | Both | All | Template environment variables file for Docker Compose. |

---

## 2. Environment Variables Reference

Copy [`.env.example`](./.env.example) to `.env` in the repository root before running `docker compose` to customize ports and API settings:

```powershell
Copy-Item .env.example .env
```

### `FlowUI` Environment Variables

| Variable | Default in Compose | Description |
| :--- | :--- | :--- |
| `FLOW_UI_PORT` | `5185` | Host and container HTTP port for `FlowUI`. |
| `FLOW_API_HOST` | `http://host.docker.internal` | Base scheme + hostname for `go-flowAPI` (`http://go-flow-api` in `api`/`full` compose files). |
| `FLOW_API_PORT` | `8080` | Port for `go-flowAPI`. |
| `FLOW_API_URL` | *(optional)* | Full URL override for `go-flowAPI` (overrides `FLOW_API_HOST` + `FLOW_API_PORT` if set). |
| `FLOW_UI_AUTH_MODE` | `ApiBasic` | Default authentication provider (`ApiBasic` or `Windows`). In `ApiBasic` mode, API requests use the credentials entered by the user at login. |

### `go-flowAPI` & `sqlserver` Variables (Used in `docker-compose.api.yml` & `docker-compose.full.yml`)

| Variable | Default in Compose | Description |
| :--- | :--- | :--- |
| `FLOW_DB_SERVER` | `host.docker.internal` | SQL Server hostname for `go-flowAPI` DSN in `docker-compose.api.yml`. |
| `FLOW_DB_NAME` | `EXAMPLE_DB` | Target SQL Server database name for `go-flowAPI`. |
| `FLOW_DB_PASSWORD` | `YourStrong!Passw0rd` | SQL Server password used by `go-flowAPI` and `sqlserver` container. |
| `MSSQL_HOST_PORT` | `1433` | Host port mapped to the SQL Server container in `docker-compose.full.yml`. |

---

## 3. Usage & Examples by Deployment Scenario

### Scenario 1: `FlowUI` Only (Default — External `go-flowAPI`)

Use [`docker-compose.yml`](./docker-compose.yml) when `go-flowAPI` is already running on your host machine or another server on your network.

#### Example: Connect to `go-flowAPI` on port `8085` on the host machine
```powershell
# PowerShell
$env:FLOW_API_HOST = "http://host.docker.internal"
$env:FLOW_API_PORT = "8085"
$env:FLOW_UI_AUTH_MODE = "ApiBasic"

docker compose up -d --build
```

Open **`http://localhost:5185`** in your browser.

---

### Scenario 2: `FlowUI` + `go-flowAPI` (External SQL Server)

Use [`docker-compose.api.yml`](./docker-compose.api.yml) to containerize both the Blazor UI (`FlowUI`) and the Go backend (`go-flow-api`) while `go-flow-api` connects to an existing SQL Server instance.

> **Note:** Expects the sibling repositories `../go-flowAPI` and `../go-flow` to exist alongside `FlowUI` (`etl-madness/go-flowAPI` and `etl-madness/go-flow`).

#### Example: Run UI + API against external SQL Server `t15p`
```powershell
# PowerShell
$env:FLOW_DB_SERVER = "dbserver.example.com"
$env:FLOW_DB_NAME = "EXAMPLE_DB"
$env:FLOW_DB_PASSWORD = "YourStrong!Passw0rd"
$env:FLOW_UI_ADMIN_USER = "admin"
$env:FLOW_UI_ADMIN_PASSWORD = "Admin123!"

docker compose -f docker-compose.api.yml up -d --build
```

- **FlowUI Studio:** `http://localhost:5185`
- **go-flowAPI Endpoint:** `http://localhost:8080/api/v1/health`

---

### Scenario 3: Full Stack (`FlowUI` + `go-flowAPI` + SQL Server 2022)

Use [`docker-compose.full.yml`](./docker-compose.full.yml) to spin up a complete, self-contained environment from scratch:
1. **`sqlserver`** starts Microsoft SQL Server 2022 Developer Edition and waits until healthy.
2. **`sqlserver-init`** executes [`docker/init/sqlserver-init.sql`](./docker/init/sqlserver-init.sql) to create the `EXAMPLE_DB` database and all `flow_*` / `pipeline_*` tables for `go-flowAPI`.
3. **`go-flow-api`** starts on port `8080` connected to `sqlserver` and bootstraps the admin API user.
4. **`FlowUI`** starts on port `5185` and connects exclusively to `go-flow-api`.

#### Example: Launch the full stack
```powershell
# PowerShell
docker compose -f docker-compose.full.yml up -d --build
```

Check container status and initialization logs:
```powershell
docker compose -f docker-compose.full.yml ps
docker compose -f docker-compose.full.yml logs -f FlowUI go-flow-api
```

Access the stack:
- **FlowUI Web UI:** `http://localhost:5185` (Login via `ApiBasic` with `admin` / `Admin123!`)
- **go-flowAPI Health:** `http://localhost:8080/api/v1/health`

---

### Scenario 4: Windows Containers (`Dockerfile.windows` & Windows Integrated Auth)

Use [`Dockerfile.windows`](./Dockerfile.windows) and [`docker-compose.windows.yml`](./docker-compose.windows.yml) when running on Windows Server or Docker Desktop in **Windows containers** mode with `FLOW_UI_AUTH_MODE=Windows`.

#### Example: Build and run with Windows Compose
```powershell
# Ensure Docker Desktop is switched to Windows containers mode
docker compose -f docker-compose.windows.yml up -d --build
```

---

## 4. Standalone `docker build` & `docker run` Examples

If you prefer running `docker` directly without Docker Compose:

### Build & Run Linux Container
```powershell
# 1. Build the image
docker build -t FlowUI:latest -f Dockerfile .

# 2. Run the container connected to go-flowAPI
docker run -d --name FlowUI `
  -p 5185:5185 `
  --add-host=host.docker.internal:host-gateway `
  -e FLOW_API_HOST="http://host.docker.internal" `
  -e FLOW_API_PORT="8080" `
  -e FLOW_UI_AUTH_MODE="ApiBasic" `
  FlowUI:latest
```

### Build & Run Linux Container with Explicit `FLOW_API_URL`
```bash
docker run -d --name FlowUI \
  -p 5185:5185 \
  -e FLOW_API_URL="http://10.0.0.25:8080" \
  -e FLOW_UI_AUTH_MODE="ApiBasic" \
  FlowUI:latest
```

---

## 5. Common Operations

### View Live Logs
```powershell
# Default compose
docker compose logs -f FlowUI

# Full stack
docker compose -f docker-compose.full.yml logs -f
```

### Stop & Clean Up Containers
```powershell
# Stop containers (preserves SQL Server data volume)
docker compose -f docker-compose.full.yml down

# Stop containers AND delete the SQL Server persistent volume (full reset)
docker compose -f docker-compose.full.yml down -v
```
