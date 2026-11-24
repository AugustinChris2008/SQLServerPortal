import { useCallback, useState } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatJobStatus, formatNumber } from './format';
import { useJobData } from './hooks';
import { JobServerDialog } from './JobServerDialog';
import type { JobRun, JobServerSummary } from './types';
import { EnvironmentTag, ErrorState, Icon, LoadingState, PageTitle, Pagination, SearchInput, StatusPill } from './ui';

const serverPageSize = 15;
const unknownEnvironment = 'Nicht klassifiziert';
const unknownServerType = 'Nicht ermittelt';

export function JobsPage({ initialFilters }: { initialFilters: URLSearchParams }) {
  const requestedStatus = initialFilters.get('status');
  const initialStatus = requestedStatus === 'not-successful' || requestedStatus === 'successful' ? requestedStatus : '';
  const initialHistory = initialFilters.get('history') === 'true';
  const [search, setSearch] = useState('');
  const [evaluation, setEvaluation] = useState(
    initialStatus === 'not-successful' && initialHistory ? 'history' : initialStatus,
  );
  const [environment, setEnvironment] = useState('');
  const [serverType, setServerType] = useState('');
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [historyDays, setHistoryDays] = useState(7);
  const [selectedServer, setSelectedServer] = useState<JobServerSummary | null>(null);
  const showRuleViolations = evaluation === 'rules';
  const fetchServers = useCallback(
    (signal: AbortSignal) => portalApi.jobServers(historyDays, showRuleViolations, signal),
    [historyDays, showRuleViolations],
  );
  const servers = useJobData(fetchServers, revision);
  const violations = useJobData(showRuleViolations ? fetchAllViolations : null, revision);
  const refresh = () => setRevision((value) => value + 1);

  const allServers = servers.data ?? [];
  const ruleServers = new Set(violations.data?.map((rule) => rule.serverName));
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const candidateServers = allServers.filter((server) =>
    server.totalJobs > 0 || (showRuleViolations && ruleServers.has(server.serverName)));
  const environments = [...new Set([environment, ...candidateServers.map((server) => server.environment ?? unknownEnvironment)])]
    .filter(Boolean).sort();
  const serverTypes = [...new Set([serverType, ...candidateServers.map((server) => server.serverType ?? unknownServerType)])]
    .filter(Boolean).sort();
  const filtered = candidateServers.filter((server) => {
    if (!server.serverName.toLocaleLowerCase().includes(normalizedSearch)) return false;
    if (environment && (server.environment ?? unknownEnvironment) !== environment) return false;
    if (serverType && (server.serverType ?? unknownServerType) !== serverType) return false;
    if (showRuleViolations) return ruleServers.has(server.serverName);
    if (evaluation === 'history') return server.hasUnsuccessfulHistory;
    if (evaluation === 'not-successful') return server.successfulJobs < server.totalJobs;
    if (evaluation === 'successful') return server.successfulJobs === server.totalJobs;
    return true;
  }).sort((left, right) => left.serverName.localeCompare(right.serverName));
  const totalPages = Math.max(1, Math.ceil(filtered.length / serverPageSize));
  const currentPage = Math.min(page, totalPages);
  const serverOffset = (currentPage - 1) * serverPageSize;
  const loading = servers.loading || (showRuleViolations && violations.loading);
  const failed = servers.failed || (showRuleViolations && violations.failed);

  return (
    <>
      <PageTitle
        eyebrow="SQL Server Agent"
        title="Jobs"
        description="Jobs und Regelabweichungen pro Server."
        action={<button className="button button--secondary" onClick={refresh} disabled={loading}><Icon name="refresh" size={17} />Aktualisieren</button>}
      />
      <section className="panel jobs-panel" aria-labelledby="job-server-summaries">
        <header className="panel__header">
          <div><h2 id="job-server-summaries">Jobstatus pro Server</h2><p>Die Quote basiert auf dem letzten abgeschlossenen Lauf je Job und SQL-Instanz im gewählten Zeitraum.</p></div>
          {!loading && !failed && <span className="summary-count jobs-count">{formatNumber(filtered.length)}{filtered.length !== candidateServers.length && ` von ${formatNumber(candidateServers.length)}`} Server</span>}
        </header>
        <div className="filter-bar jobs-filter-bar">
          <SearchInput value={search} placeholder="Server suchen …" onChange={(value) => { setSearch(value); setPage(1); }} />
          <label className="select-field">
            <span>Historienzeitraum</span>
            <select value={historyDays} onChange={(event) => { setHistoryDays(Number(event.target.value)); setPage(1); }}>
              <option value={1}>Letzte 24 Stunden</option>
              <option value={3}>Letzte 3 Tage</option>
              <option value={7}>Letzte 7 Tage</option>
              <option value={30}>Letzte 30 Tage</option>
              <option value={90}>Letzte 90 Tage</option>
            </select>
          </label>
          <label className="select-field">
            <span id="jobs-server-status-label">Serverstatus</span>
            <select aria-labelledby="jobs-server-status-label" value={evaluation} onChange={(event) => { setEvaluation(event.target.value); setPage(1); }}>
              <option value="">Alle Server</option>
              <option value="successful">Alle letzten Läufe erfolgreich</option>
              <option value="not-successful">Letzter Lauf nicht erfolgreich</option>
              <option value="rules">Mit Regelabweichungen</option>
              <option value="history">Nicht erfolgreiche Läufe in der Historie</option>
            </select>
          </label>
          <label className="select-field">
            <span id="jobs-environment-label">Umgebung</span>
            <select aria-labelledby="jobs-environment-label" value={environment} onChange={(event) => { setEnvironment(event.target.value); setPage(1); }}>
              <option value="">Alle Umgebungen</option>
              {environments.map((value) => <option key={value}>{value}</option>)}
            </select>
          </label>
          <label className="select-field">
            <span id="jobs-server-type-label">Servertyp</span>
            <select aria-labelledby="jobs-server-type-label" value={serverType} onChange={(event) => { setServerType(event.target.value); setPage(1); }}>
              <option value="">Alle Servertypen</option>
              {serverTypes.map((value) => <option key={value}>{value}</option>)}
            </select>
          </label>
        </div>
        {loading ? <LoadingState label="Jobstatus der Server wird geladen" /> :
          failed ? <ErrorState retry={refresh} /> :
          filtered.length > 0 ? (
            <>
              <div className="jobs-server-list">
                {filtered.slice(serverOffset, serverOffset + serverPageSize).map((server) => (
                  <JobServerRow key={server.serverName} server={server} onOpen={() => setSelectedServer(server)} />
                ))}
              </div>
              {totalPages > 1 && <Pagination page={currentPage} totalPages={totalPages} totalItems={filtered.length} onChange={setPage} />}
            </>
          ) : <div className="jobs-empty" role="status">Keine passenden Server gefunden.</div>}
      </section>
      {selectedServer && (
        <JobServerDialog
          server={selectedServer}
          historyDays={historyDays}
          initialStatus={showRuleViolations ? 'rules' : ''}
          onClose={() => setSelectedServer(null)}
        />
      )}
    </>
  );
}

function fetchAllViolations(signal: AbortSignal) {
  return portalApi.jobViolations(undefined, signal);
}

function JobServerRow({ server, onOpen }: { server: JobServerSummary; onOpen: () => void }) {
  const { serverName, successfulJobs, totalJobs, environment, serverType, lastScanAtUtc } = server;
  const unsuccessfulJobs = totalJobs - successfulJobs;
  const percent = totalJobs > 0 ? successfulJobs * 100 / totalJobs : 0;

  return (
    <button type="button" className="server-compliance-row server-compliance-row--expanded jobs-server-row" aria-haspopup="dialog" onClick={onOpen}>
      <div>
        <div className="jobs-server-name">
          <strong>{serverName}</strong>
          <EnvironmentTag value={environment ?? unknownEnvironment} />
          <small>{serverType ?? 'Servertyp nicht ermittelt'}</small>
        </div>
        <span>
          {formatNumber(totalJobs)} {totalJobs === 1 ? 'Job' : 'Jobs'} · Letzter Scan{' '}
          {lastScanAtUtc ? <time dateTime={lastScanAtUtc}>{formatDateTime(lastScanAtUtc)}</time> : 'nicht verfügbar'}
        </span>
      </div>
      {totalJobs > 0 ? (
        <>
          <div className="server-compliance-score">
            <span><b>{formatNumber(percent, 1)} %</b> erfolgreich</span>
            <progress className="jobs-success-bar" max={100} value={percent} aria-label={`Erfolgsquote für ${serverName}`} />
          </div>
          <div className="server-compliance-counts">
            <span>{formatNumber(successfulJobs)} erfolgreich</span>
            <span className={unsuccessfulJobs ? 'server-compliance-counts--danger' : ''}>{formatNumber(unsuccessfulJobs)} nicht erfolgreich · {formatNumber(100 - percent, 1)} %</span>
          </div>
        </>
      ) : <div className="server-compliance-counts">Keine Läufe im Zeitraum</div>}
      <Icon name="chevron-right" size={17} />
    </button>
  );
}

export function DashboardJobRuns({ revision, onOpen }: { revision: number; onOpen: () => void }) {
  const [retry, setRetry] = useState(0);
  const result = useJobData(fetchDashboardJobRuns, revision + retry);

  return (
    <section className="panel jobs-panel" aria-labelledby="dashboard-job-runs">
      <header className="panel__header">
        <div><h2 id="dashboard-job-runs">Nicht erfolgreiche Jobläufe</h2><p>Die jüngsten fehlgeschlagenen oder abgebrochenen Läufe aus der Jobhistorie</p></div>
        <button className="text-button" onClick={onOpen}>Alle anzeigen</button>
      </header>
      {result.loading ? <LoadingState label="Jobläufe werden geladen" /> :
        result.failed ? <ErrorState retry={() => setRetry((value) => value + 1)} /> :
        result.data?.items.length ? <DashboardJobRunsTable runs={result.data.items} /> :
        <div className="jobs-empty" role="status">Keine fehlgeschlagenen oder abgebrochenen Läufe erfasst</div>}
    </section>
  );
}

function fetchDashboardJobRuns(signal: AbortSignal) {
  return portalApi.jobRuns({ status: 'not-successful', history: true, pageSize: 5 }, signal);
}

function DashboardJobRunsTable({ runs }: { runs: JobRun[] }) {
  return (
    <div className="table-scroll">
      <table className="jobs-table">
        <thead><tr><th>Job</th><th>Server / SQL-Instanz</th><th>Start</th><th>Dauer</th><th>Status</th><th>Agent-Meldung</th></tr></thead>
        <tbody>
          {runs.map((run, index) => (
            <tr key={`${run.serverName}-${run.sqlInstance}-${run.jobName}-${run.startedAt}-${index}`}>
              <td><strong>{run.jobName}</strong></td>
              <td><strong>{run.serverName}</strong>{run.sqlInstance !== run.serverName && <small>{run.sqlInstance}</small>}</td>
              <td>{run.startedAt ? formatDateTime(run.startedAt) : '–'}</td>
              <td>{run.duration ?? '–'}</td>
              <td><StatusPill status={formatJobStatus(run.status)} /></td>
              <td>{run.message ? <details className="jobs-message"><summary>Meldung anzeigen</summary><p>{run.message}</p></details> : '–'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
