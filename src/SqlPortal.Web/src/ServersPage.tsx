import { useEffect, useState } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatNumber, formatStorage, relativeTime } from './format';
import { useDebouncedValue } from './hooks';
import type { PagedResult, PortalServer } from './types';
import { ErrorState, Icon, LoadingState, PageTitle, Pagination, SearchInput, SortButton } from './ui';

export function ServersPage() {
  const [result, setResult] = useState<PagedResult<PortalServer> | null>(null);
  const [search, setSearch] = useState('');
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
    portalApi.servers({ search: debouncedSearch, environment, page, pageSize: 20, sortBy, sortDirection }, controller.signal)
      .then(setResult)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [debouncedSearch, environment, page, sortBy, sortDirection, reloadKey]);

  function updateSearch(value: string) { setSearch(value); setPage(1); }
  function updateSort(field: string, direction: 'asc' | 'desc') { setSortBy(field); setSortDirection(direction); setPage(1); }

  return (
    <>
      <PageTitle eyebrow="Snapshot-Inventar" title="SQL Server" description="Instanzen, Versionen und RAM-Kapazitäten aus der letzten Erfassung." />
      <section className="panel list-panel">
        <div className="filter-bar">
          <SearchInput value={search} onChange={updateSearch} placeholder="Server, Instanz oder Version suchen …" />
          <label className="select-field"><span>Umgebung</span><select value={environment} onChange={(event) => { setEnvironment(event.target.value); setPage(1); }}><option value="">Alle Umgebungen</option><option>Produktion</option><option>Abnahme</option><option>Entwicklung</option></select></label>
          <button className="icon-button" aria-label="Aktualisieren" onClick={() => setReloadKey((value) => value + 1)}><Icon name="refresh" size={18} /></button>
        </div>

        {loading && !result ? <LoadingState label="Server werden geladen" /> : failed && !result ? <ErrorState retry={() => setReloadKey((value) => value + 1)} /> : result && (
          <>
            <div className={`table-scroll${loading ? ' table-scroll--loading' : ''}`}>
              <table>
                <thead><tr>
                  <th><SortButton label="Server" field="name" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Umgebung</th><th>Version</th>
                  <th><SortButton label="Datenbanken" field="databaseCount" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Installierter RAM" field="memoryGb" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Datenstand" field="collectedAtUtc" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                </tr></thead>
                <tbody>{result.items.map((server) => (
                  <tr key={server.id}>
                    <td><strong>{server.name}</strong><small>{server.instanceName}</small></td>
                    <td><span className="environment-tag">{server.environment}</span></td>
                    <td><span>{server.version}</span><small>{server.edition}</small></td>
                    <td>{formatNumber(server.databaseCount)}</td>
                    <td>{server.memoryGb === null ? '–' : formatStorage(server.memoryGb)}</td>
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
    </>
  );
}

function snapshotAgeTone(value: string): 'recent' | 'week' | 'old' {
  const ageInDays = (Date.now() - new Date(value).getTime()) / 86_400_000;
  if (ageInDays <= 1) return 'recent';
  if (ageInDays <= 7) return 'week';
  return 'old';
}

