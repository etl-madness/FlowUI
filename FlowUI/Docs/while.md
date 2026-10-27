# Conditional Iteration: `<while>`

The `<while>` component repeats execution of its inner child steps as long as an evaluation condition or variable comparison remains true. It is designed for polling asynchronous database jobs, paginated REST API extractions, wait-and-retry workflows, and multi-pass batching loops.

`<while>` includes a built-in safety ceiling (`max_iterations`, default 1000) to protect against runaway infinite loops and automatically injects a zero-based iteration tracker `{{WHILE_INDEX}}`.

---

## Architecture & Lifecycle

```mermaid
flowchart TD
    Start(["Enter &lt;while&gt; Loop"]) --> InitIndex["Initialize WHILE_INDEX = 0"]
    InitIndex --> CheckLimit{"WHILE_INDEX >= max_iterations?<br/>(default: 1000)"}

    CheckLimit -- "Yes (Safety Guard)" --> Tripwire[/"Error / Terminate Loop<br/>(Loop limit exceeded)"/]
    CheckLimit -- "No" --> EvalCond{"Evaluate Loop Condition<br/>condition or var == equals"}

    EvalCond -- "False / Not Equal" --> Exit([Loop Terminates Successfully])
    EvalCond -- "True / Equal" --> ExecChildren["Execute Child Steps Sequentially"]

    ExecChildren --> Incr["Increment WHILE_INDEX = WHILE_INDEX + 1"]
    Incr --> CheckLimit
```

---

## Attribute Reference

| Attribute | Type | Default | Mandatory | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `string` | *(generated)* | No | Unique identifier for the while loop step. |
| `var` | `string` | `""` | Conditional | Name of the pipeline variable monitored for equality (e.g. `JobStatus`). |
| `if_var` | `string` | `""` | No | Alias for `var`. |
| `equals` | `string` | `""` | Conditional | Loop continues while `var` equals this string or value (e.g. `RUNNING` or `true`). |
| `val` / `value` | `string` | `""` | No | Aliases for `equals`. |
| `if_val` / `if_equals` | `string` | `""` | No | Aliases for `equals`. |
| `condition` | `string` | `""` | Conditional | Expression evaluated before each iteration (e.g. `ne .Status "DONE"` or `lt .PAGE_COUNT 10`). |
| `cond` | `string` | `""` | No | Alias for `condition`. |
| `max_iterations` | `positiveInteger` | `1000` | No | Infinite loop guard ceiling. Pipeline halts if iterations reach this threshold. |
| `max_loops` | `positiveInteger` | `1000` | No | Alias for `max_iterations`. |
| `timeout` | `string` | *(none)* | No | Optional timeout duration for the total while block (e.g. `5m`, `30s`). |
| `on_error` | `string` | `stop` | No | Error handling policy for inner child failures: `stop`, `continue`, or `retry`. |
| `retry_count` | `integer` | `0` | No | Number of retries permitted per child failure before applying `on_error`. |
| `description` | `string` | `""` | No | Human-readable explanation of the loop's purpose. |

---

## Key Capabilities & Runtime Guarantees

### 1. Loop Control Modes
`<while>` supports two distinct condition styles:

- **Variable Equality Mode (`var` + `equals`)**:
  Monitors a specific variable and repeats as long as its value matches `equals`.
  ```xml
  <while id="poll_sync" var="SYNC_STATUS" equals="IN_PROGRESS" max_iterations="30">
      <!-- Child steps query status and update SYNC_STATUS -->
  </while>
  ```

- **Expression Condition Mode (`condition`)**:
  Evaluates boolean or comparison expressions dynamically before each pass.
  ```xml
  <while id="page_iterator" condition="and (eq .HAS_MORE \"true\") (lt .CURRENT_PAGE 50)" max_iterations="50">
      <!-- Child steps fetch next page and update HAS_MORE -->
  </while>
  ```

### 2. Built-in `WHILE_INDEX` Variable
On every pass through the loop, the engine injects a zero-based iteration index named `WHILE_INDEX`:
- **Iteration 1**: `{{WHILE_INDEX}}` = `0`
- **Iteration 2**: `{{WHILE_INDEX}}` = `1`
- **Iteration 3**: `{{WHILE_INDEX}}` = `2`

Child steps, Go scripts (`vars.GetInt("WHILE_INDEX")`), SQL queries, and log statements can access `{{WHILE_INDEX}}` directly.

### 3. Infinite Loop Protection (`max_iterations`)
To prevent unbounded resource consumption or runaway API costs, `<while>` defaults to a maximum ceiling of **1,000 iterations**. If the loop condition remains true after reaching `max_iterations`, execution halts immediately with an explanatory error.

### 4. Pacing & Delay
Most polling workflows require pauses between iterations. You can introduce delays using an embedded `<script language="go">` with `time.Sleep(...)`:
```xml
<script id="poll_delay" language="go">
    package main
    import "time"
    func main() {
        time.Sleep(3 * time.Second)
    }
</script>
```

---

## Production Pipeline Examples

### Example 1: Asynchronous Database Job Polling (MSSQL)
Poll the status of a long-running SQL Server Agent or stored procedure task until completion.

```xml
<pipeline description="Poll MSSQL background job status"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="JobStatus" type="string" value="RUNNING" />
        <variable name="TargetJobId" type="string" value="JOB_2026_0981" />
    </variables>

    <databases>
        <database name="primary_db" 
                  connection_string="sqlserver://SRV01:1433?database=AppDB&amp;integrated+security=true" />
    </databases>

    <flow>
        <!-- Loop while JobStatus is RUNNING, max 30 attempts (approx 60s total) -->
        <while id="poll_mssql_job" var="JobStatus" equals="RUNNING" max_iterations="30"
               description="Wait for background batch procedure to complete">

            <!-- Step 1: Wait 2 seconds between polling checks -->
            <script id="sleep_step" language="go">
                package main
                import "time"
                func main() {
                    time.Sleep(2 * time.Second)
                }
            </script>

            <!-- Step 2: Query database and update JobStatus output variable -->
            <sql id="fetch_status" db="primary_db" output_var="JobStatus">
                SELECT TOP 1 [status]
                FROM dbo.async_tasks WITH (NOLOCK)
                WHERE task_id = '{{TargetJobId}}';
            </sql>

            <!-- Step 3: Log progress using WHILE_INDEX -->
            <script id="log_attempt" language="go">
                package main
                import (
                    "fmt"
                    "host/vars"
                )
                func main() {
                    idx := vars.GetInt("WHILE_INDEX")
                    status := vars.GetString("JobStatus")
                    fmt.Printf("[Attempt #%d] Current Job Status: %s\n", idx+1, status)
                }
            </script>
        </while>

        <!-- Final verification gate -->
        <assert id="verify_success" var="JobStatus" equals="SUCCESS" message="Background job failed or timed out" />
    </flow>
</pipeline>
```

---

### Example 2: Paginated REST API Extraction
Retrieve records page by page from an external REST API until `has_more` returns false or the max page limit is reached.

```xml
<pipeline description="Paginated REST API Data Extraction"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="PAGE_NUM" type="int" value="1" />
        <variable name="HAS_MORE" type="string" value="true" />
        <variable name="API_RESPONSE" type="string" value="" />
    </variables>

    <flow>
        <while id="paginate_api" var="HAS_MORE" equals="true" max_iterations="100"
               description="Fetch user pages until has_more is false">

            <!-- Request current page -->
            <http_client id="fetch_page" 
                         url="https://api.example.com/v2/events?page={{PAGE_NUM}}&amp;limit=100" 
                         method="GET" 
                         into="API_RESPONSE" />

            <!-- Extract has_more boolean flag -->
            <json_path id="check_has_more" source="var" var="API_RESPONSE" query="$.pagination.has_more" into="HAS_MORE" />

            <!-- Append page payload to raw log file -->
            <file_save id="append_page" file="./data/events_feed.jsonl" source="var" var="API_RESPONSE" append="true" />

            <!-- Increment PAGE_NUM for the next pass -->
            <script id="incr_page" language="go">
                package main
                import "host/vars"
                func main() {
                    current := vars.GetInt("PAGE_NUM")
                    vars.Set("PAGE_NUM", current+1)
                }
            </script>
        </while>
    </flow>
</pipeline>
```

---

### Example 3: External Service Readiness & Health Check
Wait for a remote container or database service to become healthy with exponential backoff.

```xml
<pipeline description="Wait for remote service readiness"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="IS_READY" type="string" value="false" />
        <variable name="HTTP_STATUS" type="int" value="0" />
    </variables>

    <flow>
        <while id="wait_for_health" var="IS_READY" equals="false" max_iterations="20" timeout="5m">
            <!-- Ping health endpoint -->
            <http_client id="check_health" url="http://microservice.internal/healthz" method="GET" status_into="HTTP_STATUS" on_error="continue" />

            <!-- If HTTP 200, mark IS_READY to true to exit loop -->
            <if id="eval_health" condition="eq .HTTP_STATUS 200">
                <then>
                    <variable id="set_ready" name="IS_READY" value="true" />
                </then>
                <else>
                    <!-- Backoff pause before re-checking -->
                    <script id="backoff_sleep" language="go">
                        package main
                        import "time"
                        func main() {
                            time.Sleep(3 * time.Second)
                        }
                    </script>
                </else>
            </if>
        </while>
    </flow>
</pipeline>
```

---

### Example 4: Chunked Queue Consumer & Staging Purge
Process a work queue in batches until no pending records remain in the queue table.

```xml
<pipeline description="Drain work queue table in batches"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="PENDING_COUNT" type="int" value="1" />
    </variables>

    <databases>
        <database name="queue_db" connection_string="postgres://app:secret@localhost:5432/appdb?sslmode=disable" />
    </databases>

    <flow>
        <!-- Loop while there are pending tasks (PENDING_COUNT > 0) -->
        <while id="drain_queue" condition="gt .PENDING_COUNT 0" max_iterations="500">
            <!-- Process 1000 items in a transaction -->
            <group id="batch_worker" transaction="true" db="queue_db">
                <sql id="archive_batch" db="queue_db">
                    WITH batch AS (
                        SELECT id FROM processing_queue 
                        WHERE status = 'PENDING' 
                        LIMIT 1000 FOR UPDATE SKIP LOCKED
                    )
                    UPDATE processing_queue q
                    SET status = 'PROCESSED', processed_at = NOW()
                    FROM batch
                    WHERE q.id = batch.id;
                </sql>

                <!-- Check remaining count -->
                <sql id="count_remaining" db="queue_db" output_var="PENDING_COUNT">
                    SELECT COUNT(*) AS PENDING_COUNT FROM processing_queue WHERE status = 'PENDING';
                </sql>
            </group>
        </while>
    </flow>
</pipeline>
```

---

### Example 5: Multi-Stage Pipeline State Polling with `<assert>`
Poll a cloud export or machine learning job until status reaches either `COMPLETED` or `FAILED`, using assertions to catch fatal errors.

```xml
<pipeline description="Cloud export job status monitor"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="JOB_ID" type="string" value="EXP_8829" />
        <variable name="STATUS" type="string" value="PENDING" />
        <variable name="RESPONSE" type="string" value="" />
    </variables>

    <flow>
        <!-- Keep polling while STATUS is PENDING or PROCESSING -->
        <while id="monitor_cloud_job" condition="or (eq .STATUS \"PENDING\") (eq .STATUS \"PROCESSING\")" max_iterations="40">
            <script id="wait_5s" language="go">
                package main
                import "time"
                func main() {
                    time.Sleep(5 * time.Second)
                }
            </script>

            <http_client id="get_status" url="https://cloud.example.com/api/jobs/{{JOB_ID}}" method="GET" into="RESPONSE" />
            <json_path id="parse_status" source="var" var="RESPONSE" query="$.job.status" into="STATUS" />
        </while>

        <!-- Guarantee final status was COMPLETED -->
        <assert id="assert_job_finished_ok" var="STATUS" equals="COMPLETED" 
                message="Export job did not complete successfully" on_failure="halt" />
    </flow>
</pipeline>
```

---

## Best Practices & Troubleshooting

> [!TIP]
> **Always Include a Delay**: In polling loops, always add a `<script>` sleep step between iterations to prevent aggressive CPU spinning or API rate-limit penalties (`HTTP 429 Too Many Requests`).

> [!IMPORTANT]
> **Update Monitored Variables**: Ensure that at least one child step within the `<while>` block alters the monitored variable (`output_var="..."`, `<variable>`, or `vars.Set(...)`). If the variable is never updated, the loop will run until `max_iterations` is hit.

> [!WARNING]
> **Set Realistic `max_iterations`**: Tailor `max_iterations` to your operational expectations (e.g. `max_iterations="30"` with a 2-second sleep gives a 60-second window). Relying on the 1000 default for a 5-second polling loop could keep a blocked pipeline open for over an hour.
