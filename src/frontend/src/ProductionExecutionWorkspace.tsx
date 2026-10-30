import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, ApiError, type Page } from "./api";

type Props = {
  view: "production-planning" | "job-cards";
  setError: (message: string) => void;
  onUnauthorized: () => void;
};

interface Operation {
  id: string;
  sequence: number;
  code: string;
  name: string;
  plannedStartDate: string;
  plannedEndDate: string;
  plannedTargetQuantity: number;
  productionLine: string | null;
  machineId: string | null;
  assignedTeam: string | null;
  status: string;
}

interface Plan {
  workOrderId: string;
  workOrderNumber: string;
  priority: number;
  plannedStartDate: string;
  plannedEndDate: string;
  plannedQuantity: number;
  actualQuantity: number;
  acceptedGoodQuantity: number;
  outstandingQuantity: number;
  scheduleStatus: string;
  workOrderStatus: string;
  downtimeEventCount: number;
  downtimeHours: number;
  operations: Operation[];
}

interface JobCard {
  id: string;
  jobCardNumber: string;
  workOrderId: string;
  workOrderNumber: string;
  workOrderOperationId: string;
  operationSequence: number;
  operationCode: string;
  operationName: string;
  plannedQuantity: number;
  processedQuantity: number;
  outstandingQuantity: number;
  productionLine: string | null;
  machineId: string | null;
  assignedTeam: string | null;
  status: string;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

interface Confirmation {
  id: string;
  workOrderId: string;
  jobCardId: string;
  correctsConfirmationId: string | null;
  goodQuantity: number;
  rejectedQuantity: number;
  reworkQuantity: number;
  scrapQuantity: number;
  shift: string | null;
  productionLine: string | null;
  machineId: string | null;
  team: string | null;
  startedAt: string;
  completedAt: string;
  status: string;
  createdBySubject: string;
  approvedBySubject: string | null;
  correctionReason: string | null;
  rejectionReason: string | null;
}

interface OperationPlan {
  operationId: string;
  targetQuantity: string;
  productionLine: string;
  machineId: string;
  assignedTeam: string;
  plannedStartDate: string;
  plannedEndDate: string;
}

interface MachineOption {
  id: string;
  code: string;
  name: string;
}

const idempotencyKey = () => crypto.randomUUID();
const localDateTime = () => {
  const date = new Date(Date.now() - new Date().getTimezoneOffset() * 60_000);
  return date.toISOString().slice(0, 16);
};

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : "The request could not be completed.";
}

function handleError(error: unknown, setError: Props["setError"], onUnauthorized: Props["onUnauthorized"]) {
  if (error instanceof ApiError && error.status === 401) onUnauthorized();
  else setError(messageOf(error));
}

export default function ProductionExecutionWorkspace(props: Props) {
  return props.view === "production-planning"
    ? <ProductionPlanning {...props} />
    : <JobCardsWorkspace {...props} />;
}

function ProductionPlanning({ setError, onUnauthorized }: Props) {
  const [plans, setPlans] = useState<Plan[]>([]);
  const [editing, setEditing] = useState<Plan | null>(null);
  const [priority, setPriority] = useState("3");
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [operations, setOperations] = useState<OperationPlan[]>([]);
  const [machines, setMachines] = useState<MachineOption[]>([]);
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const [result, machinePage] = await Promise.all([
        api<Page<Plan>>("/api/v1/planning/production/orders?page=1&pageSize=100"),
        api<Page<MachineOption>>("/api/v1/master-data/machines?page=1&pageSize=100&active=true"),
      ]);
      setPlans(result.items);
      setMachines(machinePage.items);
    } catch (error) {
      handleError(error, setError, onUnauthorized);
    }
  }, [onUnauthorized, setError]);

  useEffect(() => { void refresh(); }, [refresh]);

  const edit = (plan: Plan) => {
    setEditing(plan);
    setPriority(String(plan.priority));
    setStartDate(plan.plannedStartDate);
    setEndDate(plan.plannedEndDate);
    setOperations(plan.operations.map(operation => ({
      operationId: operation.id,
      targetQuantity: String(operation.plannedTargetQuantity),
      productionLine: operation.productionLine ?? "",
      machineId: operation.machineId ?? "",
      assignedTeam: operation.assignedTeam ?? "",
      plannedStartDate: operation.plannedStartDate,
      plannedEndDate: operation.plannedEndDate,
    })));
  };

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!editing) return;
    setBusy(true);
    setError("");
    try {
      await api(`/api/v1/planning/production/orders/${editing.workOrderId}`, {
        method: "PUT",
        headers: { "Idempotency-Key": idempotencyKey() },
        body: JSON.stringify({
          priority: Number(priority),
          plannedStartDate: startDate,
          plannedEndDate: endDate,
          operations: operations.map(operation => ({
            ...operation,
            targetQuantity: Number(operation.targetQuantity),
            machineId: operation.machineId || null,
            productionLine: operation.productionLine || null,
            assignedTeam: operation.assignedTeam || null,
          })),
        }),
      });
      setEditing(null);
      await refresh();
    } catch (error) {
      handleError(error, setError, onUnauthorized);
    } finally {
      setBusy(false);
    }
  };

  return <>
    <section className="data-card">
      <header className="card-heading">
        <div><h2>Production planning</h2>
          <p className="muted">Plan approved Work Orders before release. Linked machine downtime contributes to schedule risk.</p>
        </div>
        <button className="secondary-button" onClick={() => void refresh()}>Refresh</button>
      </header>
      <div className="table-scroll"><table><thead><tr>
        <th>Work Order</th><th>Priority</th><th>Schedule</th><th>Planned</th>
        <th>Actual</th><th>Accepted good</th><th>Outstanding</th><th>Risk</th><th>Action</th>
      </tr></thead><tbody>
        {plans.map(plan => <tr key={plan.workOrderId}>
          <td><strong>{plan.workOrderNumber}</strong><small>{plan.workOrderStatus.replaceAll("_", " ")}</small></td>
          <td>{plan.priority}</td><td>{plan.plannedStartDate} – {plan.plannedEndDate}</td>
          <td>{plan.plannedQuantity}</td><td>{plan.actualQuantity}</td>
          <td>{plan.acceptedGoodQuantity}</td><td>{plan.outstandingQuantity}</td>
          <td><span className="status-pill">{plan.scheduleStatus.replaceAll("_", " ")}</span>
            {plan.downtimeEventCount > 0 && <small>{plan.downtimeEventCount} event(s) · {plan.downtimeHours.toFixed(1)} h downtime</small>}</td>
          <td>{plan.workOrderStatus === "approved" && <button className="text-button"
            onClick={() => edit(plan)}>Edit plan</button>}</td>
        </tr>)}
      </tbody></table></div>
      {plans.length === 0 && <p className="muted">No planned Work Orders.</p>}
    </section>
    {editing && <div className="modal-backdrop"><form className="record-dialog" onSubmit={save}>
      <header className="dialog-heading"><h2>Plan {editing.workOrderNumber}</h2></header>
      <div className="form-grid">
        <label>Priority (1 highest)<input required type="number" min="1" max="5"
          value={priority} onChange={event => setPriority(event.target.value)} /></label>
        <label>Planned start<input required type="date" value={startDate}
          onChange={event => setStartDate(event.target.value)} /></label>
        <label>Planned end<input required type="date" value={endDate}
          onChange={event => setEndDate(event.target.value)} /></label>
      </div>
      {operations.map((operation, index) => {
        const source = editing.operations.find(value => value.id === operation.operationId);
        return <fieldset key={operation.operationId}>
          <legend>{source?.sequence}. {source?.code} · {source?.name}</legend>
          <div className="form-grid">
            <label>Target quantity<input required type="number" min="0.000001" step="0.000001"
              value={operation.targetQuantity} onChange={event => setOperations(current =>
                current.map((value, row) => row === index
                  ? { ...value, targetQuantity: event.target.value } : value))} /></label>
            <label>Production line<input maxLength={100} value={operation.productionLine}
              onChange={event => setOperations(current => current.map((value, row) => row === index
                ? { ...value, productionLine: event.target.value } : value))} /></label>
            <label>Machine<select value={operation.machineId}
              onChange={event => setOperations(current => current.map((value, row) => row === index
                ? { ...value, machineId: event.target.value } : value))}>
              <option value="">Unassigned</option>
              {machines.map(machine => <option key={machine.id} value={machine.id}>
                {machine.code} · {machine.name}
              </option>)}
            </select></label>
            <label>Assigned team<input maxLength={100} value={operation.assignedTeam}
              onChange={event => setOperations(current => current.map((value, row) => row === index
                ? { ...value, assignedTeam: event.target.value } : value))} /></label>
            <label>Operation start<input required type="date" value={operation.plannedStartDate}
              onChange={event => setOperations(current => current.map((value, row) => row === index
                ? { ...value, plannedStartDate: event.target.value } : value))} /></label>
            <label>Operation end<input required type="date" value={operation.plannedEndDate}
              onChange={event => setOperations(current => current.map((value, row) => row === index
                ? { ...value, plannedEndDate: event.target.value } : value))} /></label>
          </div>
        </fieldset>;
      })}
      <div className="toolbar">
        <button className="primary-button" disabled={busy}>{busy ? "Saving…" : "Save plan"}</button>
        <button className="secondary-button" type="button" onClick={() => setEditing(null)}>Close</button>
      </div>
    </form></div>}
  </>;
}

function JobCardsWorkspace({ setError, onUnauthorized }: Props) {
  const [cards, setCards] = useState<JobCard[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [confirmations, setConfirmations] = useState<Confirmation[]>([]);
  const [busy, setBusy] = useState(false);
  const [good, setGood] = useState("0");
  const [rejected, setRejected] = useState("0");
  const [rework, setRework] = useState("0");
  const [scrap, setScrap] = useState("0");
  const [shift, setShift] = useState("");
  const [team, setTeam] = useState("");
  const [startedAt, setStartedAt] = useState(localDateTime);
  const [completedAt, setCompletedAt] = useState(localDateTime);
  const [correctionId, setCorrectionId] = useState("");
  const [correctionReason, setCorrectionReason] = useState("");
  const selected = cards.find(card => card.id === selectedId);

  const refresh = useCallback(async () => {
    try {
      const result = await api<Page<JobCard>>("/api/v1/production/job-cards?page=1&pageSize=100");
      setCards(result.items);
      if (!selectedId && result.items[0]) setSelectedId(result.items[0].id);
    } catch (error) {
      handleError(error, setError, onUnauthorized);
    }
  }, [onUnauthorized, selectedId, setError]);

  const refreshConfirmations = useCallback(async () => {
    if (!selectedId) {
      setConfirmations([]);
      return;
    }
    try {
      setConfirmations(await api<Confirmation[]>(
        `/api/v1/production/job-cards/${selectedId}/confirmations`,
      ));
    } catch (error) {
      handleError(error, setError, onUnauthorized);
    }
  }, [onUnauthorized, selectedId, setError]);

  useEffect(() => { void refresh(); }, [refresh]);
  useEffect(() => { void refreshConfirmations(); }, [refreshConfirmations]);

  const command = async (path: string, method = "POST", body: unknown = {}) => {
    setBusy(true);
    setError("");
    try {
      await api(path, {
        method,
        headers: { "Idempotency-Key": idempotencyKey() },
        body: JSON.stringify(body),
      });
      await Promise.all([refresh(), refreshConfirmations()]);
    } catch (error) {
      handleError(error, setError, onUnauthorized);
    } finally {
      setBusy(false);
    }
  };

  const submitConfirmation = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selected) return;
    await command(`/api/v1/production/job-cards/${selected.id}/confirmations`, "POST", {
      goodQuantity: Number(good),
      rejectedQuantity: Number(rejected),
      reworkQuantity: Number(rework),
      scrapQuantity: Number(scrap),
      shift: shift || null,
      team: team || selected.assignedTeam,
      machineId: selected.machineId,
      productionLine: selected.productionLine,
      startedAt: new Date(startedAt).toISOString(),
      completedAt: new Date(completedAt).toISOString(),
    });
    setGood("0");
    setRejected("0");
    setRework("0");
    setScrap("0");
  };

  const correct = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const original = confirmations.find(value => value.id === correctionId);
    if (!original) return;
    await command(`/api/v1/production/confirmations/${correctionId}/corrections`, "POST", {
      goodQuantity: Number(good),
      rejectedQuantity: Number(rejected),
      reworkQuantity: Number(rework),
      scrapQuantity: Number(scrap),
      reason: correctionReason,
    });
    setCorrectionId("");
    setCorrectionReason("");
  };

  return <>
    <section className="data-card">
      <header className="card-heading">
        <div><h2>Job Cards</h2>
          <p className="muted">Operation-level progress and controlled production confirmations.</p></div>
        <button className="secondary-button" onClick={() => void refresh()}>Refresh</button>
      </header>
      <div className="table-scroll"><table><thead><tr>
        <th>Job Card</th><th>Work Order / operation</th><th>Line · team</th>
        <th>Target</th><th>Processed</th><th>Outstanding</th><th>Status</th><th>Action</th>
      </tr></thead><tbody>
        {cards.map(card => <tr key={card.id} className={selectedId === card.id ? "selected-row" : ""}>
          <td><button className="text-button" onClick={() => setSelectedId(card.id)}>
            {card.jobCardNumber}</button></td>
          <td>{card.workOrderNumber}<small>{card.operationSequence}. {card.operationName}</small></td>
          <td>{card.productionLine ?? "—"} · {card.assignedTeam ?? "—"}</td>
          <td>{card.plannedQuantity}</td><td>{card.processedQuantity}</td><td>{card.outstandingQuantity}</td>
          <td><span className="status-pill">{card.status.replaceAll("_", " ")}</span></td>
          <td className="row-actions">
            {card.status === "ready" || card.status === "partially_completed"
              ? <button className="text-button" disabled={busy}
                onClick={() => void command(`/api/v1/production/job-cards/${card.id}/start`)}>Start</button>
              : null}
            {card.status === "in_progress" && <>
              <button className="text-button" disabled={busy}
                onClick={() => void command(`/api/v1/production/job-cards/${card.id}/hold`,
                  "POST", { reason: "Paused by production supervisor." })}>Hold</button>
            </>}
            {card.status === "on_hold" && <button className="text-button" disabled={busy}
              onClick={() => void command(`/api/v1/production/job-cards/${card.id}/resume`)}>Resume</button>}
          </td>
        </tr>)}
      </tbody></table></div>
      {cards.length === 0 && <p className="muted">Released Work Orders generate Job Cards automatically.</p>}
    </section>

    {selected && <section className="data-card">
      <header className="card-heading"><div><h2>Confirm production · {selected.jobCardNumber}</h2>
        <p className="muted">Good quantity enters available inventory only after a different user approves it.</p></div></header>
      {selected.status === "in_progress" && <form className="toolbar" onSubmit={submitConfirmation}>
        <label>Good<input required type="number" min="0" step="0.000001" value={good}
          onChange={event => setGood(event.target.value)} /></label>
        <label>Rejected<input required type="number" min="0" step="0.000001" value={rejected}
          onChange={event => setRejected(event.target.value)} /></label>
        <label>Rework<input required type="number" min="0" step="0.000001" value={rework}
          onChange={event => setRework(event.target.value)} /></label>
        <label>Scrap<input required type="number" min="0" step="0.000001" value={scrap}
          onChange={event => setScrap(event.target.value)} /></label>
        <label>Shift<input maxLength={80} value={shift}
          onChange={event => setShift(event.target.value)} /></label>
        <label>Team<input maxLength={100} value={team}
          onChange={event => setTeam(event.target.value)} /></label>
        <label>Started<input required type="datetime-local" value={startedAt}
          onChange={event => setStartedAt(event.target.value)} /></label>
        <label>Completed<input required type="datetime-local" value={completedAt}
          onChange={event => setCompletedAt(event.target.value)} /></label>
        <button className="primary-button" disabled={busy}>Submit for approval</button>
      </form>}
      <div className="table-scroll"><table><thead><tr>
        <th>Confirmation</th><th>Good</th><th>Rejected</th><th>Rework</th><th>Scrap</th>
        <th>Shift / team</th><th>State</th><th>Approval / correction</th>
      </tr></thead><tbody>{confirmations.map(confirmation => <tr key={confirmation.id}>
        <td>{confirmation.id.slice(0, 8)}<small>{new Date(confirmation.completedAt).toLocaleString()}</small></td>
        <td>{confirmation.goodQuantity}</td><td>{confirmation.rejectedQuantity}</td>
        <td>{confirmation.reworkQuantity}</td><td>{confirmation.scrapQuantity}</td>
        <td>{confirmation.shift ?? "—"} / {confirmation.team ?? "—"}</td>
        <td><span className="status-pill">{confirmation.status.replaceAll("_", " ")}</span></td>
        <td className="row-actions">
          {confirmation.status === "pending_approval" && <>
            <button className="text-button" disabled={busy}
              onClick={() => void command(`/api/v1/production/confirmations/${confirmation.id}/approve`)}>
              Approve
            </button>
            <button className="text-button danger-action" disabled={busy}
              onClick={() => void command(`/api/v1/production/confirmations/${confirmation.id}/reject`,
                "POST", { reason: "Rejected by supervisor." })}>Reject</button>
          </>}
          {confirmation.status === "approved" && <button className="text-button" disabled={busy}
            onClick={() => {
              setCorrectionId(confirmation.id);
              setGood(String(confirmation.goodQuantity));
              setRejected(String(confirmation.rejectedQuantity));
              setRework(String(confirmation.reworkQuantity));
              setScrap(String(confirmation.scrapQuantity));
            }}>Correct</button>}
        </td>
      </tr>)}</tbody></table></div>
      {correctionId && <form className="toolbar" onSubmit={correct}>
        <strong>Correction replaces the selected approved record.</strong>
        <label>Correction reason<input required maxLength={500} value={correctionReason}
          onChange={event => setCorrectionReason(event.target.value)} /></label>
        <button className="primary-button" disabled={busy}>Submit correction for approval</button>
        <button className="secondary-button" type="button" onClick={() => setCorrectionId("")}>Cancel</button>
      </form>}
      {confirmations.length === 0 && <p className="muted">No confirmations submitted for this Job Card.</p>}
    </section>}
  </>;
}
