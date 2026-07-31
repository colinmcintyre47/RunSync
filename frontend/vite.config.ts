import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev server runs on 5173, which must match Cors:AllowedOrigin in the backend's
// appsettings.Development.json or the API will reject browser requests.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
  },
});
