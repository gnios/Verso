/* Debug: lista cards do dashboard + tenta operações do editor com o id semeado. */
import { connectPage, EDITOR_ID } from './lib/cdp.mjs';

const { ws, call } = await connectPage();

const dash = await call('dashboard.load', {});
const saveText = await call('editor.saveText', {
  segmentId: 'e2e33333-3333-3333-3333-333333333331',
  text: 'teste guid',
}).catch((err) => ({ error: err.message }));
const addSpeaker = await call('editor.addSpeaker', {
  transcriptionId: EDITOR_ID,
  name: 'Probe',
}).catch((err) => ({ error: err.message }));
const editor = await call('editor.load', { transcriptionId: EDITOR_ID }).catch((err) => ({
  error: err.message,
}));

console.log(
  JSON.stringify(
    {
      cards: dash.cards.map((c) => ({ title: c.title, status: c.status, id: c.id })),
      saveTextError: saveText?.error ?? 'OK',
      addSpeakerResult: JSON.stringify(addSpeaker),
      editorError: editor?.error ?? null,
      editorTitle: editor?.title ?? null,
    },
    null,
    2,
  ),
);
ws.close();
