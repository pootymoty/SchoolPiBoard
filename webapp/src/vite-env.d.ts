/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Адрес API. Задаётся в .env, см. .env.example. */
  readonly VITE_API_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
