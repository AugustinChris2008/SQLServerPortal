import { useCallback, useMemo, useRef, useState } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatJobStatus, formatNumber } from './format';
import { useDebouncedValue, useJobData, useModalDialog } from './hooks';
import { JobHistoryDialog } from './JobHistoryDialog';
import type { JobRuleViolation, JobRun, JobServerSummary } from './types';
import { ErrorState, Icon, LoadingState, Pagination, SearchInput, StatusPill } from './ui';

const jobsPageSize = 20;

export function JobServerDialog({ server, initialHistory, initialStatus, onClose }: {
  server: JobServerSummary;
  initialHistory: boolean;
  initialStatus: string;
  onClose: () => void;
}) {
  const dialog = useModalDialog();
  const body = useRef<HTMLDivElement>(null);
  const [history, setHistory] = useState(initialHistory);
  const [selectedJob, setSelectedJob] = useState<JobRun | null>(null);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState(initialStatus);
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const debouncedSearch = useDebouncedValue(search);
  const { serverName, successfulJobs, totalJobs } = server;

  const fetchLatest = useCallback((signal: AbortSignal) => loadLatestRuns(serverName, signal), [serverName]);
  const fetchViolations = useCallback((signal: AbortSignal) => portalApi.jobViolations(serverName, signal), [serverName]);
  const fetchHistory = useCallback((signal: AbortSignal) => portalApi.jobRuns({
    server: serverName, history: true, status, search: debouncedSearch, page, pageSize: jobsPageSize, includeMessage: false,
  }, signal), [serverName, status, debouncedSearch, page]);
  const latest = useJobData(history ? null : fetchLatest, revision);
  const violations = useJobData(history ? null : fetchViolations, revision);
  const pastRuns = useJobData(history ? fetchHistory : null, revision);

  const violationsByJob = useMemo(() => {
    const grouped = new Map<string, JobRuleViolation[]>();
    for (const rule of violations.data ?? []) {
      const key = jobKey(rule);
      const rules = grouped.get(key);
      if (rules) rules.push(rule);
      else grouped.set(key, [rule]);
    }
    return grouped;
  }, [violations.data]);

  const latestFiltered = useMemo(() => {
    const normalized = search.trim().toLocaleLowerCase();
    return (latest.data ?? []).filter((run) => {
      if (!run.jobName.toLocaleLowerCase().includes(normalized) && !run.sqlInstance.toLocaleLowerCase().includes(normalized)) return false;
      if (status === 'successful') return run.status === 'Succeeded';
      if (status === 'not-successful') return run.status !== 'Succeeded';
      if (status === 'rules') return violationsByJob.has(jobKey(run));
      if (status === 'missing') return (violationsByJob.get(jobKey(run)) ?? []).some(isMissingRun);
      return true;
    });
  }, [latest.data, search, status, violationsByJob]);

  const totalItems = history ? pastRuns.data?.totalItems ?? 0 : latestFiltered.length;
  const totalPages = history ? pastRuns.data?.totalPages ?? 1 : Math.max(1, Math.ceil(totalItems / jobsPageSize));
  const currentPage = Math.min(page, totalPages);
  const offset = (currentPage - 1) * jobsPageSize;
  const runs = history ? pastRuns.data?.items ?? [] : latestFiltered.slice(offset, offset + jobsPageSize);
  const rulesRequired = status === 'rules' || status === 'missing';
  const loading = history ? pastRuns.loading : latest.loading || violations.loading;
  const failed = history ? pastRuns.failed : latest.failed || (rulesRequired && violations.failed);
  const refresh = () => setRevision((value) => value + 1);
  const ruleStatusText = status === 'rules' ? 'mit Regelabweichungen' : status === 'missing' ? 'ohne erwarteten Lauf' : '';

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
            <p>{formatNumber(successfulJobs)} von {formatNumber(totalJobs)} {totalJobs === 1 ? 'Job' : 'Jobs'} erfolgreich beim letzten Lauf</p>
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
                {!history && <option value="missing">Kein erwarteter Lauf</option>}
                {!history && <option value="rules">Regelabweichung</option>}
              </select>
            </label>
            <label className="select-field">
              <span id="jobs-dialog-history-label">Jobläufe</span>
              <select aria-labelledby="jobs-dialog-history-label" value={history ? 'history' : 'latest'} onChange={(event) => {
                setHistory(event.target.value === 'history');
                if (rulesRequired) setStatus('');
                setPage(1);
              }}>
                <option value="latest">Letzter Lauf je Job</option>
                <option value="history">Alle Läufe</option>
              </select>
            </label>
          </div>

          {!history && violations.failed && !rulesRequired && (
            <div className="jobs-rule-error" role="alert">
              <span>Regelabweichungen konnten nicht geladen werden.</span>
              <button className="text-button" onClick={refresh}>Erneut versuchen</button>
            </div>
          )}
          {loading ? <LoadingState label="Jobdetails werden geladen" /> :
            failed ? <ErrorState retry={refresh} /> :
            runs.length > 0 ? (
              <>
                <div className="jobs-detail-list">
                  {runs.map((run, index) => (
                    <JobResult key={`${jobKey(run)}-${run.startedAt}-${index}`} run={run}
                      rules={history ? [] : violationsByJob.get(jobKey(run)) ?? []} history={history}
                      onOpen={() => setSelectedJob(run)} />
                  ))}
                </div>
                {totalPages > 1 && <Pagination page={currentPage} totalPages={totalPages} totalItems={totalItems} onChange={(nextPage) => {
                  setPage(nextPage);
                  body.current?.scrollTo({ top: 0 });
                }} />}
              </>
            ) : <div className="jobs-empty" role="status">{ruleStatusText ? `Keine passenden Jobs ${ruleStatusText}.` : 'Keine passenden Jobläufe gefunden.'}</div>}
        </div>
      </dialog>
      {selectedJob && <JobHistoryDialog job={selectedJob} onClose={() => setSelectedJob(null)} />}
    </>
  );
}

async function loadLatestRuns(server: string, signal: AbortSignal): Promise<JobRun[]> {
  const pageSize = 100;
  const firstPage = await portalApi.jobRuns({ server, page: 1, pageSize, includeMessage: false }, signal);
  const runs = firstPage.items;
  for (let page = 2; page <= firstPage.totalPages; page += 1) {
    const next = await portalApi.jobRuns({ server, page, pageSize, includeMessage: false }, signal);
    runs.push(...next.items);
  }
  return runs;
}

function JobResult({ run, rules, history, onOpen }: {
  run: JobRun;
  rules: JobRuleViolation[];
  history: boolean;
  onOpen: () => void;
}) {
  const missedRun = rules.some(isMissingRun);

  return (
    <article className="jobs-detail-result">
      <header>
        <div>
          <h3>
            <button className="jobs-job-link" type="button" aria-haspopup="dialog"
              aria-label={`Jobhistorie für ${run.jobName} auf ${run.sqlInstance}`} onClick={onOpen}>
              <span>{run.jobName}</span><Icon name="chevron-right" size={16} />
            </button>
          </h3>
          <p>{run.sqlInstance} · {history ? 'Start' : 'Letzter Lauf'} {run.startedAt ? formatDateTime(run.startedAt) : 'unbekannt'}{run.duration && ` · ${run.duration}`}</p>
        </div>
        <div className="jobs-result-statuses">
          <StatusPill status={formatJobStatus(run.status)} />
          {rules.length > 0 && <StatusPill status="Regelabweichung" tone="danger" />}
          {missedRun && <StatusPill status="Kein Lauf in 24 Std." tone="warning" />}
        </div>
      </header>

      {rules.length > 0 && (
        <ul className="jobs-result-rules" aria-label="Regelabweichungen der letzten 24 Stunden">
          {rules.map((rule, index) => (
            <li key={index}>
              {rule.runCountViolated && <strong>{formatNumber(rule.actualRuns)} Läufe in 24 Std. · {expectedRuns(rule)}</strong>}
              {rule.successViolated && <strong>{formatNumber(rule.unsuccessfulRuns)} nicht erfolgreich in 24 Std.</strong>}
              <p>{[rule.environment, rule.serverType, rule.description].filter(Boolean).join(' · ')}</p>
            </li>
          ))}
        </ul>
      )}
    </article>
  );
}

function expectedRuns({ expectedRuns24h, runCountCheckType }: JobRuleViolation): string {
  if (expectedRuns24h === null) return 'Sollwert fehlt';
  return `Soll ${runCountCheckType === 'MINIMUM' ? 'mindestens' : 'genau'} ${formatNumber(expectedRuns24h)}`;
}

function isMissingRun(rule: JobRuleViolation): boolean {
  return rule.runCountViolated && rule.actualRuns === 0 && (rule.expectedRuns24h ?? 0) > 0;
}

function jobKey(job: Pick<JobRun, 'sqlInstance' | 'jobName'>): string {
  return JSON.stringify([job.sqlInstance, job.jobName]);
}
