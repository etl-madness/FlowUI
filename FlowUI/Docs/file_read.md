# Overview of the `<file_read>` Node in Flow

The `<file_read>` in flow allow you to interact directly with the local filesystem. You can read configuration files or datasets into pipeline variables.

## Key Attributes

### `<file_read>`
* **`file` / `path` / `filename`**: Target path of the file to load. Supports `{{VarName}}` interpolation.
* **`output_var` / `var`**: The pipeline variable where the file's contents will be stored as a string.

---

## Examples

### 1. Reading a JSON Config and Sending it via HTTP
Load a configuration file into memory and POST it to a remote endpoint.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <scripts>
        <file_read file="./data/payload.json" output_var="JSON_DATA" />
        
        <sql_bulk 
            method="POST" 
            uri="https://api.example.com/sync" 
            content_type="application/json" 
            data="{{JSON_DATA}}" />
    </scripts>
</pipeline>
```

### 2. Reading a SQL Query from File and Executing It
Keep your SQL scripts organized in files, load them dynamically, and execute them on your database.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="primary_db" connection_string="sqlserver://user:pass@localhost:1433" />
    </databases>
    <scripts>
        <!-- Read SQL text into variable QUERY_TEXT -->
        <file_read file="./queries/nightly_cleanup.sql" output_var="QUERY_TEXT" />
        
        <!-- Execute the SQL query -->
        <sql db="primary_db" var="QUERY_TEXT" />
    </scripts>
</pipeline>
```

### 3. Modifying and Resaving a File using Scripts
Read an existing file, process or modify it using a script (like Go, Bash, or PowerShell), and save the results to a new file.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <scripts>
        <!-- 1. Read input CSV -->
        <file_read file="./data/raw_users.csv" output_var="RAW_CSV" />
        
        <!-- 2. Process it via shell script, outputting to CLEAN_CSV -->
        <script lang="bash" output_var="CLEAN_CSV">
            # Using grep to remove empty lines and invalid data
            echo "$RAW_CSV" | grep -v "^$" | grep -i "@example.com"
        </script>
        
        <!-- 3. Save processed results to a new file -->
        <file_save file="./data/clean_users.csv" var="CLEAN_CSV" />
    </scripts>
</pipeline>
```
