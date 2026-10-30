# Manufacturing ERP Implementation Roadmap

## 1. Delivery principles

Deliver one bounded, end-to-end capability at a time. Every slice includes domain invariants, schema/migration, API/application use case, permission and scope enforcement, usable frontend workflow when applicable, audit/history, focused tests, documentation, and observable failure behavior.

Do not build all modules before an operational vertical slice. Do not treat a navigation menu, mock data, or generated schema as delivered business functionality. Keep the frontend, API, and database running locally during early development; decide production topology separately.

## 2. Prerequisites and cross-cutting foundations

**Before application implementation:**

- Confirm target company/site model, first apparel flow, first user roles/approvals, identity provider, local database strategy, backup ownership, privacy boundaries, and valuation/UOM decisions.
- Approve the stack and local port/configuration conventions.
- Create repository setup, dependency/version policy, secret handling, local run instructions, CI, coding standards, and a minimal threat model.

**Shared prerequisites for all modules:**

- Authentication/session, authorization policy and company/site scope.
- Error/validation contracts, structured logging/correlation, time and numeric policies.
- Database migration discipline and integration-test infrastructure.
- Audit/history, document numbering, idempotency, transaction boundaries, outbox contract.
- Common master data and controlled UOM conversions.

These are platform capabilities, not permission to build every module up front.

## 3. Phases and exit gates

### Phase 0 — Product discovery and architecture review

Deliver reviewed requirements, module boundaries, data relationships, workflow maps, assumptions, security boundaries, and decisions list.

**Gate:** owner approves first operational workflow, platform/database/auth choices, local runtime expectations, and unresolved policies are recorded.

### Phase 1 — Local developer foundation and secure access

Create backend solution/layers, frontend shell/project, PostgreSQL local-development setup guidance, health endpoints, configuration/secret handling, test projects, and reproducible local run scripts. Implement login/logout with the selected identity approach, secure server-side session/cookies, CSRF, user provisioning, and an initial administrator path.

**Gate:** both app processes start locally; database migration/connection succeeds; login/logout and session expiry work; unauthenticated/forbidden requests are tested; no development auth bypass or checked-in secrets.

### Phase 2 — Platform scope and master data

Implement company/site scope, role/permission assignment and approval primitives; UOM/conversion, customer, supplier, item/category, warehouse/bin, operation/shift, and basic equipment references. Add controlled search/filter/paging and audit.

**Gate:** database enforces scoped uniqueness/references; authorization and validation tests pass; referenced masters cannot be destructively deleted.

### Phase 3 — Product engineering and first industry pack

Implement product/style and variants, normalized size/color dimensions, versioned BOM and routing, effective dates, review/approval, and snapshots. Configure the initial apparel operations/material categories.

**Gate:** version/effectivity, BOM explosion/rounding, variant identity, approval and snapshot behavior tested; no apparel-only assumption is required by core.

### Phase 4 — Sales order and demand

Implement buyer PO, Sales Order lines/delivery schedules, approval/rejection/amendment, status history, confirmed MTO demand, and MTS demand source distinction.

**Gate:** unique demand creation under retry; unauthorized/self-approval and confirmed-edit rules pass; all orders trace to buyer reference and product/variant.

The Sales Order transaction slice now covers buyer references, product/variant lines, separated submission/approval/confirmation, numbered amendments, partial fulfillment, idempotent commands, permissions, history, and a frontend workspace. This records commercial fulfillment only; it does not create demand allocations or inventory postings. See [Sales and Purchase workflows](./SALES_PURCHASE_WORKFLOWS.md) for implemented scope and verification status.

### Phase 5 — Inventory ledger, procurement, and MRP

Build inventory posting foundation before purchase/production postings: receipt, status, reservation, availability, issue/return, transfer, count/adjustment, lot/batch, reconciliation, and chosen valuation policy. Implement requisition/approval, optional RFQ, PO, partial receipt, inspection disposition, then time-phased MRP suggestions.

**Gate:** PostgreSQL tests verify migrations, ledger projection rebuild, status eligibility, idempotency, transaction rollback, negative-stock prevention, concurrency, receipt/inspection, reservation, and valuation allocations/reversals. MRP results explain their inputs and never auto-purchase.

The initial Purchase Requisition/Purchase Order workflow and traceable partial goods-receipt capture are implemented, including outstanding quantities and requisition commitment checks. Receipts deliberately do not post inventory. Do not start inventory valuation or MRP until the transaction flows and PostgreSQL migration/concurrency gates are verified.

### Phase 6 — Planning and work execution

Implement capacity/line/shift plan, production schedule revisions, demand-linked Work Orders, approval/release snapshots, Job Cards, material issue/return/consumption, WIP movements, partial confirmations, downtime, and controlled hold/cancel/close.

**Gate:** operation quantities reconcile under partial output/rework; invalid/repeated transitions are rejected or idempotently replayed; no unaccepted output becomes available stock.

### Phase 7 — Quality and apparel execution

Implement incoming/in-process/inline/end-line/final inspection, defect/disposition, holds/rework/scrap, and apparel cutting/bundle/line/size-color reports where discovery confirms need.

**Gate:** quality disposition controls eligibility; defect/rework flows reconcile and cannot duplicate or loop; customer/style reports trace to transactions.

### Phase 8 — Job work, maintenance, and workforce

Implement supplier custody and job-work dispatch/receipt/reconciliation/charges, maintenance requests/plans/work orders/spares/downtime, and privacy-controlled HR skills/shift/attendance/team references.

**Gate:** supplier-held quantity cannot appear as warehouse-available; custody reconciles; downtime links to schedule; HR permissions tested separately.

### Phase 9 — Finished goods, packing, and dispatch

Implement quality-released finished-goods receipts, variant-aware pick/pack/cartons, dispatch authorization/documents, partial shipment, and delivery confirmation.

**Gate:** PostgreSQL tests prevent dispatch beyond eligible packed stock or remaining Sales Order quantity; each dispatch traces back to shipment, sales line, work order, lot, and inspection.

### Phase 10 — Costing, notifications, reports, and MES

Add cost capture/variance, accounting boundary, in-app/email outbox delivery, operational dashboards/exports, and versioned MES adapters with logs/retry/reconciliation.

**Gate:** calculations are defined and sourced; email/MES outage/retry does not duplicate postings; reports enforce scope; integration contracts and recovery/reconciliation are tested.

### Phase 11 — Production readiness and industry expansion

Complete threat/security review, performance/load tests, migration rehearsal, backups/restore, access reviews, operational monitoring, training/UAT, data migration, and pilot exit criteria. Add shoes or other industry packs only through separate discovery, extension contracts, data mapping, and regression tests.

**Gate:** business owners accept the end-to-end workflow; restore and failure drills pass; supported processes/limitations and operational ownership are explicit.

## 4. Dependency graph

```text
Product/identity/security/local DB
  -> company scope + audit + approvals + UOM/master data
  -> product/style/variant + BOM/routing revisions
  -> buyer PO/Sales Order + demand sources
  -> inventory ledger + procurement receipt/inspection
  -> MRP + capacity schedule
  -> Work Order release + Job Cards + material postings
  -> production confirmation + quality/rework
  -> accepted WIP/finished goods + packing/dispatch
  -> costing, dashboards, email/MES integrations (incrementally wired)
```

Specific ordering constraints:

- Do not build MRP before there are approved product structures and a defined eligible-stock/reservation model.
- Do not post procurement, production, transfer, or dispatch quantities before the shared inventory ledger service exists.
- Do not release a Work Order before BOM/routing snapshot and demand allocation exist.
- Do not treat production confirmation as quality acceptance or finished-goods receipt.
- Do not implement MES write access before normal ERP commands, authorization, idempotency, and reconciliation are stable.
- Build reporting read models from defined source transactions; avoid KPIs from placeholder/sample data.

## 5. Testing and release strategy

For each phase, run domain unit tests, API/application authorization and validation tests, frontend workflow tests, migration tests, and targeted end-to-end tests. Use PostgreSQL—not only SQLite—for production-critical constraints, isolation, locking, numeric handling, and query semantics. Test retries, duplicate requests, stale revisions, wrong-company references, rejection/revision, partial quantities, and rollback.

Apply migrations only through reviewed deployment steps. Keep credentials and realistic customer/employee data out of source control and test fixtures. Release gates include documented operational limits and rollback/forward-recovery steps.

## 6. First implementation slice recommendation

After architecture decisions are reviewed, implement **local backend foundation + authentication login/logout** as a bounded first slice, since this is the explicitly desired development order. Keep the frontend to a minimal authenticated shell only when required to exercise the backend. Do not build access-control administration, sales, manufacturing modules, or install extra packages until the chosen authentication and local database approach requires them.

## 7. Risks and open decisions

- Local PostgreSQL installation and backup/restore ownership.
- OIDC provider versus local-only account/session for first development phase.
- Shoes vs garment target. This roadmap follows the garment/apparel target until the owner confirms a different first industry.
- Company/site/branch scope and data retention.
- Approval matrix, document numbering, currencies, UOM precision and rounding.
- Inventory status, reservations, negative-stock policy, FIFO vs other valuation.
- Shop-floor device/network/offline requirements and MES ownership.
- HR privacy, country-specific payroll/tax/accounting scope.
- Performance/load, availability, RPO/RTO, deployment topology, and operational support.
