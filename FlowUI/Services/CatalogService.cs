using FlowUI.Models;

namespace FlowUI.Services;

/// <summary>
/// 1:1 port of go-flow/builder/catalog.go.
/// Provides all 9 categories and 26 Flow pipeline component definitions.
/// </summary>
public sealed class CatalogService
{
    public IReadOnlyList<string> Categories { get; } =
    [
        "Pipeline Setup", "Control Flow", "Database & SQL",
        "Key-Value & Cache", "Template & Transform", "File & Storage",
        "Data Extraction", "Quality & Validation", "Integration & Web"
    ];

    public IReadOnlyList<ComponentMeta> Components { get; }

    public CatalogService()
    {
        Components = BuildCatalog();
    }

    private static readonly HashSet<string> ConfigAllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "variable", "database", "script", "group", "if", "sql", "kv",
        "template", "file_read", "json_path", "yaml_path", "xml_xpath",
        "assert", "http_client"
    };

    public bool IsAllowedInConfig(ComponentMeta component) =>
        component is not null && ConfigAllowedTypes.Contains(component.Type);

    public bool IsAllowedInConfig(string nodeType) =>
        !string.IsNullOrWhiteSpace(nodeType) && ConfigAllowedTypes.Contains(nodeType.Trim());

    public IReadOnlyList<ComponentMeta> ConfigComponents =>
        Components.Where(IsAllowedInConfig).ToList();

    public ComponentMeta? GetByType(string nodeType) =>
        Components.FirstOrDefault(c => string.Equals(c.Type, nodeType, StringComparison.OrdinalIgnoreCase));

    public ComponentMeta? GetComponent(string nodeType) => GetByType(nodeType);

    public IEnumerable<ComponentMeta> GetByCategory(string category, string? searchFilter = null, bool configOnly = false)
    {
        var query = Components.Where(c => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));
        if (configOnly)
        {
            query = query.Where(IsAllowedInConfig);
        }
        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            var term = searchFilter.Trim();
            query = query.Where(c =>
                c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Tag.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return query;
    }

    public IReadOnlyList<(string Category, List<ComponentMeta> Items)> GetGroupedByCategory(string? searchFilter = null, bool configOnly = false)
    {
        var result = new List<(string Category, List<ComponentMeta> Items)>();
        foreach (var cat in Categories)
        {
            var items = GetByCategory(cat, searchFilter, configOnly).ToList();
            if (items.Count > 0)
            {
                result.Add((cat, items));
            }
        }
        return result;
    }

    private static AttributeMeta F(
        string name,
        string label,
        string type = "text",
        bool required = false,
        string defaultValue = "",
        string help = "",
        string[]? options = null) => new()
        {
            Name = name,
            Label = label,
            Type = type,
            Required = required,
            Default = defaultValue,
            Help = help,
            Options = options?.ToList()
        };

    private static ComponentMeta C(
        string type,
        string name,
        string category,
        string section,
        string description,
        bool hasContent,
        string contentLabel,
        List<AttributeMeta> attributes) => new()
        {
            Type = type,
            Tag = type,
            Name = name,
            Category = category,
            Section = section,
            Description = description,
            HasContent = hasContent,
            ContentLabel = contentLabel,
            Attributes = attributes
        };

    private static ComponentMeta BuildTemplateComponent(
        string type, string name, string description, string contentLabel, string intoHelp, string fileLabel, string fileHelp) =>
        C(type, name, "Template & Transform", "flow", description, true, contentLabel,
        [
            F("id", "Step ID", "text", true, "", "Unique step ID"),
            F("into", "Into Variable", "text", false, "", intoHelp),
            F("file", fileLabel, "text", false, "", fileHelp),
            F("name", "Template Name"), F("output_var", "Output Variable (Alias)"),
            F("var", "Var (Alias)"), F("timeout", "Timeout Duration"), F("description", "Description")
        ]);

    private static ComponentMeta BuildPathExtractionComponent(
        string type, string name, string description, string sourceLabel, string queryLabel, string queryHelp, string defaultMode, string[] modeOptions) =>
        C(type, name, "Data Extraction", "flow", description, false, "",
        [
            F("id", "Step ID", "text", true), F("source", sourceLabel, "text", true),
            F("query", queryLabel, "text", true, "", queryHelp), F("into", "Target Variable", "text", true),
            F("mode", "Output Mode", "select", false, defaultMode, "", modeOptions),
            F("file", "Source File Path"), F("timeout", "Timeout Duration"), F("description", "Description")
        ]);

    private static List<ComponentMeta> BuildCatalog() =>
    [
        // 1. Pipeline Setup
        C("variable", "Variable", "Pipeline Setup", "variables",
            "Initializes a named pipeline variable with explicit type conversion and variable interpolation support.", false, "",
            [
                F("id", "Identifier", "text", false, "", "Optional unique identifier for the variable definition"),
                F("name", "Variable Name", "text", true, "", "Unique variable name accessed via {{Name}}"),
                F("value", "Value", "text", true, "", "Initial variable value"),
                F("type", "Data Type", "select", false, "string", "", ["string", "int", "bool", "float", "datetime", "date"]),
                F("description", "Description", "text", false, "", "Human-readable description of the variable")
            ]),
        C("database", "Database Connection", "Pipeline Setup", "databases",
            "Defines a named database connection pool (MSSQL, PostgreSQL, MySQL, SQLite, Oracle, KV).", false, "",
            [
                F("name", "Connection Name", "text", true, "", "Unique connection name referenced by db attribute"),
                F("driver", "Driver", "select", true, "sqlserver", "", ["sqlserver", "azuresql", "sqlite", "postgres", "mysql", "oracle", "bbolt", "badger", "redis", "etcd"]),
                F("connection_string", "Connection String / DSN", "text", true, "", "Driver-specific connection string or path. Supports {{Variables}}"),
                F("workload", "Workload Profile", "select", false, "", "Workload tuning profile for connection pool defaults", ["", "oltp", "bulk", "analytics", "batch"]),
                F("max_open_conns", "Max Open Conns", "int", false, "25"),
                F("max_idle_conns", "Max Idle Conns", "int", false, "10"),
                F("conn_max_lifetime_seconds", "Max Lifetime (sec or duration)", "text", false, "300", "Maximum duration a connection is recycled (e.g. 300 or 5m)")
            ]),
        C("preflight", "Preflight Container", "Pipeline Setup", "preflight",
            "A container for pre-execution quality gates, environment readiness checks, and assertions.", false, "",
            [F("id", "Step ID", "text", false, "", "Identifier for preflight group")]),

        // 2. Control Flow
        C("script", "Script Execution", "Control Flow", "flow",
            "Executes dynamic code with cross-platform runtimes (Go, PowerShell Core, Windows PowerShell, Bash, Cmd, .NET Script).",
            true, "Inline script code executed by the chosen interpreter runtime.",
            [
                F("id", "Step ID", "text", true, "", "Unique step ID for logging and telemetry"),
                F("language", "Script Language / Runtime", "select", true, "powershell", "Target interpreter runtime", ["powershell", "pwsh", "shell", "bash", "cmd", "go", "git-bash", "zsh", "dotnet-script", "csx", "lua"]),
                F("output_var", "Output Variable", "text", false, "", "Variable name receiving script standard output (stdout)"),
                F("var", "Script Body Variable", "text", false, "", "Optional pipeline variable containing script code to execute"),
                F("timeout", "Timeout Duration", "text", false, "", "Execution timeout duration (e.g. 10s, 45s, 5m, 1h)"),
                F("description", "Description", "text", false, "", "Human-readable explanation of script action"),
                F("on_error", "On Error", "select", false, "stop", "Failure policy when the script exits with non-zero code", ["stop", "continue", "retry"]),
                F("retry_count", "Retry Count", "int", false, "0", "Number of retries when on_error is set to retry"),
                F("retry_interval", "Retry Interval", "text", false, "", "Delay between retries (e.g. 2s, 10s)"),
                F("db", "Database Alias", "text", false, "", "Optional database connection alias")
            ]),
        C("group", "Execution Group", "Control Flow", "flow",
            "Groups pipeline steps together with independent retry, transaction boundaries, and error policies.", false, "",
            [
                F("id", "Group ID", "text", true, "", "Unique group step identifier"),
                F("transaction", "Transactional", "bool", false, "false", "Wraps child steps in an atomic database transaction", ["true", "false"]),
                F("db", "Database Connection", "text", false, "", "Database connection name for transaction group"),
                F("timeout", "Timeout Duration", "text", false, "", "Transaction execution deadline duration (e.g. 30s, 5m)"),
                F("description", "Description", "text", false, "", "Description of the group block"),
                F("condition", "Condition Expression", "text", false, "", "Inline boolean expression evaluated before execution"),
                F("if_var", "Condition Variable", "text", false, "", "Pipeline variable name evaluated before executing child steps"),
                F("if_equals", "Expected Value", "text", false, "", "Expected variable value required for execution"),
                F("on_error", "On Error", "select", false, "stop", "", ["stop", "continue", "retry"]),
                F("retry_count", "Retry Count", "int", false, "0"),
                F("retry_interval", "Retry Interval", "text", false, "", "Delay between retries (e.g. 2s, 10s)")
            ]),
        C("if", "Conditional Branch", "Control Flow", "flow",
            "Evaluates an expression or variable equality to execute steps in <then> and <else> branches.", false, "",
            [
                F("id", "Condition ID", "text", true, "", "Unique step ID"),
                F("var", "Variable Name", "text", false, "", "Variable name to evaluate (alias: if_var)"),
                F("equals", "Equals Value", "text", false, "", "Expected target value for truthy branch"),
                F("condition", "Condition Expression", "text", false, "", "Dynamic boolean or template expression"),
                F("description", "Description", "text", false, "", "Description of the conditional logic")
            ]),
        C("foreach", "Foreach Loop", "Control Flow", "flow",
            "Iterates over rows returned by a SQL query with constant-memory streaming or optional buffering.",
            true, "SQL driver query that returns rows to iterate over (e.g. SELECT id, name FROM items).",
            [
                F("id", "Loop ID", "text", true, "", "Unique step ID"),
                F("db", "Database Connection", "text", true, "", "Database connection name executing driver query"),
                F("stream", "Streaming Mode", "bool", false, "true", "Constant-memory streaming cursor mode", ["true", "false"]),
                F("buffer", "Buffer In-Memory", "bool", false, "false", "Opt-in in-memory buffering to read rows upfront", ["true", "false"]),
                F("mode", "Execution Mode", "select", false, "stream", "Loop execution mode", ["stream", "buffer"]),
                F("var", "Query Variable", "text", false, "", "Optional variable containing driver query"),
                F("language", "Driver Language", "select", false, "sql", "Driver query language runtime", ["sql"]),
                F("condition", "Condition Expression", "text", false, "", "Optional pre-condition expression"),
                F("on_error", "On Error Action", "select", false, "stop", "Error policy", ["stop", "continue", "retry"]),
                F("retry_count", "Retry Count"), F("timeout", "Timeout Duration", "text", false, "", "Loop timeout duration (e.g. 30s, 5m)"),
                F("description", "Description", "text", false, "", "Description of the loop")
            ]),
        C("while", "While Loop", "Control Flow", "flow",
            "Executes inner steps repeatedly while a condition expression remains true.", false, "",
            [
                F("id", "Loop ID", "text", true, "", "Unique step ID"),
                F("condition", "Condition Expression", "text", false, "", "Loop while this condition is true"),
                F("var", "Variable Name", "text", false, "", "Variable name to monitor"),
                F("equals", "Equals Value", "text", false, "", "Loop while variable equals this value"),
                F("max_iterations", "Max Iterations", "int", false, "1000", "Infinite loop guard ceiling"),
                F("timeout", "Timeout Duration", "text", false, "", "Optional timeout duration for entire while loop"),
                F("on_error", "On Error Action", "select", false, "stop", "", ["stop", "continue", "retry"]),
                F("description", "Description", "text", false, "", "Description of the while loop")
            ]),
        C("parallel", "Parallel Execution", "Control Flow", "flow",
            "Executes child branches or tasks concurrently across multiple goroutines with synchronization.", false, "",
            [
                F("id", "Parallel Step ID", "text", true, "", "Unique step ID"),
                F("max_threads", "Max Threads", "int", false, "4", "Max concurrent worker threads"),
                F("timeout", "Timeout Duration", "text", false, "", "Optional timeout duration for parallel tasks"),
                F("on_error", "On Error Action", "select", false, "stop", "", ["stop", "continue", "retry"]),
                F("description", "Description", "text", false, "", "Description of parallel block")
            ]),

        // 3. Database & SQL
        C("sql", "SQL Query / Command", "Database & SQL", "flow",
            "Executes a SQL statement or query with variable binding and optional result mapping.",
            true, "Raw SQL query or DDL command. Variable interpolation e.g. {{.TableName}} supported.",
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("db", "Database Connection", "text", true, "", "Connection name declared in <databases>"),
                F("output_var", "Output Variable", "text", false, "", "Variable name to capture query result set or affected row count"),
                F("timeout", "Timeout Duration", "text", false, "", "Statement execution timeout duration (e.g. 30s, 45m)"),
                F("into", "Into Variable (Alias)", "text", false, "", "Legacy alias for output_var"),
                F("on_error", "On Error", "select", false, "stop", "", ["stop", "continue", "retry"]),
                F("retry_count", "Retry Count", "int", false, "0"), F("retry_interval", "Retry Interval", "text", false, "1s"),
                F("description", "Description", "text", false, "", "Human-readable description of this SQL step")
            ]),
        C("sql_bulk", "SQL Bulk Insert / Copy", "Database & SQL", "flow",
            "High-performance bulk stream copy of rows from a source SQL query directly into a destination table.",
            true, "Source extraction SQL query that pipes rows directly into the destination table.",
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("db", "Source Database", "text", true, "", "Source database connection handle declared in <databases>"),
                F("target_table", "Target Table", "text", true, "", "Destination table name receiving bulk inserts"),
                F("target_db", "Target Database", "text", false, "", "Destination database connection handle (defaults to source db)"),
                F("batch_size", "Batch Size", "int", false, "10000", "Number of rows committed per network chunk"),
                F("tablock", "Table Lock (MSSQL)", "bool", false, "true", "(SQL Server) Acquires table-level lock", ["true", "false"]),
                F("check_constraints", "Check Constraints", "bool", false, "false", "(SQL Server) Validates foreign keys/check constraints", ["true", "false"]),
                F("fire_triggers", "Fire Triggers", "bool", false, "false", "(SQL Server) Fires insert triggers on target table", ["true", "false"]),
                F("keep_nulls", "Keep Nulls", "bool", false, "false", "(SQL Server) Preserves incoming NULLs", ["true", "false"]),
                F("timeout", "Timeout Duration", "text", false, "", "Bulk copy operation timeout duration"),
                F("output_var", "Output Variable", "text", false, "", "Stores total number of rows streamed to destination"),
                F("on_error", "On Error", "select", false, "stop", "", ["stop", "continue", "retry"]),
                F("retry_count", "Retry Count", "int", false, "0"),
                F("description", "Description", "text", false, "", "Human-readable description of bulk stream copy")
            ]),

        // 4. Key-Value & Cache
        C("kv", "Key-Value Store", "Key-Value & Cache", "flow",
            "Executes atomic key-value operations (get, put/set, delete/del, scan/list) against embedded or remote KV stores.",
            true, "Optional inline DSL commands (e.g. SET mykey 'myval' or GET mykey).",
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("db", "KV Connection", "text", true, "", "Database connection name configured with a KV driver"),
                F("op", "Operation", "select", false, "get", "Atomic operation", ["get", "put", "delete", "scan"]),
                F("bucket", "Bucket / Keyspace", "text", false, "default", "Bucket name or key prefix namespace"),
                F("key", "Key", "text", false, "", "Target record key (supports {{VarName}})"),
                F("value", "Value", "text", false, "", "Value payload for write operations"),
                F("into", "Output Variable", "text", false, "", "Variable name receiving retrieved value"),
                F("var", "DSL Variable", "text", false, "", "Optional variable containing dynamic DSL command string"),
                F("timeout", "Timeout Duration"), F("description", "Description"),
                F("on_error", "On Error", "select", false, "stop", "", ["stop", "continue", "retry"])
            ]),
        C("kv_bulk", "KV Bulk Operations", "Key-Value & Cache", "flow",
            "Executes batch key-value insertions, range scans, or prefix queries efficiently.", false, "",
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("db", "KV Connection", "text", true, "", "KV database connection handle"),
                F("bucket", "Bucket", "text", false, "default"),
                F("source", "Source Dataset", "text", true, "", "Dataset containing key/value map")
            ]),

        // 5. Template & Transform
        BuildTemplateComponent("template", "Template Generator",
            "Renders text, SQL queries, or configurations using Go text/template with custom pipeline helpers.",
            "Template content. Access variables via {{.VarName}} and built-in functions.",
            "Variable name to receive rendered text", "Output / Source File", "File path to write rendered output or load external template"),
        BuildTemplateComponent("template_html", "HTML Template",
            "Safely renders contextual HTML templates with automatic escaping for reports and emails.",
            "HTML template body with Go html/template syntax.",
            "Variable name to receive rendered HTML", "Output HTML File", "File path to write rendered HTML directly"),
        BuildTemplateComponent("html_template", "HTML Template (Alias)",
            "Safely renders contextual HTML templates with automatic escaping for reports and emails (alias for template_html).",
            "HTML template body with Go html/template syntax.", "", "Output HTML File", ""),

        // 6. File & Storage
        C("file_read", "Read File", "File & Storage", "flow",
            "Reads file contents from disk into a pipeline variable.", false, "",
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("path", "File Path", "text", true, "", "Path to input file on disk (supports {{Variables}})"),
                F("into", "Target Variable", "text", true, "", "Variable name receiving file content"),
                F("timeout", "Timeout Duration"), F("description", "Description")
            ]),
        C("file_save", "Save File", "File & Storage", "flow",
            "Writes variable text, bytes, or template outputs to a file on disk.",
            true, "Inline text or template to write if source variable is not specified.",
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("path", "File Path", "text", true, "", "Destination file path on disk"),
                F("source", "Source Variable", "text", false, "", "Variable containing data to write"),
                F("mode", "Write Mode", "select", false, "overwrite", "", ["overwrite", "append"]),
                F("timeout", "Timeout Duration"), F("description", "Description")
            ]),
        C("excel_read", "Excel Read (.xlsx)", "File & Storage", "flow",
            "Parses Excel worksheets into structured pipeline rows and variables.", false, "",
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("file", "Excel File Path", "text", true, "", "Path to .xlsx file on disk"),
                F("sheet", "Sheet Name", "text", false, "Sheet1"),
                F("header", "Has Header Row", "select", false, "true", "", ["true", "false"]),
                F("output_var", "Output Variable", "text", true, "", "Variable name receiving extracted rows as JSON array"),
                F("timeout", "Timeout Duration"), F("description", "Description")
            ]),
        C("excel_write", "Excel Write (.xlsx)", "File & Storage", "flow",
            "Exports SQL query results into styled multi-tab Excel spreadsheets.",
            true, "SQL query to extract rows for the worksheet (e.g. SELECT * FROM customers).",
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("file", "Target Excel File", "text", true, "", "Path to write destination .xlsx file"),
                F("sheet", "Sheet Name", "text", false, "Sheet1"),
                F("db", "Database Connection", "text", true, "", "Database connection name executing source export query"),
                F("var", "Query Variable"), F("timeout", "Timeout Duration"), F("description", "Description")
            ]),

        // 7. Data Extraction
        BuildPathExtractionComponent("json_path", "JSON Path Extraction",
            "Queries JSON strings or documents using JSONPath expressions.",
            "Source JSON Variable", "JSONPath Query", "e.g. $.items[*].id", "value", ["value", "json", "json_array"]),
        BuildPathExtractionComponent("yaml_path", "YAML Path Extraction",
            "Extracts scalar or complex structures from YAML strings and manifests.",
            "Source YAML Variable", "Path Expression", "e.g. spec.replicas", "value", ["value", "json", "json_array", "yaml"]),
        BuildPathExtractionComponent("xml_xpath", "XML XPath Extraction",
            "Evaluates XPath expressions against XML documents.",
            "Source XML Variable", "XPath Query", "e.g. //book/title/text()", "text", ["text", "xml", "json_array"]),
        BuildPathExtractionComponent("xml_path", "XML Path Extraction",
            "Evaluates XPath expressions against XML documents (alias for xml_xpath).",
            "Source XML Variable", "XPath Query", "", "text", ["text", "xml", "json_array"]),

        // 8. Quality & Validation
        C("assert", "Assertion Check", "Quality & Validation", "preflight",
            "Enforces data quality rules and halts or warns if an expression fails.", false, "",
            [
                F("id", "Step ID", "text", true), F("var", "Variable to Evaluate"),
                F("equals", "Expected Value"), F("condition", "Condition Expression", "text", false, "", "e.g. gt .RecordCount 0"),
                F("message", "Failure Message"), F("on_failure", "On Failure Action", "select", false, "halt", "", ["halt", "warn", "continue"]),
                F("fail_var", "Failure Flag Variable"), F("fail_val", "Failure Flag Value", "text", false, "true"),
                F("timeout", "Timeout Duration"), F("description", "Description")
            ]),

        // 9. Integration & Web
        C("http_client", "HTTP / REST Client", "Integration & Web", "flow",
            "Executes HTTP GET/POST/PUT/DELETE requests with headers and payload handling.",
            true, "Request body content (for POST/PUT). Supports JSON and variable interpolation.",
            [
                F("id", "Step ID", "text", true), F("url", "Endpoint URL", "text", true, "", "Target endpoint URL (supports {{Variables}})"),
                F("method", "HTTP Method", "select", true, "GET", "", ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"]),
                F("headers", "Request Headers", "text", false, "", "Custom headers JSON or K1:V1,K2:V2"),
                F("content_type", "Content-Type"), F("into", "Response Variable"), F("status_into", "Status Code Variable"),
                F("timeout", "Timeout Duration"), F("proxy", "HTTP Proxy URL"),
                F("tls_insecure_skip_verify", "Skip TLS Verify", "select", false, "false", "", ["false", "true"]),
                F("follow_redirects", "Follow Redirects", "select", false, "true", "", ["true", "false"]),
                F("description", "Description")
            ])
    ];
}
