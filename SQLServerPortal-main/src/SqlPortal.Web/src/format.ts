export function formatNumber(value: number, maximumFractionDigits = 0): string {
  return new Intl.NumberFormat('de-DE', { maximumFractionDigits }).format(value);
}

export function formatStorage(gigabytes: number): string {
  if (gigabytes >= 1024) {
    return `${formatNumber(gigabytes / 1024, 1)} TB`;
  }
  return `${formatNumber(gigabytes, 1)} GB`;
}

export function formatPercent(value: number | null): string {
  return value === null ? '–' : `${formatNumber(value, 0)} %`;
}

export function formatDateTime(value: string | null): string {
  if (!value) return 'Nicht verfügbar';
  return new Intl.DateTimeFormat('de-DE', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}

export function relativeTime(value: string | null): string {
  if (!value) return 'unbekannt';
  const differenceMinutes = Math.round((new Date(value).getTime() - Date.now()) / 60_000);
  const formatter = new Intl.RelativeTimeFormat('de-DE', { numeric: 'auto' });

  if (Math.abs(differenceMinutes) < 60) {
    return formatter.format(differenceMinutes, 'minute');
  }

  const hours = Math.round(differenceMinutes / 60);
  if (Math.abs(hours) < 24) {
    return formatter.format(hours, 'hour');
  }

  return formatter.format(Math.round(hours / 24), 'day');
}

export function statusTone(status: string): 'success' | 'warning' | 'danger' | 'neutral' {
  const normalized = status.toLowerCase();
  if (normalized === 'gesund' || normalized === 'konform') return 'success';
  if (normalized === 'warnung') return 'warning';
  if (normalized === 'kritisch' || normalized === 'abweichung') return 'danger';
  return 'neutral';
}

