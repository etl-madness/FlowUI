# Text Template Generator Node (`<template>`) Guide 📄

The `<template>` node renders plaintext, configuration files, SQL queries, dynamic emails, or JSON/YAML payloads by leveraging Go's standard `text/template` engine. It evaluates your active pipeline variables and custom functions, storing the resulting text into a pipeline variable or writing it directly to a disk file.

---

## 📋 Attribute Specifications

| Attribute | Required | Default | Description |
| :--- | :---: | :---: | :--- |
| `id` | **Yes** | — | Unique step identifier across the pipeline. |
| `into` / `output_var` / `var` | No | — | Name of pipeline variable to store the rendered text output. |
| `file` | No | — | File path to write the rendered text directly to disk (or path to external template if used as source). |
| `name` | No | Step `id` | Template name assigned during template parsing. |
| `engine` | No | `text` | Templating engine: `text` (Go `text/template`). |
| `mode` | No | — | Optional output mode or file permission specification. |
| `timeout` | No | — | Template rendering deadline duration (e.g. `10s`, `1m`). |
| `condition` / `cond` | No | — | Condition expression required to execute this step. |
| `description` | No | — | Human-readable explanation of this template step. |

> [!NOTE]
> If you are generating HTML content (such as HTML emails or dashboard reports), use [`<template_html>`](template_html.md) instead, which provides automatic contextual escaping to prevent security vulnerabilities.

---

## 📚 Go Standard Library Reference

The `<template>` node is powered directly by Go's `text/template` package. The following official documentation covers the full syntax, actions, variables, pipelines, and built-in functions available inside your templates:

| Resource | URL |
| :--- | :--- |
| **`text/template` package docs** | [pkg.go.dev/text/template](https://pkg.go.dev/text/template) |
| **Built-in template functions** | [pkg.go.dev/text/template#hdr-Functions](https://pkg.go.dev/text/template#hdr-Functions) |
| **Template actions reference** | [pkg.go.dev/text/template#hdr-Actions](https://pkg.go.dev/text/template#hdr-Actions) |

> [!TIP]
> The full list of built-in functions (`and`, `call`, `html`, `index`, `slice`, `js`, `len`, `not`, `or`, `print`, `printf`, `println`, `urlquery`, comparison operators) is documented at [pkg.go.dev/text/template#hdr-Functions](https://pkg.go.dev/text/template#hdr-Functions).

---

## 🧩 Template Syntax & Expressions

The `<template>` node exposes Go's standard `text/template` engine syntax:

### Accessing Pipeline Variables
- Single variable: `{{.MY_VAR}}` or `{{ .MY_VAR }}`
- Nested structures / JSON objects: `{{.CONFIG.Database.Host}}`
- Missing variable safety: Empty strings are rendered if a variable does not exist.

### Conditional Logic
```go
{{if .IS_PRODUCTION}}
Environment: PRODUCTION
Log Level: ERROR
{{else if .IS_STAGING}}
Environment: STAGING
Log Level: DEBUG
{{else}}
Environment: DEVELOPMENT
Log Level: TRACE
{{end}}
```

### Looping Over Collections (`range`)
```go
{{range .USERS}}
User: {{.name}} | Email: {{.email}} | Role: {{.role}}
{{else}}
No users found in active pipeline batch.
{{end}}
```

### Built-In Comparison Functions
- `eq arg1 arg2`: Returns `true` if `arg1 == arg2`
- `ne arg1 arg2`: Returns `true` if `arg1 != arg2`
- `lt arg1 arg2`: Returns `true` if `arg1 < arg2`
- `le arg1 arg2`: Returns `true` if `arg1 <= arg2`
- `gt arg1 arg2`: Returns `true` if `arg1 > arg2`
- `ge arg1 arg2`: Returns `true` if `arg1 >= arg2`
- `and`, `or`, `not`: Standard boolean operators

---

## 💡 Practical Examples

### Example 1: Inline Template into Pipeline Variable

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="USER_NAME" type="string" value="Alice" />
        <variable name="BATCH_ID" type="string" value="B-2026-101" />
        <variable name="STATUS" type="string" value="SUCCESS" />
    </variables>

    <flow>
        <template id="render_notification" into="SLACK_MESSAGE">
            [ETL Notification]
            Batch: {{.BATCH_ID}}
            Triggered By: {{.USER_NAME}}
            Status: {{.STATUS}}
            Timestamp: 2026-10-11T18:00:00Z
        </template>

        <http_client id="send_slack"
                     url="https://hooks.slack.com/services/T00/B00/X00"
                     method="POST"
                     content_type="application/json">
            {"text": "{{SLACK_MESSAGE}}"}
        </http_client>
    </flow>
</pipeline>
```

---

### Example 2: Generating a JSON Payload for Webhook Calls

Use `<template>` to build JSON payloads with dynamic values:

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="TRANSACTION_ID" type="string" value="TX-99482" />
        <variable name="AMOUNT" type="float" value="1250.75" />
        <variable name="CURRENCY" type="string" value="USD" />
    </variables>

    <flow>
        <template id="build_webhook_payload" into="POST_BODY">
            {
                "event": "transaction_processed",
                "transaction_id": "{{.TRANSACTION_ID}}",
                "amount": {{.AMOUNT}},
                "currency": "{{.CURRENCY}}",
                "reconciled": true
            }
        </template>

        <http_client id="post_webhook"
                     url="https://api.partner.com/v1/events"
                     method="POST"
                     content_type="application/json">
            {{POST_BODY}}
        </http_client>
    </flow>
</pipeline>
```

---

### Example 3: Dynamic SQL Query Generation

Generate dynamic SQL statements based on runtime parameters and execute them with a downstream `<sql>` node:

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="TARGET_SCHEMA" type="string" value="staging" />
        <variable name="MIN_BALANCE" type="int" value="5000" />
    </variables>

    <databases>
        <database name="app_db" driver="postgres" connection_string="postgresql://app:secret@localhost:5432/appdb" />
    </databases>

    <flow>
        <template id="build_archive_query" into="ARCHIVE_SQL">
            INSERT INTO {{.TARGET_SCHEMA}}.high_value_accounts (account_id, balance, tier)
            SELECT id, balance, 'GOLD'
            FROM public.accounts
            WHERE balance >= {{.MIN_BALANCE}};
        </template>

        <sql id="execute_archive" db="app_db">
            {{ARCHIVE_SQL}}
        </sql>
    </flow>
</pipeline>
```

---

### Example 4: Writing Formatted Configuration Files Directly to Disk

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="APP_PORT" type="int" value="8080" />
        <variable name="LOG_LEVEL" type="string" value="INFO" />
    </variables>

    <flow>
        <template id="generate_env_file" file="./dist/.env">
            PORT={{.APP_PORT}}
            LOG_LEVEL={{.LOG_LEVEL}}
            BUILD_DATE=2026-10-11
        </template>
    </flow>
</pipeline>
```

---

## 🛠️ Summary Best Practices

1. **Use `<template>` for Plaintext & Structured Non-HTML Formats**: Ideal for SQL, Markdown, CSV, JSON, YAML, and server configuration files.
2. **Switch to `<template_html>` for HTML Documents**: When rendering HTML emails, webpages, or browser dashboards, use `<template_html>` for automatic contextual XSS protection.
3. **Validate Output with Assertions**: Use `<assert>` on variables populated by `<template>` to ensure required tokens or valid JSON formatting were produced before proceeding.
4. **Full Syntax Reference**: Consult the official Go docs at [pkg.go.dev/text/template](https://pkg.go.dev/text/template) for the complete action syntax, pipelines, variables, and built-in function list.
