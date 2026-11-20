/* Conexão CDP com o WebView2 do Verso.App.
 * Pré-requisito: app rodando com WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9223
 * (scripts/run-ui-checks.ps1 cuida disso). */

export const CDP_HTTP = 'http://127.0.0.1:9223/json';
export const EDITOR_ID = 'e2e11111-1111-1111-1111-111111111111';

export async function connectPage() {
  const targets = await (await fetch(CDP_HTTP)).json();
  const page = targets.find((t) => t.type === 'page');
  if (!page) {
    throw new Error(
      'Nenhum target de página encontrado — o app está rodando com ' +
        'WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9223?',
    );
  }

  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    ws.onopen = resolve;
    ws.onerror = reject;
  });

  let msgId = 0;
  const pending = new Map();
  ws.onmessage = (event) => {
    const msg = JSON.parse(event.data);
    if (msg.id && pending.has(msg.id)) {
      pending.get(msg.id)(msg);
      pending.delete(msg.id);
    }
  };

  const send = (method, params = {}) =>
    new Promise((resolve) => {
      const id = ++msgId;
      pending.set(id, resolve);
      ws.send(JSON.stringify({ id, method, params }));
    });

  const evaluate = async (expression) => {
    const res = await send('Runtime.evaluate', {
      expression,
      returnByValue: true,
      awaitPromise: true,
    });
    if (res.result?.exceptionDetails) {
      throw new Error(
        `JS erro: ${JSON.stringify(res.result.exceptionDetails.exception?.description ?? res.result.exceptionDetails)}`,
      );
    }
    return res.result?.result?.value;
  };

  // Ponte nativa via chrome.webview — mesmo protocolo de ui/src/lib/bridge.ts.
  const call = (method, args) =>
    evaluate(`(async () => {
      const method = ${JSON.stringify(method)};
      const args = ${JSON.stringify(args ?? {})};
      return await new Promise((resolve, reject) => {
        const id = Math.floor(Math.random() * 1e9);
        const handler = (e) => {
          let msg;
          try { msg = JSON.parse(String(e.data)); } catch { return; }
          if (msg.id === id) {
            window.chrome.webview.removeEventListener('message', handler);
            msg.ok ? resolve(msg.result) : reject(new Error(msg.error));
          }
        };
        window.chrome.webview.addEventListener('message', handler);
        window.external.sendMessage(JSON.stringify({ id, method, args }));
        setTimeout(() => reject(new Error('timeout: ' + method)), 10000);
      });
    })()`);

  const navigate = (screen, parameter) =>
    call('navigation.navigate', { screen, ...parameter });

  const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

  // innerText + valores de inputs/textareas (título e segmentos do editor são editáveis).
  const domText = () =>
    evaluate(`document.querySelector('#root').innerText + '\\n' +
      [...document.querySelectorAll('input, textarea')].map((el) => el.value).join('\\n')`);

  const screenshot = async (path) => {
    await send('Page.enable', {});
    const shot = await send('Page.captureScreenshot', { format: 'png' });
    if (!shot.result?.data) {
      return false;
    }
    const fs = await import('node:fs');
    fs.writeFileSync(path, Buffer.from(shot.result.data, 'base64'));
    return true;
  };

  return { ws, send, evaluate, call, navigate, sleep, domText, screenshot };
}
