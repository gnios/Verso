import { Component, type ReactNode } from 'react';
import { Button } from '@astryxdesign/core/Button';
import { Heading } from '@astryxdesign/core/Heading';
import { Text } from '@astryxdesign/core/Text';
import { VStack } from '@astryxdesign/core/Stack';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  error: Error | null;
}

/**
 * Contém exceções de render: sem isso, qualquer erro desmonta a árvore inteira e a
 * janela fica em branco (o "app some do nada"). Mostra tela de recuperação com
 * reload do WebView — equivalente a reabrir a tela, sem reiniciar o processo.
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: { componentStack?: string | null }): void {
    console.error('[Verso] Erro de renderização:', error, info.componentStack);
  }

  render(): ReactNode {
    const { error } = this.state;
    if (error) {
      return (
        <VStack padding={8} align="center" gap={4} width="100%">
          <Heading level={2}>Algo deu errado</Heading>
          <Text type="body" color="secondary">
            {error.message}
          </Text>
          <Button label="Recarregar" variant="primary" onClick={() => window.location.reload()} />
        </VStack>
      );
    }

    return this.props.children;
  }
}
