import { useCallback, useEffect, useState } from "react";
import { api, ApiError } from "./api";

export type OperationsView = "quality" | "job-work" | "maintenance";

interface Props {
  view: OperationsView;
  setError: (message: string) => void;
  onUnauthorized: () => void;
}

interface QualityInspection {
  id: string;
  inspectionNumber: string;
  stage: string;
  sourceType: string;
  sourceId: string;
  inspectedQuantity: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  reworkQuantity: number;
  holdQuantity: number;
  status: string;
}
interface DefectCode {
  id: string;
  code: string;
  description: string;
}

interface JobWorkOrder {
  id: string;
  orderNumber: string;
  subcontractorId: string;
  plannedQuantity: number;
  processedQuantity: number;
  rejectedQuantity: number;
  status: string;
  outstandingHeldMaterial: number;
  materials: { issueId: string; itemId: string; warehouseId: string; outstandingQuantity: number }[];
  receipts: { id: string; reference: string; processedQuantity: number; rejectedQuantity: number }[];
}

interface MaintenanceRequest {
  id: string;
  requestNumber: string;
  machineId: string;
  description: string;
  priority: string;
  status: string;
  jobCardId: string;
}

interface MachineDowntime {
  id: string;
  machineId: string;
  workOrderId?: string | null;
  reason: string;
  startedAt: string;
  endedAt?: string | null;
}

const requestHeader = () => ({ "Idempotency-Key": crypto.randomUUID() });

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : "The request could not be completed.";
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401;
}

export default function QualityJobWorkMaintenanceWorkspace({
  view,
  setError,
  onUnauthorized,
}: Props) {
  return view === "quality"
    ? <QualityWorkspace setError={setError} onUnauthorized={onUnauthorized} />
    : view === "job-work"
      ? <JobWorkWorkspace setError={setError} onUnauthorized={onUnauthorized} />
      : <MaintenanceWorkspace setError={setError} onUnauthorized={onUnauthorized} />;
}

function QualityWorkspace({ setError, onUnauthorized }: Omit<Props, "view">) {
  const [inspections, setInspections] = useState<QualityInspection[]>([]);
  const [defectCodes, setDefectCodes] = useState<DefectCode[]>([]);
  const [resultSummary, setResultSummary] = useState("");
  const [inspectionBody, setInspectionBody] = useState(JSON.stringify({
    stage: "incoming",
    sourceType: "purchase_receipt_line",
    sourceId: "",
    sourceLineId: "",
    itemId: "",
    warehouseId: "",
    inspectedQuantity: 1,
    acceptedQuantity: 1,
    rejectedQuantity: 0,
    reworkQuantity: 0,
    holdQuantity: 0,
  }, null, 2));
  const [defectBody, setDefectBody] = useState('{"code":"DEFECT","description":"Describe the defect"}');
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const [inspectionRows, defectRows] = await Promise.all([
        api<QualityInspection[]>("/api/v1/quality/inspections"),
        api<DefectCode[]>("/api/v1/quality/defect-codes"),
      ]);
      setInspections(inspectionRows);
      setDefectCodes(defectRows);
    } catch (error) {
      if (isUnauthorized(error)) onUnauthorized();
      else setError(messageOf(error));
    }
  }, [onUnauthorized, setError]);

  useEffect(() => { void refresh(); }, [refresh]);

  const post = async (path: string, body: string) => {
    setBusy(true);
    setError("");
    try {
      const result = await api<unknown>(path, { method: "POST", headers: requestHeader(), body });
      setResultSummary(JSON.stringify(result));
      await refresh();
    } catch (error) {
      if (isUnauthorized(error)) onUnauthorized();
      else setError(messageOf(error));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="workspace-grid">
      <section className="data-card">
        <header className="card-heading">
          <div><h2>Quality inspections</h2><p className="muted">Incoming and production output remain quarantined until approved.</p></div>
          <button className="quiet-button" onClick={() => void refresh()}>Refresh</button>
        </header>
        {inspections.length === 0 ? <p className="empty-state">No inspections recorded.</p> : (
          <div className="table-scroll"><table><thead><tr>
            <th>Inspection</th><th>Stage</th><th>Source</th><th>Inspected</th><th>Disposition</th><th>Status</th><th />
          </tr></thead><tbody>{inspections.map((inspection) => <tr key={inspection.id}>
            <td>{inspection.inspectionNumber}</td><td>{inspection.stage}</td>
            <td>{inspection.sourceType}<small>{inspection.sourceId}</small></td>
            <td>{inspection.inspectedQuantity}</td>
            <td>{inspection.acceptedQuantity} accepted · {inspection.rejectedQuantity} rejected · {inspection.reworkQuantity} rework · {inspection.holdQuantity} hold</td>
            <td>{inspection.status}</td>
            <td>{inspection.status === "pending_approval" && <button className="primary-button compact"
              disabled={busy} onClick={() => void post(`/api/v1/quality/inspections/${inspection.id}/approve`, "{}")}>Approve</button>}</td>
          </tr>)}</tbody></table></div>
        )}
        <h3>Defect codes</h3>
        <div className="table-scroll"><table><thead><tr><th>Code</th><th>Description</th><th>ID</th></tr></thead>
          <tbody>{defectCodes.map((code) => <tr key={code.id}>
            <td>{code.code}</td><td>{code.description}</td><td>{code.id}</td>
          </tr>)}</tbody></table></div>
        {resultSummary && <pre className="muted">{resultSummary}</pre>}
      </section>
      <section className="data-card">
        <header className="card-heading"><div><h2>Record inspection</h2><p className="muted">Use source IDs from the receipt, Job Card, or Job Work receipt.</p></div></header>
        <form onSubmit={(event) => { event.preventDefault(); void post("/api/v1/quality/inspections", inspectionBody); }}>
          <label>Inspection details (JSON)<textarea rows={14} value={inspectionBody} onChange={(event) => setInspectionBody(event.target.value)} /></label>
          <button className="primary-button" disabled={busy}>Record inspection</button>
        </form>
        <form className="toolbar" onSubmit={(event) => { event.preventDefault(); void post("/api/v1/quality/defect-codes", defectBody); }}>
          <label>New defect code (JSON)<textarea rows={3} value={defectBody} onChange={(event) => setDefectBody(event.target.value)} /></label>
          <button className="quiet-button" disabled={busy}>Add defect code</button>
        </form>
      </section>
    </div>
  );
}

function JobWorkWorkspace({ setError, onUnauthorized }: Omit<Props, "view">) {
  const [orders, setOrders] = useState<JobWorkOrder[]>([]);
  const [orderId, setOrderId] = useState("");
  const [profileBody, setProfileBody] = useState('{"supplierId":"","registrationCode":"SUB-001"}');
  const [orderBody, setOrderBody] = useState(JSON.stringify({
    subcontractorId: "",
    outputItemId: "",
    outputWarehouseId: "",
    plannedQuantity: 10,
    unitCharge: 0,
    currencyCode: "USD",
    expectedReturnDate: new Date().toISOString().slice(0, 10),
    materials: [{ itemId: "", warehouseId: "", quantity: 1 }],
  }, null, 2));
  const [dispatchBody, setDispatchBody] = useState('[{"materialIssueId":"","quantity":1}]');
  const [receiptBody, setReceiptBody] = useState('{"reference":"SUB-RECEIPT-001","processedQuantity":1,"rejectedQuantity":0}');
  const [reconcileBody, setReconcileBody] = useState('{"materialIssueId":"","quantity":1}');
  const [busy, setBusy] = useState(false);
  const [resultSummary, setResultSummary] = useState("");

  const refresh = useCallback(async () => {
    try {
      setOrders(await api<JobWorkOrder[]>("/api/v1/job-work/orders"));
    } catch (error) {
      if (isUnauthorized(error)) onUnauthorized();
      else setError(messageOf(error));
    }
  }, [onUnauthorized, setError]);

  useEffect(() => { void refresh(); }, [refresh]);

  const post = async (path: string, body: string) => {
    setBusy(true);
    setError("");
    try {
      const result = await api<JobWorkOrder | string>(path, {
        method: "POST", headers: requestHeader(), body,
      });
      if (typeof result === "object" && result !== null && "id" in result
        && path === "/api/v1/job-work/orders") setOrderId(result.id);
      setResultSummary(JSON.stringify(result));
      await refresh();
    } catch (error) {
      if (isUnauthorized(error)) onUnauthorized();
      else setError(messageOf(error));
    } finally {
      setBusy(false);
    }
  };

  const selectedOrder = orders.find((order) => order.id === orderId);
  return (
    <div className="workspace-grid">
      <section className="data-card">
        <header className="card-heading"><div><h2>Job Work Orders</h2><p className="muted">Supplier-held quantities are custody, never available warehouse stock.</p></div>
          <button className="quiet-button" onClick={() => void refresh()}>Refresh</button></header>
        {orders.length === 0 ? <p className="empty-state">No Job Work Orders recorded.</p> : (
          <div className="table-scroll"><table><thead><tr><th>Order</th><th>Subcontractor</th><th>Processed</th><th>Rejected</th><th>Custody outstanding</th><th>Status</th></tr></thead>
            <tbody>{orders.map((order) => <tr key={order.id} onClick={() => setOrderId(order.id)}>
              <td>{order.orderNumber}<small>{order.id}</small></td><td>{order.subcontractorId}</td>
              <td>{order.processedQuantity}</td><td>{order.rejectedQuantity}</td>
              <td>{order.outstandingHeldMaterial}</td><td>{order.status}</td>
            </tr>)}</tbody></table></div>
        )}
        {selectedOrder && <div className="alert">
          Selected {selectedOrder.orderNumber}. Material issue IDs: {JSON.stringify(selectedOrder.materials.map((row) => row.issueId))}
          {" "}Receipt IDs: {JSON.stringify(selectedOrder.receipts.map((receipt) => receipt.id))}
        </div>}
        {resultSummary && <pre className="muted">{resultSummary}</pre>}
      </section>
      <section className="data-card">
        <header className="card-heading"><div><h2>Supplier custody and receipts</h2><p className="muted">Register a supplier, create a Job Work Order, then dispatch, reconcile and record partial receipts.</p></div></header>
        <CommandForm label="Register subcontractor (supplier ID)" value={profileBody} onChange={setProfileBody}
          submitLabel="Register" busy={busy} onSubmit={() => void post("/api/v1/job-work/subcontractors", profileBody)} rows={2} />
        <CommandForm label="Create Job Work Order" value={orderBody} onChange={setOrderBody}
          submitLabel="Create order" busy={busy} onSubmit={() => void post("/api/v1/job-work/orders", orderBody)} rows={10} />
        <div className="toolbar"><label>Selected Job Work Order ID<input value={orderId} onChange={(event) => setOrderId(event.target.value)} /></label></div>
        <CommandForm label="Dispatch material (JSON array)" value={dispatchBody} onChange={setDispatchBody}
          submitLabel="Dispatch" busy={busy} onSubmit={() => void post(`/api/v1/job-work/orders/${orderId}/dispatch`, dispatchBody)} rows={3} />
        <CommandForm label="Record partial receipt" value={receiptBody} onChange={setReceiptBody}
          submitLabel="Receive" busy={busy} onSubmit={() => void post(`/api/v1/job-work/orders/${orderId}/receipts`, receiptBody)} rows={3} />
        <CommandForm label="Reconcile material return" value={reconcileBody} onChange={setReconcileBody}
          submitLabel="Reconcile" busy={busy} onSubmit={() => void post(`/api/v1/job-work/orders/${orderId}/reconcile`, reconcileBody)} rows={3} />
      </section>
    </div>
  );
}

function MaintenanceWorkspace({ setError, onUnauthorized }: Omit<Props, "view">) {
  const [requests, setRequests] = useState<MaintenanceRequest[]>([]);
  const [downtime, setDowntime] = useState<MachineDowntime[]>([]);
  const [requestBody, setRequestBody] = useState(JSON.stringify({
    machineId: "",
    requestNumber: `MR-${Date.now()}`,
    description: "Describe the maintenance issue",
    priority: "normal",
    assignedTeam: "Maintenance",
    workOrderId: null,
  }, null, 2));
  const [scheduleBody, setScheduleBody] = useState(JSON.stringify({
    machineId: "", name: "Preventive inspection", intervalDays: 30,
    nextDueDate: new Date().toISOString().slice(0, 10),
  }, null, 2));
  const [downtimeBody, setDowntimeBody] = useState(JSON.stringify({
    machineId: "", workOrderId: null, reason: "Breakdown",
    startedAt: new Date().toISOString(),
  }, null, 2));
  const [cardId, setCardId] = useState("");
  const [spareBody, setSpareBody] = useState('{"itemId":"","warehouseId":"","quantity":1}');
  const [completionSummary, setCompletionSummary] = useState("");
  const [busy, setBusy] = useState(false);
  const [resultSummary, setResultSummary] = useState("");

  const refresh = useCallback(async () => {
    try {
      const [maintenanceRequests, machineDowntime] = await Promise.all([
        api<MaintenanceRequest[]>("/api/v1/maintenance/requests"),
        api<MachineDowntime[]>("/api/v1/maintenance/downtime"),
      ]);
      setRequests(maintenanceRequests);
      setDowntime(machineDowntime);
    } catch (error) {
      if (isUnauthorized(error)) onUnauthorized();
      else setError(messageOf(error));
    }
  }, [onUnauthorized, setError]);

  useEffect(() => { void refresh(); }, [refresh]);

  const post = async (path: string, body: string) => {
    setBusy(true);
    setError("");
    try {
      const result = await api<MaintenanceRequest>(path, {
        method: "POST", headers: requestHeader(), body,
      });
      if (path === "/api/v1/maintenance/requests" && result.jobCardId) setCardId(result.jobCardId);
      setResultSummary(JSON.stringify(result));
      await refresh();
    } catch (error) {
      if (isUnauthorized(error)) onUnauthorized();
      else setError(messageOf(error));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="workspace-grid">
      <section className="data-card">
        <header className="card-heading"><div><h2>Maintenance requests</h2><p className="muted">Machine maintenance and production-linked downtime history.</p></div>
          <button className="quiet-button" onClick={() => void refresh()}>Refresh</button></header>
        <div className="table-scroll"><table><thead><tr><th>Request</th><th>Machine</th><th>Priority</th><th>Status</th><th>Job Card</th></tr></thead>
          <tbody>{requests.map((request) => <tr key={request.id} onClick={() => setCardId(request.jobCardId)}>
            <td>{request.requestNumber}<small>{request.description}</small></td><td>{request.machineId}</td>
            <td>{request.priority}</td><td>{request.status}</td><td>{request.jobCardId}</td>
          </tr>)}</tbody></table></div>
        <header className="card-heading"><div><h2>Machine downtime</h2></div></header>
        <div className="table-scroll"><table><thead><tr><th>Machine</th><th>Reason</th><th>Started</th><th>Ended</th><th /></tr></thead>
          <tbody>{downtime.map((record) => <tr key={record.id}><td>{record.machineId}</td><td>{record.reason}</td>
            <td>{new Date(record.startedAt).toLocaleString()}</td><td>{record.endedAt ? new Date(record.endedAt).toLocaleString() : "Open"}</td>
            <td>{!record.endedAt && <button className="quiet-button" disabled={busy}
              onClick={() => void post(`/api/v1/maintenance/downtime/${record.id}/end`, "{}")}>End</button>}</td>
          </tr>)}</tbody></table></div>
      </section>
      <section className="data-card">
        <header className="card-heading"><div><h2>Maintenance workflow</h2><p className="muted">Create a request, issue spares, and complete its generated maintenance Job Card.</p></div></header>
        <CommandForm label="New maintenance request" value={requestBody} onChange={setRequestBody}
          submitLabel="Create request" busy={busy} onSubmit={() => void post("/api/v1/maintenance/requests", requestBody)} rows={8} />
        <div className="toolbar"><label>Maintenance Job Card ID<input value={cardId} onChange={(event) => setCardId(event.target.value)} /></label></div>
        <CommandForm label="Issue spare parts" value={spareBody} onChange={setSpareBody}
          submitLabel="Issue spares" busy={busy} onSubmit={(body) => void post(`/api/v1/maintenance/job-cards/${cardId}/spares`, body)} rows={3} />
        <form className="toolbar" onSubmit={(event) => {
          event.preventDefault();
          void post(`/api/v1/maintenance/job-cards/${cardId}/complete`, JSON.stringify({ summary: completionSummary }));
        }}>
          <label>Completion summary<input required value={completionSummary} onChange={(event) => setCompletionSummary(event.target.value)} /></label>
          <button className="primary-button" disabled={busy || !cardId}>Complete maintenance</button>
        </form>
        <CommandForm label="Preventive maintenance schedule" value={scheduleBody} onChange={setScheduleBody}
          submitLabel="Add schedule" busy={busy} onSubmit={() => void post("/api/v1/maintenance/preventive-schedules", scheduleBody)} rows={4} />
        <CommandForm label="Report machine downtime" value={downtimeBody} onChange={setDowntimeBody}
          submitLabel="Start downtime" busy={busy} onSubmit={() => void post("/api/v1/maintenance/downtime", downtimeBody)} rows={5} />
        {resultSummary && <pre className="muted">{resultSummary}</pre>}
      </section>
    </div>
  );
}

function CommandForm({
  label, value, onChange, submitLabel, busy, onSubmit, rows,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  submitLabel: string;
  busy: boolean;
  onSubmit: (body: string) => void;
  rows: number;
}) {
  return <form className="toolbar" onSubmit={(event) => {
    event.preventDefault();
    onSubmit(value);
  }}>
    <label>{label}<textarea rows={rows} value={value}
      onChange={(event) => onChange(event.target.value)} /></label>
    <button className="primary-button" disabled={busy}>{submitLabel}</button>
  </form>;
}
