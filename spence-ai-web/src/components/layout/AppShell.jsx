import { NavLink, Outlet } from 'react-router-dom';

const navItems = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/upload', label: 'Upload PDF' },
  { to: '/expenses', label: 'Expenses' },
  { to: '/settings', label: 'Settings' },
];

function navClassName({ isActive }) {
  return [
    'flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors',
    isActive
      ? 'bg-teal-700 text-white shadow-sm'
      : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900',
  ].join(' ');
}

export default function AppShell() {
  return (
    <div className="dashboard-shell flex min-h-screen">
      <aside className="hidden w-64 shrink-0 border-r border-slate-200 bg-white/90 backdrop-blur md:flex md:flex-col">
        <div className="border-b border-slate-200 px-6 py-6">
          <p className="text-xs font-semibold uppercase tracking-wider text-teal-700">
            Spence AI
          </p>
          <h1 className="mt-1 text-xl font-bold text-slate-900">Expense Tracker</h1>
          <p className="mt-1 text-sm text-slate-500">Intelligent finance dashboard</p>
        </div>
        <nav className="flex flex-1 flex-col gap-1 p-4">
          {navItems.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.end} className={navClassName}>
              {item.label}
            </NavLink>
          ))}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="border-b border-slate-200 bg-white/90 px-4 py-4 backdrop-blur md:hidden">
          <p className="text-xs font-semibold uppercase tracking-wider text-teal-700">
            Spence AI
          </p>
          <nav className="mt-3 flex gap-2 overflow-x-auto">
            {navItems.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  [
                    'whitespace-nowrap rounded-full px-3 py-1.5 text-sm font-medium',
                    isActive
                      ? 'bg-teal-700 text-white'
                      : 'bg-slate-100 text-slate-600',
                  ].join(' ')
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>
        </header>

        <main className="flex-1 overflow-auto p-4 sm:p-6 lg:p-8">
          <div className="mx-auto max-w-7xl">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  );
}
