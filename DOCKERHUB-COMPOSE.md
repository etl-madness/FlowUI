# Run FlowUI and go-flowAPI from Docker Hub with Docker Compose

This guide uses the published images instead of building either application from source:

- FlowUI: `etlmadness/flowui:latest`
- API: `etlmadness/go-flow-api:latest`
- Optional SQL Server: `mcr.microsoft.com/mssql/server:2022-latest`

The request listed `etlmadness/go-flow-api` twice; this guide assumes the first image is the FlowUI image, `etlmadness/flowui`. If the FlowUI repository on Docker Hub has a different name, substitute that image reference below.

Run the commands from the FlowUI repository root. Docker Compose reads a `.env` file in the current directory for the values shown below.

## 1. FlowUI and API with an existing database

Use the existing API Compose example, but make a separate copy so the source-build configuration stays unchanged:

```powershell
Copy-Item docker-compose.api.yml docker-compose.hub.yml
```

In `docker-compose.hub.yml`, replace the `go-flow-api` service's `build:` section and image with:

```yaml
    image: etlmadness/go-flow-api:latest
```

Replace the `FlowUI` service's `build:` section and image with:

```yaml
    image: etlmadness/flowui:latest
```

Keep the remaining service settings. The API uses the `FLOW_API_DSN` connection string to connect to your existing database. Create a `.env` file in the repository root and set the connection string and non-default credentials:

```dotenv
FLOW_API_PORT=8080
FLOW_UI_PORT=5185
FLOW_DB_SERVER=host.docker.internal
FLOW_DB_NAME=EXAMPLE_DB
FLOW_DB_PASSWORD=replace-with-a-strong-database-password
FLOW_UI_ADMIN_USER=admin
FLOW_UI_ADMIN_PASSWORD=replace-with-a-strong-admin-password
```

The example Compose file constructs a SQL Server DSN from `FLOW_DB_SERVER`, `FLOW_DB_NAME`, and `FLOW_DB_PASSWORD`. Adjust those values for your database. On Linux, `host.docker.internal` may need to be replaced with a host address reachable from the containers.

Start the services:

```powershell
docker compose -f docker-compose.hub.yml pull
docker compose -f docker-compose.hub.yml up -d
```

Open [http://localhost:5185](http://localhost:5185). The API is also published at port `8080`.

## 2. FlowUI with an already-running API

If the API is already running outside this Compose project, make a copy of the root Compose file so the source-build configuration stays unchanged:

```powershell
Copy-Item docker-compose.yml docker-compose.hub-ui.yml
```

In `docker-compose.hub-ui.yml`, replace the `FlowUI` service's `build:` section and image with:

```yaml
    image: etlmadness/flowui:latest
```

Set `FLOW_API_HOST` and `FLOW_API_PORT` to the address and port reachable **from inside the FlowUI container**. For an API running on the Docker host, the existing `host.docker.internal` default can be used. Then pull and start FlowUI:

```powershell
docker compose -f docker-compose.hub-ui.yml pull
docker compose -f docker-compose.hub-ui.yml up -d
```

## 3. Optional SQL Server container

The repository's `docker-compose.full.yml` already includes SQL Server, a persistent data volume, and initialization of the `EXAMPLE_DB` schema from `docker/init/sqlserver-init.sql`. To use the Docker Hub application images with that setup:

1. Copy the full-stack example:

   ```powershell
   Copy-Item docker-compose.full.yml docker-compose.hub-full.yml
   ```

2. In `docker-compose.hub-full.yml`, replace the API service's `build:` section and image with:

   ```yaml
       image: etlmadness/go-flow-api:latest
   ```

   Replace the FlowUI service's `build:` section and image with:

   ```yaml
       image: etlmadness/flowui:latest
   ```

3. Create or update the root `.env` file. Use strong, unique values:

   ```dotenv
   FLOW_DB_PASSWORD=replace-with-a-strong-sql-server-password
   FLOW_DB_NAME=EXAMPLE_DB
   FLOW_UI_ADMIN_USER=admin
   FLOW_UI_ADMIN_PASSWORD=replace-with-a-strong-admin-password
   FLOW_UI_PORT=5185
   FLOW_API_PORT=8080
   ```

4. Pull the images and start the full stack:

   ```powershell
   docker compose -f docker-compose.hub-full.yml pull
   docker compose -f docker-compose.hub-full.yml up -d
   ```

The SQL Server data is stored in the `flow_sqlserver_data` named volume and persists when containers are recreated. The initialization script creates the database and tables on first startup.

## Check status and stop

Use the same Compose file with each command:

```powershell
docker compose -f docker-compose.hub-full.yml ps
docker compose -f docker-compose.hub-full.yml logs -f
docker compose -f docker-compose.hub-full.yml down
```

`down` stops and removes the containers and network but keeps the named SQL Server volume. To avoid data loss, do not remove that volume unless you intentionally want to delete the database data.

## Image tags and private repositories

The examples use the `latest` tag. To run a specific published version, replace `latest` with that tag in the Compose file. If either Docker Hub repository is private, authenticate before pulling:

```powershell
docker login
```
