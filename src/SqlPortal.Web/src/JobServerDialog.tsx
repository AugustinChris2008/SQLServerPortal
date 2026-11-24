import { useCallback, useMemo, useRef, useState } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatJobStatus, formatNumber } from './format';
import { useJobData, useModalDialog } from './hooks';
import { JobHistoryDialog } from './JobHistoryDialog';
import type { JobIdentity, JobMonitoringResult, JobRun, JobServerSummary } from './types';
import { ErrorState, Icon, LoadingState, Pagination, SearchInput, StatusPill } from './ui';

const jobsPageSize = 20;

interface JobRow {
  job: JobIdentity;
  latestRun?: JobRun;
  rules: JobMonitoringResult[];
}

export function JobServerDialog({ server, historyDays, initialStatus, onClose }: {
  server: JobServerSummary;
  historyDays: number;
  initialStatus: string;
  onClose: () => void;
}) {
  const dialog = useModalDialog();
  const body = useRef<HTMLDivElement>(null);
  const [selectedJob, setSelectedJob] = useState<JobIdentity | null>(null);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState(initialStatus);
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const { serverName, successfulJobs, totalJobs } = server;

  const fetchLatest = useCallback((signal: AbortSignal) => loadLatestRuns(serverName, historyDays, signal), [serverName, historyDays]);
  const fetchMonitoring = useCallback((signal: AbortSignal) => portalApi.jobMonitoring(serverName, signal), [serverName]);
  const latest = useJobData(fetchLatest, revision);
  const monitoring = useJobData(fetchMonitoring, revision);

  const jobs = useMemo(() => {
    const grouped = new Map<string, JobRow>();
    for (const run of latest.data ?? []) {
      grouped.set(jobKey(run), { job: run, latestRun: run, rules: [] });
    }
    for (const rule of monitoring.data ?? []) {
      const key = jobKey(rule);
      const row = grouped.get(key);
      if (row) row.rules.push(rule);
      else grouped.set(key, { job: rule, rules: [rule] });
    }
    return [...grouped.values()].filter((row) => row.latestRun || row.rules.some(hasViolation));
  }, [latest.data, monitoring.data]);

  const normalized = search.trim().toLocaleLowerCase();
  const filtered = jobs.filter(({ job, latestRun, rules }) => {
    if (!job.jobName.toLocaleLowerCase().includes(normalized) && !job.sqlInstance.toLocaleLowerCase().includes(normalized)) return false;
    if (status === 'successful') return latestRun?.status === 'Succeeded';
    if (status === 'not-successful') return latestRun && latestRun.status !== 'Succeeded';
    if (status === 'rules') return rules.some(hasViolation);
    if (status === 'missing') return rules.some((rule) => rule.isMissingRun);
    return true;
  });

  const totalItems = filtered.length;
  const totalPages = Math.max(1, Math.ceil(totalItems / jobsPageSize));
  const currentPage = Math.min(page, totalPages);
  const offset = (currentPage - 1) * jobsPageSize;
  const rulesRequired = status === 'rules' || status === 'missing';
  const loading = latest.loading || monitoring.loading;
  const failed = latest.failed || (rulesRequired && monitoring.failed);
  const refresh = () => setRevision((value) => value + 1);

  return (
    <>
      <dialog
        ref={dialog}
        className="server-detail-modal jobs-dialog"
        aria-labelledby="job-server-title"
        onCancel={(event) => { event.preventDefault(); onClose(); }}
        onClick={(event) => { if (event.target === event.currentTarget) onClose(); }}
      >
        <header className="server-detail-modal__header">
          <div>
            <span className="eyebrow">SQL Server Agent</span>
            <h2 id="job-server-title">{serverName}</h2>
            <p>{totalJobs > 0
              ? `${formatNumber(successfulJobs)} von ${formatNumber(totalJobs)} ${totalJobs === 1 ? 'Job' : 'Jobs'} erfolgreich beim letzten Lauf im Zeitraum`
              : 'Keine Läufe im gewählten Zeitraum'}</p>
          </div>
          <button className="icon-button" aria-label="Detailfenster schließen" onClick={onClose}><Icon name="close" size={19} /></button>
        </header>
        <div className="server-detail-modal__body" ref={body}>
          <div className="filter-bar jobs-filter-bar jobs-dialog-filters">
            <SearchInput value={search} placeholder="Job oder SQL-Instanz suchen …" onChange={(value) => { setSearch(value); setPage(1); }} />
            <label className="select-field">
              <span id="jobs-dialog-status-label">Jobstatus</span>
              <select aria-labelledby="jobs-dialog-status-label" value={status} onChange={(event) => { setStatus(event.target.value); setPage(1); }}>
                <option value="">Alle Status</option>
                <option value="successful">Erfolgreich</option>
                <option value="not-successful">Nicht erfolgreich</option>
                <option value="missing">Kein erwarteter Lauf</option>
                <option value="rules">Regelabweichung</option>
              </select>
            </label>
          </div>

          {monitoring.failed && !rulesRequired && (
            <div className="jobs-rule-error" role="alert">
              <span>Der Monitoringstatus konnte nicht geladen werden.</span>
              <button className="text-button" onClick={refresh}>Erneut versuchen</button>
            </div>
          )}
          {loading ? <LoadingState label="Jobdetails werden geladen" /> :
            failed ? <ErrorState retry={refresh} /> :
            totalItems > 0 ? (
              <>
                <div className="jobs-detail-list">
                  {filtered.slice(offset, offset + jobsPageSize).map((row) => (
                    <JobResult key={jobKey(row.job)} row={row} onOpen={() => setSelectedJob(row.job)} />
                  ))}
                </div>
                {totalPages > 1 && <Pagination page={currentPage} totalPages={totalPages} totalItems={totalItems} onChange={(nextPage) => {
                  setPage(nextPage);
                  body.current?.scrollTo({ top: 0 });
                }} />}
              </>
            ) : <div className="jobs-empty" role="status">Keine passenden Jobs gefunden.</div>}
        </div>
      </dialog>
      {selectedJob && <JobHistoryDialog job={selectedJob} historyDays={historyDays} onClose={() => setSelectedJob(null)} />}
    </>
  );
}

async function loadLatestRuns(server: string, historyDays: number, signal: AbortSignal): Promise<JobRun[]> {
  const pageSize = 100;
  const query = { server, historyDays, pageSize, includeMessage: false };
  const firstPage = await portalApi.jobRuns({ ...query, page: 1 }, signal);
  const runs = firstPage.items;
  for (let page = 2; page <= firstPage.totalPages; page += 1) {
    const next = await portalApi.jobRuns({ ...query, page }, signal);
    runs.push(...next.items);
  }
  return runs;
}

function JobResult({ row: { job, latestRun, rules }, onOpen }: { row: JobRow; onOpen: () => void }) {
  const hasFailedRule = rules.some(hasViolation);

  return (
    <article className="jobs-detail-result">
      <header>
        <div>
          <h3>
            <button className="jobs-job-link" type="button" aria-haspopup="dialog"
              aria-label={`Jobhistorie für ${job.jobName} auf ${job.sqlInstance}`} onClick={onOpen}>
              <span>{job.jobName}</span><Icon name="chevron-right" size={16} />
            </button>
          </h3>
          <p>{job.sqlInstance} · {latestRun
            ? `Letzter Lauf ${latestRun.startedAt ? formatDateTime(latestRun.startedAt) : 'unbekannt'}${latestRun.duration ? ` · ${latestRun.duration}` : ''}`
            : 'Kein Lauf im gewählten Historienzeitraum'}</p>
        </div>
        <div className="jobs-result-statuses">
          {latestRun && <StatusPill status={formatJobStatus(latestRun.status)} />}
          {rules.length > 0 && <StatusPill status={hasFailedRule ? 'Regelabweichung' : 'Monitoring OK'} tone={hasFailedRule ? 'danger' : 'success'} />}
          {rules.some((rule) => rule.isMissingRun) && <StatusPill status="Kein erwarteter Lauf" tone="warning" />}
        </div>
      </header>

      {rules.length > 0 && (
        <ul className="jobs-result-rules" aria-label="Monitoringregeln">
          {rules.map((rule, index) => (
            <li key={index} className={hasViolation(rule) ? 'jobs-rule--failed' : undefined}>
              <strong>{formatNumber(rule.monitoringWindowHours)} Std. · {formatNumber(rule.actualRuns)} {rule.actualRuns === 1 ? 'Lauf' : 'Läufe'} · {formatNumber(rule.successfulRuns)} erfolgreich · {formatNumber(rule.failedRuns)} fehlgeschlagen</strong>
              <p>{[
                rule.runCountStatus !== 'NotChecked' && `Laufanzahl ${rule.runCountStatus === 'OK' ? 'OK' : 'abweichend'} (${expectedRuns(rule)})`,
                rule.successStatus !== 'NotChecked' && `Erfolgsprüfung ${rule.successStatus === 'OK' ? 'OK' : 'fehlgeschlagen'}`,
              ].filter(Boolean).join(' · ')}</p>
              <p>{[rule.environment, rule.serverType, rule.description].filter(Boolean).join(' · ')}</p>
            </li>
          ))}
        </ul>
      )}
    </article>
  );
}

function expectedRuns({ expectedRuns, runCountCheckType }: JobMonitoringResult): string {
  if (expectedRuns === null) return 'Sollwert fehlt';
  return `Soll ${runCountCheckType === 'MINIMUM' ? 'mindestens' : 'genau'} ${formatNumber(expectedRuns)}`;
}

function hasViolation(rule: JobMonitoringResult): boolean {
  return rule.overallStatus === 'Failed';
}

function jobKey(job: JobIdentity): string {
  return JSON.stringify([job.sqlInstance, job.jobName.toLowerCase()]);
}
