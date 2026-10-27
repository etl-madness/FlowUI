# Database Query to Template and Standard I/O (`os.stdio`) Output

This guide explains how to query a database, format result rows using the `<template>` engine, and stream the rendered output directly to standard output (`os.stdio` / `os.Stdout`) in Flow.

---

## Pattern Overview

The **Query $\rightarrow$ Template $\rightarrow$ Stdio** pattern decouples data extraction, presentation formatting, and stream writing:

```mermaid
flowchart LR
    DB[(Database)] -->|Streaming Cursor| Foreach["&lt;foreach&gt;<br/>Row Iteration"]
    Foreach -->|Exposes Row Columns as Variables| Template["&lt;template&gt;<br/>Go text/template Engine"]
    Template -->|Writes to output_var| Script["&lt;script language='go'&gt;<br/>host/vars &amp; os.Stdout"]
    Script -->|Direct Stream| Stdio["Standard Output<br/>(os.stdio / Console)"]
```

1. **Database Cursor (`<foreach>`)**: Executes the SQL query and processes rows iteratively with constant $O(1)$ RAM usage.
2. **Variable Binding**: In each loop iteration, all columns in the row (e.g. `id`, `name`, `salary`) are automatically exposed as in-scope pipeline variables.
3. **Template Formatting (`<template>`)**: The row variables are interpolated into custom text formats using Go's `text/template` engine and saved to a target variable (`output_var`).
4. **Standard I/O Writing (`<script>`)**: An embedded Go script imports `"host/vars"` and `"os"` to write the formatted string directly to `os.Stdout`.

---

## Complete Pipeline Example

The pipeline below is available in the repository at [`examples/db_select_template_stdio.xml`](file:///c:/Users/U00001/source/repos/etl-madness/go-flow/examples/db_select_template_stdio.xml).

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">

    <!-- 1. Pipeline Variables -->
    <variables>
        <variable name="DBPath" type="string" value="./demo_employees.db" />
        <variable name="MinSalary" type="float" value="100000.00" />
    </variables>

    <!-- 2. Database Connection Definition
         Uses SQLite for an immediately runnable, zero-dependency local example.
         For other engines, see the connection string section below.
    -->
    <databases>
        <database name="sample_db" driver="sqlite" connection_string="{{DBPath}}" />
    </databases>

    <flow>
        <!-- 3. Setup Sample Table and Data (Self-contained demonstration) -->
        <sql id="InitSampleData" db="sample_db">
            <![CDATA[
                CREATE TABLE IF NOT EXISTS employees (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    department TEXT NOT NULL,
                    salary REAL NOT NULL
                );
                DELETE FROM employees;
                INSERT INTO employees (id, name, department, salary) VALUES
                    (101, 'Alice Smith', 'Engineering', 125000.50),
                    (102, 'Bob Jones', 'Data Science', 118000.00),
                    (103, 'Carol White', 'DevOps', 132500.75);
            ]]>
        </sql>

        <!-- 4. Query Database and Iterate Over Rows in a Streaming Foreach Loop -->
        <foreach id="SelectEmployees" db="sample_db">
            <![CDATA[
                SELECT id, name, department, salary
                FROM employees
                WHERE salary >= {{MinSalary}}
                ORDER BY id;
            ]]>

            <!-- 5. Render Row Data into a Variable Using Go text/template -->
            <template id="FormatEmployeeRow" output_var="FormattedRow">
==================================================
Employee ID: {{.id}}
Name:        {{.name}}
Department:  {{.department}}
Salary:      ${{.salary}}
==================================================
            </template>

            <!-- 6. Print Formatted Output Directly to Standard Output (os.stdio / os.Stdout) -->
            <script id="PrintToStdio" language="go">
                <![CDATA[
                package main

                import (
                    "fmt"
                    "os"
                    "host/vars"
                )

                func main() {
                    // Fetch the rendered template text from pipeline variables
                    rowText := vars.GetString("FormattedRow")

                    // Output directly to os.Stdout (os.stdio)
                    fmt.Fprintln(os.Stdout, rowText)
                }
                ]]>
            </script>
        </foreach>
    </flow>
</pipeline>
```

---

## Component Breakdown

### 1. Database Configuration (`<databases>`)

The example uses SQLite for immediate, zero-dependency execution. You can target any supported database engine by updating the `driver` and `connection_string` attributes:

```xml
<!-- SQL Server -->
<database name="sample_db" driver="sqlserver" 
          connection_string="sqlserver://dbhost:1433?database=Production&amp;integrated+security=true&amp;trustServerCertificate=true" />

<!-- PostgreSQL -->
<database name="sample_db" driver="postgres" 
          connection_string="postgres://app_user:secret@localhost:5432/production?sslmode=disable" />

<!-- MySQL -->
<database name="sample_db" driver="mysql" 
          connection_string="app_user:secret@tcp(localhost:3306)/production" />

<!-- Oracle -->
<database name="sample_db" driver="oracle" 
          connection_string="oracle://app_user:secret@localhost:1521/ORCLPDB1" />
```

### 2. Streaming Query (`<foreach>`)

The `<foreach>` container executes the SQL query in streaming mode:

* Query results are streamed row-by-row rather than loading the entire result set into memory.
* Columns returned by the `SELECT` clause are automatically registered as current-row variables accessible inside the loop body.
* Variable interpolation (`{{MinSalary}}`) allows parameterized filtering directly within the query.

### 3. Template Engine (`<template>`)

The `<template>` node leverages Go's standard `text/template` library:

* **Variable Access**: Row columns are accessed using dot notation (`{{.id}}`, `{{.name}}`, `{{.department}}`, `{{.salary}}`).
* **Conditionals & Logic**: Native Go template directives such as `{{if .department}}...{{else}}...{{end}}` can be used.
* **`output_var`**: Defines the pipeline variable where the rendered text string is stored.

### 4. Printing to `os.stdio` (`<script language="go">`)

The embedded Go script provides direct access to operating system file descriptors and standard streams:

* **`"host/vars"`**: Built-in host integration package that exposes pipeline variables to the script runtime. Use `vars.GetString("FormattedRow")` to retrieve the template output.
* **`"os"` & `"fmt"`**: Standard Go packages. `fmt.Fprintln(os.Stdout, rowText)` or `os.Stdout.WriteString(rowText + "\n")` flushes the rendered text to standard output (`os.stdio`).

---

## Alternative Script Languages

While Go is fast and portable with zero external interpreter dependencies, the print step can also be written in other supported scripting languages:

### PowerShell
```xml
<script id="PrintStdio" language="powershell">
    Write-Host "{{FormattedRow}}"
</script>
```

### Bash / Linux Shell
```xml
<script id="PrintStdio" language="bash">
    echo "$FormattedRow"
</script>
```

### Windows Command Prompt (`cmd`)
```xml
<script id="PrintStdio" language="cmd">
    echo {{FormattedRow}}
</script>
```

---

## Execution Instructions

### Running from the Command Line

Run the example using `flow.exe`:

```powershell
.\flow.exe -file .\examples\db_select_template_stdio.xml
```

### Overriding Parameters at Runtime

Override variables on the command line without modifying the XML file:

```powershell
.\flow.exe -file .\examples\db_select_template_stdio.xml -vars "MinSalary=125000.00"
```

### Console Output

```text
==================================================
Employee ID: 101
Name:        Alice Smith
Department:  Engineering
Salary:      $125000.5
==================================================
==================================================
Employee ID: 102
Name:        Bob Jones
Department:  Data Science
Salary:      $118000
==================================================
==================================================
Employee ID: 103
Name:        Carol White
Department:  DevOps
Salary:      $132500.75
==================================================
```

---

## Best Practices

> [!TIP]
> **Use CDATA Blocks for SQL & Scripts**: Wrapping queries and script code inside `<![CDATA[ ... ]]>` avoids XML entity escaping issues with characters like `<`, `>`, `&`, or `&&`.

> [!NOTE]
> **Memory Efficiency**: The `<foreach>` node uses a streaming cursor by default. Even when iterating over millions of rows, memory consumption remains constant ($O(1)$).

> [!IMPORTANT]
> **Case Sensitivity**: Database column names exposed as variables within Go templates match the case returned by the database driver. For consistency across database engines (e.g. Oracle returning uppercase identifiers), use explicit SQL column aliases (e.g. `SELECT id AS id, name AS name ...`).
