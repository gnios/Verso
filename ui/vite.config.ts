import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// O build sai direto no wwwroot do app .NET (Photino serve essa pasta).
// emptyOutDir: false preserva assets legados durante a migração (js/waveform.js etc.).
export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    outDir: '../src/Verso.App/wwwroot',
    emptyOutDir: false,
  },
  server: {
    port: 5173,
  },
});