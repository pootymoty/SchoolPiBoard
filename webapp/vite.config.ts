import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    // Порт зафиксирован: он же перечислен в Web:AppOrigins на сервере,
    // иначе браузер не пустит приложение к API.
    port: 5173,
    strictPort: true,
  },
});
