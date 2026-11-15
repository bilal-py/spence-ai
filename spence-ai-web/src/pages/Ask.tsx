import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { ArrowUp, RotateCcw, Search } from 'lucide-react';
import { Fragment, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Sprig, usePet } from '../components/Pet';
import { Button, PageHeader } from '../components/ui';
import { api, type AskMessage } from '../lib/api';
import { today } from '../lib/format';

interface Turn extends AskMessage {
  lookups?: string[];
  error?: boolean;
}

const STARTERS = [
  'How much did I spend on food this month?',
  'What were my 5 biggest purchases since June?',
  'Am I spending more than last month?',
  'Which subscriptions cost me the most a year?',
  'How much can I still spend this month?',
  'Are my savings pots on track?',
];

const STORE_KEY = 'spence:ask';

function loadTurns(): Turn[] {
  try {
    return JSON.parse(sessionStorage.getItem(STORE_KEY) ?? '[]') as Turn[];
  } catch {
    return [];
  }
}

/** "Ask your expenses": a chat where the AI answers from your data through read-only lookups, and shows what it looked at. */
export default function Ask() {
  const [turns, setTurns] = useState<Turn[]>(loadTurns);
  const [text, setText] = useState('');
  const end = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const pet = usePet().data;

  useEffect(() => {
    try {
      sessionStorage.setItem(STORE_KEY, JSON.stringify(turns.slice(-30)));
    } catch {
      /* private mode: the chat just won't survive a reload */
    }
    end.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [turns]);

  const ask = useMutation({
    mutationFn: (history: Turn[]) =>
      api.ask(
        history.filter((t) => !t.error).map(({ role, text }) => ({ role, text })),
        today(),
      ),
    onSuccess: (res) => setTurns((all) => [...all, { role: 'assistant', text: res.reply, lookups: res.lookups }]),
    onError: (e) => setTurns((all) => [...all, { role: 'assistant', text: e instanceof Error ? e.message : String(e), error: true }]),
  });

  const send = (question: string) => {
    const q = question.trim();
    if (!q || ask.isPending) return;
    const next = [...turns, { role: 'user' as const, text: q }];
    setTurns(next);
    setText('');
    ask.mutate(next);
    input.current?.focus();
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    send(text);
  };

  return (
    <div className="mx-auto flex max-w-3xl flex-col">
      <PageHeader title="Ask Spence">
        {turns.length > 0 && (
          <Button variant="ghost" onClick={() => setTurns([])} disabled={ask.isPending}>
            <RotateCcw className="size-4" aria-hidden /> New chat
          </Button>
        )}
      </PageHeader>

      {turns.length === 0 ? (
        <div className="rise-in">
          <div className="flex items-center gap-4">
            <Sprig mood="cheerful" accessory={pet?.accessory} className="size-16 shrink-0" />
            <p className="max-w-prose text-ink-soft">
              Ask anything about your money in plain words. {pet?.name ?? 'Spence'} looks it up in your data, answers with real numbers, and shows what it
              checked. It can't change anything. Uses the AI set in <Link to="/settings" className="text-pine underline-offset-2 hover:underline">Settings</Link>.
            </p>
          </div>
          <ul className="mt-6 flex flex-wrap gap-2">
            {STARTERS.map((s) => (
              <li key={s}>
                <button onClick={() => send(s)} className="rounded-full border border-line bg-surface px-3.5 py-1.5 text-sm hover:border-pine hover:text-pine">
                  {s}
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <ol className="grid gap-5" aria-live="polite">
          {turns.map((t, i) => (
            <li key={i} className={clsx('rise-in flex gap-3', t.role === 'user' && 'justify-end')}>
              {t.role === 'assistant' && <Sprig mood={t.error ? 'watchful' : 'content'} accessory={pet?.accessory} className="mt-0.5 size-8 shrink-0" />}
              <div className={clsx('max-w-[85%]', t.role === 'user' ? 'rounded-2xl rounded-br-md bg-ink px-4 py-2.5 text-paper' : 'min-w-0')}>
                {t.role === 'user' ? (
                  <p className="whitespace-pre-wrap">{t.text}</p>
                ) : (
                  <>
                    <div className={clsx('grid gap-2 leading-relaxed', t.error && 'text-brick')}>
                      <Rich text={t.text} />
                    </div>
                    {t.lookups && t.lookups.length > 0 && (
                      <ul className="mt-2 flex flex-wrap gap-1.5" aria-label="What Spence looked at">
                        {t.lookups.map((l) => (
                          <li key={l} className="inline-flex items-center gap-1 rounded-md bg-sunken px-2 py-0.5 text-xs text-ink-soft">
                            <Search className="size-3" aria-hidden /> {l}
                          </li>
                        ))}
                      </ul>
                    )}
                  </>
                )}
              </div>
            </li>
          ))}
          {ask.isPending && (
            <li className="flex items-center gap-3 text-ink-soft">
              <Sprig mood="content" accessory={pet?.accessory} className="size-8 shrink-0" />
              <span className="text-sm">
                Looking through your numbers
                <span className="inline-flex w-4">
                  <span className="animate-pulse">…</span>
                </span>
              </span>
            </li>
          )}
        </ol>
      )}

      <div ref={end} />

      <form onSubmit={submit} className="sticky bottom-20 mt-8 md:bottom-6">
        <div className="flex items-end gap-2 rounded-2xl border border-line bg-surface p-2 shadow-lg focus-within:border-pine">
          <textarea
            ref={input}
            rows={1}
            aria-label="Ask about your money"
            className="max-h-40 min-h-10 flex-1 resize-none bg-transparent px-2 py-2 text-[0.95rem] text-ink placeholder:text-ink-faint focus:outline-none"
            placeholder="How much went on Uber since August?"
            maxLength={2000}
            value={text}
            autoFocus
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                send(text);
              }
            }}
          />
          <Button type="submit" variant="primary" className="size-10 !rounded-xl !px-0" disabled={!text.trim()} busy={ask.isPending} aria-label="Ask">
            {!ask.isPending && <ArrowUp className="size-4" aria-hidden />}
          </Button>
        </div>
      </form>
    </div>
  );
}

/** A tiny, safe formatter for model replies: paragraphs, "- " lists and **bold**. Everything is rendered as text. */
function Rich({ text }: { text: string }) {
  const blocks: ReactNode[] = [];
  let list: string[] = [];
  const flush = () => {
    if (list.length) {
      blocks.push(
        <ul key={`l${blocks.length}`} className="grid list-disc gap-1 pl-5">
          {list.map((item, i) => (
            <li key={i}>{bold(item)}</li>
          ))}
        </ul>,
      );
      list = [];
    }
  };
  for (const raw of text.split('\n')) {
    const line = raw.trim();
    const item = /^(?:[-*•]|\d+[.)])\s+(.*)$/.exec(line);
    if (item) {
      list.push(item[1]);
      continue;
    }
    flush();
    if (line) blocks.push(<p key={`p${blocks.length}`}>{bold(line)}</p>);
  }
  flush();
  return <>{blocks}</>;
}

function bold(s: string): ReactNode {
  return s.split(/\*\*(.+?)\*\*/g).map((part, i) => (i % 2 ? <strong key={i}>{part}</strong> : <Fragment key={i}>{part}</Fragment>));
}
