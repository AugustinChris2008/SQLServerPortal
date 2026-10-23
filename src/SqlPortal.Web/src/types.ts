export type Status = 'Warnung' | 'Gesund' | 'Kritisch' | string;

export interface PortalServer {
  id: number;
  name: string;
  instanceName: string;
  environment: string;
  version: string;
  edition: string;
  databaseCount: number;
  memoryGb: number | null;
  collectedAtUtc: string;
}

export interface PortalDatabase {
  id: number;
  serverId: number;
  serverName: string;
  name: string;
  environment: string;
  status: Status;
  state: string;
  recoveryModel: string;
  compatibilityLevel: number;
  sizeGb: number;
  dataUsedPercent: number | null;
  logUsedPercent: number | null;
  lastFullBackupAtUtc: string | null;
  lastLogBackupAtUtc: string | null;
  owner: string;
  collectedAtUtc: string;
}

export interface OverviewMetrics {
  totalServers: number;
  recentServers: number;
  weekOldServers: number;
  olderServers: number;
  totalDatabases: number;
  healthyDatabases: number;
  attentionDatabases: number;
  totalSizeGb: number;
  totalMemoryGb: number;
}

export interface EnvironmentSummary {
  environment: string;
  serverCount: number;
  databaseCount: number;
}

export interface DatabaseAlert {
  id: number;
  serverName: string;
  name: string;
  environment: string;
  status: Status;
  sizeGb: number;
  logUsedPercent: number | null;
  lastFullBackupAtUtc: string | null;
  collectedAtUtc: string;
}

export interface PortalOverview {
  sourceMode: 'Demo' | 'SqlServer';
  generatedAtUtc: string;
  oldestCollectedAtUtc: string | null;
  metrics: OverviewMetrics;
  environments: EnvironmentSummary[];
  focusServers: PortalServer[];
  databaseAlerts: DatabaseAlert[];
}

export interface ConfigurationCheck {
  id: number;
  serverId: number;
  serverName: string;
  environment: string;
  category: string;
  parameterName: string;
  expectedValue: string;
  actualValue: string | null;
  unit: string | null;
  status: 'Konform' | 'Abweichung' | 'Nicht ermittelt' | string;
  deviation: string | null;
  collectedAtUtc: string;
}

export interface ConfigurationMetrics {
  totalChecks: number;
  compliantChecks: number;
  driftedChecks: number;
  unknownChecks: number;
  compliancePercent: number;
}

export interface ServerConfigurationSummary {
  serverId: number;
  serverName: string;
  environment: string;
  totalChecks: number;
  compliantChecks: number;
  driftedChecks: number;
  unknownChecks: number;
  compliancePercent: number;
  collectedAtUtc: string;
}

export interface ConfigurationOverview {
  sourceMode: 'Demo' | 'SqlServer';
  generatedAtUtc: string;
  metrics: ConfigurationMetrics;
  categories: string[];
  servers: ServerConfigurationSummary[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface ListQuery {
  search?: string;
  status?: string;
  environment?: string;
  category?: string;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}

