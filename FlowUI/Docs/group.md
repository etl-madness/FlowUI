# `<group>` Reference & Execution Guide

The `<group>` element organizes child pipeline nodes into a sequential, cohesive execution block. In addition to logical step organization, `<group>` supports four core capabilities:

1. **Conditional Execution** (`var` / `if_var` / `condition` + `equals` / `if_equals`)
2. **Atomic Database Transactions** (`transaction="true"` / `tx="true"`) with automatic dialect-aware nested savepoint support
3. **Session Connection Pinning** (`session="true"` / `pin_connection="true"` / `pin_conn="true"` / `sticky="true"`) for sharing temporary tables (`#Temp`, `TEMP TABLE`) and session state across steps without holding long-lived transaction locks
4. **Scoped Execution Timeouts** (`timeout="30s"`, `timeout="5m"`, etc.)

---

## 1. Architectural Overview

```mermaid
flowchart TD
    Start(["Enter &lt;group&gt;"]) --> CondCheck{"Has Condition?<br/>(var / if_var / condition)"}
    
    CondCheck -- "Condition Fails" --> Skipped(["Skip Group &amp; Children"])
    CondCheck -- "Passes or None" --> TimeoutCheck{"timeout Set with<br/>session or transaction?"}
    
    TimeoutCheck -- "Yes" --> DeriveCtx["Derive Child Context<br/>context.WithTimeout(ctx, timeout)"]
    TimeoutCheck -- "No" --> KeepCtx["Use Parent Context"]
    
    DeriveCtx --> SessionCheck{"session='true'?"}
    KeepCtx --> SessionCheck
    
    SessionCheck -- "Yes (Root Session)" --> PinConn["Pin Dedicated *sql.Conn<br/>db.Conn(ctx) &amp; Push to activeConns"]
    SessionCheck -- "Yes (Nested on Same DB)" --> ReuseConn["Reuse Ancestor *sql.Conn<br/>Push to activeConns"]
    SessionCheck -- "No" --> TxCheck{"transaction='true'?"}
    
    PinConn --> TxCheck
    ReuseConn --> TxCheck
    
    TxCheck -- "No" --> ExecSteps["Execute Child Nodes Sequentially<br/>(Routes SQL: activeTxs → activeConns → *sql.DB)"]
    TxCheck -- "Yes" --> StackCheck{"Active Outer Tx<br/>on Same DB?"}
    
    StackCheck -- "No (Root Tx)" --> BeginTx["BeginTx(ctx, nil)<br/>(On pinned *sql.Conn if active, else *sql.DB)<br/>Push to activeTxs"]
    StackCheck -- "Yes (Nested Tx)" --> CreateSP["Create Dialect Savepoint<br/>• MSSQL: SAVE TRANSACTION sp_N<br/>• ANSI: SAVEPOINT sp_N"]
    
    BeginTx --> ExecSteps
    CreateSP --> ExecSteps
    
    ExecSteps --> StepResult{"All Child Nodes<br/>Succeeded?"}
    
    StepResult -- "Yes" --> TxSuccess{"transaction='true'?"}
    TxSuccess -- "No" --> CleanupSession
    TxSuccess -- "Yes (Root Tx)" --> CommitTx["Commit Transaction<br/>Pop activeTxs"] --> CleanupSession
    TxSuccess -- "Yes (Nested Tx)" --> ReleaseSP["Release Savepoint<br/>Pop activeTxs"] --> CleanupSession
    
    StepResult -- "No (Error / Timeout)" --> TxFail{"transaction='true'?"}
    TxFail -- "Yes (Root Tx)" --> RollbackTx["Rollback Transaction<br/>Pop activeTxs"] --> CleanupSessionFail
    TxFail -- "Yes (Nested Tx)" --> RollbackSP["Rollback to Savepoint<br/>Pop activeTxs"] --> CleanupSessionFail
    TxFail -- "No" --> CleanupSessionFail
    
    CleanupSession["If session='true': Pop activeConns<br/>(&amp; Close *sql.Conn if Root Owner)"] --> Done(["Group Succeeded"])
    CleanupSessionFail["If session='true': Pop activeConns<br/>(&amp; Close *sql.Conn if Root Owner)"] --> Fail(["Propagate Error"])
```

---

## 2. Supported Attributes

```xml
<group id="staged_order_settlement"
       var="ENABLE_SETTLEMENT"
       equals="true"
       session="true"
       transaction="false"
       db="orders_db"
       timeout="5m"
       description="Stages and settles orders using a pinned database session">
    <!-- Sequential child nodes: <sql>, <sql_bulk>, <foreach>, <group>, <script>, <assert>, etc. -->
</group>
```

| Attribute | Aliases | Type | Mandatory | Default | Description |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `id` | `name` | `string` | No | — | Unique identifier for the group node in execution results, observability events, and diagnostics. |
| `var` | `if_var` | `string` | No | — | Name of the pipeline variable evaluated before executing the group. If the condition does not match, the group and all child nodes are skipped. |
| `condition` | `cond` | `string` | No | — | Inline condition expression or variable name evaluated when `var` / `if_var` is not set. Supports `{{Var}} == 'val'` and `{{Var}} != 'val'` expressions. |
| `equals` | `val`, `value`, `if_val`, `if_equals` | `string` | No | `"true"` (when `var` is set without `==`/`!=`) | Expected value required for the group to execute when `var` or `if_var` is specified. Supports `{{Variable}}` interpolation. |
| `transaction` | `tx` | `boolean` | No | `false` | Wraps all child database operations on `db` in an atomic database transaction (`*sql.Tx`). Commits when all children succeed; rolls back automatically on error or context cancellation. |
| `session` | `pin_connection`, `pin_conn`, `sticky` | `boolean` | No | `false` | Pins a single physical database connection (`*sql.Conn`) from the pool for `db` across all child steps so session-scoped temporary tables and session settings persist. |
| `db` | `database` | `string` | Conditional | — | Name of the registered `<database>` connection. **Required** when `transaction="true"` or `session="true"`. |
| `timeout` | — | `string` | No | — | Scoped execution deadline for the group (e.g., `"30s"`, `"5m"`, `"1h"`). Derives a child `context.WithTimeout` for the transaction or session group and all nested steps. |
| `description` | — | `string` | No | — | Human-readable description of the group block. |

---

## 3. Execution Modes

### Mode A: Logical & Conditional Grouping

Use `<group>` to organize related steps and optionally gate their execution on a pipeline variable or inline condition expression without needing a separate `<if><then>...</then></if>` wrapper.

```xml
<group id="run_if_enabled" var="ENABLE_CLEANUP" equals="true" description="Optional cleanup steps">
    <sql id="purge_old_logs" db="app_db">
        DELETE FROM audit_logs WHERE created_at &lt; CURRENT_DATE - INTERVAL '90 days';
    </sql>
</group>
```

You can also use inline comparison expressions (`==` or `!=`) in `condition` (or `var`):

```xml
<group id="non_prod_seed" condition="{{ENV}} != 'production'">
    <sql id="seed_test_users" db="app_db">
        INSERT INTO users (username, role) VALUES ('test_admin', 'admin');
    </sql>
</group>
```

---

### Mode B: Atomic Database Transactions (`transaction="true"`)

When `transaction="true"` (or `tx="true"`) is enabled with `db="..."`, Flow begins a `*sql.Tx` before executing child nodes:
- **Atomic Commit**: If all child nodes succeed, Flow commits the transaction.
- **Automatic Rollback**: If any child node fails, panics, or the context times out/cancels, Flow automatically rolls back the transaction.
- **Nested Transactions (Dialect-Aware Savepoints)**: If a `<group transaction="true" db="X">` is nested inside an outer `<group transaction="true" db="X">`, Flow automatically reuses the outer transaction and issues dialect-aware savepoint commands:

| Database Driver | Savepoint Command | Rollback Command | Release Command |
| :--- | :--- | :--- | :--- |
| **PostgreSQL** (`postgres`, `pgx`) | `SAVEPOINT sp_N` | `ROLLBACK TO SAVEPOINT sp_N` | `RELEASE SAVEPOINT sp_N` |
| **SQLite** (`sqlite`, `sqlite3`) | `SAVEPOINT sp_N` | `ROLLBACK TO SAVEPOINT sp_N` | `RELEASE SAVEPOINT sp_N` |
| **MySQL / MariaDB** (`mysql`) | `SAVEPOINT sp_N` | `ROLLBACK TO SAVEPOINT sp_N` | `RELEASE SAVEPOINT sp_N` |
| **Oracle** (`oracle`, `godror`) | `SAVEPOINT sp_N` | `ROLLBACK TO SAVEPOINT sp_N` | — *(Automatic on commit)* |
| **Microsoft SQL Server** (`sqlserver`, `mssql`) | `SAVE TRANSACTION sp_N` | `ROLLBACK TRANSACTION sp_N` | — *(T-SQL omits release syntax)* |

```xml
<group id="order_settlement_tx" transaction="true" db="orders_db" timeout="30s">
    <sql id="debit_account" db="orders_db">
        UPDATE accounts SET balance = balance - 100 WHERE account_id = {{ACCOUNT_ID}};
    </sql>
    <sql id="create_receipt" db="orders_db">
        INSERT INTO receipts (account_id, amount) VALUES ({{ACCOUNT_ID}}, 100);
    </sql>
</group>
```

---

### Mode C: Session Connection Pinning (`session="true"`) for Temporary Tables

Database engines scope temporary tables (`#Temp` in SQL Server, `CREATE TEMP TABLE` in PostgreSQL/SQLite, `CREATE TEMPORARY TABLE` in MySQL) to the **physical session/connection**. Because `*sql.DB` is a connection pool, separate `<sql>` and `<sql_bulk>` steps normally execute on different pooled connections, causing session-scoped temp tables to disappear between steps.

Setting `session="true"` (or `pin_connection="true"`, `pin_conn="true"`, `sticky="true"`) pins a single `*sql.Conn` for the specified `db` across the entire group:
- Every child `<sql>`, `<sql_bulk>`, `<foreach>`, `<excel_write>`, and `<kv_bulk>` targeting `db` executes on that same pinned connection.
- Each statement outside a transaction block **auto-commits immediately**, avoiding long-running transaction locks or transaction log bloat during staging and bulk loads.
- If another `<group session="true" db="...">` is nested on the same database, Flow reuses the ancestor's pinned connection and only closes/returns the connection to the pool when the outermost owning session group exits.

```xml
<group id="temp_table_etl" session="true" db="dw_db" timeout="15m">
    <!-- Step 1: Create session-scoped temp table -->
    <sql id="create_temp_stage" db="dw_db">
        CREATE TABLE #StagingSales (
            SaleID INT,
            Region VARCHAR(50),
            Amount DECIMAL(18,2),
            IsValid BIT DEFAULT 1
        );
    </sql>

    <!-- Step 2: Bulk stream from source DB into #StagingSales on the pinned connection -->
    <sql_bulk id="bulk_load_stage" db="crm_db" target_db="dw_db" target_table="#StagingSales" batch_size="10000">
        SELECT SaleID, Region, Amount, 1 AS IsValid FROM dbo.RawSales
    </sql_bulk>

    <!-- Step 3: Scrub data in #StagingSales (auto-commits immediately) -->
    <sql id="scrub_stage" db="dw_db">
        UPDATE #StagingSales SET IsValid = 0 WHERE Amount &lt;= 0;
    </sql>

    <!-- Step 4: Final insert from #StagingSales -->
    <sql id="publish_clean_sales" db="dw_db">
        INSERT INTO dbo.FactSales (SaleID, Region, Amount)
        SELECT SaleID, Region, Amount FROM #StagingSales WHERE IsValid = 1;

        DROP TABLE #StagingSales;
    </sql>
</group>
```

---

### Mode D: Combining `session="true"` with Nested `transaction="true"`

You can nest `<group transaction="true" db="...">` inside `<group session="true" db="...">` (or set both `session="true" transaction="true"` on the same group). When a transaction group starts inside a pinned session for the same database, Flow starts the transaction **directly on the pinned `*sql.Conn`** (`conn.BeginTx(ctx, nil)`), preserving full visibility into any `#Temp` tables created earlier in the session:

```mermaid
sequenceDiagram
    participant Pool as *sql.DB Pool
    participant Conn as Pinned *sql.Conn
    participant Tx as *sql.Tx (on Pinned Conn)

    Note over Pool,Conn: Enter <group session="true" db="dw_db">
    Pool->>Conn: db.Conn(ctx) — Pin Connection
    Conn->>Conn: <sql> CREATE TEMP TABLE temp_updates (Auto-Commit)
    Conn->>Conn: <sql_bulk> Stream rows into temp_updates (Auto-Commit Batches)

    Note over Conn,Tx: Enter Nested <group transaction="true" db="dw_db">
    Conn->>Tx: conn.BeginTx(ctx, nil)
    Tx->>Tx: <sql> UPDATE customers FROM temp_updates
    Tx->>Tx: <sql> INSERT INTO batch_audit SELECT COUNT(*) FROM temp_updates
    Tx->>Conn: tx.Commit()

    Note over Pool,Conn: Exit <group session="true" db="dw_db">
    Conn->>Pool: conn.Close() — Return Connection to Pool
```

```xml
<group id="staged_publish_session" session="true" db="dw_db">
    <!-- Outside transaction: fast staging into temp table -->
    <sql id="create_temp" db="dw_db">
        CREATE TEMP TABLE temp_updates (id INT, new_status TEXT);
    </sql>
    <sql_bulk id="load_temp" db="src_db" target_db="dw_db" target_table="temp_updates">
        SELECT id, status FROM incoming_events
    </sql_bulk>

    <!-- Inside transaction on the SAME pinned connection: atomic swap/merge -->
    <group id="atomic_apply" transaction="true" db="dw_db">
        <sql id="update_target" db="dw_db">
            UPDATE customers
            SET status = u.new_status
            FROM temp_updates u
            WHERE customers.id = u.id;
        </sql>
        <sql id="audit_batch" db="dw_db">
            INSERT INTO batch_audit (processed_count)
            SELECT COUNT(*) FROM temp_updates;
        </sql>
    </group>
</group>
```

---

## 4. Interaction with Child Nodes, Loops & Concurrency

### Child Node Routing Priority
Inside a `session="true"` or `transaction="true"` group, child `<sql>`, `<sql_bulk>` (both as `db` source and `target_db` destination), `<foreach>`, `<excel_write>`, and `<kv_bulk>` nodes targeting the group's `db` automatically resolve their execution handle in the following priority order:
1. **Active Transaction (`activeTxs`)**: Executes on the active `*sql.Tx` if inside a transaction group for `db`.
2. **Pinned Session Connection (`activeConns`)**: Executes on the pinned `*sql.Conn` if inside a session group for `db`.
3. **Database Connection Pool (`*sql.DB`)**: Falls back to standard pooled execution when neither a transaction nor a pinned session is active for `db`.

### Single-Connection Cursor Buffering (`<sql_bulk>` & `<foreach>`)
Because a single physical database connection (`*sql.Conn` or `*sql.Tx`) can only have one active `*sql.Rows` result cursor open at a time in Go's `database/sql`:
- **`<sql_bulk>` on Shared Connection**: When `<sql_bulk>` reads and writes on the same database connection (`db == target_db` inside a `session="true"` or `transaction="true"` group), Flow automatically buffers the source rows in memory and closes the source `*sql.Rows` cursor before executing destination batch writes on that connection.
- **`<foreach>` on Pinned Session or Transaction**: When `<foreach>` executes its driver query on a database with an active transaction or pinned session connection, Flow automatically buffers the driver rows and closes the cursor before running child loop steps on that same session.

### Loops (`<foreach>`, `<while>`)
When a `<group transaction="true">` or `<group session="true">` is placed *inside* a `<foreach>` or `<while>` loop, Flow starts and finishes an independent transaction or pinned session **per loop iteration**:
- Failure in iteration `N` rolls back only iteration `N`.
- Preceding iterations remain permanently committed.

### Parallel Execution (`<parallel>`)
Each worker branch inside a `<parallel>` block receives an isolated cloned executor state:
- Avoid sharing a single `*sql.Conn` or `*sql.Tx` across concurrent goroutines.
- Place `<group session="true">` or `<group transaction="true">` **inside** each `<parallel>` branch so every concurrent worker pins its own dedicated connection or transaction from the pool.

---

## 5. Production Pipeline Examples

### Example 1: Atomic Financial Transfer with `<assert>` Quality Gate
Demonstrates debiting one account, verifying affected rows with `<assert>`, crediting the destination account, and recording an immutable ledger entry inside a 30-second atomic transaction.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="SenderID" type="int" value="101" />
        <variable name="ReceiverID" type="int" value="202" />
        <variable name="TransferAmount" type="float" value="150.00" />
    </variables>

    <databases>
        <database name="core_banking" driver="postgres"
                  connection_string="postgresql://app_user:Secret123@db.internal:5432/banking?sslmode=require" />
    </databases>

    <flow>
        <group id="transfer_funds_txn" transaction="true" db="core_banking" timeout="30s"
               description="Transfers funds atomically between accounts">
            
            <!-- Step 1: Deduct balance and capture affected row count -->
            <sql id="debit_sender" db="core_banking" output_var="DEBIT_ROWS">
                UPDATE accounts 
                SET balance = balance - {{TransferAmount}}, updated_at = NOW()
                WHERE account_id = {{SenderID}} AND balance &gt;= {{TransferAmount}};
            </sql>

            <!-- Step 2: Halt and trigger automatic rollback if 0 rows were updated -->
            <assert id="check_debit_applied" var="DEBIT_ROWS" equals="1"
                    message="Insufficient funds or invalid sender account ID" on_failure="halt" />

            <!-- Step 3: Credit destination account -->
            <sql id="credit_receiver" db="core_banking">
                UPDATE accounts 
                SET balance = balance + {{TransferAmount}}, updated_at = NOW()
                WHERE account_id = {{ReceiverID}};
            </sql>

            <!-- Step 4: Record immutable audit ledger -->
            <sql id="record_ledger_entry" db="core_banking">
                INSERT INTO transaction_ledger (from_acc, to_acc, amount, created_at)
                VALUES ({{SenderID}}, {{ReceiverID}}, {{TransferAmount}}, NOW());
            </sql>
        </group>
    </flow>
</pipeline>
```

---

### Example 2: Session-Pinned Temp Table Staging & Atomic Merge
Demonstrates using `session="true"` on SQL Server to create and bulk-populate a `#Temp` table outside a transaction (for minimal logging and fast batch ingestion), followed by a nested `transaction="true"` group on the same pinned connection to atomically apply changes.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="erp_db" driver="postgres"
                  connection_string="postgresql://reader:secret@erp.internal:5432/erp" />
        <database name="dw_db" driver="sqlserver"
                  connection_string="sqlserver://dw.internal:1433?database=Warehouse&amp;integrated+security=true" />
    </databases>

    <flow>
        <group id="session_staging_workflow" session="true" db="dw_db" timeout="15m"
               description="Pins a SQL Server session for #StagingInventory and merges atomically">
            
            <!-- 1. Create #StagingInventory on the pinned connection (auto-commits) -->
            <sql id="create_staging_temp" db="dw_db">
                CREATE TABLE #StagingInventory (
                    SKU VARCHAR(64) NOT NULL,
                    WarehouseCode VARCHAR(32) NOT NULL,
                    OnHandQty INT NOT NULL
                );
            </sql>

            <!-- 2. Stream rows from PostgreSQL into SQL Server #StagingInventory -->
            <sql_bulk id="stream_erp_to_temp"
                      db="erp_db"
                      target_db="dw_db"
                      target_table="#StagingInventory"
                      batch_size="10000">
                SELECT sku, warehouse_code, on_hand_qty FROM inventory_snapshot;
            </sql_bulk>

            <!-- 3. Atomic transaction on the same pinned *sql.Conn -->
            <group id="atomic_warehouse_swap" transaction="true" db="dw_db" timeout="2m">
                <sql id="delete_Negative_qty" db="dw_db">
                    DELETE FROM #StagingInventory WHERE OnHandQty &lt; 0;
                </sql>

                <sql id="upsert_fact_inventory" db="dw_db">
                    DELETE FROM dbo.FactInventory;
                    INSERT INTO dbo.FactInventory (SKU, WarehouseCode, OnHandQty)
                    SELECT SKU, WarehouseCode, OnHandQty FROM #StagingInventory;
                </sql>
            </group>

            <!-- 4. Clean up temp table before releasing connection back to pool -->
            <sql id="drop_staging_temp" db="dw_db">
                DROP TABLE #StagingInventory;
            </sql>
        </group>
    </flow>
</pipeline>
```

---

### Example 3: Concurrent Isolated Sessions & Transactions in `<parallel>`
Demonstrates running multi-regional partition archiving concurrently across 3 worker threads, each maintaining an isolated transaction on its respective database.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="eu_db" driver="postgres" connection_string="postgresql://app:pass@eu-db:5432/main" />
        <database name="us_db" driver="postgres" connection_string="postgresql://app:pass@us-db:5432/main" />
        <database name="ap_db" driver="postgres" connection_string="postgresql://app:pass@ap-db:5432/main" />
    </databases>

    <flow>
        <parallel max_threads="3">
            <!-- Worker Branch 1: EU Partition Archive -->
            <group id="archive_eu_txn" transaction="true" db="eu_db" timeout="2m">
                <sql db="eu_db">
                    INSERT INTO orders_archive SELECT * FROM orders WHERE created_at &lt; '2025-01-01';
                </sql>
                <sql db="eu_db">
                    DELETE FROM orders WHERE created_at &lt; '2025-01-01';
                </sql>
            </group>

            <!-- Worker Branch 2: US Partition Archive -->
            <group id="archive_us_txn" transaction="true" db="us_db" timeout="2m">
                <sql db="us_db">
                    INSERT INTO orders_archive SELECT * FROM orders WHERE created_at &lt; '2025-01-01';
                </sql>
                <sql db="us_db">
                    DELETE FROM orders WHERE created_at &lt; '2025-01-01';
                </sql>
            </group>

            <!-- Worker Branch 3: APAC Partition Archive -->
            <group id="archive_ap_txn" transaction="true" db="ap_db" timeout="2m">
                <sql db="ap_db">
                    INSERT INTO orders_archive SELECT * FROM orders WHERE created_at &lt; '2025-01-01';
                </sql>
                <sql db="ap_db">
                    DELETE FROM orders WHERE created_at &lt; '2025-01-01';
                </sql>
            </group>
        </parallel>
    </flow>
</pipeline>
```
