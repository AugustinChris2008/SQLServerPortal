import { useCallback, useEffect, useState, type CSSProperties } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatNumber, relativeTime } from './format';
import type { PortalOverview } from './types';
import { EnvironmentTag, ErrorState, Icon, LoadingState, PageTitle } from './ui';

export function DashboardPage({ onNavigate }: { onNavigate: (view: 'servers' | 'databases' | 'configuration') => void }) {
  const [overview, setOverview] = useState<PortalOverview | null>(null);
  const [misconfiguredServers, setMisconfiguredServers] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  const load = useCallback(() => setReloadKey((value) => value + 1), []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setFailed(false);
    setMisconfiguredServers(null);
    portalApi.overview(controller.signal)
      .then(setOverview)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    portalApi.configurationOverview(controller.signal)
      .then((configuration) => {
        setMisconfiguredServers(configuration.servers.filter((server) => server.driftedChecks > 0).length);
      })
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setMisconfiguredServers(null);
      });
    return () => controller.abort();
  }, [reloadKey]);

  if (loading && !overview) return <LoadingState label="Dashboard wird aufgebaut" />;
  if (failed && !overview) return <ErrorState retry={load} />;
  if (!overview) return null;

  const { metrics } = overview;
  const withinWeekServers = metrics.recentServers + metrics.weekOldServers;
  const withinWeekRate = metrics.totalServers === 0 ? 0 : Math.round((withinWeekServers / metrics.totalServers) * 100);

  return (
    <>
      <PageTitle
        eyebrow="SQL Inventar"
        title="Bestandsübersicht."
        description="Server-, Datenbank- und Kapazitätsdaten aus der jeweils letzten Erfassung."
        action={
          <button className="button button--secondary" onClick={load} disabled={loading}>
            <Icon name="refresh" size={17} />
            Aktualisieren
          </button>
        }
      />

      <div className="demo-banner snapshot-banner">
        <span className="demo-banner__icon"><Icon name="clock" size={18} /></span>
        <div><strong>{overview.sourceMode === 'Demo' ? 'Demo-Scans aktiv' : 'Scan-Daten'}</strong><span>Keine Liveüberwachung: Jeder Wert zeigt den zuletzt erfassten Datenstand.</span></div>
        <span className="demo-banner__freshness"><Icon name="clock" size={15} /> Ältester Stand {relativeTime(overview.oldestCollectedAtUtc)}</span>
      </div>

      <section className="metric-grid dashboard-metric-grid" aria-label="Kennzahlen">
        <MetricCard label="SQL Server" value={formatNumber(metrics.totalServers)} detail="erfasste Instanzen" icon="server" tone="blue" onClick={() => onNavigate('servers')} />
        <MetricCard label="Datenbanken" value={formatNumber(metrics.totalDatabases)} detail="laut letzter Erfassung" icon="database" tone="violet" onClick={() => onNavigate('databases')} />
        <MetricCard label="Nicht konform" value={misconfiguredServers === null ? '–' : formatNumber(misconfiguredServers)} detail="Server mit Abweichung" icon="settings" tone={misconfiguredServers === null ? 'amber' : misconfiguredServers > 0 ? 'red' : 'green'} onClick={() => onNavigate('configuration')} />
        <MetricCard label="Auffälligkeiten" value={formatNumber(metrics.attentionDatabases)} detail="im letzten Scan" icon="alert" tone={metrics.attentionDatabases > 0 ? 'red' : 'green'} onClick={() => onNavigate('databases')} />
      </section>

      <section className="dashboard-grid">
        <article className="panel health-panel">
          <PanelHeader title="Datenalter" subtitle="Zeitpunkt der letzten Servererfassung" />
          <div className="health-overview">
            <div className="donut" style={{ '--progress': `${withinWeekRate * 3.6}deg` } as CSSProperties}>
              <div><strong>{withinWeekRate}%</strong><span>≤ 7 Tage</span></div>
            </div>
            <div className="health-legend">
              <FreshnessRow label="Unter 24 Stunden" value={metrics.recentServers} total={metrics.totalServers} tone="success" />
              <FreshnessRow label="1 bis 7 Tage" value={metrics.weekOldServers} total={metrics.totalServers} tone="warning" />
              <FreshnessRow label="Älter als 7 Tage" value={metrics.olderServers} total={metrics.totalServers} tone="danger" />
            </div>
          </div>
          <div className="environment-list">
            {overview.environments.map((item) => (
              <div key={item.environment}>
                <span>{item.environment}</span>
                <strong>{item.serverCount} Server</strong>
                <small>{item.databaseCount} Datenbanken</small>
              </div>
            ))}
          </div>
        </article>

        <article className="panel table-panel dashboard-oldest-scans">
          <PanelHeader title="Älteste Server-Scans" subtitle="Description, Klassifizierung und Datenstand" action={<button className="text-button" onClick={() => onNavigate('servers')}>Alle Server</button>} />
          <div className="table-scroll">
            <table>
              <thead><tr><th>Server</th><th>Description</th><th>Umgebung</th><th>Datenbanken</th><th>Datenstand</th></tr></thead>
              <tbody>
                {overview.focusServers.map((server) => (
                  <tr key={server.id}>
                    <td><strong>{server.name}</strong><small>{server.version} · {server.edition}</small></td>
                    <td>{server.description ?? '–'}</td>
                    <td><EnvironmentTag value={server.environment} /></td>
                    <td>{server.databaseCount}</td>
                    <td className="muted" title={formatDateTime(server.collectedAtUtc)}>{relativeTime(server.collectedAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </article>
      </section>
    </>
  );
}

function MetricCard({ label, value, detail, icon, tone, onClick }: { label: string; value: string; detail: string; icon: 'server' | 'database' | 'storage' | 'alert' | 'settings'; tone: string; onClick?: () => void }) {
  const content = <><span className={`metric-card__icon metric-card__icon--${tone}`}><Icon name={icon} size={22} /></span><div><span>{label}</span><strong>{value}</strong><small>{detail}</small></div></>;
  return onClick ? <button className="metric-card metric-card--button" onClick={onClick}>{content}</button> : <article className="metric-card">{content}</article>;
}

function PanelHeader({ title, subtitle, action }: { title: string; subtitle: string; action?: React.ReactNode }) {
  return <header className="panel__header"><div><h2>{title}</h2><p>{subtitle}</p></div>{action}</header>;
}

function FreshnessRow({ label, value, total, tone }: { label: string; value: number; total: number; tone: string }) {
  const percentage = total === 0 ? 0 : (value / total) * 100;
  return <div className="health-row"><div><span><i className={`dot dot--${tone}`} />{label}</span><strong>{value}</strong></div><div className="health-row__bar"><span className={`health-row__bar-fill health-row__bar-fill--${tone}`} style={{ width: `${percentage}%` }} /></div></div>;
}

