/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Адрес API. Задаётся в .env, см. .env.example. */
  readonly VITE_API_URL?: string;
  /** Ключ сайта Yandex SmartCaptcha. Пусто — капча не показывается (разработка). */
  readonly VITE_CAPTCHA_SITEKEY?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
