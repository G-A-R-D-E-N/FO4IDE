import { useState, useCallback, type DragEvent } from 'react';
import { Music, X, FileAudio2, Waves, Combine, Split, Play, FolderOpen, CheckCircle2, XCircle } from 'lucide-react';
import { getAudio } from './backend';
import './AudioPanel.css';

type Mode = 'to-xwm' | 'from-xwm' | 'fuz-make' | 'fuz-extract';
const LS = (k: string, d: string) => localStorage.getItem('audio.' + k) ?? d;
const LSB = (k: string, d: boolean) => { const v = localStorage.getItem('audio.' + k); return v === null ? d : v === '1'; };
const setLS = (k: string, v: string | boolean) => localStorage.setItem('audio.' + k, typeof v === 'boolean' ? (v ? '1' : '0') : v);

const BITRATES = [
  { value: 0, label: 'Auto (48000, xWMAEncode default)' },
  { value: 20000, label: '20000 bps -- 22050Hz mono / 32000Hz mono' },
  { value: 32000, label: '32000 bps -- 22050Hz stereo / 32000Hz stereo / 44100Hz mono+stereo' },
  { value: 48000, label: '48000 bps -- 32000Hz stereo / 44100Hz mono+stereo / 48000Hz stereo+5.1' },
  { value: 64000, label: '64000 bps -- 48000Hz stereo' },
  { value: 96000, label: '96000 bps -- 44100Hz stereo+5.1 / 48000Hz stereo' },
  { value: 160000, label: '160000 bps -- 48000Hz stereo' },
  { value: 192000, label: '192000 bps -- 44100Hz stereo+5.1 / 48000Hz stereo+5.1' },
];
const OUT_FORMATS = ['wav', 'mp3', 'flac', 'ogg', 'm4a', 'wma'];

export default function AudioPanel({ onClose }: { onClose: () => void }) {
  const [mode, setMode] = useState<Mode>(() => (LS('mode', 'to-xwm') as Mode));
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState('');
  const [log, setLog] = useState<string[]>([]);
  const [dragOver, setDragOver] = useState(false);
  const [lastOutDir, setLastOutDir] = useState('');


  const [xwmSource, setXwmSource] = useState(() => LS('xwmSource', ''));
  const [xwmOutput, setXwmOutput] = useState(() => LS('xwmOutput', ''));
  const [bitrate, setBitrate] = useState(() => Number(LS('bitrate', '0')));


  const [decSource, setDecSource] = useState(() => LS('decSource', ''));
  const [decOutput, setDecOutput] = useState(() => LS('decOutput', ''));
  const [decFormat, setDecFormat] = useState(() => LS('decFormat', 'wav'));


  const [fuzAudioSource, setFuzAudioSource] = useState(() => LS('fuzAudioSource', ''));
  const [fuzLip, setFuzLip] = useState(() => LS('fuzLip', ''));
  const [fuzOutput, setFuzOutput] = useState(() => LS('fuzOutput', ''));
  const [fuzNoLip, setFuzNoLip] = useState(() => LSB('fuzNoLip', false));


  const [extSource, setExtSource] = useState(() => LS('extSource', ''));
  const [extXwmOut, setExtXwmOut] = useState(() => LS('extXwmOut', ''));
  const [extLipOut, setExtLipOut] = useState(() => LS('extLipOut', ''));
  const [extAlsoWav, setExtAlsoWav] = useState(() => LSB('extAlsoWav', true));

  const audio = getAudio();
  const unavailable = !audio;

  const persist = (k: string, v: string | boolean) => setLS(k, v);

  const appendLog = (line: string) =>
    setLog(prev => [`[${new Date().toLocaleTimeString()}] ${line}`, ...prev].slice(0, 200));
  const baseName = (p: string) => p.replace(/[\\/]+$/, '').split(/[\\/]/).pop() || p;

  const browseFile = async (setter: (v: string) => void, key: string, title: string, filter: string) => {
    if (!audio) return;
    const p = await audio.BrowseForFile(title, filter);
    if (p) { setter(p); persist(key, p); }
  };
  const browseSave = async (setter: (v: string) => void, key: string, title: string, filter: string) => {
    if (!audio) return;
    const p = await audio.BrowseForSave(title, filter);
    if (p) { setter(p); persist(key, p); }
  };

  const AUDIO_FILTER = 'Audio/video (*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.wma;*.mp4;*.avi)|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.wma;*.mp4;*.avi|All files|*.*';
  const XWM_FILTER = 'xWMA (*.xwm)|*.xwm|All files|*.*';
  const FUZ_FILTER = 'Fuz voice file (*.fuz)|*.fuz|All files|*.*';


  const onDrop = useCallback(async (e: DragEvent, setter: (v: string) => void, key: string) => {
    e.preventDefault(); setDragOver(false);
    if (!audio) return;
    const f = e.dataTransfer.files?.[0];
    if (!f) return;
    try {
      const buf = await f.arrayBuffer();
      let bin = ''; const bytes = new Uint8Array(buf);
      for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
      const b64 = btoa(bin);
      const path = await audio.StageDroppedFile(f.name, b64);
      if (path.startsWith('ERR:')) { appendLog('✗ drop failed -- ' + path); return; }
      setter(path); persist(key, path);
      appendLog('• dropped ' + f.name + ' -> source set');
    } catch (err) {
      appendLog('✗ drop failed -- ' + (err instanceof Error ? err.message : String(err)));
    }
  }, [audio]);

  const run = async () => {
    if (!audio) return;
    setBusy(true); setResult('Working…');
    let name = '';
    try {
      let out = '';
      if (mode === 'to-xwm') {
        if (!xwmSource.trim()) return;
        name = baseName(xwmSource);
        out = await audio.ConvertToXwm(xwmSource, xwmOutput, bitrate);
      } else if (mode === 'from-xwm') {
        if (!decSource.trim()) return;
        name = baseName(decSource);
        out = await audio.ConvertFromXwm(decSource, decOutput, decFormat);
      } else if (mode === 'fuz-make') {
        if (!fuzAudioSource.trim() || !fuzOutput.trim()) return;
        name = baseName(fuzAudioSource);
        out = await audio.MakeFuz(fuzAudioSource, fuzLip, fuzOutput, fuzNoLip);
      } else {
        if (!extSource.trim()) return;
        name = baseName(extSource);
        out = await audio.ExtractFuz(extSource, extXwmOut, extLipOut, extAlsoWav);
      }
      const text = out || '(no output)';
      setResult(text);
      const savedMatch = text.match(/-> ([^,)\n]+)/);
      if (savedMatch) setLastOutDir(savedMatch[1].trim());
      const ok = /^RESULT: success/.test(text);
      const first = text.split('\n')[0];
      appendLog(`${ok ? '✓' : '✗'} ${modeVerb(mode)} ${name} -- ${first}`);
    } catch (e) {
      const msg = 'Error: ' + (e instanceof Error ? e.message : String(e));
      setResult(msg); appendLog(`✗ ${modeVerb(mode)} ${name} -- ${msg}`);
    } finally { setBusy(false); }
  };

  const openOut = async () => { if (audio && lastOutDir) await audio.OpenFolder(lastOutDir); };
  const banner = result ? makeBanner(result) : null;

  const canRun =
    !unavailable && !busy && (
      mode === 'to-xwm' ? !!xwmSource.trim() :
      mode === 'from-xwm' ? !!decSource.trim() :
      mode === 'fuz-make' ? !!fuzAudioSource.trim() && !!fuzOutput.trim() :
      !!extSource.trim()
    );

  return (
    <div className="audio-overlay" onClick={onClose}>
      <div className="audio-modal glass-panel" onClick={e => e.stopPropagation()}>
        <div className="audio-header">
          <span className="audio-title"><Music size={16} /> Audio</span>
          <div className="audio-modes">
            <button className={`audio-mode ${mode === 'to-xwm' ? 'active' : ''}`} onClick={() => { setMode('to-xwm'); setLS('mode', 'to-xwm'); }}><FileAudio2 size={14} /> To XWM</button>
            <button className={`audio-mode ${mode === 'from-xwm' ? 'active' : ''}`} onClick={() => { setMode('from-xwm'); setLS('mode', 'from-xwm'); }}><Waves size={14} /> From XWM</button>
            <button className={`audio-mode ${mode === 'fuz-make' ? 'active' : ''}`} onClick={() => { setMode('fuz-make'); setLS('mode', 'fuz-make'); }}><Combine size={14} /> Make Fuz</button>
            <button className={`audio-mode ${mode === 'fuz-extract' ? 'active' : ''}`} onClick={() => { setMode('fuz-extract'); setLS('mode', 'fuz-extract'); }}><Split size={14} /> Extract Fuz</button>
          </div>
          <button className="audio-close" onClick={onClose} title="Close"><X size={16} /></button>
        </div>

        {unavailable && <div className="audio-warn">Audio bridge not available -- run the desktop app (not the browser dev server).</div>}

        <div className="audio-body">
          <div className="audio-form">
            {mode === 'to-xwm' && (
              <>
                <label className="audio-field">
                  <span>Source (audio or video file)</span>
                  <div className={`audio-drop ${dragOver ? 'over' : ''}`}
                       onDragOver={e => { e.preventDefault(); setDragOver(true); }}
                       onDragLeave={() => setDragOver(false)}
                       onDrop={e => onDrop(e, v => { setXwmSource(v); }, 'xwmSource')}>
                    <input value={xwmSource} onChange={e => { setXwmSource(e.target.value); persist('xwmSource', e.target.value); }}
                           placeholder="Path, or drag a file here…" />
                  </div>
                  <div className="audio-input-row">
                    <button className="sidebar-action-btn" onClick={() => browseFile(setXwmSource, 'xwmSource', 'Select an audio or video file', AUDIO_FILTER)} disabled={unavailable}>File…</button>
                  </div>
                </label>
                <label className="audio-field">
                  <span>Output .xwm (default: alongside source)</span>
                  <div className="audio-input-row">
                    <input value={xwmOutput} onChange={e => { setXwmOutput(e.target.value); persist('xwmOutput', e.target.value); }} placeholder="(default: same name, .xwm)" />
                    <button className="sidebar-action-btn" onClick={() => browseSave(setXwmOutput, 'xwmOutput', 'Save xwm as', XWM_FILTER)} disabled={unavailable}>Save…</button>
                  </div>
                </label>
                <label className="audio-field">
                  <span>Bitrate</span>
                  <select value={bitrate} onChange={e => { const v = Number(e.target.value); setBitrate(v); persist('bitrate', String(v)); }}>
                    {BITRATES.map(b => <option key={b.value} value={b.value}>{b.label}</option>)}
                  </select>
                </label>
              </>
            )}

            {mode === 'from-xwm' && (
              <>
                <label className="audio-field">
                  <span>Source .xwm</span>
                  <div className={`audio-drop ${dragOver ? 'over' : ''}`}
                       onDragOver={e => { e.preventDefault(); setDragOver(true); }}
                       onDragLeave={() => setDragOver(false)}
                       onDrop={e => onDrop(e, v => setDecSource(v), 'decSource')}>
                    <input value={decSource} onChange={e => { setDecSource(e.target.value); persist('decSource', e.target.value); }}
                           placeholder="Path, or drag a .xwm file here…" />
                  </div>
                  <div className="audio-input-row">
                    <button className="sidebar-action-btn" onClick={() => browseFile(setDecSource, 'decSource', 'Select a .xwm file', XWM_FILTER)} disabled={unavailable}>File…</button>
                  </div>
                </label>
                <label className="audio-field">
                  <span>Output format</span>
                  <select value={decFormat} onChange={e => { setDecFormat(e.target.value); persist('decFormat', e.target.value); }}>
                    {OUT_FORMATS.map(f => <option key={f} value={f}>{f}{f === 'wav' ? ' (no ffmpeg needed)' : ''}</option>)}
                  </select>
                </label>
                <label className="audio-field">
                  <span>Output file (default: alongside source)</span>
                  <div className="audio-input-row">
                    <input value={decOutput} onChange={e => { setDecOutput(e.target.value); persist('decOutput', e.target.value); }} placeholder={`(default: same name, .${decFormat})`} />
                    <button className="sidebar-action-btn" onClick={() => browseSave(setDecOutput, 'decOutput', 'Save as', 'All files|*.*')} disabled={unavailable}>Save…</button>
                  </div>
                </label>
              </>
            )}

            {mode === 'fuz-make' && (
              <>
                <label className="audio-field">
                  <span>Audio source (any format -- encoded to xwm first if needed)</span>
                  <div className={`audio-drop ${dragOver ? 'over' : ''}`}
                       onDragOver={e => { e.preventDefault(); setDragOver(true); }}
                       onDragLeave={() => setDragOver(false)}
                       onDrop={e => onDrop(e, v => setFuzAudioSource(v), 'fuzAudioSource')}>
                    <input value={fuzAudioSource} onChange={e => { setFuzAudioSource(e.target.value); persist('fuzAudioSource', e.target.value); }}
                           placeholder="Path, or drag a file here…" />
                  </div>
                  <div className="audio-input-row">
                    <button className="sidebar-action-btn" onClick={() => browseFile(setFuzAudioSource, 'fuzAudioSource', 'Select an audio file', AUDIO_FILTER)} disabled={unavailable}>File…</button>
                  </div>
                </label>
                <label className="audio-field">
                  <span>Lip file (optional -- default: same name .lip next to the source)</span>
                  <div className="audio-input-row">
                    <input value={fuzLip} onChange={e => { setFuzLip(e.target.value); persist('fuzLip', e.target.value); }} placeholder="(auto-detected if present)" disabled={fuzNoLip} />
                    <button className="sidebar-action-btn" onClick={() => browseFile(setFuzLip, 'fuzLip', 'Select a .lip file', 'Lip file (*.lip)|*.lip|All files|*.*')} disabled={unavailable || fuzNoLip}>File…</button>
                  </div>
                </label>
                <label className="audio-field">
                  <span>Output .fuz</span>
                  <div className="audio-input-row">
                    <input value={fuzOutput} onChange={e => { setFuzOutput(e.target.value); persist('fuzOutput', e.target.value); }} placeholder="Required" />
                    <button className="sidebar-action-btn" onClick={() => browseSave(setFuzOutput, 'fuzOutput', 'Save fuz as', FUZ_FILTER)} disabled={unavailable}>Save…</button>
                  </div>
                </label>
                <div className="audio-opts">
                  <label><input type="checkbox" checked={fuzNoLip} onChange={e => { setFuzNoLip(e.target.checked); persist('fuzNoLip', e.target.checked); }} /> No lip (voice-only fuz)</label>
                </div>
              </>
            )}

            {mode === 'fuz-extract' && (
              <>
                <label className="audio-field">
                  <span>Source .fuz</span>
                  <div className={`audio-drop ${dragOver ? 'over' : ''}`}
                       onDragOver={e => { e.preventDefault(); setDragOver(true); }}
                       onDragLeave={() => setDragOver(false)}
                       onDrop={e => onDrop(e, v => setExtSource(v), 'extSource')}>
                    <input value={extSource} onChange={e => { setExtSource(e.target.value); persist('extSource', e.target.value); }}
                           placeholder="Path, or drag a .fuz file here…" />
                  </div>
                  <div className="audio-input-row">
                    <button className="sidebar-action-btn" onClick={() => browseFile(setExtSource, 'extSource', 'Select a .fuz file', FUZ_FILTER)} disabled={unavailable}>File…</button>
                  </div>
                </label>
                <label className="audio-field">
                  <span>xwm output (default: alongside source)</span>
                  <input value={extXwmOut} onChange={e => { setExtXwmOut(e.target.value); persist('extXwmOut', e.target.value); }} placeholder="(default: same name, .xwm)" />
                </label>
                <label className="audio-field">
                  <span>lip output (default: alongside source)</span>
                  <input value={extLipOut} onChange={e => { setExtLipOut(e.target.value); persist('extLipOut', e.target.value); }} placeholder="(default: same name, .lip)" />
                </label>
                <div className="audio-opts">
                  <label><input type="checkbox" checked={extAlsoWav} onChange={e => { setExtAlsoWav(e.target.checked); persist('extAlsoWav', e.target.checked); }} /> Also decode to .wav</label>
                </div>
              </>
            )}

            <button className="audio-run" onClick={run} disabled={!canRun}>
              <Play size={14} /> {busy ? 'Working…' : modeLabel(mode)}
            </button>
          </div>

          <div className="audio-output">
            {banner && (
              <div className={`audio-banner ${banner.kind}`}>
                {banner.kind === 'ok' ? <CheckCircle2 size={15} /> : <XCircle size={15} />}
                <span className="audio-banner-text">{banner.text}</span>
                {lastOutDir && <button className="audio-openfolder" onClick={openOut} title={lastOutDir}><FolderOpen size={13} /> Open folder</button>}
              </div>
            )}
            <div className="audio-output-head">
              <span>OUTPUT</span>
              {result && <button className="audio-copy" onClick={() => navigator.clipboard.writeText(result)}>Copy</button>}
            </div>
            <pre className="audio-output-body">
              {result || 'Pick a source (or drag one in) and run.'}
            </pre>
            <div className="audio-log-head">
              <span>LOG ({log.length})</span>
              {log.length > 0 && <button className="audio-copy" onClick={() => setLog([])}>Clear</button>}
            </div>
            <div className="audio-log-body">
              {log.length === 0 ? <div className="audio-log-empty">No runs yet.</div>
                : log.map((l, i) => <div key={i} className={`audio-log-row ${l.includes('✗') ? 'err' : 'ok'}`}>{l}</div>)}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function modeVerb(m: Mode): string {
  return m === 'to-xwm' ? 'encoded' : m === 'from-xwm' ? 'decoded' : m === 'fuz-make' ? 'packed' : 'extracted';
}
function modeLabel(m: Mode): string {
  return m === 'to-xwm' ? 'Convert to XWM' : m === 'from-xwm' ? 'Convert from XWM' : m === 'fuz-make' ? 'Make Fuz' : 'Extract Fuz';
}
function makeBanner(text: string): { kind: 'ok' | 'error'; text: string } {
  if (/^RESULT: success/.test(text)) return { kind: 'ok', text: text.split('\n')[0].replace('RESULT: success', 'Done').trim() };
  return { kind: 'error', text: text.split('\n')[0] || 'Failed -- see output' };
}
