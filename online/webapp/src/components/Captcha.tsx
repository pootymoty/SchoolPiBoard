import { useEffect, useRef, useState } from 'react';
import type { ReactElement } from 'react';

const SITE_KEY = import.meta.env.VITE_CAPTCHA_SITEKEY ?? '';
const SCRIPT_URL = 'https://smartcaptcha.yandexcloud.net/captcha.js?render=onload&onload=onSmartCaptchaLoad';

interface Props {
  onToken: (token: string) => void;
}

interface SmartCaptchaApi {
  render: (
    container: HTMLElement,
    parameters: { sitekey: string; hl?: string; callback?: (token: string) => void },
  ) => number;
}

declare global {
  interface Window {
    smartCaptcha?: SmartCaptchaApi;
    onSmartCaptchaLoad?: () => void;
  }
}

/**
 * Капча Yandex SmartCaptcha — из доступных в России вариантов самый простой.
 *
 * Если ключ сайта не задан (разработка), виджет не показывается, а наружу
 * отдаётся пустой токен: сервер в таком режиме проверку тоже не выполняет.
 */
export function Captcha({ onToken }: Props): ReactElement | null {
  const container = useRef<HTMLDivElement | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!SITE_KEY) {
      onToken('');
      return;
    }

    let disposed = false;

    const render = () => {
      if (disposed || !container.current || !window.smartCaptcha) return;

      window.smartCaptcha.render(container.current, {
        sitekey: SITE_KEY,
        hl: 'ru',
        callback: (token: string) => onToken(token),
      });
    };

    if (window.smartCaptcha) {
      render();
      return () => {
        disposed = true;
      };
    }

    window.onSmartCaptchaLoad = render;

    // Скрипт грузим один раз на всё приложение.
    let script = document.querySelector<HTMLScriptElement>(`script[src="${SCRIPT_URL}"]`);
    if (!script) {
      script = document.createElement('script');
      script.src = SCRIPT_URL;
      script.defer = true;
      script.onerror = () => setFailed(true);
      document.head.appendChild(script);
    }

    return () => {
      disposed = true;
    };
  }, [onToken]);

  if (!SITE_KEY) {
    return null;
  }

  if (failed) {
    return <p className="error">Не удалось загрузить проверку «я не робот». Обновите страницу.</p>;
  }

  return <div className="captcha" ref={container} />;
}
