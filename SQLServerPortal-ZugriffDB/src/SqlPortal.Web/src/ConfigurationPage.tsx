import { useEffect, useMemo, useState, type CSSProperties } from 'react';
import { portalApi } from './api';
import { formatNumber, relativeTime } from './format';
import { useDebouncedValue } from './hooks';
import type { ConfigurationOverview, PagedResult, ConfigurationCheck } from './types';
import { ErrorState, Icon, LoadingState, PageTitle, Pagination, SearchInput, SortButton, StatusPill } from './ui';

export function ConfigurationPage() {
  const [overview, setOverview] = useState<ConfigurationOverview | null>(null);
  const [result, setResult] = useState<PagedResult<ConfigurationCheck> | null>(null);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [environment, setEnvironment] = useState('');
  const [category, setCategory] = useState('');
  const [page, setPage] = useState(1);
  const [sortBy, setSortBy] = useState('status');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');
  const [overviewLoading, setOverviewLoading] = useState(true);
  const [checksLoading, setChecksLoading] = useState(true);
  const [overviewFailed, setOverviewFailed] = useState(false);
  const [checksFailed, setChecksFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const debouncedSearch = useDebouncedValue(search);

  useEffect(() => {
    const controller = new AbortController();
    setOverviewLoading(true);
    setOverviewFailed(false);
    portalApi.configurationOverview(controller.signal)
      .then(setOverview)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setOverviewFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setOverviewLoading(false);
      });
    return () => controller.abort();
  }, [reloadKey]);

  useEffect(() => {
    const controller = new AbortController();
    setChecksLoading(true);
    setChecksFailed(false);
    portalApi.configurationChecks({
      search: debouncedSearch,
      status,
      environment,
      category,
      page,
      pageSize: 20,
      sortBy,
      sortDirection,
    }, controller.signal)
      .then(setResult)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setChecksFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setChecksLoading(false);
      });
    return () => controller.abort();
  }, [debouncedSearch, status, environment, category, page, sortBy, sortDirection, reloadKey]);

  const environments = useMemo(
    () => [...new Set(overview?.servers.map((server) => server.environment) ?? [])].sort(),
    [overview],
  );

  function reload() {
    setReloadKey((value) => value + 1);
  }

  function updateSearch(value: string) {
    setSearch(value);
    setPage(1);
  }

  function updateSort(field: string, direction: 'asc' | 'desc') {
    setSortBy(field);
    setSortDirection(direction);
    setPage(1);
  }

  function focusServer(serverName: string) {
    setSearch(serverName);
    setPage(1);
    document.getElementById('configuration-checks')?.scrollIntoView({ behavior: 'smooth' });
  }

  if (overviewLoading && !overview) return <LoadingState label="Konfigurationsprüfung wird aufgebaut" />;
  if (overviewFailed && !overview) return <ErrorState retry={reload} />;
  if (!overview) return null;

  const { metrics } = overview;

  return (
    <>
      <PageTitle
        eyebrow="SQL Compliance"
        title="Konfigurationsprüfung"
        description="SOLL-Vorgaben aus ComplianceRules mit den neuesten IST-Werten aus ComplianceResults vergleichen."
        action={
          <button className="button button--secondary" onClick={reload} disabled={overviewLoading || checksLoading}>
            <Icon name="refresh" size={17} />
            Aktualisieren
          </button>
        }
      />

      {overview.sourceMode === 'Demo' && (
        <div className="demo-banner">
          <span className="demo-banner__icon"><Icon name="activity" size={18} /></span>
          <div><strong>Demo-Konfiguration aktiv</strong><span>Im SQL-Modus werden ComplianceRules und ComplianceResults verwendet.</span></div>
        </div>
      )}

      <section className="metric-grid" aria-label="Konfigurationskennzahlen">
        <ConfigurationMetric label="Prüfungen" value={formatNumber(metrics.totalChecks)} detail="über alle Server" icon="settings" tone="blue" />
        <ConfigurationMetric label="Konform" value={formatNumber(metrics.compliantChecks)} detail={`${formatNumber(metrics.compliancePercent, 1)} % erfüllt`} icon="check" tone="green" />
        <ConfigurationMetric label="Abweichungen" value={formatNumber(metrics.driftedChecks)} detail="SOLL und IST prüfen" icon="alert" tone={metrics.driftedChecks > 0 ? 'red' : 'green'} />
        <ConfigurationMetric label="Nicht ermittelt" value={formatNumber(metrics.unknownChecks)} detail="IST-Wert fehlt" icon="clock" tone={metrics.unknownChecks > 0 ? 'amber' : 'green'} />
      </section>

      <section className="configuration-summary-grid">
        <article className="panel compliance-panel">
          <header className="panel__header"><div><h2>Gesamterfüllung</h2><p>Anteil konformer Prüfungen</p></div></header>
          <div className="compliance-panel__body">
            <div className="donut compliance-donut" style={{ '--progress': `${metrics.compliancePercent * 3.6}deg` } as CSSProperties}>
              <div><strong>{formatNumber(metrics.compliancePercent, 1)}%</strong><span>konform</span></div>
            </div>
            <div className="compliance-breakdown">
              <ComplianceLine label="Konform" value={metrics.compliantChecks} total={metrics.totalChecks} tone="success" />
              <ComplianceLine label="Abweichung" value={metrics.driftedChecks} total={metrics.totalChecks} tone="danger" />
              <ComplianceLine label="Nicht ermittelt" value={metrics.unknownChecks} total={metrics.totalChecks} tone="neutral" />
            </div>
          </div>
        </article>

        <article className="panel server-compliance-panel">
          <header className="panel__header"><div><h2>Serverbewertung</h2><p>Server mit Abweichungen stehen oben</p></div><span className="summary-count">{overview.servers.length} Server</span></header>
          <div className="server-compliance-list">
            {overview.servers.map((server) => {
              const serverStatus = server.driftedChecks > 0 ? 'Abweichung' : server.unknownChecks > 0 ? 'Nicht ermittelt' : 'Konform';
              return (
                <button key={server.serverId} className="server-compliance-row" onClick={() => focusServer(server.serverName)}>
                  <div><strong>{server.serverName}</strong><span>{server.environment} · Stand {relativeTime(server.collectedAtUtc)}</span></div>
                  <div className="server-compliance-score">
                    <span><b>{formatNumber(server.compliancePercent, 1)}%</b> konform</span>
                    <i><span style={{ width: `${server.compliancePercent}%` }} /></i>
                  </div>
                  <StatusPill status={serverStatus} />
                  <small>{server.driftedChecks > 0 ? `${server.driftedChecks} abweichend` : server.unknownChecks > 0 ? `${server.unknownChecks} ohne IST` : 'vollständig erfüllt'}</small>
                </button>
              );
            })}
          </div>
        </article>
      </section>

      <section className="panel list-panel configuration-checks" id="configuration-checks">
        <header className="panel__header"><div><h2>Detailprüfung</h2><p>Alle Parameter mit SOLL-, IST- und Bewertungswert</p></div></header>
        <div className="filter-bar configuration-filter-bar">
          <SearchInput value={search} onChange={updateSearch} placeholder="Server, Parameter oder Wert suchen …" />
          <label className="select-field"><span>Status</span><select value={status} onChange={(event) => { setStatus(event.target.value); setPage(1); }}><option value="">Alle Status</option><option>Abweichung</option><option>Konform</option><option>Nicht ermittelt</option></select></label>
          <label className="select-field"><span>Umgebung</span><select value={environment} onChange={(event) => { setEnvironment(event.target.value); setPage(1); }}><option value="">Alle Umgebungen</option>{environments.map((value) => <option key={value}>{value}</option>)}</select></label>
          <label className="select-field"><span>Servertyp</span><select value={category} onChange={(event) => { setCategory(event.target.value); setPage(1); }}><option value="">Alle Servertypen</option>{overview.categories.map((value) => <option key={value}>{value}</option>)}</select></label>
          <button className="icon-button" aria-label="Aktualisieren" onClick={reload}><Icon name="refresh" size={18} /></button>
        </div>

        {checksLoading && !result ? <LoadingState label="Konfigurationswerte werden geladen" /> : checksFailed && !result ? <ErrorState retry={reload} /> : result && (
          <>
            <div className={`table-scroll${checksLoading ? ' table-scroll--loading' : ''}`}>
              <table className="configuration-table">
                <thead><tr>
                  <th><SortButton label="Server" field="serverName" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Parameter" field="parameterName" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Servertyp" field="category" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>SOLL</th><th>IST</th>
                  <th>Konfiguriert</th><th>Laufend</th>
                  <th><SortButton label="Bewertung" field="status" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Stand</th>
                </tr></thead>
                <tbody>{result.items.map((check) => {
                  const scope = configurationScope(check);
                  return (
                    <tr key={check.id} className={check.status === 'Abweichung' ? 'configuration-row--drift' : ''}>
                      <td><strong>{check.serverName}</strong><small>{check.environment}</small></td>
                      <td className="configuration-parameter-cell">
                        <strong className="parameter-name">{check.parameterName}</strong>
                        {scope && <small>{scope}</small>}
                        {check.description && <small className="configuration-description" title={check.description}>{check.description}</small>}
                      </td>
                      <td><span className="environment-tag">{check.category}</span></td>
                      <td><ConfigurationValue value={check.expectedValue} unit={check.unit} expected /></td>
                      <td><ConfigurationValue value={check.actualValue} unit={check.unit} drifted={check.status === 'Abweichung'} /></td>
                      <td><ConfigurationValue value={check.configuredValue} unit={check.unit} /></td>
                      <td><ConfigurationValue value={check.runningValue} unit={check.unit} /></td>
                      <td><StatusPill status={check.status} /></td>
                      <td className="muted" title={check.complianceState ?? undefined}>{relativeTime(check.collectedAtUtc)}</td>
                    </tr>
                  );
                })}</tbody>
              </table>
              {result.items.length === 0 && <div className="empty-table"><Icon name="search" size={24} /><strong>Keine Prüfungen gefunden</strong><span>Ändere die Suche oder die Filter.</span></div>}
            </div>
            <Pagination page={result.page} totalPages={result.totalPages} totalItems={result.totalItems} onChange={setPage} />
          </>
        )}
      </section>
    </>
  );
}

function ConfigurationMetric({ label, value, detail, icon, tone }: { label: string; value: string; detail: string; icon: 'settings' | 'check' | 'alert' | 'clock'; tone: string }) {
  return <article className="metric-card"><span className={`metric-card__icon metric-card__icon--${tone}`}><Icon name={icon} size={22} /></span><div><span>{label}</span><strong>{value}</strong><small>{detail}</small></div></article>;
}

function ComplianceLine({ label, value, total, tone }: { label: string; value: number; total: number; tone: 'success' | 'danger' | 'neutral' }) {
  const percentage = total === 0 ? 0 : (value / total) * 100;
  return <div className="compliance-line"><div><span><i className={`dot dot--${tone}`} />{label}</span><strong>{value}</strong></div><div><span className={`compliance-line__fill compliance-line__fill--${tone}`} style={{ width: `${percentage}%` }} /></div></div>;
}

function ConfigurationValue({ value, unit, expected = false, drifted = false }: { value: string | null; unit: string | null; expected?: boolean; drifted?: boolean }) {
  const className = [
    'configuration-value',
    expected ? 'configuration-value--expected' : '',
    drifted ? 'configuration-value--drift' : '',
    value === null ? 'configuration-value--missing' : '',
  ].filter(Boolean).join(' ');
  return <span className={className}>{value === null ? '–' : withUnit(value, unit)}</span>;
}

function configurationScope(check: ConfigurationCheck): string | null {
  const parts = [
    check.databaseName ? `DB: ${check.databaseName}` : null,
    check.objectName ? `Objekt: ${check.objectName}` : null,
  ].filter((value): value is string => value !== null);
  return parts.length > 0 ? parts.join(' · ') : null;
}

function withUnit(value: string, unit: string | null): string {
  return unit ? `${value} ${unit}` : value;
}
