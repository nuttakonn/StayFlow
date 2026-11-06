import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';

import en from './locales/en.json';
import th from './locales/th.json';
import zhCN from './locales/zh-CN.json';

export const SUPPORTED_LANGUAGES = ['en', 'th', 'zh-CN'] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export const LANGUAGE_STORAGE_KEY = 'stayflow_language';

export function isSupportedLanguage(value: unknown): value is SupportedLanguage {
  return typeof value === 'string' && (SUPPORTED_LANGUAGES as readonly string[]).includes(value);
}

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      en: { translation: en },
      th: { translation: th },
      'zh-CN': { translation: zhCN },
    },
    supportedLngs: SUPPORTED_LANGUAGES,
    fallbackLng: 'en',
    // zh-CN is not fully reviewed yet; an untranslated key must render English,
    // never an empty string or the raw key.
    returnEmptyString: false,
    detection: {
      order: ['localStorage', 'navigator'],
      lookupLocalStorage: LANGUAGE_STORAGE_KEY,
      caches: ['localStorage'],
    },
    interpolation: {
      // React already escapes rendered values.
      escapeValue: false,
    },
  });

export default i18n;
