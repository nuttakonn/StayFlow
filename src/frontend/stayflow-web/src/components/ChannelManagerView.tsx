import { useEffect, useState } from 'react';
import {
  Cable,
  Plus,
  RefreshCw,
  Trash2,
  ChevronDown,
  ChevronRight,
  AlertCircle,
  CheckCircle2,
  Clock,
  BookOpen,
  Link2,
  Link2Off,
} from 'lucide-react';
import { RoomTypeDto, PropertyDto } from '../types';
import { apiFetch, getStoredSession } from '../lib/api';
import { useTranslation } from 'react-i18next';

// ── Types ─────────────────────────────────────────────────────────────────────

interface ChannelConnectionDto {
  id: string;
  propertyId: string;
  provider: number;
  providerName: string;
  providerPropertyId: string;
  isActive: boolean;
  lastSyncAtUtc: string | null;
  lastSyncStatus: string | null;
  createdAtUtc: string;
}

interface ChannelRateMappingDto {
  id: string;
  channelConnectionId: string;
  roomTypeId: string;
  channelRoomTypeCode: string;
  channelRatePlanCode: string;
  isActive: boolean;
}

interface ChannelReservationDto {
  id: string;
  channelBookingReference: string;
  otaSource: string;
  reservationId: string | null;
  processingStatus: string;
  receivedAtUtc: string;
  processedAtUtc: string | null;
  failureReason: string | null;
}

const PROVIDERS = [
  { value: 1, label: 'STAAH', description: 'Asia-Pacific coverage (recommended for Thailand)' },
  { value: 2, label: 'SiteMinder', description: 'Global coverage — 450+ OTAs' },
  { value: 3, label: 'Roomcloud', description: 'European channel manager' },
  { value: 4, label: 'MyAllocator', description: 'Budget-friendly global option' },
];

const STATUS_BADGE: Record<string, string> = {
  Pending:   'bg-amber-100 text-amber-700',
  Created:   'bg-emerald-100 text-emerald-700',
  Duplicate: 'bg-slate-100 text-slate-600',
  Failed:    'bg-rose-100 text-rose-700',
  Cancelled: 'bg-slate-100 text-slate-500',
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtDate(d: string | null) {
  if (!d) return '—';
  return new Date(d).toLocaleString('th-TH', { dateStyle: 'medium', timeStyle: 'short' });
}

// ── Props ─────────────────────────────────────────────────────────────────────

interface Props {
  activeProperty: PropertyDto | null;
  roomTypes: RoomTypeDto[];
}

// ── Main component ────────────────────────────────────────────────────────────

export function ChannelManagerView({ activeProperty, roomTypes }: Props) {
  const { t } = useTranslation();
  const session = getStoredSession();
  const token = session?.token ?? '';
  const orgId = session?.organizationId ?? '';

  const [connections, setConnections] = useState<ChannelConnectionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Create connection form
  const [showCreate, setShowCreate] = useState(false);
  const [newProvider, setNewProvider] = useState(1);
  const [newPropId, setNewPropId] = useState('');
  const [newApiKey, setNewApiKey] = useState('');
  const [newApiSecret, setNewApiSecret] = useState('');
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  // Expanded connection (rate mappings panel)
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [mappings, setMappings] = useState<Record<string, ChannelRateMappingDto[]>>({});
  const [mappingLoading, setMappingLoading] = useState<string | null>(null);

  // Create mapping form
  const [mappingForm, setMappingForm] = useState<{ connectionId: string; roomTypeId: string; roomCode: string; ratePlanCode: string } | null>(null);
  const [mappingCreating, setMappingCreating] = useState(false);
  const [mappingError, setMappingError] = useState<string | null>(null);

  // Bookings log
  const [showBookings, setShowBookings] = useState(false);
  const [bookings, setBookings] = useState<ChannelReservationDto[]>([]);
  const [bookingsLoading, setBookingsLoading] = useState(false);

  useEffect(() => {
    if (activeProperty) load();
  }, [activeProperty]);

  const load = async () => {
    if (!activeProperty) return;
    setLoading(true);
    setError(null);
    try {
      const data = await apiFetch<ChannelConnectionDto[]>(
        `/api/v1/properties/${activeProperty.id}/channel-connections`,
        token, orgId
      );
      setConnections(data);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('channelManager.loadError'));
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeProperty) return;
    setCreating(true);
    setCreateError(null);
    try {
      await apiFetch(
        `/api/v1/properties/${activeProperty.id}/channel-connections`,
        token, orgId,
        {
          method: 'POST',
          body: JSON.stringify({
            provider: newProvider,
            providerPropertyId: newPropId.trim(),
            apiKey: newApiKey.trim(),
            apiSecret: newApiSecret.trim() || null,
          }),
        }
      );
      setShowCreate(false);
      setNewPropId(''); setNewApiKey(''); setNewApiSecret('');
      await load();
    } catch (e: unknown) {
      setCreateError(e instanceof Error ? e.message : t('channelManager.createError'));
    } finally {
      setCreating(false);
    }
  };

  const handleDeactivate = async (c: ChannelConnectionDto) => {
    if (!activeProperty) return;
    if (!confirm(`${t('channelManager.deleteConnection')} ${c.providerName}?`)) return;
    try {
      await apiFetch(
        `/api/v1/properties/${activeProperty.id}/channel-connections/${c.id}`,
        token, orgId,
        { method: 'DELETE' }
      );
      await load();
    } catch (e: unknown) {
      alert(e instanceof Error ? e.message : t('common.error'));
    }
  };

  const toggleExpand = async (connectionId: string) => {
    if (!activeProperty) return;
    if (expandedId === connectionId) {
      setExpandedId(null);
      return;
    }
    setExpandedId(connectionId);
    if (mappings[connectionId]) return;
    setMappingLoading(connectionId);
    try {
      const data = await apiFetch<ChannelRateMappingDto[]>(
        `/api/v1/properties/${activeProperty.id}/channel-connections/${connectionId}/rate-mappings`,
        token, orgId
      );
      setMappings(prev => ({ ...prev, [connectionId]: data }));
    } catch {
      // keep empty
    } finally {
      setMappingLoading(null);
    }
  };

  const handleCreateMapping = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeProperty || !mappingForm) return;
    setMappingCreating(true);
    setMappingError(null);
    try {
      await apiFetch(
        `/api/v1/properties/${activeProperty.id}/channel-connections/${mappingForm.connectionId}/rate-mappings`,
        token, orgId,
        {
          method: 'POST',
          body: JSON.stringify({
            roomTypeId: mappingForm.roomTypeId,
            channelRoomTypeCode: mappingForm.roomCode.trim(),
            channelRatePlanCode: mappingForm.ratePlanCode.trim(),
          }),
        }
      );
      // Refresh mappings for this connection
      const data = await apiFetch<ChannelRateMappingDto[]>(
        `/api/v1/properties/${activeProperty.id}/channel-connections/${mappingForm.connectionId}/rate-mappings`,
        token, orgId
      );
      setMappings(prev => ({ ...prev, [mappingForm.connectionId]: data }));
      setMappingForm(null);
    } catch (e: unknown) {
      setMappingError(e instanceof Error ? e.message : t('channelManager.createMappingError'));
    } finally {
      setMappingCreating(false);
    }
  };

  const handleDeleteMapping = async (connectionId: string, mappingId: string) => {
    if (!activeProperty) return;
    if (!confirm(t('channelManager.deleteMappingBtn') + '?')) return;
    try {
      await apiFetch(
        `/api/v1/properties/${activeProperty.id}/channel-connections/${connectionId}/rate-mappings/${mappingId}`,
        token, orgId,
        { method: 'DELETE' }
      );
      setMappings(prev => ({
        ...prev,
        [connectionId]: (prev[connectionId] ?? []).filter(m => m.id !== mappingId),
      }));
    } catch (e: unknown) {
      alert(e instanceof Error ? e.message : t('common.error'));
    }
  };

  const loadBookings = async () => {
    if (!activeProperty) return;
    setBookingsLoading(true);
    try {
      const res = await apiFetch<{ data: ChannelReservationDto[] }>(
        `/api/v1/properties/${activeProperty.id}/channel-reservations`,
        token, orgId
      );
      setBookings(res.data);
    } catch {
      setBookings([]);
    } finally {
      setBookingsLoading(false);
    }
  };

  const toggleBookings = () => {
    if (!showBookings) loadBookings();
    setShowBookings(v => !v);
  };

  const getStatusLabel = (status: string) => {
    switch (status) {
      case 'Pending':   return t('channelManager.statusPending');
      case 'Created':   return t('channelManager.statusCreated');
      case 'Duplicate': return t('channelManager.statusDuplicate');
      case 'Failed':    return t('channelManager.statusFailed');
      default:          return status;
    }
  };

  if (!activeProperty) {
    return (
      <div className="bg-white rounded-2xl border border-slate-200 p-10 text-center text-slate-400 text-sm">
        {t('property.selectPrompt')}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between flex-wrap gap-3">
        <div>
          <h3 className="text-base font-black text-slate-900 flex items-center gap-2">
            <Cable className="w-4 h-4 text-violet-500" />
            {t('channelManager.title')}
          </h3>
          <p className="text-xs text-slate-500 mt-0.5">
            {t('channelManager.noConnectionsHint')}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={toggleBookings}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold text-indigo-600 border border-indigo-200 rounded-xl hover:bg-indigo-50"
          >
            <BookOpen className="w-3.5 h-3.5" />
            {showBookings ? t('common.close') : t('channelManager.inboundBookings')}
          </button>
          <button
            onClick={() => setShowCreate(v => !v)}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold bg-violet-600 text-white rounded-xl hover:bg-violet-700"
          >
            <Plus className="w-3.5 h-3.5" />
            {t('channelManager.addConnection')}
          </button>
        </div>
      </div>

      {/* Create connection form */}
      {showCreate && (
        <form onSubmit={handleCreate} className="bg-violet-50 border border-violet-200 rounded-2xl p-5 space-y-4">
          <h4 className="text-sm font-bold text-violet-800">{t('channelManager.createTitle')}</h4>
          {createError && (
            <div className="flex items-center gap-2 text-xs text-rose-700 bg-rose-50 border border-rose-200 rounded-xl p-3">
              <AlertCircle className="w-4 h-4 shrink-0" />{createError}
            </div>
          )}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label className="text-xs font-semibold text-slate-600 block mb-1">{t('channelManager.providerLabel')}</label>
              <select
                value={newProvider}
                onChange={e => setNewProvider(Number(e.target.value))}
                className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-violet-500"
              >
                {PROVIDERS.map(p => (
                  <option key={p.value} value={p.value}>{p.label} — {p.description}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="text-xs font-semibold text-slate-600 block mb-1">{t('channelManager.propertyIdLabel')}</label>
              <input
                required
                value={newPropId}
                onChange={e => setNewPropId(e.target.value)}
                placeholder={t('channelManager.propertyIdPlaceholder')}
                className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-violet-500"
              />
            </div>
            <div>
              <label className="text-xs font-semibold text-slate-600 block mb-1">{t('channelManager.apiKeyLabel')}</label>
              <input
                required
                type="password"
                value={newApiKey}
                onChange={e => setNewApiKey(e.target.value)}
                placeholder={t('channelManager.apiKeyPlaceholder')}
                className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-violet-500"
              />
            </div>
            <div>
              <label className="text-xs font-semibold text-slate-600 block mb-1">API Secret</label>
              <input
                type="password"
                value={newApiSecret}
                onChange={e => setNewApiSecret(e.target.value)}
                placeholder="—"
                className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-violet-500"
              />
            </div>
          </div>
          <div className="flex gap-2 pt-1">
            <button
              type="submit"
              disabled={creating}
              className="px-4 py-2 text-xs font-bold bg-violet-600 text-white rounded-xl hover:bg-violet-700 disabled:opacity-50"
            >
              {creating ? t('common.saving') : t('common.save')}
            </button>
            <button
              type="button"
              onClick={() => setShowCreate(false)}
              className="px-4 py-2 text-xs font-bold text-slate-600 border border-slate-300 rounded-xl hover:bg-slate-50"
            >
              {t('common.cancel')}
            </button>
          </div>
        </form>
      )}

      {/* Connection list */}
      {loading ? (
        <div className="bg-white rounded-2xl border border-slate-200 p-10 text-center text-slate-400 text-sm">
          <RefreshCw className="w-5 h-5 animate-spin mx-auto mb-2" />
          {t('common.loading')}
        </div>
      ) : error ? (
        <div className="flex items-center gap-2 text-sm text-rose-700 bg-rose-50 border border-rose-200 rounded-2xl p-5">
          <AlertCircle className="w-4 h-4 shrink-0" />{error}
        </div>
      ) : connections.length === 0 ? (
        <div className="bg-white rounded-2xl border border-dashed border-slate-300 p-10 text-center space-y-2">
          <Cable className="w-8 h-8 text-slate-300 mx-auto" />
          <p className="text-sm font-semibold text-slate-500">{t('channelManager.noConnections')}</p>
          <p className="text-xs text-slate-400">{t('channelManager.noConnectionsHint')}</p>
        </div>
      ) : (
        <div className="space-y-3">
          {connections.map(c => (
            <div key={c.id} className="bg-white rounded-2xl border border-slate-200 overflow-hidden">
              {/* Connection header */}
              <div className="flex items-center gap-3 p-4">
                <button
                  onClick={() => toggleExpand(c.id)}
                  className="text-slate-400 hover:text-slate-600"
                >
                  {expandedId === c.id
                    ? <ChevronDown className="w-4 h-4" />
                    : <ChevronRight className="w-4 h-4" />}
                </button>
                <div className={`w-2 h-2 rounded-full ${c.isActive ? 'bg-emerald-400' : 'bg-slate-300'}`} />
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-bold text-slate-900">{c.providerName}</span>
                    <span className={`text-xs px-2 py-0.5 rounded-full font-semibold ${c.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-500'}`}>
                      {c.isActive ? t('channelManager.connectionActive') : t('channelManager.connectionInactive')}
                    </span>
                  </div>
                  <div className="text-xs text-slate-500 mt-0.5">
                    Property ID: <span className="font-mono">{c.providerPropertyId}</span>
                    {c.lastSyncAtUtc && (
                      <span className="ml-3 inline-flex items-center gap-1">
                        <Clock className="w-3 h-3" />
                        {t('channelManager.lastSync')}: {fmtDate(c.lastSyncAtUtc)}
                        {c.lastSyncStatus && (
                          <span className={`ml-1 ${c.lastSyncStatus === 'Success' ? 'text-emerald-600' : 'text-rose-600'}`}>
                            ({c.lastSyncStatus})
                          </span>
                        )}
                      </span>
                    )}
                  </div>
                </div>
                <button
                  onClick={() => handleDeactivate(c)}
                  disabled={!c.isActive}
                  title={t('channelManager.deleteConnection')}
                  className="p-1.5 text-slate-400 hover:text-rose-500 disabled:opacity-30"
                >
                  {c.isActive ? <Link2Off className="w-4 h-4" /> : <Link2 className="w-4 h-4" />}
                </button>
              </div>

              {/* Rate mappings panel */}
              {expandedId === c.id && (
                <div className="border-t border-slate-100 bg-slate-50 p-4 space-y-3">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-bold text-slate-700">{t('channelManager.rateMappings')}</span>
                    <button
                      onClick={() => setMappingForm({ connectionId: c.id, roomTypeId: roomTypes[0]?.id ?? '', roomCode: '', ratePlanCode: '' })}
                      className="flex items-center gap-1 text-xs font-semibold text-violet-600 hover:text-violet-800"
                    >
                      <Plus className="w-3.5 h-3.5" />{t('channelManager.addMapping')}
                    </button>
                  </div>

                  {mappingLoading === c.id ? (
                    <p className="text-xs text-slate-400">{t('common.loading')}</p>
                  ) : (mappings[c.id] ?? []).length === 0 ? (
                    <p className="text-xs text-slate-400">{t('channelManager.noMappings')}</p>
                  ) : (
                    <div className="rounded-xl overflow-hidden border border-slate-200 bg-white">
                      <table className="w-full text-xs">
                        <thead>
                          <tr className="bg-slate-100 text-slate-600">
                            <th className="text-left px-3 py-2 font-semibold">{t('channelManager.roomTypeLabel')}</th>
                            <th className="text-left px-3 py-2 font-semibold">{t('channelManager.channelRoomCode')}</th>
                            <th className="text-left px-3 py-2 font-semibold">{t('channelManager.channelRatePlan')}</th>
                            <th className="px-3 py-2" />
                          </tr>
                        </thead>
                        <tbody>
                          {(mappings[c.id] ?? []).map(m => {
                            const rt = roomTypes.find(r => r.id === m.roomTypeId);
                            return (
                              <tr key={m.id} className="border-t border-slate-100 hover:bg-slate-50">
                                <td className="px-3 py-2 font-medium text-slate-800">{rt?.name ?? m.roomTypeId}</td>
                                <td className="px-3 py-2 font-mono text-slate-600">{m.channelRoomTypeCode}</td>
                                <td className="px-3 py-2 font-mono text-slate-600">{m.channelRatePlanCode}</td>
                                <td className="px-3 py-2 text-right">
                                  <button
                                    onClick={() => handleDeleteMapping(c.id, m.id)}
                                    className="text-slate-400 hover:text-rose-500"
                                  >
                                    <Trash2 className="w-3.5 h-3.5" />
                                  </button>
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    </div>
                  )}

                  {/* Inline mapping form */}
                  {mappingForm?.connectionId === c.id && (
                    <form onSubmit={handleCreateMapping} className="bg-white border border-violet-200 rounded-xl p-3 space-y-3">
                      <p className="text-xs font-bold text-violet-700">{t('channelManager.addMapping')}</p>
                      {mappingError && (
                        <p className="text-xs text-rose-600">{mappingError}</p>
                      )}
                      <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
                        <div>
                          <label className="text-xs text-slate-500 mb-0.5 block">{t('channelManager.roomTypeLabel')}</label>
                          <select
                            required
                            value={mappingForm.roomTypeId}
                            onChange={e => setMappingForm(f => f ? { ...f, roomTypeId: e.target.value } : f)}
                            className="w-full px-2 py-1.5 border border-slate-300 rounded-lg text-xs focus:ring-2 focus:ring-violet-500"
                          >
                            {roomTypes.map(rt => (
                              <option key={rt.id} value={rt.id}>{rt.name}</option>
                            ))}
                          </select>
                        </div>
                        <div>
                          <label className="text-xs text-slate-500 mb-0.5 block">{t('channelManager.channelRoomCode')}</label>
                          <input
                            required
                            value={mappingForm.roomCode}
                            onChange={e => setMappingForm(f => f ? { ...f, roomCode: e.target.value } : f)}
                            placeholder="e.g. STD_DBL"
                            className="w-full px-2 py-1.5 border border-slate-300 rounded-lg text-xs focus:ring-2 focus:ring-violet-500"
                          />
                        </div>
                        <div>
                          <label className="text-xs text-slate-500 mb-0.5 block">{t('channelManager.channelRatePlan')}</label>
                          <input
                            required
                            value={mappingForm.ratePlanCode}
                            onChange={e => setMappingForm(f => f ? { ...f, ratePlanCode: e.target.value } : f)}
                            placeholder="e.g. BAR_RATE"
                            className="w-full px-2 py-1.5 border border-slate-300 rounded-lg text-xs focus:ring-2 focus:ring-violet-500"
                          />
                        </div>
                      </div>
                      <div className="flex gap-2">
                        <button
                          type="submit"
                          disabled={mappingCreating}
                          className="px-3 py-1.5 text-xs font-bold bg-violet-600 text-white rounded-lg hover:bg-violet-700 disabled:opacity-50"
                        >
                          {mappingCreating ? t('common.saving') : t('common.save')}
                        </button>
                        <button
                          type="button"
                          onClick={() => setMappingForm(null)}
                          className="px-3 py-1.5 text-xs text-slate-600 border border-slate-300 rounded-lg hover:bg-slate-50"
                        >
                          {t('common.cancel')}
                        </button>
                      </div>
                    </form>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Inbound booking log */}
      {showBookings && (
        <div className="bg-white rounded-2xl border border-slate-200 overflow-hidden">
          <div className="flex items-center justify-between px-4 py-3 bg-indigo-50 border-b border-indigo-100">
            <span className="text-xs font-bold text-indigo-800 flex items-center gap-2">
              <BookOpen className="w-4 h-4" />
              {t('channelManager.inboundTitle')}
            </span>
            <button onClick={loadBookings} className="text-xs text-indigo-600 hover:underline flex items-center gap-1">
              <RefreshCw className="w-3.5 h-3.5" />{t('common.refresh')}
            </button>
          </div>
          {bookingsLoading ? (
            <div className="p-6 text-center text-slate-400 text-sm">
              <RefreshCw className="w-4 h-4 animate-spin mx-auto mb-1" />{t('common.loading')}
            </div>
          ) : bookings.length === 0 ? (
            <div className="p-8 text-center text-sm text-slate-400">{t('channelManager.noInbound')}</div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-xs">
                <thead>
                  <tr className="bg-slate-50 text-slate-600 border-b border-slate-200">
                    <th className="text-left px-4 py-2.5 font-semibold">{t('channelManager.colRef')}</th>
                    <th className="text-left px-4 py-2.5 font-semibold">{t('channelManager.colSource')}</th>
                    <th className="text-left px-4 py-2.5 font-semibold">{t('channelManager.colStatus')}</th>
                    <th className="text-left px-4 py-2.5 font-semibold">{t('channelManager.colReceived')}</th>
                    <th className="text-left px-4 py-2.5 font-semibold">{t('channelManager.colProcessed')}</th>
                    <th className="text-left px-4 py-2.5 font-semibold">{t('common.notes')}</th>
                  </tr>
                </thead>
                <tbody>
                  {bookings.map(b => (
                    <tr key={b.id} className="border-b border-slate-100 hover:bg-slate-50">
                      <td className="px-4 py-2.5 font-mono text-slate-700">{b.channelBookingReference}</td>
                      <td className="px-4 py-2.5 font-semibold text-slate-800">{b.otaSource}</td>
                      <td className="px-4 py-2.5">
                        <span className={`px-2 py-0.5 rounded-full text-xs font-semibold ${STATUS_BADGE[b.processingStatus] ?? 'bg-slate-100 text-slate-500'}`}>
                          {getStatusLabel(b.processingStatus)}
                        </span>
                      </td>
                      <td className="px-4 py-2.5 text-slate-500">{fmtDate(b.receivedAtUtc)}</td>
                      <td className="px-4 py-2.5 text-slate-500">{fmtDate(b.processedAtUtc)}</td>
                      <td className="px-4 py-2.5 text-slate-500 max-w-xs truncate">
                        {b.failureReason ?? (b.reservationId ? (
                          <span className="text-emerald-600 flex items-center gap-1">
                            <CheckCircle2 className="w-3 h-3" />
                            {t('channelManager.statusCreated')}
                          </span>
                        ) : '—')}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
