# Manufacturing Workflows

## 1. Common command rules

Every command validates authenticated actor, permission, company/site scope, current state, referenced revision, quantities/UOM, and idempotency. UI validation is advisory; the backend and database enforce invariants. A committed posting is immutable. Corrections use linked reversal/adjustment workflows. Each material transition records actor, timestamp, reason where required, correlation/idempotency key, audit record, and outbox event in the same transaction.

Statuses below are initial proposals. Configure exact states and transition authority during discovery; do not permit arbitrary status updates.

## 2. Dependency chain

```text
Platform/security + company scope
  -> master data and UOM
  -> product/variant + approved BOM/routing
  -> customer order or MTS plan -> demand
  -> inventory/procurement visibility -> MRP and capacity plan
  -> approved/released work order
  -> job cards + reservations/issues + WIP movements
  -> production confirmations + inspection/disposition
  -> accepted finished goods -> packing -> authorized dispatch
  -> costing/reporting and reconciliation
```

Email, MES, and reporting subscribe to committed events/queries. They do not bypass the owning workflow. This dependency chain determines the implementation order in the roadmap.

## 3. Customer/buyer order to demand

1. Maintain customer, buyer, contact, commercial terms, product/style, and active variant/size/color attributes.
2. Capture buyer PO reference and date. Create a Sales Order with line quantities/UOM, price/currency, and delivery schedules.
3. Validate customer/variant scope, duplicate references, quantity/price precision, delivery date, and order-specific constraints.
4. Save as Draft; submit for approval according to policy. Store approval against the exact order revision; requester self-approval is blocked where required.
5. Reject with a reason or approve. Confirmation freezes the revision for normal edits.
6. Amend a confirmed order only through a numbered amendment, impact analysis, approval, and history of before/after values. Recalculate affected demand explicitly.
7. Create one idempotent MTO demand source per confirmed line/schedule. It is not proof of available material or completed production.
8. MTS demand originates from approved forecast/replenishment policy and carries a distinct source type; do not count it as a customer order.

## 4. Product engineering and apparel data

1. Maintain common items, stocking/purchase UOM, controlled conversion, category, tracking policy, and status.
2. Apparel product/style defines governed season, size/color axes, product variants, and customer/brand references as required.
3. Author a BOM with fabric, trims, accessories, and packaging; define standard consumption, waste, UOM, alternatives, and effective dates.
4. Author routing operations for configured cutting, sewing, finishing, washing, print, embroidery, packing, or other processes. Identify internal vs subcontract operations.
5. Validate structure, cycles, UOM dimensions, effective dates, and required components. Submit and approve an immutable BOM/routing version.
6. A released work order stores its exact versions and a material/operation snapshot. Later engineering changes create new versions, not retroactive edits.

## 5. MRP and production planning

1. Select an as-of time, planning horizon, company/site, and eligible demand set.
2. Explode each demand through its approved effective BOM snapshot, including variant-specific components, wastage, and lead-time offsets.
3. Calculate gross requirements, eligible on-hand, active reservations/allocations, open supply by expected date, and net shortage. Exclude quarantine/rejected stock and supplier custody from ordinary availability.
4. Persist the run inputs/version and calculation explanation. A rerun creates a new run instead of erasing prior results.
5. Produce order-wise material shortages, earliest availability dates, and purchase suggestions. Users review and approve requisitions; MRP never places a PO automatically.
6. Capacity planning allocates line/work center, operation, machine capability, shifts, target, priority, and date windows. Record schedule revisions and bottlenecks.
7. Flag infeasible schedules and missing material; do not mark planned quantity as available production capacity without resource calendars and assumptions.

## 6. Procurement and receiving

Proposed lifecycle:

```text
Requisition Draft -> Submitted -> Pending Approval -> Approved/Rejected
-> RFQ/Quotation (optional) -> PO Draft -> Pending Approval -> Issued
-> Partially Received -> Received/Closed
```

- Link requisition lines to shortage/demand and PO lines back to requisitions where applicable.
- PO records supplier commitment, ordered UOM/quantity, conversion, price/currency, and promised date. Issuing a PO changes no stock.
- Receive actual quantity against PO lines; partial delivery is supported. Excess beyond configured tolerance requires authorization.
- Create lot/receipt records and inspection requirements. Disposition separately identifies accepted, held/quarantined, rejected, and returned quantity.
- Post accepted/restricted stock movements only through the inventory posting boundary. Supplier invoice references do not substitute for receipt.

## 7. Inventory and warehouse transactions

Availability is calculated by company/site/item/location/bin/lot/status from posted movements less active reservations/allocations. Make on-hand, available, reserved, allocated, in-transit, quarantine, rejected, WIP, finished goods, and subcontractor custody distinguishable.

- **Receipt:** post actual received quantity with source/lot/status; inspection governs eligibility.
- **Reservation:** allocate eligible quantity to demand/work order; does not change physical on-hand.
- **Issue:** check released work order, required item/remaining need, authorized location, eligibility, reservation policy, UOM conversion, and idempotency; append linked movement.
- **Return/consumption/scrap:** separate commands; issued is not equivalent to consumed.
- **Transfer:** validate source and destination; record dispatch, in-transit, and destination receipt if two-step transfer is required. Preserve lot/status.
- **Count/adjustment:** require count authority, reason, variance approval policy, and linked ledger posting. Never set balance directly.
- Prevent negative eligible stock by default. Any exception requires an explicit scoped policy, authorization, and audit.

## 8. Work order, job cards, and material issue

Proposed lifecycle:

```text
Draft -> Pending Approval -> Approved -> Released -> In Progress
-> Partially Completed -> Completed -> Closed
```

Hold, resume, cancel, and controlled quantity revision are explicit transitions.

1. Create a work order linked to demand allocation, product/variant, approved BOM/routing snapshot, planned quantity, site, and production/delivery dates.
2. Check duplicate demand allocation, engineering validity, schedule/resource feasibility, authorization, and configured material policy.
3. Release only after required approvals and snapshot records are committed. A work order retains exact engineering versions.
4. Generate job cards by operation and planned quantity. Assign line/machine/shift/team as applicable.
5. Reserve or issue materials against the work order/job card under configured policy; allow partial issue. Record issue separately from consumption.
6. Track quantities remaining at every operation; do not release an operation before its prerequisite and input quantity are reconciled.
7. Hold/cancel/close requires validated reason and checks for outstanding material, WIP, quality, and child job cards.

## 9. Production confirmation and quality

1. Record confirmation against a released work order/job card/operation using a unique idempotency key.
2. Capture good, rejected, rework, scrap quantities, shift/team/line/machine, actual start/end, downtime, remarks, and supervisor where applicable.
3. Validate against assigned/input/remaining quantity and operation prerequisites. Repeated request with the same key returns the original result, not a duplicate posting.
4. Reported output is not automatically accepted output. Create inspection subject and hold controlled output pending required checks.
5. Incoming, inline, end-line, final, and finished-goods inspection record plan/sample, defect, severity, inspector, and disposition.
6. Disposition moves quantities to accepted/next-operation, hold, rework, or rejection; totals reconcile to inspected/produced quantities.
7. Rework is linked to original defect/output, authorized quantity, and a defined rework route. Rework cannot re-enter indefinitely or be counted twice as good.
8. Finished-goods receipt is permitted only for completed, quality-released output and creates a traceable stock movement.

## 10. Job work and subcontracting

```text
Draft -> Approved -> Material/WIP Dispatched -> Partially Received
-> Reconciled -> Quality Released -> Closed
```

1. Create job-work order from an outsourced routing operation; assign supplier, expected return date, quantities, and charge basis.
2. Dispatch company-owned material/WIP through a custody movement and document. It is not available warehouse stock while at supplier.
3. Record partial processed returns, good/rework/reject quantities, material consumed/returned/scrapped, and supplier charges.
4. Inspect the returned output where required; post eligible inventory only after disposition.
5. Reconcile issued, processed, returned, consumed, rejected, and outstanding quantities before closure. Unexplained variance requires resolution/approval.

## 11. Maintenance and workforce

- Maintenance request identifies machine, severity, downtime, line impact, and reporter; approval/assignment creates maintenance work.
- Record preventive plan, technician, checks, parts issued, cause, corrective/preventive action, cost, approval, and completion time.
- Link downtime to line/capacity and production performance without rewriting confirmed production.
- Manage employee, skill/qualification, training, shift, attendance, leave, and team assignments under privacy permissions.
- Production may reference a qualified team/lead/shift. Do not require one employee per produced unit unless an approved business rule demands it.

## 12. Finished goods, packing, and dispatch

```text
Produced Pending Inspection -> Quality Released -> Available
-> Picked -> Packed -> Dispatch Authorized -> Dispatched -> Delivered
```

1. Receive eligible finished output by order, variant, lot, and warehouse.
2. Allocate pick quantity to a Sales Order line only from eligible available stock and within remaining order quantity.
3. Pack by size/color/variant into cartons with unique IDs; reconcile carton contents to picked quantity.
4. Authorize dispatch after checks for order status, remaining demand, packed quantity, quality status, stock, documents, and permission.
5. Support partial dispatch. Record dispatch documents, transporter/vehicle/tracking, actual time, and delivery confirmation as separate events.
6. Never allow dispatch beyond packed, eligible available stock or remaining authorized quantity. Delivery confirmation is not inferred from dispatch.

## 13. Finance, notifications, MES, and reporting

- Cost entries link procurement price, FIFO allocation if selected, material consumption, labor/time, machine, subcontract charge, scrap, and output. Variance is explained, not silently balanced.
- Accounting/tax postings are a separate integration/release scope. Configuration for a jurisdiction is not a compliance claim.
- Commit in-app/email notifications through an outbox after the business transaction; track attempts, retry, permanent failure, and user preferences.
- MES messages use versioned contracts, authenticated endpoints, idempotency, bounded retries, integration logs, and reconciliation. ERP validations remain mandatory.
- Reports read persisted transactional data or rebuildable read models, enforce authorization, show as-of/filter context, and define calculations. Exports are permission controlled and logged.

## 14. Reconciliation checkpoints

Expose, rather than hide, differences between: ordered/demanded/allocated; gross/available/reserved/scheduled material; PO/received/accepted/rejected quantities; issued/returned/consumed/scrapped materials; operation input/output/reject/rework; subcontractor custody; inspected/accepted/held output; finished goods/packed/dispatched/delivered quantities; and inventory ledger/projection/FIFO value.

## 15. Implemented basic transaction workflow

The initial implementation connects the existing versioned BOM and Sales/Purchase flows to an inventory ledger, MRP response, and Work Orders:

- `POST /api/v1/purchase/orders/{id}/receipts` requires an active `warehouseId`, records stock-unit quantities in the inventory ledger, and adds new receipts to quarantine. Incoming inspection approval is the release boundary; historical receipts may have no warehouse because the original records did not capture one.
- `POST /api/v1/inventory/adjustments` posts a reasoned ledger adjustment. `GET /api/v1/inventory/balances` shows active-warehouse balances by item/status. Available stock excludes hold and quarantine.
- `POST /api/v1/planning/mrp/requirements` plans confirmed Sales Order lines within the requested date range and open Work Orders. It recursively explodes approved, effective BOM versions, applies additive waste, uses effective UOM conversions, and nets free available stock plus open PO receipts by expected date. Purchase suggestions are informational and do not create requisitions.
- `POST /api/v1/production/work-orders` snapshots an approved BOM version, material requirements, and production operations against a confirmed Sales Order line. Work Orders are idempotent and preserve status history.
- Work Order submission/approval, release, hold/resume, completion, and cancellation are explicit actions. Release reserves available materials and generates one idempotent Job Card per operation. Completion consumes reservations only after all operation cards have approved production to target.
- `GET /api/v1/planning/production/orders` reports priority, line schedules, planned versus actual processed output, accepted-good output, and remaining quantity. Due dates within three days are marked at risk when output remains; past due orders are delayed. `PUT /api/v1/planning/production/orders/{id}` updates priority, target quantities, line, machine, team, and dates for approved unreleased orders. Operation targets cannot increase through the route and the final target must match the Work Order quantity.
- `GET /api/v1/production/job-cards` lists operation cards and progress. Cards have controlled ready/start/hold/resume/partial/completed states; later operations cannot start before earlier cards complete. Card targets prevent over-processing.
- `POST /api/v1/production/job-cards/{id}/confirmations` records good, rejected, rework, and scrap quantities plus shift, line, machine, team, and actual timestamps. Quantities must be positive in aggregate and within the unprocessed target. An idempotency key prevents duplicate postings, and a pending confirmation blocks another submission for that card.
- Confirmation approval/rejection requires a different actor than the submitter. Approved good output from the final operation posts to quarantine; only approved final quality inspection moves the accepted quantity to available finished-goods inventory. Rejected, rework, and scrap quantities never become available. Corrections create linked replacement records and require separate supervisor approval.
- A confirmed Sales Order cannot be amended while it has a non-cancelled linked Work Order. Historical BOM versions and Work Order material/operation snapshots are not silently rewritten.

## 16. Quality control, Job Work, and maintenance

- `POST /api/v1/quality/inspections` records incoming, in-process, final, and subcontractor-output inspections against a company-scoped source transaction. Disposition quantities must reconcile to inspected quantity; optional defect-code rows are persisted with the inspection.
- `POST /api/v1/quality/inspections/{id}/approve` requires a different actor than the inspector. Incoming, Job Work, and final accepted quantities move from quarantine to available through linked inventory ledger entries. Rejected, rework, and held quantities remain unavailable in quarantine.
- A production Job Card's next operation cannot start until every preceding operation has a fully accepted, approved in-process inspection. Final-operation good quantity remains quarantined until final inspection.
- `POST /api/v1/job-work/subcontractors` registers an existing supplier for subcontracting. Job Work dispatch is capped by each order material's planned quantity, reduces available warehouse stock, and records supplier custody transactions; returned materials enter quarantine. Custody balances are calculated from immutable dispatch/return events, not added to warehouse availability.
- Job Work receipts are partial, reference-numbered, bounded by the planned quantity, and post processed/rejected output to quarantine. Output requires a separate `jobwork` inspection before accepted goods become available. Material reconciliation prevents returns in excess of supplier-held quantity; charges are recorded against processed quantity.
- `POST /api/v1/maintenance/requests` generates a maintenance Job Card. Spare issues are inventory ledger transactions. Preventive schedules are stored by machine; downtime can be linked to a Work Order and is displayed in production planning, where linked downtime flags outstanding schedules at risk.
- Workflow changes are idempotent, transactional, company-scoped, permission-protected, concurrency-checked where inventory or workflow transitions can race, and recorded in the transaction history endpoints under the respective quality, Job Work, or maintenance resources.

Known scope limits for this foundational release: Quality records rework disposition and blocks unaccepted quantity, but a complete rework-route/reinspection loop is not implemented. The Quality, Job Work, and Maintenance workspace currently uses JSON request forms rather than guided transaction forms. BOM alternative groups require a component-selection workflow and currently stop planning with a validation error; optional BOM lines are omitted. MRP results are returned and summarized in the transaction audit, not persisted as versioned run snapshots. Job Work materials are issued and returned as explicit custody events; consumption/reconciliation to a detailed subcontract process yield remains a separate extension. Preventive schedules are recorded but do not yet auto-generate maintenance requests. Resource calendars/capacity optimization, MRP-to-requisition conversion, inventory valuation, and advanced MRP remain out of scope. Apply the generated EF migration before deploying this workflow to PostgreSQL.
