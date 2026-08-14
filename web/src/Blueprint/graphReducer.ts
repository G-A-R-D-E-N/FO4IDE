import type { BpDocument, BpNode, BpNodeDef, BpWire } from './graphModel';
import { newId } from './graphModel';






export interface BpSelection {
  nodes: string[];
  wires: string[];
}

export interface GraphState {
  doc: BpDocument;
  selection: BpSelection;
  past: BpDocument[];
  future: BpDocument[];
}

export type GraphAction =
  | { type: 'LOAD'; doc: BpDocument }
  | { type: 'SET_HEADER'; header: Partial<BpDocument['header']> }
  | { type: 'SET_VARIABLES'; variables: BpDocument['variables'] }
  | { type: 'ADD_NODE'; node: BpNode; wire?: Omit<BpWire, 'id'> }
  | { type: 'MOVE_NODES'; ids: string[]; dx: number; dy: number }
  | { type: 'SET_POSITIONS'; positions: Record<string, { x: number; y: number }> }
  | { type: 'SET_PIN_VALUE'; nodeId: string; pinId: string; valueType: string; value: string }
  | { type: 'SET_CONFIG'; nodeId: string; key: string; value: string }
  | { type: 'ADD_WIRE'; wire: Omit<BpWire, 'id'>; defs: Record<string, BpNodeDef> }
  | { type: 'DELETE_SELECTION' }
  | { type: 'PASTE'; nodes: BpNode[]; wires: BpWire[]; dx: number; dy: number }
  | { type: 'SELECT'; ids: string[]; wires?: string[]; additive?: boolean }
  | { type: 'SELECT_NONE' }
  | { type: 'UNDO' }
  | { type: 'REDO' };

const HISTORY_LIMIT = 100;

export const initialState = (doc: BpDocument): GraphState => ({
  doc,
  selection: { nodes: [], wires: [] },
  past: [],
  future: [],
});

const pushHistory = (state: GraphState, doc: BpDocument): GraphState => ({
  ...state,
  doc,
  past: [...state.past, state.doc].slice(-HISTORY_LIMIT),
  future: [],
});

export function graphReducer(state: GraphState, action: GraphAction): GraphState {
  switch (action.type) {
    case 'LOAD':
      return initialState(action.doc);

    case 'SET_HEADER':
      return pushHistory(state, {
        ...state.doc,
        header: { ...state.doc.header, ...action.header },
      });

    case 'SET_VARIABLES':
      return pushHistory(state, { ...state.doc, variables: action.variables });

    case 'ADD_NODE': {
      const wires = action.wire
        ? [...state.doc.wires, { id: newId('w'), ...action.wire }]
        : state.doc.wires;
      return {
        ...pushHistory(state, {
          ...state.doc,
          nodes: [...state.doc.nodes, action.node],
          wires,
        }),
        selection: { nodes: [action.node.id], wires: [] },
      };
    }

    case 'MOVE_NODES': {


      const moving = new Set(action.ids);
      return pushHistory(state, {
        ...state.doc,
        nodes: state.doc.nodes.map((n) =>
          moving.has(n.id) ? { ...n, x: n.x + action.dx, y: n.y + action.dy } : n,
        ),
      });
    }

    case 'SET_PIN_VALUE':
      return pushHistory(state, {
        ...state.doc,
        nodes: state.doc.nodes.map((n) =>
          n.id !== action.nodeId
            ? n
            : {
                ...n,
                pinValues: {
                  ...(n.pinValues ?? {}),
                  [action.pinId]: { type: action.valueType, value: action.value },
                },
              },
        ),
      });

    case 'SET_CONFIG':
      return pushHistory(state, {
        ...state.doc,
        nodes: state.doc.nodes.map((n) =>
          n.id !== action.nodeId
            ? n
            : { ...n, config: { ...(n.config ?? {}), [action.key]: action.value } },
        ),
      });

    case 'ADD_WIRE': {


      const { wire, defs } = action;
      const fromDef = defs[wire.from.node];
      const toDef = defs[wire.to.node];
      const fromPin = fromDef?.pins.find((p) => p.id === wire.from.pin);
      const toPin = toDef?.pins.find((p) => p.id === wire.to.pin);
      if (!fromPin || !toPin) return state;

      const kept = state.doc.wires.filter((w) => {
        if (toPin.kind === 'data'
            && w.to.node === wire.to.node && w.to.pin === wire.to.pin) return false;
        if (fromPin.kind === 'exec'
            && w.from.node === wire.from.node && w.from.pin === wire.from.pin) return false;
        return true;
      });

      return pushHistory(state, {
        ...state.doc,
        wires: [...kept, { id: newId('w'), ...wire }],
      });
    }

    case 'DELETE_SELECTION': {
      const nodes = new Set(state.selection.nodes);
      const wires = new Set(state.selection.wires);
      if (nodes.size === 0 && wires.size === 0) return state;

      return {
        ...pushHistory(state, {
          ...state.doc,
          nodes: state.doc.nodes.filter((n) => !nodes.has(n.id)),
          wires: state.doc.wires.filter(
            (w) => !wires.has(w.id) && !nodes.has(w.from.node) && !nodes.has(w.to.node),
          ),
        }),
        selection: { nodes: [], wires: [] },
      };
    }

    case 'SET_POSITIONS': {


      const moved = action.positions;
      return pushHistory(state, {
        ...state.doc,
        nodes: state.doc.nodes.map((n) => (moved[n.id] ? { ...n, ...moved[n.id] } : n)),
      });
    }

    case 'PASTE': {
      const remap = new Map(action.nodes.map((n) => [n.id, newId('n')]));
      const nodes = action.nodes.map((n) => ({
        ...n,
        id: remap.get(n.id)!,
        x: n.x + action.dx,
        y: n.y + action.dy,
      }));
      const wires = action.wires
        .filter((w) => remap.has(w.from.node) && remap.has(w.to.node))
        .map((w) => ({
          id: newId('w'),
          from: { node: remap.get(w.from.node)!, pin: w.from.pin },
          to: { node: remap.get(w.to.node)!, pin: w.to.pin },
        }));

      return {
        ...pushHistory(state, {
          ...state.doc,
          nodes: [...state.doc.nodes, ...nodes],
          wires: [...state.doc.wires, ...wires],
        }),
        selection: { nodes: nodes.map((n) => n.id), wires: [] },
      };
    }

    case 'SELECT':

      return {
        ...state,
        selection: action.additive
          ? {
              nodes: Array.from(new Set([...state.selection.nodes, ...action.ids])),
              wires: Array.from(new Set([...state.selection.wires, ...(action.wires ?? [])])),
            }
          : { nodes: action.ids, wires: action.wires ?? [] },
      };

    case 'SELECT_NONE':
      return { ...state, selection: { nodes: [], wires: [] } };

    case 'UNDO': {
      if (state.past.length === 0) return state;
      const doc = state.past[state.past.length - 1];
      return {
        doc,
        selection: { nodes: [], wires: [] },
        past: state.past.slice(0, -1),
        future: [state.doc, ...state.future].slice(0, HISTORY_LIMIT),
      };
    }

    case 'REDO': {
      if (state.future.length === 0) return state;
      const [doc, ...rest] = state.future;
      return {
        doc,
        selection: { nodes: [], wires: [] },
        past: [...state.past, state.doc].slice(-HISTORY_LIMIT),
        future: rest,
      };
    }

    default:
      return state;
  }
}
