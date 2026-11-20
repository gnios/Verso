import { Icon } from '@astryxdesign/core/Icon';
import { SegmentedControl, SegmentedControlItem } from '@astryxdesign/core/SegmentedControl';
import { GridIcon, ListIcon } from '../icons';
import type { LibraryViewMode } from '../lib/viewMode';

interface LibraryViewToggleProps {
  value: LibraryViewMode;
  onChange: (value: string) => void;
}

export function LibraryViewToggle({ value, onChange }: LibraryViewToggleProps) {
  return (
    <SegmentedControl size="sm" value={value} onChange={onChange} label="Modo de visualização">
      <SegmentedControlItem
        value="table"
        label="Tabela"
        icon={<Icon icon={ListIcon} />}
      />
      <SegmentedControlItem
        value="cards"
        label="Cartões"
        icon={<Icon icon={GridIcon} />}
      />
    </SegmentedControl>
  );
}
