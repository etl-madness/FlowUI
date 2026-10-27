# SQL Query & Command Node (`<sql>`) Guide 🚀

The `<sql>` node executes native SQL scripts, queries, and multi-statement database commands directly on any registered database connection. It is engineered for relational database workloads ranging from simple single-statement lookups to massive multi-million-row in-database transformations and data warehouse consolidations.

---

## 📋 Attribute Specifications

| Attribute | Required | Default | Description |
| :--- | :---: | :---: | :--- |
| `id` | **Yes** | — | Unique step identifier across the pipeline. |
| `db` / `database` | **Yes** | — | Name of the database configured in the `<databases>` section. |
| `output_var` / `into` / `var` | No | — | Pipeline variable name to capture returned query result sets (as JSON arrays) or affected row counts. |
| `timeout` | No | — | Execution deadline duration (e.g. `30s`, `5m`, `45m`). |
| `condition` / `cond` | No | — | Condition expression required to execute this step. |
| `on_error` | No | `stop` | Failure handling strategy: `stop`, `continue`, or `retry`. |
| `retry_count` | No | `0` | Number of retries on transient connection or query errors. |
| `retry_interval` | No | `1s` | Duration between retries (e.g. `2s`, `500ms`). |
| `description` | No | — | Human-readable explanation of this SQL operation. |

---

## 🧠 Intelligent Query vs. DML Classification (`isDMLQuery`)

Flow features a built-in token-aware SQL analyzer that automatically distinguishes between data retrieval (`SELECT`, `SHOW`, `WITH`) and data mutation (`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `CREATE`, `DROP`):

1. **Comments & CDATA Stripping**: Leading/trailing single-line comments (`-- ...`), multi-line comments (`/* ... */`), and XML `<![CDATA[ ... ]]>` tags are cleanly stripped before token evaluation.
2. **Column Name Collision Safety**: Queries selecting columns named `deleted_at`, `is_deleted`, `insert_ts`, or `update_count` are correctly classified as queries and are **not** misclassified as DML mutations.
3. **Row-Returning Mutations**: Statements utilizing clauses like `RETURNING` (PostgreSQL, SQLite, Oracle) or `OUTPUT` (SQL Server) are executed as queries so returned rows are preserved in `output_var`.
4. **Summary Selects & Rowcounts**: Complex multi-statement batches that conclude with a scalar summary (such as `SELECT @@ROWCOUNT;` or `SELECT count(*);`) return that scalar value directly into `output_var`.

---

## 🔒 Atomic Transactions with `<group transaction="true">`

To coordinate multiple `<sql>` steps inside an atomic database transaction with automatic commit and rollback:

```xml
<group id="financial_batch" transaction="true" db="main_db" timeout="2m">
    <sql id="debit_account" db="main_db">
        UPDATE accounts SET balance = balance - 250 WHERE id = 101;
    </sql>
    <sql id="credit_account" db="main_db">
        UPDATE accounts SET balance = balance + 250 WHERE id = 202;
    </sql>
</group>
```

- **Dialect-Aware Savepoints**: Nested groups on the same database automatically use savepoints (`SAVE TRANSACTION` on MSSQL, ANSI `SAVEPOINT` on PostgreSQL/SQLite/MySQL).
- **Deferred Rollback**: If any child step errors, the entire transaction (or nested savepoint) is rolled back before propagation.

---

## 💡 Practical Examples

### Example 1: Schema Initialization & Table Setup (DDL)

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="reporting_db" driver="sqlite" connection_string="./reporting.db" />
    </databases>

    <flow>
        <sql id="init_schema" db="reporting_db" description="Initialize reporting tables">
            CREATE TABLE IF NOT EXISTS customer_daily_summary (
                customer_id INTEGER PRIMARY KEY,
                total_spend REAL NOT NULL,
                last_order_date TEXT NOT NULL,
                status TEXT DEFAULT 'ACTIVE'
            );
            CREATE INDEX IF NOT EXISTS idx_cust_status ON customer_daily_summary(status);
        </sql>
    </flow>
</pipeline>
```

---

### Example 2: Data Extraction into Pipeline Variable

Extracting rows into a pipeline variable formats the result as a structured JSON array suitable for downstream loops, assertions, or templates:

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="oltp_pg" driver="postgres" connection_string="postgresql://app:secret@localhost:5432/appdb" />
    </databases>

    <flow>
        <sql id="get_top_spenders" 
             db="oltp_pg" 
             output_var="VIP_CUSTOMERS"
             timeout="30s">
            SELECT 
                customer_id, 
                full_name, 
                lifetime_value, 
                country_code
            FROM customers
            WHERE lifetime_value >= 10000.00
            ORDER BY lifetime_value DESC
            LIMIT 50;
        </sql>

        <!-- Assert that high spenders were retrieved -->
        <assert id="check_vip_count" 
                var="VIP_CUSTOMERS" 
                condition="count(VIP_CUSTOMERS) > 0" 
                message="Expected at least one VIP customer." />
    </flow>
</pipeline>
```

---

### Example 3: Multi-Stage Enterprise ETL Batch Returning `SELECT @@ROWCOUNT`

This production pattern runs in-database transformations across millions of rows in SQL Server without transferring heavy intermediate datasets across the network:

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="dw_sqlserver" 
                  driver="sqlserver" 
                  connection_string="sqlserver://etl:P@ssword1@dw.internal:1433?database=EnterpriseDW"
                  workload="bulk" />
    </databases>

    <flow>
        <sql id="nightly_consolidation" 
             db="dw_sqlserver" 
             timeout="45m"
             output_var="ROWS_PROCESSED">
            <![CDATA[
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            BEGIN TRANSACTION;

            -- 1. Deduplicate raw incoming events into staging temp table
            IF OBJECT_ID('tempdb..#StagedEvents') IS NOT NULL DROP TABLE #StagedEvents;

            CREATE TABLE #StagedEvents (
                EventKey BIGINT NOT NULL PRIMARY KEY,
                AccountID INT NOT NULL,
                Amount DECIMAL(18,4) NOT NULL,
                EventTimestamp DATETIME2(3) NOT NULL
            );

            INSERT INTO #StagedEvents WITH (TABLOCK)
            SELECT EventKey, AccountID, Amount, EventTimestamp
            FROM (
                SELECT EventKey, AccountID, Amount, EventTimestamp,
                       ROW_NUMBER() OVER (PARTITION BY EventKey ORDER BY IngestTimestamp DESC) AS rn
                FROM Ingest.RawEvents
                WHERE ProcessingStatus = 'PENDING'
            ) t WHERE t.rn = 1;

            -- 2. Upsert into production Fact table
            MERGE INTO Fact.AccountEvents WITH (TABLOCK) AS tgt
            USING #StagedEvents AS src
            ON (tgt.EventKey = src.EventKey)
            WHEN MATCHED THEN
                UPDATE SET tgt.Amount = src.Amount, tgt.LastModifiedUTC = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (EventKey, AccountID, Amount, EventTimestamp, CreatedUTC)
                VALUES (src.EventKey, src.AccountID, src.Amount, src.EventTimestamp, GETUTCDATE());

            -- 3. Mark processed in raw ledger
            UPDATE raw
            SET raw.ProcessingStatus = 'PROCESSED'
            FROM Ingest.RawEvents raw
            INNER JOIN #StagedEvents stg ON raw.EventKey = stg.EventKey;

            DROP TABLE #StagedEvents;

            COMMIT TRANSACTION;

            -- 4. Return strictly scalar rowcount to Flow output variable
            SELECT @@ROWCOUNT AS affected_rows;
            ]]>
        </sql>
    </flow>
</pipeline>
```

---

### Example 4: PostgreSQL `RETURNING` Clause Capturing Inserted Rows

```xml
<sql id="insert_and_capture_ids" db="oltp_pg" output_var="INSERTED_RECORDS">
    INSERT INTO order_audit (order_id, action, performed_by)
    VALUES 
        (1001, 'APPROVED', 'system'),
        (1002, 'APPROVED', 'system')
    RETURNING id, order_id, created_at;
</sql>
```

---

## 🛠️ Best Practices

1. **Keep Heavy Data in the Engine**: When transforming existing database records, use `<sql>` with in-database SQL (`MERGE`, `INSERT INTO ... SELECT`, `UPDATE ... JOIN`) rather than pulling millions of records into Go memory.
2. **End Multi-Statement Batches with a Scalar Query**: When executing DDL/DML batches where you need telemetry metrics, conclude with `SELECT @@ROWCOUNT;` or `SELECT count(*);`.
3. **Use CDATA for Complex Queries**: Enclose SQL containing comparison operators (`<`, `>`, `<=`, `>=`) or `&` characters within `<![CDATA[ ... ]]>` to prevent XML parsing syntax errors.
4. **Tune Timeouts**: Always set explicit `timeout="5m"` or `timeout="30m"` for long-running batches to prevent indefinite pipeline hangs on locked resources.
