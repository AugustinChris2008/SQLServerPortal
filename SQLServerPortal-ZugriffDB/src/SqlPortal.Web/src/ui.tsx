import type { ReactNode } from 'react';
import { statusTone } from './format';

type IconName =
  | 'dashboard'
  | 'server'
  | 'database'
  | 'backup'
  | 'search'
  | 'refresh'
  | 'alert'
  | 'check'
  | 'clock'
  | 'chevron-left'
  | 'chevron-right'
  | 'menu'
  | 'close'
  | 'activity'
  | 'storage'
  | 'settings';

export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  const paths: Record<IconName, ReactNode> = {
    dashboard: <><rect x="3" y="3" width="7" height="7" rx="2"/><rect x="14" y="3" width="7" height="7" rx="2"/><rect x="3" y="14" width="7" height="7" rx="2"/><rect x="14" y="14" width="7" height="7" rx="2"/></>,
    server: <><rect x="3" y="4" width="18" height="6" rx="2"/><rect x="3" y="14" width="18" height="6" rx="2"/><path d="M7 7h.01M7 17h.01M11 7h7M11 17h7"/></>,
    database: <><ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v6c0 1.7 3.6 3 8 3s8-1.3 8-3V5M4 11v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6"/></>,
    backup: <><path d="M3 12a9 9 0 1 0 3-6.7L3 8"/><path d="M3 3v5h5M12 7v5l3 2"/></>,
    search: <><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></>,
    refresh: <><path d="M20 11a8 8 0 0 0-14.9-4M4 4v5h5M4 13a8 8 0 0 0 14.9 4M20 20v-5h-5"/></>,
    alert: <><path d="M10.3 3.7 2.8 17a2 2 0 0 0 1.7 3h15a2 2 0 0 0 1.7-3L13.7 3.7a2 2 0 0 0-3.4 0Z"/><path d="M12 9v4M12 17h.01"/></>,
    check: <path d="m5 12 4 4L19 6"/>,
    clock: <><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></>,
    'chevron-left': <path d="m15 18-6-6 6-6"/>,
    'chevron-right': <path d="m9 18 6-6-6-6"/>,
    menu: <path d="M4 7h16M4 12h16M4 17h16"/>,
    close: <path d="m6 6 12 12M18 6 6 18"/>,
    activity: <path d="M3 12h4l2-7 4 14 2-7h6"/>,
    storage: <><path d="M4 6h16v12H4z"/><path d="M8 10h8M8 14h5"/></>,
    settings: <><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H2.8v-4H3a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1A1.7 1.7 0 0 0 9 4.6 1.7 1.7 0 0 0 10 3v-.2h4V3a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1Z"/></>,
  };

  return (
    <svg
      className="icon"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {paths[name]}
    </svg>
  );
}

export function StatusPill({ status }: { status: string }) {
  const tone = statusTone(status);
  return <span className={`status-pill status-pill--${tone}`}><span />{status}</span>;
}

export function EnvironmentTag({ value }: { value: string }) {
  const normalized = value.trim().toLowerCase();
  const tone = normalized === 'prod'
    ? 'prod'
    : normalized === 'cons'
      ? 'cons'
      : normalized === 'dev' || normalized === 'test'
        ? 'dev'
        : 'neutral';
  return <span className={`environment-tag environment-tag--${tone}`}>{value}</span>;
}

export function Meter({ value, tone = 'default' }: { value: number | null; tone?: 'default' | 'warning' | 'danger' }) {
  if (value === null) return <span className="muted">Nicht verfügbar</span>;
  const effectiveTone = value >= 90 ? 'danger' : value >= 75 ? 'warning' : tone;
  return (
    <div className="meter" title={`${Math.round(value)} %`}>
      <div className={`meter__fill meter__fill--${effectiveTone}`} style={{ width: `${Math.min(value, 100)}%` }} />
      <span>{Math.round(value)} %</span>
    </div>
  );
}

export function PageTitle({ eyebrow, title, description, action }: { eyebrow: string; title: string; description: string; action?: ReactNode }) {
  return (
    <header className="page-title">
      <div>
        <span className="eyebrow">{eyebrow}</span>
        <h1>{title}</h1>
        <p>{description}</p>
      </div>
      {action}
    </header>
  );
}

export function LoadingState({ label = 'Daten werden geladen' }: { label?: string }) {
  return (
    <div className="state-card" role="status">
      <span className="spinner" />
      <strong>{label}</strong>
      <span>Das SQL Operations Portal aktualisiert die Ansicht.</span>
    </div>
  );
}

export function ErrorState({ retry }: { retry: () => void }) {
  return (
    <div className="state-card state-card--error" role="alert">
      <span className="state-card__icon"><Icon name="alert" size={24} /></span>
      <strong>Daten konnten nicht geladen werden</strong>
      <span>Prüfe die API und die konfigurierte Datenquelle.</span>
      <button className="button button--secondary" onClick={retry}>Erneut versuchen</button>
    </div>
  );
}

export function Pagination({ page, totalPages, totalItems, onChange }: { page: number; totalPages: number; totalItems: number; onChange: (page: number) => void }) {
  return (
    <div className="pagination">
      <span>{totalItems} Einträge · Seite {page} von {totalPages}</span>
      <div>
        <button aria-label="Vorherige Seite" disabled={page <= 1} onClick={() => onChange(page - 1)}><Icon name="chevron-left" size={18} /></button>
        <button aria-label="Nächste Seite" disabled={page >= totalPages} onClick={() => onChange(page + 1)}><Icon name="chevron-right" size={18} /></button>
      </div>
    </div>
  );
}

export function SearchInput({ value, onChange, placeholder }: { value: string; onChange: (value: string) => void; placeholder: string }) {
  return (
    <label className="search-input">
      <Icon name="search" size={18} />
      <span className="sr-only">Suche</span>
      <input value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} />
      {value && <button type="button" className="search-input__clear" aria-label="Suche löschen" onClick={() => onChange('')}><Icon name="close" size={14} /></button>}
    </label>
  );
}

export function SortButton({ label, field, activeField, direction, onChange }: { label: string; field: string; activeField: string; direction: 'asc' | 'desc'; onChange: (field: string, direction: 'asc' | 'desc') => void }) {
  const active = field === activeField;
  return (
    <button
      className={`sort-button${active ? ' sort-button--active' : ''}`}
      onClick={() => onChange(field, active && direction === 'asc' ? 'desc' : 'asc')}
    >
      {label}<span aria-hidden="true">{active ? (direction === 'asc' ? '↑' : '↓') : '↕'}</span>
    </button>
  );
}

