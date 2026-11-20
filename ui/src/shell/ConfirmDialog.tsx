import { AlertDialog } from '@astryxdesign/core/AlertDialog';
import type { ConfirmRequest } from '../types';

interface ConfirmDialogProps {
  request: ConfirmRequest | null;
  onResolve: (ok: boolean) => void;
}

export function ConfirmDialog({ request, onResolve }: ConfirmDialogProps) {
  return (
    <AlertDialog
      isOpen={request !== null}
      onOpenChange={(open) => {
        if (!open) {
          onResolve(false);
        }
      }}
      title={request?.title ?? ''}
      description={request?.message ?? ''}
      actionLabel="Confirmar"
      cancelLabel="Cancelar"
      actionVariant={request?.danger ? 'destructive' : 'primary'}
      onAction={() => onResolve(true)}
    />
  );
}