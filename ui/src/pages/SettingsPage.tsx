import { useCallback, useEffect, useState } from 'react';
import { Button } from '@astryxdesign/core/Button';
import { Heading } from '@astryxdesign/core/Heading';
import { Selector } from '@astryxdesign/core/Selector';
import { Switch } from '@astryxdesign/core/Switch';
import { Text } from '@astryxdesign/core/Text';
import { invoke } from '../lib/bridge';
import type { SettingsData } from '../types';

type SettingsChanges = Partial<Pick<SettingsData,
  'defaultLanguage' | 'liveTranscriptionEnabled'
  | 'defaultEngine' | 'defaultParakeetModel' | 'defaultQuality' | 'device'
>>;

export function SettingsPage() {
  const [data, setData] = useState<SettingsData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [verifying, setVerifying] = useState(false);
  const [runtimeMessage, setRuntimeMessage] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setData(await invoke<SettingsData>('settings.load'));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao carregar as configurações.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const update = async (changes: SettingsChanges) => {
    if (saving) return;
    setSaving(true);
    setError(null);
    try {
      setData(await invoke<SettingsData>('settings.update', changes));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao salvar as configurações.');
    } finally {
      setSaving(false);
    }
  };

  const verifyRuntime = async () => {
    if (!data || verifying) return;
    setVerifying(true);
    setRuntimeMessage(null);
    try {
      const { message } = await invoke<{ message: string }>('settings.verifyRuntime', {
        device: data.device,
        quality: data.defaultQuality,
      }, 5 * 60_000);
      setRuntimeMessage(message);
    } catch (err) {
      setRuntimeMessage(err instanceof Error ? err.message : 'Falha ao verificar o Whisper.');
    } finally {
      setVerifying(false);
    }
  };

  const openLogs = async () => {
    setError(null);
    try {
      await invoke('settings.openLogs');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao abrir a pasta de logs.');
    }
  };

  const controlsDisabled = saving || verifying;

  return (
    <div className="settings-scroll">
      <div className="settings-page">
        <header className="settings-heading">
          <Heading level={1}>Configurações</Heading>
          {saving && <Text size="sm" color="secondary" role="status">Salvando…</Text>}
        </header>

        {error && <p className="settings-error" role="alert">{error}</p>}

        {!data ? (
          error
            ? <Button variant="secondary" label="Tentar novamente" onClick={() => void load()} />
            : <Text color="secondary" role="status">Carregando…</Text>
        ) : (
          <>
            <section className="settings-section" aria-labelledby="settings-transcription">
              <Heading level={2} id="settings-transcription">Transcrição</Heading>
              <div className="settings-row settings-select-row">
                <span aria-hidden="true">Idioma padrão</span>
                <Selector
                  label="Idioma padrão"
                  isLabelHidden
                  options={data.languages.map(({ code, label }) => ({ value: code, label }))}
                  value={data.defaultLanguage}
                  onChange={(defaultLanguage) => void update({ defaultLanguage })}
                  isDisabled={saving}
                  width="100%"
                />
              </div>
            </section>

            <section className="settings-section" aria-labelledby="settings-recording">
              <Heading level={2} id="settings-recording">Gravação</Heading>
              <div className="settings-row">
                <Switch
                  label="Transcrição ao vivo"
                  value={data.liveTranscriptionEnabled}
                  onChange={(liveTranscriptionEnabled) => void update({ liveTranscriptionEnabled })}
                  isDisabled={saving}
                  labelPosition="start"
                  labelSpacing="spread"
                  width="100%"
                />
              </div>
            </section>

            <details className="settings-details">
              <summary>Modelos e desempenho</summary>
              <div className="settings-advanced">
                <div className="settings-row settings-select-row">
                  <span aria-hidden="true">Motor de transcrição</span>
                  <Selector
                    label="Motor de transcrição"
                    isLabelHidden
                    options={data.engines.map(({ value, label, hint }) => ({ value, label, description: hint }))}
                    value={data.defaultEngine}
                    onChange={(defaultEngine) => void update({ defaultEngine })}
                    isDisabled={controlsDisabled}
                    width="100%"
                  />
                </div>
                {data.defaultEngine === 'Parakeet' ? (
                  <div className="settings-row settings-select-row">
                    <span aria-hidden="true">Modelo Parakeet</span>
                    <Selector
                      label="Modelo Parakeet"
                      isLabelHidden
                      options={data.parakeetModels.map(({ value, label, sizeLabel }) => ({
                        value, label, description: sizeLabel,
                      }))}
                      value={data.defaultParakeetModel}
                      onChange={(defaultParakeetModel) => void update({ defaultParakeetModel })}
                      isDisabled={controlsDisabled}
                      width="100%"
                    />
                  </div>
                ) : (
                  <div className="settings-row settings-select-row">
                    <span aria-hidden="true">Perfil do Whisper</span>
                    <Selector
                      label="Perfil do Whisper"
                      isLabelHidden
                      options={data.models.map(({ value, label, sizeLabel, speedHint }) => ({
                        value,
                        label,
                        description: [sizeLabel, speedHint, value === data.recommendation.quality && 'Recomendado']
                          .filter(Boolean).join(' · '),
                      }))}
                      value={data.defaultQuality}
                      onChange={(defaultQuality) => void update({ defaultQuality })}
                      isDisabled={controlsDisabled}
                      width="100%"
                    />
                  </div>
                )}
                <div className="settings-row settings-select-row">
                  <span aria-hidden="true">Dispositivo de execução</span>
                  <Selector
                    label="Dispositivo de execução"
                    isLabelHidden
                    options={data.devices}
                    value={data.device}
                    onChange={(device) => void update({ device })}
                    isDisabled={controlsDisabled}
                    width="100%"
                  />
                </div>
              </div>
            </details>

            <details className="settings-details">
              <summary>Diagnóstico</summary>
              <div className="settings-diagnostics">
                <dl>
                  <div><dt>Versão do Verso</dt><dd>{data.developer.appVersion}</dd></div>
                  {data.developer.gpus.map((gpu) => (
                    <div key={gpu.name}><dt>GPU</dt><dd>{gpu.name} · {gpu.ramLabel}</dd></div>
                  ))}
                  {data.developer.isRuntimeLoaded && (
                    <div><dt>Runtime do Whisper</dt><dd>{data.developer.loadedRuntime} · {data.developer.loadedBackend}</dd></div>
                  )}
                </dl>
                <div className="settings-actions">
                  <Button size="sm" variant="secondary" label="Abrir pasta de logs" onClick={() => void openLogs()} />
                  <Button size="sm" variant="secondary" label="Verificar Whisper"
                    isDisabled={controlsDisabled} onClick={() => void verifyRuntime()} />
                </div>
                {(verifying || runtimeMessage) && (
                  <p className="settings-runtime-message" role="status">
                    {verifying ? 'Verificando o Whisper…' : runtimeMessage}
                  </p>
                )}
              </div>
            </details>
          </>
        )}
      </div>
    </div>
  );
}
