import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { X, Plus, Sparkles, Building2, BedDouble } from 'lucide-react';
import { BuildingDto, FloorDto, RoomOperationalStatus, RoomTypeDto } from '../types';
import { fetchWithAuth } from '../lib/api';

interface CreateRoomModalProps {
  propertyId: string;
  businessType?: string;
  roomTypes: RoomTypeDto[];
  buildings: BuildingDto[];
  floors: FloorDto[];
  onClose: () => void;
  onCreated: () => void;
}

export const CreateRoomModal: React.FC<CreateRoomModalProps> = ({
  propertyId,
  businessType: _businessType,
  roomTypes,
  buildings,
  floors,
  onClose,
  onCreated,
}) => {
  const { t } = useTranslation();
  const [activeTab, setActiveTab] = useState<'room' | 'roomType' | 'building'>('room');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // New Room State
  const [roomNumber, setRoomNumber] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [selectedRoomTypeId, setSelectedRoomTypeId] = useState(roomTypes[0]?.id || '');
  const [selectedBuildingId, setSelectedBuildingId] = useState(buildings[0]?.id || '');
  const [selectedFloorId, setSelectedFloorId] = useState(floors[0]?.id || '');
  const [capacity, setCapacity] = useState(2);
  const [operationalStatus, setOperationalStatus] = useState<RoomOperationalStatus>(RoomOperationalStatus.Clean);
  const [notes, setNotes] = useState('');

  // New Room Type State
  const [rtName, setRtName] = useState('');
  const [rtCode, setRtCode] = useState('');
  const [rtBaseRate, setRtBaseRate] = useState(1500);

  // New Building State
  const [bldName, setBldName] = useState('');
  const [bldCode, setBldCode] = useState('');
  const [bldFloors, setBldFloors] = useState(3);

  const handleCreateRoom = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!roomNumber.trim()) {
      setError(`${t('common.required')}: Room Number`);
      return;
    }
    if (!selectedRoomTypeId) {
      setError(`${t('common.required')}: ${t('reservations.roomType')}`);
      return;
    }

    try {
      setSubmitting(true);
      setError(null);

      const res = await fetchWithAuth(`/api/v1/properties/${propertyId}/rooms`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          roomTypeId: selectedRoomTypeId,
          roomNumber: roomNumber.trim(),
          buildingId: selectedBuildingId || null,
          floorId: selectedFloorId || null,
          displayName: displayName.trim() || null,
          capacity: Number(capacity),
          operationalStatus: Number(operationalStatus),
          notes: notes.trim() || null,
        }),
      });

      if (!res.ok) {
        const errData = await res.json();
        throw new Error(errData.detail || t('common.error'));
      }

      onCreated();
      onClose();
    } catch (err: any) {
      setError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleCreateRoomType = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!rtName.trim() || !rtCode.trim()) {
      setError(`${t('common.required')}: ${t('common.name')} / Code`);
      return;
    }

    try {
      setSubmitting(true);
      setError(null);

      const res = await fetchWithAuth(`/api/v1/properties/${propertyId}/room-types`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: rtName.trim(),
          code: rtCode.trim().toUpperCase(),
          baseRate: Number(rtBaseRate),
          standardOccupancy: 2,
          maxOccupancy: 3,
          maxAdults: 2,
          maxChildren: 1,
        }),
      });

      if (!res.ok) throw new Error(t('common.error'));

      onCreated();
      setActiveTab('room');
    } catch (err: any) {
      setError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleCreateBuilding = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!bldName.trim() || !bldCode.trim()) {
      setError(`${t('common.required')}: ${t('common.name')} / Code`);
      return;
    }

    try {
      setSubmitting(true);
      setError(null);

      const res = await fetchWithAuth(`/api/v1/properties/${propertyId}/buildings`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: bldName.trim(),
          code: bldCode.trim().toUpperCase(),
          numberOfFloors: Number(bldFloors),
        }),
      });

      if (!res.ok) throw new Error(t('common.error'));

      onCreated();
      setActiveTab('room');
    } catch (err: any) {
      setError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm t-modal-overlay">
      <div className="bg-white rounded-2xl shadow-2xl max-w-lg w-full border border-slate-200 overflow-hidden t-modal-content">
        {/* Header */}
        <div className="p-6 border-b border-slate-100 flex items-center justify-between bg-slate-50/50">
          <div>
            <h3 className="text-lg font-bold text-slate-900">Add Room & Structure Data</h3>
            <p className="text-xs text-slate-500">Inventory Management Wizard</p>
          </div>
          <button onClick={onClose} className="p-2 text-slate-400 hover:text-slate-600 rounded-lg hover:bg-slate-100 t-button">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Tab Selection */}
        <div className="flex border-b border-slate-200 px-6 pt-2 bg-slate-50/30 gap-4">
          <button
            onClick={() => { setActiveTab('room'); setError(null); }}
            className={`pb-2.5 text-xs font-bold border-b-2 flex items-center gap-1.5 t-pill ${
              activeTab === 'room' ? 'border-blue-600 text-blue-600' : 'border-transparent text-slate-500 hover:text-slate-700'
            }`}
          >
            <BedDouble className="w-4 h-4" /> New Room
          </button>
          <button
            onClick={() => { setActiveTab('roomType'); setError(null); }}
            className={`pb-2.5 text-xs font-bold border-b-2 flex items-center gap-1.5 t-pill ${
              activeTab === 'roomType' ? 'border-blue-600 text-blue-600' : 'border-transparent text-slate-500 hover:text-slate-700'
            }`}
          >
            <Sparkles className="w-4 h-4" /> {t('reservations.roomType')}
          </button>
          <button
            onClick={() => { setActiveTab('building'); setError(null); }}
            className={`pb-2.5 text-xs font-bold border-b-2 flex items-center gap-1.5 t-pill ${
              activeTab === 'building' ? 'border-blue-600 text-blue-600' : 'border-transparent text-slate-500 hover:text-slate-700'
            }`}
          >
            <Building2 className="w-4 h-4" /> New Building
          </button>
        </div>

        {error && (
          <div className="mx-6 mt-4 p-3 rounded-xl bg-rose-50 border border-rose-200 text-rose-700 text-xs font-medium">
            {error}
          </div>
        )}

        {/* Form Body */}
        <div className="p-6">
          {activeTab === 'room' && (
            <form onSubmit={handleCreateRoom} className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">
                    Room Number *
                  </label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. 101, A204"
                    value={roomNumber}
                    onChange={(e) => setRoomNumber(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 font-bold uppercase"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">
                    Display Name
                  </label>
                  <input
                    type="text"
                    placeholder="e.g. Deluxe Pool View"
                    value={displayName}
                    onChange={(e) => setDisplayName(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                  />
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-700 block mb-1">
                  {t('reservations.roomType')} *
                </label>
                {roomTypes.length === 0 ? (
                  <p className="text-xs text-amber-700 bg-amber-50 p-2 rounded-lg border border-amber-200">
                    No room types yet. Use the "{t('reservations.roomType')}" tab above to create one first.
                  </p>
                ) : (
                  <select
                    value={selectedRoomTypeId}
                    onChange={(e) => setSelectedRoomTypeId(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 bg-white"
                  >
                    {roomTypes.map((rt) => (
                      <option key={rt.id} value={rt.id}>
                        {rt.name} ({rt.code}) - ฿{rt.baseRate.toLocaleString()}{t('common.perNight')}
                      </option>
                    ))}
                  </select>
                )}
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">
                    Building
                  </label>
                  <select
                    value={selectedBuildingId}
                    onChange={(e) => setSelectedBuildingId(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 bg-white"
                  >
                    <option value="">(No building)</option>
                    {buildings.map((b) => (
                      <option key={b.id} value={b.id}>
                        {b.name} ({b.code})
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">
                    Floor
                  </label>
                  <select
                    value={selectedFloorId}
                    onChange={(e) => setSelectedFloorId(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 bg-white"
                  >
                    <option value="">(No floor)</option>
                    {floors.map((f) => (
                      <option key={f.id} value={f.id}>
                        {f.name}
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">
                    Capacity (guests)
                  </label>
                  <input
                    type="number"
                    min={1}
                    value={capacity}
                    onChange={(e) => setCapacity(Number(e.target.value))}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">
                    Initial Status
                  </label>
                  <select
                    value={operationalStatus}
                    onChange={(e) => setOperationalStatus(Number(e.target.value))}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 bg-white"
                  >
                    <option value={RoomOperationalStatus.Clean}>✨ {t('operationalStatus.Clean')}</option>
                    <option value={RoomOperationalStatus.Dirty}>🧹 {t('operationalStatus.Dirty')}</option>
                    <option value={RoomOperationalStatus.Inspected}>🔍 {t('operationalStatus.Inspected')}</option>
                    <option value={RoomOperationalStatus.Maintenance}>🛠 {t('operationalStatus.Maintenance')}</option>
                  </select>
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-700 block mb-1">
                  {t('common.notes')}
                </label>
                <input
                  type="text"
                  placeholder="e.g. Near elevator, garden view"
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                />
              </div>

              <div className="pt-2 flex justify-end gap-2">
                <button
                  type="button"
                  onClick={onClose}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 hover:text-slate-800 bg-slate-100 rounded-xl hover:bg-slate-200 t-button"
                >
                  {t('common.cancel')}
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-5 py-2 text-xs font-bold text-white bg-blue-600 rounded-xl hover:bg-blue-700 t-button shadow-md flex items-center gap-1.5"
                >
                  <Plus className="w-4 h-4" /> {t('common.save')} {t('common.room')}
                </button>
              </div>
            </form>
          )}

          {activeTab === 'roomType' && (
            <form onSubmit={handleCreateRoomType} className="space-y-4">
              <div>
                <label className="text-xs font-semibold text-slate-700 block mb-1">{t('reservations.roomType')} {t('common.name')} *</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Deluxe King Sea View"
                  value={rtName}
                  onChange={(e) => setRtName(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                />
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">{t('reservations.roomType')} Code *</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. DLX-K"
                    value={rtCode}
                    onChange={(e) => setRtCode(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 uppercase"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">Base Rate ({t('common.baht')}{t('common.perNight')}) *</label>
                  <input
                    type="number"
                    required
                    min={0}
                    value={rtBaseRate}
                    onChange={(e) => setRtBaseRate(Number(e.target.value))}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                  />
                </div>
              </div>
              <div className="pt-2 flex justify-end gap-2">
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-5 py-2 text-xs font-bold text-white bg-blue-600 rounded-xl hover:bg-blue-700 t-button shadow-md"
                >
                  {t('common.create')} {t('reservations.roomType')}
                </button>
              </div>
            </form>
          )}

          {activeTab === 'building' && (
            <form onSubmit={handleCreateBuilding} className="space-y-4">
              <div>
                <label className="text-xs font-semibold text-slate-700 block mb-1">Building {t('common.name')} *</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Ocean Wing, Building A"
                  value={bldName}
                  onChange={(e) => setBldName(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                />
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">Building Code *</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. BLD-A"
                    value={bldCode}
                    onChange={(e) => setBldCode(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 uppercase"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">Number of Floors</label>
                  <input
                    type="number"
                    min={1}
                    value={bldFloors}
                    onChange={(e) => setBldFloors(Number(e.target.value))}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                  />
                </div>
              </div>
              <div className="pt-2 flex justify-end gap-2">
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-5 py-2 text-xs font-bold text-white bg-blue-600 rounded-xl hover:bg-blue-700 t-button shadow-md"
                >
                  {t('common.create')} Building
                </button>
              </div>
            </form>
          )}
        </div>
      </div>
    </div>
  );
};
