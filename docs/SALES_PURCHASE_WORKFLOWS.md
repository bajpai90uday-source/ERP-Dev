# Sales and Purchase Workflows

## Scope

This slice implements commercial order and receiving workflows against existing company-scoped master data. It does not allocate Sales Order demand, post stock movements, perform inspection disposition, calculate inventory valuation, or run MRP.

## Sales Orders

Routes are under `/api/v1/sales/orders`.

- Create a draft with customer, customer purchase-order reference/date, currency code, and product/variant lines.
- Submit, approve, then confirm as separate commands. The submitter cannot approve the same revision.
- A confirmed order is not edited in place. Amendments create a new numbered draft revision and require the approval/confirmation flow again.
- Fulfillments record references and quantities against confirmed lines. Fulfilled quantity cannot exceed the ordered quantity.
- The order history route returns timestamped actions and actors.

Statuses: `draft`, `submitted`, `approved`, `rejected`, `confirmed`.

## Purchase Requisitions and Orders

Requisition routes are under `/api/v1/purchase/requisitions`; Purchase Order and receipt routes are under `/api/v1/purchase/orders`.

- Create a requisition against active items and compatible units of measure, then submit and independently approve it.
- A Purchase Order may reference an approved requisition and its lines. Cumulative non-rejected Purchase Order commitments cannot exceed each approved requisition line.
- Submit and approve the Purchase Order, then issue it before recording a goods receipt.
- Receipts identify the issued Purchase Order and stable line. Received quantity is stored separately from ordered quantity; outstanding quantity is derived from their difference.
- Receipts cannot exceed the current ordered quantity. Amendments preserve receipt traceability, cannot remove received lines or reduce quantities below what was received, and cannot change a stable line's requisition source.
- Requisition, Purchase Order, and receipt commands are recorded in transaction history.

Statuses:

- Requisition: `draft`, `submitted`, `approved`, `rejected`.
- Purchase Order: `draft`, `submitted`, `approved`, `rejected`, `issued`, `partially_received`, `received`.

## Command safety and validation

Every mutating command requires an `Idempotency-Key` (1-100 characters). Keys are scoped to company and actor; retrying the same operation and payload replays its saved response, while reusing a key for another operation or payload returns conflict. Commands also require the authenticated user's relevant module/action permission and company scope.

Each order uses one three-letter uppercase currency code. Current validation enforces the code shape; a maintained ISO 4217 currency catalogue is not yet configured. Master-data references must be active and belong to the same company. Numeric quantities/prices and request line counts are validated before persistence.

## Verification and release limits

`CommerceApiTests` exercises the HTTP workflows with SQLite-backed integration tests, including independent approval, idempotent replay/key mismatch, requisition commitment limits, partial receipt/outstanding values, over-receipt and over-fulfillment rejection, amendment revisioning, line-source preservation, and history.

The EF migrations are generated and the PostgreSQL SQL script can be generated locally, but no PostgreSQL server was available during implementation. The migrations have not been applied to a live database. Before release, run migration and concurrency tests against PostgreSQL and verify deployment/rollback procedures. Inventory valuation and MRP remain explicitly out of scope until those gates pass.
