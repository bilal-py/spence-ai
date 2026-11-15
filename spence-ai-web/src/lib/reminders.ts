import { useEffect } from 'react';
import { money, today } from './format';
import { useBills, useCurrency } from './hooks';

// Browser reminders for bills. They only fire while Spence is open in a tab (there's no server to push from),
// at most once a day, and only if the user turned them on. For reminders when the app is closed, the Bills page
// offers a calendar file instead.

const ON_KEY = 'spence:bill-reminders';
const LAST_KEY = 'spence:bill-reminded-on';

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string | null) {
  try {
    if (value === null) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch {
    /* storage blocked: reminders just won't remember */
  }
}

export function reminderPermission(): NotificationPermission | 'unsupported' {
  return typeof window !== 'undefined' && 'Notification' in window ? Notification.permission : 'unsupported';
}

export function remindersOn(): boolean {
  return read(ON_KEY) === 'on' && reminderPermission() === 'granted';
}

export function setRemindersOn(on: boolean) {
  write(ON_KEY, on ? 'on' : null);
  if (on) write(LAST_KEY, null);
}

/** Mounted once in the layout: one notification a day listing bills due today or tomorrow, and any missed. */
export function useBillReminders() {
  const { data } = useBills();
  const currency = useCurrency();

  useEffect(() => {
    if (!data || !remindersOn()) return;
    const day = today();
    if (read(LAST_KEY) === day) return;

    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    const limit = `${tomorrow.getFullYear()}-${String(tomorrow.getMonth() + 1).padStart(2, '0')}-${String(tomorrow.getDate()).padStart(2, '0')}`;
    const items = data.dueSoon.filter((i) => i.status === 'late' || i.dueDate <= limit);
    if (items.length === 0) return;

    write(LAST_KEY, day);
    const late = items.filter((i) => i.status === 'late');
    const title = late.length > 0 ? `${late.length === 1 ? late[0].name : `${late.length} bills`} past due` : items.length === 1 ? `${items[0].name} is due soon` : `${items.length} bills due soon`;
    const body = items.map((i) => `${i.name} · ${money(i.amount, currency)} · ${i.status === 'late' ? 'missed' : i.dueDate === day ? 'today' : 'tomorrow'}`).join('\n');
    try {
      const n = new Notification(title, { body, tag: 'spence-bills', icon: '/favicon.svg' });
      n.onclick = () => {
        window.focus();
        window.location.assign('/bills');
      };
    } catch {
      /* some browsers only allow notifications from a service worker */
    }
  }, [data, currency]);
}
