import { HashRouter, Routes, Route } from 'react-router-dom';
import { DialogProvider } from './dialogs';
import ConflictResolver from './ConflictResolver';
import MainShell from './MainShell';

function App() {
  return (
    <DialogProvider>
    <HashRouter>
      <Routes>
        <Route path="/" element={<ConflictResolver />} />
        <Route path="/main" element={<MainShell />} />
      </Routes>
    </HashRouter>
    </DialogProvider>
  );
}

export default App;
