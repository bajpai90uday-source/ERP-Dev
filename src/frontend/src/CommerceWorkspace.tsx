import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, ApiError, type Page, type SelectOption } from "./api";

type CommerceKind = "sales-orders" | "purchase-requisitions" | "purchase-orders";

interface CommerceLine {
  id?: string;
  lineId?: string;
  productId?: string;
  productVariantId?: string;
  productCode?: string;
  productName?: string;
  variantCode?: string;
  variantName?: string;
  itemId?: string;
  itemCode?: string;
  itemName?: string;
  unitOfMeasureId?: string;
  unitOfMeasureCode?: string;
  purchaseRequisitionLineId?: string | null;
  quantity: number;
  unitPrice?: number;
  requestedDeliveryDate?: string;
  requiredByDate?: string;
  expectedDeliveryDate?: string;
  fulfilledQuantity?: number;
  outstandingQuantity?: number;
  receivedQuantity?: number;
}

interface CommerceRecord {
  id: string;
  orderNumber?: string;
  requisitionNumber?: string;
  revisionNumber?: number;
  status: string;
  customerId?: string;
  customerCode?: string;
  customerName?: string;
  customerPurchaseOrderReference?: string;
  customerPurchaseOrderDate?: string;
  currencyCode?: string;
  supplierId?: string;
  supplierCode?: string;
  supplierName?: string;
  purchaseRequisitionId?: string;
  lines: CommerceLine[];
}

interface SalesLineForm {
  productId: string;
  productVariantId: string;
  quantity: string;
  unitPrice: string;
  requestedDeliveryDate: string;
}

interface PurchaseLineForm {
  lineId?: string;
  purchaseRequisitionLineId?: string;
  itemId: string;
  unitOfMeasureId: string;
  quantity: string;
  unitPrice: string;
  requiredByDate: string;
  expectedDeliveryDate: string;
}

interface Options {
  customers: SelectOption[];
  suppliers: SelectOption[];
  products: SelectOption[];
  variants: (SelectOption & { productId: string })[];
  items: SelectOption[];
  units: SelectOption[];
  requisitions: (SelectOption & { status: string; lines: CommerceLine[] })[];
  warehouses: SelectOption[];
}

interface Props {
  kind: CommerceKind;
  setError: (message: string) => void;
  onUnauthorized: () => void;
}

const routeByKind: Record<CommerceKind, string> = {
  "sales-orders": "/api/v1/sales/orders",
  "purchase-requisitions": "/api/v1/purchase/requisitions",
  "purchase-orders": "/api/v1/purchase/orders",
};

const titleByKind: Record<CommerceKind, string> = {
  "sales-orders": "Sales orders",
  "purchase-requisitions": "Purchase requisitions",
  "purchase-orders": "Purchase orders",
};

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : "The request could not be completed.";
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401;
}

async function loadOptions(route: string): Promise<Record<string, unknown>[]> {
  const rows: Record<string, unknown>[] = [];
  for (let page = 1; page <= 101; page++) {
    const result = await api<Page<Record<string, unknown>>>(
      `${route}?page=${page}&pageSize=100&active=true`,
    );
    rows.push(...result.items);
    if (rows.length >= result.totalCount) return rows;
  }
  throw new Error("Too many master records to load in one transaction form.");
}

function optionOf(value: Record<string, unknown>): SelectOption {
  return {
    id: String(value.id),
    label: [value.code, value.name].filter(Boolean).join(" — ") || String(value.id),
  };
}

function newSalesLine(): SalesLineForm {
  return {
    productId: "",
    productVariantId: "",
    quantity: "1",
    unitPrice: "0",
    requestedDeliveryDate: "",
  };
}

function newPurchaseLine(kind: CommerceKind): PurchaseLineForm {
  return {
    itemId: "",
    unitOfMeasureId: "",
    quantity: "1",
    unitPrice: "0",
    requiredByDate: kind === "purchase-requisitions" ? "" : "",
    expectedDeliveryDate: "",
  };
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

export default function CommerceWorkspace({ kind, setError, onUnauthorized }: Props) {
  const [records, setRecords] = useState<CommerceRecord[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [searchText, setSearchText] = useState("");
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [options, setOptions] = useState<Options | null>(null);
  const [dialog, setDialog] = useState<
    | { type: "edit"; record: CommerceRecord | null; amendment: boolean }
    | { type: "reject"; record: CommerceRecord }
    | { type: "fulfill"; record: CommerceRecord }
    | { type: "receive"; record: CommerceRecord }
    | null
  >(null);
  const [dialogError, setDialogError] = useState("");
  const route = routeByKind[kind];
  const title = titleByKind[kind];

  const refresh = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: "25" });
      if (search) params.set("search", search);
      const result = await api<Page<CommerceRecord>>(`${route}?${params}`);
      setRecords(result.items);
      setTotalCount(result.totalCount);
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setLoading(false);
    }
  }, [onUnauthorized, page, route, search, setError]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const loadFormOptions = async () => {
    if (options) return options;
    const [customers, suppliers, products, variants, items, units, requisitions, warehouses] =
      await Promise.all([
        loadOptions("/api/v1/master-data/customers"),
        loadOptions("/api/v1/master-data/suppliers"),
        loadOptions("/api/v1/master-data/products"),
        loadOptions("/api/v1/master-data/variants"),
        loadOptions("/api/v1/master-data/items"),
        loadOptions("/api/v1/master-data/units"),
        loadOptions("/api/v1/purchase/requisitions"),
        loadOptions("/api/v1/master-data/warehouses"),
      ]);
    const loaded: Options = {
      customers: customers.map(optionOf),
      suppliers: suppliers.map(optionOf),
      products: products.map(optionOf),
      variants: variants.map((variant) => ({
        ...optionOf(variant),
        productId: String(variant.productId),
      })),
      items: items.map(optionOf),
      units: units.map(optionOf),
      requisitions: requisitions.map((value) => ({
        ...optionOf(value),
        status: String(value.status),
        lines: (value.lines as CommerceLine[]) ?? [],
      })),
      warehouses: warehouses.map(optionOf),
    };
    setOptions(loaded);
    return loaded;
  };

  const openEditor = async (record: CommerceRecord | null, amendment = false) => {
    setDialogError("");
    setError("");
    try {
      await loadFormOptions();
      setDialog({ type: "edit", record, amendment });
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    }
  };

  const openReceipt = async (record: CommerceRecord) => {
    setDialogError("");
    setError("");
    try {
      await loadFormOptions();
      setDialog({ type: "receive", record });
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    }
  };

  const openDispatch = async (record: CommerceRecord) => {
    setDialogError("");
    setError("");
    try {
      await loadFormOptions();
      setDialog({ type: "fulfill", record });
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    }
  };

  const dispatchShipment = async (
    record: CommerceRecord,
    cartonNumber: string,
    lineId: string,
    quantity: string,
    warehouseId: string,
  ) => {
    setBusy(true);
    setDialogError("");
    try {
      const lineQuantity = Number(quantity);
      const plan = await api<{ id: string }>("/api/v1/dispatch/shipments", {
        method: "POST",
        headers: { "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify({
          salesOrderId: record.id,
          lines: [{ salesOrderLineId: lineId, warehouseId, quantity: lineQuantity }],
          cartons: [{
            cartonNumber,
            weight: 0,
            contents: [{ salesOrderLineId: lineId, quantity: lineQuantity }],
          }],
        }),
      });
      await api(`/api/v1/dispatch/shipments/${plan.id}/authorize`, {
        method: "POST",
        headers: { "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify({}),
      });
      await api(`/api/v1/dispatch/shipments/${plan.id}/confirm`, {
        method: "POST",
        headers: { "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify({}),
      });
      setDialog(null);
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setDialogError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  };

  const command = async (
    record: CommerceRecord,
    action: string,
    body: unknown = {},
    method = "POST",
  ) => {
    setBusy(true);
    setError("");
    try {
      await api(`${route}/${record.id}/${action}`, {
        method,
        headers: { "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify(body),
      });
      setDialog(null);
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else if (dialog) setDialogError(messageOf(requestError));
      else setError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  };

  const searchSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setPage(1);
    setSearch(searchText.trim());
  };

  const label = (record: CommerceRecord) =>
    record.orderNumber ?? record.requisitionNumber ?? record.id;

  return (
    <>
      <section className="data-card commerce-card">
        <div className="table-toolbar">
          <form className="search-form" onSubmit={searchSubmit}>
            <label className="search-field">
              <span className="sr-only">Search {title}</span>
              <span className="search-icon" aria-hidden="true">⌕</span>
              <input
                value={searchText}
                onChange={(event) => setSearchText(event.target.value)}
                placeholder={`Search ${title.toLowerCase()}…`}
              />
            </label>
            <button className="secondary-button" type="submit">Search</button>
          </form>
          <button className="primary-button" onClick={() => void openEditor(null)}>
            <span aria-hidden="true">＋</span> New {kind === "sales-orders" ? "sales order" : kind === "purchase-orders" ? "purchase order" : "requisition"}
          </button>
        </div>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Number</th>
                <th>{kind === "sales-orders" ? "Customer / buyer PO" : kind === "purchase-orders" ? "Supplier" : "Lines"}</th>
                <th>Revision</th>
                <th>Status</th>
                {kind !== "purchase-requisitions" && <th>Quantity</th>}
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading && <tr><td className="empty-state" colSpan={6}>Loading transactions…</td></tr>}
              {!loading && records.length === 0 && (
                <tr><td className="empty-state" colSpan={6}>
                  <strong>No transactions found</strong>
                  <span>Create a transaction after setting up its master-data references.</span>
                </td></tr>
              )}
              {!loading && records.map((record) => {
                const draftable = record.status === "draft" || record.status === "rejected";
                return (
                  <tr key={record.id}>
                    <td>
                      <strong>{label(record)}</strong>
                      {kind === "sales-orders" && (
                        <small className="commerce-subline">
                          {record.customerPurchaseOrderReference}
                        </small>
                      )}
                    </td>
                    <td>
                      {kind === "sales-orders"
                        ? record.customerName ?? "—"
                        : kind === "purchase-orders"
                          ? record.supplierName ?? "—"
                          : `${record.lines.length} line(s)`}
                    </td>
                    <td>{record.revisionNumber ?? "—"}</td>
                    <td><span className={`commerce-status ${record.status}`}>{record.status.replaceAll("_", " ")}</span></td>
                    {kind !== "purchase-requisitions" && (
                      <td>
                        {kind === "sales-orders"
                          ? `${record.lines.reduce((sum, line) => sum + line.fulfilledQuantity!, 0)} / ${record.lines.reduce((sum, line) => sum + line.quantity, 0)}`
                          : `${record.lines.reduce((sum, line) => sum + (line.receivedQuantity ?? 0), 0)} / ${record.lines.reduce((sum, line) => sum + line.quantity, 0)}`}
                      </td>
                    )}
                    <td className="row-actions commerce-actions">
                      {draftable && (
                        <button className="text-button" onClick={() => void openEditor(record)}>
                          Edit
                        </button>
                      )}
                      {draftable && <button className="text-button" disabled={busy}
                        onClick={() => void command(record, "submit")}>Submit</button>}
                      {record.status === "submitted" && <>
                        <button className="text-button" disabled={busy}
                          onClick={() => void command(record, "approve")}>Approve</button>
                        <button className="text-button danger-action"
                          onClick={() => { setDialogError(""); setDialog({ type: "reject", record }); }}>Reject</button>
                      </>}
                      {kind === "sales-orders" && record.status === "approved" && (
                        <button className="text-button" disabled={busy}
                          onClick={() => void command(record, "confirm")}>Confirm</button>
                      )}
                      {kind === "purchase-orders" && record.status === "approved" && (
                        <button className="text-button" disabled={busy}
                          onClick={() => void command(record, "issue")}>Issue</button>
                      )}
                      {kind === "sales-orders" && record.status === "confirmed" && (
                        <>
                          <button className="text-button" onClick={() =>
                            void openEditor(record, true)}>Amend</button>
                          <button className="text-button" onClick={() => {
                            void openDispatch(record);
                          }}>Dispatch</button>
                        </>
                      )}
                      {kind === "purchase-orders"
                        && ["issued", "partially_received", "received"].includes(record.status)
                        && <>
                          <button className="text-button" onClick={() => void openReceipt(record)}>Receive</button>
                          <button className="text-button" onClick={() =>
                            void openEditor(record, true)}>Amend</button>
                        </>}
                      <button className="text-button" onClick={() =>
                        window.open(`${route}/${record.id}/history`, "_blank", "noopener,noreferrer")}>
                        History
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <footer className="pagination">
          <span>{totalCount === 0 ? "No records" : `${(page - 1) * 25 + 1}–${Math.min(page * 25, totalCount)} of ${totalCount}`}</span>
          <div>
            <button className="secondary-button compact" disabled={page <= 1 || loading}
              onClick={() => setPage((value) => Math.max(1, value - 1))}>Previous</button>
            <button className="secondary-button compact" disabled={page * 25 >= totalCount || loading}
              onClick={() => setPage((value) => value + 1)}>Next</button>
          </div>
        </footer>
      </section>
      {dialog?.type === "edit" && options && (
        <TransactionEditor
          kind={kind}
          options={options}
          record={dialog.record}
          amendment={dialog.amendment}
          busy={busy}
          error={dialogError}
          onClose={() => setDialog(null)}
          onSave={async (body) => {
            const target = dialog.record;
            const path = target
              ? `${route}/${target.id}/${dialog.amendment ? "amend" : "draft"}`
              : route;
            const method = target && !dialog.amendment ? "PUT" : "POST";
            setBusy(true);
            setDialogError("");
            try {
              await api(path, {
                method,
                headers: { "Idempotency-Key": crypto.randomUUID() },
                body: JSON.stringify(body),
              });
              setDialog(null);
              await refresh();
            } catch (requestError) {
              if (isUnauthorized(requestError)) onUnauthorized();
              else setDialogError(messageOf(requestError));
            } finally {
              setBusy(false);
            }
          }}
        />
      )}
      {dialog?.type === "reject" && (
        <ReasonDialog
          title={`Reject ${label(dialog.record)}`}
          busy={busy}
          error={dialogError}
          onClose={() => setDialog(null)}
          onSubmit={(reason) => void command(dialog.record, "reject", { reason })}
        />
      )}
      {dialog?.type === "fulfill" && (
        <QuantityDialog
          title={`Pack and dispatch · ${label(dialog.record)}`}
          fieldLabel="Carton number"
          fieldMaxLength={60}
          lineLabel="Order line"
          lines={dialog.record.lines
            .filter(line => (line.outstandingQuantity ?? 0) > 0)
            .map(line => ({
              id: line.id!,
              label: `${line.variantCode} · ${line.outstandingQuantity} remaining`,
              maximum: line.outstandingQuantity!,
            }))}
          warehouseOptions={options?.warehouses}
          busy={busy}
          error={dialogError}
          onClose={() => setDialog(null)}
          onSubmit={(cartonNumber, lineId, quantity, warehouseId) =>
            void dispatchShipment(dialog.record, cartonNumber, lineId, quantity, warehouseId)}
        />
      )}
      {dialog?.type === "receive" && options && (
        <QuantityDialog
          title={`Record goods receipt · ${label(dialog.record)}`}
          fieldLabel="Supplier delivery reference"
          lineLabel="Purchase-order line"
          lines={dialog.record.lines
            .filter(line => (line.outstandingQuantity ?? 0) > 0)
            .map(line => ({
              id: line.lineId!,
              label: `${line.itemCode} · ${line.outstandingQuantity} remaining`,
              maximum: line.outstandingQuantity!,
            }))}
          busy={busy}
          error={dialogError}
          warehouseOptions={options.warehouses}
          onClose={() => setDialog(null)}
          onSubmit={(reference, lineId, quantity, warehouseId) =>
            void command(dialog.record, "receipts", {
              warehouseId,
              supplierDeliveryReference: reference,
              lines: [{ purchaseOrderLineId: lineId, quantity: Number(quantity) }],
            })}
        />
      )}
    </>
  );
}

function TransactionEditor({
  kind,
  options,
  record,
  amendment,
  busy,
  error,
  onClose,
  onSave,
}: {
  kind: CommerceKind;
  options: Options;
  record: CommerceRecord | null;
  amendment: boolean;
  busy: boolean;
  error: string;
  onClose: () => void;
  onSave: (body: unknown) => Promise<void>;
}) {
  const [customerId, setCustomerId] = useState(record?.customerId ?? "");
  const [customerReference, setCustomerReference] = useState(
    record?.customerPurchaseOrderReference ?? "",
  );
  const [customerDate, setCustomerDate] = useState(
    record?.customerPurchaseOrderDate ?? today(),
  );
  const [supplierId, setSupplierId] = useState(record?.supplierId ?? "");
  const [currencyCode, setCurrencyCode] = useState(record?.currencyCode ?? "");
  const [requisitionId, setRequisitionId] = useState(record?.purchaseRequisitionId ?? "");
  const [salesLines, setSalesLines] = useState<SalesLineForm[]>(() =>
    record?.lines.map(line => ({
      productId: line.productId ?? "",
      productVariantId: line.productVariantId ?? "",
      quantity: String(line.quantity),
      unitPrice: String(line.unitPrice ?? 0),
      requestedDeliveryDate: line.requestedDeliveryDate ?? "",
    })) ?? [newSalesLine()]);
  const [purchaseLines, setPurchaseLines] = useState<PurchaseLineForm[]>(() =>
    record?.lines.map(line => ({
      lineId: line.lineId,
      purchaseRequisitionLineId: line.purchaseRequisitionLineId ?? undefined,
      itemId: line.itemId ?? "",
      unitOfMeasureId: line.unitOfMeasureId ?? "",
      quantity: String(line.quantity),
      unitPrice: String(line.unitPrice ?? 0),
      requiredByDate: line.requiredByDate ?? "",
      expectedDeliveryDate: line.expectedDeliveryDate ?? "",
    })) ?? [newPurchaseLine(kind)]);
  const requisition = options.requisitions.find(value => value.id === requisitionId);
  const editing = record !== null;
  const isSales = kind === "sales-orders";
  const isRequisition = kind === "purchase-requisitions";
  const title = amendment
    ? `Amend ${record?.orderNumber}`
    : editing ? `Edit ${record?.orderNumber ?? record?.requisitionNumber}` : `New ${titleByKind[kind]}`;

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const body = isSales
      ? {
          customerId,
          customerPurchaseOrderReference: customerReference,
          customerPurchaseOrderDate: customerDate,
          currencyCode,
          lines: salesLines.map(line => ({
            productId: line.productId,
            productVariantId: line.productVariantId,
            quantity: Number(line.quantity),
            unitPrice: Number(line.unitPrice),
            requestedDeliveryDate: line.requestedDeliveryDate,
          })),
        }
      : isRequisition
        ? {
            lines: purchaseLines.map(line => ({
              itemId: line.itemId,
              unitOfMeasureId: line.unitOfMeasureId,
              quantity: Number(line.quantity),
              requiredByDate: line.requiredByDate,
            })),
          }
        : {
            supplierId,
            currencyCode,
            purchaseRequisitionId: requisitionId || null,
            lines: purchaseLines.map(line => ({
              lineId: line.lineId || null,
              purchaseRequisitionLineId: line.purchaseRequisitionLineId || null,
              itemId: line.itemId,
              unitOfMeasureId: line.unitOfMeasureId,
              quantity: Number(line.quantity),
              unitPrice: Number(line.unitPrice),
              expectedDeliveryDate: line.expectedDeliveryDate,
            })),
          };
    void onSave(body);
  };

  return (
    <div className="modal-backdrop" role="presentation">
      <form className="record-dialog commerce-editor" onSubmit={submit}
        role="dialog" aria-modal="true" aria-labelledby="commerce-editor-title">
        <header className="dialog-heading">
          <div>
            <p className="eyebrow">{amendment ? "CONTROLLED AMENDMENT" : "TRANSACTION DRAFT"}</p>
            <h2 id="commerce-editor-title">{title}</h2>
          </div>
          <button type="button" className="icon-button" onClick={onClose} aria-label="Close">×</button>
        </header>
        {error && <div className="alert" role="alert">{error}</div>}
        {isSales ? (
          <>
            <div className="form-grid">
              <label>Customer<select required value={customerId} disabled={amendment}
                onChange={event => setCustomerId(event.target.value)}>
                <option value="">Select customer</option>
                {options.customers.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}
              </select></label>
              <label>Customer PO reference<input required maxLength={80}
                disabled={amendment} value={customerReference}
                onChange={event => setCustomerReference(event.target.value)} /></label>
              <label>Customer PO date<input required type="date" value={customerDate}
                onChange={event => setCustomerDate(event.target.value)} /></label>
              <label>Currency code<input required minLength={3} maxLength={3}
                value={currencyCode} placeholder="e.g. INR"
                onChange={event => setCurrencyCode(event.target.value.toUpperCase())} /></label>
            </div>
            <h3>Order lines</h3>
            {salesLines.map((line, index) => (
              <div className="commerce-line" key={index}>
                <label>Product<select required value={line.productId}
                  onChange={event => setSalesLines(current => current.map((value, row) =>
                    row === index ? { ...value, productId: event.target.value, productVariantId: "" } : value))}>
                  <option value="">Select product</option>
                  {options.products.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}
                </select></label>
                <label>Variant<select required value={line.productVariantId}
                  onChange={event => setSalesLines(current => current.map((value, row) =>
                    row === index ? { ...value, productVariantId: event.target.value } : value))}>
                  <option value="">Select variant</option>
                  {options.variants.filter(option => option.productId === line.productId)
                    .map(option => <option key={option.id} value={option.id}>{option.label}</option>)}
                </select></label>
                <label>Quantity<input required type="number" min="0.000001" step="0.000001"
                  value={line.quantity} onChange={event => setSalesLines(current => current.map((value, row) =>
                    row === index ? { ...value, quantity: event.target.value } : value))} /></label>
                <label>Unit price<input required type="number" min="0" step="0.000001"
                  value={line.unitPrice} onChange={event => setSalesLines(current => current.map((value, row) =>
                    row === index ? { ...value, unitPrice: event.target.value } : value))} /></label>
                <label>Delivery date<input required type="date" value={line.requestedDeliveryDate}
                  onChange={event => setSalesLines(current => current.map((value, row) =>
                    row === index ? { ...value, requestedDeliveryDate: event.target.value } : value))} /></label>
                {salesLines.length > 1 && <button type="button" className="text-button danger-action"
                  onClick={() => setSalesLines(current => current.filter((_, row) => row !== index))}>Remove</button>}
              </div>
            ))}
            <button type="button" className="secondary-button" onClick={() =>
              setSalesLines(current => [...current, newSalesLine()])}>Add line</button>
          </>
        ) : (
          <>
            {!isRequisition && <div className="form-grid">
              <label>Supplier<select required value={supplierId}
                onChange={event => setSupplierId(event.target.value)}>
                <option value="">Select supplier</option>
                {options.suppliers.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}
              </select></label>
              <label>Currency code<input required minLength={3} maxLength={3}
                value={currencyCode} placeholder="e.g. INR"
                onChange={event => setCurrencyCode(event.target.value.toUpperCase())} /></label>
              <label>Approved requisition<select value={requisitionId}
                onChange={event => {
                  setRequisitionId(event.target.value);
                  const selected = options.requisitions.find(value => value.id === event.target.value);
                  if (selected) {
                    setPurchaseLines(selected.lines.map(line => ({
                      purchaseRequisitionLineId: line.id,
                      itemId: line.itemId ?? "",
                      unitOfMeasureId: line.unitOfMeasureId ?? "",
                      quantity: String(line.quantity),
                      unitPrice: "0",
                      requiredByDate: "",
                      expectedDeliveryDate: line.requiredByDate ?? "",
                    })));
                  }
                }}>
                <option value="">Direct purchase</option>
                {options.requisitions.filter(value => value.status === "approved")
                  .map(option => <option key={option.id} value={option.id}>{option.label}</option>)}
              </select></label>
            </div>}
            <h3>{isRequisition ? "Requested materials" : "Purchase order lines"}</h3>
            {purchaseLines.map((line, index) => {
              const source = requisition?.lines.find(value =>
                value.id === line.purchaseRequisitionLineId);
              return (
                <div className="commerce-line" key={index}>
                  {!isRequisition && requisition && <label>Requisition line<select required
                    value={line.purchaseRequisitionLineId ?? ""}
                    onChange={event => {
                      const selected = requisition.lines.find(value => value.id === event.target.value);
                      setPurchaseLines(current => current.map((value, row) => row === index
                        ? {
                            ...value,
                            purchaseRequisitionLineId: selected?.id,
                            itemId: selected?.itemId ?? "",
                            unitOfMeasureId: selected?.unitOfMeasureId ?? "",
                            quantity: selected ? String(selected.quantity) : "1",
                            expectedDeliveryDate: selected?.requiredByDate ?? "",
                          }
                        : value));
                    }}>
                    <option value="">Select requisition line</option>
                    {requisition.lines.map(value => <option key={value.id} value={value.id}>
                      {value.itemCode} · {value.quantity} {value.unitOfMeasureCode}
                    </option>)}
                  </select></label>}
                  {!source && <label>Item<select required value={line.itemId}
                    disabled={Boolean(requisition)}
                    onChange={event => setPurchaseLines(current => current.map((value, row) =>
                      row === index ? { ...value, itemId: event.target.value } : value))}>
                    <option value="">Select item</option>
                    {options.items.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}
                  </select></label>}
                  {!source && <label>Unit<select required value={line.unitOfMeasureId}
                    disabled={Boolean(requisition)}
                    onChange={event => setPurchaseLines(current => current.map((value, row) =>
                      row === index ? { ...value, unitOfMeasureId: event.target.value } : value))}>
                    <option value="">Select unit</option>
                    {options.units.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}
                  </select></label>}
                  <label>Quantity<input required type="number" min="0.000001" step="0.000001"
                    max={source?.quantity}
                    value={line.quantity} onChange={event => setPurchaseLines(current => current.map((value, row) =>
                      row === index ? { ...value, quantity: event.target.value } : value))} /></label>
                  {isRequisition ? (
                    <label>Required by<input required type="date" value={line.requiredByDate}
                      onChange={event => setPurchaseLines(current => current.map((value, row) =>
                        row === index ? { ...value, requiredByDate: event.target.value } : value))} /></label>
                  ) : <>
                    <label>Unit price<input required type="number" min="0" step="0.000001"
                      value={line.unitPrice} onChange={event => setPurchaseLines(current => current.map((value, row) =>
                        row === index ? { ...value, unitPrice: event.target.value } : value))} /></label>
                    <label>Expected delivery<input required type="date" value={line.expectedDeliveryDate}
                      onChange={event => setPurchaseLines(current => current.map((value, row) =>
                        row === index ? { ...value, expectedDeliveryDate: event.target.value } : value))} /></label>
                  </>}
                  {purchaseLines.length > 1 && <button type="button" className="text-button danger-action"
                    onClick={() => setPurchaseLines(current => current.filter((_, row) => row !== index))}>Remove</button>}
                </div>
              );
            })}
            {!requisition && <button type="button" className="secondary-button" onClick={() =>
              setPurchaseLines(current => [...current, newPurchaseLine(kind)])}>Add line</button>}
          </>
        )}
        <footer className="dialog-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Cancel</button>
          <button className="primary-button" type="submit" disabled={busy}>
            {busy ? "Saving…" : amendment ? "Create amendment" : "Save draft"}
          </button>
        </footer>
      </form>
    </div>
  );
}

function ReasonDialog({
  title,
  busy,
  error,
  onClose,
  onSubmit,
}: {
  title: string;
  busy: boolean;
  error: string;
  onClose: () => void;
  onSubmit: (reason: string) => void;
}) {
  const [reason, setReason] = useState("");
  return (
    <div className="modal-backdrop">
      <form className="record-dialog" role="dialog" aria-modal="true"
        onSubmit={event => { event.preventDefault(); onSubmit(reason); }}>
        <header className="dialog-heading"><h2>{title}</h2></header>
        {error && <div className="alert" role="alert">{error}</div>}
        <label>Reason<textarea required maxLength={500} value={reason}
          onChange={event => setReason(event.target.value)} /></label>
        <footer className="dialog-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Cancel</button>
          <button className="primary-button" disabled={busy}>Reject</button>
        </footer>
      </form>
    </div>
  );
}

function QuantityDialog({
  title,
  fieldLabel,
  fieldMaxLength = 80,
  lineLabel,
  lines,
  warehouseOptions,
  busy,
  error,
  onClose,
  onSubmit,
}: {
  title: string;
  fieldLabel: string;
  fieldMaxLength?: number;
  lineLabel: string;
  lines: { id: string; label: string; maximum: number }[];
  warehouseOptions?: SelectOption[];
  busy: boolean;
  error: string;
  onClose: () => void;
  onSubmit: (reference: string, lineId: string, quantity: string, warehouseId: string) => void;
}) {
  const [reference, setReference] = useState("");
  const [lineId, setLineId] = useState(lines[0]?.id ?? "");
  const [warehouseId, setWarehouseId] = useState(warehouseOptions?.[0]?.id ?? "");
  const selectedLine = lines.find(line => line.id === lineId);
  const [quantity, setQuantity] = useState(selectedLine ? String(selectedLine.maximum) : "");
  return (
    <div className="modal-backdrop">
      <form className="record-dialog" role="dialog" aria-modal="true"
        onSubmit={event => { event.preventDefault(); onSubmit(reference, lineId, quantity, warehouseId); }}>
        <header className="dialog-heading"><h2>{title}</h2></header>
        {error && <div className="alert" role="alert">{error}</div>}
        {lines.length === 0 ? <p className="muted">There is no outstanding quantity to record.</p> : <>
          <label>{fieldLabel}<input required maxLength={fieldMaxLength} value={reference}
            onChange={event => setReference(event.target.value)} /></label>
          <label>{lineLabel}<select required value={lineId}
            onChange={event => {
              setLineId(event.target.value);
              const next = lines.find(line => line.id === event.target.value);
              setQuantity(next ? String(next.maximum) : "");
            }}>
            {lines.map(line => <option key={line.id} value={line.id}>{line.label}</option>)}
          </select></label>
          <label>Quantity<input required type="number" min="0.000001" step="0.000001"
            max={selectedLine?.maximum} value={quantity}
            onChange={event => setQuantity(event.target.value)} /></label>
          {warehouseOptions && <label>Warehouse<select required value={warehouseId}
            onChange={event => setWarehouseId(event.target.value)}>
            <option value="">Select warehouse</option>
            {warehouseOptions.map(option =>
              <option key={option.id} value={option.id}>{option.label}</option>)}
          </select></label>}
        </>}
        <footer className="dialog-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Close</button>
          {lines.length > 0 && <button className="primary-button" disabled={busy}>Record</button>}
        </footer>
      </form>
    </div>
  );
}
