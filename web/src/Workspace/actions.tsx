import { Copy, Eraser, Hash, Minimize2, Trash2 } from 'lucide-react';

export interface WorkspaceAction {
  id: string;
  label: string;
  icon: React.ReactNode;
  danger?: boolean;
  run: () => void;
}

export function buildActions(handlers: {
  copyAsOverride: () => void;
  changeFormId: () => void;
  compactToEsl: () => void;
  cleanUdr: () => void;
  deleteRecord: () => void;
}): WorkspaceAction[] {
  return [
    { id: 'copy', label: 'Copy as override into...', icon: <Copy size={13} />, run: handlers.copyAsOverride },
    { id: 'formid', label: 'Change FormID...', icon: <Hash size={13} />, run: handlers.changeFormId },
    { id: 'esl', label: 'Compact to ESL', icon: <Minimize2 size={13} />, run: handlers.compactToEsl },
    { id: 'udr', label: 'Clean UDR', icon: <Eraser size={13} />, run: handlers.cleanUdr },
    { id: 'delete', label: 'Remove', icon: <Trash2 size={13} />, danger: true, run: handlers.deleteRecord },
  ];
}
