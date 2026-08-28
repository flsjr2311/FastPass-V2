import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    // Proxy: todas as chamadas /api e /health vão para a API .NET
    // Isso resolve o problema de cookie cross-origin — do ponto de vista do browser,
    // o frontend e a API têm a mesma origem (127.0.0.1:5173).
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5104',
        changeOrigin: true,
      },
      '/health': {
        target: 'http://127.0.0.1:5104',
        changeOrigin: true,
      },
    },
  },
});
