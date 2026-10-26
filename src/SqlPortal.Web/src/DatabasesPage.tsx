import { useEffect, useState } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatPercent, formatStorage, relativeTime } from './format';
import { useDebouncedValue } from './hooks';
import type { PagedResult, PortalDatabase } from './types';
import { ErrorState, Icon, LoadingState, Meter, PageTitle, Pagination, SearchInput, SortButton, StatusPill } from './ui';

export function DatabasesPage() {
  const [result, setResult] = useState<PagedResult<PortalDatabase> | null>(null);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [environment, setEnvironment] = useState('');
  const [page, setPage] = useState(1);
  const [sortBy, setSortBy] = useState('name');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const debouncedSearch = useDebouncedValue(search);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setFailed(false);
    portalApi.databases({ search: debouncedSearch, status, environment, page, pageSize: 20, sortBy, sortDirection }, controller.signal)
      .then(setResult)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [debouncedSearch, status, environment, page, sortBy, sortDirection, reloadKey]);

  function updateSearch(value: string) { setSearch(value); setPage(1); }
  function updateSort(field: string, direction: 'asc' | 'desc') { setSortBy(field); setSortDirection(direction); setPage(1); }

  return (
    <>
      <PageTitle eyebrow="Snapshot-Inventar" title="Datenbanken" description="Status, Kapazität und Sicherungsstand aus der letzten Erfassung." />
      <section className="panel list-panel">
        <div className="filter-bar">
          <SearchInput value={search} onChange={updateSearch} placeholder="Datenbank, Server oder Owner suchen …" />
          <label className="select-field"><span>Status</span><select value={status} onChange={(event) => { setStatus(event.target.value); setPage(1); }}><option value="">Alle Status</option><option>Gesund</option><option>Warnung</option><option>Kritisch</option></select></label>
          <label className="select-field"><span>Umgebung</span><select value={environment} onChange={(event) => { setEnvironment(event.target.value); setPage(1); }}><option value="">Alle Umgebungen</option><option>Produktion</option><option>Abnahme</option><option>Entwicklung</option></select></label>
          <button className="icon-button" aria-label="Aktualisieren" onClick={() => setReloadKey((value) => value + 1)}><Icon name="refresh" size={18} /></button>
        </div>

        {loading && !result ? <LoadingState label="Datenbanken werden geladen" /> : failed && !result ? <ErrorState retry={() => setReloadKey((value) => value + 1)} /> : result && (
          <>
            <div className={`table-scroll${loading ? ' table-scroll--loading' : ''}`}>
              <table>
                <thead><tr>
                  <th><SortButton label="Datenbank" field="name" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Server" field="serverName" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Status" field="status" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>State</th><th>Recovery</th>
                  <th><SortButton label="Größe" field="sizeGb" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Daten</th>
                  <th><SortButton label="Log" field="logUsedPercent" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Letztes Full Backup</th><th>Stand</th>
                </tr></thead>
                <tbody>{result.items.map((database) => (
                  <tr key={database.id}>
                    <td><strong>{database.name}</strong><small>Owner: {database.owner}</small></td>
                    <td><strong className="table-secondary">{database.serverName}</strong><small>{database.environment}</small></td>
                    <td><StatusPill status={database.status} /></td>
                    <td><span className={`state-label${database.state !== 'ONLINE' ? ' state-label--danger' : ''}`}>{database.state}</span></td>
                    <td>{database.recoveryModel}<small>Compat {database.compatibilityLevel ?? '–'}</small></td>
                    <td>{database.sizeGb === null ? '–' : formatStorage(database.sizeGb)}</td>
                    <td><Meter value={database.dataUsedPercent} /></td>
                    <td><div className="compact-meter"><Meter value={database.logUsedPercent} /><small>{formatPercent(database.logUsedPercent)}</small></div></td>
                    <td title={formatDateTime(database.lastFullBackupAtUtc)}>{relativeTime(database.lastFullBackupAtUtc)}</td>
                    <td className="muted">{relativeTime(database.collectedAtUtc)}</td>
                  </tr>
                ))}</tbody>
              </table>
              {result.items.length === 0 && <div className="empty-table"><Icon name="search" size={24} /><strong>Keine Datenbanken gefunden</strong><span>Ändere die Suche oder die Filter.</span></div>}
            </div>
            <Pagination page={result.page} totalPages={result.totalPages} totalItems={result.totalItems} onChange={setPage} />
          </>
        )}
      </section>
    </>
  );
}

