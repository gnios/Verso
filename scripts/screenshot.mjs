/* Tira screenshot da tela atual do app via CDP.
 * Uso: node scripts/screenshot.mjs <saida.png> */
import { connectPage } from './lib/cdp.mjs';

const out = process.argv[2] ?? 'scripts/verso-screen.png';

const { ws, screenshot } = await connectPage();
if (!(await screenshot(out))) {
  console.error('screenshot vazio — Page.captureScreenshot não retornou dados');
  process.exit(1);
}
console.log('screenshot:', out);
ws.close();
