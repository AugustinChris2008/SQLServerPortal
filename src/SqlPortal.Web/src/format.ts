export function formatNumber(value: number, maximumFractionDigits = 0): string {
  return new Intl.NumberFormat('de-DE', { maximumFractionDigits }).format(value);
}

export function formatStorage(gigabytes: number): string {
  const formatValue = (value: number) => new Intl.NumberFormat('de-DE', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);

  if (gigabytes >= 1024) {
    return `${formatValue(gigabytes / 1024)} TB`;
  }
  if (gigabytes >= 1) {
    return `${formatValue(gigabytes)} GB`;
  }
  return `${formatValue(gigabytes * 1024)} MB`;
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

export function formatDate(value: string | null): string {
  if (!value) return 'Nicht hinterlegt';
  return new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' }).format(new Date(value));
}

export function formatRemainingSupport(value: string | null): string {
  if (!value) return 'Keine Frist hinterlegt';

  const endDate = new Date(value);
  if (Number.isNaN(endDate.getTime())) return 'Ungültiges Datum';

  const today = new Date();
  const todayUtc = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate());
  const endUtc = Date.UTC(endDate.getFullYear(), endDate.getMonth(), endDate.getDate());
  const differenceDays = Math.round((endUtc - todayUtc) / 86_400_000);

  if (differenceDays === 0) return 'Endet heute';

  const absoluteDays = Math.abs(differenceDays);
  const years = Math.floor(absoluteDays / 365.2425);
  const afterYears = absoluteDays - Math.round(years * 365.2425);
  const months = Math.floor(afterYears / 30.436875);
  const days = Math.max(0, Math.round(afterYears - months * 30.436875));
  const parts: string[] = [];

  if (years > 0) parts.push(`${years} ${years === 1 ? 'Jahr' : 'Jahre'}`);
  if (months > 0 && parts.length < 2) parts.push(`${months} ${months === 1 ? 'Monat' : 'Monate'}`);
  if (days > 0 && parts.length < 2) parts.push(`${days} ${days === 1 ? 'Tag' : 'Tage'}`);

  const duration = parts.length > 0 ? parts.join(', ') : 'weniger als 1 Tag';
  return differenceDays > 0 ? `Noch ${duration}` : `Seit ${duration} beendet`;
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
  if (normalized === 'gesund' || normalized === 'konform' || normalized === 'erfolgreich') return 'success';
  if (normalized === 'warnung' || normalized === 'übersprungen') return 'warning';
  if (normalized === 'kritisch' || normalized === 'abweichung' || normalized === 'fehlgeschlagen') return 'danger';
  return 'neutral';
}

