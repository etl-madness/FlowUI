# Context-Aware HTML Template Node (`<template_html>`) Guide 🌐

The `<template_html>` node (alias: `<html_template>`) renders dynamic HTML reports, emails, dashboard components, and status pages using Go's standard `html/template` package. Unlike standard plaintext templating, `html/template` is context-aware: it automatically inspects the surrounding HTML syntax (tags, attributes, JavaScript, CSS, URIs) and applies the precise escaping required to prevent Cross-Site Scripting (XSS) and code injection vulnerabilities.

---

## 📋 Attribute Specifications

| Attribute | Required | Default | Description |
| :--- | :---: | :---: | :--- |
| `id` | **Yes** | — | Unique step identifier across the pipeline. |
| `into` / `output_var` / `var` | No | — | Pipeline variable name to store the rendered HTML document or fragment. |
| `file` | No | — | File path to write the rendered HTML directly to disk (e.g. `reports/summary.html`). |
| `name` | No | Step `id` | Template name assigned during template parsing. |
| `engine` | No | `html` | Templating engine: `html` (Go `html/template`). |
| `mode` | No | — | Optional file permission or output formatting specification. |
| `timeout` | No | — | Template rendering deadline duration (e.g. `10s`, `1m`). |
| `condition` / `cond` | No | — | Condition expression required to execute this step. |
| `description` | No | — | Human-readable explanation of this HTML template step. |

---

## 📚 Go Standard Library Reference

The `<template_html>` node is powered directly by Go's `html/template` package, which shares the same syntax as `text/template` but adds context-aware HTML escaping. Official documentation:

| Resource | URL |
| :--- | :--- |
| **`html/template` package docs** | [pkg.go.dev/html/template](https://pkg.go.dev/html/template) |
| **`text/template` syntax reference** | [pkg.go.dev/text/template](https://pkg.go.dev/text/template) |
| **Built-in template functions** | [pkg.go.dev/text/template#hdr-Functions](https://pkg.go.dev/text/template#hdr-Functions) |
| **Template actions reference** | [pkg.go.dev/text/template#hdr-Actions](https://pkg.go.dev/text/template#hdr-Actions) |
| **Security model** | [pkg.go.dev/html/template#hdr-Security_Model](https://pkg.go.dev/html/template#hdr-Security_Model) |

> [!TIP]
> `html/template` is a drop-in superset of `text/template`. Any valid `text/template` syntax also works in `<template_html>` — the only difference is the automatic context-sensitive escaping applied at render time.

---

## 🛡️ Context-Aware Escaping Explained

Go's `html/template` parses the full HTML document grammar. When variables are interpolated, it applies different sanitizers depending on the exact context:

| Injection Context | Template Snippet | Automatic Sanitization Applied |
| :--- | :--- | :--- |
| **HTML Body** | `<p>{{.MESSAGE}}</p>` | Converts `<`, `>`, `&`, `"`, and `'` to HTML character entities (`&lt;`, `&gt;`, `&amp;`). |
| **HTML Attribute** | `<input value="{{.NAME}}">` | Quotes and escapes special characters so malicious input cannot break out of attributes. |
| **Hyperlink / URL** | `<a href="{{.LINK}}">` | Validates URI schemes; filters dangerous prefixes like `javascript:`. |
| **Inline JavaScript** | `<script>var id = {{.USER_ID}};</script>` | Encodes values into valid JSON/JavaScript literals, preventing script breakout attacks. |
| **CSS Style Block** | `<div style="color: {{.COLOR}}">` | Strips invalid CSS properties and prevents style-based script injection. |

---

## 💡 Practical Examples

### Example 1: Executive ETL Execution Summary Report (HTML File)

Generates a standalone, styled HTML execution dashboard and writes it directly to disk:

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <variable name="PIPELINE_NAME" type="string" value="Nightly Customer Ledger Sync" />
        <variable name="RECORDS_PROCESSED" type="int" value="142580" />
        <variable name="EXECUTION_STATUS" type="string" value="SUCCESS" />
        <variable name="DURATION" type="string" value="1m 42s" />
    </variables>

    <flow>
        <template_html id="render_dashboard" file="./reports/latest_execution.html">
            <![CDATA[
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <title>ETL Execution Report - {{.PIPELINE_NAME}}</title>
                <style>
                    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #0f172a; color: #f8fafc; padding: 2rem; }
                    .card { background: #1e293b; border-radius: 12px; padding: 24px; max-width: 600px; margin: 0 auto; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.3); border: 1px solid #334155; }
                    .badge { display: inline-block; padding: 4px 12px; border-radius: 9999px; font-weight: bold; font-size: 12px; }
                    .badge-success { background: #065f46; color: #34d399; }
                    .stat { margin: 12px 0; font-size: 14px; color: #94a3b8; }
                    .stat strong { color: #f8fafc; }
                </style>
            </head>
            <body>
                <div class="card">
                    <h2>{{.PIPELINE_NAME}}</h2>
                    <div class="stat">Status: <span class="badge badge-success">{{.EXECUTION_STATUS}}</span></div>
                    <div class="stat">Records Processed: <strong>{{.RECORDS_PROCESSED}}</strong></div>
                    <div class="stat">Duration: <strong>{{.DURATION}}</strong></div>
                    <div class="stat">Generated: <strong>2026-10-11T18:00:00Z</strong></div>
                </div>
            </body>
            </html>
            ]]>
        </template_html>
    </flow>
</pipeline>
```

---

### Example 2: Tabular Report Iterating Over Extracted Database Records

Combines `<sql>` extraction with `<template_html>` collection iteration (`{{range ...}}`):

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <databases>
        <database name="sales_db" driver="sqlite" connection_string="./sales.db" />
    </databases>

    <flow>
        <!-- Extract top sales representatives into JSON array variable -->
        <sql id="fetch_top_sales" db="sales_db" output_var="TOP_REPS">
            SELECT rep_name, region, quarterly_sales
            FROM representatives
            WHERE quarterly_sales >= 100000
            ORDER BY quarterly_sales DESC;
        </sql>

        <!-- Render safe HTML email fragment -->
        <template_html id="format_email_table" into="EMAIL_TABLE_HTML">
            <![CDATA[
            <table border="1" cellpadding="8" cellspacing="0" style="border-collapse: collapse; width: 100%;">
                <tr style="background-color: #2563eb; color: white;">
                    <th>Representative</th>
                    <th>Region</th>
                    <th>Quarterly Sales</th>
                </tr>
                {{range .TOP_REPS}}
                <tr>
                    <td>{{.rep_name}}</td>
                    <td>{{.region}}</td>
                    <td align="right">${{.quarterly_sales}}</td>
                </tr>
                {{else}}
                <tr>
                    <td colspan="3" align="center">No qualifying sales records found.</td>
                </tr>
                {{end}}
            </table>
            ]]>
        </template_html>
    </flow>
</pipeline>
```

---

### Example 3: Contextual XSS Protection Demonstration

Demonstrating that malicious inputs like `<script>alert('xss')</script>` are safely escaped into harmless entities without manual developer sanitization:

```xml
<pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd">
    <variables>
        <!-- Untrusted user input containing malicious markup -->
        <variable name="USER_INPUT" type="string" value="&lt;script&gt;alert('pwnd')&lt;/script&gt;" />
        <variable name="PROFILE_URL" type="string" value="javascript:stealTokens()" />
    </variables>

    <flow>
        <template_html id="safe_rendering" into="SAFE_HTML">
            <div>
                <!-- Automatically escaped to &lt;script&gt;alert('pwnd')&lt;/script&gt; -->
                <p>Welcome, {{.USER_INPUT}}!</p>
                
                <!-- Automatically neutralized because scheme is javascript: -->
                <a href="{{.PROFILE_URL}}">View Profile</a>
            </div>
        </template_html>
    </flow>
</pipeline>
```

---

## 🛠️ Summary Best Practices

1. **Always Use `<template_html>` for Web / Email Output**: Never use plaintext `<template>` for content rendered in web browsers or email clients.
2. **Enclose Markup in CDATA**: Wrap HTML template blocks containing `<`, `>`, or `&` within `<![CDATA[ ... ]]>` to prevent XML parser syntax errors.
3. **Use `into` for Variables, `file` for Disk Output**: Specify `into="VAR_NAME"` if passing HTML to `<http_client>` (e.g. SendGrid or SES API), or `file="path/to/file.html"` to save directly to disk.
4. **Full Syntax & Security Reference**: See the official Go docs at [pkg.go.dev/html/template](https://pkg.go.dev/html/template) for the complete escaping model, supported contexts, and security guarantees.
