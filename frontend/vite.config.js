import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const devApiBase = env.VITE_DEV_API_BASE || 'http://localhost:5299';

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: devApiBase,
          changeOrigin: true,
          secure: false,
        }
      }
    },
    preview: {
      port: 4173,
    },
  };
});
