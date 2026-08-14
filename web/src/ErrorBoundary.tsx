









import { Component, type ErrorInfo, type ReactNode } from 'react';

interface Props { children: ReactNode }
interface State { error: Error | null; info: string }

export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null, info: '' };

  static getDerivedStateFromError(error: Error): Partial<State> {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {

    console.error('[FO4IDE] UI error:', error, info.componentStack);
    this.setState({ info: info.componentStack ?? '' });
  }

  private reset = () => this.setState({ error: null, info: '' });

  render() {
    const { error, info } = this.state;
    if (!error) return this.props.children;

    return (
      <div style={{
        padding: '24px 28px', fontFamily: 'Segoe UI, system-ui, sans-serif',
        color: '#ddd', background: '#1e1e1e', height: '100vh', overflow: 'auto', boxSizing: 'border-box',
      }}>
        <h1 style={{ fontSize: 18, margin: '0 0 6px', color: '#f44747' }}>The interface hit an error</h1>
        <p style={{ margin: '0 0 16px', color: '#aaa', fontSize: 13 }}>
          The rest of the app is still running and your plugins are still loaded. Nothing was written to
          disk by this error.
        </p>
        <pre style={{
          background: '#252526', border: '1px solid #3c3c3c', borderRadius: 4, padding: 12,
          fontSize: 12, whiteSpace: 'pre-wrap', wordBreak: 'break-word', margin: '0 0 12px',
        }}>{String(error?.stack || error)}</pre>
        {info && (
          <details style={{ marginBottom: 16 }}>
            <summary style={{ cursor: 'pointer', fontSize: 12, color: '#aaa' }}>Component stack</summary>
            <pre style={{
              background: '#252526', border: '1px solid #3c3c3c', borderRadius: 4, padding: 12,
              fontSize: 12, whiteSpace: 'pre-wrap', wordBreak: 'break-word', marginTop: 8,
            }}>{info}</pre>
          </details>
        )}
        <button onClick={this.reset} style={{
          background: '#0e639c', color: '#fff', border: 'none', borderRadius: 3,
          padding: '7px 16px', cursor: 'pointer', fontSize: 13, marginRight: 8,
        }}>Dismiss and continue</button>
        <button onClick={() => location.reload()} style={{
          background: '#3c3c3c', color: '#ddd', border: 'none', borderRadius: 3,
          padding: '7px 16px', cursor: 'pointer', fontSize: 13,
        }}>Reload the app</button>
      </div>
    );
  }
}
