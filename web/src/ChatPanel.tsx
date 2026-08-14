import { useState, useEffect, useRef, type KeyboardEvent, type ClipboardEvent } from 'react';
import { useDialogs } from './dialogs';
import { MessageSquare, X, Plus, Pencil, Trash2, Wrench, Brain, CornerDownRight, Image as ImageIcon, GitBranch, Square } from 'lucide-react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import type { ChatSessionMeta, ChatSessionFull, SlashCommand } from './backend';
import './ChatPanel.css';

interface ChatMsg { role: 'system' | 'user' | 'assistant'; text: string; images?: string[] }




const ACTIVITY = [
  { e: '🔧', kind: 'tool' as const },
  { e: '💭', kind: 'think' as const },
  { e: '↳', kind: 'result' as const },
  { e: '✍️', kind: 'write' as const },
];
function parseActivity(line: string) {
  const t = line.trim().replace(/^_+|_+$/g, '').trim();
  for (const a of ACTIVITY) if (t.startsWith(a.e)) return { kind: a.kind, text: t.slice(a.e.length).trim() };
  return null;
}

function RichText({ text }: { text: string }) {

  const blocks: Array<{ type: 'prose'; text: string } | { type: 'activity'; kind: string; text: string }> = [];
  let prose: string[] = [];
  const flush = () => { if (prose.join('').trim()) blocks.push({ type: 'prose', text: prose.join('\n') }); prose = []; };
  for (const line of text.split('\n')) {
    const act = parseActivity(line);
    if (act) { flush(); blocks.push({ type: 'activity', kind: act.kind, text: act.text }); }
    else prose.push(line);
  }
  flush();

  return (
    <>
      {blocks.map((b, i) => b.type === 'prose' ? (
        <div key={i} className="chat-md">
          <ReactMarkdown remarkPlugins={[remarkGfm]}>{b.text}</ReactMarkdown>
        </div>
      ) : (
        <div key={i} className={`chat-activity ${b.kind}`} title={b.text}>
          {b.kind === 'tool'  ? <Wrench size={11} />
           : b.kind === 'think' ? <Brain size={11} />
           : b.kind === 'write' ? <Pencil size={11} />
           : <CornerDownRight size={11} />}
          <span>{b.text}</span>
        </div>
      ))}
    </>
  );
}

const host = () => window.chrome?.webview?.hostObjects?.chat;

const appendToLastAssistant = (msgs: ChatMsg[], chunk: string): ChatMsg[] => {
  const last = msgs[msgs.length - 1];
  if (last && last.role === 'assistant') return [...msgs.slice(0, -1), { ...last, text: last.text + chunk }];
  return [...msgs, { role: 'assistant', text: chunk }];
};

const GREETING: ChatMsg = {
  role: 'system',
  text: "I'm Claude. Ask me about your load order, diagnose conflicts, or author patches. Type / for commands.",
};

export default function ChatPanel() {

  const { confirm: askConfirm, prompt: askPrompt } = useDialogs();
  const [buffers, setBuffers] = useState<Record<string, ChatMsg[]>>({});
  const [busyMap, setBusyMap] = useState<Record<string, boolean>>({});
  const [input, setInput] = useState('');
  const [sessions, setSessions] = useState<ChatSessionMeta[]>([]);
  const [currentId, setCurrentId] = useState('');
  const currentIdRef = useRef(currentId);
  currentIdRef.current = currentId;
  const busyMapRef = useRef(busyMap);
  busyMapRef.current = busyMap;
  const [commands, setCommands] = useState<SlashCommand[]>([]);
  const [cmdIndex, setCmdIndex] = useState(0);
  const [attachments, setAttachments] = useState<string[]>([]);
  const endRef = useRef<HTMLDivElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);


  const pendingQueues = useRef<Record<string, Array<{ text: string; imgs: string[] }>>>({});
  const [queueCounts, setQueueCounts] = useState<Record<string, number>>({});


  const messages = buffers[currentId] ?? [GREETING];
  const busy = !!busyMap[currentId];
  const queueCount = queueCounts[currentId] ?? 0;

  const setMsgs = (sid: string, fn: (m: ChatMsg[]) => ChatMsg[]) =>
    setBuffers(b => ({ ...b, [sid]: fn(b[sid] ?? [GREETING]) }));
  const setBusyFor = (sid: string, v: boolean) => setBusyMap(b => ({ ...b, [sid]: v }));
  const dtoToMsgs = (dto: ChatSessionFull): ChatMsg[] =>
    dto.messages.length === 0 ? [GREETING] : dto.messages.map(m => ({ role: m.isUser ? 'user' : 'assistant', text: m.text } as ChatMsg));


  const addImageFiles = (files: FileList | File[] | null) => {
    if (!files) return;
    for (const f of Array.from(files)) {
      if (!f.type.startsWith('image/')) continue;
      const reader = new FileReader();
      reader.onload = () => setAttachments(a => [...a, reader.result as string]);
      reader.readAsDataURL(f);
    }
  };

  const onPaste = (e: ClipboardEvent<HTMLInputElement>) => {
    const imgs = Array.from(e.clipboardData.items)
      .filter(it => it.kind === 'file' && it.type.startsWith('image/'))
      .map(it => it.getAsFile())
      .filter((f): f is File => !!f);
    if (imgs.length) { e.preventDefault(); addImageFiles(imgs); }
  };

  const renderSession = (dto: ChatSessionFull) => {
    setBuffers(b => ({ ...b, [dto.id]: dtoToMsgs(dto) }));
    setCurrentId(dto.id);
  };

  const refreshSessions = async () => {
    const c = host();
    if (!c) return;
    try { setSessions(JSON.parse(await c.ListSessions())); } catch {  }
  };


  const sendDirect = async (sid: string, text: string, imgs: string[]) => {
    const c = host();
    if (!c) return;

    if (text.startsWith('/')) {
      setMsgs(sid, p => [...p, { role: 'user', text }]);
      try { await c.SendMessage(sid, text, '[]'); }
      catch (e: any) { setMsgs(sid, p => [...p, { role: 'system', text: '⚠️ ' + (e?.message || e) }]); }
      refreshSessions();
      return;
    }

    setMsgs(sid, p => [...p, { role: 'user', text, images: imgs }, { role: 'assistant', text: '' }]);
    setBusyFor(sid, true);
    try { await c.SendMessage(sid, text, JSON.stringify(imgs)); }
    catch (e: any) { setBusyFor(sid, false); setMsgs(sid, p => appendToLastAssistant(p, '\n⚠️ ' + (e?.message || e))); }
    refreshSessions();
  };



  const drainFnRef = useRef<(sid: string) => void>(() => {});
  drainFnRef.current = (sid: string) => {
    const q = pendingQueues.current[sid] ?? [];
    if (q.length === 0) return;
    const [next, ...rest] = q;
    pendingQueues.current[sid] = rest;
    setQueueCounts(prev => ({ ...prev, [sid]: rest.length }));
    sendDirect(sid, next.text, next.imgs);
  };


  useEffect(() => {
    const onMessage = (e: any) => {
      const data = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
      if (!data || !data.Type) return;
      const sid: string | undefined = data.SessionId;
      switch (data.Type) {
        case 'AiToken': if (sid) setMsgs(sid, p => appendToLastAssistant(p, data.Text || '')); break;
        case 'AiToolStatus': if (sid) setMsgs(sid, p => appendToLastAssistant(p, `\n🔧 ${data.Text}\n`)); break;
        case 'AiInfo': if (sid) setMsgs(sid, p => [...p, { role: 'system', text: data.Text }]); break;
        case 'AiError': if (sid) { setBusyFor(sid, false); setMsgs(sid, p => appendToLastAssistant(p, `\n⚠️ ${data.Text}`)); } break;
        case 'AiDone':
          if (sid) {
            setBusyFor(sid, false);
            if (data.Stopped) setMsgs(sid, p => appendToLastAssistant(p, '\n[stopped]'));

            setTimeout(() => drainFnRef.current(sid), 80);
          }
          break;
        case 'AiClear': if (sid) setMsgs(sid, () => [GREETING]); break;
        case 'AiRetry': if (sid) { setMsgs(sid, p => [...p, { role: 'assistant', text: '' }]); setBusyFor(sid, true); } break;
        case 'AiReload': if (data.Session) setBuffers(b => ({ ...b, [data.Session.id]: dtoToMsgs(data.Session) })); break;
        case 'McpLive':



          if (data.IsWrite) {
            const activeSid = sid ?? Object.entries(busyMapRef.current).find(([, v]) => v)?.[0];
            if (activeSid) setMsgs(activeSid, p => appendToLastAssistant(p, `\n✍️ ${data.Summary}\n`));
          }
          break;
        case 'SessionRenamed':
        case 'SessionsChanged': refreshSessions(); break;
      }
    };

    window.chrome?.webview?.addEventListener('message', onMessage);

    return () => window.chrome?.webview?.removeEventListener('message', onMessage);
  }, []);


  useEffect(() => {
    (async () => {
      const c = host();
      if (!c) return;
      try {
        setCommands(JSON.parse(await c.GetCommands()));
        const list: ChatSessionMeta[] = JSON.parse(await c.ListSessions());
        setSessions(list);
        renderSession(JSON.parse(list.length > 0 ? await c.LoadSession(list[0].id) : await c.NewSession()));
      } catch {  }
    })();
  }, []);


  useEffect(() => {
    const handler = (e: Event) => {
      const detail = (e as CustomEvent).detail;
      if (typeof detail === 'string') sendExternal(detail);
    };
    window.addEventListener('fo4:ask-ai', handler);
    return () => window.removeEventListener('fo4:ask-ai', handler);

  }, []);

  useEffect(() => { endRef.current?.scrollIntoView({ behavior: 'smooth' }); }, [messages]);

  const switchSession = async (id: string) => {
    if (id === currentId) return;
    setCurrentId(id);


    if (!buffers[id]) {
      const c = host();
      if (!c) return;
      try { renderSession(JSON.parse(await c.LoadSession(id))); } catch {  }
    }
  };

  const newChat = async () => {
    const c = host();
    if (!c) return;
    renderSession(JSON.parse(await c.NewSession()));
    refreshSessions();
  };

  const [forking, setForking] = useState(false);
  const forkChat = async () => {
    const c = host();
    if (!c) return;
    if (!currentId) { setMsgs(currentId, p => [...p, { role: 'system', text: '⚠️ No active chat to fork.' }]); return; }
    if (forking) return;
    setForking(true);
    const from = currentId;
    try {
      const dto = JSON.parse(await c.ForkSession(from));
      await refreshSessions();
      renderSession(dto);
    } catch (e: any) {
      setMsgs(from, p => [...p, { role: 'system', text: '⚠️ Fork failed: ' + (e?.message || e?.toString() || '') }]);
    } finally { setForking(false); }
  };

  const renameChat = async () => {
    const c = host();
    if (!c || !currentId) return;
    const cur = sessions.find(s => s.id === currentId);
    const name = await askPrompt({ title: 'Rename chat', label: 'Chat name',
      defaultValue: cur?.name || '', validate: v => v.trim() ? null : 'Enter a name.' });
    if (!name) return;
    await c.RenameSession(currentId, name);
    refreshSessions();
  };

  const deleteChat = async () => {
    const c = host();
    if (!c || !currentId) return;
    if (!await askConfirm({ title: 'Delete chat', danger: true, confirmLabel: 'Delete',
      message: 'Delete this chat? Its messages cannot be recovered.' })) return;
    const idDel = currentId;
    const list: ChatSessionMeta[] = JSON.parse(await c.DeleteSession(idDel));
    setSessions(list);
    setBuffers(b => { const n = { ...b }; delete n[idDel]; return n; });
    setBusyMap(b => { const n = { ...b }; delete n[idDel]; return n; });
    renderSession(JSON.parse(list.length > 0 ? await c.LoadSession(list[0].id) : await c.NewSession()));
  };

  const send = async () => {
    const text = input.trim();
    const imgs = attachments;
    if (!text && imgs.length === 0) return;
    setInput('');
    setAttachments([]);
    const c = host();
    if (!c) return;
    const sid = currentId;

    if (busy) {

      const q = pendingQueues.current[sid] ?? [];
      pendingQueues.current[sid] = [...q, { text, imgs }];
      setQueueCounts(prev => ({ ...prev, [sid]: q.length + 1 }));
      return;
    }

    await sendDirect(sid, text, imgs);
  };



  const sendExternal = async (text: string) => {
    const sid = currentIdRef.current;
    if (!text.trim() || !sid || busyMapRef.current[sid]) return;
    const c = host();
    if (!c) return;
    setMsgs(sid, p => [...p, { role: 'user', text }, { role: 'assistant', text: '' }]);
    setBusyFor(sid, true);
    try { await c.SendMessage(sid, text, '[]'); }
    catch (e: any) { setBusyFor(sid, false); setMsgs(sid, p => appendToLastAssistant(p, '\n⚠️ ' + (e?.message || e))); }
    refreshSessions();
  };

  const stop = () => host()?.CancelMessage(currentId);


  const showCmd = input.startsWith('/') && !input.includes(' ');
  const filtered = showCmd ? commands.filter(c => c.name.startsWith(input.toLowerCase())) : [];
  const acceptCmd = (c: SlashCommand) => setInput(c.name + ' ');

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (filtered.length > 0) {
      if (e.key === 'ArrowDown') { e.preventDefault(); setCmdIndex(i => (i + 1) % filtered.length); return; }
      if (e.key === 'ArrowUp') { e.preventDefault(); setCmdIndex(i => (i - 1 + filtered.length) % filtered.length); return; }
      if (e.key === 'Tab') { e.preventDefault(); acceptCmd(filtered[Math.min(cmdIndex, filtered.length - 1)]); return; }
    }
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }
  };

  return (
    <div className="ai-panel">
      <div className="chat-toolbar">
        <select className="chat-session-select" value={currentId} onChange={e => switchSession(e.target.value)}>
          {sessions.length === 0 && <option value="">New Chat</option>}
          {sessions.map(s => <option key={s.id} value={s.id}>{(busyMap[s.id] ? '● ' : '') + (s.name || 'Untitled')}</option>)}
        </select>
        <button className="chat-icon-btn" onClick={newChat} title="New chat"><Plus size={15} /></button>
        <button className="chat-icon-btn" onClick={forkChat} disabled={forking} title="Summarize & fork into a new chat"><GitBranch size={14} /></button>
        <button className="chat-icon-btn" onClick={renameChat} title="Rename chat"><Pencil size={14} /></button>
        <button className="chat-icon-btn" onClick={deleteChat} title="Delete chat"><Trash2 size={14} /></button>
      </div>

      <div className="ai-content">
        {messages.map((m, i) => (
          <div key={i} className={`ai-chat-bubble ${m.role}`}>
            {m.images && m.images.length > 0 && (
              <div className="chat-msg-images">
                {m.images.map((src, j) => <img key={j} src={src} alt="attachment" />)}
              </div>
            )}
            {m.role === 'user'
              ? m.text
              : (m.text
                  ? <RichText text={m.text} />
                  : (m.role === 'assistant' && busy
                      ? <span className="chat-typing"><span /><span /><span /></span>
                      : ''))}
          </div>
        ))}
        <div ref={endRef} />
      </div>

      <div className="ai-input-wrap">
        {filtered.length > 0 && (
          <div className="cmd-menu">
            {filtered.map((c, i) => (
              <div
                key={c.name}
                className={`cmd-item ${i === Math.min(cmdIndex, filtered.length - 1) ? 'active' : ''}`}
                onMouseEnter={() => setCmdIndex(i)}
                onClick={() => acceptCmd(c)}
              >
                <span className="cmd-name">{c.name}{c.args ? ' ' + c.args : ''}</span>
                <span className="cmd-help">{c.help}</span>
              </div>
            ))}
          </div>
        )}
        {attachments.length > 0 && (
          <div className="chat-attachments">
            {attachments.map((src, i) => (
              <div key={i} className="chat-attach-thumb">
                <img src={src} alt="attachment" />
                <button onClick={() => setAttachments(a => a.filter((_, j) => j !== i))} title="Remove"><X size={11} /></button>
              </div>
            ))}
          </div>
        )}
        {busy && queueCount > 0 && (
          <div className="chat-queue-banner">
            {queueCount} message{queueCount !== 1 ? 's' : ''} queued
          </div>
        )}
        <div className="ai-input">
          <input
            ref={fileRef}
            type="file"
            accept="image/*"
            multiple
            style={{ display: 'none' }}
            onChange={e => { addImageFiles(e.target.files); e.target.value = ''; }}
          />
          <button className="chat-attach-btn" onClick={() => fileRef.current?.click()} title="Attach image">
            <ImageIcon size={16} />
          </button>
          <input
            type="text"
            placeholder={busy ? 'Responding… press Enter to queue' : 'Ask Claude…  (paste or attach images)'}
            value={input}
            onChange={e => { setInput(e.target.value); setCmdIndex(0); }}
            onKeyDown={onKeyDown}
            onPaste={onPaste}
          />
          {busy
            ? <button className="chat-stop-btn" onClick={stop} title="Stop generating">
                <Square size={11} />
                Stop
              </button>
            : <button onClick={send} title="Send"><MessageSquare size={16} /></button>}
        </div>
      </div>
    </div>
  );
}
