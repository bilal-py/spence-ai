import { useMutation, useQuery } from '@tanstack/react-query';
import { ArrowUpRight, FolderOpen } from 'lucide-react';
import { api } from '../lib/api';
import { keys } from '../lib/hooks';
import { ErrorNote, SectionTitle } from './ui';

const RELEASES = 'https://github.com/bilal-py/spence-ai/releases';

/** Version, update notice and where the data lives. The update check runs in the desktop app only. */
export function AboutSection() {
  const info = useQuery({ queryKey: keys.app, queryFn: api.app.info, staleTime: 5 * 60_000 });
  const openFolder = useMutation({ mutationFn: api.app.openDataFolder });
  const data = info.data;
  if (!data) return <ErrorNote error={info.error} />;

  return (
    <section className="max-w-3xl">
      <SectionTitle aside={<span className="num">Version {data.version}</span>}>About Spence</SectionTitle>

      {data.latestVersion && data.updateUrl && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-pine/40 bg-pine-soft px-4 py-3">
          <p>
            <b>Spence {data.latestVersion} is out.</b> <span className="text-ink-soft">Download it and replace this folder. Your data stays as it is.</span>
          </p>
          <a
            href={data.updateUrl}
            target="_blank"
            rel="noreferrer"
            className="inline-flex h-9 items-center gap-1.5 rounded-md bg-pine px-3.5 font-medium text-on-pine hover:bg-pine-strong"
          >
            Get the update <ArrowUpRight className="size-4" aria-hidden />
          </a>
        </div>
      )}

      <p className="text-ink-soft">
        Your data is in <code className="break-all rounded bg-sunken px-1.5 py-0.5 text-[0.85em] text-ink">{data.dataDirectory}</code>, along with
        backups and logs.
      </p>
      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2">
        {data.desktop && (
          <button
            type="button"
            onClick={() => openFolder.mutate()}
            className="inline-flex h-9 items-center gap-1.5 rounded-md border border-line bg-surface px-3.5 font-medium hover:border-ink-faint"
          >
            <FolderOpen className="size-4" aria-hidden /> Open data folder
          </button>
        )}
        <a href={RELEASES} target="_blank" rel="noreferrer" className="text-sm text-ink-soft underline-offset-2 hover:text-ink hover:underline">
          Release notes
        </a>
        <a href="https://github.com/bilal-py/spence-ai" target="_blank" rel="noreferrer" className="text-sm text-ink-soft underline-offset-2 hover:text-ink hover:underline">
          Source code
        </a>
      </div>
      {data.desktop && !data.latestVersion && <p className="mt-3 text-xs text-ink-faint">Spence checks GitHub for a new version once a day. Nothing about you is sent.</p>}
      <ErrorNote error={openFolder.error} className="mt-3" />
    </section>
  );
}
