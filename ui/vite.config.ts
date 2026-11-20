import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// O build sai direto no wwwroot do app .NET (Photino serve essa pasta).
// Os arquivos estáticos da UI vêm de public/ e são copiados a cada build.
export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    outDir: '../src/Verso.App/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
  },
});
