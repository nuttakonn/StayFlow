import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Save, MapPin, Clock, Building2, CheckCircle2, AlertCircle, RefreshCw } from 'lucide-react';
import { PropertyDto } from '../types';
import { fetchWithAuth, API_BASE } from '../lib/api';

interface PropertySettingsFormProps {
  property: PropertyDto;
  onUpdated: (updated: PropertyDto) => void;
}

const TIMEZONES = [
  'Asia/Bangkok',
  'Asia/Singapore',
  'Asia/Kuala_Lumpur',
  'Asia/Jakarta',
  'Asia/Manila',
  'Asia/Tokyo',
  'UTC',
];

const CURRENCIES = ['THB', 'USD', 'EUR', 'SGD', 'MYR', 'JPY', 'IDR'];

export function PropertySettingsForm({ property, onUpdated }: PropertySettingsFormProps) {
  const { t } = useTranslation();
  const [name, setName] = useState(property.name);
  const [address, setAddress] = useState(property.address ?? '');
  const [contactPhone, setContactPhone] = useState(property.contactPhone ?? '');
  const [contactEmail, setContactEmail] = useState(property.contactEmail ?? '');
  const [timezone, setTimezone] = useState(property.ianaTimeZone);
  const [currency, setCurrency] = useState(property.defaultCurrency);
  const [checkIn, setCheckIn] = useState(property.checkInTime);
  const [checkOut, setCheckOut] = useState(property.checkOutTime);

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sessionExpired, setSessionExpired] = useState(false);
  const [recovering, setRecovering] = useState(false);
  const [success, setSuccess] = useState(false);

  const recoverSession = async () => {
    setSessionExpired(true);
    setRecovering(true);
    try {
      const r = await fetch(API_BASE + '/api/v1/auth/quick-demo', { method: 'POST' });
      if (r.ok) {
        const auth = await r.json();
        localStorage.setItem('stayflow_auth_token', auth.accessToken);
        if (auth.refreshToken) localStorage.setItem('stayflow_refresh_token', auth.refreshToken);
        if (auth.activeOrganizationId) localStorage.setItem('stayflow_active_org_id', auth.activeOrganizationId);
        if (auth.user) localStorage.setItem('stayflow_user', JSON.stringify(auth.user));
      }
    } catch { /* reload anyway */ }
    setTimeout(() => window.location.reload(), 1500);
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) { setError('Property name is required'); return; }

    setSaving(true);
    setError(null);
    setSessionExpired(false);
    setSuccess(false);
    try {
      const res = await fetchWithAuth(`/api/v1/properties/${property.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name,
          address: address || null,
          contactPhone: contactPhone || null,
          contactEmail: contactEmail || null,
          ianaTimeZone: timezone,
          defaultCurrency: currency,
          checkInTime: checkIn,
          checkOutTime: checkOut,
        }),
      });

      if (res.ok) {
        const updated: PropertyDto = await res.json();
        onUpdated(updated);
        setSuccess(true);
        setTimeout(() => setSuccess(false), 3000);
      } else if (res.status === 404) {
        await recoverSession();
      } else {
        const err = await res.json().catch(() => ({}));
        setError(`[${res.status}] ${err?.detail ?? 'Failed to save, please try again'}`);
      }
    } catch {
      setError('Connection error, please try again');
    } finally {
      setSaving(false);
    }
  };

  return (
    <form onSubmit={handleSave} className="space-y-5">
      {/* Header */}
      <div>
        <h3 className="text-base font-black text-slate-900 flex items-center gap-2">
          <Building2 className="w-4 h-4 text-teal-500" />
          {t('settings.title')} — {property.name}
        </h3>
        <p className="text-xs text-slate-400 mt-0.5">
          {property.businessType === 'Apartment' ? `🏢 ${t('property.typeApartment')}` : `🏨 ${t('property.typeHotel')}`} · Code {property.code}
        </p>
      </div>

      {/* Notifications */}
      {sessionExpired && (
        <div className="p-4 bg-amber-50 border border-amber-200 rounded-xl">
          <p className="flex items-center gap-2 text-xs font-bold text-amber-800">
            <RefreshCw className={`w-4 h-4 shrink-0 ${recovering ? 'animate-spin' : ''}`} />
            {recovering ? t('team.sessionRecovering') : t('team.sessionExpired')}
          </p>
          {recovering && (
            <p className="text-xs text-amber-700 mt-1">{t('team.sessionReloading')}</p>
          )}
        </div>
      )}
      {error && (
        <div className="flex items-center gap-2 p-3 bg-rose-50 border border-rose-200 rounded-xl text-xs text-rose-700 font-medium">
          <AlertCircle className="w-4 h-4 shrink-0" />{error}
        </div>
      )}
      {success && (
        <div className="flex items-center gap-2 p-3 bg-emerald-50 border border-emerald-200 rounded-xl text-xs text-emerald-700 font-medium">
          <CheckCircle2 className="w-4 h-4 shrink-0" />Property saved successfully
        </div>
      )}

      {/* Identity */}
      <div className="bg-white rounded-2xl border border-slate-200 p-5 space-y-4">
        <h4 className="text-sm font-black text-slate-700 flex items-center gap-2">
          <Building2 className="w-3.5 h-3.5 text-blue-500" />
          General Information
        </h4>
        {/* Business Type - read-only */}
        <div className="flex items-center gap-3 p-3 bg-slate-50 rounded-xl border border-slate-200">
          <span className="text-lg">{property.businessType === 'Apartment' ? '🏢' : '🏨'}</span>
          <div>
            <p className="text-xs font-black text-slate-700">
              {t('property.businessTypeLabel')}: {property.businessType === 'Apartment' ? `${t('property.typeApartment')} (Apartment)` : property.businessType === 'Hybrid' ? `${t('property.typeHotel')}+${t('property.typeApartment')} (Hybrid)` : `${t('property.typeHotel')} (Hotel)`}
            </p>
            <p className="text-[10px] text-slate-400 mt-0.5">Business type cannot be changed after the property is created</p>
          </div>
        </div>
        <div>
          <label className="text-xs font-semibold text-slate-600 block mb-1">{t('property.nameLabel')}</label>
          <input
            type="text"
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label className="text-xs font-semibold text-slate-600 block mb-1">Address</label>
          <input
            type="text"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
            placeholder="e.g. 123 Main St, Bangkok"
          />
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label className="text-xs font-semibold text-slate-600 block mb-1">{t('common.phone')}</label>
            <input
              type="tel"
              inputMode="tel"
              value={contactPhone}
              onChange={(e) => setContactPhone(e.target.value.replace(/[^\d+\-\s]/g, ''))}
              className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
              placeholder="02-xxx-xxxx"
            />
          </div>
          <div>
            <label className="text-xs font-semibold text-slate-600 block mb-1">{t('common.email')}</label>
            <input
              type="email"
              value={contactEmail}
              onChange={(e) => setContactEmail(e.target.value)}
              className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
              placeholder="info@hotel.com"
            />
          </div>
        </div>
      </div>

      {/* Locale */}
      <div className="bg-white rounded-2xl border border-slate-200 p-5 space-y-4">
        <h4 className="text-sm font-black text-slate-700 flex items-center gap-2">
          <MapPin className="w-3.5 h-3.5 text-pink-500" />
          Time Zone &amp; Currency
        </h4>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label className="text-xs font-semibold text-slate-600 block mb-1">IANA Time Zone</label>
            <select
              value={timezone}
              onChange={(e) => setTimezone(e.target.value)}
              className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm bg-white focus:ring-2 focus:ring-blue-500"
            >
              {TIMEZONES.map(tz => <option key={tz} value={tz}>{tz}</option>)}
            </select>
          </div>
          <div>
            <label className="text-xs font-semibold text-slate-600 block mb-1">{t('common.currency')}</label>
            <select
              value={currency}
              onChange={(e) => setCurrency(e.target.value)}
              className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm bg-white focus:ring-2 focus:ring-blue-500"
            >
              {CURRENCIES.map(c => <option key={c} value={c}>{c}</option>)}
            </select>
          </div>
        </div>
      </div>

      {/* Check-in / check-out (Hotels only) */}
      {property.businessType !== 'Apartment' && (
        <div className="bg-white rounded-2xl border border-slate-200 p-5 space-y-4">
          <h4 className="text-sm font-black text-slate-700 flex items-center gap-2">
            <Clock className="w-3.5 h-3.5 text-amber-500" />
            Check-in / Check-out Policy
          </h4>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label className="text-xs font-semibold text-slate-600 block mb-1">{t('reservations.checkIn')}</label>
              <input
                type="time"
                value={checkIn}
                onChange={(e) => setCheckIn(e.target.value)}
                className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="text-xs font-semibold text-slate-600 block mb-1">{t('reservations.checkOut')}</label>
              <input
                type="time"
                value={checkOut}
                onChange={(e) => setCheckOut(e.target.value)}
                className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
              />
            </div>
          </div>
        </div>
      )}

      {/* Save */}
      <div>
        <button
          type="submit"
          disabled={saving}
          className="flex items-center gap-2 px-5 py-2.5 text-sm font-bold text-white bg-teal-600 hover:bg-teal-700 rounded-xl shadow-md disabled:opacity-60"
        >
          <Save className="w-4 h-4" />
          {saving ? t('common.saving') : t('common.save')}
        </button>
      </div>
    </form>
  );
}
