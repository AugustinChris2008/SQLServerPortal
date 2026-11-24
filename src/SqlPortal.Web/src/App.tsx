import { useEffect, useState } from 'react';
import { DashboardPage } from './DashboardPage';
import { AnalyticsPage } from './AnalyticsPage';
import { ConfigurationServerPage } from './ConfigurationServerPage';
import { DatabaseInventoryPage } from './DatabaseInventoryPage';
import { ServerInventoryPage } from './ServerInventoryPage';
import { RestoreTestsPage } from './RestoreTestsPage';
import { JobsPage } from './JobsPage';
import { Icon } from './ui';

const views = ['dashboard', 'analytics', 'servers', 'databases', 'configuration', 'restore-tests', 'jobs'] as const;
type View = typeof views[number];

export function App() {
  const [hash, setHash] = useState(() => window.location.hash);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [path, query] = hash.replace('#/', '').split('?');
  const view = views.find((value) => value === path) ?? 'dashboard';

  useEffect(() => {
    const handleHashChange = () => setHash(window.location.hash);
    window.addEventListener('hashchange', handleHashChange);
    return () => window.removeEventListener('hashchange', handleHashChange);
  }, []);

  function navigate(nextView: View, queryString = '') {
    window.location.hash = `/${nextView}${queryString}`;
    setHash(window.location.hash);
    setSidebarOpen(false);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  return (
    <div className="app-shell">
      <button className={`sidebar-backdrop${sidebarOpen ? ' sidebar-backdrop--visible' : ''}`} aria-label="Navigation schließen" onClick={() => setSidebarOpen(false)} />
      <aside className={`sidebar${sidebarOpen ? ' sidebar--open' : ''}`}>
        <div className="brand">
          <span className="brand__mark"><DatabaseMark /></span>
          <div><strong>SQL Portal</strong><span>Operations Center</span></div>
          <button className="sidebar__close" aria-label="Navigation schließen" onClick={() => setSidebarOpen(false)}><Icon name="close" /></button>
        </div>

        <nav aria-label="Hauptnavigation">
          <span className="nav-label">Übersicht</span>
          <NavButton active={view === 'dashboard'} icon="dashboard" label="Dashboard" onClick={() => navigate('dashboard')} />
          <NavButton active={view === 'analytics'} icon="chart" label="Analysen" onClick={() => navigate('analytics')} />
          <span className="nav-label nav-label--spaced">Infrastruktur</span>
          <NavButton active={view === 'servers'} icon="server" label="SQL Server" onClick={() => navigate('servers')} />
          <NavButton active={view === 'databases'} icon="database" label="Datenbanken" onClick={() => navigate('databases')} />
          <NavButton active={view === 'configuration'} icon="settings" label="Konfiguration" onClick={() => navigate('configuration')} />
          <NavButton active={view === 'restore-tests'} icon="backup" label="Restoretests" onClick={() => navigate('restore-tests')} />
          <NavButton active={view === 'jobs'} icon="activity" label="Jobs" onClick={() => navigate('jobs')} />
        </nav>

        <div className="sidebar__footer">
          <div className="read-only-card"><span><Icon name="check" size={16} /></span><div><strong>Read-only</strong><small>Keine Änderungen an SQL Servern</small></div></div>
          <div className="profile"><span>DB</span><div><strong>DBA Team</strong><small>SQL Operations</small></div><i /></div>
        </div>
      </aside>

      <div className="workspace">
        <header className="mobile-header">
          <button aria-label="Navigation öffnen" onClick={() => setSidebarOpen(true)}><Icon name="menu" /></button>
          <div className="mobile-brand"><span className="brand__mark"><DatabaseMark /></span><strong>SQL Portal</strong></div>
          <span className="mobile-status" title="Portal erreichbar" />
        </header>
        <main>
          {view === 'dashboard' && <DashboardPage onNavigate={navigate} />}
          {view === 'analytics' && <AnalyticsPage />}
          {view === 'servers' && <ServerInventoryPage />}
          {view === 'databases' && <DatabaseInventoryPage />}
          {view === 'configuration' && <ConfigurationServerPage />}
          {view === 'restore-tests' && <RestoreTestsPage />}
          {view === 'jobs' && <JobsPage key={hash} initialFilters={new URLSearchParams(query)} />}
        </main>
        <footer className="page-footer"><span>SQL Operations Portal</span><span>Scan-Daten · Keine Liveüberwachung · Nur-Lese-Zugriff</span></footer>
      </div>
    </div>
  );
}

function NavButton({ active, icon, label, onClick }: { active: boolean; icon: 'dashboard' | 'chart' | 'server' | 'database' | 'settings' | 'backup' | 'activity'; label: string; onClick: () => void }) {
  return <button className={`nav-item${active ? ' nav-item--active' : ''}`} onClick={onClick}><Icon name={icon} /><span>{label}</span></button>;
}

function DatabaseMark() {
  return <svg viewBox="0 0 32 32" aria-hidden="true"><ellipse cx="16" cy="8" rx="10" ry="4"/><path d="M6 8v8c0 2.2 4.5 4 10 4s10-1.8 10-4V8M6 16v8c0 2.2 4.5 4 10 4s10-1.8 10-4v-8"/></svg>;
}

