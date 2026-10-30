import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import CommerceWorkspace from "./CommerceWorkspace";
import {
  api,
  ApiError,
  type Page,
  type RecordData,
  type SelectOption,
} from "./api";
import {
  resources,
  type FieldDefinition,
  type ResourceDefinition,
} from "./resources";

interface Session {
  userId: string;
  displayName: string;
  email: string;
  companyId: string;
}

interface BomComponent {
  id?: string;
  lineNumber: number;
  itemId: string;
  itemCode?: string;
  itemName?: string;
  unitOfMeasureId: string;
  unitOfMeasureCode?: string;
  quantity: number;
  wastePercent: number;
  alternativeGroupCode?: string | null;
  isOptional: boolean;
}

interface BomVersion {
  id: string;
  bomId: string;
  versionNumber: number;
  status: string;
  effectiveFrom: string;
  effectiveTo?: string | null;
  approvedBySubject?: string | null;
  approvedAt?: string | null;
  components: BomComponent[];
}

interface FormState {
  record: RecordData | null;
  fields: FieldDefinition[];
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : "The request could not be completed.";
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401;
}

async function loadOptions(route: string): Promise<SelectOption[]> {
  const options: SelectOption[] = [];
  let page = 1;
  let totalCount = 0;
  do {
    const result = await api<Page<RecordData>>(
      `/api/v1/master-data/${route}?page=${page}&pageSize=100&active=true`,
    );
    totalCount = result.totalCount;
    options.push(
      ...result.items.map((record) => ({
        id: record.id,
        label: [record.code, record.name].filter(Boolean).join(" — ") || record.id,
      })),
    );
    page += 1;
  } while (options.length < totalCount && page <= 101);

  if (options.length < totalCount) {
    throw new Error(`The ${route} option list is too large to load in one form.`);
  }
  return options;
}

export default function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [checkingSession, setCheckingSession] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    api<Session>("/api/v1/auth/me")
      .then((current) => {
        if (active) setSession(current);
      })
      .catch((requestError: unknown) => {
        if (!isUnauthorized(requestError) && active) {
          setError(messageOf(requestError));
        }
      })
      .finally(() => {
        if (active) setCheckingSession(false);
      });
    return () => {
      active = false;
    };
  }, []);

  const handleLogin = async (email: string, password: string, companyId: string) => {
    setError("");
    try {
      const current = await api<Session>("/api/v1/auth/login", {
        method: "POST",
        body: JSON.stringify({ email, password, companyId }),
      });
      setSession(current);
    } catch (requestError) {
      setError(messageOf(requestError));
    }
  };

  const handleLogout = async () => {
    setError("");
    try {
      await api<void>("/api/v1/auth/logout", {
        method: "POST",
        body: JSON.stringify({}),
      });
      setSession(null);
    } catch (requestError) {
      if (isUnauthorized(requestError)) setSession(null);
      else setError(messageOf(requestError));
    }
  };

  if (checkingSession) {
    return <main className="loading-screen">Connecting to the local ERP API…</main>;
  }

  if (!session) {
    return (
      <LoginScreen
        error={error}
        onLogin={handleLogin}
      />
    );
  }

  return (
    <ApplicationShell
      session={session}
      error={error}
      setError={setError}
      onLogout={handleLogout}
      onUnauthorized={() => setSession(null)}
    />
  );
}

function LoginScreen({
  error,
  onLogin,
}: {
  error: string;
  onLogin: (email: string, password: string, companyId: string) => Promise<void>;
}) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [companyId, setCompanyId] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    try {
      await onLogin(email, password, companyId);
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="login-layout">
      <section className="login-brand">
        <div className="brand-mark">M</div>
        <p className="eyebrow">LOCAL MANUFACTURING PLATFORM</p>
        <h1>One connected view of your factory.</h1>
        <p className="muted">
          Manage suppliers, materials, styles, warehouse locations and production
          references from your company database.
        </p>
        <div className="local-indicator"><span /> Local API connection</div>
      </section>
      <form className="login-card" onSubmit={submit}>
        <p className="eyebrow">SECURE WORKSPACE</p>
        <h2>Sign in</h2>
        <p className="muted">Use your ERP account and company identifier.</p>
        {error && <div className="alert" role="alert">{error}</div>}
        <label>
          Company ID
          <input
            required
            autoComplete="organization"
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
            placeholder="Company identifier"
          />
        </label>
        <label>
          Email
          <input
            required
            type="email"
            autoComplete="username"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            placeholder="name@company.com"
          />
        </label>
        <label>
          Password
          <input
            required
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            placeholder="Your password"
          />
        </label>
        <button className="primary-button full-width" type="submit" disabled={busy}>
          {busy ? "Signing in…" : "Sign in"}
        </button>
        <p className="form-footnote">Session cookies are secured by the local API.</p>
      </form>
    </main>
  );
}

function ApplicationShell({
  session,
  error,
  setError,
  onLogout,
  onUnauthorized,
}: {
  session: Session;
  error: string;
  setError: (message: string) => void;
  onLogout: () => Promise<void>;
  onUnauthorized: () => void;
}) {
  const [selected, setSelected] = useState(resources[0].key);
  const current = resources.find((resource) => resource.key === selected);
  const commerce = selected === "sales-orders"
    || selected === "purchase-requisitions"
    || selected === "purchase-orders"
    ? selected
    : null;
  const currentLabel = current?.label
    ?? (commerce === "sales-orders" ? "Sales orders"
      : commerce === "purchase-requisitions" ? "Purchase requisitions"
        : commerce === "purchase-orders" ? "Purchase orders" : "Workspace");

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark small">M</div>
          <div><strong>Manufacturing ERP</strong><span>Master data workspace</span></div>
        </div>
        <p className="nav-caption">MASTER DATA</p>
        <nav aria-label="Master data">
          {resources.map((resource) => (
            <button
              className={`nav-item ${selected === resource.key ? "selected" : ""}`}
              key={resource.key}
              onClick={() => {
                setSelected(resource.key);
                setError("");
              }}
            >
              <span className="nav-dot" />
              {resource.label}
              {resource.bomVersions && <span className="nav-tag">BOM</span>}
            </button>
          ))}
        </nav>
        <p className="nav-caption">ORDER MANAGEMENT</p>
        <nav aria-label="Order management">
          {([
            ["sales-orders", "Sales orders"],
            ["purchase-requisitions", "Purchase requisitions"],
            ["purchase-orders", "Purchase orders"],
          ] as const).map(([key, label]) => (
            <button
              className={`nav-item ${selected === key ? "selected" : ""}`}
              key={key}
              onClick={() => {
                setSelected(key);
                setError("");
              }}
            >
              <span className="nav-dot" />
              {label}
            </button>
          ))}
        </nav>
        <div className="sidebar-footer">
          <span className="connection-dot" /> Connected to local API
        </div>
      </aside>
      <main className="main-content">
        <header className="topbar">
          <div className="breadcrumb">Workspace <span>/</span> {currentLabel}</div>
          <div className="profile">
            <div className="avatar">{session.displayName.slice(0, 1).toUpperCase()}</div>
            <div className="profile-copy">
              <strong>{session.displayName}</strong>
              <span>{session.email}</span>
            </div>
            <button className="quiet-button" onClick={() => void onLogout()}>Sign out</button>
          </div>
        </header>
        <section className="workspace">
          <div className="page-heading">
            <div>
              <p className="eyebrow">{commerce ? "BUSINESS TRANSACTIONS" : "CONFIGURATION / MASTER DATA"}</p>
              <h1>{currentLabel}</h1>
              <p className="muted">{commerce
                ? "Versioned, company-scoped transactions with approval and audit history."
                : "Company-scoped records managed through the ERP API."}</p>
            </div>
            <span className="company-chip">Company {session.companyId.slice(0, 8)}</span>
          </div>
          {error && <div className="alert workspace-alert" role="alert">{error}</div>}
          {commerce
            ? <CommerceWorkspace
              key={commerce}
              kind={commerce}
              setError={setError}
              onUnauthorized={onUnauthorized}
            />
            : current && <ResourceWorkspace
              key={current.key}
              resource={current}
              setError={setError}
              onUnauthorized={onUnauthorized}
            />}
        </section>
      </main>
    </div>
  );
}

function ResourceWorkspace({
  resource,
  setError,
  onUnauthorized,
}: {
  resource: ResourceDefinition;
  setError: (message: string) => void;
  onUnauthorized: () => void;
}) {
  const [page, setPage] = useState<Page<RecordData>>({
    items: [],
    page: 1,
    pageSize: 25,
    totalCount: 0,
  });
  const [pageNumber, setPageNumber] = useState(1);
  const [searchText, setSearchText] = useState("");
  const [search, setSearch] = useState("");
  const [activeFilter, setActiveFilter] = useState("");
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState<FormState | null>(null);
  const [formError, setFormError] = useState("");
  const [sortKey, setSortKey] = useState("");
  const [ascending, setAscending] = useState(true);
  const [selectedBom, setSelectedBom] = useState<RecordData | null>(null);

  const query = useMemo(() => {
    const params = new URLSearchParams({
      page: String(pageNumber),
      pageSize: String(page.pageSize),
    });
    if (search.trim()) params.set("search", search.trim());
    if (activeFilter) params.set("active", activeFilter);
    return params.toString();
  }, [activeFilter, page.pageSize, pageNumber, search]);

  const refresh = useCallback(async () => {
    setBusy(true);
    setError("");
    try {
      const result = await api<Page<RecordData>>(
        `/api/v1/master-data/${resource.route}?${query}`,
      );
      setPage(result);
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  }, [onUnauthorized, query, resource.route, setError]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const sortedItems = useMemo(() => {
    if (!sortKey) return page.items;
    return [...page.items].sort((left, right) => {
      const a = String(left[sortKey] ?? "").toLocaleLowerCase();
      const b = String(right[sortKey] ?? "").toLocaleLowerCase();
      return (a.localeCompare(b) || 0) * (ascending ? 1 : -1);
    });
  }, [ascending, page.items, sortKey]);

  const openForm = (record: RecordData | null) => {
    setFormError("");
    setForm({ record, fields: resource.fields });
  };

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setPageNumber(1);
    setSearch(searchText);
  };

  const saveForm = async (values: Record<string, string | boolean>) => {
    if (!form) return;
    setFormError("");
    const body: Record<string, unknown> = {};
    for (const field of form.fields) {
      if (field.name === "isActive") continue;
      const value = values[field.name];
      if (field.kind === "checkbox") {
        body[field.name] = Boolean(value);
      } else if (field.kind === "number") {
        if (value !== "") body[field.name] = Number(value);
      } else {
        body[field.name] = value === "" ? null : value;
      }
    }

    try {
      const editingId = form.record?.id;
      await api(
        editingId
          ? `/api/v1/master-data/${resource.route}/${editingId}`
          : `/api/v1/master-data/${resource.route}`,
        {
          method: editingId ? "PUT" : "POST",
          body: JSON.stringify(body),
        },
      );
      setForm(null);
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setFormError(messageOf(requestError));
    }
  };

  const setRecordActive = async (record: RecordData) => {
    try {
      await api(`/api/v1/master-data/${resource.route}/${record.id}/active`, {
        method: "PATCH",
        body: JSON.stringify({ isActive: !record.isActive }),
      });
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    }
  };

  return (
    <>
      <section className="data-card">
        <div className="table-toolbar">
          <form className="search-form" onSubmit={submitSearch}>
            <label className="search-field">
              <span className="sr-only">Search {resource.label}</span>
              <span className="search-icon" aria-hidden="true">⌕</span>
              <input
                value={searchText}
                onChange={(event) => setSearchText(event.target.value)}
                placeholder={`Search ${resource.label.toLowerCase()}…`}
              />
            </label>
            <select
              aria-label="Filter by status"
              value={activeFilter}
              onChange={(event) => {
                setActiveFilter(event.target.value);
                setPageNumber(1);
              }}
            >
              <option value="">All statuses</option>
              <option value="true">Active</option>
              <option value="false">Inactive</option>
            </select>
            <button className="secondary-button" type="submit">Search</button>
          </form>
          <button className="primary-button" onClick={() => void openForm(null)}>
            <span aria-hidden="true">＋</span> New record
          </button>
        </div>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                {resource.columns.map((column) => (
                  <th key={column.key}>
                    <button
                      className="sort-button"
                      onClick={() => {
                        if (sortKey === column.key) setAscending((value) => !value);
                        else {
                          setSortKey(column.key);
                          setAscending(true);
                        }
                      }}
                    >
                      {column.label}
                      {sortKey === column.key && <span>{ascending ? " ↑" : " ↓"}</span>}
                    </button>
                  </th>
                ))}
                <th><span className="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              {busy && (
                <tr><td className="empty-state" colSpan={resource.columns.length + 1}>Loading records…</td></tr>
              )}
              {!busy && sortedItems.length === 0 && (
                <tr>
                  <td className="empty-state" colSpan={resource.columns.length + 1}>
                    <strong>No records found</strong>
                    <span>Create a record to start maintaining this company master.</span>
                  </td>
                </tr>
              )}
              {!busy && sortedItems.map((record) => (
                <tr key={record.id}>
                  {resource.columns.map((column) => (
                    <td key={column.key}>
                      {column.key === "isActive"
                        ? <span className={`status ${record.isActive ? "active" : "inactive"}`}>
                          <span /> {record.isActive ? "Active" : "Inactive"}
                        </span>
                        : column.key === "isOutsourced"
                          ? (record.isOutsourced ? "Yes" : "No")
                          : displayValue(record[column.key])}
                    </td>
                  ))}
                  <td className="row-actions">
                    <button className="text-button" onClick={() => void openForm(record)}>Edit</button>
                    {resource.bomVersions && (
                      <button className="text-button" onClick={() => setSelectedBom(record)}>
                        Versions
                      </button>
                    )}
                    <button
                      className="text-button muted-action"
                      onClick={() => void setRecordActive(record)}
                    >
                      {record.isActive ? "Deactivate" : "Activate"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <footer className="pagination">
          <span>
            {page.totalCount === 0
              ? "No records"
              : `${(pageNumber - 1) * page.pageSize + 1}–${Math.min(
                pageNumber * page.pageSize,
                page.totalCount,
              )} of ${page.totalCount}`}
          </span>
          <div>
            <button
              className="secondary-button compact"
              disabled={pageNumber <= 1 || busy}
              onClick={() => setPageNumber((value) => Math.max(1, value - 1))}
            >
              Previous
            </button>
            <button
              className="secondary-button compact"
              disabled={pageNumber * page.pageSize >= page.totalCount || busy}
              onClick={() => setPageNumber((value) => value + 1)}
            >
              Next
            </button>
          </div>
        </footer>
      </section>
      {form && (
        <RecordDialog
          form={form}
          onClose={() => setForm(null)}
          onSave={saveForm}
          error={formError}
        />
      )}
      {selectedBom && (
        <BomVersionsDialog
          bom={selectedBom}
          onClose={() => setSelectedBom(null)}
          onUnauthorized={onUnauthorized}
        />
      )}
    </>
  );
}

function displayValue(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "boolean") return value ? "Yes" : "No";
  return String(value);
}

function RecordDialog({
  form,
  onClose,
  onSave,
  error,
}: {
  form: FormState;
  onClose: () => void;
  onSave: (values: Record<string, string | boolean>) => Promise<void>;
  error: string;
}) {
  const [values, setValues] = useState<Record<string, string | boolean>>(() => {
    const initial: Record<string, string | boolean> = {};
    for (const field of form.fields) {
      const value = form.record?.[field.name];
      if (field.kind === "checkbox") initial[field.name] = Boolean(value);
      else if (value !== null && value !== undefined) initial[field.name] = String(value);
      else if (field.name === "purchaseToStockFactor") initial[field.name] = "1";
      else initial[field.name] = "";
    }
    return initial;
  });
  const [saving, setSaving] = useState(false);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSaving(true);
    try {
      await onSave(values);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose();
    }}>
      <form className="record-dialog" onSubmit={submit} role="dialog" aria-modal="true">
        <header className="dialog-heading">
          <div>
            <p className="eyebrow">{form.record ? "UPDATE MASTER" : "NEW MASTER"}</p>
            <h2>{form.record ? "Edit record" : "Create record"}</h2>
          </div>
          <button className="icon-button" type="button" onClick={onClose} aria-label="Close dialog">×</button>
        </header>
        {error && <div className="alert" role="alert">{error}</div>}
        <div className="form-grid">
          {form.fields.map((field) => (
            <FieldInput
              key={field.name}
              field={field}
              value={values[field.name] ?? ""}
              onChange={(value) => setValues((current) => ({ ...current, [field.name]: value }))}
            />
          ))}
        </div>
        <footer className="dialog-actions">
          <button className="secondary-button" type="button" onClick={onClose}>Cancel</button>
          <button className="primary-button" type="submit" disabled={saving}>
            {saving ? "Saving…" : "Save record"}
          </button>
        </footer>
      </form>
    </div>
  );
}

function FieldInput({
  field,
  value,
  onChange,
}: {
  field: FieldDefinition;
  value: string | boolean;
  onChange: (value: string | boolean) => void;
}) {
  const [options, setOptions] = useState<SelectOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState("");

  useEffect(() => {
    if (!field.optionRoute) return;
    let active = true;
    setLoading(true);
    loadOptions(field.optionRoute)
      .then((result) => {
        if (active) setOptions(result);
      })
      .catch((error: unknown) => {
        if (active) setLoadError(messageOf(error));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [field.optionRoute]);

  if (field.kind === "checkbox") {
    return (
      <label className="checkbox-field">
        <input
          type="checkbox"
          checked={Boolean(value)}
          onChange={(event) => onChange(event.target.checked)}
        />
        <span>{field.label}</span>
      </label>
    );
  }

  return (
    <label className="form-field">
      <span>{field.label}{field.required && <b> *</b>}</span>
      {field.kind === "select" ? (
        <select
          required={field.required}
          value={String(value)}
          onChange={(event) => onChange(event.target.value)}
          disabled={loading}
        >
          <option value="">{loading ? "Loading…" : "Select an option"}</option>
          {field.options?.map((option) => (
            <option value={option.value} key={option.value}>{option.label}</option>
          ))}
          {options.map((option) => (
            <option value={option.id} key={option.id}>{option.label}</option>
          ))}
        </select>
      ) : (
        <input
          type={field.kind === "number" ? "number" : "text"}
          required={field.required}
          value={String(value)}
          min={field.min}
          max={field.max}
          step={field.step}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
      {field.help && <small>{field.help}</small>}
      {loadError && <small className="field-error">{loadError}</small>}
    </label>
  );
}

function BomVersionsDialog({
  bom,
  onClose,
  onUnauthorized,
}: {
  bom: RecordData;
  onClose: () => void;
  onUnauthorized: () => void;
}) {
  const [versions, setVersions] = useState<BomVersion[]>([]);
  const [items, setItems] = useState<SelectOption[]>([]);
  const [units, setUnits] = useState<SelectOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [effectiveFrom, setEffectiveFrom] = useState(new Date().toISOString().slice(0, 10));

  const refresh = useCallback(async () => {
    const [page, itemOptions, unitOptions] = await Promise.all([
      api<Page<BomVersion>>(`/api/v1/master-data/boms/${bom.id}/versions?page=1&pageSize=100`),
      loadOptions("items"),
      loadOptions("units"),
    ]);
    setVersions(page.items);
    setItems(itemOptions);
    setUnits(unitOptions);
  }, [bom.id]);

  useEffect(() => {
    let active = true;
    setLoading(true);
    refresh()
      .catch((requestError: unknown) => {
        if (!active) return;
        if (isUnauthorized(requestError)) onUnauthorized();
        else setError(messageOf(requestError));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [onUnauthorized, refresh]);

  const createVersion = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      await api(`/api/v1/master-data/boms/${bom.id}/versions`, {
        method: "POST",
        body: JSON.stringify({ effectiveFrom }),
      });
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  };

  const saveComponents = async (version: BomVersion, components: BomComponent[]) => {
    setBusy(true);
    setError("");
    try {
      await api(`/api/v1/master-data/bom-versions/${version.id}/components`, {
        method: "PUT",
        body: JSON.stringify(components.map((component, index) => ({
          itemId: component.itemId,
          unitOfMeasureId: component.unitOfMeasureId,
          lineNumber: index + 1,
          quantity: Number(component.quantity),
          wastePercent: Number(component.wastePercent),
          alternativeGroupCode: component.alternativeGroupCode || null,
          isOptional: Boolean(component.isOptional),
        }))),
      });
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  };

  const approveVersion = async (version: BomVersion) => {
    setBusy(true);
    setError("");
    try {
      await api(`/api/v1/master-data/bom-versions/${version.id}/approve`, {
        method: "POST",
        body: JSON.stringify({}),
      });
      await refresh();
    } catch (requestError) {
      if (isUnauthorized(requestError)) onUnauthorized();
      else setError(messageOf(requestError));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="modal-backdrop" role="presentation">
      <section className="bom-dialog" role="dialog" aria-modal="true" aria-labelledby="bom-title">
        <header className="dialog-heading">
          <div>
            <p className="eyebrow">PRODUCT ENGINEERING</p>
            <h2 id="bom-title">{bom.code} · BOM versions</h2>
          </div>
          <button className="icon-button" onClick={onClose} aria-label="Close versions">×</button>
        </header>
        {error && <div className="alert" role="alert">{error}</div>}
        <form className="new-version-form" onSubmit={(event) => void createVersion(event)}>
          <label>
            Effective from
            <input
              type="date"
              required
              value={effectiveFrom}
              onChange={(event) => setEffectiveFrom(event.target.value)}
            />
          </label>
          <button className="primary-button" type="submit" disabled={busy}>Create draft version</button>
        </form>
        {loading ? <p className="muted">Loading version history…</p> : (
          versions.length === 0
            ? <div className="empty-state block">No BOM versions have been created.</div>
            : <div className="version-list">
              {versions.map((version) => (
                <BomVersionCard
                  key={version.id}
                  version={version}
                  items={items}
                  units={units}
                  busy={busy}
                  onSave={(components) => void saveComponents(version, components)}
                  onApprove={() => void approveVersion(version)}
                />
              ))}
            </div>
        )}
      </section>
    </div>
  );
}

function BomVersionCard({
  version,
  items,
  units,
  busy,
  onSave,
  onApprove,
}: {
  version: BomVersion;
  items: SelectOption[];
  units: SelectOption[];
  busy: boolean;
  onSave: (components: BomComponent[]) => void;
  onApprove: () => void;
}) {
  const [components, setComponents] = useState<BomComponent[]>(version.components);

  useEffect(() => {
    setComponents(version.components);
  }, [version.components]);

  const update = (index: number, patch: Partial<BomComponent>) => {
    setComponents((current) => current.map((component, position) =>
      position === index ? { ...component, ...patch } : component));
  };

  const add = () => {
    setComponents((current) => [...current, {
      lineNumber: current.length + 1,
      itemId: "",
      unitOfMeasureId: "",
      quantity: 1,
      wastePercent: 0,
      alternativeGroupCode: "",
      isOptional: false,
    }]);
  };

  const editable = version.status === "draft";
  return (
    <article className="version-card">
      <header className="version-heading">
        <div>
          <strong>Version {version.versionNumber}</strong>
          <span>Effective {version.effectiveFrom}</span>
        </div>
        <span className={`status ${editable ? "draft" : "active"}`}>{version.status}</span>
      </header>
      <div className="table-scroll">
        <table className="components-table">
          <thead>
            <tr>
              <th>Material</th><th>Unit</th><th>Quantity</th><th>Waste %</th>
              {editable && <th>Optional</th>}
              {editable && <th><span className="sr-only">Remove component</span></th>}
            </tr>
          </thead>
          <tbody>
            {components.map((component, index) => (
              <tr key={component.id ?? `new-${index}`}>
                <td>
                  {editable ? (
                    <select
                      required
                      value={component.itemId}
                      onChange={(event) => update(index, { itemId: event.target.value })}
                    >
                      <option value="">Select item</option>
                      {items.map((item) => <option key={item.id} value={item.id}>{item.label}</option>)}
                    </select>
                  ) : `${component.itemCode ?? ""} ${component.itemName ?? ""}`}
                </td>
                <td>
                  {editable ? (
                    <select
                      required
                      value={component.unitOfMeasureId}
                      onChange={(event) => update(index, { unitOfMeasureId: event.target.value })}
                    >
                      <option value="">Unit</option>
                      {units.map((unit) => <option key={unit.id} value={unit.id}>{unit.label}</option>)}
                    </select>
                  ) : component.unitOfMeasureCode}
                </td>
                <td>
                  {editable
                    ? <input type="number" min="0.000001" step="0.000001" value={component.quantity}
                      onChange={(event) => update(index, { quantity: Number(event.target.value) })} />
                    : component.quantity}
                </td>
                <td>
                  {editable
                    ? <input type="number" min="0" max="100" step="0.01" value={component.wastePercent}
                      onChange={(event) => update(index, { wastePercent: Number(event.target.value) })} />
                    : component.wastePercent}
                </td>
                {editable && (
                  <>
                    <td>
                      <input type="checkbox" checked={component.isOptional}
                        onChange={(event) => update(index, { isOptional: event.target.checked })} />
                    </td>
                    <td>
                      <button className="text-button danger-text"
                        onClick={() => setComponents((current) =>
                          current.filter((_, position) => position !== index))}
                        type="button">
                        Remove
                      </button>
                    </td>
                  </>
                )}
              </tr>
            ))}
            {components.length === 0 && (
              <tr><td className="muted" colSpan={editable ? 6 : 4}>No component lines.</td></tr>
            )}
          </tbody>
        </table>
      </div>
      {editable && (
        <footer className="version-actions">
          <button className="secondary-button compact" onClick={add} type="button">Add material</button>
          <button className="secondary-button compact" onClick={() => onSave(components)} disabled={busy} type="button">
            Save components
          </button>
          <button className="primary-button compact" onClick={onApprove} disabled={busy} type="button">
            Approve version
          </button>
        </footer>
      )}
      {!editable && version.approvedAt && (
        <p className="muted approval-stamp">Approved {new Date(version.approvedAt).toLocaleString()}</p>
      )}
    </article>
  );
}
