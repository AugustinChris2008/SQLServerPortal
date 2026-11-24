import { useCallback, useRef, useState } from 'react';
import { portalApi } from './api';
import { formatDateTime, formatJobStatus, formatNumber, statusTone } from './format';
import { useJobData, useModalDialog } from './hooks';
import type { JobRun } from './types';
import { ErrorState, Icon, LoadingState, Pagination, StatusPill } from './ui';

const historyPageSize = 20;

export function JobHistoryDialog({ job, onClose }: { job: JobRun; onClose: () => void }) {
  const dialog = useModalDialog();
  const body = useRef<HTMLDivElement>(null);
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const { serverName, sqlInstance, jobName } = job;
  const fetchHistory = useCallback((signal: AbortSignal) => portalApi.jobRuns({
    server: serverName, sqlInstance, jobName, history: true, page, pageSize: historyPageSize,
  }, signal), [serverName, sqlInstance, jobName, page]);
  const history = useJobData(fetchHistory, revision);

  function changePage(nextPage: number) {
    setPage(nextPage);
    body.current?.scrollTo({ top: 0 });
  }

  return (
    <dialog ref={dialog} className="server-detail-modal jobs-dialog jobs-history-dialog" aria-labelledby="job-history-title"
      onCancel={(event) => { event.preventDefault(); onClose(); }}
      onClick={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <header className="server-detail-modal__header">
        <div>
          <span className="eyebrow">Jobhistorie</span>
          <h2 id="job-history-title">{jobName}</h2>
          <p>{sqlInstance}</p>
        </div>
        <button className="icon-button" type="button" aria-label="Jobhistorie schließen" onClick={onClose}><Icon name="close" size={19} /></button>
      </header>
      <div className="server-detail-modal__body" ref={body}>
        {history.loading ? <LoadingState label="Jobhistorie wird geladen" /> :
          history.failed ? <ErrorState retry={() => setRevision((value) => value + 1)} /> :
          history.data?.items.length ? (
            <section className="restore-history" aria-labelledby="job-history-runs">
              <header>
                <h3 id="job-history-runs">Erfasste Jobläufe</h3>
                <strong>{formatNumber(history.data.totalItems)} {history.data.totalItems === 1 ? 'Lauf' : 'Läufe'}</strong>
              </header>
              <div className="restore-history-list">
                {history.data.items.map((run, index) => (
                  <JobHistoryEntry key={`${history.data?.page}-${index}`} run={run} />
                ))}
              </div>
              {history.data.totalPages > 1 && <Pagination page={history.data.page} totalPages={history.data.totalPages}
                totalItems={history.data.totalItems} onChange={changePage} />}
            </section>
          ) : <div className="jobs-empty" role="status">Für diesen Job sind keine Läufe erfasst.</div>}
      </div>
    </dialog>
  );
}

function JobHistoryEntry({ run }: { run: JobRun }) {
  const status = formatJobStatus(run.status);

  return (
    <article className={`restore-history-entry restore-history-entry--${statusTone(status)}`}>
      <span className="restore-history-entry__marker" aria-hidden="true" />
      <div className="restore-history-entry__content">
        <header>
          <div>
            <strong>{run.startedAt ? <time dateTime={run.startedAt}>{formatDateTime(run.startedAt)}</time> : 'Startzeit unbekannt'}</strong>
            {(run.endedAt || run.duration) && (
              <small>
                {run.endedAt && <>Ende <time dateTime={run.endedAt}>{formatDateTime(run.endedAt)}</time></>}
                {run.endedAt && run.duration && ' · '}
                {run.duration && `Dauer ${run.duration}`}
              </small>
            )}
          </div>
          <StatusPill status={status} />
        </header>
        {run.message?.trim() && <div className="jobs-agent-message"><strong>Agent-Meldung</strong><p>{run.message}</p></div>}
      </div>
    </article>
  );
}
