# Audit history implementation plan

Updated on September 7, 2026 after review. The first-release scope is accepted, with the header popup and mandatory mutation auditing specified below. This remains a design plan; no application behavior has been changed.

Build an in-page Project history popup, opened by a small history icon immediately before Logout in the top-right header. Keep the current page and its drafts in place. The popup and each config/flag History shortcut use the same project-scoped feed, backed by one append-only audit table in the existing PostgreSQL database. Each successful operation records who acted, what changed, where it changed, and when, with before/after snapshots for a readable diff.

## Current implementation

- The runtime uses Blazor Interactive Server, EF Core 10, and PostgreSQL. The README's SQLite references are stale; use `Program.cs` and the PostgreSQL migrations as the implementation reference.
- `Components/Pages/Home.razor` creates and deletes flags/configs immediately, and saves edited items together through `SaveAllChanges`. The pill components edit tracked entity instances.
- `Components/Pages/Settings.razor` adds existing registered users, restores removed memberships, changes roles, removes members, and creates/revokes client keys. Adding a member grants access immediately; no invitation delivery or acceptance exists.
- `Services/ProjectProvisioningService.cs` creates a project, its Development environment, and its owner membership in a transaction.
- `Services/ProjectPermissionService.cs` defines Viewer, Editor, Admin, and Owner access and excludes revoked memberships. Some inline permission checks in Settings omit that exclusion; route these through the shared policy when moving the write operations.
- `Data/ApplicationDbContext.cs` owns Identity users separately. A user joining a project and a person registering an account are different events.
- The public `/api/v1/featureflags` endpoint evaluates flags and returns config values. History can be served directly to Blazor through a server service without changing that API or generated SDKs.

## First release scope

| Resource | Events | Detail |
| --- | --- | --- |
| Project | Created | Name, initial environment, initial owner |
| Flag/gate | Created, updated, deleted | Name, description, enabled state, rollout percentage |
| Config | Created, updated, deleted | Name, description, JSON value, JSON schema |
| Project member | Added, restored, role changed, removed | Target email/display name and old/new role/access |
| Client key | Created, revoked | Key ID/name and status; never the key value |

Use `project.created` for provisioning, with initial environment and owner in its detail, to avoid three near-identical setup rows. Use explicit action codes such as `config.updated` and `member.role_changed`; render the human sentence in the UI. Do not infer actions solely from EF entity state: restoring a membership and changing its role are both database updates but mean different things.

One event per changed resource per committed operation. A Save affecting three resources produces three rows with the same operation ID and timestamp. Discard, unchanged saves, JSON reformatting, validation failures, permission failures, and rolled-back writes produce no success events. Operational error logs remain separate.

Account registration, sign-ins, evaluation requests, pending invitation flows, revision restoration, and exporting history are outside this first release. This plan interprets “user added” as project membership. Account registration history would need a separate system-level access policy and coordination with Identity's context.

## Storage

Add `AuditEvent` and a migration to `FeatureFlagDbContext`.

| Field | Purpose |
| --- | --- |
| `Id` | Increasing database event ID; deterministic ordering for equal timestamps |
| `OperationId` | UUID shared by the events from one Save/action and reused by any retry |
| `OccurredAtUtc` | Server-assigned UTC instant, stored as PostgreSQL `timestamptz` |
| `ProjectId` | Required authorization and query boundary |
| `EnvironmentId`, `EnvironmentName` | Nullable for project/member events; snapshot label for historical context |
| `ActorUserId`, `ActorDisplayName`, `ActorEmail` | Authenticated actor ID and identity as displayed at the time |
| `Action`, `EntityType`, `EntityId`, `EntityName` | Stable action/resource identity and historical label |
| `Before`, `After` | Nullable JSON snapshots of explicitly selected business fields |
| `SchemaVersion` | Small integer identifying the snapshot representation |

Use JSONB for the snapshots, with explicit mappings compatible with the existing JSON approach. Do not serialize whole EF or Identity entities/navigation graphs. Configs include both Schema and Value; flag snapshots include the user-editable properties. The rollout salt is internal and is not an editable field today.

For create events, Before is absent. For deletes, After is absent. For renames, keep the stable entity ID and both names. Snapshots must survive entity deletion and changes to an actor's profile. Avoid cascading foreign keys from events to actors, members, flags, configs, or keys. Retain project IDs/labels as historical context; there is no project-delete flow today, and one must define retention/access before introducing deletion.

Index `(ProjectId, OccurredAtUtc DESC, Id DESC)` and `(ProjectId, EntityType, EntityId, OccurredAtUtc DESC, Id DESC)`. Add a uniqueness constraint on `(OperationId, EntityType, EntityId)` so a retry cannot append the same resource event twice. Audit queries never load all snapshots for the feed.

Use append-only application operations; expose no event edit/delete UI or methods. This provides application history, not independent proof against a database administrator modifying records. Database permissions or tamper-evident external storage can be added if that becomes a requirement.

## Reliable writes

Move the writes out of Home and Settings into concrete actions inheriting one abstract `ProjectMutation<TCommand, TResult>` base. Its public `ExecuteAsync` method is non-virtual and owns the lifecycle below. Derived actions provide their required permission, action metadata, and resource changes through protected hooks; they do not control actor attribution, snapshot policy, saving, audit insertion, or transaction commit. Use the same path from the existing project provisioning service, treating project creation as an authenticated creation operation with its newly allocated project ID. Keep the existing EF contexts and permission service; no mediator, event bus, or further inheritance hierarchy is needed.

1. Resolve the actor from the authenticated server-side principal, never from user-supplied form fields. Use a short-lived context from the existing `IDbContextFactory` for each operation.
2. Check current project permissions, including revocation, and constrain every resource lookup by its project/environment. Apply this to reads of individual history entries as well as feed queries.
3. Load the persisted resource and verify the revision supplied with the draft. Add an EF concurrency token to editable audited records, and advance it on changes. A stale edit/delete fails with a reload/review message; do not silently retry an old draft over a newer value.
4. Capture the persisted Before snapshot, apply and validate the intended changes, build After, and skip semantically unchanged resources.
5. Save the resource and audit row in the same transaction. For database-generated resource IDs, save the resource to obtain its ID, append the event, and commit both writes inside one explicit transaction. Never commit the resource before the audit insertion succeeds. Keep multi-resource Save atomic.
6. Only after commit, clear the submitted drafts and refresh the UI. On failure, preserve drafts and dispose the context so tracked failed audit rows cannot leak into a later save. Disable repeated submit while saving; reuse the operation ID for retry and reconcile a possibly completed operation before resubmitting.

Read config/flag lists without tracking and edit detached copies, including cloned JSON. This prevents Add/Delete or another action from accidentally committing unrelated draft edits. It also ensures the Before snapshot comes from persisted state rather than a mutated UI object. Keep the draft's original revision when reloading the entity inside the write service so stale edits are actually detected.

EF supports atomic SaveChanges and explicit transactions spanning multiple saves: [transaction documentation](https://learn.microsoft.com/en-us/ef/core/saving/transactions). Its concurrency tokens detect changed or deleted rows at save time: [concurrency documentation](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

### Enforce the contract for future mutations

Inheritance supplies the normal workflow but does not prevent a caller from bypassing it. Add a guard in `FeatureFlagDbContext` across synchronous and asynchronous SaveChanges entry points. Only the mutation lifecycle's internal save phase may persist application changes; a derived hook or page calling SaveChanges directly must fail even if an operation is in progress. Scope this internal permission to the operation's context and dispose it on all failure paths; never use a global or circuit-wide audit bypass flag.

Before saving, detect all changes and require coverage for every changed project-owned entity, not just the presence of one audit event. The lifecycle creates Before/After snapshots centrally from an explicit business-field policy and requires action/target metadata for each changed resource. A newly introduced entity without a declared audit policy must fail closed. The project-created event deliberately covers the initial project, environment, and owner membership; record that explicit coverage instead of exempting provisioning. Reject unrelated project IDs and attempts to edit/delete existing audit rows.

For generated IDs, validate pending event coverage before the first save, retain the covered entity references, finalize IDs and insert audit rows before committing the enclosing transaction. An uncovered change or failed event insertion rolls back the whole operation. A no-op has neither changed resources nor events. Keep the existing retry/operation-ID rule so retrying does not duplicate history.

All new project mutation actions must inherit this base and have a check showing the expected audit event. Add regression checks that direct SaveChanges, missing coverage for one item in a batch, and new entity types without an audit policy are rejected. Document this as a repository development convention when implementing it. Database migrations remain an explicit deployment path, outside user activity; production data backfills must declare their audit treatment rather than use a silent runtime bypass.

This guard covers tracked EF writes. Raw SQL and ExecuteUpdate/ExecuteDelete bypass SaveChanges, so prohibit them for project business mutations and check that convention in CI. If a bulk-write path is introduced, it must first provide equivalent transactional audit coverage. This is an application contract, not protection against someone with direct database administration access. [EF bulk-write behavior](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)

## Diff behavior

Compute display differences from the two saved snapshots when opening an event. This preserves the original evidence while allowing the presentation to improve later.

- Scalars: show a labeled old value → new value, e.g. Rollout: 10% → 25%, Enabled: Off → On.
- Configs: compare JSON structurally and list added, removed, and changed paths. Keep Value and Schema changes visibly separate.
- Traverse object properties in a stable sorted order; ignore whitespace and property ordering. Distinguish a missing property from a property whose value is null, and preserve JSON types.
- For the first version, present a changed array as one before/after value, preserving order; item identity and array-move detection are not required.
- Offer formatted Before/After JSON in expanded details for larger edits, including creation/deletion snapshots. Display all historical strings and JSON as escaped text.

Example: `Value /checkout/maxItems: 10 → 20`; `Value /checkout/allowCoupons: Added true`; `Schema /properties/allowCoupons: Added {"type":"boolean"}`.

## Interface and time

Add an icon-only History button in `MainLayout.razor` immediately before Logout, with an accessible label and tooltip naming Project history. Render it only when a project is selected and the current user can view that project's history. No separate Activity page or navigation route is required for the first release.

Click opens an in-page modal with the project name in its heading, a Close button, a fixed header, and a scrollable history list. Use the installed Blazor Bootstrap modal and existing Bootstrap history icon; no additional UI dependency is needed. Host the header button and popup together in a small `InteractiveServer` component because the current layout/router are rendered statically and individual pages select their own interactive modes. Pass a serializable project ID, resolve current authorization on the server, and verify interactive initialization on Home and Settings. Do not make the entire layout interactive solely to add this control. [Existing modal component](https://docs.blazorbootstrap.com/components/modal)

Show newest first with server-side ordering by `OccurredAtUtc DESC, Id DESC`; use the same fields for the pagination cursor. Each row contains actor, action and target name, environment when relevant, and a timestamp column with date above time. Use username/display name with email fallback; today's Identity model has no separate personal-name field. Clicking a labeled disclosure expands that event's diff inside the popup. Add History shortcuts on config/flag pills that open the same popup content with a fixed resource filter; stable IDs survive renames and deleted resources remain readable from general project history. Reuse the content component within an existing interactive page where necessary, rather than relying on an unverified shared service between separate interactive roots.

Keep history self-contained to one project. Every list, filter-option, and event-detail query must require its project ID and validate current membership; filtering in the browser is insufficient. There is no all-project history mode. On project changes, close the popup, cancel pending requests, clear events/filters/cursors and expanded details, and ignore late responses from the previous project. On the project selection screen, omit the button. If access is revoked while open, clear the content when the next query is denied.

The popup should support resource type, actor, environment, and date filters through a compact disclosure, with cursor pagination (initially 50 events, then Load more). Fetch compact row summaries first and snapshots only when expanded. Fetch on opening/reopening, with loading, error/retry, permission-denied, no-events, and no-filter-results states. Recheck permission for each page and detail request. All current project members may view history, using the existing project membership policy.

Store UTC, transport ISO 8601 with an explicit `Z`, and format in the browser with `Intl.DateTimeFormat`. Include seconds and a compact time-zone abbreviation or offset on each time. Omit the separate region label (such as America/Los_Angeles) and visible raw UTC timestamp lines. Preserve the original instant in the semantic time element's datetime attribute. Date filters must translate viewer-local boundaries into UTC, accounting for daylight-saving transitions. Do not use server-side `ToLocalTime()` to approximate the browser's zone. Npgsql stores UTC instants in `timestamptz` ([mapping documentation](https://www.npgsql.org/doc/types/datetime.html)); the browser formatter supports the viewer's local zone ([Intl documentation](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat)).

Match the existing UI instead of introducing a separate visual style: Helvetica Neue/Helvetica/Arial typography, the header's neutral outline-secondary button for History, normal Bootstrap modal borders and corners, compact rows, and simple table-like dividers in expanded details. Keep resource names as ordinary text rather than making them look like blue navigation links. Use the app's existing primary color (`#1b6ec2`) where primary controls are needed. Avoid large custom shadows, decorative status colors, and extra metadata. Retain the collapsible event details and Before/After JSON option; the content scope is already sufficient.

Use semantic disclosure buttons, visible focus, and labels such as Added/Removed/Changed alongside colors. Trap focus while the modal is open; Escape and Close dismiss it and return focus to the opener. Opening, closing, and inspecting history must never submit, discard, or remount the editor underneath. Use a large modal on desktop and a nearly full-screen modal on small screens; stack timestamps below actions on narrow screens. Do not auto-refresh in a way that moves a row while someone is reading its diff.

## Data handling and release boundary

- Never copy API key values, passwords/hashes, security stamps, sessions, or tokens into events. Retain member emails because they identify the requested action.
- Config history intentionally retains old values after a field is removed. Keep complete config history for ordinary application settings. Any identified sensitive config paths must be excluded before storage; masking only the UI would still retain sensitive historical values.
- Start recording from deployment. Existing resources get a Before snapshot on their first change; do not invent earlier actors, timestamps, or a fabricated creation history. Show that history begins when this feature was enabled.
- First-release retention: no automatic expiry or pruning. Monitor growth and choose an explicit retention policy before introducing deletion.

## Delivery sequence and acceptance checks

1. **Persistence and required mutation auditing:** Add the PostgreSQL audit table, mutation base, save guard, snapshot policies, concurrency fields, transaction handling, detached drafts, and recording for every existing write path. Check rollback when audit insertion fails, generated IDs, correct actor, duplicate/no-op behavior, multiple changed resources sharing an operation, and rejection of writes that omit any required audit coverage.
2. **Header popup and local timestamps:** Add the interactive History button immediately before Logout, the project-scoped modal, query service, cursor pagination, filters, and rows. Check cross-project and revoked-member access, same-timestamp pagination, project switching with an in-flight request, focus/Escape/Close behavior, and preservation of unsaved editor contents. Verify the button works on both Home and Settings, is absent without a selected project, and dates work across local midnight and daylight-saving transitions.
3. **Diffs and per-resource history:** Add structural comparison, expanded snapshots, and pill History shortcuts using the same popup content. Check deleted/renamed actors and resources, nested additions/removals, explicit nulls, arrays, schema-only changes, escaped hostile strings, reformat-only edits, and mobile/keyboard interaction.

Use the existing xUnit and bUnit setup for service/component behavior, and Playwright for a create → edit rollout/config → open history flow. Add a PostgreSQL integration check for JSONB mapping, concurrency, migration, and transaction rollback; SQLite-only tests cannot establish those provider-specific guarantees. Include a regression case where a draft edit followed by Add/Delete does not save that draft, and a two-user case where the second stale save is rejected without an audit entry.

Accepted direction: the original first-release event scope, project-contained history in a header popup, descending time order, PostgreSQL storage, and mandatory auditing on future project mutations. Keep the previously proposed defaults of all current members being able to read history and no automatic pruning. Restoration can be a later feature built on these snapshots; it must validate against current rules and create a new audit event rather than rewriting history.
