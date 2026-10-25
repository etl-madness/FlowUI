using FlowUIv2.Models;

namespace FlowUIv2.Services;

/// <summary>
/// 1:1 port of go-flow/builder/catalog.go.
/// Provides all 9 categories and 26 Flow pipeline component definitions.
/// </summary>
public sealed class CatalogService
{
    public IReadOnlyList<string> Categories { get; } =
    [
        "Pipeline Setup",
        "Control Flow",
        "Database & SQL",
        "Key-Value & Cache",
        "Template & Transform",
        "File & Storage",
        "Data Extraction",
        "Quality & Validation",
        "Integration & Web"
    ];

    public IReadOnlyList<ComponentMeta> Components { get; }

    public CatalogService()
    {
        Components = BuildCatalog();
    }

    private static readonly HashSet<string> ConfigAllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // 1. Pipeline Setup
        "variable",
        "database",
        // 2. Control Flow (preflight conditionals, groups & scripts)
        "script",
        "group",
        "if",
        // 3. Database & SQL (preflight queries/checks)
        "sql",
        // 4. Key-Value & Cache
        "kv",
        // 5. Template & Transform
        "template",
        // 6. File & Storage
        "file_read",
        // 7. Data Extraction
        "json_path",
        "yaml_path",
        "xml_xpath",
        // 8. Quality & Validation
        "assert",
        // 9. Integration & Web
        "http_client"
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

    private static List<ComponentMeta> BuildCatalog() =>
    [
        // 1. Pipeline Setup
        new ComponentMeta
        {
            Type = "variable", Tag = "variable", Name = "Variable", Category = "Pipeline Setup", Section = "variables",
            Description = "Initializes a named pipeline variable with explicit type conversion and variable interpolation support.",
            HasContent = false,
            Attributes =
            [
                F("id", "Identifier", "text", false, "", "Optional unique identifier for the variable definition"),
                F("name", "Variable Name", "text", true, "", "Unique variable name accessed via {{Name}}"),
                F("value", "Value", "text", true, "", "Initial variable value"),
                F("type", "Data Type", "select", false, "string", "", ["string", "int", "bool", "float", "datetime", "date"]),
                F("description", "Description", "text", false, "", "Human-readable description of the variable")
            ]
        },
        new ComponentMeta
        {
            Type = "database", Tag = "database", Name = "Database Connection", Category = "Pipeline Setup", Section = "databases",
            Description = "Defines a named database connection pool (MSSQL, PostgreSQL, MySQL, SQLite, Oracle, KV).",
            HasContent = false,
            Attributes =
            [
                F("name", "Connection Name", "text", true, "", "Unique connection name referenced by db attribute"),
                F("driver", "Driver", "select", true, "sqlserver", "", ["sqlserver", "azuresql", "sqlite", "postgres", "mysql", "oracle", "bbolt", "badger", "redis", "etcd"]),
                F("connection_string", "Connection String / DSN", "text", true, "", "Driver-specific connection string or path. Supports {{Variables}}"),
                F("workload", "Workload Profile", "select", false, "", "Workload tuning profile for connection pool defaults", ["", "oltp", "bulk", "analytics", "batch"]),
                F("max_open_conns", "Max Open Conns", "int", false, "25"),
                F("max_idle_conns", "Max Idle Conns", "int", false, "10"),
                F("conn_max_lifetime_seconds", "Max Lifetime (sec or duration)", "text", false, "300", "Maximum duration a connection is recycled (e.g. 300 or 5m)")
            ]
        },
        new ComponentMeta
        {
            Type = "preflight", Tag = "preflight", Name = "Preflight Container", Category = "Pipeline Setup", Section = "preflight",
            Description = "A container for pre-execution quality gates, environment readiness checks, and assertions.",
            HasContent = false,
            Attributes =
            [
                F("id", "Step ID", "text", false, "", "Identifier for preflight group")
            ]
        },

        // 2. Control Flow
        new ComponentMeta
        {
            Type = "script", Tag = "script", Name = "Script Execution", Category = "Control Flow", Section = "flow",
            Description = "Executes dynamic code with cross-platform runtimes (Go, PowerShell Core, Windows PowerShell, Bash, Cmd, .NET Script).",
            HasContent = true, ContentLabel = "Inline script code executed by the chosen interpreter runtime.",
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID for logging and telemetry"),
                F("language", "Script Language / Runtime", "select", true, "powershell", "Target interpreter runtime", ["powershell", "pwsh", "shell", "bash", "cmd", "go", "git-bash", "zsh", "dotnet-script", "csx","lua"]),
                F("output_var", "Output Variable", "text", false, "", "Variable name receiving script standard output (stdout)"),
                F("var", "Script Body Variable", "text", false, "", "Optional pipeline variable containing script code to execute"),
                F("timeout", "Timeout Duration", "text", false, "", "Execution timeout duration (e.g. 10s, 45s, 5m, 1h)"),
                F("description", "Description", "text", false, "", "Human-readable explanation of script action"),
                F("on_error", "On Error", "select", false, "stop", "Failure policy when the script exits with non-zero code", ["stop", "continue", "retry"]),
                F("retry_count", "Retry Count", "int", false, "0", "Number of retries when on_error is set to retry"),
                F("retry_interval", "Retry Interval", "text", false, "", "Delay between retries (e.g. 2s, 10s)"),
                F("db", "Database Alias", "text", false, "", "Optional database connection alias")
            ]
        },
        new ComponentMeta
        {
            Type = "group", Tag = "group", Name = "Execution Group", Category = "Control Flow", Section = "flow",
            Description = "Groups pipeline steps together with independent retry, transaction boundaries, and error policies.",
            HasContent = false,
            Attributes =
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
            ]
        },
        new ComponentMeta
        {
            Type = "if", Tag = "if", Name = "Conditional Branch", Category = "Control Flow", Section = "flow",
            Description = "Evaluates an expression or variable equality to execute steps in <then> and <else> branches.",
            HasContent = false,
            Attributes =
            [
                F("id", "Condition ID", "text", true, "", "Unique step ID"),
                F("var", "Variable Name", "text", false, "", "Variable name to evaluate (alias: if_var)"),
                F("equals", "Equals Value", "text", false, "", "Expected target value for truthy branch"),
                F("condition", "Condition Expression", "text", false, "", "Dynamic boolean or template expression"),
                F("description", "Description", "text", false, "", "Description of the conditional logic")
            ]
        },
        new ComponentMeta
        {
            Type = "foreach", Tag = "foreach", Name = "Foreach Loop", Category = "Control Flow", Section = "flow",
            Description = "Iterates over rows returned by a SQL query with constant-memory streaming or optional buffering.",
            HasContent = true, ContentLabel = "SQL driver query that returns rows to iterate over (e.g. SELECT id, name FROM items).",
            Attributes =
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
                F("retry_count", "Retry Count", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, "", "Loop timeout duration (e.g. 30s, 5m)"),
                F("description", "Description", "text", false, "", "Description of the loop")
            ]
        },
        new ComponentMeta
        {
            Type = "while", Tag = "while", Name = "While Loop", Category = "Control Flow", Section = "flow",
            Description = "Executes inner steps repeatedly while a condition expression remains true.",
            HasContent = false,
            Attributes =
            [
                F("id", "Loop ID", "text", true, "", "Unique step ID"),
                F("condition", "Condition Expression", "text", false, "", "Loop while this condition is true"),
                F("var", "Variable Name", "text", false, "", "Variable name to monitor"),
                F("equals", "Equals Value", "text", false, "", "Loop while variable equals this value"),
                F("max_iterations", "Max Iterations", "int", false, "1000", "Infinite loop guard ceiling"),
                F("timeout", "Timeout Duration", "text", false, "", "Optional timeout duration for entire while loop"),
                F("on_error", "On Error Action", "select", false, "stop", "", ["stop", "continue", "retry"]),
                F("description", "Description", "text", false, "", "Description of the while loop")
            ]
        },
        new ComponentMeta
        {
            Type = "parallel", Tag = "parallel", Name = "Parallel Execution", Category = "Control Flow", Section = "flow",
            Description = "Executes child branches or tasks concurrently across multiple goroutines with synchronization.",
            HasContent = false,
            Attributes =
            [
                F("id", "Parallel Step ID", "text", true, "", "Unique step ID"),
                F("max_threads", "Max Threads", "int", false, "4", "Max concurrent worker threads"),
                F("timeout", "Timeout Duration", "text", false, "", "Optional timeout duration for parallel tasks"),
                F("on_error", "On Error Action", "select", false, "stop", "", ["stop", "continue", "retry"]),
                F("description", "Description", "text", false, "", "Description of parallel block")
            ]
        },

        // 3. Database & SQL
        new ComponentMeta
        {
            Type = "sql", Tag = "sql", Name = "SQL Query / Command", Category = "Database & SQL", Section = "flow",
            Description = "Executes a SQL statement or query with variable binding and optional result mapping.",
            HasContent = true, ContentLabel = "Raw SQL query or DDL command. Variable interpolation e.g. {{.TableName}} supported.",
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("db", "Database Connection", "text", true, "", "Connection name declared in <databases>"),
                F("output_var", "Output Variable", "text", false, "", "Variable name to capture query result set or affected row count"),
                F("timeout", "Timeout Duration", "text", false, "", "Statement execution timeout duration (e.g. 30s, 45m)"),
                F("into", "Into Variable (Alias)", "text", false, "", "Legacy alias for output_var"),
                F("on_error", "On Error", "select", false, "stop", "", ["stop", "continue", "retry"]),
                F("retry_count", "Retry Count", "int", false, "0"),
                F("retry_interval", "Retry Interval", "text", false, "1s"),
                F("description", "Description", "text", false, "", "Human-readable description of this SQL step")
            ]
        },
        new ComponentMeta
        {
            Type = "sql_bulk", Tag = "sql_bulk", Name = "SQL Bulk Insert / Copy", Category = "Database & SQL", Section = "flow",
            Description = "High-performance bulk stream copy of rows from a source SQL query directly into a destination table.",
            HasContent = true, ContentLabel = "Source extraction SQL query that pipes rows directly into the destination table.",
            Attributes =
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
            ]
        },

        // 4. Key-Value & Cache
        new ComponentMeta
        {
            Type = "kv", Tag = "kv", Name = "Key-Value Store", Category = "Key-Value & Cache", Section = "flow",
            Description = "Executes atomic key-value operations (get, put/set, delete/del, scan/list) against embedded or remote KV stores.",
            HasContent = true, ContentLabel = "Optional inline DSL commands (e.g. SET mykey 'myval' or GET mykey).",
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("db", "KV Connection", "text", true, "", "Database connection name configured with a KV driver"),
                F("op", "Operation", "select", false, "get", "Atomic operation", ["get", "put", "delete", "scan"]),
                F("bucket", "Bucket / Keyspace", "text", false, "default", "Bucket name or key prefix namespace"),
                F("key", "Key", "text", false, "", "Target record key (supports {{VarName}})"),
                F("value", "Value", "text", false, "", "Value payload for write operations"),
                F("into", "Output Variable", "text", false, "", "Variable name receiving retrieved value"),
                F("var", "DSL Variable", "text", false, "", "Optional variable containing dynamic DSL command string"),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, ""),
                F("on_error", "On Error", "select", false, "stop", "", ["stop", "continue", "retry"])
            ]
        },
        new ComponentMeta
        {
            Type = "kv_bulk", Tag = "kv_bulk", Name = "KV Bulk Operations", Category = "Key-Value & Cache", Section = "flow",
            Description = "Executes batch key-value insertions, range scans, or prefix queries efficiently.",
            HasContent = false,
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("db", "KV Connection", "text", true, "", "KV database connection handle"),
                F("bucket", "Bucket", "text", false, "default"),
                F("source", "Source Dataset", "text", true, "", "Dataset containing key/value map")
            ]
        },

        // 5. Template & Transform
        new ComponentMeta
        {
            Type = "template", Tag = "template", Name = "Template Generator", Category = "Template & Transform", Section = "flow",
            Description = "Renders text, SQL queries, or configurations using Go text/template with custom pipeline helpers.",
            HasContent = true, ContentLabel = "Template content. Access variables via {{.VarName}} and built-in functions.",
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("into", "Into Variable", "text", false, "", "Variable name to receive rendered text"),
                F("file", "Output / Source File", "text", false, "", "File path to write rendered output or load external template"),
                F("name", "Template Name", "text", false, ""),
                F("output_var", "Output Variable (Alias)", "text", false, ""),
                F("var", "Var (Alias)", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },
        new ComponentMeta
        {
            Type = "template_html", Tag = "template_html", Name = "HTML Template", Category = "Template & Transform", Section = "flow",
            Description = "Safely renders contextual HTML templates with automatic escaping for reports and emails.",
            HasContent = true, ContentLabel = "HTML template body with Go html/template syntax.",
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("into", "Into Variable", "text", false, "", "Variable name to receive rendered HTML"),
                F("file", "Output HTML File", "text", false, "", "File path to write rendered HTML directly"),
                F("name", "Template Name", "text", false, ""),
                F("output_var", "Output Variable (Alias)", "text", false, ""),
                F("var", "Var (Alias)", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },
        new ComponentMeta
        {
            Type = "html_template", Tag = "html_template", Name = "HTML Template (Alias)", Category = "Template & Transform", Section = "flow",
            Description = "Safely renders contextual HTML templates with automatic escaping for reports and emails (alias for template_html).",
            HasContent = true, ContentLabel = "HTML template body with Go html/template syntax.",
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("into", "Into Variable", "text", false, ""),
                F("file", "Output HTML File", "text", false, ""),
                F("name", "Template Name", "text", false, ""),
                F("output_var", "Output Variable (Alias)", "text", false, ""),
                F("var", "Var (Alias)", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },

        // 6. File & Storage
        new ComponentMeta
        {
            Type = "file_read", Tag = "file_read", Name = "Read File", Category = "File & Storage", Section = "flow",
            Description = "Reads file contents from disk into a pipeline variable.",
            HasContent = false,
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("path", "File Path", "text", true, "", "Path to input file on disk (supports {{Variables}})"),
                F("into", "Target Variable", "text", true, "", "Variable name receiving file content"),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },
        new ComponentMeta
        {
            Type = "file_save", Tag = "file_save", Name = "Save File", Category = "File & Storage", Section = "flow",
            Description = "Writes variable text, bytes, or template outputs to a file on disk.",
            HasContent = true, ContentLabel = "Inline text or template to write if source variable is not specified.",
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("path", "File Path", "text", true, "", "Destination file path on disk"),
                F("source", "Source Variable", "text", false, "", "Variable containing data to write"),
                F("mode", "Write Mode", "select", false, "overwrite", "", ["overwrite", "append"]),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },
        new ComponentMeta
        {
            Type = "excel_read", Tag = "excel_read", Name = "Excel Read (.xlsx)", Category = "File & Storage", Section = "flow",
            Description = "Parses Excel worksheets into structured pipeline rows and variables.",
            HasContent = false,
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("file", "Excel File Path", "text", true, "", "Path to .xlsx file on disk"),
                F("sheet", "Sheet Name", "text", false, "Sheet1"),
                F("header", "Has Header Row", "select", false, "true", "", ["true", "false"]),
                F("output_var", "Output Variable", "text", true, "", "Variable name receiving extracted rows as JSON array"),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },
        new ComponentMeta
        {
            Type = "excel_write", Tag = "excel_write", Name = "Excel Write (.xlsx)", Category = "File & Storage", Section = "flow",
            Description = "Exports SQL query results into styled multi-tab Excel spreadsheets.",
            HasContent = true, ContentLabel = "SQL query to extract rows for the worksheet (e.g. SELECT * FROM customers).",
            Attributes =
            [
                F("id", "Step ID", "text", true, "", "Unique step ID"),
                F("file", "Target Excel File", "text", true, "", "Path to write destination .xlsx file"),
                F("sheet", "Sheet Name", "text", false, "Sheet1"),
                F("db", "Database Connection", "text", true, "", "Database connection name executing source export query"),
                F("var", "Query Variable", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },

        // 7. Data Extraction
        new ComponentMeta
        {
            Type = "json_path", Tag = "json_path", Name = "JSON Path Extraction", Category = "Data Extraction", Section = "flow",
            Description = "Queries JSON strings or documents using JSONPath expressions.",
            HasContent = false,
            Attributes =
            [
                F("id", "Step ID", "text", true, ""),
                F("source", "Source JSON Variable", "text", true, ""),
                F("query", "JSONPath Query", "text", true, "", "e.g. $.items[*].id"),
                F("into", "Target Variable", "text", true, ""),
                F("mode", "Output Mode", "select", false, "value", "", ["value", "json", "json_array"]),
                F("file", "Source File Path", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },
        new ComponentMeta
        {
            Type = "yaml_path", Tag = "yaml_path", Name = "YAML Path Extraction", Category = "Data Extraction", Section = "flow",
            Description = "Extracts scalar or complex structures from YAML strings and manifests.",
            HasContent = false,
            Attributes =
            [
                F("id", "Step ID", "text", true, ""),
                F("source", "Source YAML Variable", "text", true, ""),
                F("query", "Path Expression", "text", true, "", "e.g. spec.replicas"),
                F("into", "Target Variable", "text", true, ""),
                F("mode", "Output Mode", "select", false, "value", "", ["value", "json", "json_array", "yaml"]),
                F("file", "Source File Path", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },
        new ComponentMeta
        {
            Type = "xml_xpath", Tag = "xml_xpath", Name = "XML XPath Extraction", Category = "Data Extraction", Section = "flow",
            Description = "Evaluates XPath expressions against XML documents.",
            HasContent = false,
            Attributes =
            [
                F("id", "Step ID", "text", true, ""),
                F("source", "Source XML Variable", "text", true, ""),
                F("query", "XPath Query", "text", true, "", "e.g. //book/title/text()"),
                F("into", "Target Variable", "text", true, ""),
                F("mode", "Output Mode", "select", false, "text", "", ["text", "xml", "json_array"]),
                F("file", "Source File Path", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },
        new ComponentMeta
        {
            Type = "xml_path", Tag = "xml_path", Name = "XML Path Extraction", Category = "Data Extraction", Section = "flow",
            Description = "Evaluates XPath expressions against XML documents (alias for xml_xpath).",
            HasContent = false,
            Attributes =
            [
                F("id", "Step ID", "text", true, ""),
                F("source", "Source XML Variable", "text", true, ""),
                F("query", "XPath Query", "text", true, ""),
                F("into", "Target Variable", "text", true, ""),
                F("mode", "Output Mode", "select", false, "text", "", ["text", "xml", "json_array"]),
                F("file", "Source File Path", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },

        // 8. Quality & Validation
        new ComponentMeta
        {
            Type = "assert", Tag = "assert", Name = "Assertion Check", Category = "Quality & Validation", Section = "preflight",
            Description = "Enforces data quality rules and halts or warns if an expression fails.",
            HasContent = false,
            Attributes =
            [
                F("id", "Step ID", "text", true, ""),
                F("var", "Variable to Evaluate", "text", false, ""),
                F("equals", "Expected Value", "text", false, ""),
                F("condition", "Condition Expression", "text", false, "", "e.g. gt .RecordCount 0"),
                F("message", "Failure Message", "text", false, ""),
                F("on_failure", "On Failure Action", "select", false, "halt", "", ["halt", "warn", "continue"]),
                F("fail_var", "Failure Flag Variable", "text", false, ""),
                F("fail_val", "Failure Flag Value", "text", false, "true"),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("description", "Description", "text", false, "")
            ]
        },

        // 9. Integration & Web
        new ComponentMeta
        {
            Type = "http_client", Tag = "http_client", Name = "HTTP / REST Client", Category = "Integration & Web", Section = "flow",
            Description = "Executes HTTP GET/POST/PUT/DELETE requests with headers and payload handling.",
            HasContent = true, ContentLabel = "Request body content (for POST/PUT). Supports JSON and variable interpolation.",
            Attributes =
            [
                F("id", "Step ID", "text", true, ""),
                F("url", "Endpoint URL", "text", true, "", "Target endpoint URL (supports {{Variables}})"),
                F("method", "HTTP Method", "select", true, "GET", "", ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"]),
                F("headers", "Request Headers", "text", false, "", "Custom headers JSON or K1:V1,K2:V2"),
                F("content_type", "Content-Type", "text", false, ""),
                F("into", "Response Variable", "text", false, ""),
                F("status_into", "Status Code Variable", "text", false, ""),
                F("timeout", "Timeout Duration", "text", false, ""),
                F("proxy", "HTTP Proxy URL", "text", false, ""),
                F("tls_insecure_skip_verify", "Skip TLS Verify", "select", false, "false", "", ["false", "true"]),
                F("follow_redirects", "Follow Redirects", "select", false, "true", "", ["true", "false"]),
                F("description", "Description", "text", false, "")
            ]
        }
    ];
}
