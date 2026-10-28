import { useEffect, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatNumber, formatStorage, relativeTime } from './format';
import { useDebouncedValue } from './hooks';
import type { ConfigurationCheck, PagedResult, PortalServer, ServerConfigurationSummary, ServerDetail, ServerFilterOptions } from './types';
import { EnvironmentTag, ErrorState, Icon, LoadingState, PageTitle, Pagination, SearchInput, SortButton, StatusPill } from './ui';

const environments = ['PROD', 'CONS', 'DEV'];

export function ServerInventoryPage() {
  const [result, setResult] = useState<PagedResult<PortalServer> | null>(null);
  const [filterOptions, setFilterOptions] = useState<ServerFilterOptions | null>(null);
  const [configurationSummaries, setConfigurationSummaries] = useState<ServerConfigurationSummary[] | null>(null);
  const [search, setSearch] = useState('');
  const [environment, setEnvironment] = useState('');
  const [version, setVersion] = useState('');
  const [edition, setEdition] = useState('');
  const [page, setPage] = useState(1);
  const [sortBy, setSortBy] = useState('name');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [selectedServer, setSelectedServer] = useState<PortalServer | null>(null);
  const [detail, setDetail] = useState<ServerDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailFailed, setDetailFailed] = useState(false);
  const [detailReloadKey, setDetailReloadKey] = useState(0);
  const debouncedSearch = useDebouncedValue(search);

  useEffect(() => {
    const controller = new AbortController();
    portalApi.serverFilters(controller.signal)
      .then(setFilterOptions)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setFilterOptions(null);
      });
    setConfigurationSummaries(null);
    portalApi.configurationOverview(controller.signal)
      .then((overview) => setConfigurationSummaries(overview.servers))
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setConfigurationSummaries([]);
      });
    return () => controller.abort();
  }, [reloadKey]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setFailed(false);
    portalApi.servers({
      search: debouncedSearch,
      environment,
      version,
      edition,
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
  }, [debouncedSearch, environment, version, edition, page, sortBy, sortDirection, reloadKey]);

  useEffect(() => {
    if (!selectedServer) return undefined;
    const controller = new AbortController();
    setDetail(null);
    setDetailLoading(true);
    setDetailFailed(false);
    portalApi.serverDetail(selectedServer.id, controller.signal)
      .then(setDetail)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setDetailFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setDetailLoading(false);
      });
    return () => controller.abort();
  }, [selectedServer, detailReloadKey]);

  useEffect(() => {
    if (!selectedServer) return undefined;
    const previousOverflow = document.body.style.overflow;
    const closeOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') setSelectedServer(null);
    };
    document.body.style.overflow = 'hidden';
    window.addEventListener('keydown', closeOnEscape);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', closeOnEscape);
    };
  }, [selectedServer]);

  function updateSearch(value: string) { setSearch(value); setPage(1); }
  function updateSort(field: string, direction: 'asc' | 'desc') { setSortBy(field); setSortDirection(direction); setPage(1); }
  function updateFilter(setter: (value: string) => void, value: string) { setter(value); setPage(1); }
  function openDetail(server: PortalServer) { setSelectedServer(server); }
  function openDetailWithKeyboard(event: ReactKeyboardEvent<HTMLTableRowElement>, server: PortalServer) {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      openDetail(server);
    }
  }

  return (
    <>
      <PageTitle eyebrow="Snapshot-Inventar" title="SQL Server" description="Instanzen, Klassifizierung, SQL-Version und Edition aus der letzten Erfassung." />
      <section className="panel list-panel server-list-panel">
        <div className="filter-bar server-filter-bar">
          <SearchInput value={search} onChange={updateSearch} placeholder="Server, Instanz oder Description suchen …" />
          <label className="select-field"><span>Umgebung</span><select value={environment} onChange={(event) => updateFilter(setEnvironment, event.target.value)}><option value="">Alle Umgebungen</option>{environments.map((value) => <option key={value}>{value}</option>)}</select></label>
          <label className="select-field"><span>SQL-Version</span><select value={version} onChange={(event) => updateFilter(setVersion, event.target.value)}><option value="">Alle SQL-Versionen</option>{filterOptions?.versions.map((value) => <option key={value}>{value}</option>)}</select></label>
          <label className="select-field"><span>Edition</span><select value={edition} onChange={(event) => updateFilter(setEdition, event.target.value)}><option value="">Alle Editionen</option>{filterOptions?.editions.map((value) => <option key={value}>{value}</option>)}</select></label>
          <button className="icon-button" aria-label="Aktualisieren" onClick={() => setReloadKey((value) => value + 1)}><Icon name="refresh" size={18} /></button>
        </div>

        {loading && !result ? <LoadingState label="Server werden geladen" /> : failed && !result ? <ErrorState retry={() => setReloadKey((value) => value + 1)} /> : result && (
          <>
            <div className={`table-scroll${loading ? ' table-scroll--loading' : ''}`}>
              <table className="server-table">
                <thead><tr>
                  <th><SortButton label="Server" field="name" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Description</th>
                  <th>Umgebung</th>
                  <th><SortButton label="SQL-Version" field="version" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Edition" field="edition" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Datenbanken" field="databaseCount" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Konfiguration</th>
                  <th><SortButton label="Datenstand" field="collectedAtUtc" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                </tr></thead>
                <tbody>{result.items.map((server) => (
                  <tr
                    key={server.id}
                    className="server-row"
                    role="button"
                    tabIndex={0}
                    aria-label={`Details für ${server.name} öffnen`}
                    onClick={() => openDetail(server)}
                    onKeyDown={(event) => openDetailWithKeyboard(event, server)}
                  >
                    <td><strong>{server.name}</strong><small>{server.instanceName}</small></td>
                    <td className="server-description" title={server.description ?? undefined}>{server.description ?? '–'}</td>
                    <td><EnvironmentTag value={server.environment} /></td>
                    <td>{server.version}</td>
                    <td>{server.edition}</td>
                    <td>{formatNumber(server.databaseCount)}</td>
                    <td><ServerConfigurationState server={server} summaries={configurationSummaries} /></td>
                    <td><strong className={`snapshot-age snapshot-age--${snapshotAgeTone(server.collectedAtUtc)}`}>{relativeTime(server.collectedAtUtc)}</strong><small>{formatDateTime(server.collectedAtUtc)}</small></td>
                  </tr>
                ))}</tbody>
              </table>
              {result.items.length === 0 && <div className="empty-table"><Icon name="search" size={24} /><strong>Keine Server gefunden</strong><span>Ändere die Suche oder die Filter.</span></div>}
            </div>
            <Pagination page={result.page} totalPages={result.totalPages} totalItems={result.totalItems} onChange={setPage} />
          </>
        )}
      </section>

      {selectedServer && (
        <div className="modal-backdrop" onMouseDown={(event) => { if (event.currentTarget === event.target) setSelectedServer(null); }}>
          <section className="server-detail-modal" role="dialog" aria-modal="true" aria-labelledby="server-detail-title">
            <header className="server-detail-modal__header">
              <div>
                <span className="eyebrow">SQL-Server-Details</span>
                <h2 id="server-detail-title">{selectedServer.name}</h2>
                <p>{selectedServer.instanceName}</p>
              </div>
              <button className="icon-button" aria-label="Detailfenster schließen" onClick={() => setSelectedServer(null)}><Icon name="close" size={19} /></button>
            </header>

            <div className="server-detail-modal__body">
              {detailLoading && <LoadingState label="Serverdetails werden geladen" />}
              {detailFailed && <ErrorState retry={() => setDetailReloadKey((value) => value + 1)} />}
              {detail && !detailLoading && (
                <>
                  <section className="server-detail-summary">
                    <DetailField label="Description" value={detail.server.description} wide />
                    <DetailField label="Umgebung" value={detail.server.environment} />
                    <DetailField label="SQL-Version" value={detail.server.version} />
                    <DetailField label="Edition" value={detail.server.edition} />
                    <DetailField label="Datenbanken" value={formatNumber(detail.server.databaseCount)} />
                    <DetailField label="Datenstand" value={formatDateTime(detail.server.collectedAtUtc)} />
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

                  <DetailSection title="Konfigurationen" count={detail.configurationChecks.length}>
                    {detail.configurationChecks.length > 0 ? (
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
                    ) : <div className="server-detail-empty">Für diese Instanz liegen keine Konfigurationsergebnisse vor.</div>}
                  </DetailSection>

                  <DetailSection title="Datenbanken" count={detail.databases.length}>
                    {detail.databases.length > 0 ? (
                      <div className="server-database-grid">
                        {detail.databases.map((database) => (
                          <article key={database.id} className="server-database-card">
                            <div><strong>{database.name}</strong><span>{database.state} · {database.recoveryModel}</span></div>
                            <span>{database.sizeGb === null ? '–' : formatStorage(database.sizeGb)}</span>
                          </article>
                        ))}
                      </div>
                    ) : <div className="server-detail-empty">Keine Datenbanken in der letzten Erfassung.</div>}
                  </DetailSection>
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

function ServerConfigurationState({
  server,
  summaries,
}: {
  server: PortalServer;
  summaries: ServerConfigurationSummary[] | null;
}) {
  if (summaries === null) return <span className="muted">Wird geladen …</span>;

  const summary = findConfigurationSummary(server, summaries);
  if (!summary) return <StatusPill status="Keine Prüfung" />;

  const status = summary.driftedChecks > 0
    ? 'Abweichung'
    : summary.unknownChecks > 0
      ? 'Nicht ermittelt'
      : 'Konform';
  const affectedChecks = summary.driftedChecks > 0 ? summary.driftedChecks : summary.unknownChecks;
  const detail = status === 'Konform'
    ? `${formatNumber(summary.totalChecks)} Prüfungen`
    : `${formatNumber(affectedChecks)} von ${formatNumber(summary.totalChecks)}`;

  return (
    <div
      className="server-configuration-state"
      title={`${detail} Konfigurationsprüfungen: ${status}`}
    >
      <StatusPill status={status} />
      <small>{detail}</small>
    </div>
  );
}

function findConfigurationSummary(
  server: PortalServer,
  summaries: ServerConfigurationSummary[],
): ServerConfigurationSummary | undefined {
  const matchingId = summaries.find((summary) => summary.serverId === server.id);
  if (matchingId) return matchingId;

  const serverName = server.name.trim().toLocaleLowerCase();
  const instanceName = server.instanceName.trim().toLocaleLowerCase();
  const qualifiedName = instanceName === '' || instanceName === 'mssqlserver' || instanceName === serverName
    ? serverName
    : `${serverName}\\${instanceName}`;

  return summaries.find((summary) => {
    const summaryName = summary.serverName.trim().toLocaleLowerCase();
    return summaryName === qualifiedName || summaryName === serverName;
  });
}

function snapshotAgeTone(value: string | null): 'recent' | 'week' | 'old' | 'unknown' {
  if (!value) return 'unknown';
  const ageInDays = (Date.now() - new Date(value).getTime()) / 86_400_000;
  if (ageInDays <= 1) return 'recent';
  if (ageInDays <= 7) return 'week';
  return 'old';
}
