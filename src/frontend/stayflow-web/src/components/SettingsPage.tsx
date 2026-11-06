import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Palette,
  CreditCard,
  Save,
  RotateCcw,
  CheckCircle2,
  AlertCircle,
  Info,
  Zap,
  Star,
  Building2,
  Users,
  Eye,
  EyeOff,
  History,
  Settings2,
  Cable,
} from 'lucide-react';
import { BrandingDto, EffectiveEntitlementsDto, SubscriptionPlanDto, PropertyDto, RoomTypeDto } from '../types';
import { fetchWithAuth, getStoredSession } from '../lib/api';
import { TeamManagement } from './TeamManagement';
import { AuditLogView } from './AuditLogView';
import { PropertySettingsForm } from './PropertySettingsForm';
import { BuildingFloorManager } from './BuildingFloorManager';
import { ChannelManagerView } from './ChannelManagerView';

interface SettingsPageProps {
  activeProperty: PropertyDto | null;
  onPropertyUpdated?: (p: PropertyDto) => void;
  onBrandingUpdated?: (b: BrandingDto) => void;
  roomTypes?: RoomTypeDto[];
}

type SettingsTab = 'branding' | 'subscription' | 'team' | 'audit' | 'property' | 'channels';

const CAPABILITY_LABELS: Record<string, { label: string; icon: string }> = {
  'hotel.reservations':  { label: 'Hotel Reservations',    icon: '🏨' },
  'hotel.housekeeping':  { label: 'Housekeeping',          icon: '🧹' },
  'hotel.nightAudit':    { label: 'Night Audit',           icon: '🌙' },
  'apartment.rentals':   { label: 'Apartment Rentals',     icon: '🏢' },
  'apartment.utilities': { label: 'Utility Metering',      icon: '⚡' },
  'finance.invoices':    { label: 'Invoices / Finance',    icon: '📄' },
  'reporting.advanced':  { label: 'Advanced Reporting',    icon: '📊' },
  'branding.organization':{ label: 'Organization Branding', icon: '🎨' },
  'branding.property':   { label: 'Property Branding',     icon: '🏷️' },
  'branding.customDomain':{ label: 'Custom Domain',         icon: '🌐' },
};

const PLAN_BADGE: Record<string, string> = {
  trial:        'bg-amber-100 text-amber-700 border-amber-200',
  starter:      'bg-blue-100 text-blue-700 border-blue-200',
  professional: 'bg-indigo-100 text-indigo-700 border-indigo-200',
  enterprise:   'bg-purple-100 text-purple-700 border-purple-200',
  onpremise:    'bg-slate-100 text-slate-700 border-slate-200',
};

function ColorInput({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div className="space-y-1">
      <label className="text-xs font-semibold text-slate-600 block">{label}</label>
      <div className="flex items-center gap-2">
        <input
          type="color"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          className="w-9 h-9 rounded-lg border border-slate-300 cursor-pointer p-0.5"
        />
        <input
          type="text"
          value={value}
          onChange={(e) => {
            if (/^#[0-9A-Fa-f]{0,6}$/.test(e.target.value)) onChange(e.target.value);
          }}
          maxLength={7}
          placeholder="#1E40AF"
          className="flex-1 px-3 py-2 border border-slate-300 rounded-xl text-xs font-mono focus:ring-2 focus:ring-blue-500"
        />
      </div>
    </div>
  );
}

export function SettingsPage({ activeProperty, onPropertyUpdated, onBrandingUpdated, roomTypes = [] }: SettingsPageProps) {
  const { t } = useTranslation();
  const [tab, setTab] = useState<SettingsTab>('branding');

  // Branding state
  const [branding, setBranding] = useState<BrandingDto | null>(null);
  const [brandingLoading, setBrandingLoading] = useState(true);
  const [brandingSaving, setBrandingSaving] = useState(false);
  const [brandingError, setBrandingError] = useState<string | null>(null);
  const [brandingSuccess, setBrandingSuccess] = useState(false);

  // Form fields (org-level, no propertyId for simplicity in first iteration)
  const [displayName, setDisplayName] = useState('');
  const [logoUrl, setLogoUrl] = useState('');
  const [faviconUrl, setFaviconUrl] = useState('');
  const [primaryColor, setPrimaryColor] = useState('#1E40AF');
  const [secondaryColor, setSecondaryColor] = useState('#0EA5E9');
  const [loginTitle, setLoginTitle] = useState('');
  const [loginWelcomeText, setLoginWelcomeText] = useState('');
  const [footerText, setFooterText] = useState('');
  const [supportEmail, setSupportEmail] = useState('');
  const [supportPhone, setSupportPhone] = useState('');
  const [showVendorAttribution, setShowVendorAttribution] = useState(true);
  const [previewLogo, setPreviewLogo] = useState(false);
  const [logoUploading, setLogoUploading] = useState(false);
  const [logoUploadError, setLogoUploadError] = useState<string | null>(null);

  // Subscription state
  const [entitlements, setEntitlements] = useState<EffectiveEntitlementsDto | null>(null);
  const [plans, setPlans] = useState<SubscriptionPlanDto[]>([]);
  const [subLoading, setSubLoading] = useState(true);
  const [subError, setSubError] = useState<string | null>(null);
  const [changingPlan, setChangingPlan] = useState(false);
  const [planSuccess, setPlanSuccess] = useState<string | null>(null);

  const session = getStoredSession();

  useEffect(() => {
    loadBranding();
  }, [activeProperty]);

  useEffect(() => {
    if (tab === 'subscription') loadSubscription();
  }, [tab]);

  const loadBranding = async () => {
    setBrandingLoading(true);
    setBrandingError(null);
    try {
      const params = new URLSearchParams();
      if (session?.organizationId) params.set('organizationId', session.organizationId);
      if (activeProperty) params.set('propertyId', activeProperty.id);
      const res = await fetchWithAuth(`/api/v1/branding?${params}`);
      if (res.ok) {
        const data: BrandingDto = await res.json();
        setBranding(data);
        populateForm(data);
      }
    } catch {
      setBrandingError(t('settings.loadError'));
    } finally {
      setBrandingLoading(false);
    }
  };

  const populateForm = (data: BrandingDto) => {
    setDisplayName(data.displayName);
    setLogoUrl(data.logoUrl ?? '');
    setFaviconUrl(data.faviconUrl ?? '');
    setPrimaryColor(data.primaryColor);
    setSecondaryColor(data.secondaryColor);
    setLoginTitle(data.loginTitle ?? '');
    setLoginWelcomeText(data.loginWelcomeText ?? '');
    setFooterText(data.footerText ?? '');
    setSupportEmail(data.supportEmail ?? '');
    setSupportPhone(data.supportPhone ?? '');
    setShowVendorAttribution(data.showVendorAttribution);
  };

  const handleLogoFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setLogoUploadError(null);
    // Client-side size check (2 MB)
    if (file.size > 2 * 1024 * 1024) {
      setLogoUploadError('File size must not exceed 2 MB');
      e.target.value = '';
      return;
    }
    setLogoUploading(true);
    try {
      const form = new FormData();
      form.append('file', file);
      const res = await fetchWithAuth('/api/v1/branding/upload-logo', {
        method: 'POST',
        body: form,
      });
      if (res.ok) {
        const data: { url: string } = await res.json();
        setLogoUrl(data.url);
        setPreviewLogo(true);
      } else {
        const err = await res.json().catch(() => ({}));
        setLogoUploadError(err?.detail ?? `Upload failed [${res.status}]`);
      }
    } catch {
      setLogoUploadError('An error occurred during upload');
    } finally {
      setLogoUploading(false);
      e.target.value = '';
    }
  };

  const handleSaveBranding = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!session?.organizationId) return;
    if (!/^#[0-9A-Fa-f]{6}$/.test(primaryColor)) { setBrandingError('Primary color must be in #RRGGBB format'); return; }
    if (!/^#[0-9A-Fa-f]{6}$/.test(secondaryColor)) { setBrandingError('Secondary color must be in #RRGGBB format'); return; }

    setBrandingSaving(true);
    setBrandingError(null);
    setBrandingSuccess(false);
    try {
      const res = await fetchWithAuth('/api/v1/branding', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          propertyId: null, // org-level
          displayName,
          logoUrl: logoUrl || null,
          faviconUrl: faviconUrl || null,
          primaryColor,
          secondaryColor,
          loginTitle: loginTitle || null,
          loginWelcomeText: loginWelcomeText || null,
          footerText: footerText || null,
          supportEmail: supportEmail || null,
          supportPhone: supportPhone || null,
          showVendorAttribution,
        }),
      });
      if (res.ok) {
        const updated: BrandingDto = await res.json();
        setBranding(updated);
        populateForm(updated);
        onBrandingUpdated?.(updated);
        setBrandingSuccess(true);
        setTimeout(() => setBrandingSuccess(false), 3000);
      } else {
        const err = await res.json().catch(() => ({}));
        setBrandingError(err?.detail ?? t('settings.brandingError'));
      }
    } catch {
      setBrandingError('An error occurred, please try again');
    } finally {
      setBrandingSaving(false);
    }
  };

  const handleResetBranding = () => {
    if (branding) populateForm(branding);
    setBrandingError(null);
  };

  const loadSubscription = async () => {
    if (!session?.organizationId) return;
    setSubLoading(true);
    setSubError(null);
    try {
      const [entRes, plansRes] = await Promise.all([
        fetchWithAuth(`/api/v1/organizations/${session.organizationId}/subscription`),
        fetchWithAuth('/api/v1/subscription-plans'),
      ]);
      if (entRes.ok) setEntitlements(await entRes.json());
      if (plansRes.ok) setPlans(await plansRes.json());
    } catch {
      setSubError(t('settings.loadError'));
    } finally {
      setSubLoading(false);
    }
  };

  const handleChangePlan = async (planKey: string) => {
    if (!session?.organizationId) return;
    setChangingPlan(true);
    setPlanSuccess(null);
    try {
      const res = await fetchWithAuth(
        `/api/v1/organizations/${session.organizationId}/subscription/change-plan`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ planKey }) }
      );
      if (res.ok) {
        const updated: EffectiveEntitlementsDto = await res.json();
        setEntitlements(updated);
        setPlanSuccess(`Changed plan to ${updated.planName}`);
        setTimeout(() => setPlanSuccess(null), 4000);
      }
    } catch {
      setSubError('Failed to change plan');
    } finally {
      setChangingPlan(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Page header */}
      <div>
        <h2 className="text-xl font-black text-slate-900">{t('settings.title')}</h2>
        <p className="text-sm text-slate-500 mt-0.5">Manage organization branding and subscription plan</p>
      </div>

      {/* Sub-tabs */}
      <div className="flex flex-wrap gap-1 bg-slate-100 p-1 rounded-xl w-fit border border-slate-200 text-xs font-bold overflow-x-auto">
        <button
          onClick={() => setTab('branding')}
          className={`flex items-center gap-1.5 px-3 py-2 rounded-lg transition-colors whitespace-nowrap ${tab === 'branding' ? 'bg-white text-blue-600 shadow-xs' : 'text-slate-600 hover:text-slate-900'}`}
        >
          <Palette className="w-3.5 h-3.5" />
          {t('settings.branding')}
        </button>
        <button
          onClick={() => setTab('subscription')}
          className={`flex items-center gap-1.5 px-3 py-2 rounded-lg transition-colors whitespace-nowrap ${tab === 'subscription' ? 'bg-white text-purple-600 shadow-xs' : 'text-slate-600 hover:text-slate-900'}`}
        >
          <CreditCard className="w-3.5 h-3.5" />
          {t('settings.subscription')}
        </button>
        <button
          onClick={() => setTab('team')}
          className={`flex items-center gap-1.5 px-3 py-2 rounded-lg transition-colors whitespace-nowrap ${tab === 'team' ? 'bg-white text-emerald-600 shadow-xs' : 'text-slate-600 hover:text-slate-900'}`}
        >
          <Users className="w-3.5 h-3.5" />
          {t('settings.team')}
        </button>
        <button
          onClick={() => setTab('audit')}
          className={`flex items-center gap-1.5 px-3 py-2 rounded-lg transition-colors whitespace-nowrap ${tab === 'audit' ? 'bg-white text-indigo-600 shadow-xs' : 'text-slate-600 hover:text-slate-900'}`}
        >
          <History className="w-3.5 h-3.5" />
          Audit Log
        </button>
        {activeProperty && (
          <button
            onClick={() => setTab('property')}
            className={`flex items-center gap-1.5 px-3 py-2 rounded-lg transition-colors whitespace-nowrap ${tab === 'property' ? 'bg-white text-teal-600 shadow-xs' : 'text-slate-600 hover:text-slate-900'}`}
          >
            <Settings2 className="w-3.5 h-3.5" />
            {t('settings.property')}
          </button>
        )}
        {activeProperty && (
          <button
            onClick={() => setTab('channels')}
            className={`flex items-center gap-1.5 px-3 py-2 rounded-lg transition-colors whitespace-nowrap ${tab === 'channels' ? 'bg-white text-violet-600 shadow-xs' : 'text-slate-600 hover:text-slate-900'}`}
          >
            <Cable className="w-3.5 h-3.5" />
            Channel Manager
          </button>
        )}
      </div>

      {/* ── Branding Tab ── */}
      {tab === 'branding' && (
        <div className="grid grid-cols-1 xl:grid-cols-5 gap-6">
          {/* Form */}
          <form onSubmit={handleSaveBranding} className="xl:col-span-3 space-y-5">
            {brandingLoading ? (
              <div className="bg-white rounded-2xl border border-slate-200 p-8 text-center text-slate-400 text-sm">{t('common.loading')}</div>
            ) : (
              <>
                {brandingError && (
                  <div className="flex items-center gap-2 p-3 bg-rose-50 border border-rose-200 rounded-xl text-xs text-rose-700 font-medium">
                    <AlertCircle className="w-4 h-4 shrink-0" />
                    {brandingError}
                  </div>
                )}
                {brandingSuccess && (
                  <div className="flex items-center gap-2 p-3 bg-emerald-50 border border-emerald-200 rounded-xl text-xs text-emerald-700 font-medium">
                    <CheckCircle2 className="w-4 h-4 shrink-0" />
                    {t('settings.brandingSuccess')}
                  </div>
                )}

                {/* Identity section */}
                <div className="bg-white rounded-2xl border border-slate-200 p-5 space-y-4">
                  <h3 className="text-sm font-black text-slate-800 flex items-center gap-2">
                    <Building2 className="w-4 h-4 text-blue-500" />
                    Organization Identity
                  </h3>
                  <div>
                    <label className="text-xs font-semibold text-slate-600 block mb-1">{t('settings.displayName')} *</label>
                    <input
                      type="text"
                      required
                      value={displayName}
                      onChange={(e) => setDisplayName(e.target.value)}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                      placeholder="e.g. ABC Hotel Group"
                    />
                  </div>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <div>
                      <label className="text-xs font-semibold text-slate-600 block mb-1">Logo</label>
                      <div className="flex items-center gap-2">
                        {logoUrl && (
                          <img
                            src={logoUrl}
                            alt="logo preview"
                            className="w-10 h-10 rounded-lg object-contain border border-slate-200 bg-slate-50 p-0.5 shrink-0"
                            onError={() => setLogoUrl('')}
                          />
                        )}
                        <label className={`flex-1 flex items-center gap-2 px-3 py-2 border rounded-xl text-sm cursor-pointer transition-colors ${logoUploading ? 'bg-slate-100 text-slate-400 border-slate-200' : 'border-slate-300 hover:border-blue-400 hover:bg-blue-50 text-slate-600'}`}>
                          <input
                            type="file"
                            accept="image/png,image/jpeg,image/webp"
                            className="hidden"
                            disabled={logoUploading}
                            onChange={handleLogoFileChange}
                          />
                          {logoUploading ? (
                            <span className="flex items-center gap-1.5">
                              <svg className="animate-spin w-4 h-4" viewBox="0 0 24 24" fill="none"><circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"/><path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z"/></svg>
                              Uploading...
                            </span>
                          ) : (
                            <span>{logoUrl ? 'Change Logo' : 'Upload Logo'}</span>
                          )}
                        </label>
                        {logoUrl && (
                          <button
                            type="button"
                            onClick={() => { setLogoUrl(''); setPreviewLogo(false); }}
                            className="text-xs text-red-500 hover:text-red-700 shrink-0"
                            title="Remove Logo"
                          >✕</button>
                        )}
                      </div>
                      <p className="text-[10px] text-slate-400 mt-1">PNG, JPEG, WebP · Max 2 MB</p>
                      {logoUploadError && <p className="text-[10px] text-red-500 mt-1">{logoUploadError}</p>}
                      {/* Allow pasting URL as fallback */}
                      <input
                        type="url"
                        value={logoUrl}
                        onChange={(e) => setLogoUrl(e.target.value)}
                        className="w-full mt-1.5 px-2 py-1.5 border border-slate-200 rounded-lg text-xs text-slate-500 focus:ring-1 focus:ring-blue-400"
                        placeholder="Or paste URL directly..."
                      />
                    </div>
                    <div>
                      <label className="text-xs font-semibold text-slate-600 block mb-1">{t('settings.faviconUrl')}</label>
                      <input
                        type="url"
                        value={faviconUrl}
                        onChange={(e) => setFaviconUrl(e.target.value)}
                        className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                        placeholder="https://..."
                      />
                    </div>
                  </div>
                </div>

                {/* Colors */}
                <div className="bg-white rounded-2xl border border-slate-200 p-5 space-y-4">
                  <h3 className="text-sm font-black text-slate-800 flex items-center gap-2">
                    <Palette className="w-4 h-4 text-pink-500" />
                    Brand Colors
                  </h3>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <ColorInput label={t('settings.primaryColor')} value={primaryColor} onChange={setPrimaryColor} />
                    <ColorInput label={t('settings.secondaryColor')} value={secondaryColor} onChange={setSecondaryColor} />
                  </div>
                </div>

                {/* Login page text */}
                <div className="bg-white rounded-2xl border border-slate-200 p-5 space-y-4">
                  <h3 className="text-sm font-black text-slate-800 flex items-center gap-2">
                    <Info className="w-4 h-4 text-indigo-500" />
                    Login Page Text
                  </h3>
                  <div>
                    <label className="text-xs font-semibold text-slate-600 block mb-1">{t('settings.loginTitle')}</label>
                    <input
                      type="text"
                      value={loginTitle}
                      onChange={(e) => setLoginTitle(e.target.value)}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                      placeholder="e.g. Welcome to ABC Hotel"
                    />
                  </div>
                  <div>
                    <label className="text-xs font-semibold text-slate-600 block mb-1">{t('settings.loginWelcome')}</label>
                    <textarea
                      value={loginWelcomeText}
                      onChange={(e) => setLoginWelcomeText(e.target.value)}
                      rows={2}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 resize-none"
                      placeholder="System description..."
                    />
                  </div>
                </div>

                {/* Footer & Support */}
                <div className="bg-white rounded-2xl border border-slate-200 p-5 space-y-4">
                  <h3 className="text-sm font-black text-slate-800 flex items-center gap-2">
                    <Users className="w-4 h-4 text-teal-500" />
                    Footer & Contact Info
                  </h3>
                  <div>
                    <label className="text-xs font-semibold text-slate-600 block mb-1">{t('settings.footerText')}</label>
                    <input
                      type="text"
                      value={footerText}
                      onChange={(e) => setFooterText(e.target.value)}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                      placeholder="e.g. ABC Hotel Group © 2026"
                    />
                  </div>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <div>
                      <label className="text-xs font-semibold text-slate-600 block mb-1">{t('settings.supportEmail')}</label>
                      <input
                        type="email"
                        value={supportEmail}
                        onChange={(e) => setSupportEmail(e.target.value)}
                        className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                        placeholder="support@example.com"
                      />
                    </div>
                    <div>
                      <label className="text-xs font-semibold text-slate-600 block mb-1">{t('settings.supportPhone')}</label>
                      <input
                        type="tel"
                        inputMode="tel"
                        value={supportPhone}
                        onChange={(e) => setSupportPhone(e.target.value.replace(/[^\d+\-\s]/g, ''))}
                        className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                        placeholder="02-xxx-xxxx"
                      />
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    <button
                      type="button"
                      onClick={() => setShowVendorAttribution((v) => !v)}
                      aria-pressed={showVendorAttribution}
                      className={`relative shrink-0 w-10 h-6 rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-blue-400 ${showVendorAttribution ? 'bg-blue-600' : 'bg-slate-300'}`}
                    >
                      <span className={`absolute left-0 top-1 w-4 h-4 bg-white rounded-full shadow transition-transform ${showVendorAttribution ? 'translate-x-5' : 'translate-x-1'}`} />
                    </button>
                    <span className="text-xs font-semibold text-slate-700">Show "Powered by StayFlow PMS"</span>
                  </div>
                </div>

                {/* Actions */}
                <div className="flex items-center gap-3">
                  <button
                    type="submit"
                    disabled={brandingSaving}
                    className="flex items-center gap-2 px-5 py-2.5 text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 rounded-xl shadow-md disabled:opacity-60"
                  >
                    <Save className="w-4 h-4" />
                    {brandingSaving ? t('common.saving') : t('settings.saveBranding')}
                  </button>
                  <button
                    type="button"
                    onClick={handleResetBranding}
                    className="flex items-center gap-2 px-4 py-2.5 text-sm font-bold text-slate-600 bg-slate-100 hover:bg-slate-200 rounded-xl"
                  >
                    <RotateCcw className="w-4 h-4" />
                    {t('common.cancel')}
                  </button>
                </div>
              </>
            )}
          </form>

          {/* Preview panel */}
          <div className="xl:col-span-2 space-y-4">
            <div className="bg-white rounded-2xl border border-slate-200 p-5 space-y-4 sticky top-24">
              <h3 className="text-sm font-black text-slate-800">Preview</h3>

              {/* Header preview */}
              <div
                className="rounded-xl p-3 flex items-center gap-3 text-white"
                style={{ background: `linear-gradient(135deg, ${primaryColor}, ${secondaryColor})` }}
              >
                {logoUrl && previewLogo ? (
                  <img src={logoUrl} alt="logo" className="w-8 h-8 rounded-lg object-contain bg-white/20 p-0.5" onError={() => setPreviewLogo(false)} />
                ) : (
                  <div className="w-8 h-8 rounded-lg bg-white/20 flex items-center justify-center text-xs font-black">
                    {displayName.charAt(0) || 'S'}
                  </div>
                )}
                <div>
                  <p className="text-sm font-black leading-tight">{displayName || 'StayFlow PMS'}</p>
                  <p className="text-[10px] opacity-75">{loginWelcomeText || 'Property Management System'}</p>
                </div>
              </div>

              {logoUrl && (
                <button
                  type="button"
                  onClick={() => setPreviewLogo((v) => !v)}
                  className="flex items-center gap-1.5 text-xs text-slate-500 hover:text-slate-700"
                >
                  {previewLogo ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                  {previewLogo ? 'Hide logo in preview' : 'Show logo in preview'}
                </button>
              )}

              {/* Color swatches */}
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <div className="h-10 rounded-lg border border-slate-200" style={{ background: primaryColor }} />
                  <p className="text-[10px] text-slate-500 mt-1 font-mono text-center">{primaryColor} Primary</p>
                </div>
                <div>
                  <div className="h-10 rounded-lg border border-slate-200" style={{ background: secondaryColor }} />
                  <p className="text-[10px] text-slate-500 mt-1 font-mono text-center">{secondaryColor} Secondary</p>
                </div>
              </div>

              {/* Footer preview */}
              <div className="border-t border-slate-100 pt-3">
                <p className="text-[10px] text-slate-400 text-center">
                  {footerText || 'StayFlow PMS © 2026'}
                  {showVendorAttribution && footerText ? ' — Powered by StayFlow PMS' : ''}
                </p>
                {supportEmail && <p className="text-[10px] text-slate-400 text-center mt-0.5">📧 {supportEmail}</p>}
                {supportPhone && <p className="text-[10px] text-slate-400 text-center mt-0.5">📞 {supportPhone}</p>}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ── Team Management Tab ── */}
      {tab === 'team' && <TeamManagement />}

      {/* ── Audit Log Tab ── */}
      {tab === 'audit' && <AuditLogView />}

      {/* ── Property Settings Tab ── */}
      {tab === 'property' && activeProperty && onPropertyUpdated && (() => {
        const session = getStoredSession();
        return (
          <div className="space-y-8">
            <PropertySettingsForm property={activeProperty} onUpdated={onPropertyUpdated} />
            <div className="border-t pt-6">
              {session && (
                <BuildingFloorManager
                  property={activeProperty}
                  token={session.token}
                  orgId={session.organizationId ?? ''}
                />
              )}
            </div>
          </div>
        );
      })()}

      {/* ── Channel Manager Tab ── */}
      {tab === 'channels' && (
        <ChannelManagerView activeProperty={activeProperty} roomTypes={roomTypes} />
      )}

      {/* ── Subscription Tab ── */}
      {tab === 'subscription' && (
        <div className="space-y-5">
          {subLoading ? (
            <div className="bg-white rounded-2xl border border-slate-200 p-8 text-center text-slate-400 text-sm">{t('common.loading')}</div>
          ) : subError ? (
            <div className="flex items-center gap-2 p-4 bg-rose-50 border border-rose-200 rounded-2xl text-sm text-rose-700">
              <AlertCircle className="w-4 h-4" />
              {subError}
            </div>
          ) : (
            <>
              {planSuccess && (
                <div className="flex items-center gap-2 p-3 bg-emerald-50 border border-emerald-200 rounded-xl text-xs text-emerald-700 font-medium">
                  <CheckCircle2 className="w-4 h-4" />
                  {planSuccess}
                </div>
              )}

              {/* Current plan card */}
              {entitlements && (
                <div className="bg-white rounded-2xl border border-slate-200 p-6 space-y-4">
                  <div className="flex items-start justify-between gap-4 flex-wrap">
                    <div>
                      <p className="text-xs text-slate-400 font-semibold uppercase tracking-wider mb-1">{t('settings.currentPlan')}</p>
                      <div className="flex items-center gap-2 flex-wrap">
                        <h3 className="text-xl font-black text-slate-900">{entitlements.planName}</h3>
                        <span className={`px-2 py-0.5 rounded-full text-[10px] font-black uppercase border ${PLAN_BADGE[entitlements.planKey] ?? 'bg-slate-100 text-slate-600 border-slate-200'}`}>
                          {entitlements.planKey}
                        </span>
                        <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold ${entitlements.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-rose-100 text-rose-700'}`}>
                          {entitlements.isActive ? 'Active' : 'Inactive'}
                        </span>
                      </div>
                    </div>
                    {entitlements.trialEndsAtUtc && (
                      <div className="text-right">
                        <p className="text-xs text-amber-600 font-semibold">Trial ends</p>
                        <p className="text-sm font-bold text-amber-700">
                          {new Date(entitlements.trialEndsAtUtc).toLocaleDateString()}
                        </p>
                      </div>
                    )}
                  </div>

                  {/* Usage limits */}
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <div className="p-3 bg-slate-50 rounded-xl border border-slate-100">
                      <p className="text-xs text-slate-400 font-semibold">Max Users</p>
                      <p className="text-lg font-black text-slate-800">
                        {entitlements.maxUsers ? `Up to ${entitlements.maxUsers}` : 'Unlimited'}
                      </p>
                    </div>
                    <div className="p-3 bg-slate-50 rounded-xl border border-slate-100">
                      <p className="text-xs text-slate-400 font-semibold">Max Properties</p>
                      <p className="text-lg font-black text-slate-800">
                        {entitlements.maxProperties ? `Up to ${entitlements.maxProperties}` : 'Unlimited'}
                      </p>
                    </div>
                  </div>

                  {/* Capabilities */}
                  <div>
                    <p className="text-xs font-black text-slate-500 uppercase tracking-wider mb-2">{t('settings.entitlements')}</p>
                    <div className="flex flex-wrap gap-2">
                      {entitlements.capabilities.map((cap) => {
                        const info = CAPABILITY_LABELS[cap];
                        return (
                          <span key={cap} className="flex items-center gap-1 px-3 py-1 bg-blue-50 border border-blue-100 rounded-full text-xs font-semibold text-blue-700">
                            <span>{info?.icon ?? '✓'}</span>
                            {info?.label ?? cap}
                          </span>
                        );
                      })}
                    </div>
                  </div>
                </div>
              )}

              {/* Available plans */}
              {plans.length > 0 && (
                <div>
                  <h3 className="text-sm font-black text-slate-800 mb-3">Available Plans</h3>
                  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                    {plans.map((plan) => {
                      const isCurrent = plan.key === entitlements?.planKey;
                      return (
                        <div
                          key={plan.id}
                          className={`bg-white rounded-2xl border p-5 space-y-3 ${isCurrent ? 'border-blue-400 ring-2 ring-blue-100' : 'border-slate-200'}`}
                        >
                          <div className="flex items-center justify-between">
                            <div>
                              <div className="flex items-center gap-2">
                                <Star className={`w-4 h-4 ${isCurrent ? 'text-amber-400 fill-amber-400' : 'text-slate-300'}`} />
                                <h4 className="text-sm font-black text-slate-900">{plan.name}</h4>
                              </div>
                              <p className="text-[10px] text-slate-400 font-mono mt-0.5">{plan.key}</p>
                            </div>
                            {isCurrent && (
                              <span className="text-[10px] font-black px-2 py-0.5 bg-blue-100 text-blue-700 rounded-full">{t('settings.currentPlan')}</span>
                            )}
                          </div>

                          <div className="text-xs text-slate-500 space-y-0.5">
                            <p>👥 {plan.maxUsers ? `Up to ${plan.maxUsers} users` : 'Unlimited users'}</p>
                            <p>🏨 {plan.maxProperties ? `Up to ${plan.maxProperties} properties` : 'Unlimited properties'}</p>
                            {plan.trialDays > 0 && <p><Zap className="inline w-3 h-3 text-amber-500" /> {plan.trialDays}-day trial</p>}
                          </div>

                          <div className="flex flex-wrap gap-1">
                            {plan.capabilities.slice(0, 4).map((cap) => (
                              <span key={cap} className="text-[9px] px-1.5 py-0.5 bg-slate-100 text-slate-600 rounded-full">
                                {CAPABILITY_LABELS[cap]?.label ?? cap}
                              </span>
                            ))}
                            {plan.capabilities.length > 4 && (
                              <span className="text-[9px] px-1.5 py-0.5 bg-slate-100 text-slate-500 rounded-full">
                                +{plan.capabilities.length - 4} more
                              </span>
                            )}
                          </div>

                          {!isCurrent && (
                            <button
                              onClick={() => handleChangePlan(plan.key)}
                              disabled={changingPlan}
                              className="w-full py-2 text-xs font-bold text-blue-600 bg-blue-50 hover:bg-blue-100 rounded-xl border border-blue-200 disabled:opacity-60"
                            >
                              {changingPlan ? 'Switching...' : 'Switch to this plan'}
                            </button>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}
