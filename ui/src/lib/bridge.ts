import { mockInvoke } from './mock';

/**
 * Ponte nativa Photino (JS ⇄ C#). Protocolo:
 *   JS → C#  {"id": n, "method": "...", "args": {...}}  via window.external.sendMessage
 *   C# → JS  {"id": n, "ok": true, "result": {...}} | {"ok": false, "error": "..."}
 *   C# → JS  {"event": "nome", "payload": {...}}          via window.external.receiveMessage
 *
 * Sem bridge nativa (ex.: npm run dev no navegador), os métodos caem no mock (mock.ts),
 * para o design ser navegável fora do app desktop.
 */

export interface BridgeResponse {
  id: number;
  ok: boolean;
  result?: unknown;
  error?: string;
}

export interface BridgeEvent {
  event: string;
  payload?: unknown;
}

type PendingEntry = { resolve: (value: unknown) => void; reject: (error: Error) => void };

const pending = new Map<number, PendingEntry>();
const listeners = new Map<string, Set<(payload: unknown) => void>>();
let nextId = 1;

function hasNative(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof (window as unknown as { external?: { sendMessage?: unknown } }).external?.sendMessage ===
      'function'
  );
}

export function invoke<T = unknown>(method: string, args?: unknown, timeoutMs = 60_000): Promise<T> {
  if (!hasNative()) {
    return mockInvoke(method, args) as Promise<T>;
  }

  return new Promise<T>((resolve, reject) => {
    const id = nextId++;
    // Resposta perdida/handler travado não pode deixar a promise pendente pra sempre —
    // era isso que congelava as telas em "Carregando…" sem saída. O default de 60s
    // tolera operações pesadas; quem é mais demorado passa timeoutMs maior.
    const timeout = setTimeout(() => {
      if (pending.delete(id)) {
        reject(new Error(`Bridge sem resposta: ${method}`));
      }
    }, timeoutMs);
    pending.set(id, {
      resolve: (value) => {
        clearTimeout(timeout);
        resolve(value as T);
      },
      reject: (error) => {
        clearTimeout(timeout);
        reject(error);
      },
    });
    try {
      (window as unknown as { external: { sendMessage: (raw: string) => void } }).external.sendMessage(
        JSON.stringify({ id, method, args }),
      );
    } catch (error) {
      clearTimeout(timeout);
      pending.delete(id);
      reject(error instanceof Error ? error : new Error(String(error)));
    }
  });
}

export function onEvent<T = unknown>(event: string, handler: (payload: T) => void): () => void {
  let set = listeners.get(event);
  if (!set) {
    set = new Set();
    listeners.set(event, set);
  }
  set.add(handler as (payload: unknown) => void);
  return () => set!.delete(handler as (payload: unknown) => void);
}

function handleNativeMessage(raw: unknown) {
  let message: BridgeResponse | BridgeEvent;
  try {
    message = JSON.parse(String(raw)) as BridgeResponse | BridgeEvent;
  } catch {
    return;
  }

  if (message && typeof (message as BridgeResponse).id === 'number' && 'ok' in message) {
    const response = message as BridgeResponse;
    const entry = pending.get(response.id);
    if (!entry) {
      return;
    }
    pending.delete(response.id);
    if (response.ok) {
      entry.resolve(response.result);
    } else {
      entry.reject(new Error(response.error ?? 'Erro nativo'));
    }
    return;
  }

  const event = message as BridgeEvent;
  if (event?.event) {
    const set = listeners.get(event.event);
    if (set) {
      for (const handler of [...set]) {
        handler(event.payload);
      }
    }
  }
}

if (typeof window !== 'undefined') {
  const external = (window as unknown as { external?: { receiveMessage?: unknown } }).external;
  if (external) {
    const handler = (raw: unknown) => handleNativeMessage(raw);
    try {
      (external.receiveMessage as ((cb: (raw: unknown) => void) => void) | undefined)?.(handler);
    } catch {
      // Registro opcional — a atribuição abaixo cobre o caso de API de chamada direta.
    }
    external.receiveMessage = handler;
  }
}