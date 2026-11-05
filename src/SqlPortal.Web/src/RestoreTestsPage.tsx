import { useEffect, useState, type CSSProperties, type KeyboardEvent as ReactKeyboardEvent } from 'react';
import { portalApi } from './api';
import { formatDate, formatDateTime, formatNumber, formatStorage, relativeTime } from './format';
import { useDebouncedValue } from './hooks';
import type { PagedResult, RestoreTestDatabase, RestoreTestHistoryDetail, RestoreTestOverview } from './types';
import { ErrorState, Icon, LoadingState, PageTitle, Pagination, SearchInput, SortButton, StatusPill } from './ui';

const restoreStatuses = ['Erfolgreich', 'Fehlgeschlagen', 'Übersprungen', 'Nicht getestet'];
const selectionStatuses = ['Nie wiederhergestellt', 'Einmal wiederhergestellt', 'Mehrfach wiederhergestellt'];
const restorePeriods = [
  { value: '365', label: '1 Jahr' },
  { value: '730', label: '2 Jahre' },
  { value: '1095', label: '3 Jahre' },
  { value: '1825', label: '5 Jahre' },
  { value: 'all', label: 'Gesamt' },
];

export function RestoreTestsPage() {
  const [overview, setOverview] = useState<RestoreTestOverview | null>(null);
  const [result, setResult] = useState<PagedResult<RestoreTestDatabase> | null>(null);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [selection, setSelection] = useState('');
  const [period, setPeriod] = useState('365');
  const [page, setPage] = useState(1);
  const [sortBy, setSortBy] = useState('periodStatus');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [selectedDatabase, setSelectedDatabase] = useState<RestoreTestDatabase | null>(null);
  const [historyDetail, setHistoryDetail] = useState<RestoreTestHistoryDetail | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyFailed, setHistoryFailed] = useState(false);
  const [historyReloadKey, setHistoryReloadKey] = useState(0);
  const debouncedSearch = useDebouncedValue(search);

  useEffect(() => {
    const controller = new AbortController();
    setFailed(false);
    portalApi.restoreTestOverview(period, controller.signal)
      .then(setOverview)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setFailed(true);
      });
    return () => controller.abort();
  }, [period, reloadKey]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setFailed(false);
    portalApi.restoreTestDatabases({
      search: debouncedSearch,
      status,
      selection,
      period,
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
  }, [debouncedSearch, status, selection, period, page, sortBy, sortDirection, reloadKey]);

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

  useEffect(() => {
    if (!selectedDatabase) return undefined;
    const controller = new AbortController();
    setHistoryDetail(null);
    setHistoryLoading(true);
    setHistoryFailed(false);
    portalApi.restoreTestHistory(selectedDatabase.id, period, controller.signal)
      .then(setHistoryDetail)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setHistoryFailed(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setHistoryLoading(false);
      });
    return () => controller.abort();
  }, [selectedDatabase, period, historyReloadKey]);

  function updateSort(field: string, direction: 'asc' | 'desc') {
    setSortBy(field);
    setSortDirection(direction);
    setPage(1);
  }

  function openWithKeyboard(event: ReactKeyboardEvent<HTMLTableRowElement>, database: RestoreTestDatabase) {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      setSelectedDatabase(database);
    }
  }

  if (loading && !result && !overview) return <LoadingState label="Restoretest-Auswertung wird geladen" />;
  if (failed && !result && !overview) return <ErrorState retry={() => setReloadKey((value) => value + 1)} />;

  const metrics = overview?.metrics;
  const coverage = metrics?.coveragePercent ?? 0;

  return (
    <>
      <PageTitle
        eyebrow="Backup-Validierung"
        title="Automatische Restoretests"
        description="Abdeckung aller aktuell inventarisierten Datenbanken mit Restore- und DBCC-Ergebnis im gewählten Zeitraum."
        action={<button className="button button--secondary" onClick={() => setReloadKey((value) => value + 1)}><Icon name="refresh" size={17} />Aktualisieren</button>}
      />

      {overview && (
        <div className="restore-window-banner">
          <span><Icon name="clock" size={18} /></span>
          <div><strong>{overview.windowStartUtc ? 'Rollierender Auswertungszeitraum' : 'Gesamter verfügbarer Auswertungszeitraum'}</strong><small>{overview.windowStartUtc ? `${formatDate(overview.windowStartUtc)} bis heute` : 'Alle vorhandenen Restoretest-Ergebnisse'} · Grundgesamtheit aus SQLPortal</small></div>
          <div className="restore-period-picker" role="group" aria-label="Auswertungszeitraum auswählen">
            {restorePeriods.map((option) => <button key={option.value} className={period === option.value ? 'restore-period-picker__active' : ''} onClick={() => { setPeriod(option.value); setPage(1); }}>{option.label}</button>)}
          </div>
        </div>
      )}

      {metrics && (
        <section className="restore-overview-grid" aria-label="Restoretest-Kennzahlen">
          <article className="panel restore-coverage-panel">
            <div className="restore-coverage-donut" style={{ '--progress': `${coverage * 3.6}deg` } as CSSProperties}>
              <div><strong>{formatNumber(coverage, 1)}%</strong><span>abgedeckt</span></div>
            </div>
            <div className="restore-coverage-copy">
              <span>Abdeckung im Zeitraum</span>
              <strong>{formatNumber(metrics.successfullyTestedDatabases)} von {formatNumber(metrics.totalDatabases)}</strong>
              <p>Restore und DBCC innerhalb des gewählten Zeitraums erfolgreich.</p>
              <div><i style={{ width: `${coverage}%` }} /></div>
            </div>
          </article>
          <div className="restore-metric-grid">
            <RestoreMetric label="Noch offen" value={metrics.remainingDatabases} detail="ohne erfolgreichen Test im Zeitraum" tone="warning" />
            <RestoreMetric label="Aktuelle Fehler" value={metrics.currentFailureDatabases} detail="letzter Versuch fehlgeschlagen" tone="danger" />
            <RestoreMetric label="Übersprungen" value={metrics.skippedDatabases} detail="im gewählten Zeitraum" tone="warning" />
            <RestoreMetric label="Nicht getestet" value={metrics.notTestedDatabases} detail="kein Versuch im Zeitraum" tone="neutral" />
          </div>
        </section>
      )}

      {metrics && (
        <section className="panel restore-selection-panel">
          <header className="panel__header">
            <div><h2>Restores pro Datenbank</h2><p>Wie häufig eine Datenbank innerhalb des gewählten Zeitraums wiederhergestellt wurde</p></div>
            <div className="restore-selection-total"><strong>{formatNumber(metrics.totalAttemptsWithinPeriod)}</strong><span>Testläufe · {formatNumber(metrics.repeatedAttempts)} Wiederholungen</span></div>
          </header>
          <div className="restore-selection-buckets">
            <SelectionBucket label="Nie wiederhergestellt" value={metrics.neverSelectedDatabases} total={metrics.totalDatabases} tone="danger" onClick={() => { setSelection('Nie wiederhergestellt'); setPage(1); }} />
            <SelectionBucket label="Einmal wiederhergestellt" value={metrics.selectedOnceDatabases} total={metrics.totalDatabases} tone="success" onClick={() => { setSelection('Einmal wiederhergestellt'); setPage(1); }} />
            <SelectionBucket label="Mehrfach wiederhergestellt" value={metrics.selectedMultipleTimesDatabases} total={metrics.totalDatabases} tone="warning" onClick={() => { setSelection('Mehrfach wiederhergestellt'); setPage(1); }} />
          </div>
        </section>
      )}

      {overview && overview.failureCategories.length > 0 && (
        <section className="panel restore-errors-panel">
          <header className="panel__header"><div><h2>Fehlerübersicht</h2><p>Aufbereitete Kategorien aus dem jeweils letzten fehlgeschlagenen Versuch</p></div></header>
          <div className="restore-error-categories">
            {overview.failureCategories.map((category) => (
              <button key={category.category} onClick={() => { setSearch(category.category); setStatus(''); setPage(1); }}>
                <span><Icon name="alert" size={16} /></span>
                <div><strong>{category.category}</strong><small>{formatNumber(category.databaseCount)} Datenbanken</small></div>
              </button>
            ))}
          </div>
        </section>
      )}

      <section className="panel list-panel restore-list-panel">
        <div className="filter-bar restore-filter-bar">
          <SearchInput value={search} onChange={(value) => { setSearch(value); setPage(1); }} placeholder="Server, Datenbank oder Fehlermeldung suchen …" />
          <label className="select-field"><span>Zeitraumstatus</span><select value={status} onChange={(event) => { setStatus(event.target.value); setPage(1); }}><option value="">Alle Status</option>{restoreStatuses.map((value) => <option key={value}>{value}</option>)}</select></label>
          <label className="select-field"><span>Restores im Zeitraum</span><select value={selection} onChange={(event) => { setSelection(event.target.value); setPage(1); }}><option value="">Alle Häufigkeiten</option>{selectionStatuses.map((value) => <option key={value}>{value}</option>)}</select></label>
          <button className="icon-button" aria-label="Aktualisieren" onClick={() => setReloadKey((value) => value + 1)}><Icon name="refresh" size={18} /></button>
        </div>

        {loading && !result ? <LoadingState label="Datenbanken werden ausgewertet" /> : failed && !result ? <ErrorState retry={() => setReloadKey((value) => value + 1)} /> : result && (
          <>
            <div className={`table-scroll${loading ? ' table-scroll--loading' : ''}`}>
              <table className="restore-table">
                <thead><tr>
                  <th><SortButton label="Datenbank" field="databaseName" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Quellserver" field="sourceServer" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Zeitraumstatus</th>
                  <th><SortButton label="Restores im Zeitraum" field="attemptCountWithinPeriod" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th>Letzter Versuch</th>
                  <th>Ergebnis</th>
                  <th><SortButton label="Letzter Erfolg" field="lastSuccessfulAtUtc" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                  <th><SortButton label="Größe" field="sizeGb" activeField={sortBy} direction={sortDirection} onChange={updateSort} /></th>
                </tr></thead>
                <tbody>{result.items.map((database) => (
                  <tr key={database.id} role="button" tabIndex={0} onClick={() => setSelectedDatabase(database)} onKeyDown={(event) => openWithKeyboard(event, database)}>
                    <td><strong>{database.databaseName}</strong><small>{database.testServer ? `Test auf ${database.testServer}` : 'Kein Testserver erfasst'}</small></td>
                    <td>{database.sourceServer}</td>
                    <td><StatusPill status={database.periodStatus} /></td>
                    <td className="restore-selection-cell"><strong>{formatNumber(database.attemptCountWithinPeriod)}×</strong><small>{database.selectionStatus} · {formatNumber(database.successfulAttemptCountWithinPeriod)} erfolgreich{database.undatedAttemptCount > 0 ? ` · ${formatNumber(database.undatedAttemptCount)} ohne Datum` : ''}</small></td>
                    <td><StatusPill status={database.latestAttemptStatus} /><small>{database.latestAttemptAtUtc ? relativeTime(database.latestAttemptAtUtc) : 'Zeitpunkt unbekannt'}</small></td>
                    <td className="restore-result-cell"><strong>{database.failureCategory ?? database.latestAttemptStatus}</strong><small title={database.restoreResult ?? database.dbccResult ?? undefined}>{database.restoreResult ?? database.dbccResult ?? '–'}</small></td>
                    <td>{database.lastSuccessfulAtUtc ? <><strong>{relativeTime(database.lastSuccessfulAtUtc)}</strong><small>{formatDateTime(database.lastSuccessfulAtUtc)}</small></> : '–'}</td>
                    <td>{database.sizeGb === null ? '–' : formatStorage(database.sizeGb)}</td>
                  </tr>
                ))}</tbody>
              </table>
              {result.items.length === 0 && <div className="empty-table"><Icon name="search" size={24} /><strong>Keine Datenbanken gefunden</strong><span>Ändere die Suche oder den Zeitraumstatus.</span></div>}
            </div>
            <Pagination page={result.page} totalPages={result.totalPages} totalItems={result.totalItems} onChange={setPage} />
          </>
        )}
      </section>

      {selectedDatabase && (
        <RestoreDetail
          database={selectedDatabase}
          historyDetail={historyDetail}
          historyLoading={historyLoading}
          historyFailed={historyFailed}
          retryHistory={() => setHistoryReloadKey((value) => value + 1)}
          onClose={() => setSelectedDatabase(null)}
        />
      )}
    </>
  );
}

function RestoreMetric({ label, value, detail, tone }: { label: string; value: number; detail: string; tone: string }) {
  return <article className={`restore-metric restore-metric--${tone}`}><span>{label}</span><strong>{formatNumber(value)}</strong><small>{detail}</small></article>;
}

function SelectionBucket({ label, value, total, tone, onClick }: { label: string; value: number; total: number; tone: string; onClick: () => void }) {
  const percentage = total === 0 ? 0 : value * 100 / total;
  return (
    <button className={`restore-selection-bucket restore-selection-bucket--${tone}`} onClick={onClick}>
      <div><span>{label}</span><strong>{formatNumber(value)} <small>({formatNumber(percentage, 1)}%)</small></strong></div>
      <i><span style={{ width: `${percentage}%` }} /></i>
    </button>
  );
}

function RestoreDetail({ database, historyDetail, historyLoading, historyFailed, retryHistory, onClose }: {
  database: RestoreTestDatabase;
  historyDetail: RestoreTestHistoryDetail | null;
  historyLoading: boolean;
  historyFailed: boolean;
  retryHistory: () => void;
  onClose: () => void;
}) {
  return (
    <div className="modal-backdrop" onMouseDown={(event) => { if (event.currentTarget === event.target) onClose(); }}>
      <section className="server-detail-modal restore-detail-modal" role="dialog" aria-modal="true" aria-labelledby="restore-detail-title">
        <header className="server-detail-modal__header">
          <div><span className="eyebrow">Restoretest-Details</span><h2 id="restore-detail-title">{database.databaseName}</h2><p>{database.sourceServer}</p></div>
          <button className="icon-button" aria-label="Detailfenster schließen" onClick={onClose}><Icon name="close" size={19} /></button>
        </header>
        <div className="server-detail-modal__body">
          <section className="restore-detail-status">
            <div><span>Zeitraumstatus</span><StatusPill status={database.periodStatus} /></div>
            <div><span>Restorehäufigkeit</span><strong>{database.selectionStatus}</strong></div>
            <div><span>Letzter Versuch</span><StatusPill status={database.latestAttemptStatus} /></div>
            <div><span>Fehlerkategorie</span><strong>{database.failureCategory ?? '–'}</strong></div>
            <div><span>Backup vorhanden</span><strong>{database.backupFileExists === null ? 'Unbekannt' : database.backupFileExists ? 'Ja' : 'Nein'}</strong></div>
          </section>
          <section className="restore-detail-grid">
            <DetailValue label="Testserver" value={database.testServer} />
            <DetailValue label="Größe" value={database.sizeGb === null ? null : formatStorage(database.sizeGb)} />
            <DetailValue label="Letzter Versuch" value={formatDateTime(database.latestAttemptAtUtc)} />
            <DetailValue label="Letzter Erfolg" value={formatDateTime(database.lastSuccessfulAtUtc)} />
            <DetailValue label="Restore-Dauer" value={database.restoreElapsed} />
            <DetailValue label="DBCC-Dauer" value={database.dbccElapsed} />
            <DetailValue label="Restores im Zeitraum" value={`${formatNumber(database.attemptCountWithinPeriod)} Testläufe`} />
            <DetailValue label="Davon erfolgreich" value={`${formatNumber(database.successfulAttemptCountWithinPeriod)} Testläufe`} />
            <DetailValue label="Versuche ohne Datum" value={formatNumber(database.undatedAttemptCount)} />
          </section>
          <section className="restore-message-grid">
            <RestoreMessage title="Restore-Ergebnis" value={database.restoreResult} danger={database.latestAttemptStatus === 'Fehlgeschlagen'} />
            <RestoreMessage title="DBCC-Ergebnis" value={database.dbccResult} danger={database.latestAttemptStatus === 'Fehlgeschlagen'} />
          </section>
          <section className="restore-history">
            <header>
              <div><span className="eyebrow">Testverlauf</span><h3>Historie der Restoretests</h3></div>
              {historyDetail && <strong>{formatNumber(historyDetail.history.length)} Läufe</strong>}
            </header>
            {historyLoading && <LoadingState label="Restoretest-Historie wird geladen" />}
            {historyFailed && <ErrorState retry={retryHistory} />}
            {historyDetail && !historyLoading && (
              historyDetail.history.length > 0 ? (
                <div className="restore-history-list">
                  {historyDetail.history.map((entry) => (
                    <article key={`${entry.sequence}-${entry.attemptAtUtc ?? 'undated'}`} className={`restore-history-entry restore-history-entry--${historyTone(entry.status)}`}>
                      <span className="restore-history-entry__marker" />
                      <div className="restore-history-entry__content">
                        <header>
                          <div><strong>{entry.attemptAtUtc ? formatDateTime(entry.attemptAtUtc) : 'Zeitpunkt unbekannt'}</strong><small>{entry.withinPeriod ? 'Im gewählten Zeitraum' : 'Außerhalb des gewählten Zeitraums'}{entry.testServer ? ` · ${entry.testServer}` : ''}</small></div>
                          <StatusPill status={entry.status} />
                        </header>
                        {entry.failureCategory && <div className="restore-history-entry__error"><Icon name="alert" size={14} />{entry.failureCategory}</div>}
                        <div className="restore-history-entry__results">
                          <div><span>Restore</span><code>{entry.restoreResult ?? 'Kein Ergebnis'}</code><small>Dauer {entry.restoreElapsed ?? '–'}</small></div>
                          <div><span>DBCC</span><code>{entry.dbccResult ?? 'Kein Ergebnis'}</code><small>Dauer {entry.dbccElapsed ?? '–'}</small></div>
                        </div>
                      </div>
                    </article>
                  ))}
                </div>
              ) : <div className="restore-history-empty">Für diese Datenbank ist noch kein Restoretest-Verlauf vorhanden.</div>
            )}
          </section>
          <section className="restore-paths">
            <DetailPath label="Full Backup" date={database.fullDateUtc} value={database.fullPath} />
            <DetailPath label="Differential Backup" date={database.diffDateUtc} value={database.diffPath} />
            <DetailPath label="Log Backups" date={null} value={database.logPaths ?? database.logDates} />
          </section>
        </div>
      </section>
    </div>
  );
}

function historyTone(status: string): 'success' | 'warning' | 'danger' | 'neutral' {
  if (status === 'Erfolgreich') return 'success';
  if (status === 'Übersprungen') return 'warning';
  if (status === 'Fehlgeschlagen') return 'danger';
  return 'neutral';
}

function DetailValue({ label, value }: { label: string; value: string | null }) {
  return <div><span>{label}</span><strong>{value ?? '–'}</strong></div>;
}

function RestoreMessage({ title, value, danger }: { title: string; value: string | null; danger: boolean }) {
  return <article className={danger && value && value.toLowerCase() !== 'success' ? 'restore-message--danger' : ''}><span>{title}</span><pre>{value ?? 'Kein Ergebnis erfasst'}</pre></article>;
}

function DetailPath({ label, date, value }: { label: string; date: string | null; value: string | null }) {
  return <div><span>{label}{date ? ` · ${formatDateTime(date)}` : ''}</span><code>{value ?? 'Nicht hinterlegt'}</code></div>;
}
