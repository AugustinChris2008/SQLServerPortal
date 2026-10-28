import { useEffect, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatNumber, formatStorage, relativeTime } from './format';
import { useDebouncedValue } from './hooks';
import type { ConfigurationCheck, DatabaseDetail, PagedResult, PortalDatabase } from './types';
import { ErrorState, Icon, LoadingState, PageTitle, Pagination, SearchInput, SortButton, StatusPill } from './ui';

const environments = ['Prod', 'Cons', 'Test'];

export function DatabaseInventoryPage() {
  const [result, setResult] = useState<PagedResult<PortalDatabase> | null>(null);
  const [search, setSearch] = useState('');
  const [environment, setEnvironment] = useState('');
  const [page, setPage] = useState(1);
  const [sortBy, setSortBy] = useState('name');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [selectedDatabase, setSelectedDatabase] = useState<PortalDatabase | null>(null);
  const [detail, setDetail] = useState<DatabaseDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailFailed, setDetailFailed] = useState(false);
  const [detailReloadKey, setDetailReloadKey] = useState(0);
  const debouncedSearch = useDebouncedValue(search);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setFailed(false);
    portalApi.databases({
      search: debouncedSearch,
      environment,
      page,
      pageSize: 20,
      sortBy,
      sortDirection,
    }, controller.signal)
      .then(setResult)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [debouncedSearch, environment, page, sortBy, sortDirection, reloadKey]);

  useEffect(() => {
    if (!selectedDatabase) return undefined;
    const controller = new AbortController();
    setDetail(null);
    setDetailLoading(true);
    setDetailFailed(false);
    portalApi.databaseDetail(selectedDatabase.id, controller.signal)
      .then(setDetail)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setDetailFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setDetailLoading(false);
      });
    return () => controller.abort();
  }, [selectedDatabase, detailReloadKey]);

  useEffect(() => {
    if (!selectedDatabase) return undefined;
    const previousOverflow = document.body.style.overflow;
    const closeOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') setSelectedDatabase(null);
    };
    document.body.style.overflow = 'hidden';
    window.addEventListener('keydown', closeOnEscape);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', closeOnEscape);
    };
  }, [selectedDatabase]);

  function updateSearch(value: string) { setSearch(value); setPage(1); }
  function updateSort(field: string, direction: 'asc' | 'desc') { setSortBy(field); setSortDirection(direction); setPage(1); }
  function openDetail(database: PortalDatabase) { setSelectedDatabase(database); }
  function openDetailWithKeyboard(event: ReactKeyboardEvent<HTMLTableRowElement>, database: PortalDatabase) {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      openDetail(database);
    }
  }

  return (
    <>
      <PageTitle eyebrow="Snapshot-Inventar" title="Datenbanken" description="Datenbanken, Klassifizierung, Recovery und Sicherungsstand aus der letzten Erfassung." />
      <section className="panel list-panel database-list-panel">
        <div className="filter-bar database-filter-bar">
          <SearchInput value={search} onChange={updateSearch} placeholder="Datenbank, Server oder Owner suchen …" />
          <label className="select-field"><span>Umgebung</span><select value={environment} onChange={(event) => { setEnvironment(event.target.value); setPage(1); }}><option value="">Alle Umgebungen</option>{environments.map((value) => <option key={value}>{value}</option>)}</select></label>
          <button className="icon-button" aria-label="Aktualisieren" onClick={() => setReloadKey((value) => value + 1)}><Icon name="refresh" size={18} /></button>
        </div>

        {loading && !result ? <LoadingState label="Datenbanken werden geladen" /> : failed && !result ? <ErrorState retry={() => setReloadKey((value) => value + 1)} /> : result && (
          <>
            <div className={`table-scroll${loading ? ' table-scroll--loading' : ''}`}>
              <table className="database-table">
                <thead><tr>
                  <th><SortButton label="Datenbank" field="name" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Server" field="serverName" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Umgebung</th>
                  <th>Recovery</th>
                  <th><SortButton label="Größe" field="sizeGb" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Letztes Full Backup</th>
                  <th><SortButton label="Datenstand" field="collectedAtUtc" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                </tr></thead>
                <tbody>{result.items.map((database) => (
                  <tr
                    key={database.id}
                    className="database-row"
                    role="button"
                    tabIndex={0}
                    aria-label={`Details für ${database.name} öffnen`}
                    onClick={() => openDetail(database)}
                    onKeyDown={(event) => openDetailWithKeyboard(event, database)}
                  >
                    <td><strong>{database.name}</strong><small>Owner: {database.owner}</small></td>
                    <td><strong className="table-secondary">{database.serverName}</strong></td>
                    <td><span className="environment-tag">{database.environment}</span></td>
                    <td>{database.recoveryModel}<small>Compat {database.compatibilityLevel ?? '–'}</small></td>
                    <td>{database.sizeGb === null ? '–' : formatStorage(database.sizeGb)}</td>
                    <td title={formatDateTime(database.lastFullBackupAtUtc)}>{relativeTime(database.lastFullBackupAtUtc)}</td>
                    <td className="muted" title={formatDateTime(database.collectedAtUtc)}>{relativeTime(database.collectedAtUtc)}</td>
                  </tr>
                ))}</tbody>
              </table>
              {result.items.length === 0 && <div className="empty-table"><Icon name="search" size={24} /><strong>Keine Datenbanken gefunden</strong><span>Ändere die Suche oder den Filter.</span></div>}
            </div>
            <Pagination page={result.page} totalPages={result.totalPages} totalItems={result.totalItems} onChange={setPage} />
          </>
        )}
      </section>

      {selectedDatabase && (
        <div className="modal-backdrop" onMouseDown={(event) => { if (event.currentTarget === event.target) setSelectedDatabase(null); }}>
          <section className="server-detail-modal" role="dialog" aria-modal="true" aria-labelledby="database-detail-title">
            <header className="server-detail-modal__header">
              <div>
                <span className="eyebrow">Datenbankdetails</span>
                <h2 id="database-detail-title">{selectedDatabase.name}</h2>
                <p>{selectedDatabase.serverName}</p>
              </div>
              <button className="icon-button" aria-label="Detailfenster schließen" onClick={() => setSelectedDatabase(null)}><Icon name="close" size={19} /></button>
            </header>

            <div className="server-detail-modal__body">
              {detailLoading && <LoadingState label="Datenbankdetails werden geladen" />}
              {detailFailed && <ErrorState retry={() => setDetailReloadKey((value) => value + 1)} />}
              {detail && !detailLoading && (
                <>
                  <section className="server-detail-summary">
                    <DetailField label="Server" value={detail.database.serverName} wide />
                    <DetailField label="Umgebung" value={detail.database.environment} />
                    <DetailField label="Recovery" value={detail.database.recoveryModel} />
                    <DetailField label="Owner" value={detail.database.owner} />
                    <DetailField label="Größe" value={detail.database.sizeGb === null ? null : formatStorage(detail.database.sizeGb)} />
                    <DetailField label="Letztes Full Backup" value={formatDateTime(detail.database.lastFullBackupAtUtc)} />
                    <DetailField label="Datenstand" value={formatDateTime(detail.database.collectedAtUtc)} />
                  </section>

                  <DetailSection title="Alle Parameter" count={detail.parameters.length}>
                    <div className="server-parameter-grid">
                      {detail.parameters.map((parameter) => (
                        <div key={parameter.name} className="server-parameter">
                          <span>{parameter.name}</span>
                          <code title={parameter.value ?? undefined}>{parameter.value ?? 'NULL'}</code>
                        </div>
                      ))}
                    </div>
                  </DetailSection>

                  {detail.configurationChecks.length > 0 && (
                    <DetailSection title="Konfigurationen" count={detail.configurationChecks.length}>
                      <div className="table-scroll server-detail-table-scroll">
                        <table className="server-detail-configuration-table">
                          <thead><tr><th>Prüfung</th><th>SOLL</th><th>IST</th><th>Konfiguriert</th><th>Laufend</th><th>Status</th></tr></thead>
                          <tbody>{detail.configurationChecks.map((check) => (
                            <tr key={check.id}>
                              <td><strong className="parameter-name">{check.parameterName}</strong><small>{configurationScope(check)}</small></td>
                              <td><code>{check.expectedValue}</code></td>
                              <td><code>{check.actualValue ?? '–'}</code></td>
                              <td><code>{check.configuredValue ?? '–'}</code></td>
                              <td><code>{check.runningValue ?? '–'}</code></td>
                              <td><StatusPill status={check.status} /></td>
                            </tr>
                          ))}</tbody>
                        </table>
                      </div>
                    </DetailSection>
                  )}
                </>
              )}
            </div>
          </section>
        </div>
      )}
    </>
  );
}

function DetailField({ label, value, wide = false }: { label: string; value: string | null; wide?: boolean }) {
  return <div className={`server-detail-field${wide ? ' server-detail-field--wide' : ''}`}><span>{label}</span><strong>{value ?? '–'}</strong></div>;
}

function DetailSection({ title, count, children }: { title: string; count: number; children: ReactNode }) {
  return <section className="server-detail-section"><header><h3>{title}</h3><span>{formatNumber(count)}</span></header>{children}</section>;
}

function configurationScope(check: ConfigurationCheck): string {
  return [check.category, check.databaseName, check.objectName].filter(Boolean).join(' · ');
}
