import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { LinkProvider } from '@astryxdesign/core/Link';
import App from './App';
import { NativeLink } from './lib/NativeLink';
import './styles.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LinkProvider component={NativeLink}>
      <App />
    </LinkProvider>
  </StrictMode>,
);