import { useCallback, useEffect, useState } from 'react';
import { AppShell } from '@astryxdesign/core/AppShell';
import { Theme } from '@astryxdesign/core/theme';
import { themes, defaultThemeSlug } from './astryx-themes';
import { invoke, onEvent } from './lib/bridge';
import { FOLDERS_ENABLED } from './lib/features';
import type { ScreenKey } from './lib/screens';
import type { AppInfo, ConfirmRequest, NavigationParam } from './types';
import { SidebarNav } from './shell/SidebarNav';
import { ErrorBoundary } from './shell/ErrorBoundary';
import { ConfirmDialog } from './shell/ConfirmDialog';
import { NewFolderDialog } from './shell/NewFolderDialog';
import { FeedbackDialog } from './shell/FeedbackDialog';
import { ModelDownloadDialog } from './shell/ModelDownloadDialog';
import { DashboardPage } from './pages/DashboardPage';
import { UploadPage } from './pages/UploadPage';
import { RecordingPage } from './pages/RecordingPage';
import { EditorPage } from './pages/EditorPage';
import { SettingsPage } from './pages/SettingsPage';
import { FolderPage } from './pages/FolderPage';

export default function App() {
  const [screen, setScreen] = useState<ScreenKey>('Dashboard');
  const [parameter, setParameter] = useState<NavigationParam>({});
  const [mode, setMode] = useState<'light' | 'dark'>('light');
  const [isDark, setIsDark] = useState(false);
  const [confirm, setConfirm] = useState<ConfirmRequest | null>(null);
  const [appInfo, setAppInfo] = useState<AppInfo | null>(null);
  const [newFolderOpen, setNewFolderOpen] = useState(false);
  const [feedbackOpen, setFeedbackOpen] = useState(false);

  useEffect(() => {
    // Keep-alive: sem ping o WebViewWatchdog do C# detecta renderer morto e
    // recarrega a página (cobertura para crash do WebView2 → tela em branco).
    const ping = setInterval(() => {
      void invoke('app.ping').catch(() => undefined);
    }, 20_000);

    void invoke<AppInfo>('app.info')
      .then(setAppInfo)
      .catch(() => undefined);
    void invoke<{ isDark: boolean }>('theme.get')
      .then((theme) => {
        setIsDark(theme.isDark);
        setMode(theme.isDark ? 'dark' : 'light');
      })
      .catch(() => undefined);

    const offTheme = onEvent<{ isDark: boolean }>('theme.changed', (theme) => {
      setIsDark(theme.isDark);
      setMode(theme.isDark ? 'dark' : 'light');
    });
    const offNavigation = onEvent<{ screen: ScreenKey; parameter?: NavigationParam }>(
      'navigation.changed',
      (navigation) => {
        setScreen(navigation.screen);
        setParameter(navigation.parameter ?? {});
      },
    );
    const offConfirm = onEvent<ConfirmRequest>('ui.confirm', setConfirm);

    return () => {
      clearInterval(ping);
      offTheme();
      offNavigation();
      offConfirm();
    };
  }, []);

  const toggleTheme = useCallback(() => {
    void invoke<{ isDark: boolean }>('theme.toggle')
      .then((theme) => {
        setIsDark(theme.isDark);
        setMode(theme.isDark ? 'dark' : 'light');
      })
      .catch(() => undefined);
  }, []);

  const resolveConfirm = useCallback((ok: boolean) => {
    setConfirm(null);
    void invoke('ui.confirm.respond', { ok }).catch(() => undefined);
  }, []);

  return (
    <Theme theme={themes[defaultThemeSlug]} mode={mode}>
      <AppShell
        sideNav={
          <SidebarNav
            screen={screen}
            parameter={parameter}
            isDark={isDark}
            appInfo={appInfo}
            onToggleTheme={toggleTheme}
            onNewFolder={() => setNewFolderOpen(true)}
            onFeedback={() => setFeedbackOpen(true)}
          />
        }
        contentPadding={0}
      >
        {/* ErrorBoundary remonta ao trocar de tela (key): erro numa tela não
            derruba o shell nem as outras — e nunca vira página em branco. */}
        <ErrorBoundary key={screen}>
          {screen === 'Dashboard' && <DashboardPage parameter={parameter} />}
          {screen === 'Folder' && <FolderPage parameter={parameter} />}
          {screen === 'Upload' && <UploadPage parameter={parameter} />}
          {screen === 'Recording' && <RecordingPage />}
          {screen === 'Editor' && <EditorPage parameter={parameter} isDark={isDark} />}
          {screen === 'Settings' && <SettingsPage />}
        </ErrorBoundary>
      </AppShell>

      <ConfirmDialog request={confirm} onResolve={resolveConfirm} />
      {FOLDERS_ENABLED && (
        <NewFolderDialog isOpen={newFolderOpen} onOpenChange={setNewFolderOpen} />
      )}
      <FeedbackDialog isOpen={feedbackOpen} onOpenChange={setFeedbackOpen} />
      <ModelDownloadDialog />
    </Theme>
  );
}
