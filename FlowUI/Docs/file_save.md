# Overview of the `<file_save>` Node in Flow

The `<file_save>` node in `flow` allows you to interact directly with the local filesystem. You can write generated templates, script outputs, or log data back to disk.

## Key Attributes

### `<file_save>`
* **`file` / `path` / `filename`**: Target path on the local filesystem. Supports `{{VarName}}` interpolation.
* **`var` / `variable`**: (Optional) Source variable containing the text to write. If omitted, the inline text inside the element is used.
* **`append`**: (Optional) Boolean (`true` / `false`). If `true`, text is appended; if `false` (default), the file is overwritten. 

*Note: `<file_save>` automatically creates missing parent directories.*
---

## Examples
    
### 1. Rendering a Template and Saving the Output
Generate a dynamic HTML report using the `<template>` node and save the resulting variable to disk.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="REPORT_DATE" type="string" value="2026-08-25" />
    </variables>
    <scripts>
        <!-- Generate HTML content into REPORT_HTML -->
        <template id="gen_report" output_var="REPORT_HTML">
            <html>
                <body>
                    <h1>Daily Report: {{.REPORT_DATE}}</h1>
                    <p>All systems operational.</p>
                </body>
            </html>
        </template>
        
        <!-- Save the HTML to an output directory -->
        <file_save file="./output/reports/daily_{{REPORT_DATE}}.html" var="REPORT_HTML" />
    </scripts>
</pipeline>
```
    
### 2. Modifying and Resaving a File using Scripts
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
