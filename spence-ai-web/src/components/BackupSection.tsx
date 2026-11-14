import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArchiveRestore, Download, HardDriveDownload } from 'lucide-react';
import { useRef, useState, type FormEvent } from 'react';
import { api, type BackupFile, type BackupKind, type BackupManifest, type BackupStatus } from '../lib/api';
import { useToast } from './Toast';
import { Button, ErrorNote, SectionTitle, Spinner } from './ui';

const backupKey = ['backup'] as const;

const KIND_LABEL: Record<BackupKind, string> = {
  Manual: 'Made by you',
  Daily: 'Daily',
  BeforeRestore: 'Before a restore',
  BeforeUpdate: 'Before an update',
};

/** What the user is about to restore: an uploaded file (already inspected) or a backup in the backups folder. */
type Pending = { source: 'file'; file: File; manifest: BackupManifest } | { source: 'saved'; backup: BackupFile };

export function BackupSection() {
  const status = useQuery({ queryKey: backupKey, queryFn: api.backup.status });

  return (
    <section className="max-w-3xl">
      <SectionTitle>Backups</SectionTitle>
      <ErrorNote error={status.error} />
      {status.isPending ? <Spinner /> : status.data && <Backups key={`${status.data.autoBackup}|${status.data.copyFolder}`} status={status.data} />}
    </section>
  );
}

function Backups({ status }: { status: BackupStatus }) {
  const qc = useQueryClient();
  const toast = useToast();
  const fileInput = useRef<HTMLInputElement>(null);
  const [pending, setPending] = useState<Pending | null>(null);
  const [autoBackup, setAutoBackup] = useState(status.autoBackup);
  const [copyFolder, setCopyFolder] = useState(status.copyFolder ?? '');

  const create = useMutation({
    mutationFn: api.backup.create,
    onSuccess: (file) => {
      qc.invalidateQueries({ queryKey: backupKey });
      window.location.assign(api.backup.downloadUrl(file.fileName));
    },
  });

  const inspect = useMutation({
    mutationFn: api.backup.inspect,
    onSuccess: (manifest, file) => setPending({ source: 'file', file, manifest }),
  });

  const restore = useMutation({
    mutationFn: (p: Pending) => (p.source === 'file' ? api.backup.restore(p.file) : api.backup.restoreSaved(p.backup.fileName)),
    onSuccess: (result) => {
      setPending(null);
      // Everything may have changed: transactions, categories, rules and settings.
      qc.invalidateQueries();
      toast.show(`Restored ${count(result.restored.transactions, 'transaction')}. Your previous data is saved in the list below.`);
    },
  });

  const saveSettings = useMutation({
    mutationFn: () => api.backup.updateSettings({ autoBackup, copyFolder: copyFolder.trim() || null }),
    onSuccess: (s) => {
      qc.setQueryData(backupKey, s);
      toast.show('Backup settings saved');
    },
  });

  const settingsChanged = autoBackup !== status.autoBackup || copyFolder.trim() !== (status.copyFolder ?? '');

  const submitSettings = (e: FormEvent) => {
    e.preventDefault();
    saveSettings.mutate();
  };

  return (
    <div className="grid gap-6">
      <p className="max-w-prose text-ink-soft">
        A backup is one <code className="text-[0.85em]">.spence</code> file with everything: transactions, categories, rules and settings.{' '}
        {status.lastBackupAt ? <>Last backup {ago(status.lastBackupAt)}.</> : 'No backups yet.'}
      </p>

      <div className="flex flex-wrap gap-2">
        <Button onClick={() => create.mutate()} busy={create.isPending}>
          <HardDriveDownload className="size-4" aria-hidden /> Back up now
        </Button>
        <Button onClick={() => fileInput.current?.click()} busy={inspect.isPending}>
          <ArchiveRestore className="size-4" aria-hidden /> Restore from a file
        </Button>
        <input
          ref={fileInput}
          type="file"
          accept=".spence"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0];
            e.target.value = '';
            if (file) inspect.mutate(file);
          }}
        />
      </div>
      <ErrorNote error={create.error ?? inspect.error} />

      {pending && (
        <div role="alertdialog" aria-labelledby="restore-title" className="rounded-lg border border-ochre/60 bg-surface p-4">
          <h3 id="restore-title" className="font-medium">
            Replace everything in Spence with this backup?
          </h3>
          <p className="mt-1 text-sm text-ink-soft">
            {pending.source === 'file' ? describe(pending.manifest) : `${KIND_LABEL[pending.backup.kind]} backup from ${when(pending.backup.createdAt)}.`}{' '}
            Your current data is backed up first, so you can switch back.
          </p>
          <div className="mt-3 flex gap-2">
            <Button variant="primary" onClick={() => restore.mutate(pending)} busy={restore.isPending}>
              Restore
            </Button>
            <Button variant="ghost" onClick={() => setPending(null)} disabled={restore.isPending}>
              Cancel
            </Button>
          </div>
          <ErrorNote error={restore.error} className="mt-2" />
        </div>
      )}

      <form onSubmit={submitSettings} className="grid gap-3">
        <label className="flex items-center gap-2">
          <input type="checkbox" className="size-4 accent-[var(--pine)]" checked={autoBackup} onChange={(e) => setAutoBackup(e.target.checked)} />
          Back up automatically once a day
        </label>
        <div>
          <label className="label" htmlFor="copy-folder">
            Also copy backups to
          </label>
          <input
            id="copy-folder"
            className="field"
            placeholder="C:\Users\you\OneDrive\Spence"
            value={copyFolder}
            onChange={(e) => setCopyFolder(e.target.value)}
          />
          <p className="mt-1 text-xs text-ink-faint">
            Optional. A synced folder (OneDrive, Dropbox) keeps a copy off this computer. Backups aren't encrypted, so only use a folder you trust.
          </p>
        </div>
        {settingsChanged && (
          <div>
            <Button type="submit" size="sm" busy={saveSettings.isPending}>
              Save backup settings
            </Button>
          </div>
        )}
        <ErrorNote error={saveSettings.error} />
      </form>

      {status.backups.length > 0 && (
        <div>
          <h3 className="mb-1 text-sm font-medium">Saved on this computer</h3>
          <p className="mb-2 text-xs break-all text-ink-faint">{status.directory}</p>
          <ul className="divide-y divide-line">
            {status.backups.slice(0, 10).map((b) => (
              <li key={b.fileName} className="flex items-center gap-3 py-2 text-sm">
                <span className="min-w-0 flex-1">
                  {when(b.createdAt)} <span className="text-ink-faint">· {KIND_LABEL[b.kind]} · {size(b.sizeBytes)}</span>
                </span>
                <a
                  href={api.backup.downloadUrl(b.fileName)}
                  aria-label={`Download backup from ${when(b.createdAt)}`}
                  className="rounded p-1.5 text-ink-faint hover:bg-sunken hover:text-ink"
                >
                  <Download className="size-3.5" />
                </a>
                <Button size="sm" variant="ghost" onClick={() => setPending({ source: 'saved', backup: b })}>
                  Restore
                </Button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function describe(m: BackupManifest): string {
  const range = m.firstDate && m.lastDate ? `, ${day(m.firstDate)} to ${day(m.lastDate)}` : '';
  return `Made ${when(m.createdAt)}: ${count(m.transactions, 'transaction')}${range}, ${count(m.categories, 'category', 'categories')}, ${count(m.rules, 'rule')}.`;
}

const count = (n: number, one: string, many = `${one}s`) => `${n.toLocaleString()} ${n === 1 ? one : many}`;

const when = (iso: string) =>
  new Date(iso).toLocaleString(undefined, { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' });

const day = (iso: string) => new Date(`${iso}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });

function size(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function ago(iso: string): string {
  const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.round(hours / 24);
  return days === 1 ? 'yesterday' : `${days} days ago`;
}
