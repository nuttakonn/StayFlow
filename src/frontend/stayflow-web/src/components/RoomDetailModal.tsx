import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
  X,
  Clock,
  Sparkles,
  AlertTriangle,
  ShieldCheck,
  Wrench,
  Ban,
  Send,
  History,
  Edit3,
  CheckCircle2,
  AlertCircle,
  Save
} from 'lucide-react';
import {
  RoomDto,
  RoomOperationalStatus,
  RoomStatusHistoryDto,
  RoomTypeDto,
  BuildingDto,
  FloorDto
} from '../types';
import { fetchWithAuth } from '../lib/api';

interface RoomDetailModalProps {
  room: RoomDto | null;
  roomTypes?: RoomTypeDto[];
  buildings?: BuildingDto[];
  floors?: FloorDto[];
  onClose: () => void;
  onStatusChange: (roomId: string, newStatus: RoomOperationalStatus, notes: string) => Promise<void>;
  onRoomUpdated?: () => void;
}

export const RoomDetailModal: React.FC<RoomDetailModalProps> = ({
  room,
  roomTypes = [],
  buildings = [],
  floors = [],
  onClose,
  onStatusChange,
  onRoomUpdated
}) => {
  const { t, i18n } = useTranslation();
  const [modalTab, setModalTab] = useState<'status' | 'edit'>('status');

  // Status change state
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [histories, setHistories] = useState<RoomStatusHistoryDto[]>([]);
  const [loadingHistory, setLoadingHistory] = useState(false);

  // Edit room details state
  const [editRoomNumber, setEditRoomNumber] = useState('');
  const [editDisplayName, setEditDisplayName] = useState('');
  const [editRoomTypeId, setEditRoomTypeId] = useState('');
  const [editBuildingId, setEditBuildingId] = useState<string>('');
  const [editFloorId, setEditFloorId] = useState<string>('');
  const [editCapacity, setEditCapacity] = useState<number>(2);
  const [editNotes, setEditNotes] = useState('');
  const [savingEdit, setSavingEdit] = useState(false);
  const [editSuccessMsg, setEditSuccessMsg] = useState<string | null>(null);
  const [editErrorMsg, setEditErrorMsg] = useState<string | null>(null);

  useEffect(() => {
    if (!room) return;
    setNotes('');
    setEditSuccessMsg(null);
    setEditErrorMsg(null);

    // Populate edit fields
    setEditRoomNumber(room.roomNumber);
    setEditDisplayName(room.displayName || '');
    setEditRoomTypeId(room.roomTypeId);
    setEditBuildingId(room.buildingId || '');
    setEditFloorId(room.floorId || '');
    setEditCapacity(room.capacity || 2);
    setEditNotes(room.notes || '');

    setLoadingHistory(true);
    fetchWithAuth(`/api/v1/rooms/${room.id}/history`)
      .then((res) => (res.ok ? res.json() : []))
      .then((data: RoomStatusHistoryDto[]) => {
        setHistories(data);
        setLoadingHistory(false);
      })
      .catch(() => setLoadingHistory(false));
  }, [room]);

  if (!room) return null;

  const handleApplyStatus = async (status: RoomOperationalStatus) => {
    try {
      setSubmitting(true);
      await onStatusChange(room.id, status, notes);
      setNotes('');
      const res = await fetchWithAuth(`/api/v1/rooms/${room.id}/history`);
      if (res.ok) {
        const data = await res.json();
        setHistories(data);
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleSaveRoomDetails = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editRoomNumber.trim()) {
      setEditErrorMsg(`${t('common.required')}: Room Number`);
      return;
    }
    setSavingEdit(true);
    setEditErrorMsg(null);
    setEditSuccessMsg(null);

    try {
      const res = await fetchWithAuth(`/api/v1/rooms/${room.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          roomTypeId: editRoomTypeId || room.roomTypeId,
          roomNumber: editRoomNumber.trim().toUpperCase(),
          buildingId: editBuildingId || null,
          floorId: editFloorId || null,
          displayName: editDisplayName.trim() || null,
          capacity: editCapacity,
          notes: editNotes.trim() || null
        })
      });

      if (!res.ok) {
        const errJson = await res.json();
        throw new Error(errJson.detail || errJson.message || t('common.error'));
      }

      setEditSuccessMsg(`${t('common.success')}!`);
      onRoomUpdated?.();
    } catch (err: any) {
      setEditErrorMsg(err.message);
    } finally {
      setSavingEdit(false);
    }
  };

  const statusConfig: Record<string, { color: string; bg: string; border: string }> = {
    Clean: { color: 'text-emerald-700', bg: 'bg-emerald-50', border: 'border-emerald-300' },
    Dirty: { color: 'text-rose-700', bg: 'bg-rose-50', border: 'border-rose-300' },
    Cleaning: { color: 'text-sky-700', bg: 'bg-sky-50', border: 'border-sky-300' },
    Inspected: { color: 'text-indigo-700', bg: 'bg-indigo-50', border: 'border-indigo-300' },
    Maintenance: { color: 'text-amber-700', bg: 'bg-amber-50', border: 'border-amber-300' },
    OutOfService: { color: 'text-slate-700', bg: 'bg-slate-100', border: 'border-slate-300' },
  };

  const currentConfig = statusConfig[room.operationalStatus] || statusConfig.Clean;
  const currentStatusLabel = t(`operationalStatus.${room.operationalStatus}`, { defaultValue: room.operationalStatus });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm t-modal-overlay">
      <div 
        className="bg-white rounded-3xl shadow-2xl max-w-xl w-full border border-slate-200 overflow-hidden t-modal-content flex flex-col max-h-[90vh]"
        role="dialog"
      >
        {/* Header */}
        <div className="p-6 border-b border-slate-100 flex items-center justify-between bg-slate-50/50">
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-2xl bg-blue-600 text-white flex items-center justify-center text-xl font-black tracking-tight shadow-md">
              {room.roomNumber}
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h3 className="text-xl font-bold text-slate-900">{room.displayName || `Room ${room.roomNumber}`}</h3>
                <span className={`px-2.5 py-0.5 rounded-full text-xs font-semibold border ${currentConfig.bg} ${currentConfig.color} ${currentConfig.border}`}>
                  {currentStatusLabel}
                </span>
              </div>
              <p className="text-xs text-slate-500 mt-0.5">
                {room.roomTypeName} • ฿{room.baseRate.toLocaleString()}{t('common.perNight')} • {room.buildingName || 'No building'} ({room.floorName || 'No floor'})
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="p-2 text-slate-400 hover:text-slate-600 rounded-lg hover:bg-slate-100 t-button"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Modal Sub-Tabs */}
        <div className="flex border-b border-slate-200 px-6 bg-slate-50/30 gap-2">
          <button
            onClick={() => setModalTab('status')}
            className={`py-3 px-4 text-xs font-bold border-b-2 transition flex items-center gap-1.5 ${
              modalTab === 'status'
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-slate-500 hover:text-slate-700'
            }`}
          >
            <Sparkles className="w-4 h-4" />
            <span>{t('common.status')} & Audit</span>
          </button>
          <button
            onClick={() => setModalTab('edit')}
            className={`py-3 px-4 text-xs font-bold border-b-2 transition flex items-center gap-1.5 ${
              modalTab === 'edit'
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-slate-500 hover:text-slate-700'
            }`}
          >
            <Edit3 className="w-4 h-4" />
            <span>✏️ {t('common.edit')} Details</span>
          </button>
        </div>

        {/* Content */}
        <div className="p-6 space-y-6 overflow-y-auto flex-1">
          {/* ── SubTab 1: Status & Timeline ─────────────────────────────────── */}
          {modalTab === 'status' && (
            <div className="space-y-6">
              {/* Quick Actions Bar */}
              <div className="space-y-3">
                <label className="text-xs font-bold text-slate-700 uppercase tracking-wider block">
                  Quick Actions
                </label>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
                  <button
                    type="button"
                    disabled={submitting}
                    onClick={() => handleApplyStatus(RoomOperationalStatus.Clean)}
                    className="flex items-center justify-center gap-2 p-3 rounded-xl border border-emerald-300 bg-emerald-50 text-emerald-800 font-semibold text-xs hover:bg-emerald-100 t-button shadow-sm"
                  >
                    <Sparkles className="w-4 h-4 text-emerald-600" />
                    <span>{t('operationalStatus.Clean')}</span>
                  </button>

                  <button
                    type="button"
                    disabled={submitting}
                    onClick={() => handleApplyStatus(RoomOperationalStatus.Dirty)}
                    className="flex items-center justify-center gap-2 p-3 rounded-xl border border-rose-300 bg-rose-50 text-rose-800 font-semibold text-xs hover:bg-rose-100 t-button shadow-sm"
                  >
                    <AlertTriangle className="w-4 h-4 text-rose-600" />
                    <span>{t('operationalStatus.Dirty')}</span>
                  </button>

                  <button
                    type="button"
                    disabled={submitting}
                    onClick={() => handleApplyStatus(RoomOperationalStatus.Cleaning)}
                    className="flex items-center justify-center gap-2 p-3 rounded-xl border border-sky-300 bg-sky-50 text-sky-800 font-semibold text-xs hover:bg-sky-100 t-button shadow-sm"
                  >
                    <Clock className="w-4 h-4 text-sky-600" />
                    <span>{t('operationalStatus.Cleaning')}</span>
                  </button>

                  <button
                    type="button"
                    disabled={submitting}
                    onClick={() => handleApplyStatus(RoomOperationalStatus.Inspected)}
                    className="flex items-center justify-center gap-2 p-3 rounded-xl border border-indigo-300 bg-indigo-50 text-indigo-800 font-semibold text-xs hover:bg-indigo-100 t-button shadow-sm"
                  >
                    <ShieldCheck className="w-4 h-4 text-indigo-600" />
                    <span>{t('operationalStatus.Inspected')}</span>
                  </button>

                  <button
                    type="button"
                    disabled={submitting}
                    onClick={() => handleApplyStatus(RoomOperationalStatus.Maintenance)}
                    className="flex items-center justify-center gap-2 p-3 rounded-xl border border-amber-300 bg-amber-50 text-amber-800 font-semibold text-xs hover:bg-amber-100 t-button shadow-sm"
                  >
                    <Wrench className="w-4 h-4 text-amber-600" />
                    <span>{t('operationalStatus.Maintenance')}</span>
                  </button>

                  <button
                    type="button"
                    disabled={submitting}
                    onClick={() => handleApplyStatus(RoomOperationalStatus.OutOfService)}
                    className="flex items-center justify-center gap-2 p-3 rounded-xl border border-slate-300 bg-slate-100 text-slate-800 font-semibold text-xs hover:bg-slate-200 t-button shadow-sm"
                  >
                    <Ban className="w-4 h-4 text-slate-600" />
                    <span>{t('operationalStatus.OutOfService')}</span>
                  </button>
                </div>
              </div>

              {/* Notes input */}
              <div className="space-y-1.5">
                <label className="text-xs font-medium text-slate-600 block">
                  Status Change Notes (e.g. missing towels, AC cleaned)
                </label>
                <div className="flex gap-2">
                  <input
                    type="text"
                    placeholder="Short note..."
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                    className="flex-1 px-3.5 py-2 border border-slate-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                  />
                  <span className="text-xs text-slate-400 self-center">
                    <Send className="w-4 h-4 text-slate-300 inline" /> Saved automatically on click
                  </span>
                </div>
              </div>

              {/* Status History Timeline */}
              <div className="space-y-3 pt-2">
                <h4 className="text-xs font-bold text-slate-700 uppercase tracking-wider flex items-center gap-1.5">
                  <History className="w-4 h-4 text-blue-600" />
                  Audit History
                </h4>

                {loadingHistory && (
                  <p className="text-xs text-slate-400 py-2">{t('common.loading')}</p>
                )}

                {!loadingHistory && histories.length === 0 && (
                  <p className="text-xs text-slate-400 py-3 bg-slate-50 text-center rounded-lg border border-dashed border-slate-200">
                    {t('common.noData')}
                  </p>
                )}

                {!loadingHistory && histories.length > 0 && (
                  <div className="space-y-2 max-h-48 overflow-y-auto pr-1">
                    {histories.map((h) => (
                      <div key={h.id} className="p-3 bg-slate-50 border border-slate-100 rounded-xl text-xs space-y-1">
                        <div className="flex items-center justify-between text-slate-500">
                          <span className="font-semibold text-slate-800">
                            {h.fromStatus} &rarr; <span className="text-blue-600 font-bold">{h.toStatus}</span>
                          </span>
                          <span>{new Date(h.createdAtUtc).toLocaleString(i18n.language)}</span>
                        </div>
                        {h.notes && (
                          <p className="text-slate-600 italic bg-white p-1.5 rounded border border-slate-100">
                            "{h.notes}"
                          </p>
                        )}
                        {h.changedByUserEmail && (
                          <p className="text-[10px] text-slate-400">by: {h.changedByUserEmail}</p>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          )}

          {/* ── SubTab 2: Edit Room Details Form ─────────────────────────────── */}
          {modalTab === 'edit' && (
            <form onSubmit={handleSaveRoomDetails} className="space-y-4">
              {editSuccessMsg && (
                <div className="p-3 bg-emerald-50 border border-emerald-200 text-emerald-800 rounded-xl text-xs font-bold flex items-center gap-2">
                  <CheckCircle2 className="w-4 h-4 text-emerald-600" />
                  <span>{editSuccessMsg}</span>
                </div>
              )}

              {editErrorMsg && (
                <div className="p-3 bg-rose-50 border border-rose-200 text-rose-800 rounded-xl text-xs font-bold flex items-center gap-2">
                  <AlertCircle className="w-4 h-4 text-rose-600" />
                  <span>{editErrorMsg}</span>
                </div>
              )}

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-bold text-slate-700 block mb-1">Room Number *</label>
                  <input
                    type="text"
                    required
                    value={editRoomNumber}
                    onChange={(e) => setEditRoomNumber(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm font-bold text-slate-900 uppercase"
                  />
                </div>
                <div>
                  <label className="text-xs font-bold text-slate-700 block mb-1">Display Name</label>
                  <input
                    type="text"
                    placeholder="e.g. Deluxe King 101"
                    value={editDisplayName}
                    onChange={(e) => setEditDisplayName(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm"
                  />
                </div>
              </div>

              <div>
                <label className="text-xs font-bold text-slate-700 block mb-1">{t('reservations.roomType')} *</label>
                <select
                  required
                  value={editRoomTypeId}
                  onChange={(e) => setEditRoomTypeId(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm bg-white font-bold text-slate-800"
                >
                  {roomTypes.map((rt) => (
                    <option key={rt.id} value={rt.id}>
                      {rt.name} ({rt.code}) - ฿{rt.baseRate.toLocaleString()}
                    </option>
                  ))}
                </select>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-bold text-slate-700 block mb-1">Building</label>
                  <select
                    value={editBuildingId}
                    onChange={(e) => setEditBuildingId(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm bg-white"
                  >
                    <option value="">-- No building --</option>
                    {buildings.map((b) => (
                      <option key={b.id} value={b.id}>
                        {b.name} ({b.code})
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="text-xs font-bold text-slate-700 block mb-1">Floor</label>
                  <select
                    value={editFloorId}
                    onChange={(e) => setEditFloorId(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm bg-white"
                  >
                    <option value="">-- No floor --</option>
                    {floors.map((f) => (
                      <option key={f.id} value={f.id}>
                        {f.name} (Floor {f.floorNumber})
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              <div>
                <label className="text-xs font-bold text-slate-700 block mb-1">Capacity</label>
                <input
                  type="number"
                  min={1}
                  max={20}
                  value={editCapacity}
                  onChange={(e) => setEditCapacity(Number(e.target.value))}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm font-bold"
                />
              </div>

              <div>
                <label className="text-xs font-bold text-slate-700 block mb-1">{t('common.notes')}</label>
                <textarea
                  rows={2}
                  placeholder="e.g. Recently renovated, pool view, near elevator..."
                  value={editNotes}
                  onChange={(e) => setEditNotes(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm"
                />
              </div>

              <div className="flex justify-end pt-2">
                <button
                  type="submit"
                  disabled={savingEdit}
                  className="px-5 py-2.5 bg-blue-600 hover:bg-blue-700 text-white rounded-xl text-xs font-bold shadow-md transition flex items-center gap-1.5"
                >
                  <Save className="w-4 h-4" />
                  <span>{savingEdit ? t('common.saving') : t('common.save')}</span>
                </button>
              </div>
            </form>
          )}
        </div>

        {/* Footer */}
        <div className="p-4 border-t border-slate-100 bg-slate-50/50 flex justify-end">
          <button
            onClick={onClose}
            className="px-5 py-2 text-xs font-bold text-slate-600 hover:text-slate-800 bg-white border border-slate-200 rounded-xl hover:bg-slate-50 t-button shadow-sm"
          >
            {t('common.close')}
          </button>
        </div>
      </div>
    </div>
  );
};
