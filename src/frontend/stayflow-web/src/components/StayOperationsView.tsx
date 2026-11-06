import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import type { PropertyDto, StayOperationItemDto, OperationsDashboardSummaryDto, RoomDto, AvailabilityRoomTypeDto, NightAuditResultDto, ReservationDto } from '../types';
import { apiFetch } from '../lib/api';
import { WalkInWizard } from './WalkInWizard';

interface Props {
  property: PropertyDto;
  token: string;
  orgId: string;
  onRefreshRooms?: () => void;
}

export function StayOperationsView({ property, token, orgId, onRefreshRooms }: Props) {
  const { t } = useTranslation();
  const [activeSubTab, setActiveSubTab] = useState<'arrivals' | 'departures' | 'inhouse' | 'housekeeping'>('arrivals');
  const [summary, setSummary] = useState<OperationsDashboardSummaryDto | null>(null);
  const [arrivals, setArrivals] = useState<StayOperationItemDto[]>([]);
  const [departures, setDepartures] = useState<StayOperationItemDto[]>([]);
  const [inHouse, setInHouse] = useState<StayOperationItemDto[]>([]);
  const [rooms, setRooms] = useState<RoomDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');

  // Room move modal state
  const [movingStay, setMovingStay] = useState<StayOperationItemDto | null>(null);
  const [targetRoomId, setTargetRoomId] = useState('');
  const [moveReason, setMoveReason] = useState('');

  // Check-out folio balance warning
  const [checkoutWarning, setCheckoutWarning] = useState<{ reservationId: string; balance: number; currency: string } | null>(null);

  // Night audit
  const [showNightAudit, setShowNightAudit] = useState(false);
  const [auditDate, setAuditDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [auditPreview, setAuditPreview] = useState<NightAuditResultDto | null>(null);
  const [auditResult, setAuditResult] = useState<NightAuditResultDto | null>(null);
  const [auditLoading, setAuditLoading] = useState(false);
  const [auditError, setAuditError] = useState('');

  // Walk-in wizard
  const [showWalkIn, setShowWalkIn] = useState(false);

  // Inline room assignment for arrivals with no room
  const [assigningRoomFor, setAssigningRoomFor] = useState<string | null>(null);
  const [assignAvailability, setAssignAvailability] = useState<AvailabilityRoomTypeDto[]>([]);
  const [loadingAssign, setLoadingAssign] = useState(false);
  const [selectedAssignRoom, setSelectedAssignRoom] = useState('');

  const fetchOperationsData = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [sumRes, arrRes, depRes, inhRes, roomRes] = await Promise.all([
        apiFetch<OperationsDashboardSummaryDto>(`/api/v1/properties/${property.id}/operations/dashboard`, token, orgId),
        apiFetch<StayOperationItemDto[]>(`/api/v1/properties/${property.id}/operations/arrivals`, token, orgId),
        apiFetch<StayOperationItemDto[]>(`/api/v1/properties/${property.id}/operations/departures`, token, orgId),
        apiFetch<StayOperationItemDto[]>(`/api/v1/properties/${property.id}/operations/in-house`, token, orgId),
        apiFetch<any>(`/api/v1/properties/${property.id}/rooms?isActive=true`, token, orgId),
      ]);

      setSummary(sumRes);
      setArrivals(arrRes ?? []);
      setDepartures(depRes ?? []);
      setInHouse(inhRes ?? []);
      const roomList: RoomDto[] = Array.isArray(roomRes) ? roomRes : (roomRes?.data ?? []);
      setRooms(roomList);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('operations.loadError'));
    } finally {
      setLoading(false);
    }
  }, [property.id, token, orgId, t]);

  useEffect(() => {
    fetchOperationsData();
  }, [fetchOperationsData]);

  const handleCheckIn = async (reservationId: string) => {
    setActionLoading(true);
    setError('');
    try {
      await apiFetch(`/api/v1/reservations/${reservationId}/check-in`, token, orgId, { method: 'POST' });
      setSuccessMsg(t('operations.checkInSuccess'));
      setTimeout(() => setSuccessMsg(''), 3000);
      await fetchOperationsData();
      onRefreshRooms?.();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('operations.checkInError'));
    } finally {
      setActionLoading(false);
    }
  };

  const handleCheckOut = async (reservationId: string, skipFolioCheck = false) => {
    setActionLoading(true);
    setError('');
    setCheckoutWarning(null);
    try {
      // Check for outstanding folio balance before checkout
      if (!skipFolioCheck) {
        try {
          const folios = await apiFetch<{ status: string; balance: number; currency: string }[]>(
            `/api/v1/properties/${property.id}/folios?reservationId=${reservationId}&status=Open`,
            token, orgId
          );
          const openWithBalance = (folios ?? []).filter(f => f.balance > 0);
          if (openWithBalance.length > 0) {
            const totalBalance = openWithBalance.reduce((s, f) => s + f.balance, 0);
            setCheckoutWarning({ reservationId, balance: totalBalance, currency: openWithBalance[0].currency });
            setActionLoading(false);
            return;
          }
        } catch {
          // folio check failed — proceed with checkout anyway
        }
      }

      await apiFetch(`/api/v1/reservations/${reservationId}/check-out`, token, orgId, { method: 'POST' });
      setSuccessMsg(t('operations.checkOutSuccess'));
      setTimeout(() => setSuccessMsg(''), 3000);
      await fetchOperationsData();
      onRefreshRooms?.();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('operations.checkOutError'));
    } finally {
      setActionLoading(false);
    }
  };

  const handleRoomMove = async () => {
    if (!movingStay || !targetRoomId || !moveReason.trim()) return;
    setActionLoading(true);
    setError('');
    try {
      await apiFetch(`/api/v1/properties/${property.id}/operations/reservations/${movingStay.reservationId}/room-move`, token, orgId, {
        method: 'POST',
        body: JSON.stringify({
          newRoomId: targetRoomId,
          reason: moveReason.trim()
        })
      });
      setSuccessMsg(t('operations.moveSuccess'));
      setTimeout(() => setSuccessMsg(''), 3000);
      setMovingStay(null);
      setTargetRoomId('');
      setMoveReason('');
      await fetchOperationsData();
      onRefreshRooms?.();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('operations.moveError'));
    } finally {
      setActionLoading(false);
    }
  };

  const handleUpdateRoomStatus = async (roomId: string, newStatus: string) => {
    setActionLoading(true);
    setError('');

    const statusMap: Record<string, number> = {
      Clean: 1,
      Dirty: 2,
      Cleaning: 3,
      Inspected: 4,
      OutOfService: 5,
      Maintenance: 6,
    };

    try {
      await apiFetch(`/api/v1/rooms/${roomId}/status`, token, orgId, {
        method: 'PUT',
        body: JSON.stringify({
          newStatus: statusMap[newStatus] ?? newStatus,
          notes: 'Updated via housekeeping board'
        })
      });
      await fetchOperationsData();
      onRefreshRooms?.();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('common.error'));
    } finally {
      setActionLoading(false);
    }
  };

  const openRoomAssign = async (item: StayOperationItemDto) => {
    setAssigningRoomFor(item.reservationId);
    setSelectedAssignRoom('');
    setLoadingAssign(true);
    try {
      const data = await apiFetch<AvailabilityRoomTypeDto[]>(
        `/api/v1/properties/${property.id}/availability?checkInDate=${item.checkInDate}&checkOutDate=${item.checkOutDate}`,
        token, orgId
      );
      setAssignAvailability(data ?? []);
    } catch { setAssignAvailability([]); }
    finally { setLoadingAssign(false); }
  };

  const handleAssignRoom = async (reservationId: string) => {
    if (!selectedAssignRoom) return;
    setActionLoading(true);
    setError('');
    try {
      await apiFetch(`/api/v1/reservations/${reservationId}/assign-room`, token, orgId, {
        method: 'POST',
        body: JSON.stringify({ roomId: selectedAssignRoom }),
      });
      setAssigningRoomFor(null);
      setSelectedAssignRoom('');
      await fetchOperationsData();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('operations.assignError'));
    } finally {
      setActionLoading(false);
    }
  };

  const handleAuditPreview = async () => {
    setAuditLoading(true);
    setAuditError('');
    setAuditPreview(null);
    setAuditResult(null);
    try {
      const data = await apiFetch<NightAuditResultDto>(
        `/api/v1/properties/${property.id}/operations/night-audit/preview?businessDate=${auditDate}`,
        token, orgId
      );
      setAuditPreview(data);
    } catch (e: unknown) {
      setAuditError(e instanceof Error ? e.message : t('operations.nightAuditError'));
    } finally {
      setAuditLoading(false);
    }
  };

  const handleRunAudit = async () => {
    setAuditLoading(true);
    setAuditError('');
    try {
      const data = await apiFetch<NightAuditResultDto>(
        `/api/v1/properties/${property.id}/operations/night-audit`,
        token, orgId,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ businessDate: auditDate }) }
      );
      setAuditResult(data);
      setAuditPreview(null);
    } catch (e: unknown) {
      setAuditError(e instanceof Error ? e.message : t('operations.nightAuditError'));
    } finally {
      setAuditLoading(false);
    }
  };

  // Rooms available for moving
  const availableRoomsForMove = rooms.filter(r =>
    r.id !== movingStay?.roomId &&
    r.isActive &&
    r.operationalStatus !== 'OutOfService' &&
    r.operationalStatus !== 'Maintenance' &&
    !inHouse.some(ih => ih.roomId === r.id)
  );

  return (
    <div className="space-y-6">
      {/* Night Audit Panel */}
      <div className="bg-indigo-50 border border-indigo-200 rounded-xl p-4">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="font-semibold text-indigo-900 text-sm">{t('operations.nightAudit')}</h3>
            <p className="text-xs text-indigo-600 mt-0.5">{t('operations.nightAuditDesc')}</p>
          </div>
          <button
            onClick={() => { setShowNightAudit(v => !v); setAuditPreview(null); setAuditResult(null); setAuditError(''); }}
            className="text-sm text-indigo-700 border border-indigo-300 bg-white px-3 py-1.5 rounded hover:bg-indigo-50"
          >
            {showNightAudit ? t('common.close') : t('operations.runNightAudit')}
          </button>
        </div>

        {showNightAudit && (
          <div className="mt-4 space-y-3">
            <div className="flex items-end gap-2">
              <div>
                <label className="text-xs text-indigo-700 block mb-1">Business Date</label>
                <input
                  type="date"
                  value={auditDate}
                  onChange={e => { setAuditDate(e.target.value); setAuditPreview(null); setAuditResult(null); }}
                  className="border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400"
                />
              </div>
              <button
                onClick={handleAuditPreview}
                disabled={auditLoading}
                className="bg-indigo-600 text-white px-4 py-1.5 rounded text-sm hover:bg-indigo-700 disabled:opacity-50"
              >
                {auditLoading && !auditPreview ? t('common.loading') : 'Preview'}
              </button>
            </div>

            {auditError && <p className="text-red-600 text-sm">{auditError}</p>}

            {auditPreview && !auditResult && (
              <div className="bg-white border border-indigo-200 rounded p-3 space-y-2">
                <p className="text-sm font-medium text-gray-800">
                  {auditPreview.lines.length} rooms — post {auditPreview.postedCount}, skip {auditPreview.skippedCount} (already posted)
                </p>
                <div className="max-h-40 overflow-y-auto divide-y text-xs">
                  {auditPreview.lines.map(l => (
                    <div key={l.reservationId} className="flex items-center justify-between py-1.5">
                      <span className="text-gray-700">{l.guestName}</span>
                      <div className="flex items-center gap-2">
                        <span className="text-gray-500">{l.roomRate.toLocaleString('th-TH')} {l.currency}</span>
                        <span className={`px-1.5 py-0.5 rounded-full font-medium ${l.result === 'posted' ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
                          {l.result === 'posted' ? 'Post' : 'Skip'}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>
                {auditPreview.postedCount > 0 && (
                  <div className="flex items-center justify-between pt-2 border-t">
                    <span className="text-sm font-medium text-gray-700">{t('common.total')}</span>
                    <span className="font-bold text-indigo-700">
                      {auditPreview.totalPosted.toLocaleString('th-TH')} {auditPreview.currency}
                    </span>
                  </div>
                )}
                <div className="flex gap-2 pt-1">
                  <button
                    onClick={handleRunAudit}
                    disabled={auditLoading || auditPreview.postedCount === 0}
                    className="bg-indigo-600 text-white px-4 py-1.5 rounded text-sm hover:bg-indigo-700 disabled:opacity-50"
                  >
                    {auditLoading ? t('operations.runningAudit') : `${t('common.confirm')} Night Audit (${auditPreview.postedCount})`}
                  </button>
                  <button onClick={() => setAuditPreview(null)} className="border px-3 py-1.5 rounded text-sm hover:bg-gray-50">
                    {t('common.cancel')}
                  </button>
                </div>
              </div>
            )}

            {auditResult && (
              <div className="bg-green-50 border border-green-200 rounded p-3 space-y-2">
                <p className="text-sm font-semibold text-green-800">
                  {t('operations.nightAuditSuccess', { count: auditResult.postedCount })}
                </p>
                <p className="text-sm text-green-700 font-medium">
                  {t('common.total')}: {auditResult.totalPosted.toLocaleString('th-TH')} {auditResult.currency}
                </p>
                <button
                  onClick={() => { setAuditResult(null); setShowNightAudit(false); }}
                  className="text-xs text-green-700 hover:underline"
                >
                  {t('common.close')}
                </button>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Operations Quick Counters Dashboard */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <div
          onClick={() => setActiveSubTab('arrivals')}
          className={`p-4 rounded-2xl border cursor-pointer transition-all t-card ${
            activeSubTab === 'arrivals'
              ? 'bg-blue-600 text-white border-blue-600 shadow-md ring-2 ring-blue-300'
              : 'bg-white text-slate-800 border-slate-200 hover:border-blue-300'
          }`}
        >
          <div className="flex items-center justify-between mb-1">
            <span className="text-2xl">🛬</span>
            <span className={`text-2xl font-black ${activeSubTab === 'arrivals' ? 'text-white' : 'text-blue-600'}`}>
              {summary?.expectedArrivals ?? 0}
            </span>
          </div>
          <div className="font-bold text-sm">{t('operations.arrivals')}</div>
          <div className={`text-xs mt-0.5 ${activeSubTab === 'arrivals' ? 'text-blue-100' : 'text-slate-400'}`}>
            {t('operations.checkIn')} {summary?.completedArrivals ?? 0}
          </div>
        </div>

        <div
          onClick={() => setActiveSubTab('departures')}
          className={`p-4 rounded-2xl border cursor-pointer transition-all t-card ${
            activeSubTab === 'departures'
              ? 'bg-amber-600 text-white border-amber-600 shadow-md ring-2 ring-amber-300'
              : 'bg-white text-slate-800 border-slate-200 hover:border-amber-300'
          }`}
        >
          <div className="flex items-center justify-between mb-1">
            <span className="text-2xl">🛫</span>
            <span className={`text-2xl font-black ${activeSubTab === 'departures' ? 'text-white' : 'text-amber-600'}`}>
              {summary?.expectedDepartures ?? 0}
            </span>
          </div>
          <div className="font-bold text-sm">{t('operations.departures')}</div>
          <div className={`text-xs mt-0.5 ${activeSubTab === 'departures' ? 'text-amber-100' : 'text-slate-400'}`}>
            {t('operations.checkOut')} {summary?.completedDepartures ?? 0}
          </div>
        </div>

        <div
          onClick={() => setActiveSubTab('inhouse')}
          className={`p-4 rounded-2xl border cursor-pointer transition-all t-card ${
            activeSubTab === 'inhouse'
              ? 'bg-indigo-600 text-white border-indigo-600 shadow-md ring-2 ring-indigo-300'
              : 'bg-white text-slate-800 border-slate-200 hover:border-indigo-300'
          }`}
        >
          <div className="flex items-center justify-between mb-1">
            <span className="text-2xl">🏨</span>
            <span className={`text-2xl font-black ${activeSubTab === 'inhouse' ? 'text-white' : 'text-indigo-600'}`}>
              {summary?.inHouseGuests ?? 0}
            </span>
          </div>
          <div className="font-bold text-sm">{t('operations.inHouse')}</div>
          <div className={`text-xs mt-0.5 ${activeSubTab === 'inhouse' ? 'text-indigo-100' : 'text-slate-400'}`}>
            {t('operations.moveRoom')}
          </div>
        </div>

        <div
          onClick={() => setActiveSubTab('housekeeping')}
          className={`p-4 rounded-2xl border cursor-pointer transition-all t-card ${
            activeSubTab === 'housekeeping'
              ? 'bg-emerald-600 text-white border-emerald-600 shadow-md ring-2 ring-emerald-300'
              : 'bg-white text-slate-800 border-slate-200 hover:border-emerald-300'
          }`}
        >
          <div className="flex items-center justify-between mb-1">
            <span className="text-2xl">🧹</span>
            <span className={`text-2xl font-black ${activeSubTab === 'housekeeping' ? 'text-white' : 'text-emerald-600'}`}>
              {(summary?.dirtyRooms ?? 0) + (summary?.cleaningRooms ?? 0)}
            </span>
          </div>
          <div className="font-bold text-sm">{t('operations.housekeeping')}</div>
          <div className={`text-xs mt-0.5 ${activeSubTab === 'housekeeping' ? 'text-emerald-100' : 'text-slate-400'}`}>
            {t('operationalStatus.Dirty')} {summary?.dirtyRooms ?? 0} • {t('operationalStatus.Cleaning')} {summary?.cleaningRooms ?? 0}
          </div>
        </div>
      </div>

      {/* Notifications */}
      {error && (
        <div className="bg-rose-50 border border-rose-200 text-rose-700 px-4 py-3 rounded-2xl text-sm flex items-center justify-between">
          <span>⚠️ {error}</span>
          <button onClick={() => setError('')} className="text-rose-400 hover:text-rose-600">✕</button>
        </div>
      )}
      {successMsg && (
        <div className="bg-emerald-50 border border-emerald-200 text-emerald-800 px-4 py-3 rounded-2xl text-sm flex items-center justify-between">
          <span>✅ {successMsg}</span>
          <button onClick={() => setSuccessMsg('')} className="text-emerald-400 hover:text-emerald-600">✕</button>
        </div>
      )}

      {/* Check-out folio balance warning */}
      {checkoutWarning && (
        <div className="bg-amber-50 border-2 border-amber-300 rounded-2xl p-4 space-y-3">
          <div className="flex items-start gap-3">
            <span className="text-2xl shrink-0">💳</span>
            <div>
              <div className="font-bold text-amber-900 text-sm">{t('operations.folioBalance')}</div>
              <div className="text-xs text-amber-700 mt-0.5">
                {t('operations.balanceWarning', { amount: checkoutWarning.balance.toLocaleString(), currency: checkoutWarning.currency })}
              </div>
            </div>
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            <button
              onClick={() => setCheckoutWarning(null)}
              className="px-3 py-1.5 text-xs font-bold bg-white border border-amber-300 text-amber-800 rounded-xl hover:bg-amber-50"
            >
              {t('common.back')}
            </button>
            <button
              disabled={actionLoading}
              onClick={() => handleCheckOut(checkoutWarning.reservationId, true)}
              className="px-3 py-1.5 text-xs font-bold bg-rose-600 text-white rounded-xl hover:bg-rose-700 disabled:opacity-40"
            >
              {actionLoading ? t('common.saving') : t('operations.checkOut')}
            </button>
          </div>
        </div>
      )}

      {/* Section Content */}
      <div className="bg-white rounded-3xl border border-slate-200 p-6 shadow-sm space-y-4">
        {/* Header toolbar */}
        <div className="flex flex-wrap items-center justify-between gap-3 pb-3 border-b border-slate-100">
          <div className="flex items-center gap-2">
            <span className="text-xl">
              {activeSubTab === 'arrivals' && '🛬'}
              {activeSubTab === 'departures' && '🛫'}
              {activeSubTab === 'inhouse' && '🏨'}
              {activeSubTab === 'housekeeping' && '🧹'}
            </span>
            <h3 className="font-bold text-slate-900 text-base">
              {activeSubTab === 'arrivals' && t('operations.arrivals')}
              {activeSubTab === 'departures' && t('operations.departures')}
              {activeSubTab === 'inhouse' && t('operations.inHouse')}
              {activeSubTab === 'housekeeping' && t('operations.housekeeping')}
            </h3>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => setShowWalkIn(true)}
              className="px-3 py-1.5 text-xs font-semibold text-white bg-green-600 hover:bg-green-700 rounded-xl t-button flex items-center gap-1.5"
            >
              🚶 Walk-in Check-in
            </button>
            <button
              onClick={fetchOperationsData}
              disabled={loading}
              className="px-3 py-1.5 text-xs font-semibold text-slate-600 bg-slate-100 hover:bg-slate-200 rounded-xl t-button flex items-center gap-1.5"
            >
              <span>{loading ? t('common.loading') : `🔄 ${t('common.refresh')}`}</span>
            </button>
          </div>
        </div>

        {/* Tab 1: Arrivals List */}
        {activeSubTab === 'arrivals' && (
          <div className="space-y-3">
            {arrivals.length === 0 ? (
              <div className="text-center py-12 text-slate-400">
                <div className="text-4xl mb-2">🛬</div>
                <div className="font-medium text-sm">{t('operations.noArrivals')}</div>
              </div>
            ) : (
              arrivals.map(item => (
                <div key={item.reservationId} className="border border-slate-200 rounded-2xl p-4 space-y-3 hover:border-blue-300 t-card">
                  {/* Guest info row */}
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                    <div className="space-y-1">
                      <div className="flex items-center gap-2">
                        <span className="font-bold text-slate-900">{item.guestName}</span>
                        <span className={`px-2 py-0.5 text-xs font-bold rounded-full ${
                          item.status === 'CheckedIn' ? 'bg-emerald-100 text-emerald-800' : 'bg-blue-100 text-blue-800'
                        }`}>
                          {item.status === 'CheckedIn' ? t('status.CheckedIn') : t('status.Confirmed')}
                        </span>
                      </div>
                      <div className="text-xs text-slate-500">
                        {t('operations.room')}:{' '}
                        {item.roomNumber
                          ? <span className="font-bold text-indigo-600">{item.roomNumber}</span>
                          : <span className="font-bold text-amber-600">—</span>
                        }
                        {' '}• {item.roomTypeName ?? 'Standard'} • {item.numberOfNights} {t('common.nights')}
                      </div>
                      <div className="text-xs text-slate-400">
                        {t('common.phone')}: {item.guestPhone ?? '-'} • {t('common.total')}: {item.totalAmount.toLocaleString()} {item.currency}
                      </div>
                    </div>

                    <div className="flex items-center gap-2 shrink-0">
                      {item.status !== 'CheckedIn' && !item.roomNumber && assigningRoomFor !== item.reservationId && (
                        <button
                          onClick={() => openRoomAssign(item)}
                          disabled={actionLoading}
                          className="px-4 py-2 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-40 text-white font-bold text-xs rounded-xl t-button"
                        >
                          🔑 {t('operations.assignRoom')}
                        </button>
                      )}
                      {item.status !== 'CheckedIn' && item.roomNumber && (
                        <button
                          onClick={() => handleCheckIn(item.reservationId)}
                          disabled={actionLoading}
                          className="px-4 py-2 bg-emerald-600 hover:bg-emerald-700 disabled:opacity-40 text-white font-bold text-xs rounded-xl t-button shadow-xs"
                        >
                          ✓ {t('operations.checkIn')}
                        </button>
                      )}
                    </div>
                  </div>

                  {/* Inline room picker */}
                  {assigningRoomFor === item.reservationId && (
                    <div className="bg-indigo-50 border border-indigo-200 rounded-xl p-3 space-y-3">
                      <p className="text-sm font-bold text-indigo-800">
                        {t('operations.selectRoom')} {item.checkInDate} → {item.checkOutDate}
                      </p>
                      {loadingAssign ? (
                        <p className="text-xs text-slate-400 text-center py-2">{t('common.loading')}</p>
                      ) : assignAvailability.length === 0 ? (
                        <p className="text-xs text-rose-600 text-center py-2">{t('walkIn.noRoomsAvailable')}</p>
                      ) : (
                        <div className="space-y-2 max-h-40 overflow-y-auto pr-1">
                          {assignAvailability.map(rt => (
                            <div key={rt.roomTypeId}>
                              <p className="text-[10px] font-black text-slate-500 uppercase tracking-wider mb-1">
                                {rt.name} — {rt.availableRooms} {t('roomRack.clean')}
                              </p>
                              <div className="flex flex-wrap gap-1.5">
                                {rt.rooms.map(room => (
                                  <button
                                    key={room.roomId}
                                    onClick={() => setSelectedAssignRoom(room.roomId)}
                                    className={`px-3 py-1.5 text-xs font-bold rounded-lg border transition-all ${
                                      selectedAssignRoom === room.roomId
                                        ? 'bg-indigo-600 text-white border-indigo-600 shadow-sm'
                                        : 'bg-white text-slate-700 border-slate-200 hover:border-indigo-400'
                                    }`}
                                  >
                                    {room.roomNumber}
                                    {room.displayName && <span className="ml-1 opacity-60 text-[10px]">{room.displayName}</span>}
                                  </button>
                                ))}
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                      <div className="flex gap-2 pt-1">
                        <button
                          onClick={() => handleAssignRoom(item.reservationId)}
                          disabled={!selectedAssignRoom || actionLoading}
                          className="flex-1 py-2 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-40 text-white font-bold text-sm rounded-xl t-button"
                        >
                          {actionLoading ? t('common.saving') : `✓ ${t('operations.assignRoom')}`}
                        </button>
                        <button
                          onClick={() => { setAssigningRoomFor(null); setSelectedAssignRoom(''); }}
                          className="px-4 py-2 bg-slate-100 hover:bg-slate-200 text-slate-600 text-sm rounded-xl t-button"
                        >
                          {t('common.cancel')}
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              ))
            )}
          </div>
        )}

        {/* Tab 2: Departures List */}
        {activeSubTab === 'departures' && (
          <div className="space-y-3">
            {departures.length === 0 ? (
              <div className="text-center py-12 text-slate-400">
                <div className="text-4xl mb-2">🛫</div>
                <div className="font-medium text-sm">{t('operations.noDepartures')}</div>
              </div>
            ) : (
              departures.map(item => (
                <div key={item.reservationId} className="border border-slate-200 rounded-2xl p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4 hover:border-amber-300 t-card">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-bold text-slate-900">{item.guestName}</span>
                      <span className={`px-2 py-0.5 text-xs font-bold rounded-full ${
                        item.status === 'CheckedOut' ? 'bg-slate-100 text-slate-600' : 'bg-amber-100 text-amber-800'
                      }`}>
                        {item.status === 'CheckedOut' ? t('status.CheckedOut') : t('status.CheckedIn')}
                      </span>
                    </div>
                    <div className="text-xs text-slate-500">
                      {t('operations.room')}: <span className="font-bold text-slate-800">{item.roomNumber ?? '-'}</span> • {t('operations.checkInDate')}: {item.checkInDate}
                    </div>
                  </div>

                  <div className="flex items-center gap-2">
                    {item.status === 'CheckedIn' && (
                      <button
                        onClick={() => handleCheckOut(item.reservationId)}
                        disabled={actionLoading}
                        className="px-4 py-2 bg-slate-800 hover:bg-slate-900 disabled:opacity-40 text-white font-bold text-xs rounded-xl t-button shadow-xs"
                      >
                        👋 {t('operations.checkOut')}
                      </button>
                    )}
                  </div>
                </div>
              ))
            )}
          </div>
        )}

        {/* Tab 3: In-House Stays with Room Move */}
        {activeSubTab === 'inhouse' && (
          <div className="space-y-3">
            {inHouse.length === 0 ? (
              <div className="text-center py-12 text-slate-400">
                <div className="text-4xl mb-2">🏨</div>
                <div className="font-medium text-sm">{t('operations.noInHouseGuests')}</div>
              </div>
            ) : (
              inHouse.map(item => (
                <div key={item.reservationId} className="border border-slate-200 rounded-2xl p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4 hover:border-indigo-300 t-card">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-bold text-slate-900">{item.guestName}</span>
                      <span className="px-2 py-0.5 text-xs font-bold rounded-full bg-emerald-100 text-emerald-800">
                        {t('status.CheckedIn')}
                      </span>
                    </div>
                    <div className="text-xs text-slate-500">
                      {t('operations.room')}: <span className="font-bold text-indigo-700 text-sm">{item.roomNumber}</span> • {item.roomTypeName} • {t('operations.checkOutDate')}: {item.checkOutDate}
                    </div>
                    <div className="text-xs text-slate-400">
                      {t('common.phone')}: {item.guestPhone ?? '-'} • {item.adults} {t('walkIn.adultsLabel')} {item.children > 0 ? `+ ${item.children} ${t('walkIn.childrenLabel')}` : ''}
                    </div>
                  </div>

                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => setMovingStay(item)}
                      className="px-3.5 py-2 bg-indigo-50 hover:bg-indigo-100 text-indigo-700 font-bold text-xs rounded-xl border border-indigo-200 t-button"
                    >
                      🔄 {t('operations.moveRoom')}
                    </button>
                    <button
                      onClick={() => handleCheckOut(item.reservationId)}
                      disabled={actionLoading}
                      className="px-3.5 py-2 bg-slate-100 hover:bg-slate-200 text-slate-700 font-bold text-xs rounded-xl t-button"
                    >
                      {t('operations.checkOut')}
                    </button>
                  </div>
                </div>
              ))
            )}
          </div>
        )}

        {/* Tab 4: Housekeeping Board */}
        {activeSubTab === 'housekeeping' && (
          <div className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-3">
              {rooms.map(room => (
                <div key={room.id} className="border border-slate-200 rounded-2xl p-4 space-y-3 t-card hover:border-slate-300">
                  <div className="flex items-center justify-between">
                    <div>
                      <div className="font-black text-slate-900 text-lg">{t('operations.room')} {room.roomNumber}</div>
                      <div className="text-xs text-slate-400">{room.roomTypeName}</div>
                    </div>
                    <span className={`px-2.5 py-1 text-xs font-bold rounded-xl ${
                      room.operationalStatus === 'Clean' ? 'bg-emerald-100 text-emerald-800' :
                      room.operationalStatus === 'Dirty' ? 'bg-rose-100 text-rose-800' :
                      room.operationalStatus === 'Cleaning' ? 'bg-amber-100 text-amber-800' :
                      room.operationalStatus === 'Inspected' ? 'bg-blue-100 text-blue-800' :
                      'bg-slate-100 text-slate-700'
                    }`}>
                      {room.operationalStatus === 'Clean' ? `✨ ${t('operationalStatus.Clean')}` :
                       room.operationalStatus === 'Dirty' ? `🧹 ${t('operationalStatus.Dirty')}` :
                       room.operationalStatus === 'Cleaning' ? `🧼 ${t('operationalStatus.Cleaning')}` :
                       room.operationalStatus === 'Inspected' ? `🔍 ${t('operationalStatus.Inspected')}` :
                       room.operationalStatus === 'Maintenance' ? `🔧 ${t('operationalStatus.Maintenance')}` : `⛔ ${t('operationalStatus.OutOfService')}`}
                    </span>
                  </div>

                  <div className="flex flex-wrap gap-1.5 pt-2 border-t border-slate-100">
                    {room.operationalStatus === 'Dirty' && (
                      <button
                        onClick={() => handleUpdateRoomStatus(room.id, 'Cleaning')}
                        disabled={actionLoading}
                        className="flex-1 py-1.5 text-xs font-bold text-amber-700 bg-amber-50 hover:bg-amber-100 rounded-lg t-button"
                      >
                        🧼 {t('housekeeping.startCleaning')}
                      </button>
                    )}
                    {(room.operationalStatus === 'Dirty' || room.operationalStatus === 'Cleaning') && (
                      <button
                        onClick={() => handleUpdateRoomStatus(room.id, 'Clean')}
                        disabled={actionLoading}
                        className="flex-1 py-1.5 text-xs font-bold text-emerald-700 bg-emerald-50 hover:bg-emerald-100 rounded-lg t-button"
                      >
                        ✨ {t('housekeeping.markClean')}
                      </button>
                    )}
                    {room.operationalStatus === 'Clean' && (
                      <button
                        onClick={() => handleUpdateRoomStatus(room.id, 'Inspected')}
                        disabled={actionLoading}
                        className="flex-1 py-1.5 text-xs font-bold text-blue-700 bg-blue-50 hover:bg-blue-100 rounded-lg t-button"
                      >
                        🔍 {t('housekeeping.markInspected')}
                      </button>
                    )}
                    {room.operationalStatus === 'Inspected' && (
                      <button
                        onClick={() => handleUpdateRoomStatus(room.id, 'Dirty')}
                        disabled={actionLoading}
                        className="flex-1 py-1.5 text-xs font-bold text-rose-700 bg-rose-50 hover:bg-rose-100 rounded-lg t-button"
                      >
                        🧹 {t('housekeeping.markDirty')}
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Walk-in Wizard */}
      {showWalkIn && (
        <WalkInWizard
          property={property}
          token={token}
          orgId={orgId}
          onClose={() => setShowWalkIn(false)}
          onSuccess={(res: ReservationDto) => {
            setShowWalkIn(false);
            setSuccessMsg(`Walk-in ${t('operations.checkInSuccess')} — ${t('operations.room')} ${res.roomNumber ?? '—'} ${res.guestName}`);
            setTimeout(() => setSuccessMsg(''), 5000);
            fetchOperationsData();
            onRefreshRooms?.();
          }}
        />
      )}

      {/* Room Move Modal */}
      {movingStay && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm t-modal-overlay">
          <div className="bg-white rounded-3xl shadow-2xl max-w-lg w-full border border-slate-200 overflow-hidden t-modal-content p-6 space-y-4">
            <div className="flex items-center justify-between border-b border-slate-100 pb-3">
              <div>
                <h3 className="text-base font-bold text-slate-900">🔄 {t('operations.moveRoom')}</h3>
                <p className="text-xs text-slate-500">{t('operations.guest')}: {movingStay.guestName} ({t('operations.room')} {movingStay.roomNumber})</p>
              </div>
              <button onClick={() => setMovingStay(null)} className="text-slate-400 hover:text-slate-600 text-lg">✕</button>
            </div>

            <div className="space-y-4">
              <div>
                <label className="text-xs font-bold text-slate-700 block mb-1">{t('operations.selectRoom')} *</label>
                {availableRoomsForMove.length === 0 ? (
                  <div className="p-3 bg-amber-50 border border-amber-200 rounded-xl text-amber-800 text-xs font-semibold">
                    ⚠️ {t('walkIn.noRoomsAvailable')}
                  </div>
                ) : (
                  <select
                    value={targetRoomId}
                    onChange={e => setTargetRoomId(e.target.value)}
                    className="w-full px-3 py-2.5 border border-slate-300 rounded-xl text-sm font-semibold text-slate-800 focus:ring-2 focus:ring-indigo-500 bg-white"
                  >
                    <option value="">{t('operations.selectRoomPrompt')}</option>
                    {availableRoomsForMove.map(r => (
                      <option key={r.id} value={r.id}>
                        {t('operations.room')} {r.roomNumber} ({r.roomTypeName}) - {t('common.status')}: {r.operationalStatus}
                      </option>
                    ))}
                  </select>
                )}
              </div>

              <div>
                <label className="text-xs font-bold text-slate-700 block mb-1">{t('common.remarks')} *</label>
                <textarea
                  rows={3}
                  required
                  placeholder="e.g. AC broken, guest requested view change..."
                  value={moveReason}
                  onChange={e => setMoveReason(e.target.value)}
                  className="w-full px-3 py-2.5 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-indigo-500 resize-none"
                />
              </div>

              <div className="p-3 bg-slate-50 border border-slate-100 rounded-xl text-xs text-slate-500">
                💡 <b>Note:</b> After confirming, the original room ({movingStay.roomNumber}) will be set to <b>Dirty</b> for housekeeping.
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setMovingStay(null)}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 rounded-xl hover:bg-slate-200 t-button"
                >
                  {t('common.cancel')}
                </button>
                <button
                  type="button"
                  disabled={actionLoading || !targetRoomId || !moveReason.trim()}
                  onClick={handleRoomMove}
                  className="px-5 py-2 text-xs font-bold text-white bg-indigo-600 rounded-xl hover:bg-indigo-700 disabled:opacity-40 t-button shadow-md"
                >
                  {actionLoading ? t('common.saving') : `✓ ${t('operations.moveRoom')}`}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
