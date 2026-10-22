import { mount } from 'svelte';
import './app.css';
import App from './App.svelte';

// Kein Browser-Zoom (Strg + Mausrad, Strg + Plus/Minus/0): der Launcher hat ein festes Layout.
// Nur die Standardaktion wird verhindert – eigene Zoom-Funktionen (z. B. im Banner-Editor) bekommen das Ereignis weiter.
window.addEventListener(
  'wheel',
  (e) => {
    if (e.ctrlKey) e.preventDefault();
  },
  { passive: false, capture: true }
);
window.addEventListener(
  'keydown',
  (e) => {
    if ((e.ctrlKey || e.metaKey) && ['+', '-', '=', '0', 'Add', 'Subtract'].includes(e.key)) e.preventDefault();
  },
  { capture: true }
);

const app = mount(App, { target: document.getElementById('app')! });

export default app;
