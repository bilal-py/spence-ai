import { useMutation, useQuery } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Camera, FileText, LoaderCircle, Sheet, Undo2, Upload } from 'lucide-react';
import { useRef, useState, type DragEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { useToast } from '../components/Toast';
import { Button, ErrorNote, PageHeader, SectionTitle, Spinner } from '../components/ui';
import { api, type ImportBatch } from '../lib/api';
import { keys, useInvalidateMoney, useSettings } from '../lib/hooks';

const ACCEPT = '.pdf,.csv,.txt,.png,.jpg,.jpeg,.webp';

export default function Import() {
  const navigate = useNavigate();
  const toast = useToast();
  const invalidate = useInvalidateMoney();
  const settings = useSettings().data;
  const input = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);

  const history = useQuery({
    queryKey: keys.imports,
    queryFn: api.imports.list,
    // Poll while anything is still being read by the AI.
    refetchInterval: (q) => (q.state.data?.some((b) => b.status === 'Queued' || b.status === 'Processing') ? 2000 : false),
  });

  const upload = useMutation({
    mutationFn: async (files: File[]) => {
      const created: ImportBatch[] = [];
      for (const f of files) created.push(await api.imports.upload(f));
      return created;
    },
    onSuccess: (created) => {
      invalidate();
      if (created.length === 1) navigate(`/import/${created[0].id}`);
      else toast.show(`Reading ${created.length} files. Each one will be ready to review when it's done.`);
    },
  });

  const undo = useMutation({
    mutationFn: (id: string) => api.imports.discard(id),
    onSuccess: () => {
      invalidate();
      toast.show('Import undone, its transactions were removed');
    },
  });

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    const files = [...e.dataTransfer.files];
    if (files.length) upload.mutate(files);
  };

  const provider = settings?.aiProvider === 'Ollama' ? `${settings.aiModel} on this device` : settings ? `${settings.aiProvider} (${settings.aiModel})` : '';

  return (
    <>
      <PageHeader title="Import" />

      <div
        onDragOver={(e) => {
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
        className={clsx(
          'flex flex-col items-center rounded-xl border-2 border-dashed px-6 py-14 text-center transition-colors',
          dragging ? 'border-pine bg-pine-soft/40' : 'border-line bg-surface',
        )}
      >
        {upload.isPending ? (
          <LoaderCircle className="size-8 animate-spin text-pine" aria-hidden />
        ) : (
          <div className="flex gap-3 text-ink-faint" aria-hidden>
            <FileText className="size-7" strokeWidth={1.5} />
            <Sheet className="size-7" strokeWidth={1.5} />
            <Camera className="size-7" strokeWidth={1.5} />
          </div>
        )}
        <p className="mt-4 font-display text-2xl font-semibold tracking-[-0.01em]">Drop a statement, a CSV export or a receipt photo</p>
        <p className="mt-1 max-w-md text-ink-soft">
          Spence reads it, sorts each transaction into a category, and lets you check everything before it's saved.
        </p>
        <Button variant="primary" className="mt-5" onClick={() => input.current?.click()} busy={upload.isPending}>
          <Upload className="size-4" aria-hidden /> Choose files
        </Button>
        <input
          ref={input}
          type="file"
          accept={ACCEPT}
          multiple
          className="sr-only"
          onChange={(e) => {
            const files = [...(e.target.files ?? [])];
            if (files.length) upload.mutate(files);
            e.target.value = '';
          }}
        />
        <p className="mt-4 text-xs text-ink-faint">
          PDF, CSV, PNG, JPG or WEBP up to 20 MB.{provider && <> Read by {provider}. <Link to="/settings" className="underline underline-offset-2 hover:text-ink">Change</Link></>}
        </p>
      </div>
      <ErrorNote error={upload.error} className="mt-4" />

      <section className="mt-12">
        <SectionTitle>Recent imports</SectionTitle>
        <ErrorNote error={history.error ?? undo.error} className="mb-3" />
        {history.isPending ? (
          <Spinner />
        ) : history.data?.length === 0 ? (
          <p className="py-6 text-ink-soft">Nothing imported yet.</p>
        ) : (
          <ul className="divide-y divide-line">
            {history.data?.map((b) => (
              <li key={b.id} className="flex flex-wrap items-center gap-x-4 gap-y-1 py-3">
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium">{b.fileName}</p>
                  <p className="text-xs text-ink-faint">{new Date(b.createdAt + 'Z').toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}</p>
                </div>
                <Status batch={b} />
                {b.status === 'ReadyForReview' && (
                  <Link to={`/import/${b.id}`} className="inline-flex h-8 items-center rounded-md bg-pine px-3 text-sm font-medium text-on-pine hover:bg-pine-strong">
                    Review {b.transactionCount}
                  </Link>
                )}
                {b.status === 'Committed' && (
                  <Button size="sm" variant="ghost" onClick={() => undo.mutate(b.id)} busy={undo.isPending && undo.variables === b.id}>
                    <Undo2 className="size-3.5" aria-hidden /> Undo
                  </Button>
                )}
                {b.status === 'Failed' && (
                  <Button size="sm" variant="ghost" onClick={() => undo.mutate(b.id)}>
                    Dismiss
                  </Button>
                )}
                {b.error && <p className="w-full text-sm text-brick">{b.error}</p>}
              </li>
            ))}
          </ul>
        )}
      </section>
    </>
  );
}

function Status({ batch }: { batch: ImportBatch }) {
  const map: Record<ImportBatch['status'], [string, string]> = {
    Queued: ['Waiting', 'text-ink-soft'],
    Processing: ['Reading', 'text-ochre'],
    ReadyForReview: ['Ready to review', 'text-pine'],
    Committed: [`${batch.transactionCount} saved`, 'text-ink-soft'],
    Failed: ["Couldn't read", 'text-brick'],
    Discarded: ['Discarded', 'text-ink-faint'],
  };
  const [label, color] = map[batch.status];
  return (
    <span className={clsx('inline-flex items-center gap-1.5 text-sm', color)}>
      {(batch.status === 'Processing' || batch.status === 'Queued') && <LoaderCircle className="size-3.5 animate-spin" aria-hidden />}
      {label}
    </span>
  );
}
