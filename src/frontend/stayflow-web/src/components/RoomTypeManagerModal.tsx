import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { X, Tag, Plus, CheckCircle2, AlertCircle, Save } from 'lucide-react';
import { RoomTypeDto } from '../types';
import { fetchWithAuth } from '../lib/api';

interface RoomTypeManagerModalProps {
  propertyId: string;
  roomTypes: RoomTypeDto[];
  onClose: () => void;
  onRefresh: () => void;
}

export const RoomTypeManagerModal: React.FC<RoomTypeManagerModalProps> = ({
  propertyId,
  roomTypes,
  onClose,
  onRefresh
}) => {
  const { t, i18n } = useTranslation();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [editCode, setEditCode] = useState('');
  const [editRate, setEditRate] = useState<number>(0);
  const [editStandardOcc, setEditStandardOcc] = useState<number>(2);
  const [editMaxOcc, setEditMaxOcc] = useState<number>(2);
  const [editDesc, setEditDesc] = useState('');

  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [newCode, setNewCode] = useState('');
  const [newRate, setNewRate] = useState<number>(1500);
  const [newDesc, setNewDesc] = useState('');

  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const startEdit = (rt: RoomTypeDto) => {
    setEditingId(rt.id);
    setEditName(rt.name);
    setEditCode(rt.code);
    setEditRate(rt.baseRate);
    setEditStandardOcc(rt.standardOccupancy || 2);
    setEditMaxOcc(rt.maxOccupancy || 2);
    setEditDesc(rt.description || '');
  };

  const handleSaveEdit = async (id: string) => {
    setSaving(true);
    setError(null);
    try {
      const res = await fetchWithAuth(`/api/v1/room-types/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: editName,
          code: editCode,
          baseRate: editRate,
          standardOccupancy: editStandardOcc,
          maxOccupancy: editMaxOcc,
          maxAdults: editMaxOcc,
          maxChildren: 1,
          description: editDesc,
          amenitiesJson: null
        })
      });

      if (!res.ok) {
        throw new Error(t('common.error'));
      }

      setMessage(`${t('common.success')}!`);
      setEditingId(null);
      onRefresh();
    } catch (err: any) {
      setError(err.message);
    } finally {
      setSaving(false);
    }
  };

  const handleCreateNew = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const res = await fetchWithAuth(`/api/v1/properties/${propertyId}/room-types`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: newName,
          code: newCode.toUpperCase(),
          baseRate: newRate,
          standardOccupancy: 2,
          maxOccupancy: 3,
          maxAdults: 2,
          maxChildren: 1,
          description: newDesc,
          amenitiesJson: null
        })
      });

      if (!res.ok) {
        throw new Error(t('common.error'));
      }

      setMessage(`${t('common.create')} ${t('reservations.roomType')} - ${t('common.success')}!`);
      setShowCreate(false);
      setNewName('');
      setNewCode('');
      setNewRate(1500);
      setNewDesc('');
      onRefresh();
    } catch (err: any) {
      setError(err.message);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm">
      <div className="bg-white rounded-3xl shadow-2xl max-w-2xl w-full border border-slate-200 overflow-hidden flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="p-6 border-b border-slate-100 flex items-center justify-between bg-slate-50/50">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-indigo-600 text-white flex items-center justify-center shadow-md">
              <Tag className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-bold text-slate-900">{t('reservations.roomType')} & Rates</h3>
              <p className="text-xs text-slate-500">Set base rates per night/month and room type details</p>
            </div>
          </div>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600 p-1">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 space-y-4 overflow-y-auto flex-1">
          {message && (
            <div className="p-3 bg-emerald-50 border border-emerald-200 text-emerald-800 rounded-xl text-xs font-bold flex items-center justify-between">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-emerald-600" />
                <span>{message}</span>
              </div>
              <button onClick={() => setMessage(null)} className="text-emerald-500">✕</button>
            </div>
          )}

          {error && (
            <div className="p-3 bg-rose-50 border border-rose-200 text-rose-800 rounded-xl text-xs font-bold flex items-center justify-between">
              <div className="flex items-center gap-2">
                <AlertCircle className="w-4 h-4 text-rose-600" />
                <span>{error}</span>
              </div>
              <button onClick={() => setError(null)} className="text-rose-500">✕</button>
            </div>
          )}

          <div className="flex justify-between items-center">
            <span className="text-xs font-bold text-slate-700 uppercase tracking-wider">
              {t('reservations.roomType')} ({roomTypes.length})
            </span>
            <button
              onClick={() => setShowCreate(!showCreate)}
              className="px-3 py-1.5 bg-indigo-50 hover:bg-indigo-100 text-indigo-700 border border-indigo-200 rounded-xl text-xs font-bold flex items-center gap-1.5 transition"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>{showCreate ? t('common.close') : `${t('common.add')} ${t('reservations.roomType')}`}</span>
            </button>
          </div>

          {/* Create Form */}
          {showCreate && (
            <form onSubmit={handleCreateNew} className="p-4 bg-indigo-50/50 border border-indigo-100 rounded-2xl space-y-3">
              <span className="text-xs font-bold text-indigo-900 block">{t('common.create')} {t('reservations.roomType')}:</span>
              <div className="grid grid-cols-3 gap-2">
                <input
                  type="text"
                  required
                  placeholder="Room type name (e.g. Deluxe King)"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  className="col-span-2 px-3 py-2 border border-slate-300 rounded-xl text-xs bg-white"
                />
                <input
                  type="text"
                  required
                  placeholder="Code (DLX)"
                  value={newCode}
                  onChange={(e) => setNewCode(e.target.value)}
                  className="px-3 py-2 border border-slate-300 rounded-xl text-xs uppercase font-bold bg-white"
                />
              </div>
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="text-[10px] text-slate-500 block mb-0.5">Base Rate ({t('common.baht')}) *</label>
                  <input
                    type="number"
                    required
                    min={0}
                    value={newRate}
                    onChange={(e) => setNewRate(Number(e.target.value))}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs font-bold text-emerald-700 bg-white"
                  />
                </div>
                <div>
                  <label className="text-[10px] text-slate-500 block mb-0.5">{t('common.description')}</label>
                  <input
                    type="text"
                    placeholder="Short description..."
                    value={newDesc}
                    onChange={(e) => setNewDesc(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs bg-white"
                  />
                </div>
              </div>
              <div className="flex justify-end gap-2 pt-1">
                <button
                  type="submit"
                  disabled={saving}
                  className="px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl text-xs font-bold shadow-sm"
                >
                  {saving ? t('common.saving') : t('common.save')}
                </button>
              </div>
            </form>
          )}

          {/* List of room types */}
          <div className="space-y-3">
            {roomTypes.map((rt) => {
              const isEditing = editingId === rt.id;

              return (
                <div
                  key={rt.id}
                  className={`p-4 rounded-2xl border transition ${
                    isEditing ? 'border-indigo-400 bg-indigo-50/20 shadow-sm' : 'border-slate-200 bg-white hover:border-slate-300'
                  }`}
                >
                  {isEditing ? (
                    <div className="space-y-3">
                      <div className="grid grid-cols-3 gap-2">
                        <div className="col-span-2">
                          <label className="text-[10px] text-slate-500 block mb-1">{t('reservations.roomType')} {t('common.name')}</label>
                          <input
                            type="text"
                            value={editName}
                            onChange={(e) => setEditName(e.target.value)}
                            className="w-full px-3 py-1.5 border border-slate-300 rounded-xl text-xs font-bold text-slate-900"
                          />
                        </div>
                        <div>
                          <label className="text-[10px] text-slate-500 block mb-1">Code</label>
                          <input
                            type="text"
                            value={editCode}
                            onChange={(e) => setEditCode(e.target.value)}
                            className="w-full px-3 py-1.5 border border-slate-300 rounded-xl text-xs uppercase font-bold text-slate-900"
                          />
                        </div>
                      </div>

                      <div className="grid grid-cols-2 gap-2">
                        <div>
                          <label className="text-[10px] text-slate-500 block mb-1 font-bold text-emerald-700">
                            Base Rate ({t('common.baht')}) *
                          </label>
                          <input
                            type="number"
                            min={0}
                            value={editRate}
                            onChange={(e) => setEditRate(Number(e.target.value))}
                            className="w-full px-3 py-1.5 border border-emerald-400 rounded-xl text-xs font-black text-emerald-700"
                          />
                        </div>
                        <div>
                          <label className="text-[10px] text-slate-500 block mb-1">Capacity (guests)</label>
                          <input
                            type="number"
                            min={1}
                            value={editMaxOcc}
                            onChange={(e) => setEditMaxOcc(Number(e.target.value))}
                            className="w-full px-3 py-1.5 border border-slate-300 rounded-xl text-xs font-bold text-slate-900"
                          />
                        </div>
                      </div>

                      <div>
                        <label className="text-[10px] text-slate-500 block mb-1">{t('common.description')}</label>
                        <input
                          type="text"
                          value={editDesc}
                          onChange={(e) => setEditDesc(e.target.value)}
                          className="w-full px-3 py-1.5 border border-slate-300 rounded-xl text-xs"
                        />
                      </div>

                      <div className="flex justify-end gap-2 pt-2">
                        <button
                          type="button"
                          onClick={() => setEditingId(null)}
                          className="px-3 py-1.5 text-xs font-semibold text-slate-600 bg-slate-100 rounded-xl hover:bg-slate-200"
                        >
                          {t('common.cancel')}
                        </button>
                        <button
                          type="button"
                          disabled={saving}
                          onClick={() => handleSaveEdit(rt.id)}
                          className="px-4 py-1.5 bg-emerald-600 hover:bg-emerald-700 text-white rounded-xl text-xs font-bold shadow-sm flex items-center gap-1.5"
                        >
                          <Save className="w-3.5 h-3.5" />
                          <span>{saving ? t('common.saving') : t('common.save')}</span>
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="flex items-center justify-between">
                      <div className="space-y-0.5">
                        <div className="flex items-center gap-2">
                          <span className="font-bold text-slate-900 text-sm">{rt.name}</span>
                          <span className="px-2 py-0.5 rounded-md bg-slate-100 text-slate-600 text-[10px] font-bold">
                            {rt.code}
                          </span>
                        </div>
                        <p className="text-xs text-slate-400">
                          {rt.description || t('common.noData')} • Max {rt.maxOccupancy}
                        </p>
                      </div>

                      <div className="flex items-center gap-4">
                        <div className="text-right">
                          <span className="text-[10px] text-slate-400 block uppercase font-semibold">Rate</span>
                          <span className="text-base font-black text-emerald-600">
                            ฿{rt.baseRate.toLocaleString(i18n.language)}
                          </span>
                        </div>
                        <button
                          onClick={() => startEdit(rt)}
                          className="px-3 py-1.5 bg-slate-50 hover:bg-slate-100 text-slate-700 border border-slate-200 rounded-xl text-xs font-bold transition"
                        >
                          {t('common.edit')}
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>

        {/* Footer */}
        <div className="p-4 border-t border-slate-100 bg-slate-50/50 flex justify-end">
          <button
            onClick={onClose}
            className="px-5 py-2 text-xs font-bold text-slate-600 hover:text-slate-800 bg-white border border-slate-200 rounded-xl"
          >
            {t('common.close')}
          </button>
        </div>
      </div>
    </div>
  );
};
