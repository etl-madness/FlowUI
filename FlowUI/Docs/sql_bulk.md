# SQL Bulk Streaming Replication Node (`<sql_bulk>`) Guide 🚀

The `<sql_bulk>` node is an optimized pipeline streaming primitive that extracts rows from a source SQL query and streams them directly into a target destination table with constant $O(1)$ memory usage. It bypasses intermediate disk files (CSV, Parquet, JSON) and eliminates memory bloat, safely copying tens of millions of records across heterogeneous database engines.

---

## 📋 Attribute Specifications

| Attribute | Required | Default | Description |
| :--- | :---: | :---: | :--- |
| `id` | **Yes** | — | Unique step identifier across the pipeline. |
| `db` / `database` | **Yes** | — | Source database connection declared in `<databases>`. |
| `target_table` / `table` | **Yes** | — | Destination table name receiving bulk inserts. |
| `target_db` / `target_database` | No | Source `db` | Destination database connection handle (defaults to source database for intra-database table copies). |
| `batch_size` | No | `10000` | Number of rows committed per network chunk (recommended: `10000`–`50000`). |
| `tablock` | No | `true` | *(SQL Server)* Acquires table-level lock for minimal transaction logging and maximum insertion speed. |
| `check_constraints` | No | `false` | *(SQL Server)* Validates foreign keys and check constraints during bulk load. Set `false` for fastest ingestion. |
| `fire_triggers` | No | `false` | *(SQL Server)* Executes insert triggers on the target table during bulk load. Set `false` for high-throughput ETL. |
| `keep_nulls` | No | `false` | *(SQL Server)* Preserves incoming `NULL` values instead of replacing them with target column default definitions. |
| `timeout` | No | — | Bulk copy timeout duration (e.g. `15m`, `2h`). |
| `output_var` | No | — | Captures total number of rows streamed to destination into a pipeline variable. |
| `condition` / `cond` | No | — | Condition expression required to execute this step. |
| `on_error` | No | `stop` | Failure handling strategy: `stop`, `continue`, or `retry`. |
| `retry_count` | No | `0` | Number of retries on transient connection errors. |
| `description` | No | — | Human-readable explanation of this bulk replication step. |

---

## 🎯 High-Performance Tuning Matrix

| Option / Parameter | High-Throughput ETL Setting | Safe Audit / Strict Setting | Impact & Mechanism |
| :--- | :--- | :--- | :--- |
| `batch_size` | `25000` to `50000` | `5000` | Larger batch sizes amortize network roundtrips. Keep $\le 50000$ to avoid excessive memory buffers on the target server. |
| `tablock` | `true` | `false` | **SQL Server**: Enables table-level exclusive lock, qualifying the operation for minimal transaction log writes ($10\times$ faster). |
| `check_constraints` | `false` | `true` | When `false`, SQL Server bypasses checking foreign keys per row during bulk load. Validate constraints in bulk afterward. |
| `fire_triggers` | `false` | `true` | Disabling triggers prevents row-by-row trigger firing overhead during multi-million-row ingestion. |
| `keep_nulls` | `true` | `false` | When `true`, explicit `NULL` values from source columns are retained instead of being overwritten by column default constraints. |

---

## 💡 Practical Examples

### Example 1: Cross-Database Aggregation Streaming (PostgreSQL → SQLite)

Streams aggregated daily orders directly from a production PostgreSQL instance into a local reporting SQLite database:

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="oltp_pg" driver="postgres" connection_string="postgresql://app:secret@pg-primary:5432/orders" />
        <database name="reporting_sqlite" driver="sqlite" connection_string="./reporting_daily.db" />
    </databases>

    <flow>
        <!-- Initialize destination schema -->
        <sql id="init_dst_schema" db="reporting_sqlite">
            CREATE TABLE IF NOT EXISTS daily_sales_summary (
                customer_id INTEGER NOT NULL,
                total_amount REAL NOT NULL,
                order_count INTEGER NOT NULL,
                summary_date TEXT NOT NULL
            );
        </sql>

        <!-- Stream aggregated rows directly into SQLite -->
        <sql_bulk id="sync_daily_sales"
                  db="oltp_pg"
                  target_db="reporting_sqlite"
                  target_table="daily_sales_summary"
                  batch_size="5000"
                  output_var="ROWS_COPIED">
            SELECT 
                customer_id,
                SUM(amount) AS total_amount,
                COUNT(*) AS order_count,
                CAST(order_date AS TEXT) AS summary_date
            FROM orders
            WHERE order_date >= CURRENT_DATE - INTERVAL '1 day'
            GROUP BY customer_id, order_date;
        </sql_bulk>

        <!-- Validate with assertion -->
        <assert id="verify_replication"
                var="ROWS_COPIED"
                condition="ROWS_COPIED > 0"
                message="Expected at least one daily summary record to be copied." />
    </flow>
</pipeline>
```

---

### Example 2: Massive 20M+ Row Ingestion into SQL Server with TABLOCK

Transfers millions of transaction records from a transactional PostgreSQL log into a Microsoft SQL Server enterprise staging table:

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="source_pg" 
                  driver="postgres" 
                  connection_string="postgresql://etl:Secret2026@pg-cluster.corp:5432/tx_db?sslmode=require"
                  max_open_conns="30"
                  workload="bulk" />

        <database name="dest_mssql" 
                  driver="sqlserver" 
                  connection_string="sqlserver://sa:Secret2026@mssql-dw.corp:1433?database=StagingLake"
                  max_open_conns="30"
                  workload="bulk" />
    </databases>

    <flow>
        <sql_bulk id="replicate_tx_ledger"
                  db="source_pg"
                  target_db="dest_mssql"
                  target_table="stg_transaction_ledger"
                  batch_size="25000"
                  tablock="true"
                  check_constraints="false"
                  fire_triggers="false"
                  keep_nulls="true"
                  timeout="2h"
                  output_var="TOTAL_REPLICATED">
            SELECT 
                tx_id,
                account_id,
                merchant_id,
                amount,
                currency,
                created_at,
                metadata_json
            FROM transactions.ledger
            WHERE created_at >= '2026-01-01 00:00:00Z'
            ORDER BY tx_id ASC;
        </sql_bulk>
    </flow>
</pipeline>
```

---

### Example 3: Intra-Database Table-to-Table Streaming (Archival / Staging)

When `target_db` is omitted, `<sql_bulk>` automatically reuses the source `db` connection:

```xml
<sql_bulk id="archive_closed_tickets"
          db="crm_database"
          target_table="tickets_archive_2025"
          batch_size="10000"
          output_var="ARCHIVED_COUNT">
    SELECT * 
    FROM tickets 
    WHERE status = 'CLOSED' 
      AND closed_date < '2026-01-01';
</sql_bulk>
```

---

## 🛠️ Workload Comparison: `<sql_bulk>` vs. `<sql>` vs. `<foreach>`

| Workload Requirement | Recommended Node | Reason |
| :--- | :---: | :--- |
| **Move rows between different database engines** | `<sql_bulk>` | Native streaming pipe; zero intermediate files; $O(1)$ RAM usage. |
| **Transform data within the same database engine** | `<sql>` | Keeps data in the database engine (`INSERT INTO ... SELECT`, `MERGE`); fastest performance. |
| **Call an external REST API or webhook per row** | `<foreach>` | Iterates row-by-row with streaming cursor; allows dynamic child tasks. |
| **Intra-database table replication** | `<sql_bulk>` | Chunks inserts across batches without transaction log exhaustion. |
