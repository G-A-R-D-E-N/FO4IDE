import React from 'react';
import { AlertTriangle } from 'lucide-react';
import type { BpDiagnostic, BpNode, BpNodeDef, BpPinDef } from './graphModel';
import { NODE_WIDTH, isArrayType, pinKey } from './graphModel';
import type { GraphAction } from './graphReducer';
import { pinColorVar } from './pinColor';

interface Props {
  node: BpNode;
  def?: BpNodeDef;
  selected: boolean;
  diagnostics?: BpDiagnostic[];
  diagByPin: Map<string, BpDiagnostic[]>;
  connect: { node: string; pin: string; compatible: Set<string> } | null;
  registerElement: (element: HTMLDivElement | null) => void;
  onPointerDown: (event: React.PointerEvent, nodeId: string) => void;
  onPinPointerDown: (event: React.PointerEvent, nodeId: string, pin: BpPinDef) => void;
  dispatch: (action: GraphAction) => void;
}

function BlueprintNode({
  node, def, selected, diagnostics, diagByPin, connect,
  registerElement, onPointerDown, onPinPointerDown, dispatch,
}: Props) {
  const severity = worstSeverity(diagnostics);
  const inputs = def?.pins.filter((p) => p.dir === 'in') ?? [];
  const outputs = def?.pins.filter((p) => p.dir === 'out') ?? [];

  return (
    <div
      ref={registerElement}
      className={[
        'bp-node',
        selected ? 'bp-node-selected' : '',
        severity === 'error' ? 'bp-node-error' : '',
        severity === 'warning' ? 'bp-node-warning' : '',
        def?.isPure ? 'bp-node-pure' : '',
        !def ? 'bp-node-unknown' : '',
      ].filter(Boolean).join(' ')}
      style={{ transform: `translate(${node.x}px, ${node.y}px)`, width: NODE_WIDTH }}
      onPointerDown={(e) => onPointerDown(e, node.id)}
    >
      <div className="bp-node-header" title={def?.summary ?? undefined}>
        <span className="bp-node-title">{def?.label ?? node.def}</span>
        {severity && (
          <span className="bp-node-badge" title={(diagnostics ?? []).map((d) => d.message).join('\n')}>
            <AlertTriangle size={12} strokeWidth={2} />
          </span>
        )}
      </div>

      <div className="bp-node-body">
        <div className="bp-pins-in">
          {inputs.map((pin) => (
            <Pin
              key={pin.id}
              nodeId={node.id}
              pin={pin}
              node={node}
              connect={connect}
              diagnostics={diagByPin.get(pinKey(node.id, pin.id))}
              onPinPointerDown={onPinPointerDown}
              dispatch={dispatch}
            />
          ))}
        </div>
        <div className="bp-pins-out">
          {outputs.map((pin) => (
            <Pin
              key={pin.id}
              nodeId={node.id}
              pin={pin}
              node={node}
              connect={connect}
              diagnostics={diagByPin.get(pinKey(node.id, pin.id))}
              onPinPointerDown={onPinPointerDown}
              dispatch={dispatch}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

interface PinProps {
  nodeId: string;
  node: BpNode;
  pin: BpPinDef;
  connect: { node: string; pin: string; compatible: Set<string> } | null;
  diagnostics?: BpDiagnostic[];
  onPinPointerDown: (event: React.PointerEvent, nodeId: string, pin: BpPinDef) => void;
  dispatch: (action: GraphAction) => void;
}

function Pin({ nodeId, node, pin, connect, diagnostics, onPinPointerDown, dispatch }: PinProps) {
  const key = pinKey(nodeId, pin.id);
  const dimmed = connect != null && !connect.compatible.has(key)
    && !(connect.node === nodeId && connect.pin === pin.id);
  const compatible = connect != null && connect.compatible.has(key);
  const severity = worstSeverity(diagnostics);

  const editable = pin.kind === 'data' && pin.dir === 'in' && isEditable(pin.dataType);
  const current = node.pinValues?.[pin.id]?.value ?? '';

  return (
    <div
      className={[
        'bp-pin',
        pin.kind === 'exec' ? 'bp-pin-exec' : '',
        isArrayType(pin.dataType) ? 'bp-pin-array' : '',
        dimmed ? 'bp-pin-dim' : '',
        compatible ? 'bp-pin-compatible' : '',
        severity === 'error' ? 'bp-pin-error' : '',
      ].filter(Boolean).join(' ')}
      title={pin.description ?? `${pin.name}${pin.dataType ? ': ' + pin.dataType : ''}`}
    >
      <span
        className="bp-pin-hit bp-nodrag"
        data-node={nodeId}
        data-pin={pin.id}
        onPointerDown={(e) => onPinPointerDown(e, nodeId, pin)}
      >
        <span
          className="bp-pin-glyph"
          style={pin.kind === 'data' ? { color: pinColorVar(pin.dataType) } : undefined}
        />
      </span>
      <span className="bp-pin-label">{pin.name}</span>
      {editable && (
        <input
          className="bp-pin-literal bp-nodrag"
          defaultValue={current}
          placeholder={pin.defaultLiteral ?? ''}
          onPointerDown={(e) => e.stopPropagation()}
          onBlur={(e) => commit(e.currentTarget.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') e.currentTarget.blur();
            if (e.key === 'Escape') { e.currentTarget.value = current; e.currentTarget.blur(); }
          }}
        />
      )}
    </div>
  );

  function commit(value: string) {
    if (value === current) return;
    dispatch({
      type: 'SET_PIN_VALUE',
      nodeId,
      pinId: pin.id,
      valueType: literalKind(pin.dataType),
      value,
    });
  }
}

const worstSeverity = (diagnostics?: BpDiagnostic[]) =>
  !diagnostics || diagnostics.length === 0
    ? null
    : diagnostics.some((d) => d.severity === 'error') ? 'error' : 'warning';

const isEditable = (dataType: string) =>
  ['int', 'float', 'bool', 'string'].includes(dataType.toLowerCase());

const literalKind = (dataType: string) => dataType.toLowerCase();

export default React.memo(BlueprintNode);
