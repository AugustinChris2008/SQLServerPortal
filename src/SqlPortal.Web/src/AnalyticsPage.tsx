import { useEffect, useMemo, useState } from 'react';
import { portalApi } from './api';
import { formatNumber } from './format';
import type { AnalyticsDistributionItem, AnalyticsOverview } from './types';
import { ErrorState, Icon, LoadingState, PageTitle } from './ui';

export function AnalyticsPage() {
  const [overview, setOverview] = useState<AnalyticsOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setFailed(false);
    portalApi.analyticsOverview(controller.signal)
      .then(setOverview)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [reloadKey]);

  if (loading && !overview) return <LoadingState label="Analysen werden aufgebaut" />;
  if (failed && !overview) return <ErrorState retry={() => setReloadKey((value) => value + 1)} />;
  if (!overview) return null;

  const { metrics } = overview;

  return (
    <>
      <PageTitle
        eyebrow="Kapazität & Lizenzen"
        title="Analysen"
        description="CPU-Kapazität, SQL-Lizenzbedarf sowie die Verteilung der eingesetzten Editionen und Versionen."
        action={
          <button className="button button--secondary" onClick={() => setReloadKey((value) => value + 1)} disabled={loading}>
            <Icon name="refresh" size={17} />
            Aktualisieren
          </button>
        }
      />

      {overview.sourceMode === 'Demo' && (
        <div className="demo-banner">
          <span className="demo-banner__icon"><Icon name="activity" size={18} /></span>
          <div><strong>Demo-Analysen aktiv</strong><span>Im SQL-Modus stammen die CPU-Kerne aus dem letzten PowerShell-Scan.</span></div>
        </div>
      )}

      <section className="metric-grid analytics-metric-grid" aria-label="Analysekennzahlen">
        <AnalyticsMetricCard
          label="CPU-Kerne gesamt"
          value={formatNumber(metrics.totalCpuCores)}
          detail="je physischem Server einmal gezählt"
          icon="cpu"
          tone="blue"
        />
        <AnalyticsMetricCard
          label="SQL-Lizenzen"
          value={formatNumber(metrics.sqlLicenseCount)}
          detail={`${formatNumber(metrics.licensableCpuCores)} lizenzpflichtige Kerne · 2 Kerne je Lizenz`}
          icon="license"
          tone="violet"
        />
      </section>

      <div className="analytics-note">
        <Icon name="check" size={18} />
        <span>Developer- und Express-Editionen sind von der Lizenzberechnung ausgeschlossen.</span>
      </div>

      <section className="analytics-chart-grid">
        <DistributionChart
          title="SQL-Editionen"
          subtitle="Serveranzahl nach eingesetzter Edition"
          items={overview.editions}
          tone="edition"
        />
        <DistributionChart
          title="SQL-Versionen"
          subtitle="Serveranzahl nach SQL-Server-Version"
          items={overview.versions}
          tone="version"
        />
      </section>
    </>
  );
}

function AnalyticsMetricCard({
  label,
  value,
  detail,
  icon,
  tone,
}: {
  label: string;
  value: string;
  detail: string;
  icon: 'cpu' | 'license';
  tone: string;
}) {
  return (
    <article className="metric-card analytics-metric-card">
      <span className={`metric-card__icon metric-card__icon--${tone}`}><Icon name={icon} size={22} /></span>
      <div><span>{label}</span><strong>{value}</strong><small>{detail}</small></div>
    </article>
  );
}

function DistributionChart({
  title,
  subtitle,
  items,
  tone,
}: {
  title: string;
  subtitle: string;
  items: AnalyticsDistributionItem[];
  tone: 'edition' | 'version';
}) {
  const [selectedLabel, setSelectedLabel] = useState(items[0]?.label ?? '');
  const total = useMemo(() => items.reduce((sum, item) => sum + item.serverCount, 0), [items]);
  const maximum = useMemo(() => Math.max(...items.map((item) => item.serverCount), 1), [items]);
  const selected = items.find((item) => item.label === selectedLabel) ?? items[0];
  const selectedPercent = !selected || total === 0 ? 0 : Math.round((selected.serverCount / total) * 100);

  return (
    <article className={`panel analytics-chart analytics-chart--${tone}`}>
      <header className="panel__header analytics-chart__header">
        <div><h2>{title}</h2><p>{subtitle}</p></div>
        <span className="summary-count">{formatNumber(total)} Server</span>
      </header>

      {items.length === 0 ? (
        <div className="server-detail-empty analytics-chart__empty">Keine Daten verfügbar.</div>
      ) : (
        <div className="analytics-chart__body">
          <div className="analytics-chart__selection" aria-live="polite">
            <span>Ausgewählt</span>
            <strong>{selected.label}</strong>
            <small>{formatNumber(selected.serverCount)} Server · {selectedPercent} %</small>
          </div>
          <div className="distribution-bars" role="list" aria-label={title}>
            {items.map((item) => {
              const width = (item.serverCount / maximum) * 100;
              const share = total === 0 ? 0 : Math.round((item.serverCount / total) * 100);
              const selectedItem = item.label === selected.label;
              return (
                <button
                  type="button"
                  role="listitem"
                  key={item.label}
                  className={`distribution-row${selectedItem ? ' distribution-row--selected' : ''}`}
                  aria-pressed={selectedItem}
                  aria-label={`${item.label}: ${item.serverCount} Server, ${share} Prozent`}
                  onClick={() => setSelectedLabel(item.label)}
                  onMouseEnter={() => setSelectedLabel(item.label)}
                  onFocus={() => setSelectedLabel(item.label)}
                >
                  <span className="distribution-row__label"><i />{item.label}</span>
                  <span className="distribution-row__track"><i style={{ width: `${width}%` }} /></span>
                  <strong>{formatNumber(item.serverCount)}</strong>
                  <small>{share} %</small>
                </button>
              );
            })}
          </div>
        </div>
      )}
    </article>
  );
}
