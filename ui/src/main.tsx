import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import * as Sentry from '@sentry/react';
import { LinkProvider } from '@astryxdesign/core/Link';
import App from './App';
import { NativeLink } from './lib/NativeLink';
import './styles.css';

const sentryDsn = import.meta.env.VITE_SENTRY_DSN;
if (sentryDsn) {
  Sentry.init({
    dsn: sentryDsn,
    environment: import.meta.env.MODE,
    release: import.meta.env.VITE_SENTRY_RELEASE,
    beforeSend(event) {
      if (event.message) event.message = '[redacted]';
      for (const exception of event.exception?.values ?? []) {
        if (exception.value) exception.value = '[redacted]';
      }
      delete event.user;
      delete event.extra;
      delete event.request;
      return event;
    },
  });
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Sentry.ErrorBoundary fallback={<p>Ocorreu um erro inesperado. Recarregue o Verso.</p>}>
      <LinkProvider component={NativeLink}>
        <App />
      </LinkProvider>
    </Sentry.ErrorBoundary>
  </StrictMode>,
);
