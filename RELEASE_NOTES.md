# Release Notes — `FlowUI` (Flow Builder Studio)

## Release Notes (`v1.4.1`) — Pipeline, Options & Config Templates Workspace & Designer Workflows

### Overview

Version `1.4.1` pairs with **`go-flowAPI v1.4.1`** to add end-to-end support for saving, browsing, editing, and instantiating reusable **Pipeline, Options, and Config Templates** with authorship metadata and per-user isolation:

1. **Dedicated Templates Tab (`Components/Tabs/TemplatesTab.razor`, `Components/Pages/Home.razor`)**:
   - Browse and search templates filtered by type (`All`, `Pipeline`, `Config`, `Options`), displaying **Author (`CreatedBy`)**, **Created (UTC)**, and **Updated (UTC)** (with `UpdatedBy`).
   - Create new Pipeline, Config, or Options template drafts directly from the Templates toolbar.
   - Row actions to **Edit / View in Designer**, **Save as Live Pipeline / Config / Options**, or **Delete** (enforcing ownership and `template-viewer` / `template-admin` / `all` permissions).

2. **Visual Designer Template Workflows (`Components/Tabs/PipelineDesignerTab.razor`, `Components/Shared/TemplateActionModal.razor`)**:
   - **Save as Template**: Save any live Pipeline, Config, or Options draft directly as a reusable template via the toolbar button and modal.
   - **Edit Templates in Designer**: Clear **Template Mode** banner showing author and last-updated timestamp, **Run** disabled (templates are non-runnable), **Save Template** to update in place, and **Save as Pipeline / Config / Options...** to promote into a live artifact (with optional pipeline group assignment).

3. **Settings Role Selector (`Components/Tabs/SettingsTab.razor`)**:
   - Added `template-viewer` and `template-admin` role combinations (`write,template-viewer`, `read-only,template-viewer`, `write,template-admin`, `template-admin`) to the API User permission selector.

---

## Release Notes (`v1.3.3`) — Logs Database Server Telemetry, `sql_bulk` Source > Target Display & Kerberos Docs

### Overview

Version `1.3.3` pairs with **`go-flowAPI v1.3.10`** to surface database server telemetry directly in the Logs workspace and expands embedded SQL Server Kerberos connection string documentation:

1. **Logs Tab — Database Server & `sql_bulk` Transfer Visibility (`Components/Tabs/LogsTab.razor`, `Components/Shared/EventDetailModal.razor`, `Models/TelemetryModels.cs`)**:
   - **Pipeline Runs**: Added a **Log DB Server** column showing the database server/catalog receiving run logs (`db_server`).
   - **Step Execution Events**: Added a **DB Server** column displaying the database server associated with each step's SPID.
   - **Cross-Database `sql_bulk` Formatting**: Automatically renders `source->target` telemetry pairs as `source > target` for database server, DB user, and SPID in both the Step Execution Events table and the Pipeline Step Event Detail modal.

2. **Embedded Connection String Documentation (`FlowUI/Docs/database_connection_strings.md`)**:
   - Added `authenticator`, `krb5-credcachefile`, `krb5-keytabfile`, `krb5-configfile`, `krb5-realm`, and `ServerSPN` to the SQL Server options reference.
   - Added Linux/FreeBSD/OpenBSD/macOS Active Directory Kerberos (`authenticator=krb5`) examples and the Unix authentication mode selection note (`SQL Auth` vs `NTLM` vs `Kerberos`).

3. **Unit Tests (`FlowUI.Tests/ModelsTests.cs`)**:
   - Added tests covering `DbServer` / `DatabaseServer` deserialization, `EffectiveDbServer`, and `sql_bulk` `source > target` formatting for server, user, and SPID (**110/110** passing).
