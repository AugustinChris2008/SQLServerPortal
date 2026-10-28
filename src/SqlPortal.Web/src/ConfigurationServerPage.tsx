import { useEffect, useMemo, useState } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatNumber, relativeTime } from './format';
import type { ConfigurationCheck, ConfigurationOverview, ServerConfigurationSummary } from './types';
import { EnvironmentTag, ErrorState, Icon, LoadingState, PageTitle, SearchInput, StatusPill } from './ui';

export function ConfigurationServerPage() {
  const [overview, setOverview] = useState<ConfigurationOverview | null>(null);
  const [overviewLoading, setOverviewLoading] = useState(true);
  const [overviewFailed, setOverviewFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [selectedServer, setSelectedServer] = useState<ServerConfigurationSummary | null>(null);
  const [checks, setChecks] = useState<ConfigurationCheck[] | null>(null);
  const [checksLoading, setChecksLoading] = useState(false);
  const [checksFailed, setChecksFailed] = useState(false);
  const [checksReloadKey, setChecksReloadKey] = useState(0);
  const [serverSearch, setServerSearch] = useState('');
  const [classification, setClassification] = useState('');
  const [evaluation, setEvaluation] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    setOverviewLoading(true);
    setOverviewFailed(false);
    portalApi.configurationOverview(controller.signal)
      .then(setOverview)
      .catch((error: unknown) => {
        if (!isAbortError(error)) setOverviewFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setOverviewLoading(false);
      });
    return () => controller.abort();
  }, [reloadKey]);

  useEffect(() => {
    if (!selectedServer) return undefined;

    const controller = new AbortController();
    const server = selectedServer;
    setChecks(null);
    setChecksLoading(true);
    setChecksFailed(false);

    async function loadChecks() {
      try {
        const loadedChecks: ConfigurationCheck[] = [];
        let page = 1;
        let totalPages = 1;

        do {
          const result = await portalApi.configurationChecks({
            search: server.serverName,
            page,
            pageSize: 100,
            sortBy: 'status',
            sortDirection: 'asc',
          }, controller.signal);
          loadedChecks.push(...result.items);
          totalPages = result.totalPages;
          page += 1;
        } while (page <= totalPages && !controller.signal.aborted);

        if (controller.signal.aborted) return;
        const normalizedServerName = server.serverName.trim().toLocaleLowerCase();
        setChecks(loadedChecks
          .filter((check) => check.serverId === server.serverId
            || check.serverName.trim().toLocaleLowerCase() === normalizedServerName)
          .sort(compareChecks));
      }
      catch (error: unknown) {
        if (!isAbortError(error)) setChecksFailed(true);
      }
      finally {
        if (!controller.signal.aborted) setChecksLoading(false);
      }
    }

    void loadChecks();
    return () => controller.abort();
  }, [selectedServer, checksReloadKey]);

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

  const classifications = useMemo(
    () => [...new Set(overview?.servers.map((server) => server.environment) ?? [])].sort(),
    [overview],
  );

  const filteredServers = useMemo(() => {
    const normalizedSearch = serverSearch.trim().toLocaleLowerCase();
    return (overview?.servers ?? []).filter((server) => {
      const matchesSearch = normalizedSearch === ''
        || server.serverName.toLocaleLowerCase().includes(normalizedSearch)
        || server.environment.toLocaleLowerCase().includes(normalizedSearch);
      const matchesClassification = classification === '' || server.environment === classification;
      const matchesEvaluation = evaluation === '' || serverStatus(server) === evaluation;
      return matchesSearch && matchesClassification && matchesEvaluation;
    });
  }, [overview, serverSearch, classification, evaluation]);

  const serverMetrics = useMemo(() => {
    const servers = overview?.servers ?? [];
    return {
      compliant: servers.filter((server) => server.driftedChecks === 0 && server.unknownChecks === 0).length,
      drifted: servers.filter((server) => server.driftedChecks > 0).length,
      unknown: servers.filter((server) => server.driftedChecks === 0 && server.unknownChecks > 0).length,
    };
  }, [overview]);

  function reload() {
    setReloadKey((value) => value + 1);
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
        description="Serverbewertung aus den neuesten SOLL- und IST-Werten. Ein Klick auf einen Server zeigt alle Parameter."
        action={
          <button className="button button--secondary" onClick={reload} disabled={overviewLoading}>
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

      <section className="metric-grid configuration-metric-grid" aria-label="Konfigurationskennzahlen">
        <ConfigurationMetric label="Gesamterfüllung" value={`${formatNumber(metrics.compliancePercent, 1)} %`} detail="Anteil konformer Prüfungen" icon="check" tone={metrics.compliancePercent === 100 ? 'green' : 'blue'} />
        <ConfigurationMetric label="Prüfungen" value={formatNumber(metrics.definedChecks)} detail="definierte Regeln" icon="settings" tone="blue" />
        <ConfigurationMetric label="Konform" value={formatNumber(serverMetrics.compliant)} detail="konforme Server" icon="check" tone="green" />
        <ConfigurationMetric label="Abweichung" value={formatNumber(serverMetrics.drifted)} detail="Server mit Abweichung" icon="alert" tone={serverMetrics.drifted > 0 ? 'red' : 'green'} />
        <ConfigurationMetric label="Nicht ermittelt" value={formatNumber(serverMetrics.unknown)} detail="nicht ermittelte Server" icon="clock" tone={serverMetrics.unknown > 0 ? 'amber' : 'green'} />
      </section>

      <section className="configuration-server-section">
        <article className="panel server-compliance-panel server-compliance-panel--expanded">
          <header className="panel__header"><div><h2>Serverbewertung</h2><p>Server auswählen, um alle Parameter zu öffnen</p></div><span className="summary-count">{filteredServers.length === overview.servers.length ? `${overview.servers.length} Server` : `${filteredServers.length} von ${overview.servers.length}`}</span></header>
          <div className="filter-bar configuration-server-filter-bar">
            <SearchInput value={serverSearch} onChange={setServerSearch} placeholder="Server oder Klassifizierung suchen …" />
            <label className="select-field"><span>Klassifizierung</span><select value={classification} onChange={(event) => setClassification(event.target.value)}><option value="">Alle Klassifizierungen</option>{classifications.map((value) => <option key={value}>{value}</option>)}</select></label>
            <label className="select-field"><span>Bewertung</span><select value={evaluation} onChange={(event) => setEvaluation(event.target.value)}><option value="">Alle Bewertungen</option><option value="Abweichung">Abweichung</option><option value="Konform">Konform</option><option value="Nicht ermittelt">Nicht ermittelt</option></select></label>
          </div>
          <div className="server-compliance-list server-compliance-list--expanded">
            {filteredServers.map((server) => {
              const status = serverStatus(server);
              return (
                <button key={server.serverId} className="server-compliance-row server-compliance-row--expanded" aria-haspopup="dialog" onClick={() => setSelectedServer(server)}>
                  <div>
                    <strong>{server.serverName}</strong>
                    <span><EnvironmentTag value={server.environment} /> Stand {relativeTime(server.collectedAtUtc)}</span>
                  </div>
                  <div className="server-compliance-score">
                    <span><b>{formatNumber(server.compliancePercent, 1)}%</b> konform</span>
                    <i><span style={{ width: `${server.compliancePercent}%` }} /></i>
                  </div>
                  <div className="server-compliance-counts">
                    <span className={server.driftedChecks > 0 ? 'server-compliance-counts--danger' : ''}>{formatNumber(server.driftedChecks)} abweichend</span>
                    <span>{formatNumber(server.unknownChecks)} offen</span>
                    <span>{formatNumber(server.compliantChecks)} konform</span>
                  </div>
                  <div className="server-compliance-action"><StatusPill status={status} /></div>
                </button>
              );
            })}
            {filteredServers.length === 0 && <div className="empty-table configuration-server-empty"><Icon name="search" size={24} /><strong>Keine Server gefunden</strong><span>Ändere die Suche oder die Filter.</span></div>}
          </div>
        </article>
      </section>

      {selectedServer && (
        <div className="modal-backdrop" onMouseDown={(event) => { if (event.currentTarget === event.target) setSelectedServer(null); }}>
          <section className="server-detail-modal configuration-detail-modal" role="dialog" aria-modal="true" aria-labelledby="configuration-detail-title">
            <header className="server-detail-modal__header">
              <div>
                <span className="eyebrow">Konfigurationsdetails</span>
                <h2 id="configuration-detail-title">{selectedServer.serverName}</h2>
                <p>{selectedServer.environment} · Stand {formatDateTime(selectedServer.collectedAtUtc)}</p>
              </div>
              <button className="icon-button" aria-label="Detailfenster schließen" onClick={() => setSelectedServer(null)}><Icon name="close" size={19} /></button>
            </header>

            <div className="server-detail-modal__body">
              <section className="configuration-detail-summary">
                <ConfigurationDetailMetric label="Prüfungen" value={selectedServer.totalChecks} />
                <ConfigurationDetailMetric label="Abweichungen" value={selectedServer.driftedChecks} tone={selectedServer.driftedChecks > 0 ? 'danger' : 'success'} />
                <ConfigurationDetailMetric label="Nicht ermittelt" value={selectedServer.unknownChecks} tone={selectedServer.unknownChecks > 0 ? 'warning' : 'success'} />
                <ConfigurationDetailMetric label="Konform" value={selectedServer.compliantChecks} tone="success" />
              </section>

              {checksLoading && <LoadingState label="Serverprüfungen werden geladen" />}
              {checksFailed && <ErrorState retry={() => setChecksReloadKey((value) => value + 1)} />}
              {checks && !checksLoading && (
                checks.length > 0
                  ? <ConfigurationGroups checks={checks} />
                  : <div className="server-detail-empty">Für diesen Server liegen keine Konfigurationsprüfungen vor.</div>
              )}
            </div>
          </section>
        </div>
      )}
    </>
  );
}

function ConfigurationGroups({ checks }: { checks: ConfigurationCheck[] }) {
  const groups = [
    { title: 'Nicht erfüllt', tone: 'danger', checks: checks.filter((check) => check.status === 'Abweichung') },
    { title: 'Nicht ermittelt', tone: 'neutral', checks: checks.filter((check) => check.status === 'Nicht ermittelt') },
    { title: 'Erfüllt', tone: 'success', checks: checks.filter((check) => check.status !== 'Abweichung' && check.status !== 'Nicht ermittelt') },
  ];

  return (
    <div className="configuration-detail-groups">
      {groups.filter((group) => group.checks.length > 0).map((group) => (
        <section key={group.title} className={`configuration-detail-group configuration-detail-group--${group.tone}`}>
          <header><div><h3>{group.title}</h3><span>{group.tone === 'danger' ? 'SOLL und IST weichen voneinander ab' : group.tone === 'neutral' ? 'Kein auswertbarer IST-Wert' : 'SOLL und IST stimmen überein'}</span></div><strong>{formatNumber(group.checks.length)}</strong></header>
          <div className="table-scroll configuration-detail-table-scroll">
            <table className="configuration-detail-table">
              <thead><tr><th>Parameter</th><th>Servertyp</th><th>SOLL</th><th>IST</th><th>Konfiguriert</th><th>Laufend</th><th>Status</th><th>Stand</th></tr></thead>
              <tbody>{group.checks.map((check) => {
                const scope = configurationScope(check);
                return (
                  <tr key={check.id} className={check.status === 'Abweichung' ? 'configuration-row--drift' : ''}>
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
                    <td className="muted" title={formatDateTime(check.collectedAtUtc)}>{relativeTime(check.collectedAtUtc)}</td>
                  </tr>
                );
              })}</tbody>
            </table>
          </div>
        </section>
      ))}
    </div>
  );
}

function ConfigurationMetric({ label, value, detail, icon, tone }: { label: string; value: string; detail: string; icon: 'settings' | 'check' | 'alert' | 'clock'; tone: string }) {
  return <article className="metric-card"><span className={`metric-card__icon metric-card__icon--${tone}`}><Icon name={icon} size={22} /></span><div><span>{label}</span><strong>{value}</strong><small>{detail}</small></div></article>;
}

function ConfigurationDetailMetric({ label, value, tone = 'neutral' }: { label: string; value: number; tone?: 'neutral' | 'success' | 'warning' | 'danger' }) {
  return <div className={`configuration-detail-metric configuration-detail-metric--${tone}`}><span>{label}</span><strong>{formatNumber(value)}</strong></div>;
}

function ConfigurationValue({ value, unit, expected = false, drifted = false }: { value: string | null; unit: string | null; expected?: boolean; drifted?: boolean }) {
  const className = [
    'configuration-value',
    expected ? 'configuration-value--expected' : '',
    drifted ? 'configuration-value--drift' : '',
    value === null ? 'configuration-value--missing' : '',
  ].filter(Boolean).join(' ');
  return <span className={className} title={value ?? undefined}>{value === null ? '–' : withUnit(value, unit)}</span>;
}

function configurationScope(check: ConfigurationCheck): string | null {
  const parts = [
    check.databaseName ? `DB: ${check.databaseName}` : null,
    check.objectName ? `Objekt: ${check.objectName}` : null,
  ].filter((value): value is string => value !== null);
  return parts.length > 0 ? parts.join(' · ') : null;
}

function compareChecks(left: ConfigurationCheck, right: ConfigurationCheck): number {
  const statusDifference = statusRank(left.status) - statusRank(right.status);
  if (statusDifference !== 0) return statusDifference;
  return left.parameterName.localeCompare(right.parameterName, 'de', { sensitivity: 'base' });
}

function statusRank(status: string): number {
  if (status === 'Abweichung') return 0;
  if (status === 'Nicht ermittelt') return 1;
  return 2;
}

function serverStatus(server: ServerConfigurationSummary): string {
  if (server.driftedChecks > 0) return 'Abweichung';
  if (server.unknownChecks > 0) return 'Nicht ermittelt';
  return 'Konform';
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

function withUnit(value: string, unit: string | null): string {
  return unit ? `${value} ${unit}` : value;
}
