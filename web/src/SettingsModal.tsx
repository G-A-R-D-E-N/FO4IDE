import { useEffect, useState } from 'react';
import { X } from 'lucide-react';
import './SettingsModal.css';

interface Settings {
  AiProvider: string;
  AnthropicApiKey: string;
  Model: string;
  GeminiApiKey: string;
  GeminiModel: string;
  ClaudeCodePath: string;
  OllamaUrl: string;
  OllamaModel: string;
  OutputFolder: string;
  DataFolder: string;
  Mo2InstancePath?: string;
  CkWikiPath: string;
  TexconvPath: string;
  PapyrusCompilerPath: string;
  PapyrusBaseImports: string;
  NiftoolPath: string;
  FfmpegPath: string;
  XwmaEncodePath: string;
  Archive2Path: string;
  ReadLargePluginsIntoMemory?: boolean;
}

const host = () => window.chrome?.webview?.hostObjects?.settings;

// Known Claude models. Sonnet 4.6 is the recommended default -- best balance for agentic plugin work.
const MODELS = [
  { id: 'claude-sonnet-4-6', label: 'Claude Sonnet 4.6 -- Recommended (best balance)' },
  { id: 'claude-opus-4-8', label: 'Claude Opus 4.8 -- Most capable' },
  { id: 'claude-haiku-4-5', label: 'Claude Haiku 4.5 -- Fastest / cheapest' },
];

const GEMINI_MODELS = [
  { id: 'gemini-2.0-flash', label: 'Gemini 2.0 Flash -- Recommended (fast, tool-use)' },
  { id: 'gemini-2.5-pro', label: 'Gemini 2.5 Pro -- Most capable' },
  { id: 'gemini-1.5-pro', label: 'Gemini 1.5 Pro' },
];

export default function SettingsModal({ onClose }: { onClose: () => void }) {
  const [s, setS] = useState<Settings | null>(null);
  const [status, setStatus] = useState('');
  const [claudeTest, setClaudeTest] = useState('');

  useEffect(() => {
    (async () => {
      const h = host();
      if (!h) { setStatus('Settings bridge unavailable.'); return; }
      try { setS(JSON.parse(await h.GetSettings())); }
      catch (e: any) { setStatus('Failed to load settings: ' + (e?.message || e)); }
    })();
  }, []);

  const set = (k: keyof Settings, v: string) => setS(prev => (prev ? { ...prev, [k]: v } : prev));

  const save = async () => {
    const h = host();
    if (!h || !s) return;
    setStatus('Saving…');
    try { setStatus(await h.SaveSettings(JSON.stringify(s))); }
    catch (e: any) { setStatus('Save failed: ' + (e?.message || e)); }
  };

  const browse = async (key: keyof Settings, title: string) => {
    const h = host();
    if (!h || !s) return;
    const path = await h.BrowseFolder(title, (s[key] as string) || '');
    if (path) set(key, path);
  };

  const browseFile = async (key: keyof Settings, title: string, filter: string) => {
    const h = host();
    if (!h || !s) return;
    const path = await h.BrowseFile(title, filter, (s[key] as string) || '');
    if (path) set(key, path);
  };

  const testClaude = async () => {
    const h = host();
    if (!h || !s) return;
    setClaudeTest('Testing…');
    try { setClaudeTest(await h.TestClaude(s.ClaudeCodePath || '')); }
    catch (e: any) { setClaudeTest('✗ ' + (e?.message || e)); }
  };

  return (
    <div className="settings-overlay" onClick={onClose}>
      <div className="settings-modal" onClick={e => e.stopPropagation()}>
        <div className="settings-header">
          <h2>Settings</h2>
          <button className="settings-close" onClick={onClose} title="Close"><X size={18} /></button>
        </div>

        {!s ? (
          <div className="settings-body"><p className="settings-status">{status || 'Loading…'}</p></div>
        ) : (
          <>
            <div className="settings-body">
              <label className="settings-field">
                <span>AI Provider</span>
                <select value={s.AiProvider} onChange={e => set('AiProvider', e.target.value)}>
                  <option value="anthropic">Claude (Anthropic API)</option>
                  <option value="claudecode">Claude Code (CLI)</option>
                  <option value="gemini">Gemini (Google API)</option>
                  <option value="ollama">Ollama (local)</option>
                </select>
              </label>

              {(s.AiProvider === 'anthropic' || s.AiProvider === 'claudecode') && (() => {
                const known = MODELS.some(m => m.id === s.Model);
                return (
                  <label className="settings-field">
                    <span>Model</span>
                    <select
                      value={known ? s.Model : '__custom__'}
                      onChange={e => set('Model', e.target.value === '__custom__' ? '' : e.target.value)}
                    >
                      {MODELS.map(m => <option key={m.id} value={m.id}>{m.label}</option>)}
                      <option value="__custom__">Custom…</option>
                    </select>
                    {!known && (
                      <input value={s.Model} onChange={e => set('Model', e.target.value)}
                        placeholder="model id (e.g. claude-opus-4-8)" />
                    )}
                  </label>
                );
              })()}

              {s.AiProvider === 'anthropic' && (
                <label className="settings-field">
                  <span>Anthropic API Key</span>
                  <input type="password" value={s.AnthropicApiKey}
                    onChange={e => set('AnthropicApiKey', e.target.value)} placeholder="sk-ant-…" />
                </label>
              )}

              {s.AiProvider === 'gemini' && (
                <>
                  {(() => {
                    const known = GEMINI_MODELS.some(m => m.id === s.GeminiModel);
                    return (
                      <label className="settings-field">
                        <span>Gemini Model</span>
                        <select
                          value={known ? s.GeminiModel : '__custom__'}
                          onChange={e => set('GeminiModel', e.target.value === '__custom__' ? '' : e.target.value)}
                        >
                          {GEMINI_MODELS.map(m => <option key={m.id} value={m.id}>{m.label}</option>)}
                          <option value="__custom__">Custom…</option>
                        </select>
                        {!known && (
                          <input value={s.GeminiModel} onChange={e => set('GeminiModel', e.target.value)}
                            placeholder="model id (e.g. gemini-2.0-flash)" />
                        )}
                      </label>
                    );
                  })()}
                  <label className="settings-field">
                    <span>Gemini API Key</span>
                    <input type="password" value={s.GeminiApiKey}
                      onChange={e => set('GeminiApiKey', e.target.value)} placeholder="AIza…" />
                  </label>
                </>
              )}

              {s.AiProvider === 'claudecode' && (
                <>
                  <label className="settings-field">
                    <span>Claude Code Path</span>
                    <input value={s.ClaudeCodePath} onChange={e => set('ClaudeCodePath', e.target.value)} placeholder="claude" />
                  </label>
                  <div className="settings-inline">
                    <button className="settings-btn" onClick={testClaude}>Test CLI</button>
                    <span className="settings-test">{claudeTest}</span>
                  </div>
                </>
              )}

              {s.AiProvider === 'ollama' && (
                <>
                  <label className="settings-field">
                    <span>Ollama URL</span>
                    <input value={s.OllamaUrl} onChange={e => set('OllamaUrl', e.target.value)} />
                  </label>
                  <label className="settings-field">
                    <span>Ollama Model</span>
                    <input value={s.OllamaModel} onChange={e => set('OllamaModel', e.target.value)} />
                  </label>
                </>
              )}

              <hr className="settings-divider" />

              <label className="settings-field">
                <span>Output Folder <em>(AI-authored plugins)</em></span>
                <div className="settings-input-row">
                  <input value={s.OutputFolder} onChange={e => set('OutputFolder', e.target.value)} />
                  <button className="settings-btn" onClick={() => browse('OutputFolder', 'Choose output folder for AI-authored plugins')}>Browse</button>
                </div>
              </label>

              <label className="settings-field">
                <span>Read large plugins into memory <em>(lets you save over a loaded plugin instead of getting a .new file beside it)</em></span>
                <div className="settings-input-row">
                  <input
                    type="checkbox"
                    checked={!!s.ReadLargePluginsIntoMemory}
                    onChange={e => set('ReadLargePluginsIntoMemory', e.target.checked as never)}
                    style={{ width: 'auto', flex: 'none' }}
                  />
                  <span className="settings-hint">
                    Off by default, and expensive. Plugins over 1&nbsp;MB are normally opened as overlays,
                    which keeps each file open for the whole session. Reading them into memory instead
                    cost <strong>~2.2&nbsp;GB extra</strong> on a measured 657-plugin load order (39 plugins,
                    197&nbsp;MB on disk -- record objects run about 11x their file size). Turn this on only
                    if you need to overwrite a loaded plugin in place.
                  </span>
                </div>
              </label>

              <label className="settings-field">
                <span>Data Folder <em>(Load Env override -- blank = auto-detect)</em></span>
                <div className="settings-input-row">
                  <input value={s.DataFolder} onChange={e => set('DataFolder', e.target.value)} />
                  <button className="settings-btn" onClick={() => browse('DataFolder', 'Choose the game Data folder to load plugins from')}>Browse</button>
                </div>
              </label>

              {/* This is the path --mo2 actually uses, and the one Open MO2 remembers. It round-tripped
                  through the host all along but had no field, so the single most load-bearing path in
                  the app was invisible and uneditable. On Linux it is also the only working way to load
                  a modlist, since Load Env cannot auto-detect one. */}
              <label className="settings-field">
                <span>MO2 Instance <em>(the folder holding mods/ and profiles/; what Open MO2 remembers)</em></span>
                <div className="settings-input-row">
                  <input value={s.Mo2InstancePath ?? ''} onChange={e => set('Mo2InstancePath', e.target.value)} />
                  <button className="settings-btn" onClick={() => browse('Mo2InstancePath', 'Choose the MO2 instance folder (contains mods/ and profiles/)')}>Browse</button>
                </div>
              </label>

              <label className="settings-field">
                <span>CK Wiki Path <em>(offline Creation Kit Wiki mirror -- blank = use the copy bundled with the app)</em></span>
                <div className="settings-input-row">
                  <input value={s.CkWikiPath} onChange={e => set('CkWikiPath', e.target.value)} placeholder="…\Creation Kit Wiki\fallout4" />
                  <button className="settings-btn" onClick={() => browse('CkWikiPath', 'Select the offline Creation Kit Wiki mirror folder')}>Browse</button>
                </div>
              </label>

              <label className="settings-field">
                <span>Niftool Path <em>(blank = bundled copy next to the exe)</em></span>
                <div className="settings-input-row">
                  <input value={s.NiftoolPath} onChange={e => set('NiftoolPath', e.target.value)} placeholder="…\niftool.exe" />
                  <button className="settings-btn" onClick={() => browseFile('NiftoolPath', 'Select niftool.exe', 'niftool|niftool.exe|Executables|*.exe')}>Browse</button>
                </div>
              </label>

              <label className="settings-field">
                <span>Ffmpeg Path <em>(blank = bundled copy next to the exe)</em></span>
                <div className="settings-input-row">
                  <input value={s.FfmpegPath} onChange={e => set('FfmpegPath', e.target.value)} placeholder="…\ffmpeg.exe" />
                  <button className="settings-btn" onClick={() => browseFile('FfmpegPath', 'Select ffmpeg.exe', 'ffmpeg|ffmpeg.exe|Executables|*.exe')}>Browse</button>
                </div>
              </label>

              <label className="settings-field">
                <span>xWMAEncode Path <em>(blank = bundled copy next to the exe)</em></span>
                <div className="settings-input-row">
                  <input value={s.XwmaEncodePath} onChange={e => set('XwmaEncodePath', e.target.value)} placeholder="…\xWMAEncode.exe" />
                  <button className="settings-btn" onClick={() => browseFile('XwmaEncodePath', 'Select xWMAEncode.exe', 'xWMAEncode|xWMAEncode.exe|Executables|*.exe')}>Browse</button>
                </div>
              </label>

              <label className="settings-field">
                <span>Texconv Path <em>(blank = bundled copy / xEdit Edit Scripts auto-detect)</em></span>
                <div className="settings-input-row">
                  <input value={s.TexconvPath} onChange={e => set('TexconvPath', e.target.value)} placeholder="…\Texconvx64.exe" />
                  <button className="settings-btn" onClick={() => browseFile('TexconvPath', 'Select Texconv.exe', 'Texconv|Texconvx64.exe;Texconv.exe|Executables|*.exe')}>Browse</button>
                </div>
              </label>

              <label className="settings-field">
                <span>Papyrus Compiler Path <em>(blank = auto-detect from the Fallout 4 install)</em></span>
                <div className="settings-input-row">
                  <input value={s.PapyrusCompilerPath} onChange={e => set('PapyrusCompilerPath', e.target.value)} placeholder="…\Papyrus Compiler\PapyrusCompiler.exe" />
                  <button className="settings-btn" onClick={() => browseFile('PapyrusCompilerPath', 'Select PapyrusCompiler.exe', 'PapyrusCompiler|PapyrusCompiler.exe|Executables|*.exe')}>Browse</button>
                </div>
              </label>

              <label className="settings-field">
                <span>Archive2 Path <em>(blank = auto-detect from the Fallout 4/Creation Kit install)</em></span>
                <div className="settings-input-row">
                  <input value={s.Archive2Path} onChange={e => set('Archive2Path', e.target.value)} placeholder="…\Tools\Archive2\Archive2.exe" />
                  <button className="settings-btn" onClick={() => browseFile('Archive2Path', 'Select Archive2.exe', 'Archive2|Archive2.exe|Executables|*.exe')}>Browse</button>
                </div>
              </label>

              <label className="settings-field">
                <span>Papyrus Base Imports <em>(extra script source roots, ';'-separated, highest priority first)</em></span>
                <div className="settings-input-row">
                  <input value={s.PapyrusBaseImports} onChange={e => set('PapyrusBaseImports', e.target.value)} placeholder="C:\Framework\Scripts\Source;C:\OtherFramework\Scripts\Source" />
                  <button className="settings-btn" onClick={async () => {
                    const h = host();
                    if (!h) return;
                    const dir = await h.BrowseFolder('Add a Papyrus source root', '');
                    if (dir) set('PapyrusBaseImports', s.PapyrusBaseImports ? `${s.PapyrusBaseImports};${dir}` : dir);
                  }}>Add folder…</button>
                </div>
              </label>
            </div>

            <div className="settings-footer">
              <span className="settings-status">{status}</span>
              <div className="settings-actions">
                <button className="settings-btn" onClick={onClose}>Cancel</button>
                <button className="settings-btn primary" onClick={save}>Save</button>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
