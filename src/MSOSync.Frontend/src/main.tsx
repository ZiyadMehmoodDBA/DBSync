import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './App.tsx';

// Prevent flash of incorrect theme — run before React mounts
const savedTheme = localStorage.getItem('msosync.theme') ?? 'light';
document.documentElement.classList.toggle('dark', savedTheme === 'dark');

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
