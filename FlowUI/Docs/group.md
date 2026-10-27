# `<group>` Execution Group & Database Transactions Guide

The `<group>` element organizes child pipeline steps into a sequential, cohesive execution block. It serves three critical functions in Flow pipelines:
1. **Logical Step Organization**: Cleanly groups related tasks under a shared identifier, descriptive label, and lifecycle scope.
2. **Atomic Database Transactions & Dialect-Aware Savepoints**: Coordinates multiple `<sql>` queries into an all-or-nothing transaction with automatic commit and rollback, plus dialect-aware savepoints for nested groups.
3. **Conditional Execution & Error Resilience**: Evaluates conditions before running child steps and governs fault tolerance via granular retry policies (`on_error`, `retry_count`, `retry_interval`).

---

## 1. Architectural Overview

```mermaid
flowchart TD
    Start(["Enter &lt;group&gt;"]) --> CondCheck{"Has Condition?<br/>(condition / if_var)"}
    
    CondCheck -- "Condition Fails" --> Skipped(["Skip Group &amp; Children"])
    CondCheck -- "Passes or None" --> TxCheck{"transaction='true'?"}
    
    TxCheck -- "No" --> ExecSteps["Execute Child Steps Sequentially"]
    TxCheck -- "Yes" --> StackCheck{"Active Outer Tx<br/>on Same DB?"}
    
    StackCheck -- "No (Root Tx)" --> BeginTx["BeginTx(ctx)<br/>Push to Transaction Stack"]
    StackCheck -- "Yes (Nested)" --> CreateSP["Create Dialect Savepoint<br/>• MSSQL: SAVE TRANSACTION sp_N<br/>• ANSI: SAVEPOINT sp_N"]
    
    BeginTx --> ExecSteps
    CreateSP --> ExecSteps
    
    ExecSteps --> StepResult{"All Steps<br/>Succeeded?"}
    
    StepResult -- "Yes" --> TxSuccess{"transaction='true'?"}
    TxSuccess -- "No" --> Done(["Group Completed"])
    TxSuccess -- "Yes (Root)" --> CommitTx["Commit Transaction<br/>Pop Stack"] --> Done
    TxSuccess -- "Yes (Nested)" --> ReleaseSP["Release / Finalize Savepoint<br/>Pop Stack"] --> Done
    
    StepResult -- "No (Error/Timeout)" --> ErrorPolicy{"on_error Policy"}
    
    ErrorPolicy -- "retry (count > 0)" --> RollbackAttempt["Rollback Tx or SP<br/>Wait retry_interval"] --> ExecSteps
    ErrorPolicy -- "continue" --> RollbackCont["Rollback Tx or SP<br/>Log Warning &amp; Continue"] --> Done
    ErrorPolicy -- "stop (Default)" --> RollbackFail["Rollback Tx or SP<br/>Abort Pipeline"] --> Fail(["Pipeline Error"])
```

---

## 2. Complete XML Syntax & Attribute Reference

```xml
<group id="ProcessBatch"
       transaction="true"
       db="finance_db"
       timeout="45s"
       on_error="retry"
       retry_count="3"
       retry_interval="5s"
       condition="{{IsActive}} == true"
       description="Executes accounting batch with atomic commit and retry">
    <!-- Sequential child steps: <sql>, <script>, <http_client>, <template>, etc. -->
</group>
```

### Attribute Reference

| Attribute | Type | Mandatory | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `string` | **Yes** (Recommended) | generated | Unique identifier for the group step, displayed in execution telemetry, logs, and flowcharts. |
| `transaction` | `bool` | No | `false` | When `true`, wraps all child `<sql>` steps inside an atomic database transaction. |
| `tx` | `bool` | No | `false` | Alias for `transaction`. |
| `db` | `string` | Conditional | — | The configured database connection name to open the transaction against. **Mandatory** when `transaction="true"`. |
| `database` | `string` | No | — | Alias for `db`. |
| `timeout` | `string` | No | — | Transaction deadline duration (e.g. `30s`, `2m`, `1h`). Cancels the context and triggers an automatic rollback if exceeded. |
| `on_error` | `string` | No | `stop` | Failure policy when a child step returns an error: `stop` (abort group and pipeline), `continue` (log and proceed to next sibling), or `retry` (reattempt group). |
| `retry_count` | `int` | No | `0` | Number of retry attempts when `on_error="retry"`. |
| `retry_interval`| `string` | No | `1s` | Delay between retry attempts (e.g. `500ms`, `2s`, `10s`). |
| `condition` | `string` | No | — | Inline boolean expression evaluated before execution (e.g. `{{Env}} == 'prod'`, `{{TotalCount}} > 0`). |
| `cond` | `string` | No | — | Alias for `condition`. |
| `if_var` | `string` | No | — | Name of a pipeline variable to test for execution (e.g. `RunBatch`). Alias `var`. |
| `var` | `string` | No | — | Alias for `if_var`. |
| `if_equals` | `string` | No | — | Expected variable value required for the group to execute. Aliases: `equals`, `if_val`, `val`, `value`. |
| `equals` | `string` | No | — | Alias for `if_equals`. |
| `if_val` | `string` | No | — | Alias for `if_equals`. |
| `val` | `string` | No | — | Alias for `if_equals`. |
| `value` | `string` | No | — | Alias for `if_equals`. |
| `description` | `string` | No | — | Descriptive summary of the group's purpose. |

---

## 3. Database Transactions & Dialect-Aware Savepoints

Flow implements a thread-safe, stack-based transaction coordinator (`activeTxs`). It guarantees that all child database operations within the group succeed as a single atomic unit or roll back completely upon failure.

```mermaid
flowchart TD
    subgraph OuterGroup ["Outer &lt;group id='financial_transfer' transaction='true' db='bank_db'&gt;"]
        T1["BeginTx() on bank_db"] --> S1["1. UPDATE accounts SET balance = balance - 500 WHERE id = 1"]
        
        subgraph InnerGroup ["Inner &lt;group id='nested_audit' transaction='true' db='bank_db'&gt;"]
            SP["Create Savepoint (sp_1)<br/>• MSSQL: SAVE TRANSACTION sp_1<br/>• ANSI: SAVEPOINT sp_1"] --> S2["2. INSERT INTO transfer_audit_log VALUES (...)"]
            S2 --> SPCheck{"Audit Step Ok?"}
            SPCheck -- "Success" --> SPRelease["Release / Retain Savepoint"]
            SPCheck -- "Failure" --> SPRollback["ROLLBACK TO SAVEPOINT sp_1<br/>(Outer Tx remains intact)"]
        end
        
        S1 --> SP
        SPRelease --> S3["3. UPDATE accounts SET balance = balance + 500 WHERE id = 2"]
        SPRollback --> S3
        
        S3 --> FinalCheck{"All Steps Ok?"}
        FinalCheck -- "Success" --> CommitTx["Commit Outer Transaction"]
        FinalCheck -- "Failure" --> RollbackTx["Rollback Full Transaction"]
    end
```

### Dialect-Aware Savepoints
When an inner `<group transaction="true">` targets the same database connection as an active outer transaction, Flow detects the nested state and automatically establishes a savepoint:

| Database Driver | Savepoint Command | Rollback Command | Release Command |
| :--- | :--- | :--- | :--- |
| **PostgreSQL** (`postgres`, `pgx`) | `SAVEPOINT sp_N` | `ROLLBACK TO SAVEPOINT sp_N` | `RELEASE SAVEPOINT sp_N` |
| **SQLite** (`sqlite3`) | `SAVEPOINT sp_N` | `ROLLBACK TO SAVEPOINT sp_N` | `RELEASE SAVEPOINT sp_N` |
| **MySQL / MariaDB** (`mysql`) | `SAVEPOINT sp_N` | `ROLLBACK TO SAVEPOINT sp_N` | `RELEASE SAVEPOINT sp_N` |
| **Oracle** (`godror`) | `SAVEPOINT sp_N` | `ROLLBACK TO SAVEPOINT sp_N` | — *(Automatic on commit)* |
| **Microsoft SQL Server** (`sqlserver`, `mssql`) | `SAVE TRANSACTION sp_N` | `ROLLBACK TRANSACTION sp_N` | — *(T-SQL omits release syntax)* |

> [!NOTE]
> If an inner group with a savepoint fails, Flow rolls back **only to that savepoint**, allowing the outer transaction to inspect the failure, execute alternative logic, or proceed to completion without corrupting the broader transaction.

---

## 4. Context Propagation & Execution Deadlines (`timeout`)

Database locks held by uncommitted transactions can degrade database performance and cause deadlocks. Flow binds every transaction group to a Go `context.Context`:

* **Granular Timeouts**: Specify `timeout="30s"` or `timeout="5m"` on the group.
* **Automatic Rollback**: If the timeout duration expires or an external cancellation signal arrives, the context is cancelled immediately. Flow executes an atomic deferred rollback, closing the transaction and releasing all table locks.

```xml
<group id="critical_update" transaction="true" db="crm_db" timeout="15s">
    <sql id="apply_discount" db="crm_db">
        UPDATE subscriptions SET discount_pct = 0.20 WHERE renew_date &lt;= CURRENT_DATE;
    </sql>
</group>
```

---

## 5. Error Handling & Retry Policies

The `<group>` element supports localized resilience and fault recovery policies:

```mermaid
flowchart LR
    Step["Child Step Fails"] --> Policy{"on_error"}
    Policy -- "stop" --> Stop["Rollback Tx &amp; Halt Pipeline"]
    Policy -- "continue" --> Cont["Rollback Tx &amp; Log Warning<br/>Proceed to Next Node"]
    Policy -- "retry" --> CountCheck{"Retries Left?"}
    CountCheck -- "Yes" --> Delay["Sleep retry_interval"] --> Reattempt["Rollback Tx &amp; Re-run Group"]
    CountCheck -- "No" --> Stop
```

* **`on_error="stop"` (Default)**: Any child failure halts the group, initiates an immediate database rollback, and fails the pipeline.
* **`on_error="continue"`**: If a child step fails, the group's transaction is rolled back safely, but the pipeline execution continues with the subsequent sibling nodes.
* **`on_error="retry"`**: When paired with `retry_count="N"` and `retry_interval="Xs"`, transient failures (such as network drops or database deadlocks) trigger an immediate rollback followed by a clean re-execution of the group from the beginning.

---

## 6. Conditional Group Execution

Instead of requiring an explicit `<if><then>...</then></if>` construct, `<group>` supports direct conditional attributes:

### 1. Simple Variable Equality Testing (`if_var` + `if_equals`)
```xml
<group id="RunProductionMigrations" if_var="Environment" if_equals="production">
    <sql db="app_db">ALTER TABLE users ADD COLUMN IF NOT EXISTS verified BOOLEAN DEFAULT FALSE;</sql>
</group>
```

### 2. Boolean Expressions (`condition`)
```xml
<group id="ProcessHighPriority" condition="{{PendingRecords}} > 1000 &amp;&amp; {{DryRun}} == false">
    <sql db="warehouse_db">INSERT INTO processing_queue SELECT * FROM staging_records;</sql>
</group>
```

---

## 7. Interaction with Loops and Parallelism

### Loops (`<foreach>`, `<while>`)
When placed inside `<foreach>` or `<while>`, a `<group transaction="true">` creates, commits, or rolls back an independent transaction **on every individual iteration**:
* Failure in iteration `N` rolls back only iteration `N`.
* Preceding iterations remain permanently committed.
* Subsequent iterations continue according to the loop's error policy.

### Parallel Execution (`<parallel>`)
When running inside `<parallel max_threads="...">`:
* Flow provisions a cloned, thread-isolated execution context for each worker branch.
* If a worker executes `<group transaction="true">`, its transaction is isolated to that worker's dedicated database connection.
* Concurrent workers never share transaction states or collide on savepoint names.

### High-Throughput Bulk Copy Caution (`<sql_bulk>`)
> [!WARNING]
> Do **NOT** wrap high-throughput streaming bulk copy (`<sql_bulk target_table="...">` / `StreamETL`) inside a transaction-enabled `<group transaction="true">`.
> 
> SQL Server TDS bulk copy operates using its own internal batch-level commits (`batch_size="10000"`). Wrapping native TDS fast-load streams inside an outer group transaction causes driver command overlap, connection pool exhaustion, and extensive table lock contention.

---

## 8. Comprehensive Production Examples

### Example 1: Multi-Step Bank Transfer with Atomic Transaction & Timeout
Demonstrates debiting one account, crediting another, and writing an audit trail with an enforced 30-second deadline.

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
        <!-- Atomic transaction block with 30s timeout -->
        <group id="transfer_funds_txn" transaction="true" db="core_banking" timeout="30s"
               description="Transfers funds atomically between accounts">
            
            <!-- Step 1: Deduct balance with balance constraint -->
            <sql id="debit_sender" db="core_banking">
                UPDATE accounts 
                SET balance = balance - {{TransferAmount}}, updated_at = NOW()
                WHERE account_id = {{SenderID}} AND balance >= {{TransferAmount}};
            </sql>

            <!-- Step 2: Ensure exactly 1 row was updated -->
            <assert id="check_debit_applied" var="debit_sender.rows_affected" equals="1"
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

### Example 2: Nested Savepoints (Outer Transaction with Inner Audit Group)
Demonstrates dialect-aware savepoints. If the nested audit log fails, the inner savepoint rolls back without interrupting the outer order processing.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="store_db" driver="sqlserver"
                  connection_string="sqlserver://sql.prod.net:1433?database=Store&amp;integrated+security=true" />
    </databases>

    <flow>
        <!-- Root transaction boundary -->
        <group id="process_order_txn" transaction="true" db="store_db" timeout="1m">
            
            <!-- 1. Place order -->
            <sql id="insert_order" db="store_db">
                INSERT INTO orders (customer_id, total_amount) VALUES (4096, 299.95);
            </sql>

            <!-- 2. Nested transaction: uses T-SQL 'SAVE TRANSACTION savepoint_1' -->
            <group id="nested_notification_log" transaction="true" db="store_db" on_error="continue">
                <sql id="log_notification" db="store_db">
                    INSERT INTO notification_queue (order_id, channel) VALUES (4096, 'EMAIL');
                </sql>
            </group>

            <!-- 3. Finalize order status -->
            <sql id="finalize_order" db="store_db">
                UPDATE customer_profiles SET last_order_date = GETDATE() WHERE id = 4096;
            </sql>
        </group>
    </flow>
</pipeline>
```

---

### Example 3: Conditional Execution with Error Retries
Demonstrates verifying an external API health check before running a group that retries up to 3 times on transient database deadlocks.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="ApiHealthy" type="bool" value="false" />
    </variables>

    <databases>
        <database name="stage_db" driver="postgres"
                  connection_string="postgresql://app:pass@localhost:5432/staging" />
    </databases>

    <flow>
        <!-- Probe upstream API -->
        <http_client id="check_upstream" method="GET" url="https://api.partner.com/health" status_into="HttpStatus" />
        
        <script id="eval_status" language="go">
            <![CDATA[
            package main
            import "host/vars"
            func main() {
                if vars.GetInt("HttpStatus") == 200 {
                    vars.SetBool("ApiHealthy", true)
                }
            }
            ]]>
        </script>

        <!-- Conditional group with retry policy -->
        <group id="pull_partner_data"
               if_var="ApiHealthy" if_equals="true"
               on_error="retry" retry_count="3" retry_interval="4s"
               transaction="true" db="stage_db">
            
            <http_client id="fetch_orders" method="GET" url="https://api.partner.com/v1/orders" into="OrdersJson" />
            
            <sql id="stage_raw_orders" db="stage_db">
                INSERT INTO staging_raw_orders (payload, received_at)
                VALUES ('{{OrdersJson}}', NOW());
            </sql>
        </group>
    </flow>
</pipeline>
```

---

### Example 4: Transactional Loop inside `<foreach>`
Demonstrates streaming active user subscriptions and applying billing renewals, committing each user in its own independent transaction.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="billing_db" driver="mysql"
                  connection_string="root:secret@tcp(127.0.0.1:3306)/billing?parseTime=true" />
    </databases>

    <flow>
        <!-- Loop driver: streams user IDs needing renewal -->
        <foreach id="renew_subscribers" db="billing_db">
            <![CDATA[
            SELECT user_id, plan_cost, plan_tier 
            FROM subscriptions 
            WHERE status = 'active' AND next_billing_date <= CURDATE();
            ]]>
            
            <!-- Each row executes in its own isolated transaction -->
            <group id="apply_user_renewal" transaction="true" db="billing_db" timeout="15s" on_error="continue">
                <sql id="deduct_user_credits" db="billing_db">
                    UPDATE user_wallets 
                    SET balance = balance - {{plan_cost}} 
                    WHERE user_id = {{user_id}} AND balance >= {{plan_cost}};
                </sql>

                <assert id="check_wallet_deducted" var="deduct_user_credits.rows_affected" equals="1"
                        message="User {{user_id}} has insufficient credit balance" />

                <sql id="advance_billing_cycle" db="billing_db">
                    UPDATE subscriptions 
                    SET next_billing_date = DATE_ADD(CURDATE(), INTERVAL 1 MONTH)
                    WHERE user_id = {{user_id}};
                </sql>

                <sql id="log_billing_history" db="billing_db">
                    INSERT INTO billing_events (user_id, amount, tier, billed_at)
                    VALUES ({{user_id}}, {{plan_cost}}, '{{plan_tier}}', NOW());
                </sql>
            </group>
        </foreach>
    </flow>
</pipeline>
```

---

### Example 5: Concurrent Isolated Transactions in `<parallel>`
Demonstrates running multi-regional partition archiving concurrently across 3 worker threads, each maintaining an isolated transaction.

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
