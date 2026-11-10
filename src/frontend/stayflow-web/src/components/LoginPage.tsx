import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Building2, Zap, Eye, EyeOff, Mail, ArrowLeft, CheckCircle2 } from 'lucide-react';
import { saveSession, API_BASE } from '../lib/api';
import LanguageSwitcher from './LanguageSwitcher';

interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  activeOrganizationId: string;
  user: {
    id: string;
    email: string;
    fullName: string;
    preferredLanguage?: string;
    role?: string;
  };
}

interface Props {
  onAuthenticated: (auth: AuthResponse) => void;
}

export function LoginPage({ onAuthenticated }: Props) {
  const { t } = useTranslation();
  const [tab, setTab] = useState<'login' | 'register' | 'forgot'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [fullName, setFullName] = useState('');
  const [orgName, setOrgName] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [demoLoading, setDemoLoading] = useState(false);
  const [error, setError] = useState('');

  // Forgot password
  const [forgotEmail, setForgotEmail] = useState('');
  const [forgotSent, setForgotSent] = useState(false);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const res = await fetch(API_BASE + '/api/v1/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      });
      if (!res.ok) {
        setError(t('auth.invalidCredentials'));
        return;
      }
      const auth: AuthResponse = await res.json();
      saveSession(auth.accessToken, auth.refreshToken, auth.activeOrganizationId, auth.user);
      onAuthenticated(auth);
    } catch {
      setError(t('auth.invalidCredentials'));
    } finally {
      setLoading(false);
    }
  };

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const res = await fetch(API_BASE + '/api/v1/auth/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fullName, email, password, organizationName: orgName }),
      });
      if (!res.ok) {
        let msg = t('auth.registerError');
        try {
          const body = await res.json();
          if (body?.detail) msg = body.detail;
          else if (body?.errors) {
            const first = Object.values(body.errors as Record<string, string[]>).flat()[0];
            if (first) msg = first;
          }
        } catch { /* ignore */ }
        setError(msg);
        return;
      }
      const auth: AuthResponse = await res.json();
      saveSession(auth.accessToken, auth.refreshToken, auth.activeOrganizationId, auth.user);
      onAuthenticated(auth);
    } catch {
      setError(t('auth.registerError'));
    } finally {
      setLoading(false);
    }
  };

  const handleQuickDemo = async () => {
    setError('');
    setDemoLoading(true);
    try {
      const res = await fetch(API_BASE + '/api/v1/auth/quick-demo', { method: 'POST' });
      if (!res.ok) { setError(t('auth.registerError')); return; }
      const auth: AuthResponse = await res.json();
      saveSession(auth.accessToken, auth.refreshToken, auth.activeOrganizationId, auth.user);
      onAuthenticated(auth);
    } catch {
      setError(t('auth.registerError'));
    } finally {
      setDemoLoading(false);
    }
  };

  const handleForgotPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await fetch(API_BASE + '/api/v1/auth/forgot-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: forgotEmail }),
      });
      setForgotSent(true);
    } catch { /* always show success to prevent enumeration */ } finally {
      setLoading(false);
    }
  };

  const inputClass =
    'w-full px-4 py-2.5 border border-slate-300 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent bg-white placeholder-slate-400 transition';

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-indigo-950 to-blue-900 flex flex-col items-center justify-center p-4">
      {/* Language switcher — top right */}
      <div className="absolute top-4 right-4">
        <LanguageSwitcher />
      </div>

      {/* Card */}
      <div className="w-full max-w-md">
        {/* Logo / branding */}
        <div className="text-center mb-8">
          <div className="inline-flex items-center justify-center w-16 h-16 rounded-2xl bg-white/10 backdrop-blur-md border border-white/20 mb-4 shadow-xl">
            <Building2 className="w-8 h-8 text-white" />
          </div>
          <h1 className="text-3xl font-black text-white tracking-tight">StayFlow PMS</h1>
          <p className="text-slate-400 text-sm mt-1">{t('auth.subtitle')}</p>
        </div>

        <div className="bg-white rounded-3xl shadow-2xl overflow-hidden">
          {/* Tabs — hidden on forgot-password view */}
          {tab !== 'forgot' && (
            <div className="flex border-b border-slate-100">
              <button
                onClick={() => { setTab('login'); setError(''); }}
                className={`flex-1 py-4 text-sm font-bold transition-colors ${
                  tab === 'login'
                    ? 'text-blue-600 border-b-2 border-blue-600 bg-blue-50/50'
                    : 'text-slate-500 hover:text-slate-700 hover:bg-slate-50'
                }`}
              >
                {t('auth.signInTab')}
              </button>
              <button
                onClick={() => { setTab('register'); setError(''); }}
                className={`flex-1 py-4 text-sm font-bold transition-colors ${
                  tab === 'register'
                    ? 'text-blue-600 border-b-2 border-blue-600 bg-blue-50/50'
                    : 'text-slate-500 hover:text-slate-700 hover:bg-slate-50'
                }`}
              >
                {t('auth.registerTab')}
              </button>
            </div>
          )}

          <div className="p-7 space-y-5">
            {/* Error banner */}
            {error && (
              <div className="bg-red-50 border border-red-200 text-red-700 text-sm rounded-xl px-4 py-3">
                {error}
              </div>
            )}

            {/* Sign In form */}
            {tab === 'login' && (
              <form onSubmit={handleLogin} className="space-y-4">
                <div>
                  <label className="block text-xs font-semibold text-slate-700 mb-1.5">{t('auth.emailLabel')}</label>
                  <input
                    type="email"
                    required
                    autoComplete="email"
                    placeholder={t('auth.emailPlaceholder')}
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    className={inputClass}
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-700 mb-1.5">{t('auth.passwordLabel')}</label>
                  <div className="relative">
                    <input
                      type={showPassword ? 'text' : 'password'}
                      required
                      autoComplete="current-password"
                      placeholder={t('auth.passwordPlaceholder')}
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
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
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <input
                      id="rememberMe"
                      type="checkbox"
                      checked={rememberMe}
                      onChange={(e) => setRememberMe(e.target.checked)}
                      className="w-4 h-4 rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                    />
                    <label htmlFor="rememberMe" className="text-sm text-slate-600 cursor-pointer select-none">
                      {t('auth.rememberMe')}
                    </label>
                  </div>
                  <button
                    type="button"
                    onClick={() => { setTab('forgot'); setError(''); setForgotSent(false); setForgotEmail(email); }}
                    className="text-xs text-blue-600 hover:text-blue-800 font-medium"
                  >
                    {t('auth.forgotPasswordLink')}
                  </button>
                </div>
                <button
                  type="submit"
                  disabled={loading}
                  className="w-full py-3 text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-60 rounded-xl shadow-md transition"
                >
                  {loading ? t('auth.signingIn') : t('auth.submitLogin')}
                </button>
              </form>
            )}

            {/* Forgot password panel */}
            {tab === 'forgot' && (
              <div className="space-y-4">
                <button
                  onClick={() => { setTab('login'); setError(''); setForgotSent(false); }}
                  className="flex items-center gap-1.5 text-xs text-slate-500 hover:text-slate-700 font-medium"
                >
                  <ArrowLeft className="w-3.5 h-3.5" />
                  {t('auth.forgotPasswordBack')}
                </button>
                <div>
                  <h3 className="text-base font-black text-slate-900">{t('auth.forgotPasswordTitle')}</h3>
                  <p className="text-sm text-slate-500 mt-1">{t('auth.forgotPasswordDesc')}</p>
                </div>
                {forgotSent ? (
                  <div className="flex items-start gap-3 p-4 bg-emerald-50 border border-emerald-200 rounded-xl">
                    <CheckCircle2 className="w-5 h-5 text-emerald-500 shrink-0 mt-0.5" />
                    <p className="text-sm text-emerald-800">{t('auth.forgotPasswordSent')}</p>
                  </div>
                ) : (
                  <form onSubmit={handleForgotPassword} className="space-y-4">
                    <div>
                      <label className="block text-xs font-semibold text-slate-700 mb-1.5">{t('auth.forgotPasswordEmailLabel')}</label>
                      <div className="relative">
                        <Mail className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
                        <input
                          type="email"
                          required
                          autoFocus
                          autoComplete="email"
                          value={forgotEmail}
                          onChange={(e) => setForgotEmail(e.target.value)}
                          className={inputClass + ' pl-9'}
                          placeholder="you@example.com"
                        />
                      </div>
                    </div>
                    <button
                      type="submit"
                      disabled={loading}
                      className="w-full py-3 text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-60 rounded-xl shadow-md transition"
                    >
                      {loading ? t('auth.forgotPasswordSending') : t('auth.forgotPasswordSubmit')}
                    </button>
                  </form>
                )}
              </div>
            )}

            {/* Register form */}
            {tab === 'register' && (
              <form onSubmit={handleRegister} className="space-y-4">
                <div>
                  <label className="block text-xs font-semibold text-slate-700 mb-1.5">{t('auth.fullNameLabel')}</label>
                  <input
                    type="text"
                    required
                    autoComplete="name"
                    placeholder={t('auth.fullNamePlaceholder')}
                    value={fullName}
                    onChange={(e) => setFullName(e.target.value)}
                    className={inputClass}
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-700 mb-1.5">{t('auth.emailLabel')}</label>
                  <input
                    type="email"
                    required
                    autoComplete="email"
                    placeholder={t('auth.emailPlaceholder')}
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    className={inputClass}
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-700 mb-1.5">{t('auth.passwordLabel')}</label>
                  <div className="relative">
                    <input
                      type={showPassword ? 'text' : 'password'}
                      required
                      autoComplete="new-password"
                      placeholder={t('auth.passwordPlaceholder')}
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
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
                  <label className="block text-xs font-semibold text-slate-700 mb-1.5">{t('auth.orgNameLabel')}</label>
                  <input
                    type="text"
                    required
                    placeholder={t('auth.orgNamePlaceholder')}
                    value={orgName}
                    onChange={(e) => setOrgName(e.target.value)}
                    className={inputClass}
                  />
                </div>
                <button
                  type="submit"
                  disabled={loading}
                  className="w-full py-3 text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-60 rounded-xl shadow-md transition"
                >
                  {loading ? t('auth.registering') : t('auth.submitRegister')}
                </button>
              </form>
            )}

            {/* Divider + Quick Demo — only on login/register tabs */}
            {tab !== 'forgot' && (
              <>
                <div className="relative flex items-center gap-3">
                  <div className="flex-1 border-t border-slate-200" />
                  <span className="text-xs text-slate-400 font-medium">{t('auth.orLabel')}</span>
                  <div className="flex-1 border-t border-slate-200" />
                </div>
                <button
                  onClick={handleQuickDemo}
                  disabled={demoLoading}
                  className="w-full py-3 text-sm font-bold text-amber-700 bg-amber-50 hover:bg-amber-100 disabled:opacity-60 border border-amber-200 rounded-xl flex items-center justify-center gap-2 transition"
                >
                  <Zap className="w-4 h-4 text-amber-500 fill-amber-500" />
                  {demoLoading ? '…' : t('auth.quickDemo')}
                </button>
                <p className="text-center text-[11px] text-slate-400">{t('auth.quickDemoHint')}</p>
              </>
            )}
          </div>
        </div>

        <p className="text-center text-xs text-slate-500 mt-6">
          StayFlow PMS © {new Date().getFullYear()}
        </p>
      </div>
    </div>
  );
}

export default LoginPage;
