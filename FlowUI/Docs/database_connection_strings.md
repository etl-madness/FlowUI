# Database Connection Strings & Driver Reference Guide 🔌

This guide provides a comprehensive reference of all supported database drivers in `go-flow`, detailing every available connection string format, query parameter, security setting, and performance tuning option. It includes practical examples for local development, enterprise production, cloud-managed instances, mutual TLS, and high-throughput ETL pipelines.

---

## 1. Driver Overview & Comparison Matrix

The table below outlines each database and storage engine supported by `go-flow`, the registered driver aliases, default network ports, URI schemes, and supported operational capabilities.

| Database / Engine | Driver Aliases | Underlying Go Library | Default Port | Primary DSN Schemes | Key Capabilities | Safe Default Example |
| :--- | :--- | :--- | :---: | :--- | :--- | :--- |
| **PostgreSQL** | `postgres`<br>`postgresql`<br>`pq` | `github.com/lib/pq` | `5432` | `postgresql://`<br>`postgres://`<br>`host=...` (Key-Value) | Full SSL/TLS (mTLS), schema `search_path`, session timeouts, binary parameters, connection keepalives | `postgresql://user:pass@localhost:5432/mydb?sslmode=disable` |
| **MySQL & MariaDB** | `mysql` | `github.com/go-sql-driver/mysql` | `3306` | `user:pass@tcp(host:port)/db`<br>`user:pass@unix(socket)/db` | Datetime parsing (`parseTime`), TLS/SSL, bulk parameter interpolation (`interpolateParams`), multi-statements, packet sizing | `user:pass@tcp(localhost:3306)/mydb?parseTime=true&loc=UTC` |
| **Microsoft SQL Server & Azure SQL** | `sqlserver`<br>`mssql`<br>`azuresql` | `github.com/microsoft/go-mssqldb`<br>(incl. `/azuread`) | `1433` | `sqlserver://`<br>`server=...;` (ADO.NET) | TLS encryption, self-signed cert trust, named instances, Kerberos/AD auth, Microsoft Entra (`fedauth`), MultiSubnetFailover (Always On AG), packet sizing (`packet size`), MARS | `sqlserver://sa:pass@localhost:1433?database=mydb&encrypt=disable` |
| **SQLite** | `sqlite`<br>`sqlite3` | `modernc.org/sqlite` | N/A | `file:...`<br>`:memory:`<br>`/path/to/file.db` | Pure Go (zero CGO), WAL journal mode, busy timeouts, RAM caches, shared in-memory caches, read-only immutable mounts | `file:app.db?cache=shared&mode=rwc&_pragma=journal_mode(WAL)` |
| **Oracle Database** | `oracle`<br>`godror` | `github.com/sijms/go-ora/v2` | `1521` | `oracle://`<br>`user/pass@host:port/svc` | Pure Go (zero Oracle Client/OCI), Service Names & SIDs, Oracle Cloud Wallets (mTLS), bulk prefetch tuning | `oracle://user:pass@localhost:1521/XEPDB1` |
| **Redis & Valkey** | `redis`<br>`valkey` | `github.com/redis/go-redis/v9` | `6379` | `redis://`<br>`rediss://` (TLS)<br>`host:port` | In-memory key-value, pipelines, TLS verification, connection pooling, DB indexes | `redis://:password@localhost:6379/0?dial_timeout=5s` |
| **bbolt** | `bbolt`<br>`bolt` | `go.etcd.io/bbolt` | N/A | `/path/to/cache.db`<br>`./store.db` | Embedded ACID transactions, lock timeouts, POSIX file permissions | `./data/cache.db?timeout=1s` |
| **BadgerDB** | `badger`<br>`badgerdb` | `github.com/dgraph-io/badger/v4` | N/A | `/path/to/dir`<br>`badger://:memory:` | Pure Go LSM-tree KV, fast sequential writes, in-memory mode, Snappy compression | `./data/badger_data?sync_writes=false` |
| **etcd (v3)** | `etcd`<br>`etcdv3` | `go.etcd.io/etcd/client/v3` | `2379` | `http://host:port,...`<br>`https://host:port` (TLS) | Distributed consensus KV, multi-endpoint failover, client certificate authentication | `http://127.0.0.1:2379,http://127.0.0.1:22379` |

> [!IMPORTANT]
> **XML Entity Escaping**: When defining connection strings inside XML pipeline files (`<database connection_string="..." />`) or options files (`<options><dsn>...</dsn></options>`), all literal ampersands (`&`) separating URL query parameters **must be escaped as `&amp;`**.
> * Example: `postgresql://user:pass@localhost:5432/db?sslmode=disable&amp;connect_timeout=10`

---

## 2. Common Parameter Comparison Matrix

The following table summarizes common operational categories and how they are configured across the relational SQL drivers:

| Category | PostgreSQL (`lib/pq`) | MySQL (`go-sql-driver/mysql`) | SQL Server (`go-mssqldb`) | SQLite (`modernc.org/sqlite`) | Oracle (`go-ora`) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **SSL / TLS Mode** | `sslmode=disable`<br>`sslmode=require`<br>`sslmode=verify-full` | `tls=false`<br>`tls=true`<br>`tls=skip-verify` | `encrypt=disable`<br>`encrypt=true`<br>`encrypt=false` | N/A (Local / In-Process) | `SSL=FALSE`<br>`SSL=ENABLE`<br>`SSL VERIFY=FALSE` |
| **Trust Self-Signed Certs** | `sslmode=require` | `tls=skip-verify` | `TrustServerCertificate=true` | N/A | `SSL VERIFY=FALSE` |
| **Client Certificate (mTLS)** | `sslcert=...`<br>`sslkey=...`<br>`sslrootcert=...` | Registered via Go code or `tls=custom` | `certificate=...` | N/A | `WALLET=/path/to/wallet.zip` |
| **Connection Timeout** | `connect_timeout=10` *(seconds)* | `timeout=10s`<br>`readTimeout=30s` | `connection timeout=15` *(seconds)* | `_pragma=busy_timeout(5000)` *(ms)* | `CONNECTION TIMEOUT=10` *(seconds)* |
| **Timezone & Datetime** | `timezone=UTC` | `parseTime=true`<br>`loc=UTC` | Driver parses `DATETIME2` to UTC | Native epoch / ISO8601 strings | Converted via session time zone |
| **Application Identity** | `application_name=flow_app` | Handled via connection ID | `app name=flow_app`<br>`workstation id=host1` | N/A | `CLIENT IDENTIFIER=flow_app` |
| **Batch / Bulk Tuning** | `binary_parameters=yes` | `interpolateParams=true`<br>`maxAllowedPacket=67108864` | `packet size=32767` | `_pragma=journal_mode(WAL)`<br>`_pragma=synchronous(NORMAL)` | `PREFETCH_ROWS=1000` |
| **Federated / Cloud Identity** | IAM authentication tokens | IAM authentication tokens | Microsoft Entra Integrated (`fedauth=ActiveDirectoryIntegrated`)<br>Entra Default (`fedauth=ActiveDirectoryDefault`)<br>MSI (`fedauth=ActiveDirectoryMSI`) | N/A | OCI IAM / Wallets |
| **High Availability & Failover** | `target_session_attrs=primary` (multi-host) | Multi-host connection strings | `MultiSubnetFailover=true`<br>`ApplicationIntent=ReadOnly`<br>`failoverpartner=mirror` | N/A (Replicated / distributed disk) | Oracle RAC / SCAN Listeners |
| **Schema Isolation** | `search_path=staging,public` | Specified in database path | `database=catalog` | Attach secondary databases | Specified in table query |

---

## 3. PostgreSQL

The PostgreSQL driver (`github.com/lib/pq`) supports two distinct connection string formats: **URI format** (`postgresql://...`) and **Key-Value format** (`host=... port=...`).

### Available Options Reference

| Parameter | Type | Allowed Values / Format | Description |
| :--- | :---: | :--- | :--- |
| `sslmode` | string | `disable`, `require`, `verify-ca`, `verify-full` | Determines TLS verification rigor. `disable` skips encryption; `require` encrypts without CA validation; `verify-full` validates server certificate hostname against CA. |
| `sslrootcert` | path | Absolute or relative path to CA certificate | Root CA certificate file (PEM format) used to verify server identity. |
| `sslcert` | path | Absolute or relative path to client cert | Client certificate file (PEM format) for mutual authentication (mTLS). |
| `sslkey` | path | Absolute or relative path to client private key | Client private key file (PEM format, must have restricted POSIX permissions `0600`). |
| `connect_timeout` | integer | Number of seconds (e.g. `10`) | Maximum wait time for connection establishment before failing. `0` disables timeout. |
| `statement_timeout` | integer | Milliseconds (e.g. `30000`) | Aborts any SQL statement that takes longer than the specified number of milliseconds. |
| `application_name` | string | Text identifier (e.g. `flow_etl_worker`) | Sets the application name displayed in `pg_stat_activity` for auditing and DBA monitoring. |
| `fallback_application_name` | string | Text identifier | Fallback application name if `application_name` is not specified. |
| `search_path` | string | Comma-separated schemas (e.g. `analytics,public`) | Configures the schema search order for unqualified table and view references. |
| `timezone` | string | Time zone identifier (e.g. `UTC`, `America/New_York`) | Sets the session `TimeZone` configuration variable. |
| `client_encoding` | string | Character encoding (e.g. `UTF8`) | Configures the character encoding of SQL statements and returned results. |
| `binary_parameters` | string | `yes`, `no` | When set to `yes`, sends parameters in binary format for improved transmission speed. |
| `keepalives` | integer | `1` (enable) or `0` (disable) | Controls whether client-side TCP keepalives are enabled. |
| `keepalives_idle` | integer | Seconds | Number of seconds of inactivity before sending a keepalive probe. |
| `keepalives_interval` | integer | Seconds | Interval in seconds between keepalive retransmissions. |
| `keepalives_count` | integer | Integer count | Number of unacknowledged keepalive probes before dropping connection. |
| `sslsni` | integer | `1` (default) or `0` | Enables or disables Server Name Indication (SNI) during TLS handshake. |

### PostgreSQL Connection Examples

#### Example 1: Local Development (Plaintext / No SSL)
```text
postgresql://postgres:localDevPass123@localhost:5432/app_db?sslmode=disable
```
*Equivalent Key-Value format:*
```text
host=localhost port=5432 user=postgres password=localDevPass123 dbname=app_db sslmode=disable
```

#### Example 2: Production Cloud Database (AWS RDS / GCP Cloud SQL / Neon / Supabase)
Requires TLS encryption with connection timeout and UTC session timezone:
```text
postgresql://app_user:StrongProdPass2026!@db.internal.company.com:5432/analytics_prod?sslmode=require&connect_timeout=10&timezone=UTC&application_name=flow_pipeline
```
*XML pipeline syntax (note the escaped `&amp;`):*
```xml
<database name="analytics_db"
          driver="postgres"
          connection_string="postgresql://app_user:StrongProdPass2026!@db.internal.company.com:5432/analytics_prod?sslmode=require&amp;connect_timeout=10&amp;timezone=UTC&amp;application_name=flow_pipeline" />
```

#### Example 3: High-Security Mutual TLS (mTLS) with Root CA Verification
Validates the server certificate against a custom root certificate authority and authenticates the client using client certificates:
```text
host=secure-db.internal port=5432 user=etl_service dbname=finance sslmode=verify-full sslrootcert=/etc/ssl/certs/internal_ca.pem sslcert=/etc/ssl/certs/etl_client.pem sslkey=/etc/ssl/certs/etl_client_key.pem connect_timeout=15
```

#### Example 4: Schema Isolation with Search Path & Statement Guardrails
Directs queries to the `staging` schema first and aborts long-running statements after 60 seconds:
```text
postgresql://loader:SecretPass@db.internal:5432/warehouse?sslmode=require&search_path=staging,shared,public&statement_timeout=60000&application_name=batch_loader
```

#### Example 5: High-Speed Local Unix Domain Socket
Avoids loopback TCP stack overhead on Linux / macOS servers:
```text
host=/var/run/postgresql user=postgres dbname=app_db sslmode=disable
```

---

## 4. MySQL & MariaDB

The MySQL driver (`github.com/go-sql-driver/mysql`) uses Data Source Names (DSNs) structured as:
```text
[username[:password]@][protocol[(address)]]/dbname[?param1=value1&param2=value2]
```

### Available Options Reference

| Parameter | Type | Allowed Values / Format | Description |
| :--- | :---: | :--- | :--- |
| `parseTime` | boolean | `true`, `false` (default `false`) | Automatically parses `DATE` and `DATETIME` columns into Go `time.Time` values instead of raw `[]byte`. **Essential for ETL pipelines.** |
| `loc` | string | `Local`, `UTC`, or URL-encoded IANA location (e.g. `America%2FNew_York`) | Sets the timezone location used when parsing datetime values. |
| `tls` | string | `false`, `true`, `skip-verify`, `preferred`, or custom profile name | `false` disables TLS; `true` requires valid server cert; `skip-verify` enables TLS but skips certificate chain verification (useful for self-signed certificates). |
| `timeout` | duration | Duration string (e.g. `5s`, `10s`) | Timeout for establishing the network connection (dial timeout). |
| `readTimeout` | duration | Duration string (e.g. `30s`, `1m`) | I/O read timeout on established connections. |
| `writeTimeout` | duration | Duration string (e.g. `30s`, `1m`) | I/O write timeout on established connections. |
| `interpolateParams` | boolean | `true`, `false` (default `false`) | Interpolates placeholders client-side into a single query string instead of preparing separate statements. **Significantly accelerates bulk inserts and batch updates.** |
| `multiStatements` | boolean | `true`, `false` (default `false`) | Allows executing multiple semicolon-delimited SQL statements in a single query call (e.g. DDL scripts, multi-table transactions). |
| `collation` | string | MySQL Collation name (e.g. `utf8mb4_unicode_ci`, `utf8mb4_general_ci`) | Sets the character set collation for client communication. |
| `charset` | string | Charset name (e.g. `utf8mb4`, `utf8`) | Sets the connection character set. |
| `maxAllowedPacket` | integer | Size in bytes (e.g. `67108864` for 64MB) | Sets maximum packet size accepted from the MySQL server. |
| `clientFoundRows` | boolean | `true`, `false` (default `false`) | Returns matching rows instead of changed rows for `UPDATE` statements. |
| `allowNativePasswords` | boolean | `true`, `false` (default `true`) | Allows the `mysql_native_password` authentication method. |
| `checkConnLiveness` | boolean | `true`, `false` (default `true`) | Performs connection health check before executing queries. |
| `autocommit` | boolean | `true`, `false` (default `true`) | Controls automatic transaction commitment. |

### MySQL Connection Examples

#### Example 1: Standard Local Development (TCP with Datetime Parsing)
```text
root:localPassword123@tcp(localhost:3306)/flow_data?parseTime=true&loc=Local
```

#### Example 2: High-Performance Bulk ETL Pipeline
Enables parameter interpolation for rapid bulk insertions, 64MB packet buffers, and explicit I/O timeouts:
```text
etl_writer:ETLSecretPass2026!@tcp(db-cluster.internal:3306)/sales_warehouse?parseTime=true&loc=UTC&interpolateParams=true&maxAllowedPacket=67108864&timeout=10s&readTimeout=60s&writeTimeout=60s
```
*XML pipeline syntax (with escaped `&amp;`):*
```xml
<database name="sales_warehouse"
          driver="mysql"
          connection_string="etl_writer:ETLSecretPass2026!@tcp(db-cluster.internal:3306)/sales_warehouse?parseTime=true&amp;loc=UTC&amp;interpolateParams=true&amp;maxAllowedPacket=67108864&amp;timeout=10s&amp;readTimeout=60s&amp;writeTimeout=60s" />
```

#### Example 3: Cloud Database with Managed SSL / Self-Signed Skip-Verify
Suitable for Amazon RDS, Google Cloud SQL, or DigitalOcean Managed MySQL when intermediate CA certs are not locally configured:
```text
app_service:ProdPassword99@tcp(mysql-master.c7xyz.us-east-1.rds.amazonaws.com:3306)/production?parseTime=true&loc=UTC&tls=skip-verify&timeout=15s
```

#### Example 4: Local High-Speed Unix Domain Socket
Directly connects to local daemon via Unix socket without TCP networking:
```text
flow_user:Pass123!@unix(/var/run/mysqld/mysqld.sock)/production_db?parseTime=true&loc=UTC
```

#### Example 5: Migration Runner (Multi-Statement Support)
Enables running multi-statement schema migration files containing semicolons:
```text
admin_user:AdminPass123@tcp(127.0.0.1:3306)/app_schema?multiStatements=true&parseTime=true
```

#### Example 6: IPv6 Loopback Connection
```text
flow_app:MyPassword@tcp([::1]:3306)/ipv6_test?parseTime=true
```

---

## 5. Microsoft SQL Server & Azure SQL

The Microsoft SQL Server driver (`github.com/microsoft/go-mssqldb`) supports both **URL format** (`sqlserver://...`) and **ADO.NET / ODBC semi-colon key-value format** (`server=...;database=...;`).

### Available Options Reference

| Parameter (URL / ADO.NET) | Type | Allowed Values / Format | Description |
| :--- | :---: | :--- | :--- |
| `database` | string | Database name | Target database / catalog name to switch to upon connecting. |
| `encrypt` | string / bool | `true`, `false`, `disable` | `true` enforces TLS/SSL encryption; `disable` or `false` allows unencrypted connections. |
| `TrustServerCertificate`<br>`trustservercertificate` | boolean | `true`, `false` (default `false`) | When set to `true`, trusts self-signed server certificates without CA validation. **Essential for local Docker or test instances.** |
| `connection timeout` | integer | Seconds (e.g. `15`) | Connection establishment timeout. |
| `app name`<br>`application name` | string | String identifier (e.g. `Flow_ETL_Service`) | Surfaces client application name in SQL Server dynamic management views (`sys.dm_exec_sessions`). |
| `workstation id` | string | Hostname (e.g. `ETL-NODE-01`) | Workstation identifier logged in SQL Server connection telemetry. |
| `packet size` | integer | Bytes (`512` to `32767`, default `4096`) | Network Packet Size (TDS buffer). **Setting to `32767` significantly accelerates high-volume ETL bulk copies.** |
| `multisubnetfailover`<br>`MultiSubnetFailover` | boolean | `true`, `false`, `Enabled`, `Disabled` | When `true`, connects in parallel to all IP addresses resolved for an Always On Availability Group listener across multi-subnet clusters. |
| `applicationintent`<br>`ApplicationIntent` | string | `ReadOnly`, `ReadWrite` | Directs read-only queries to secondary replicas in Always On Availability Group configurations (requires `database` parameter). |
| `MultipleActiveResultSets`<br>`mars` | boolean | `true`, `false` | Enables Multiple Active Result Sets (MARS) on a single connection. |
| `failoverpartner` | string | Hostname or IP of mirror server | Specifies the database mirroring or Always On Availability Group failover partner. |
| `keepAlive` | integer | Seconds (default `30`) | Frequency of TCP keep-alive packets sent to detect dead connections. |
| `log` | integer | Integer bitmask (e.g. `63`) | Enables internal driver logging to standard error for troubleshooting connection negotiation. |
| `fedauth` | string | `ActiveDirectoryIntegrated`<br>`ActiveDirectoryDefault`<br>`ActiveDirectoryMSI`<br>`ActiveDirectoryServicePrincipal`<br>`ActiveDirectoryInteractive`<br>`ActiveDirectoryPassword` | Microsoft Entra ID (formerly Azure Active Directory) authentication workflow. |
| `applicationclientid` | string | UUID (Client / Application ID) | Azure Client ID required for `ActiveDirectoryPassword` and `ActiveDirectoryInteractive`. |
| `clientcertpath` | path | File path to `.pem` certificate | Certificate path for certificate-based Service Principal authentication. |
| `resource id` | string | Azure Resource ID | Azure resource ID when using User-Assigned Managed Identity (`ActiveDirectoryMSI`). |
| `additionallyallowedtenants` | string | Comma-separated tenant IDs | Allowed additional tenant IDs for multi-tenant Microsoft Entra authentication. |
| `integrated security` | string / bool | `true`, `sspi` | Uses Windows Integrated Authentication (SSPI / Kerberos) under on-premises Windows Active Directory. |

### SQL Server Connection Examples

#### Example 1: Local Development / Docker (Plaintext Encryption Disabled)
```text
sqlserver://sa:YourStrongPassword123!@localhost:1433?database=master&encrypt=disable
```
*Equivalent ADO.NET key-value format:*
```text
server=localhost;port=1433;user id=sa;password=YourStrongPassword123!;database=master;encrypt=disable;
```

#### Example 2: Local Staging with Self-Signed Certificate
Enables TLS encryption while instructing the client to trust the self-signed certificate:
```text
sqlserver://sa:YourStrongPassword123!@localhost:1433?database=telemetry&encrypt=true&TrustServerCertificate=true
```
*XML pipeline syntax (note the escaped `&amp;`):*
```xml
<database name="telemetry_db"
          driver="sqlserver"
          connection_string="sqlserver://sa:YourStrongPassword123!@localhost:1433?database=telemetry&amp;encrypt=true&amp;TrustServerCertificate=true" />
```

#### Example 3: Azure SQL Database (SQL Authentication)
Azure SQL requires encryption enabled and authenticates using username and server suffix:
```text
sqlserver://appadmin@my-sql-srv:ComplexAzurePass999!@my-sql-srv.database.windows.net:1433?database=enterprise_db&encrypt=true&connection+timeout=30&app+name=Flow_Pipeline
```

#### Example 4: Named Instance Connection
Connects to a specific named instance (e.g. `SQLEXPRESS` or `REPORTING`):
```text
sqlserver://sa:StrongPass123!@db-host\SQLEXPRESS?database=orders&encrypt=disable
```
*ADO.NET format:*
```text
server=db-host\SQLEXPRESS;user id=sa;password=StrongPass123!;database=orders;encrypt=disable;
```

#### Example 5: High-Throughput Bulk ETL (Max TDS Packet Size)
Tunes network TDS buffers from the 4KB default to 32KB (`32767` bytes) to maximize network throughput during large data warehouse bulk copies, and logs workstation identity:

*URL format (spaces encoded as `+` or `%20`):*
```text
sqlserver://etl_operator:SecretPass2026!@sql-cluster.internal:1433?database=staging_area&encrypt=true&TrustServerCertificate=true&packet+size=32767&app+name=Flow_Bulk_Loader&workstation+id=ETL-HOST-01
```

*Equivalent ADO.NET semi-colon format:*
```text
server=sql-cluster.internal;port=1433;user id=etl_operator;password=SecretPass2026!;database=staging_area;encrypt=true;TrustServerCertificate=true;packet size=32767;app name=Flow_Bulk_Loader;workstation id=ETL-HOST-01;
```

*XML pipeline definition (note the escaped `&amp;`):*
```xml
<database name="bulk_staging_db"
          driver="sqlserver"
          connection_string="sqlserver://etl_operator:SecretPass2026!@sql-cluster.internal:1433?database=staging_area&amp;encrypt=true&amp;TrustServerCertificate=true&amp;packet+size=32767&amp;app+name=Flow_Bulk_Loader&amp;workstation+id=ETL-HOST-01"
          max_open_conns="50"
          max_idle_conns="10" />
```

#### Example 6: On-Premises Windows Integrated Authentication (Kerberos / NTLM)
Uses current on-premises Windows domain logon credentials without storing passwords in configuration files:
```text
server=localhost;database=finance;integrated security=true;
```

#### Example 7: Multi-Subnet Failover (Always On Availability Groups)
In SQL Server Always On Availability Group (AG) architectures spanning multiple subnets or disaster recovery data centers, an AG listener registers multiple IP addresses in DNS. Setting `MultiSubnetFailover=true` instructs the driver to attempt parallel TCP connections to all resolved IP addresses simultaneously. Whichever replica answers first (the currently active primary or secondary node) wins the connection immediately—slashing failover discovery time from standard 20–30 second TCP timeout delays to milliseconds.

Combine with `ApplicationIntent=ReadOnly` to offload analytical or reporting queries directly to secondary read-only replicas:

*URL format:*
```text
sqlserver://app_etl:SecurePassword2026!@ag-listener.corp.internal:1433?database=enterprise_reporting&multisubnetfailover=true&applicationintent=ReadOnly&encrypt=true&TrustServerCertificate=true
```

*Equivalent ADO.NET semi-colon format:*
```text
server=ag-listener.corp.internal;database=enterprise_reporting;user id=app_etl;password=SecurePassword2026!;MultiSubnetFailover=True;ApplicationIntent=ReadOnly;encrypt=true;TrustServerCertificate=true;
```

*XML pipeline definition:*
```xml
<database name="ag_reporting_db"
          driver="sqlserver"
          connection_string="sqlserver://app_etl:SecurePassword2026!@ag-listener.corp.internal:1433?database=enterprise_reporting&amp;multisubnetfailover=true&amp;applicationintent=ReadOnly&amp;encrypt=true&amp;TrustServerCertificate=true"
          max_open_conns="20"
          max_idle_conns="5"
          workload="analytics" />
```

#### Example 8: Multiple Active Result Sets (MARS)
**Multiple Active Result Sets (MARS)** allows executing multiple batches or interleaving queries across a single physical database connection without buffering the entire result set in client memory before issuing another command on that connection.

*URL format:*
```text
sqlserver://flow_app:OrderPass2026!@sql-srv.internal:1433?database=order_processing&MultipleActiveResultSets=true&encrypt=true&TrustServerCertificate=true
```

*Equivalent ADO.NET semi-colon format:*
```text
server=sql-srv.internal;database=order_processing;user id=flow_app;password=OrderPass2026!;MultipleActiveResultSets=True;encrypt=true;TrustServerCertificate=true;
```

*XML pipeline definition:*
```xml
<database name="orders_mars_db"
          driver="sqlserver"
          connection_string="sqlserver://flow_app:OrderPass2026!@sql-srv.internal:1433?database=order_processing&amp;MultipleActiveResultSets=true&amp;encrypt=true&amp;TrustServerCertificate=true" />
```

> [!NOTE]
> **Go Concurrency vs. MARS**: In Go's `database/sql`, concurrent queries dispatched across goroutines automatically receive separate physical connections managed by the connection pool (`max_open_conns`). For single queries or stored procedures returning multiple result sets (e.g. `SELECT * FROM headers; SELECT * FROM lines;`), Go processes each result set sequentially using `rows.NextResultSet()` natively. Specifying `MultipleActiveResultSets=True` is primarily utilized when porting legacy .NET connection strings, connecting through proxies that require the MARS prelogin flag, or when integrating with frameworks expecting MARS-enabled endpoints.

---

### Microsoft Entra ID (Azure AD) Connection Examples

`go-flow` provides native support for **Microsoft Entra ID** authentication (via `github.com/microsoft/go-mssqldb/azuread`). You can specify the driver as either `azuresql` or `sqlserver` (Flow automatically routes connection strings containing `fedauth=` to the Entra ID token provider).

#### Example 9: Microsoft Entra Integrated Authentication (Single Sign-On / SSO)
**Microsoft Entra Integrated** (`fedauth=ActiveDirectoryIntegrated`) is designed for machines that are domain-joined to on-premises Active Directory federated with Microsoft Entra ID, or joined directly via Microsoft Entra Hybrid Join. It uses the active Windows session ticket via Kerberos/WS-Trust to seamlessly negotiate an Entra access token **without requiring passwords or secrets in configuration files**.

*URL format:*
```text
sqlserver://my-sql-srv.database.windows.net?database=sales_analytics&fedauth=ActiveDirectoryIntegrated&encrypt=true
```

*ADO.NET semi-colon format:*
```text
server=my-sql-srv.database.windows.net;database=sales_analytics;fedauth=ActiveDirectoryIntegrated;encrypt=true;
```

*Standard ODBC / ADO.NET alias syntax:*
```text
server=my-sql-srv.database.windows.net;database=sales_analytics;Authentication=Active Directory Integrated;encrypt=true;
```

*XML pipeline definition (using either `driver="azuresql"` or `driver="sqlserver"`):*
```xml
<database name="sales_dw"
          driver="azuresql"
          connection_string="sqlserver://my-sql-srv.database.windows.net?database=sales_analytics&amp;fedauth=ActiveDirectoryIntegrated&amp;encrypt=true"
          max_open_conns="25"
          max_idle_conns="5"
          workload="analytics" />
```

*CLI execution:*
```powershell
.\flow.exe -dsn "sqlserver://my-sql-srv.database.windows.net?database=sales_analytics&fedauth=ActiveDirectoryIntegrated&encrypt=true"
```

#### Example 10: Microsoft Entra Default (`DefaultAzureCredential`)
Uses the Azure SDK `DefaultAzureCredential` provider. It dynamically resolves credentials from:
1. Environment variables (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`)
2. Workload Identity (AKS / Kubernetes pod service accounts)
3. Managed Identity (Azure VMs, Azure Container Apps, Azure App Services)
4. Azure Developer CLI (`az login` or `azd login`)
5. Visual Studio Code credentials

*URL format:*
```text
sqlserver://my-sql-srv.database.windows.net?database=enterprise_dw&fedauth=ActiveDirectoryDefault&encrypt=true
```

*XML pipeline definition:*
```xml
<database name="azure_dw"
          driver="azuresql"
          connection_string="sqlserver://my-sql-srv.database.windows.net?database=enterprise_dw&amp;fedauth=ActiveDirectoryDefault&amp;encrypt=true" />
```

#### Example 11: Microsoft Entra Managed Identity (System-Assigned & User-Assigned MSI)
Ideal for automated workloads running on Azure Virtual Machines, Azure Functions, Azure Container Instances (ACI), or Azure Kubernetes Service (AKS).

*System-Assigned Managed Identity:*
```text
sqlserver://my-sql-srv.database.windows.net?database=finance&fedauth=ActiveDirectoryMSI&encrypt=true
```

*User-Assigned Managed Identity (specifying Client ID):*
```text
sqlserver://my-sql-srv.database.windows.net?database=finance&fedauth=ActiveDirectoryMSI&user+id=11111111-2222-3333-4444-555555555555&encrypt=true
```

*User-Assigned Managed Identity (specifying Resource ID):*
```text
server=my-sql-srv.database.windows.net;database=finance;fedauth=ActiveDirectoryMSI;resource id=/subscriptions/sub-id/resourceGroups/rg-name/providers/Microsoft.ManagedIdentity/userAssignedIdentities/my-identity;encrypt=true;
```

#### Example 12: Microsoft Entra Service Principal (Headless CI/CD & Automated ETL)
Used when ETL pipelines run unattended outside Azure (e.g. on-premises data centers, AWS, GCP, or automated Jenkins/GitHub Actions runners).

*With Client Secret:*
```text
server=my-sql-srv.database.windows.net;database=warehouse;fedauth=ActiveDirectoryServicePrincipal;user id=00000000-0000-0000-0000-000000000000@tenant-uuid-1234;password=YourClientSecretKey!;encrypt=true;
```
*URL equivalent:*
```text
sqlserver://00000000-0000-0000-0000-000000000000@tenant-uuid-1234:YourClientSecretKey!@my-sql-srv.database.windows.net?database=warehouse&fedauth=ActiveDirectoryServicePrincipal&encrypt=true
```

*With Client Certificate (mTLS with Microsoft Entra):*
```text
server=my-sql-srv.database.windows.net;database=warehouse;fedauth=ActiveDirectoryServicePrincipal;user id=00000000-0000-0000-0000-000000000000@tenant-uuid-1234;clientcertpath=/etc/ssl/certs/service_principal.pem;encrypt=true;
```

#### Example 13: Microsoft Entra Interactive (Browser MFA Prompt)
Prompts the operator with an interactive browser or MFA authenticator prompt. Recommended for local developer workstations using Builder UI:
```text
sqlserver://admin@company.com@my-sql-srv.database.windows.net?database=dev_db&fedauth=ActiveDirectoryInteractive&applicationclientid=00000000-0000-0000-0000-000000000000&encrypt=true
```

#### Example 14: Microsoft Entra Password (ROPC Flow)
Authenticates with an Entra username and password directly (non-MFA accounts only):
```text
sqlserver://user@company.com:MyEntraPass2026!@my-sql-srv.database.windows.net?database=staging&fedauth=ActiveDirectoryPassword&applicationclientid=00000000-0000-0000-0000-000000000000&encrypt=true
```

---

## 6. SQLite

`go-flow` utilizes `modernc.org/sqlite`, a 100% pure Go SQLite engine that requires **no CGO and no external C compiler**. Connections are specified using standard filesystem paths or URI syntax.

### Available Options & Pragmas Reference

| Parameter / Pragma | Type | Allowed Values / Format | Description |
| :--- | :---: | :--- | :--- |
| `mode` | string | `ro` (read-only)<br>`rw` (read-write)<br>`rwc` (read-write-create)<br>`memory` (in-memory) | Access mode for opening database files. `rwc` creates the file if it does not already exist. |
| `cache` | string | `shared`, `private` | Controls shared-cache mode among multiple connections within the same process. |
| `immutable` | integer | `1`, `0` | Informs SQLite that the underlying file is on read-only media and will never be modified. Skips all locking and shared memory. |
| `_pragma=journal_mode(...)` | pragma | `WAL`, `DELETE`, `TRUNCATE`, `MEMORY`, `OFF` | `WAL` (Write-Ahead Logging) provides maximum concurrency, allowing readers and writers to operate simultaneously without locking. |
| `_pragma=synchronous(...)` | pragma | `NORMAL`, `FULL`, `OFF` | In WAL mode, `NORMAL` ensures database durability across application crashes while dramatically speeding up write transactions. |
| `_pragma=busy_timeout(...)` | pragma | Milliseconds (e.g. `5000`) | Sleep time before returning `SQLITE_BUSY` when waiting for another write lock to release. |
| `_pragma=foreign_keys(...)` | pragma | `1` (on), `0` (off) | Enforces relational foreign key constraints. (SQLite defaults to off). |
| `_pragma=temp_store(...)` | pragma | `MEMORY`, `FILE`, `DEFAULT` | Stores temporary tables and scratch query sorting indexes in RAM instead of disk files. |
| `_pragma=cache_size(...)` | pragma | Negative integer (e.g. `-64000` = 64MB) | Sets the memory page cache allocated to the connection handle. |
| `_pragma=locking_mode(...)` | pragma | `NORMAL`, `EXCLUSIVE` | `EXCLUSIVE` retains database file locks for the lifetime of the connection to maximize write throughput in single-process ETL. |
| `_pragma=mmap_size(...)` | pragma | Bytes (e.g. `268435456` = 256MB) | Enables memory-mapped I/O up to the specified size for ultra-fast reading. |

### SQLite Connection Examples

#### Example 1: High-Performance Production File (WAL Mode)
Configures Write-Ahead Logging, 5-second lock timeout, RAM temp storage, and 64MB memory page cache:
```text
file:./data/production.db?cache=shared&mode=rwc&_pragma=journal_mode(WAL)&_pragma=synchronous(NORMAL)&_pragma=busy_timeout(5000)&_pragma=temp_store(MEMORY)&_pragma=cache_size(-64000)&_pragma=foreign_keys(1)
```
*XML pipeline syntax (note the escaped `&amp;`):*
```xml
<database name="local_warehouse"
          driver="sqlite"
          connection_string="file:./data/production.db?cache=shared&amp;mode=rwc&amp;_pragma=journal_mode(WAL)&amp;_pragma=synchronous(NORMAL)&amp;_pragma=busy_timeout(5000)&amp;_pragma=temp_store(MEMORY)&amp;_pragma=cache_size(-64000)&amp;_pragma=foreign_keys(1)" />
```

#### Example 2: Simple Local File Path
Creates or connects to a local database file using default SQLite settings:
```text
./data/application.db
```
*On Windows:*
```text
C:\etl\databases\application.db
```

#### Example 3: Shared In-Memory Database (Ephemeral)
Lives entirely in memory, persists across pipeline steps for the lifetime of the process, and is shared among all connection pool threads:
```text
file::memory:?cache=shared
```

#### Example 4: Isolated In-Memory Database
```text
:memory:
```

#### Example 5: Read-Only Immutable Database
Opens an existing reference database in strict read-only mode, bypassing all file lock overhead (ideal for Docker containers, read-only NFS volumes, or distribution bundles):
```text
file:/opt/data/geo_reference.db?mode=ro&immutable=1
```

---

## 7. Oracle Database

The Oracle driver (`github.com/sijms/go-ora/v2`) is a **pure Go Oracle client implementation**. It connects directly to Oracle Database instances over TCP/IP without requiring Oracle Instant Client, CGO, or `libclntsh.so`.

### Available Options Reference

| Parameter | Type | Allowed Values / Format | Description |
| :--- | :---: | :--- | :--- |
| `CONNECTION TIMEOUT` / `timeout` | integer | Seconds (e.g. `10`) | TCP dial timeout before aborting connection attempts. |
| `SSL` | string | `ENABLE`, `TRUE`, `FALSE` | Enables SSL/TLS transport encryption (TCPS protocol). |
| `SSL VERIFY` | string | `TRUE`, `FALSE` | Validates server SSL certificate validity. Set to `FALSE` for self-signed certificates. |
| `WALLET` | path | Path to Oracle Cloud Wallet (`cwallet.sso` or `.zip`) | Points to client credentials wallet for Oracle Cloud Autonomous Databases (ATP / ADW). |
| `SERVER` | string | `DEDICATED`, `SHARED` | Instructs the Oracle TNS listener to assign a dedicated server process. |
| `AUTH TYPE` | string | `DEFAULT`, `SYSDBA`, `SYSOPER` | Privileged administrative logon mode. |
| `PREFETCH_ROWS` | integer | Number of rows (e.g. `1000`) | Number of rows prefetched per network round-trip. **Essential for large ETL SELECT queries.** |
| `CLIENT IDENTIFIER` | string | Text identifier (e.g. `flow_etl`) | Client identity stored in `V$SESSION` for enterprise database auditing. |
| `TRACE FILE` | path | File path (e.g. `./oracle_trace.log`) | Dumps internal TDS protocol packet traces for low-level debugging. |

### Oracle Connection Examples

#### Example 1: Standard Service Name Connection
Connects to an Oracle Pluggable Database (PDB) via Service Name:
```text
oracle://app_user:SecretPassword123@localhost:1521/XEPDB1
```

#### Example 2: Legacy SID (System Identifier) Connection
Connects using an Oracle SID (e.g. `ORCL`):
```text
oracle://system:ManagerPassword@db-host.internal:1521/ORCL
```

#### Example 3: High-Performance Bulk ETL with Prefetching
Configures dedicated server processes, 10-second timeout, and 2,500 row network prefetch buffers:
```text
oracle://etl_reader:ComplexPass999!@oracle-cluster.internal:1521/PROD_SVC?SERVER=DEDICATED&CONNECTION+TIMEOUT=10&PREFETCH_ROWS=2500&CLIENT+IDENTIFIER=Flow_ETL_Runner
```
*XML pipeline syntax (note the escaped `&amp;`):*
```xml
<database name="oracle_dw"
          driver="oracle"
          connection_string="oracle://etl_reader:ComplexPass999!@oracle-cluster.internal:1521/PROD_SVC?SERVER=DEDICATED&amp;CONNECTION+TIMEOUT=10&amp;PREFETCH_ROWS=2500&amp;CLIENT+IDENTIFIER=Flow_ETL_Runner" />
```

#### Example 4: Oracle Autonomous Cloud Database (ATP / ADW via Wallet)
Connects securely to Oracle Cloud Infrastructure (OCI) using mutual TLS and the client wallet bundle:
```text
oracle://admin:MyCloudPassword123@adb.us-ashburn-1.oraclecloud.com:1522/myatp_high?SSL=ENABLE&WALLET=/etc/oracle/wallets/Wallet_myatp.zip
```

#### Example 5: EZCONNECT Format
```text
scott/tiger@localhost:1521/XEPDB1
```

---

## 8. Key-Value Storage Engines

In addition to relational SQL databases, `go-flow` includes first-class support for embedded and distributed Key-Value (KV) storage engines.

### Redis & Valkey

Supports single-node instances, managed Redis caches (e.g. AWS ElastiCache, Azure Redis, Google Memorystore), and TLS-encrypted clusters.

#### Format:
```text
redis://[:password@]host[:port][/db_index][?param1=val1&param2=val2]
```
*For TLS / SSL connections:*
```text
rediss://[:password@]host[:port][/db_index][?param1=val1&param2=val2]
```

#### Examples:
```text
# Local unauthenticated Redis instance (Database 0)
redis://localhost:6379/0

# Authenticated Redis with password, custom database, and timeouts
redis://:SecretRedisPass2026!@redis.internal:6379/2?dial_timeout=5s&read_timeout=3s&write_timeout=3s

# Encrypted Redis over TLS (rediss://)
rediss://:SecurePass@redis-cache.us-east-1.cache.amazonaws.com:6379/0?dial_timeout=10s
```

---

### bbolt (Embedded KV)

`bbolt` is an embedded, transactional key-value store stored in a single binary file.

#### Format:
```text
/path/to/database.db[?timeout=duration&mode=octal]
```

#### Examples:
```text
# Standard local cache database file
./data/kv_cache.db

# With explicit lock acquisition timeout
./data/kv_cache.db?timeout=2s
```

---

### BadgerDB (Embedded LSM-Tree KV)

`BadgerDB` is an embedded Key-Value engine optimized for solid-state storage (SSDs) using Log-Structured Merge (LSM) trees.

#### Format:
```text
/path/to/storage_directory[?sync_writes=bool]
```
*For in-memory ephemeral KV store:*
```text
badger://:memory:
```

#### Examples:
```text
# Local directory on SSD with asynchronous write-flushing for maximum ingestion throughput
./data/badger_store?sync_writes=false

# In-memory ephemeral KV store
badger://:memory:
```

---

### etcd (Distributed KV v3)

Supports distributed configuration and consensus lookups across multi-node clusters.

#### Format:
```text
http://endpoint1:port,http://endpoint2:port
```
*With client TLS certificates:*
```text
https://etcd1.internal:2379,https://etcd2.internal:2379?ca_cert=/path/to/ca.pem&cert=/path/to/cert.pem&key=/path/to/key.pem
```

---

## 9. Flow Pipeline Configuration Integration

### Defining Databases in `<pipeline>` XML

Databases are registered in the `<databases>` block of a pipeline definition. Connection strings support **variable interpolation** (e.g. `{{DB_PASSWORD}}` or `{{DB_HOST}}`), enabling credentials to be injected securely at runtime without hardcoding.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">

    <databases>
        <!-- 1. PostgreSQL Data Warehouse -->
        <database name="dw_postgres"
                  driver="postgres"
                  connection_string="postgresql://etl_app:{{PG_PASSWORD}}@postgres.internal:5432/warehouse?sslmode=require&amp;connect_timeout=10"
                  max_open_conns="50"
                  max_idle_conns="10"
                  conn_max_lifetime_seconds="300"
                  workload="bulk" />

        <!-- 2. SQL Server Source -->
        <database name="source_mssql"
                  driver="sqlserver"
                  connection_string="sqlserver://sa:{{MSSQL_PASSWORD}}@sql-srv.internal:1433?database=Orders&amp;encrypt=true&amp;TrustServerCertificate=true&amp;packet+size=32767"
                  max_open_conns="25"
                  max_idle_conns="5"
                  workload="oltp" />

        <!-- 3. Local High-Throughput SQLite Staging -->
        <database name="staging_sqlite"
                  driver="sqlite"
                  connection_string="file:./scratch/staging.db?cache=shared&amp;mode=rwc&amp;_pragma=journal_mode(WAL)&amp;_pragma=synchronous(NORMAL)&amp;_pragma=busy_timeout(10000)"
                  max_open_conns="1"
                  workload="batch" />
    </databases>

    <flow>
        <!-- Steps referencing registered databases by name -->
        <sql id="extract_orders" db="source_mssql">
            SELECT order_id, customer_id, total_amount FROM orders WHERE status = 'PENDING';
        </sql>
    </flow>
</pipeline>
```

---

### Telemetry & Execution Logging Database (`log_db`)

When a database element is named `log_db`, `go-flow` automatically directs all pipeline execution telemetry, step events, row counts, and database SPID tracking to that database:

```xml
<database name="log_db"
          driver="sqlserver"
          connection_string="sqlserver://telemetry_user:{{LOG_PASSWORD}}@telemetry.internal:1433?database=pipeline_telemetry&amp;encrypt=true&amp;TrustServerCertificate=true" />
```

---

### Loading Pipelines Directly from Databases (`sql://` / `db://`)

`go-flow` can fetch pipeline XML definitions dynamically from a database repository using the `sql://` source prefix:

```text
sql://<driver>@<connection_string>#<SQL_SELECT_QUERY>
```

#### Examples:
```powershell
# From PostgreSQL
.\flow.exe -source "sql://postgres@postgresql://app:SecretPass@localhost:5432/pipelines?sslmode=disable#SELECT xml_content FROM pipeline_catalog WHERE pipeline_name = 'daily_sales'"

# From SQL Server
.\flow.exe -source "sql://sqlserver@sqlserver://sa:SecretPass!@localhost:1433?database=master&encrypt=disable#SELECT definition_xml FROM dbo.etl_scripts WHERE id = 42"

# From SQLite
.\flow.exe -source "sql://sqlite@file:./repos/scripts.db?mode=ro#SELECT xml_body FROM registry WHERE name = 'extract_orders'"
```

---

## 10. Security Best Practices for Connection Strings

1. **Never Hardcode Plaintext Passwords in Version Control**:
   - Use variable substitution: `connection_string="postgresql://app:{{SECRET_DB_PASS}}@host:5432/db"`.
   - Pass runtime values using `-var SECRET_DB_PASS=MyPassword` or environment variables `FLOW_VAR_SECRET_DB_PASS=MyPassword`.
2. **Encrypt Options and Pipeline Files**:
   - Use `crypto_tool` to encrypt options and pipeline files with AES-256-GCM.
   - Supply decryption keys via `-key-file` to prevent credentials from being exposed in system process listings (`ps` or Process Hacker).
3. **Always Escape Ampersands in XML**:
   - Remember that XML parsers reject bare `&` characters in attribute values. Always write `&amp;` when joining query parameters inside `<database connection_string="..." />`.
4. **Enforce TLS Verification in Production**:
   - For PostgreSQL: Use `sslmode=verify-full` or `sslmode=verify-ca`.
   - For MySQL: Use `tls=true` or registered CA bundles.
   - For SQL Server: Use `encrypt=true` and avoid `TrustServerCertificate=true` in untrusted networks.
5. **Tune Connection Pool Sizes for ETL**:
   - Avoid setting `max_open_conns` excessively high when executing parallel pipelines against shared databases.
   - For SQLite, set `max_open_conns="1"` or use WAL mode (`_pragma=journal_mode(WAL)`) to avoid database lock contention.
