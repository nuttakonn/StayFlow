import { useState, useCallback, useEffect } from 'react';
import { RoomDto, RoomStatusSummaryDto, PropertyDto, RoomOperationalStatus } from '../types';
import { apiFetch } from '../lib/api';
import { useTranslation } from 'react-i18next';

interface Props {
  property: PropertyDto;
  token: string;
  orgId: string;
}

// Status display config — labelTh removed; use t('operationalStatus.X') at render time
const STATUS_CFG: Record<string, { label: string; color: string; bg: string; badge: string }> = {
  Clean:        { label: 'Clean',          color: 'text-green-700',  bg: 'bg-green-50  border-green-200',  badge: 'bg-green-100  text-green-800'  },
  Dirty:        { label: 'Dirty',          color: 'text-red-700',    bg: 'bg-red-50    border-red-200',    badge: 'bg-red-100    text-red-800'    },
  Cleaning:     { label: 'Cleaning',       color: 'text-yellow-700', bg: 'bg-yellow-50 border-yellow-200', badge: 'bg-yellow-100 text-yellow-800' },
  Inspected:    { label: 'Inspected',      color: 'text-blue-700',   bg: 'bg-blue-50   border-blue-200',   badge: 'bg-blue-100   text-blue-800'   },
  OutOfService: { label: 'Out of Service', color: 'text-orange-700', bg: 'bg-orange-50 border-orange-200', badge: 'bg-orange-100 text-orange-800' },
  Maintenance:  { label: 'Maintenance',    color: 'text-purple-700', bg: 'bg-purple-50 border-purple-200', badge: 'bg-purple-100 text-purple-800' },
};

// Allowed next transitions per current status — labelKey used with t() at render time
const TRANSITIONS: Record<string, { status: RoomOperationalStatus; labelKey: string }[]> = {
  Dirty:        [{ status: RoomOperationalStatus.Cleaning, labelKey: 'housekeeping.startCleaning' }],
  Cleaning:     [{ status: RoomOperationalStatus.Clean, labelKey: 'housekeeping.markClean' }, { status: RoomOperationalStatus.Dirty, labelKey: 'housekeeping.markDirty' }],
  Clean:        [{ status: RoomOperationalStatus.Inspected, labelKey: 'housekeeping.markInspected' }, { status: RoomOperationalStatus.Dirty, labelKey: 'housekeeping.markDirty' }],
  Inspected:    [{ status: RoomOperationalStatus.Dirty, labelKey: 'housekeeping.markDirty' }],
  OutOfService: [{ status: RoomOperationalStatus.Dirty, labelKey: 'housekeeping.markDirty' }],
  Maintenance:  [{ status: RoomOperationalStatus.Dirty, labelKey: 'housekeeping.markDirty' }],
};

const ALL_STATUSES = ['Clean', 'Dirty', 'Cleaning', 'Inspected', 'OutOfService', 'Maintenance'];

export function HousekeepingView({ property, token, orgId }: Props) {
  const { t } = useTranslation();
  const [rooms, setRooms] = useState<RoomDto[]>([]);
  const [summary, setSummary] = useState<RoomStatusSummaryDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [filterStatus, setFilterStatus] = useState<string>(''); // '' = all
  const [updatingId, setUpdatingId] = useState<string | null>(null);
  const [updateError, setUpdateError] = useState('');
  const [staleDataBanner, setStaleDataBanner] = useState(false);

  // Notes modal state — labelKey stored instead of labelTh
  const [noteModal, setNoteModal] = useState<{
    roomId: string; roomNumber: string; newStatus: RoomOperationalStatus; labelKey: string;
  } | null>(null);
  const [noteText, setNoteText] = useState('');

  const fetchData = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const params = new URLSearchParams();
      if (filterStatus) params.set('status', String(RoomOperationalStatus[filterStatus as keyof typeof RoomOperationalStatus]));

      const [roomsData, summaryData] = await Promise.all([
        apiFetch<RoomDto[]>(`/api/v1/properties/${property.id}/rooms?${params}`, token, orgId),
        apiFetch<RoomStatusSummaryDto>(`/api/v1/properties/${property.id}/rooms/summary`, token, orgId),
      ]);

      setRooms(roomsData);
      setSummary(summaryData);
    } catch {
      setError(t('housekeeping.loadError'));
    } finally {
      setLoading(false);
    }
  }, [property.id, token, orgId, filterStatus]);

  useEffect(() => { fetchData(); }, [fetchData]);

  const handleTransition = (room: RoomDto, newStatus: RoomOperationalStatus, labelKey: string) => {
    setNoteModal({ roomId: room.id, roomNumber: room.roomNumber, newStatus, labelKey });
    setNoteText('');
    setUpdateError('');
  };

  const confirmTransition = async () => {
    if (!noteModal) return;
    setUpdatingId(noteModal.roomId);
    setUpdateError('');
    try {
      await apiFetch<RoomDto>(
        `/api/v1/properties/${property.id}/rooms/${noteModal.roomId}/status`,
        token, orgId,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ newStatus: noteModal.newStatus, notes: noteText.trim() || null }),
        }
      );
      setNoteModal(null);
      await fetchData();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : '';
      // 404 means the backend was restarted and InMemory data was lost — refresh the list
      if (msg.includes('404') || msg.includes('Not Found')) {
        setNoteModal(null);
        setStaleDataBanner(true);
        await fetchData();
      } else {
        setUpdateError(msg || t('housekeeping.updateError'));
      }
    } finally {
      setUpdatingId(null);
    }
  };

  const getStatusLabel = (status: string) => {
    switch (status) {
      case 'Clean':        return t('operationalStatus.Clean');
      case 'Dirty':        return t('operationalStatus.Dirty');
      case 'Cleaning':     return t('operationalStatus.Cleaning');
      case 'Inspected':    return t('operationalStatus.Inspected');
      case 'OutOfService': return t('operationalStatus.OutOfService');
      case 'Maintenance':  return t('operationalStatus.Maintenance');
      default:             return status;
    }
  };

  const summaryCount = (key: string): number => {
    if (!summary) return 0;
    const map: Record<string, number> = {
      Clean: summary.cleanCount,
      Dirty: summary.dirtyCount,
      Cleaning: summary.cleaningCount,
      Inspected: summary.inspectedCount,
      OutOfService: summary.outOfServiceCount,
      Maintenance: summary.maintenanceCount,
    };
    return map[key] ?? 0;
  };

  return (
    <div className="space-y-4">
      {/* Summary strip */}
      <div className="grid grid-cols-3 sm:grid-cols-6 gap-2">
        {ALL_STATUSES.map(s => {
          const cfg = STATUS_CFG[s];
          const count = summaryCount(s);
          const active = filterStatus === s;
          return (
            <button
              key={s}
              onClick={() => setFilterStatus(active ? '' : s)}
              className={`rounded-lg border p-2.5 text-center transition-all ${active ? cfg.bg + ' ring-2 ring-offset-1 ring-current ' + cfg.color : 'bg-white border-gray-200 hover:bg-gray-50'}`}
            >
              <div className={`text-2xl font-bold ${active ? cfg.color : 'text-gray-800'}`}>{count}</div>
              <div className={`text-xs mt-0.5 ${active ? cfg.color : 'text-gray-500'}`}>{getStatusLabel(s)}</div>
            </button>
          );
        })}
      </div>

      {/* Filter label + refresh */}
      <div className="flex items-center justify-between">
        <div className="text-sm text-gray-600">
          {filterStatus
            ? <><span className={`font-medium ${STATUS_CFG[filterStatus]?.color}`}>{getStatusLabel(filterStatus)}</span> ({rooms.length})</>
            : <>{rooms.length} {t('common.room')}</>}
        </div>
        <div className="flex items-center gap-3">
          {filterStatus && (
            <button onClick={() => setFilterStatus('')} className="text-xs text-blue-600 hover:underline">
              {t('common.filter')}
            </button>
          )}
          <button
            onClick={() => { setStaleDataBanner(false); fetchData(); }}
            disabled={loading}
            className="text-xs text-gray-500 hover:text-gray-700 border rounded px-2 py-0.5 hover:bg-gray-50 disabled:opacity-40"
          >
            {loading ? '...' : `↻ ${t('common.refresh')}`}
          </button>
        </div>
      </div>

      {staleDataBanner && (
        <div className="bg-amber-50 border border-amber-200 text-amber-800 text-sm px-3 py-2 rounded flex items-center justify-between">
          <span>{t('housekeeping.loadError')}</span>
          <button onClick={() => setStaleDataBanner(false)} className="ml-3 text-amber-600 hover:text-amber-800 font-medium">✕</button>
        </div>
      )}
      {error && <div className="bg-red-50 text-red-700 text-sm px-3 py-2 rounded">{error}</div>}
      {loading && <div className="text-center py-8 text-gray-400">{t('common.loading')}</div>}

      {/* Room grid */}
      {!loading && rooms.length === 0 && !error && (
        <div className="text-center py-12 text-gray-400">{t('housekeeping.noTasks')}</div>
      )}

      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-3">
        {rooms.map(room => {
          const statusKey = room.operationalStatus;
          const cfg = STATUS_CFG[statusKey] ?? STATUS_CFG['Dirty'];
          const transitions = TRANSITIONS[statusKey] ?? [];
          const isUpdating = updatingId === room.id;

          return (
            <div key={room.id} className={`rounded-lg border p-3 flex flex-col gap-2 ${cfg.bg}`}>
              {/* Room header */}
              <div className="flex items-start justify-between gap-1">
                <div>
                  <div className="font-bold text-gray-900 text-lg leading-none">{room.roomNumber}</div>
                  <div className="text-xs text-gray-500 mt-0.5">{room.roomTypeName}</div>
                  {room.floorName && <div className="text-xs text-gray-400">{room.floorName}</div>}
                </div>
                <span className={`flex-shrink-0 text-xs px-1.5 py-0.5 rounded-full font-medium ${cfg.badge}`}>
                  {getStatusLabel(statusKey)}
                </span>
              </div>

              {/* Transition buttons */}
              <div className="flex flex-col gap-1 mt-auto">
                {isUpdating ? (
                  <div className="text-xs text-gray-400 text-center py-1">{t('common.saving')}</div>
                ) : transitions.length > 0 ? (
                  transitions.map(tr => (
                    <button
                      key={tr.status}
                      onClick={() => handleTransition(room, tr.status, tr.labelKey)}
                      className="w-full text-xs py-1 px-2 rounded border border-gray-300 bg-white hover:bg-gray-50 text-gray-700 font-medium transition-colors"
                    >
                      {t(tr.labelKey)}
                    </button>
                  ))
                ) : (
                  <div className="text-xs text-gray-400 text-center py-0.5">—</div>
                )}
              </div>
            </div>
          );
        })}
      </div>

      {/* Notes / confirm modal */}
      {noteModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-xl p-5 w-80 space-y-3">
            <h3 className="font-semibold text-gray-800">
              {t('common.room')} {noteModal.roomNumber} — {t(noteModal.labelKey)}
            </h3>
            <p className="text-sm text-gray-600">
              {t('common.status')}:{' '}
              <span className={`font-medium ${STATUS_CFG[RoomOperationalStatus[noteModal.newStatus]]?.color}`}>
                {getStatusLabel(RoomOperationalStatus[noteModal.newStatus])}
              </span>
            </p>
            <div>
              <label className="text-xs text-gray-500 block mb-1">{t('common.notes')}</label>
              <textarea
                rows={2}
                value={noteText}
                onChange={e => setNoteText(e.target.value)}
                placeholder={t('housekeeping.notePlaceholder')}
                className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            {updateError && <p className="text-red-600 text-sm">{updateError}</p>}
            <div className="flex gap-2">
              <button
                onClick={confirmTransition}
                disabled={updatingId !== null}
                className="flex-1 bg-blue-600 text-white py-2 rounded text-sm hover:bg-blue-700 disabled:opacity-50"
              >
                {updatingId ? t('common.saving') : t('common.confirm')}
              </button>
              <button
                onClick={() => setNoteModal(null)}
                disabled={updatingId !== null}
                className="flex-1 border py-2 rounded text-sm hover:bg-gray-50"
              >
                {t('common.cancel')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
