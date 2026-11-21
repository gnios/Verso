import { useCallback, useEffect, useState } from 'react';
import { Button } from '@astryxdesign/core/Button';
import { Heading } from '@astryxdesign/core/Heading';
import { Text } from '@astryxdesign/core/Text';
import { invoke, onEvent } from '../lib/bridge';
import type { UpdateDetails } from '../types';

export function UpdateSettings() {
  const [details, setDetails] = useState<UpdateDetails | null>(null);
  const [requesting, setRequesting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(() => {
    void invoke<UpdateDetails>('updates.load')
      .then(setDetails)
      .catch(() => setError('Não foi possível consultar o estado da atualização.'));
  }, []);

  useEffect(() => {
    const unsubscribe = onEvent('sidebar.updateChanged', refresh);
    refresh();
    return unsubscribe;
  }, [refresh]);

  const requestUpdate = async (method: 'updates.check' | 'sidebar.updateNow') => {
    setRequesting(true);
    setError(null);
    try {
      await invoke(method, undefined, 30 * 60_000);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Não foi possível iniciar a atualização.');
    } finally {
      setRequesting(false);
      refresh();
    }
  };

  return (
    <section className="settings-section" aria-labelledby="settings-updates">
      <Heading level={2} id="settings-updates">Atualizações</Heading>
      <div className="settings-row settings-update-row">
        <div className="settings-update-summary">
          {details && <Text>Versão instalada {details.version}</Text>}
          {details && <Text>Última versão publicada {details.latestVersion ?? '—'}</Text>}
          <Text size="sm" color="secondary" role="status">
            {details?.status ?? (error ? '' : 'Consultando o estado da atualização…')}
          </Text>
        </div>
        {details?.hasChannel && (
          <div className="settings-actions">
            <Button size="sm" variant="secondary" label="Verificar atualizações"
              isDisabled={requesting || !details.action.canUpdateNow}
              onClick={() => void requestUpdate('updates.check')} />
            {details.action.isReady && (
              <Button size="sm" variant="primary" label={details.action.label}
                isDisabled={requesting || !details.action.canUpdateNow}
                onClick={() => void requestUpdate('sidebar.updateNow')} />
            )}
          </div>
        )}
      </div>
      {error && <p className="settings-error" role="alert">{error}</p>}
    </section>
  );
}
