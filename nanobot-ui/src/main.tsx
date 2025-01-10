import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { CssBaseline } from '@mui/material'; // Import CssBaseline
import App from './App.tsx';

const rootElement = document.getElementById('root');

if (!rootElement) {
  throw new Error('Root element not found. Please ensure there is a root element in the HTML file.');
}

createRoot(rootElement).render(
  <StrictMode>
    <CssBaseline /> {/* Apply default Material-UI global styles */}
    <App />
  </StrictMode>
);
