import { useCallback, useEffect, useState } from 'react';
import { Badge } from '@astryxdesign/core/Badge';
import { Button } from '@astryxdesign/core/Button';
import { IconButton } from '@astryxdesign/core/IconButton';
import { Icon } from '@astryxdesign/core/Icon';
import { SideNav, SideNavCollapseButton, SideNavHeading, SideNavItem, SideNavSection } from '@astryxdesign/core/SideNav';
import { Text } from '@astryxdesign/core/Text';
import { invoke, onEvent } from '../lib/bridge';
import { FOLDERS_ENABLED, RECORDING_ENABLED } from '../lib/features';
import { navigateTo, type ScreenKey, type NavigationParam } from '../lib/screens';
import type { AppInfo, SidebarData } from '../types';
import {
  AlertCircleIcon,
  CheckCircleIcon,
  ClockIcon,
  FileIcon,
  FolderIcon,
  GridIcon,
  MessageIcon,
  MicIcon,
  MoonIcon,
  PlusIcon,
  SettingsIcon,
  SunIcon,
} from '../icons';

interface SidebarNavProps {
  screen: ScreenKey;
  parameter: NavigationParam;
  isDark: boolean;
  appInfo: AppInfo | null;
  onToggleTheme: () => void;
  onNewFolder: () => void;
  onFeedback: () => void;
}

function countBadge(count: number | undefined): React.ReactNode {
  return <Badge label={count ?? 0} />;
}

export function SidebarNav({ screen, parameter, isDark, appInfo, onToggleTheme, onNewFolder, onFeedback }: SidebarNavProps) {
  const [sidebar, setSidebar] = useState<SidebarData | null>(null);
  const [isCollapsed, setIsCollapsed] = useState(false);

  const refresh = useCallback(() => {
    void invoke<SidebarData>('sidebar.load')
      .then(setSidebar)
      .catch(() => undefined);
  }, []);

  useEffect(() => {
    refresh();
    const offNavigation = onEvent('navigation.changed', refresh);
    const offDashboard = onEvent('dashboard.changed', refresh);
    return () => {
      offNavigation();
      offDashboard();
    };
  }, [refresh]);

  const counts = sidebar?.counts;
  const onDashboard = (parameter: Omit<NavigationParam, 'screen'>) =>
    navigateTo('Dashboard', parameter);

  const selectedStatus = parameter.statusFilter ?? 'All';
  const selectedUnassigned = !!parameter.unassignedOnly;

  // No rail colapsado, o botão de expandir fica no footerIcons (o botão do
  // header some junto com o texto) — assim só existe um botão por estado.
  return (
    <SideNav
      header={
        <SideNavHeading
          heading="Verso"
          icon={<img src="/brand/verso-logo.png" alt="" style={{ width: 26, height: 26 }} />}
          headerEndContent={
            <>
              <IconButton
                icon={<Icon icon={isDark ? SunIcon : MoonIcon} />}
                label={isDark ? 'Alternar para tema claro' : 'Alternar para tema escuro'}
                variant="ghost"
                onClick={onToggleTheme}
              />
              <SideNavCollapseButton />
            </>
          }
        />
      }
      topContent={
        <Button
          variant="primary"
          width="100%"
          label="Nova transcrição"
          icon={<Icon icon={PlusIcon} />}
          onClick={() => void navigateTo('Upload')}
        />
      }
      footer={
        <>
          <SideNavItem
            label="Feedback"
            icon={MessageIcon}
            onClick={onFeedback}
          />
          {RECORDING_ENABLED && (
            <SideNavItem
              label="Gravação"
              icon={MicIcon}
              isSelected={screen === 'Recording'}
              onClick={() => void navigateTo('Recording')}
            />
          )}
          <SideNavItem
            label="Configurações"
            icon={SettingsIcon}
            isSelected={screen === 'Settings'}
            onClick={() => void navigateTo('Settings')}
          />
          <Text size="sm" color="secondary" style={{ display: 'block', textAlign: 'right' }}>
            {sidebar?.versionLabel ?? (appInfo ? `v${appInfo.version}` : '')}
          </Text>
        </>
      }
      collapsible={{ isCollapsed, onCollapsedChange: setIsCollapsed, hasButton: false }}
      footerIcons={isCollapsed ? <SideNavCollapseButton /> : null}
    >
      {FOLDERS_ENABLED && (
      <SideNavSection
        title="Pastas"
        endContent={
          <IconButton
            icon={<Icon icon={PlusIcon} size="sm" />}
            label="Nova pasta"
            variant="ghost"
            size="sm"
            onClick={onNewFolder}
          />
        }
      >
        <SideNavItem
          label="Sem pasta"
          icon={FolderIcon}
          endContent={countBadge(counts?.unassigned)}
          isSelected={screen === 'Dashboard' && selectedUnassigned}
          onClick={() => void onDashboard({ unassignedOnly: true })}
        />
        {sidebar?.folders.map((folder) => (
          <SideNavItem
            key={folder.id}
            label={folder.title}
            icon={FolderIcon}
            endContent={countBadge(folder.transcriptionCount)}
            isSelected={screen === 'Folder' && parameter.folderId === folder.id}
            collapsible
          >
            {folder.transcriptions.map((transcription) => (
              <SideNavItem
                key={transcription.id}
                label={transcription.title}
                icon={transcription.icon ? undefined : FileIcon}
                endContent={transcription.isDone ? undefined : <Text size="sm">{transcription.statusIcon}</Text>}
                onClick={() => void navigateTo('Editor', { transcriptionId: transcription.id })}
              />
            ))}
          </SideNavItem>
        ))}
      </SideNavSection>
      )}

      <SideNavSection title="Transcrições">
        <SideNavItem
          label="Todas"
          icon={GridIcon}
          endContent={countBadge(counts?.total)}
          isSelected={screen === 'Dashboard' && selectedStatus === 'All' && !selectedUnassigned}
          onClick={() => void onDashboard({ statusFilter: 'All' })}
        />
        <SideNavItem
          label="Em andamento"
          icon={ClockIcon}
          endContent={countBadge(counts?.inProgress)}
          isSelected={screen === 'Dashboard' && selectedStatus === 'Progress'}
          onClick={() => void onDashboard({ statusFilter: 'Progress' })}
        />
        <SideNavItem
          label="Concluídas"
          icon={CheckCircleIcon}
          endContent={countBadge(counts?.done)}
          isSelected={screen === 'Dashboard' && selectedStatus === 'Done'}
          onClick={() => void onDashboard({ statusFilter: 'Done' })}
        />
        <SideNavItem
          label="Com erro"
          icon={AlertCircleIcon}
          endContent={countBadge(counts?.error)}
          isSelected={screen === 'Dashboard' && selectedStatus === 'Error'}
          onClick={() => void onDashboard({ statusFilter: 'Error' })}
        />
      </SideNavSection>

      <SideNavSection title="Tags">
        {sidebar?.tags.map((tag) => (
          <SideNavItem
            key={tag.id}
            label={tag.name}
            endContent={<Text size="sm">{tag.count}</Text>}
            isSelected={screen === 'Dashboard' && parameter.tagId === tag.id}
            onClick={() => void onDashboard({ tagId: tag.id })}
          />
        ))}
      </SideNavSection>
    </SideNav>
  );
}