import type {
  ConfigurationCheck,
  ConfigurationOverview,
  ListQuery,
  PagedResult,
  PortalDatabase,
  PortalOverview,
  PortalServer,
} from './types';

function toQueryString(query: ListQuery): string {
  const search = new URLSearchParams();
  Object.entries(query).forEach(([key, value]) => {
    if (value !== undefined && value !== '') {
      search.set(key, String(value));
    }
  });
  const value = search.toString();
  return value ? `?${value}` : '';
}

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    signal,
    headers: { Accept: 'application/json' },
  });

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}: ${response.statusText}`);
  }

  return (await response.json()) as T;
}

export const portalApi = {
  overview: (signal?: AbortSignal) =>
    getJson<PortalOverview>('/api/v1/overview', signal),
  servers: (query: ListQuery, signal?: AbortSignal) =>
    getJson<PagedResult<PortalServer>>(`/api/v1/servers${toQueryString(query)}`, signal),
  databases: (query: ListQuery, signal?: AbortSignal) =>
    getJson<PagedResult<PortalDatabase>>(`/api/v1/databases${toQueryString(query)}`, signal),
  configurationOverview: (signal?: AbortSignal) =>
    getJson<ConfigurationOverview>('/api/v1/configuration/overview', signal),
  configurationChecks: (query: ListQuery, signal?: AbortSignal) =>
    getJson<PagedResult<ConfigurationCheck>>(`/api/v1/configuration/checks${toQueryString(query)}`, signal),
};

