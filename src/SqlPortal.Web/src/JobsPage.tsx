import { useState } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatJobStatus, formatNumber } from './format';
import { useJobData } from './hooks';
import { JobServerDialog } from './JobServerDialog';
import type { JobRun, JobServerSummary } from './types';
import { ErrorState, Icon, LoadingState, PageTitle, Pagination, SearchInput, StatusPill } from './ui';

const serverPageSize = 15;

export function JobsPage({ initialFilters }: { initialFilters: URLSearchParams }) {
  const requestedStatus = initialFilters.get('status');
  const initialStatus = requestedStatus === 'not-successful' || requestedStatus === 'successful' ? requestedStatus : '';
  const initialHistory = initialFilters.get('history') === 'true';
  const [search, setSearch] = useState('');
  const [evaluation, setEvaluation] = useState(
    initialStatus === 'not-successful' && initialHistory ? 'history' : initialStatus,
  );
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [selectedServer, setSelectedServer] = useState<JobServerSummary | null>(null);
  const servers = useJobData(portalApi.jobServers, revision);
  const violations = useJobData(evaluation === 'rules' ? fetchAllViolations : null, revision);
  const refresh = () => setRevision((value) => value + 1);

  const allServers = servers.data ?? [];
  const ruleServers = new Set(violations.data?.map((rule) => rule.serverName));
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const filtered = allServers.filter((server) => {
    if (!server.serverName.toLocaleLowerCase().includes(normalizedSearch)) return false;
    if (evaluation === 'rules') return ruleServers.has(server.serverName);
    if (evaluation === 'history') return server.hasUnsuccessfulHistory;
    if (evaluation === 'not-successful') return server.successfulJobs < server.totalJobs;
    if (evaluation === 'successful') return server.successfulJobs === server.totalJobs;
    return true;
  });
  const totalPages = Math.max(1, Math.ceil(filtered.length / serverPageSize));
  const currentPage = Math.min(page, totalPages);
  const serverOffset = (currentPage - 1) * serverPageSize;
  const loading = servers.loading || (evaluation === 'rules' && violations.loading);
  const failed = servers.failed || (evaluation === 'rules' && violations.failed);

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
          <div><h2 id="job-server-summaries">Jobstatus pro Server</h2><p>Die Quote basiert auf dem letzten abgeschlossenen Lauf je Job und SQL-Instanz.</p></div>
          {!loading && !failed && <span className="summary-count jobs-count">{formatNumber(filtered.length)}{filtered.length !== allServers.length && ` von ${formatNumber(allServers.length)}`} Server</span>}
        </header>
        <div className="filter-bar jobs-filter-bar">
          <SearchInput value={search} placeholder="Server suchen …" onChange={(value) => { setSearch(value); setPage(1); }} />
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
          initialHistory={evaluation === 'history'}
          initialStatus={evaluation === 'history' ? 'not-successful' : evaluation === 'rules' ? 'rules' : ''}
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
  const { serverName, successfulJobs, totalJobs } = server;
  const unsuccessfulJobs = totalJobs - successfulJobs;
  const percent = successfulJobs * 100 / totalJobs;

  return (
    <button type="button" className="server-compliance-row server-compliance-row--expanded jobs-server-row" aria-haspopup="dialog" onClick={onOpen}>
      <div><strong>{serverName}</strong><span>{formatNumber(totalJobs)} {totalJobs === 1 ? 'Job' : 'Jobs'}</span></div>
      <div className="server-compliance-score">
        <span><b>{formatNumber(percent, 1)} %</b> erfolgreich</span>
        <progress className="jobs-success-bar" max={100} value={percent} aria-label={`Erfolgsquote für ${serverName}`} />
      </div>
      <div className="server-compliance-counts">
        <span>{formatNumber(successfulJobs)} erfolgreich</span>
        <span className={unsuccessfulJobs ? 'server-compliance-counts--danger' : ''}>{formatNumber(unsuccessfulJobs)} nicht erfolgreich · {formatNumber(100 - percent, 1)} %</span>
      </div>
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
