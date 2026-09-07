# Project mutations

All application writes to project data must run through a service inheriting `ProjectMutation` and its non-virtual `ExecuteAsync` method. `ProjectChanges` contains the current editing actions; `ProjectProvisioningService` handles initial creation. Add a new service inheriting the same base when another feature needs its own actions.

Inside the action, load resources through `MutationContext.Db`, constrain lookups by `ProjectId`, validate input and the draft's revision, apply the change, and call `Record(entity, action)` for each resource. Do not call SaveChanges, SaveMutationAsync, or commit transactions from an action or page. The base resolves the signed-in actor and current membership, enforces coverage, captures snapshots, advances revisions, saves resources and audit rows atomically, and reconciles uncertain commit responses with the operation ID.

Add new resource snapshots and allowed actions/fields explicitly in `AuditSnapshot`. Initial project creation covers its own environment and owner membership in one event. No-op writes do not create events. Never include API key values, passwords, or Identity credentials in snapshots. Use a stable operation ID when retrying the same action.

Use detached entities for editor drafts. Reads and per-operation contexts come from IDbContextFactory; never let one Save persist another form's draft. Add a test for the successful audit event and relevant authorization/failure cases. `MutationArchitectureTests` rejects direct SaveChanges and bulk SQL calls in business code because they bypass the required lifecycle. Its source check is a development guard, not a database security boundary.

Migrations and isolated test-fixture seeding are outside application activity. Test-only fixture helpers may use the internal save phase; there is no runtime setting to disable auditing. Data maintenance needs an explicit, reviewed audit policy.
