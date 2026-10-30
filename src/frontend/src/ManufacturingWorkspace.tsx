import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { api, ApiError, type Page } from "./api";
import ProductionExecutionWorkspace from "./ProductionExecutionWorkspace";
import QualityJobWorkMaintenanceWorkspace from "./QualityJobWorkMaintenanceWorkspace";

export type ManufacturingView = "work-orders" | "mrp" | "inventory" | "production-planning" | "job-cards"
  | "quality" | "job-work" | "maintenance";

interface WorkOrderOperation {
  id: string;
  sequence: number;
  productionOperationId: string;
  code: string;
  name: string;
  status: string;
}

interface WorkOrderMaterial {
  id: string;
  itemCode: string;
  itemName: string;
  unitOfMeasureCode: string;
  requiredQuantity: number;
}

interface WorkOrder {
  id: string;
  workOrderNumber: string;
  salesOrderId: string;
  salesOrderNumber: string;
  salesOrderLineId: string;
  productCode: string;
  variantCode: string;
  bomVersionId: string;
  bomVersionNumber: number;
  plannedQuantity: number;
  plannedStartDate: string;
  plannedEndDate: string;
  outputWarehouseId: string;
  status: string;
  operations: WorkOrderOperation[];
  materials: WorkOrderMaterial[];
}

interface SalesLine {
  id: string;
  productId: string;
  productVariantId: string;
  productCode: string;
  variantCode: string;
  quantity: number;
  requestedDeliveryDate: string;
  outstandingQuantity: number;
}

interface SalesOrder {
  id: string;
  orderNumber: string;
  status: string;
  lines: SalesLine[];
}

interface Bom {
  id: string;
  productId: string;
  code: string;
}

interface BomVersion {
  id: string;
  bomId: string;
  versionNumber: number;
  status: string;
  effectiveFrom: string;
  effectiveTo?: string | null;
}

interface Option {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

interface MrpMaterial {
  itemId: string;
  itemCode: string;
  itemName: string;
  stockUnitCode: string;
  grossRequirement: number;
  eligibleOnHand: number;
  alreadyReserved: number;
  openPurchaseOrderReceipts: number;
  netRequirement: number;
  purchaseSuggestionQuantity: number;
  needByDate: string;
  contributingOrders: string[];
}

interface InventoryBalance {
  itemId: string;
  itemCode: string;
  itemName: string;
  warehouseId: string;
  warehouseCode: string;
  stockStatus: string;
  onHandQuantity: number;
  reservedQuantity: number;
  availableQuantity: number;
}

interface Props {
  view: ManufacturingView;
  setError: (message: string) => void;
  onUnauthorized: () => void;
}

const today = () => new Date().toISOString().slice(0, 10);
const makeIdempotencyKey = () => crypto.randomUUID();

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : "The request could not be completed.";
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401;
}

async function loadOptions(route: string): Promise<Option[]> {
  const result = await api<Page<Option>>(
    `/api/v1/master-data/${route}?page=1&pageSize=100&active=true`,
  );
  return result.items;
}

export default function ManufacturingWorkspace({ view, setError, onUnauthorized }: Props) {
  if (view === "quality" || view === "job-work" || view === "maintenance") {
    return <QualityJobWorkMaintenanceWorkspace
      view={view}
      setError={setError}
      onUnauthorized={onUnauthorized}
    />;
  }

  if (view === "production-planning" || view === "job-cards") {
    return <ProductionExecutionWorkspace
      view={view}
      setError={setError}
      onUnauthorized={onUnauthorized}
    />;
  }

  return view === "work-orders"
    ? <WorkOrdersWorkspace setError={setError} onUnauthorized={onUnauthorized} />
    : view === "mrp"
      ? <MrpWorkspace setError={setError} onUnauthorized={onUnauthorized} />
      : <InventoryWorkspace setError={setError} onUnauthorized={onUnauthorized} />;
}

function WorkOrdersWorkspace({ setError, onUnauthorized }: Omit<Props, "view">) {
  const [workOrders, setWorkOrders] = useState<WorkOrder[]>([]);
  const [orders, setOrders] = useState<SalesOrder[]>([]);
  const [warehouses, setWarehouses] = useState<Option[]>([]);
  const [operations, setOperations] = useState<Option[]>([]);
  const [boms, setBoms] = useState<Bom[]>([]);
  const [versions, setVersions] = useState<BomVersion[]>([]);
  const [selectedOrderId, setSelectedOrderId] = useState("");
  const [selectedLineId, setSelectedLineId] = useState("");
  const [selectedVersionId, setSelectedVersionId] = useState("");
  const [selectedOperationIds, setSelectedOperationIds] = useState<string[]>([]);
  const [outputWarehouseId, setOutputWarehouseId] = useState("");
  const [plannedQuantity, setPlannedQuantity] = useState("1");
  const [startDate, setStartDate] = useState(today());
  const [endDate, setEndDate] = useState(today());
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [readiness, setReadiness] = useState<Record<string, unknown> | null>(null);
  const [reasonAction, setReasonAction] = useState<{ id: string; action: "hold" | "cancel" } | null>(null);
  const [reason, setReason] = useState("");

  const selectedOrder = orders.find(value => value.id === selectedOrderId);
  const selectedLine = selectedOrder?.lines.find(value => value.id === selectedLineId);
  const eligibleVersions = useMemo(() => versions.filter(value =>
    value.status === "approved"
      && value.effectiveFrom <= startDate
      && (!value.effectiveTo || value.effectiveTo >= startDate)), [startDate, versions]);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const [workOrderPage, salesPage, warehouseOptions, operationOptions] = await Promise.all([
        api<Page<WorkOrder>>("/api/v1/production/work-orders?page=1&pageSize=100"),
        api<Page<SalesOrder>>("/api/v1/sales/orders?page=1&pageSize=100"),
        loadOptions("warehouses"),
        loadOptions("operations"),
      ]);
      setWorkOrders(workOrderPage.items);
      setOrders(salesPage.items.filter(value => value.status === "confirmed"));
      setWarehouses(warehouseOptions);
      setOperations(operationOptions);
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setLoading(false);
    }
  }, [onUnauthorized, setError]);

  useEffect(() => { void refresh(); }, [refresh]);

  useEffect(() => {
    setSelectedLineId("");
    setSelectedVersionId("");
  }, [selectedOrderId]);

  useEffect(() => {
    let active = true;
    const productId = selectedLine?.productId;
    if (!productId) {
      setBoms([]);
      setVersions([]);
      return () => { active = false; };
    }

    void api<Page<Bom>>(
      `/api/v1/master-data/boms?page=1&pageSize=100&productId=${encodeURIComponent(productId)}&active=true`,
    ).then(async result => {
      if (!active) return;
      setBoms(result.items);
      const versionPages = await Promise.all(result.items.map(bom =>
        api<Page<BomVersion>>(`/api/v1/master-data/boms/${bom.id}/versions?page=1&pageSize=100`),
      ));
      if (active) setVersions(versionPages.flatMap(page => page.items));
    }).catch(requestError => {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    });
    return () => { active = false; };
  }, [onUnauthorized, selectedLine?.productId, setError]);

  const command = async (id: string, action: string, body: unknown = {}) => {
    setBusy(true);
    setError("");
    try {
      await api(`/api/v1/production/work-orders/${id}/${action}`, {
        method: "POST",
        headers: { "Idempotency-Key": makeIdempotencyKey() },
        body: JSON.stringify(body),
      });
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  };

  const create = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selectedOrder || !selectedLine || !selectedVersionId
      || selectedOperationIds.length === 0 || !outputWarehouseId) return;

    setBusy(true);
    setError("");
    try {
      await api("/api/v1/production/work-orders", {
        method: "POST",
        headers: { "Idempotency-Key": makeIdempotencyKey() },
        body: JSON.stringify({
          salesOrderId: selectedOrder.id,
          salesOrderLineId: selectedLine.id,
          bomVersionId: selectedVersionId,
          outputWarehouseId,
          plannedQuantity: Number(plannedQuantity),
          plannedStartDate: startDate,
          plannedEndDate: endDate,
          operations: selectedOperationIds.map((productionOperationId, index) => ({
            productionOperationId,
            sequence: index + 1,
            plannedStartDate: startDate,
            plannedEndDate: endDate,
          })),
        }),
      });
      setShowCreate(false);
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  };

  const showMaterials = async (id: string) => {
    try {
      setReadiness(await api<Record<string, unknown>>(
        `/api/v1/production/work-orders/${id}/materials`,
      ));
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    }
  };

  return (
    <>
      <section className="data-card">
        <header className="card-heading">
          <div><h2>Work Orders</h2><p className="muted">Sales-linked production orders and material readiness.</p></div>
          <div className="toolbar">
            <button className="secondary-button" onClick={() => void refresh()}>Refresh</button>
            <button className="primary-button" onClick={() => setShowCreate(true)}>New Work Order</button>
          </div>
        </header>
        {loading ? <p className="muted">Loading Work Orders…</p> : workOrders.length === 0
          ? <p className="muted">No Work Orders yet.</p>
          : <div className="table-scroll"><table>
            <thead><tr><th>Work Order</th><th>Sales Order</th><th>Product / variant</th>
              <th>Planned</th><th>Dates</th><th>Status</th><th>Actions</th></tr></thead>
            <tbody>{workOrders.map(order => <tr key={order.id}>
              <td><strong>{order.workOrderNumber}</strong><small>BOM v{order.bomVersionNumber}</small></td>
              <td>{order.salesOrderNumber}</td>
              <td>{order.productCode} · {order.variantCode}</td>
              <td>{order.plannedQuantity}</td>
              <td>{order.plannedStartDate} – {order.plannedEndDate}</td>
              <td><span className="status-pill">{order.status.replaceAll("_", " ")}</span></td>
              <td className="row-actions">
                <button className="text-button" onClick={() => void showMaterials(order.id)}>Materials</button>
                <button className="text-button" onClick={() =>
                  window.open(`/api/v1/production/work-orders/${order.id}/history`,
                    "_blank", "noopener,noreferrer")}>History</button>
                {order.status === "draft" && <button className="text-button" disabled={busy}
                  onClick={() => void command(order.id, "submit")}>Submit</button>}
                {order.status === "submitted" && <button className="text-button" disabled={busy}
                  onClick={() => void command(order.id, "approve")}>Approve</button>}
                {order.status === "approved" && <button className="text-button" disabled={busy}
                  onClick={() => void command(order.id, "release")}>Release</button>}
                {order.status === "released" && <>
                  <button className="text-button" disabled={busy}
                    onClick={() => { setReason(""); setReasonAction({ id: order.id, action: "hold" }); }}>Hold</button>
                  {order.operations.every(value => value.status === "completed") && (
                    <button className="text-button" disabled={busy}
                      onClick={() => void command(order.id, "complete")}>Complete WO</button>
                  )}
                </>}
                {order.status === "on_hold" && <button className="text-button" disabled={busy}
                  onClick={() => void command(order.id, "release")}>Resume</button>}
                {["draft", "submitted", "approved", "released", "on_hold"].includes(order.status)
                  && <button className="text-button danger-action" disabled={busy}
                    onClick={() => { setReason(""); setReasonAction({ id: order.id, action: "cancel" }); }}>Cancel</button>}
              </td>
            </tr>)}</tbody>
          </table></div>}
      </section>

      {showCreate && <div className="modal-backdrop"><form className="record-dialog" onSubmit={create}>
        <header className="dialog-heading"><h2>Create Work Order</h2></header>
        {orders.length === 0 && <p className="muted">Confirm a Sales Order before creating production work.</p>}
        <label>Confirmed Sales Order<select required value={selectedOrderId}
          onChange={event => setSelectedOrderId(event.target.value)}>
          <option value="">Select Sales Order</option>
          {orders.map(order => <option key={order.id} value={order.id}>{order.orderNumber}</option>)}
        </select></label>
        <label>Order line<select required value={selectedLineId}
          onChange={event => setSelectedLineId(event.target.value)}>
          <option value="">Select product line</option>
          {selectedOrder?.lines.map(line => <option key={line.id} value={line.id}>
            {line.productCode} · {line.variantCode} · {line.outstandingQuantity} remaining
          </option>)}
        </select></label>
        <label>Approved effective BOM<select required value={selectedVersionId}
          onChange={event => setSelectedVersionId(event.target.value)}>
          <option value="">Select BOM version</option>
          {eligibleVersions.map(version => <option key={version.id} value={version.id}>
            {boms.find(value => value.id === version.bomId)?.code ?? "BOM"} · v{version.versionNumber}
          </option>)}
        </select></label>
        <label>Planned quantity<input required type="number" min="0.000001" step="0.000001"
          max={selectedLine?.outstandingQuantity} value={plannedQuantity}
          onChange={event => setPlannedQuantity(event.target.value)} /></label>
        <label>Output warehouse<select required value={outputWarehouseId}
          onChange={event => setOutputWarehouseId(event.target.value)}>
          <option value="">Select warehouse</option>
          {warehouses.map(value => <option key={value.id} value={value.id}>{value.code} · {value.name}</option>)}
        </select></label>
        <label>Add production operation<select value=""
          onChange={event => {
            const operationId = event.target.value;
            if (operationId && !selectedOperationIds.includes(operationId)) {
              setSelectedOperationIds(current => [...current, operationId]);
            }
          }}>
          <option value="">Select operation</option>
          {operations.map(value => <option key={value.id} value={value.id}>{value.code} · {value.name}</option>)}
        </select></label>
        <ol className="selected-operations">
          {selectedOperationIds.map((id, index) => {
            const operation = operations.find(value => value.id === id);
            return <li key={id}>{index + 1}. {operation?.code} · {operation?.name}
              <button type="button" className="text-button danger-action"
                onClick={() => setSelectedOperationIds(current => current.filter(value => value !== id))}>
                Remove
              </button>
            </li>;
          })}
        </ol>
        <div className="form-grid">
          <label>Planned start<input required type="date" value={startDate}
            onChange={event => setStartDate(event.target.value)} /></label>
          <label>Planned end<input required type="date" min={startDate} value={endDate}
            onChange={event => setEndDate(event.target.value)} /></label>
        </div>
        <footer className="dialog-actions">
          <button type="button" className="secondary-button" onClick={() => setShowCreate(false)}>Cancel</button>
          <button className="primary-button" disabled={busy || !eligibleVersions.length
            || !selectedOperationIds.length || !warehouses.length}>Create draft</button>
        </footer>
      </form></div>}

      {readiness && <div className="modal-backdrop"><section className="record-dialog">
        <header className="dialog-heading"><h2>Material readiness</h2></header>
        <p>{String(readiness.workOrderNumber)} · {readiness.isReady ? "Ready" : "Shortage detected"}</p>
        <div className="table-scroll"><table><thead><tr><th>Material</th><th>Required</th>
          <th>Available</th><th>Shortage</th></tr></thead><tbody>
          {((readiness.materials as Record<string, unknown>[] | undefined) ?? []).map(row =>
            <tr key={String(row.itemId)}><td>{String(row.itemCode)} · {String(row.stockUnitCode)}</td>
              <td>{String(row.requiredQuantity)}</td><td>{String(row.availableForWorkOrder)}</td>
              <td>{String(row.shortage)}</td></tr>)}
        </tbody></table></div>
        <footer className="dialog-actions"><button className="secondary-button"
          onClick={() => setReadiness(null)}>Close</button></footer>
      </section></div>}

      {reasonAction && <div className="modal-backdrop"><form className="record-dialog"
        onSubmit={event => {
          event.preventDefault();
          const current = reasonAction;
          setReasonAction(null);
          void command(current.id, current.action, { reason });
        }}>
        <header className="dialog-heading">
          <h2>{reasonAction.action === "hold" ? "Hold Work Order" : "Cancel Work Order"}</h2>
        </header>
        <label>Reason<textarea required maxLength={500} value={reason}
          onChange={event => setReason(event.target.value)} /></label>
        <footer className="dialog-actions">
          <button type="button" className="secondary-button"
            onClick={() => setReasonAction(null)}>Back</button>
          <button className="primary-button" disabled={busy}>
            {reasonAction.action === "hold" ? "Place on hold" : "Cancel Work Order"}
          </button>
        </footer>
      </form></div>}
    </>
  );
}

function MrpWorkspace({ setError, onUnauthorized }: Omit<Props, "view">) {
  const [fromDate, setFromDate] = useState(today());
  const [throughDate, setThroughDate] = useState(today());
  const [materials, setMaterials] = useState<MrpMaterial[]>([]);
  const [busy, setBusy] = useState(false);
  const calculate = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const result = await api<{ materials: MrpMaterial[] }>("/api/v1/planning/mrp/requirements", {
        method: "POST",
        headers: { "Idempotency-Key": makeIdempotencyKey() },
        body: JSON.stringify({ fromDate, throughDate }),
      });
      setMaterials(result.materials);
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  };

  return <section className="data-card">
    <header className="card-heading"><div><h2>Material Requirements Planning</h2>
      <p className="muted">Confirmed demand, eligible stock, reservations, and expected PO receipts.</p></div></header>
    <form className="toolbar" onSubmit={calculate}>
      <label>From<input type="date" required value={fromDate}
        onChange={event => setFromDate(event.target.value)} /></label>
      <label>Through<input type="date" required min={fromDate} value={throughDate}
        onChange={event => setThroughDate(event.target.value)} /></label>
      <button className="primary-button" disabled={busy}>{busy ? "Calculating…" : "Calculate"}</button>
    </form>
    {materials.length > 0 && <div className="table-scroll"><table><thead><tr>
      <th>Material</th><th>Gross</th><th>Available</th><th>Reserved</th>
      <th>PO receipts</th><th>Net required</th><th>Purchase suggestion</th><th>Need by</th><th>Orders</th>
    </tr></thead><tbody>{materials.map(row => <tr key={row.itemId}>
      <td>{row.itemCode} · {row.itemName} ({row.stockUnitCode})</td>
      <td>{row.grossRequirement}</td><td>{row.eligibleOnHand}</td><td>{row.alreadyReserved}</td>
      <td>{row.openPurchaseOrderReceipts}</td><td>{row.netRequirement}</td>
      <td>{row.purchaseSuggestionQuantity}</td><td>{row.needByDate}</td>
      <td>{row.contributingOrders.join(", ")}</td>
    </tr>)}</tbody></table></div>}
    {materials.length === 0 && <p className="muted">Run MRP to calculate material requirements. Suggestions are informational and do not create requisitions.</p>}
  </section>;
}

function InventoryWorkspace({ setError, onUnauthorized }: Omit<Props, "view">) {
  const [balances, setBalances] = useState<InventoryBalance[]>([]);
  const [items, setItems] = useState<Option[]>([]);
  const [warehouses, setWarehouses] = useState<Option[]>([]);
  const [itemId, setItemId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [stockStatus, setStockStatus] = useState("available");
  const [quantity, setQuantity] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const [rows, itemOptions, warehouseOptions] = await Promise.all([
        api<InventoryBalance[]>("/api/v1/inventory/balances"),
        loadOptions("items"),
        loadOptions("warehouses"),
      ]);
      setBalances(rows);
      setItems(itemOptions);
      setWarehouses(warehouseOptions);
      if (!itemId && itemOptions[0]) setItemId(itemOptions[0].id);
      if (!warehouseId && warehouseOptions[0]) setWarehouseId(warehouseOptions[0].id);
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    }
  }, [itemId, onUnauthorized, setError, warehouseId]);

  useEffect(() => { void refresh(); }, [refresh]);

  const adjust = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      await api("/api/v1/inventory/adjustments", {
        method: "POST",
        headers: { "Idempotency-Key": makeIdempotencyKey() },
        body: JSON.stringify({
          itemId, warehouseId, stockStatus,
          quantity: Number(quantity), reason,
        }),
      });
      setQuantity("");
      setReason("");
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  };

  return <>
    <section className="data-card">
      <header className="card-heading"><div><h2>Inventory balances</h2>
        <p className="muted">Receipt, adjustment, reservation, and production postings.</p></div>
        <button className="secondary-button" onClick={() => void refresh()}>Refresh</button></header>
      <form className="toolbar" onSubmit={adjust}>
        <label>Item<select required value={itemId} onChange={event => setItemId(event.target.value)}>
          {items.map(value => <option key={value.id} value={value.id}>{value.code} · {value.name}</option>)}
        </select></label>
        <label>Warehouse<select required value={warehouseId}
          onChange={event => setWarehouseId(event.target.value)}>
          {warehouses.map(value => <option key={value.id} value={value.id}>{value.code} · {value.name}</option>)}
        </select></label>
        <label>Stock status<select value={stockStatus} onChange={event => setStockStatus(event.target.value)}>
          <option value="available">Available</option><option value="hold">Hold</option>
          <option value="quarantine">Quarantine</option>
        </select></label>
        <label>Quantity change<input required type="number" step="0.000001" value={quantity}
          onChange={event => setQuantity(event.target.value)} /></label>
        <label>Reason<input required maxLength={500} value={reason}
          onChange={event => setReason(event.target.value)} /></label>
        <button className="primary-button" disabled={busy}>{busy ? "Posting…" : "Post adjustment"}</button>
      </form>
    </section>
    <section className="data-card"><div className="table-scroll"><table><thead><tr>
      <th>Item</th><th>Warehouse</th><th>Status</th><th>On hand</th><th>Reserved</th><th>Available</th>
    </tr></thead><tbody>{balances.map(balance => <tr key={`${balance.itemId}-${balance.warehouseId}-${balance.stockStatus}`}>
      <td>{balance.itemCode} · {balance.itemName}</td><td>{balance.warehouseCode}</td>
      <td>{balance.stockStatus}</td><td>{balance.onHandQuantity}</td>
      <td>{balance.reservedQuantity}</td><td>{balance.availableQuantity}</td>
    </tr>)}</tbody></table></div></section>
  </>;
}
