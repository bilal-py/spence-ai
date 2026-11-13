import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Check, Cloud, Cpu, Download } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { useToast } from '../components/Toast';
import { Button, ErrorNote, PageHeader, SectionTitle, Spinner } from '../components/ui';
import { api, type AiProvider, type Settings as SettingsData, type SettingsInput } from '../lib/api';
import { CURRENCIES } from '../lib/format';
import { keys, useSettings } from '../lib/hooks';

const PROVIDERS: { value: AiProvider; title: string; body: string; icon: typeof Cpu; models: string[] }[] = [
  {
    value: 'Ollama',
    title: 'On this device',
    body: 'Runs with Ollama. Nothing leaves your computer. Slower, and small models make more mistakes.',
    icon: Cpu,
    models: ['llama3.2', 'qwen2.5:7b', 'llama3.1:8b', 'qwen2.5vl:7b'],
  },
  {
    value: 'Gemini',
    title: 'Google Gemini',
    body: 'Fast and accurate, with a free tier. Statements are sent to Google.',
    icon: Cloud,
    models: ['gemini-2.5-flash', 'gemini-2.5-flash-lite', 'gemini-2.5-pro'],
  },
  {
    value: 'OpenAI',
    title: 'OpenAI',
    body: 'Fast and accurate, paid per use. Statements are sent to OpenAI.',
    icon: Cloud,
    models: ['gpt-4.1-mini', 'gpt-4.1', 'gpt-4o-mini'],
  },
];

export default function Settings() {
  const { data, error, isPending } = useSettings();
  return (
    <>
      <PageHeader title="Settings" />
      <ErrorNote error={error} />
      {isPending ? <Spinner /> : data && <SettingsForm key={JSON.stringify(data)} saved={data} />}
    </>
  );
}

function SettingsForm({ saved }: { saved: SettingsData }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [provider, setProvider] = useState(saved.aiProvider);
  const [model, setModel] = useState(saved.aiModel);
  const [ollamaUrl, setOllamaUrl] = useState(saved.ollamaUrl);
  const [apiKey, setApiKey] = useState<string | null>(null);
  const [currency, setCurrency] = useState(saved.currency);

  const info = PROVIDERS.find((p) => p.value === provider)!;
  const input: SettingsInput = { aiProvider: provider, aiModel: model, ollamaUrl, apiKey, currency };

  const ollamaModels = useQuery({
    queryKey: ['ollama-models', ollamaUrl],
    queryFn: () => api.settings.ollamaModels(ollamaUrl),
    enabled: provider === 'Ollama',
    retry: false,
  });

  const save = useMutation({
    mutationFn: () => api.settings.update(input),
    onSuccess: (s) => {
      qc.setQueryData(keys.settings, s);
      toast.show('Settings saved');
    },
  });

  const test = useMutation({ mutationFn: () => api.settings.test(input) });

  const pickProvider = (p: AiProvider) => {
    setProvider(p);
    const next = PROVIDERS.find((x) => x.value === p)!;
    if (p === saved.aiProvider) setModel(saved.aiModel);
    else setModel(p === 'Ollama' && ollamaModels.data?.[0] ? ollamaModels.data[0] : next.models[0]);
    test.reset();
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  const suggestions = provider === 'Ollama' && ollamaModels.data?.length ? ollamaModels.data : info.models;

  return (
    <form onSubmit={submit} className="grid max-w-3xl gap-12">
      <section>
        <SectionTitle>AI for reading statements and writing summaries</SectionTitle>
        <div className="grid gap-3 sm:grid-cols-3" role="radiogroup" aria-label="AI provider">
          {PROVIDERS.map((p) => (
            <button
              key={p.value}
              type="button"
              role="radio"
              aria-checked={provider === p.value}
              onClick={() => pickProvider(p.value)}
              className={clsx(
                'flex flex-col items-start gap-1.5 rounded-lg border p-4 text-left transition-colors',
                provider === p.value ? 'border-pine bg-pine-soft/40 shadow-[inset_0_0_0_1px_var(--pine)]' : 'border-line bg-surface hover:border-ink-faint',
              )}
            >
              <span className="flex w-full items-center justify-between">
                <p.icon className="size-5 text-ink-soft" strokeWidth={1.75} aria-hidden />
                {provider === p.value && <Check className="size-4 text-pine" aria-hidden />}
              </span>
              <span className="font-display text-lg font-semibold">{p.title}</span>
              <span className="text-sm text-ink-soft">{p.body}</span>
            </button>
          ))}
        </div>

        <div className="mt-6 grid gap-4 sm:grid-cols-2">
          <Field label="Model" htmlFor="ai-model" hint={provider === 'Ollama' && ollamaModels.isError ? 'Ollama isn’t answering at that address.' : undefined}>
            <input id="ai-model" className="field" list="model-options" required value={model} onChange={(e) => setModel(e.target.value)} />
            <datalist id="model-options">
              {suggestions.map((m) => (
                <option key={m} value={m} />
              ))}
            </datalist>
          </Field>

          {provider === 'Ollama' ? (
            <Field label="Ollama address" htmlFor="ollama-url">
              <input id="ollama-url" className="field" required value={ollamaUrl} onChange={(e) => setOllamaUrl(e.target.value)} />
            </Field>
          ) : (
            <Field
              label={`${provider} API key`}
              htmlFor="api-key"
              hint={saved.hasApiKey && apiKey === null ? 'A key is saved and encrypted on this device.' : undefined}
            >
              <input
                id="api-key"
                className="field"
                type="password"
                autoComplete="off"
                placeholder={saved.hasApiKey ? '••••••••••••' : 'Paste your key'}
                value={apiKey ?? ''}
                onChange={(e) => setApiKey(e.target.value)}
              />
            </Field>
          )}
        </div>

        {provider === 'Ollama' && (
          <p className="mt-3 text-sm text-ink-soft">
            For receipt photos, use a vision model such as <code className="rounded bg-sunken px-1">qwen2.5vl:7b</code>. For better statement accuracy than
            llama3.2, try <code className="rounded bg-sunken px-1">qwen2.5:7b</code>. Install one with <code className="rounded bg-sunken px-1">ollama pull &lt;name&gt;</code>.
          </p>
        )}

        <div className="mt-5 flex flex-wrap items-center gap-3">
          <Button type="button" onClick={() => test.mutate()} busy={test.isPending}>
            Test connection
          </Button>
          {test.isSuccess && (
            <span className="inline-flex items-center gap-1.5 text-sm text-pine">
              <Check className="size-4" aria-hidden /> {model} answered
            </span>
          )}
        </div>
        <ErrorNote error={test.error} className="mt-3" />
      </section>

      <section>
        <SectionTitle>Money</SectionTitle>
        <Field label="Currency" htmlFor="currency">
          <select id="currency" className="field max-w-60" value={currency} onChange={(e) => setCurrency(e.target.value)}>
            {[...new Set([currency, ...CURRENCIES])].map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </Field>
      </section>

      <section>
        <SectionTitle>Your data</SectionTitle>
        <p className="max-w-prose text-ink-soft">
          Everything is stored in a database file on this computer. Export a copy whenever you like; the CSV opens in any spreadsheet app.
        </p>
        <a
          href={api.transactions.exportUrl({})}
          className="mt-3 inline-flex h-9 items-center gap-1.5 rounded-md border border-line bg-surface px-3.5 font-medium hover:border-ink-faint"
        >
          <Download className="size-4" aria-hidden /> Export all transactions
        </a>
      </section>

      <div className="sticky bottom-16 flex items-center gap-3 border-t border-line bg-paper py-4 md:bottom-0">
        <Button type="submit" variant="primary" busy={save.isPending}>
          Save settings
        </Button>
        <ErrorNote error={save.error} />
      </div>
    </form>
  );
}

function Field({ label, htmlFor, hint, children }: { label: string; htmlFor: string; hint?: string; children: ReactNode }) {
  return (
    <div>
      <label className="label" htmlFor={htmlFor}>
        {label}
      </label>
      {children}
      {hint && <p className="mt-1 text-xs text-ink-faint">{hint}</p>}
    </div>
  );
}
