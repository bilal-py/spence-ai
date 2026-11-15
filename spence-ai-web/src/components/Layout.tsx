import { clsx } from 'clsx';
import { ChartNoAxesColumn, FileUp, Gift, List, MessageCircleQuestion, PiggyBank, Plus, ReceiptText, Settings, Shapes } from 'lucide-react';
import { Link, NavLink, Outlet, useSearchParams } from 'react-router';
import { useBillReminders } from '../lib/reminders';
import { useQuickAdd } from './QuickAdd';

// The phone's bottom bar has room for five. On phones, Ask and Settings sit in the header; Import is reached from
// the Overview and Transactions, Categories from Settings, and Wrapped from the Overview of a finished month.
const NAV = [
  { to: '/', label: 'Overview', icon: ChartNoAxesColumn, keepPeriod: true, mobile: true },
  { to: '/transactions', label: 'Transactions', icon: List, keepPeriod: true, mobile: true },
  { to: '/bills', label: 'Bills', icon: ReceiptText, mobile: true },
  { to: '/pots', label: 'Pots', icon: PiggyBank, mobile: true },
  { to: '/ask', label: 'Ask', icon: MessageCircleQuestion, mobile: false },
  { to: '/wrapped', label: 'Wrapped', icon: Gift, mobile: false },
  { to: '/import', label: 'Import', icon: FileUp, mobile: true },
  { to: '/categories', label: 'Categories', icon: Shapes, mobile: false },
  { to: '/settings', label: 'Settings', icon: Settings, mobile: false },
];

export function Layout() {
  const quickAdd = useQuickAdd();
  useBillReminders();
  const [params] = useSearchParams();
  const p = params.get('p');
  const href = (item: (typeof NAV)[number]) => (item.keepPeriod && p ? `${item.to}?p=${p}` : item.to);

  return (
    <div className="min-h-dvh md:grid md:grid-cols-[13.5rem_1fr]">
      <aside className="sticky top-0 hidden h-dvh flex-col border-r border-line px-4 py-6 md:flex">
        <Wordmark />
        <nav className="mt-10 flex flex-col gap-0.5" aria-label="Main">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={href(item)}
              end={item.to === '/'}
              className={({ isActive }) =>
                clsx(
                  'flex h-9 items-center gap-2.5 rounded-md px-2.5 text-[0.93rem] transition-colors',
                  isActive ? 'bg-surface font-semibold text-ink shadow-[inset_3px_0_0_var(--pine)]' : 'text-ink-soft hover:text-ink',
                )
              }
            >
              <item.icon className="size-4" strokeWidth={1.75} aria-hidden />
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="mt-auto">
          <button
            onClick={() => quickAdd.open()}
            title="Quick add (N)"
            className="flex h-10 w-full items-center justify-center gap-2 rounded-md bg-pine font-medium text-on-pine hover:bg-pine-strong"
          >
            <Plus className="size-4" aria-hidden /> Add transaction
          </button>
          <p className="mt-3 text-center text-xs text-ink-faint">Stored on this device</p>
        </div>
      </aside>

      <header className="flex items-center justify-between border-b border-line px-4 py-3 md:hidden">
        <Wordmark />
        <div className="flex items-center gap-1">
          <Link to="/ask" aria-label="Ask Spence" className="grid size-9 place-items-center rounded-md text-ink-soft hover:bg-sunken hover:text-ink">
            <MessageCircleQuestion className="size-5" strokeWidth={1.75} />
          </Link>
          <Link to="/settings" aria-label="Settings" className="grid size-9 place-items-center rounded-md text-ink-soft hover:bg-sunken hover:text-ink">
            <Settings className="size-5" strokeWidth={1.75} />
          </Link>
          <button onClick={() => quickAdd.open()} aria-label="Add transaction" className="ml-1 grid size-9 place-items-center rounded-md bg-pine text-on-pine">
            <Plus className="size-4" />
          </button>
        </div>
      </header>

      <main className="mx-auto w-full min-w-0 max-w-[76rem] px-4 pt-6 pb-28 md:px-10 md:pt-10 md:pb-16">
        <Outlet />
      </main>

      <nav aria-label="Main" className="fixed inset-x-0 bottom-0 z-40 grid grid-cols-5 border-t border-line bg-surface md:hidden">
        {NAV.filter((item) => item.mobile).map((item) => (
          <NavLink
            key={item.to}
            to={href(item)}
            end={item.to === '/'}
            className={({ isActive }) => clsx('flex flex-col items-center gap-0.5 py-2 text-[0.7rem]', isActive ? 'text-pine' : 'text-ink-soft')}
          >
            <item.icon className="size-5" strokeWidth={1.75} aria-hidden />
            {item.label}
          </NavLink>
        ))}
      </nav>
    </div>
  );
}

function Wordmark() {
  return (
    <NavLink to="/" className="flex items-center gap-2 px-1" aria-label="Spence home">
      <svg viewBox="0 0 32 32" className="size-7" aria-hidden>
        <rect width="32" height="32" rx="8" fill="var(--pine)" />
        <rect x="6" y="19" width="7" height="6" rx="1.5" fill="var(--ochre)" />
        <rect x="14" y="19" width="5" height="6" rx="1.5" fill="var(--paper)" />
        <rect x="20" y="19" width="6" height="6" rx="1.5" fill="var(--pine-soft)" />
        <path d="M8 13.5c0-2.5 2.2-4 5-4h11" stroke="var(--paper)" strokeWidth="2.4" strokeLinecap="round" fill="none" />
      </svg>
      <span className="font-display text-[1.4rem] font-bold tracking-[-0.03em]">spence</span>
    </NavLink>
  );
}
