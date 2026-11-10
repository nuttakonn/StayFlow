import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Building2, Eye, EyeOff, CheckCircle2, AlertCircle } from 'lucide-react';
import { API_BASE } from '../lib/api';
import LanguageSwitcher from './LanguageSwitcher';

interface Props {
  token: string;
  onBackToLogin: () => void;
}

export function ResetPasswordPage({ token, onBackToLogin }: Props) {
  const { t } = useTranslation();
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  const inputClass =
    'w-full px-4 py-2.5 border border-slate-300 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent bg-white placeholder-slate-400 transition';

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    if (password !== confirm) {
      setError(t('auth.resetPasswordMismatch'));
      return;
    }

    setLoading(true);
    try {
      const res = await fetch(API_BASE + '/api/v1/auth/reset-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token, newPassword: password }),
      });

      if (res.ok || res.status === 204) {
        setSuccess(true);
      } else {
        let msg = t('auth.resetPasswordExpired');
        try {
          const body = await res.json();
          if (body?.detail) msg = body.detail;
        } catch { /* ignore */ }
        setError(msg);
      }
    } catch {
      setError(t('auth.resetPasswordExpired'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-indigo-950 to-blue-900 flex flex-col items-center justify-center p-4">
      <div className="absolute top-4 right-4">
        <LanguageSwitcher />
      </div>

      <div className="w-full max-w-md">
        <div className="text-center mb-8">
          <div className="inline-flex items-center justify-center w-16 h-16 rounded-2xl bg-white/10 backdrop-blur-md border border-white/20 mb-4 shadow-xl">
            <Building2 className="w-8 h-8 text-white" />
          </div>
          <h1 className="text-3xl font-black text-white tracking-tight">StayFlow PMS</h1>
        </div>

        <div className="bg-white rounded-3xl shadow-2xl p-7 space-y-5">
          <div>
            <h2 className="text-xl font-black text-slate-900">{t('auth.resetPasswordTitle')}</h2>
            <p className="text-sm text-slate-500 mt-1">{t('auth.resetPasswordDesc')}</p>
          </div>

          {error && (
            <div className="flex items-start gap-2 bg-red-50 border border-red-200 text-red-700 text-sm rounded-xl px-4 py-3">
              <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
              <span>{error}</span>
            </div>
          )}

          {success ? (
            <div className="space-y-4">
              <div className="flex items-start gap-3 p-4 bg-emerald-50 border border-emerald-200 rounded-xl">
                <CheckCircle2 className="w-5 h-5 text-emerald-500 shrink-0 mt-0.5" />
                <p className="text-sm text-emerald-800">{t('auth.resetPasswordSuccess')}</p>
              </div>
              <button
                onClick={onBackToLogin}
                className="w-full py-3 text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 rounded-xl shadow-md transition"
              >
                {t('auth.resetPasswordBackToLogin')}
              </button>
            </div>
          ) : (
            <form onSubmit={handleSubmit} className="space-y-4">
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1.5">{t('auth.resetPasswordLabel')}</label>
                <div className="relative">
                  <input
                    type={showPassword ? 'text' : 'password'}
                    required
                    autoComplete="new-password"
                    autoFocus
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder={t('auth.resetPasswordPlaceholder')}
                    className={inputClass + ' pr-11'}
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((v) => !v)}
                    className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600"
                    tabIndex={-1}
                  >
                    {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                  </button>
                </div>
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1.5">{t('auth.resetPasswordConfirmLabel')}</label>
                <input
                  type={showPassword ? 'text' : 'password'}
                  required
                  autoComplete="new-password"
                  value={confirm}
                  onChange={(e) => setConfirm(e.target.value)}
                  placeholder={t('auth.resetPasswordPlaceholder')}
                  className={inputClass}
                />
              </div>
              <button
                type="submit"
                disabled={loading || password.length < 8}
                className="w-full py-3 text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-60 rounded-xl shadow-md transition"
              >
                {loading ? t('auth.resetPasswordSaving') : t('auth.resetPasswordSubmit')}
              </button>
              <button
                type="button"
                onClick={onBackToLogin}
                className="w-full py-3 text-sm font-semibold text-slate-600 hover:text-slate-800 transition"
              >
                {t('auth.resetPasswordBackToLogin')}
              </button>
            </form>
          )}
        </div>

        <p className="text-center text-xs text-slate-500 mt-6">
          StayFlow PMS © {new Date().getFullYear()}
        </p>
      </div>
    </div>
  );
}

export default ResetPasswordPage;
