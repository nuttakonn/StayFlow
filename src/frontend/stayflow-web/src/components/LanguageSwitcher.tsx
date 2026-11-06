import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Languages, Check, X } from 'lucide-react';
import { SUPPORTED_LANGUAGES, type SupportedLanguage } from '../i18n';
import { getStoredSession, updateStoredLanguage } from '../lib/api';

const FLAG: Record<SupportedLanguage, string> = {
  en: '🇬🇧',
  th: '🇹🇭',
  'zh-CN': '🇨🇳',
};

const SAVE_FAILED_TIMEOUT_MS = 3000;

export default function LanguageSwitcher() {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  // A flag, not a resolved string: the notice must re-render in whatever
  // language is active now, not the one that was active when it was raised.
  const [saveFailed, setSaveFailed] = useState(false);

  const current = (SUPPORTED_LANGUAGES as readonly string[]).includes(i18n.language)
    ? (i18n.language as SupportedLanguage)
    : 'en';

  useEffect(() => {
    if (!saveFailed) return;
    const timer = setTimeout(() => setSaveFailed(false), SAVE_FAILED_TIMEOUT_MS);
    return () => clearTimeout(timer);
  }, [saveFailed]);

  const select = async (lang: SupportedLanguage) => {
    setOpen(false);
    setSaveFailed(false);

    // Switch the UI immediately; persistence is best-effort so the switcher
    // still works when the user has no session or the API is unreachable.
    await i18n.changeLanguage(lang);
    // Keep the stored session in sync so App.tsx's loading effect doesn't
    // revert the language on the next data fetch.
    updateStoredLanguage(lang);

    const session = getStoredSession();
    if (!session?.token) return;

    try {
      const response = await fetch('/api/v1/auth/me/language', {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${session.token}`,
        },
        body: JSON.stringify({ language: lang }),
      });
      if (!response.ok) setSaveFailed(true);
    } catch {
      setSaveFailed(true);
    }
  };

  return (
    <div className="relative">
      <button
        onClick={() => setOpen((o) => !o)}
        title={t('language.change')}
        aria-label={t('language.change')}
        aria-haspopup="listbox"
        aria-expanded={open}
        className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-bold text-slate-600 bg-slate-50 hover:bg-slate-100 rounded-xl border border-slate-200 t-button"
      >
        <Languages className="w-3.5 h-3.5 text-slate-500" />
        <span className="hidden sm:inline">{t(`language.${current}`)}</span>
        <span className="sm:hidden">{FLAG[current]}</span>
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-30" onClick={() => setOpen(false)} />
          <ul
            role="listbox"
            aria-label={t('language.label')}
            className="absolute right-0 mt-1.5 w-44 bg-white border border-slate-200 rounded-xl shadow-lg py-1 z-40"
          >
            {SUPPORTED_LANGUAGES.map((lang) => (
              <li key={lang} role="none">
                <button
                  role="option"
                  aria-selected={lang === current}
                  onClick={() => select(lang)}
                  className={`w-full flex items-center gap-2.5 px-3 py-2 text-xs font-bold text-left hover:bg-slate-50 ${
                    lang === current ? 'text-blue-600' : 'text-slate-700'
                  }`}
                >
                  <span aria-hidden="true">{FLAG[lang]}</span>
                  <span className="flex-1">{t(`language.${lang}`)}</span>
                  {lang === current && <Check className="w-3.5 h-3.5" />}
                </button>
              </li>
            ))}
          </ul>
        </>
      )}

      {saveFailed && (
        <div
          role="status"
          className="absolute right-0 mt-1.5 w-60 flex items-start gap-2 text-[11px] text-amber-800 bg-amber-50 border border-amber-200 rounded-lg px-2.5 py-1.5 shadow-sm z-40"
        >
          <span className="flex-1 leading-snug">{t('language.saveFailed')}</span>
          <button
            onClick={() => setSaveFailed(false)}
            aria-label={t('common.close')}
            className="shrink-0 -mr-0.5 p-0.5 rounded hover:bg-amber-100 text-amber-600"
          >
            <X className="w-3 h-3" />
          </button>
        </div>
      )}
    </div>
  );
}
