# Overview of the `<excel_read>` in Flow

The `<excel_read>` node in `flow` enables direct integration with `.xlsx` workbooks. You can extract data from Excel into JSON for APIs for downstream processing.

## Key Attributes

### `<excel_read>`
* **`file`**: Target path of the `.xlsx` file to read. Supports `{{VarName}}` interpolation.
* **`sheet`**: (Optional) The name of the worksheet to read. Defaults to the first sheet if omitted.
* **`header`**: (Optional) Boolean (`true` / `false`). If `true` (default), the first row is treated as keys for the resulting JSON.
* **`output_var` / `var`**: The pipeline variable where the extracted JSON string will be stored.
---

## Examples

### 1. Reading Excel Data into a JSON API Payload
Extract configuration or bulk upload data from an Excel sheet and send it to an external API endpoint.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="FILE_PATH" type="string" value="./uploads/new_users.xlsx" />
    </variables>
    <flow>
        <!-- Reads the "Users" sheet into JSON (using row 1 as keys) -->
        <excel_read file="{{FILE_PATH}}" sheet="Users" output_var="USER_JSON_DATA" />
        
        <sql_bulk 
            method="POST" 
            uri="https://api.example.com/v1/users/bulk" 
            content_type="application/json" 
            data="{{USER_JSON_DATA}}" />
    </flow>
</pipeline>
```

### 2. Reading Excel Data Without Headers
Sometimes data files don't have headers. You can set `header="false"` to process the raw rows programmatically in a later script.

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <flow>
        <excel_read file="./raw_data/metrics.xlsx" sheet="RawMetrics" header="false" output_var="RAW_DATA" />
        
        <!-- Process the array of arrays in a Go script -->
        <script lang="go">
            package main
            import (
                "fmt"
                "encoding/json"
                "host/vars"
            )
            func main() {
                rawData := vars.GetString("RAW_DATA")
                fmt.Printf("Successfully loaded raw data: %d bytes\n", len(rawData))
                // Add custom JSON unmarshaling and validation here
            }
        </script>
    </flow>
</pipeline>
```
