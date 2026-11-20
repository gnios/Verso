import { useCallback, useEffect, useState } from 'react';
import { Badge } from '@astryxdesign/core/Badge';
import { Button } from '@astryxdesign/core/Button';
import { Card } from '@astryxdesign/core/Card';
import { CheckboxInput } from '@astryxdesign/core/CheckboxInput';
import { HStack } from '@astryxdesign/core/HStack';
import { Heading } from '@astryxdesign/core/Heading';
import { Icon } from '@astryxdesign/core/Icon';
import { Selector } from '@astryxdesign/core/Selector';
import { Text } from '@astryxdesign/core/Text';
import { TextInput } from '@astryxdesign/core/TextInput';
import { VStack } from '@astryxdesign/core/VStack';
import { invoke } from '../lib/bridge';
import type { SettingsData } from '../types';
import { RefreshIcon } from '../icons';

export function SettingsPage() {
  const [data, setData] = useState<SettingsData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
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

  const update = async (changes: Record<string, unknown>) => {
    setSaving(true);
    try {
      const next = await invoke<SettingsData>('settings.update', changes);
      setData(next);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao salvar.');
    } finally {
      setSaving(false);
    }
  };

  const verifyRuntime = async () => {
    if (!data) {
      return;
    }
    setRuntimeMessage('Verificando…');
    try {
      const result = await invoke<{ message: string }>('settings.verifyRuntime', {
        device: data.device,
        quality: data.defaultQuality,
      }, 5 * 60_000);
      setRuntimeMessage(result.message);
    } catch (err) {
      setRuntimeMessage(err instanceof Error ? err.message : 'Falha na verificação.');
    }
    void load();
  };

  if (!data) {
    return (
      <VStack padding={6} align="start" gap={2}>
        <Heading level={1}>Configurações</Heading>
        {error ? (
          <>
            <Text color="secondary">{error}</Text>
            <Button variant="primary" label="Tentar de novo" onClick={() => void load()} />
          </>
        ) : (
          <Text color="secondary">Carregando…</Text>
        )}
      </VStack>
    );
  }

  const recommended = data.models.find((model) => model.value === data.recommendation.quality);

  return (
    <VStack gap={4} padding={4} align="start" isScrollable>
      <VStack gap={1} align="start">
        <Heading level={1}>Configurações</Heading>
        <Text color="secondary">Preferências de transcrição e perfil de uso.</Text>
      </VStack>

      <HStack gap={4} align="start" wrap="wrap">
        {/* Perfil */}
        <Card padding={4} width={420}>
          <VStack gap={3} align="start">
            <Heading level={3}>Perfil</Heading>
            <TextInput
              label="Nome"
              value={data.profile.name}
              onChange={(name) => setData({ ...data, profile: { ...data.profile, name } })}
              onBlur={() => void update({ name: data.profile.name })}
            />
            <TextInput
              label="E-mail"
              value={data.profile.email}
              onChange={(email) => setData({ ...data, profile: { ...data.profile, email } })}
              onBlur={() => void update({ email: data.profile.email })}
            />
            <TextInput
              label="Instituição"
              value={data.profile.institution}
              onChange={(institution) => setData({ ...data, profile: { ...data.profile, institution } })}
              onBlur={() => void update({ institution: data.profile.institution })}
            />
          </VStack>
        </Card>

        {/* Preferências */}
        <Card padding={4} width={420}>
          <VStack gap={3} align="start">
            <Heading level={3}>Preferências</Heading>
            <Selector
              label="Idioma padrão"
              options={data.languages.map((language) => ({ value: language.code, label: language.label }))}
              value={data.defaultLanguage}
              onChange={(defaultLanguage) => void update({ defaultLanguage })}
              width="100%"
            />
            <CheckboxInput
              label="Identificar locutores por padrão"
              value={data.identifySpeakersDefault}
              onChange={(identifySpeakersDefault) => void update({ identifySpeakersDefault })}
            />
            <CheckboxInput
              label="Transcrição ao vivo durante a gravação"
              value={data.liveTranscriptionEnabled}
              onChange={(liveTranscriptionEnabled) => void update({ liveTranscriptionEnabled })}
            />
          </VStack>
        </Card>
      </HStack>

      {/* Avançado: motor + dispositivo */}
      <Card padding={4} width="100%">
        <VStack gap={3} align="start">
          <Heading level={3}>Avançado</Heading>
          <HStack gap={4} wrap="wrap">
            <VStack gap={2} align="start">
              <Selector
                label="Motor de transcrição"
                options={data.engines.map((engine) => ({
                  value: engine.value,
                  label: engine.label,
                  description: engine.hint,
                }))}
                value={data.defaultEngine}
                onChange={(defaultEngine) => void update({ defaultEngine })}
              />
              {data.defaultEngine === 'Parakeet' ? (
                <Selector
                  label="Modelo Parakeet"
                  options={data.parakeetModels.map((model) => ({
                    value: model.value,
                    label: model.label,
                    description: model.sizeLabel,
                  }))}
                  value={data.defaultParakeetModel}
                  onChange={(defaultParakeetModel) => void update({ defaultParakeetModel })}
                />
              ) : null}
            </VStack>
            <VStack gap={2} align="start">
              <Selector
                label="Dispositivo de execução"
                options={data.devices.map((device) => ({ value: device.value, label: device.label }))}
                value={data.device}
                onChange={(device) => void update({ device })}
              />
              <Text size="sm" color="secondary">{data.developer.runtimePreference}</Text>
            </VStack>
          </HStack>
          {/* Perfil de precisão só se aplica ao Whisper — o Parakeet ignora
              ModelQuality (a escolha é o modelo TAGARELA/TDT v3 acima). */}
          {data.defaultEngine === 'Whisper' ? (
            <VStack gap={2} align="start" width="100%">
              <HStack gap={2} align="center">
                <Text size="sm" color="secondary">Perfil de precisão (Whisper)</Text>
                {recommended ? (
                  <Badge variant="success" label={`Recomendado: ${recommended.label}`} />
                ) : null}
              </HStack>
              {data.recommendation.detectedRamGb > 0 ? (
                <Text size="sm" color="secondary">
                  {data.recommendation.reason} · RAM detectada: {data.recommendation.detectedRamGb} GB
                </Text>
              ) : null}
              <HStack gap={3} align="start" wrap="wrap">
                {data.models.map((model) => (
                  <Card
                    key={model.value}
                    variant={data.defaultQuality === model.value ? 'blue' : 'default'}
                    padding={3}
                    width={260}
                    className="verso-card"
                    onClick={() => void update({ defaultQuality: model.value })}
                  >
                    <VStack gap={1} align="start">
                      <HStack justify="between" width="100%">
                        <Heading level={4}>{model.label}</Heading>
                        <Badge label={model.sizeLabel} />
                      </HStack>
                      <Text size="sm" color="secondary">{model.speedHint}</Text>
                      <Text size="sm">{model.whenToUse}</Text>
                    </VStack>
                  </Card>
                ))}
              </HStack>
            </VStack>
          ) : null}
        </VStack>
      </Card>

      {/* Desenvolvedor */}
      <Card padding={4} width="100%">
        <VStack gap={3} align="start">
          <HStack justify="between" width="100%">
            <Heading level={3}>Desenvolvedor</Heading>
            <Button
              size="sm"
              variant="ghost"
              label="Atualizar"
              icon={<Icon icon={RefreshIcon} size="sm" />}
              onClick={() => void load()}
            />
          </HStack>
          <HStack gap={2} wrap="wrap">
            <Badge label={`Versão ${data.developer.appVersion}`} />
            <Badge label={data.developer.isRuntimeLoaded ? `Runtime: ${data.developer.loadedRuntime}` : 'Runtime não carregado'} />
            <Badge label={`Backend ${data.developer.loadedBackend}`} />
            <Badge label={`GPU ativa: ${data.developer.activeGpu}`} />
          </HStack>
          {data.developer.gpus.length > 0 ? (
            <VStack gap={1} align="start">
              {data.developer.gpus.map((gpu) => (
                <Text key={gpu.name} size="sm" color="secondary">
                  {gpu.name} · {gpu.kindLabel} · {gpu.ramLabel}
                </Text>
              ))}
            </VStack>
          ) : (
            <Text size="sm" color="secondary">Nenhuma GPU detectada.</Text>
          )}
          <HStack gap={2}>
            <Button
              size="sm"
              variant="secondary"
              label="Verificar runtime"
              onClick={() => void verifyRuntime()}
            />
            <Button
              size="sm"
              variant="secondary"
              label="Abrir pasta de logs"
              onClick={() => void invoke('settings.openLogs')}
            />
          </HStack>
          {runtimeMessage ? <Text size="sm">{runtimeMessage}</Text> : null}
          <Text size="sm" color="secondary">
            {data.developer.updateStatus} · Logs: {data.developer.logDirectory}
          </Text>
        </VStack>
      </Card>

      {saving ? <Text size="sm" color="secondary">Salvando…</Text> : null}
      {error ? <Text size="sm" color="secondary">{error}</Text> : null}
    </VStack>
  );
}