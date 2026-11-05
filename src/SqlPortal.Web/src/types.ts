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
  collectedAtUtc: string | null;
  description: string | null;
}

export interface ServerFilterOptions {
  environments: string[];
  versions: string[];
  editions: string[];
}

export interface ServerParameter {
  name: string;
  value: string | null;
}

export interface ServerDetail {
  server: PortalServer;
  parameters: ServerParameter[];
  databases: PortalDatabase[];
  configurationChecks: ConfigurationCheck[];
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
  compatibilityLevel: number | null;
  sizeGb: number | null;
  dataUsedPercent: number | null;
  logUsedPercent: number | null;
  lastFullBackupAtUtc: string | null;
  lastLogBackupAtUtc: string | null;
  owner: string;
  collectedAtUtc: string | null;
}

export interface DatabaseDetail {
  database: PortalDatabase;
  parameters: ServerParameter[];
  configurationChecks: ConfigurationCheck[];
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
  sizeGb: number | null;
  logUsedPercent: number | null;
  lastFullBackupAtUtc: string | null;
  collectedAtUtc: string | null;
}

export interface PortalOverview {
  sourceMode: 'Demo' | 'SqlServer';
  generatedAtUtc: string;
  oldestCollectedAtUtc: string | null;
  metrics: OverviewMetrics;
  environments: EnvironmentSummary[];
  focusServers: PortalServer[];
  databaseAlerts: DatabaseAlert[];
  supportLifecycle: SupportLifecycleSummary;
}

export interface SupportLifecycleSummary {
  mainstreamSupportServers: number;
  extendedSupportServers: number;
  unsupportedServers: number;
  unknownServers: number;
  versions: SupportLifecycleVersion[];
}

export interface SupportLifecycleVersion {
  version: string;
  serverCount: number;
  endOfMainstreamSupport: string | null;
  endOfExtendedSupport: string | null;
  status: 'Mainstream Support' | 'Extended Support' | 'Nicht unterstützt' | 'Unbekannt' | string;
}

export interface AnalyticsMetrics {
  totalCpuCores: number;
  licensableCpuCores: number;
  sqlLicenseCount: number;
}

export interface AnalyticsDistributionItem {
  label: string;
  serverCount: number;
}

export interface AnalyticsOverview {
  sourceMode: 'Demo' | 'SqlServer';
  generatedAtUtc: string;
  metrics: AnalyticsMetrics;
  editions: AnalyticsDistributionItem[];
  versions: AnalyticsDistributionItem[];
}

export interface ConfigurationCheck {
  id: number;
  serverId: number;
  serverName: string;
  instanceName: string | null;
  environment: string;
  category: string;
  parameterName: string;
  expectedValue: string;
  actualValue: string | null;
  databaseName: string | null;
  objectName: string | null;
  configuredValue: string | null;
  runningValue: string | null;
  isCompliant: boolean;
  complianceState: string | null;
  description: string | null;
  unit: string | null;
  status: 'Konform' | 'Abweichung' | 'Nicht ermittelt' | string;
  deviation: string | null;
  collectedAtUtc: string;
}

export interface ConfigurationMetrics {
  totalChecks: number;
  definedChecks: number;
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

export interface RestoreTestMetrics {
  totalDatabases: number;
  successfullyTestedDatabases: number;
  remainingDatabases: number;
  currentFailureDatabases: number;
  skippedDatabases: number;
  notTestedDatabases: number;
  totalAttemptsWithinPeriod: number;
  neverSelectedDatabases: number;
  selectedOnceDatabases: number;
  selectedMultipleTimesDatabases: number;
  repeatedAttempts: number;
  coveragePercent: number;
}

export interface RestoreFailureCategory {
  category: string;
  databaseCount: number;
}

export interface RestoreTestOverview {
  sourceMode: 'Demo' | 'SqlServer';
  generatedAtUtc: string;
  period: string;
  windowStartUtc: string | null;
  metrics: RestoreTestMetrics;
  failureCategories: RestoreFailureCategory[];
}

export interface RestoreTestDatabase {
  id: number;
  sourceServer: string;
  databaseName: string;
  periodStatus: 'Erfolgreich' | 'Fehlgeschlagen' | 'Übersprungen' | 'Nicht getestet' | string;
  coveredWithinPeriod: boolean;
  selectionStatus: 'Nie wiederhergestellt' | 'Einmal wiederhergestellt' | 'Mehrfach wiederhergestellt' | string;
  attemptCountWithinPeriod: number;
  successfulAttemptCountWithinPeriod: number;
  undatedAttemptCount: number;
  latestAttemptStatus: string;
  failureCategory: string | null;
  testServer: string | null;
  sizeGb: number | null;
  backupFileExists: boolean | null;
  restoreResult: string | null;
  dbccResult: string | null;
  latestAttemptAtUtc: string | null;
  lastSuccessfulAtUtc: string | null;
  restoreElapsed: string | null;
  dbccElapsed: string | null;
  fullDateUtc: string | null;
  fullPath: string | null;
  diffDateUtc: string | null;
  diffPath: string | null;
  logDates: string | null;
  logPaths: string | null;
}

export interface RestoreTestHistoryEntry {
  sequence: number;
  status: string;
  failureCategory: string | null;
  withinPeriod: boolean;
  testServer: string | null;
  sizeGb: number | null;
  backupFileExists: boolean | null;
  restoreResult: string | null;
  dbccResult: string | null;
  attemptAtUtc: string | null;
  restoreElapsed: string | null;
  dbccElapsed: string | null;
  fullDateUtc: string | null;
  fullPath: string | null;
  diffDateUtc: string | null;
  diffPath: string | null;
  logDates: string | null;
  logPaths: string | null;
}

export interface RestoreTestHistoryDetail {
  database: RestoreTestDatabase;
  history: RestoreTestHistoryEntry[];
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
  selection?: string;
  period?: string;
  environment?: string;
  category?: string;
  version?: string;
  edition?: string;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}

