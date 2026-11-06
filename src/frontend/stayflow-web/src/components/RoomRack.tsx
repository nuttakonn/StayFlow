import React, { useState } from 'react';
import {
  Sparkles,
  AlertTriangle,
  Clock,
  ShieldCheck,
  Wrench,
  Ban,
  Search,
  Plus,
  Filter,
  BedDouble,
  Building2,
  RefreshCw,
  Tag
} from 'lucide-react';
import { RoomDto, RoomOperationalStatus, RoomStatusSummaryDto } from '../types';
import { useTranslation } from 'react-i18next';

interface RoomRackProps {
  rooms: RoomDto[];
  summary: RoomStatusSummaryDto | null;
  loading: boolean;
  businessType?: string;
  onSelectRoom: (room: RoomDto) => void;
  onOpenCreate: () => void;
  onOpenRoomTypes?: () => void;
  onRefresh: () => void;
  onQuickStatusChange: (roomId: string, newStatus: RoomOperationalStatus) => Promise<void>;
}

export const RoomRack: React.FC<RoomRackProps> = ({
  rooms,
  summary,
  loading,
  businessType,
  onSelectRoom,
  onOpenCreate,
  onOpenRoomTypes,
  onRefresh,
  onQuickStatusChange
}) => {
  const { t } = useTranslation();
  const rateUnit = businessType === 'Apartment' ? t('common.perNight').replace('/', '').trim() : t('common.night');
  const [activeFilter, setActiveFilter] = useState<RoomOperationalStatus | 'ALL'>('ALL');
  const [searchTerm, setSearchTerm] = useState('');

  const filterTabs = [
    { id: 'ALL', label: t('roomRack.allRooms'), count: summary?.totalRooms ?? rooms.length, color: 'text-slate-700', bg: 'bg-slate-100' },
    { id: RoomOperationalStatus.Clean, label: `✨ ${t('operationalStatus.Clean')}`, count: summary?.cleanCount ?? 0, color: 'text-emerald-700', bg: 'bg-emerald-50' },
    { id: RoomOperationalStatus.Dirty, label: `🧹 ${t('operationalStatus.Dirty')}`, count: summary?.dirtyCount ?? 0, color: 'text-rose-700', bg: 'bg-rose-50' },
    { id: RoomOperationalStatus.Cleaning, label: `🧼 ${t('operationalStatus.Cleaning')}`, count: summary?.cleaningCount ?? 0, color: 'text-sky-700', bg: 'bg-sky-50' },
    { id: RoomOperationalStatus.Inspected, label: `🔍 ${t('operationalStatus.Inspected')}`, count: summary?.inspectedCount ?? 0, color: 'text-indigo-700', bg: 'bg-indigo-50' },
    { id: RoomOperationalStatus.Maintenance, label: `🛠 ${t('operationalStatus.Maintenance')}`, count: summary?.maintenanceCount ?? 0, color: 'text-amber-700', bg: 'bg-amber-50' },
    { id: RoomOperationalStatus.OutOfService, label: `🚫 ${t('operationalStatus.OutOfService')}`, count: summary?.outOfServiceCount ?? 0, color: 'text-slate-700', bg: 'bg-slate-100' },
  ];

  const filteredRooms = rooms.filter((r) => {
    const matchesFilter = activeFilter === 'ALL' || r.operationalStatusCode === activeFilter;
    const matchesSearch = !searchTerm.trim() ||
      r.roomNumber.toLowerCase().includes(searchTerm.trim().toLowerCase()) ||
      (r.displayName && r.displayName.toLowerCase().includes(searchTerm.trim().toLowerCase())) ||
      r.roomTypeName.toLowerCase().includes(searchTerm.trim().toLowerCase());
    return matchesFilter && matchesSearch;
  });

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'Clean':
        return { label: t('operationalStatus.Clean'), icon: <Sparkles className="w-3.5 h-3.5 text-emerald-600" />, badge: 'bg-emerald-50 text-emerald-800 border-emerald-200' };
      case 'Dirty':
        return { label: t('operationalStatus.Dirty'), icon: <AlertTriangle className="w-3.5 h-3.5 text-rose-600" />, badge: 'bg-rose-50 text-rose-800 border-rose-200' };
      case 'Cleaning':
        return { label: t('operationalStatus.Cleaning'), icon: <Clock className="w-3.5 h-3.5 text-sky-600" />, badge: 'bg-sky-50 text-sky-800 border-sky-200' };
      case 'Inspected':
        return { label: t('operationalStatus.Inspected'), icon: <ShieldCheck className="w-3.5 h-3.5 text-indigo-600" />, badge: 'bg-indigo-50 text-indigo-800 border-indigo-200' };
      case 'Maintenance':
        return { label: t('operationalStatus.Maintenance'), icon: <Wrench className="w-3.5 h-3.5 text-amber-600" />, badge: 'bg-amber-50 text-amber-800 border-amber-200' };
      case 'OutOfService':
      default:
        return { label: t('operationalStatus.OutOfService'), icon: <Ban className="w-3.5 h-3.5 text-slate-500" />, badge: 'bg-slate-100 text-slate-700 border-slate-200' };
    }
  };

  return (
    <div className="space-y-6">
      {/* Control Bar: Filters, Search, Add Room */}
      <div className="bg-white p-4 rounded-2xl border border-slate-200 shadow-sm space-y-4">
        {/* Top bar: Search & Actions */}
        <div className="flex flex-col sm:flex-row gap-3 items-stretch sm:items-center justify-between">
          <div className="relative flex-1 max-w-md">
            <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              type="text"
              placeholder={t('roomRack.title') + '...'}
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full pl-10 pr-4 py-2 bg-slate-50 border border-slate-200 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:bg-white transition-colors"
            />
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={onRefresh}
              title={t('common.refresh')}
              className="p-2 text-slate-600 hover:text-slate-900 bg-slate-50 border border-slate-200 rounded-xl hover:bg-slate-100 t-button"
            >
              <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
            </button>

            {onOpenRoomTypes && (
              <button
                onClick={onOpenRoomTypes}
                className="flex items-center gap-1.5 px-3.5 py-2 bg-indigo-50 hover:bg-indigo-100 text-indigo-700 border border-indigo-200 rounded-xl text-sm font-bold shadow-xs t-button transition"
              >
                <Tag className="w-4 h-4" />
                <span>{t('property.typeHotel')}</span>
              </button>
            )}

            <button
              onClick={onOpenCreate}
              className="flex items-center gap-1.5 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-xl text-sm font-bold shadow-sm t-button"
            >
              <Plus className="w-4 h-4" />
              <span>{t('common.add')} {t('common.room')}</span>
            </button>
          </div>
        </div>

        {/* Filter Pills with Counts */}
        <div className="flex items-center gap-2 overflow-x-auto pb-1 scrollbar-none">
          <span className="text-xs text-slate-400 font-semibold uppercase tracking-wider flex items-center gap-1 mr-1">
            <Filter className="w-3.5 h-3.5" /> {t('common.filter')}:
          </span>
          {filterTabs.map((tab) => {
            const isActive = activeFilter === tab.id;
            return (
              <button
                key={String(tab.id)}
                onClick={() => setActiveFilter(tab.id as any)}
                className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold border t-pill whitespace-nowrap ${
                  isActive
                    ? 'bg-blue-600 text-white border-blue-600 shadow-sm'
                    : `${tab.bg} ${tab.color} border-slate-200 hover:border-slate-300`
                }`}
              >
                <span>{tab.label}</span>
                <span className={`px-1.5 py-0.2 rounded-full text-[10px] font-black ${
                  isActive ? 'bg-white/20 text-white' : 'bg-white text-slate-700'
                }`}>
                  {tab.count}
                </span>
              </button>
            );
          })}
        </div>
      </div>

      {/* Loading state */}
      {loading && (
        <div className="py-12 flex flex-col items-center justify-center text-slate-400 gap-2">
          <div className="w-8 h-8 border-3 border-blue-600 border-t-transparent rounded-full animate-spin"></div>
          <span className="text-sm">{t('common.loadingData')}</span>
        </div>
      )}

      {/* Empty State */}
      {!loading && filteredRooms.length === 0 && (
        <div className="bg-white rounded-2xl border border-dashed border-slate-300 p-12 text-center space-y-4">
          <div className="w-14 h-14 bg-blue-50 text-blue-600 rounded-2xl mx-auto flex items-center justify-center shadow-inner">
            <BedDouble className="w-7 h-7" />
          </div>
          <div className="max-w-sm mx-auto space-y-1">
            <h4 className="text-base font-bold text-slate-800">{t('roomRack.noRooms')}</h4>
            <p className="text-xs text-slate-500">
              {t('common.noData')}
            </p>
          </div>
          <button
            onClick={onOpenCreate}
            className="px-5 py-2.5 bg-blue-600 hover:bg-blue-700 text-white rounded-xl text-xs font-bold shadow-md t-button inline-flex items-center gap-1.5"
          >
            <Plus className="w-4 h-4" /> {t('common.add')} {t('common.room')}
          </button>
        </div>
      )}

      {/* Room Rack Grid (Visual Rack) */}
      {!loading && filteredRooms.length > 0 && (
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
          {filteredRooms.map((room) => {
            const badge = getStatusBadge(room.operationalStatus);
            return (
              <div
                key={room.id}
                className="bg-white rounded-2xl border border-slate-200 p-4 shadow-sm hover:shadow-md t-card flex flex-col justify-between cursor-pointer group"
                onClick={() => onSelectRoom(room)}
              >
                {/* Top header of room card */}
                <div>
                  <div className="flex items-center justify-between gap-2 mb-2">
                    <span className="text-2xl font-black text-slate-900 tracking-tight group-hover:text-blue-600 transition-colors">
                      {room.roomNumber}
                    </span>
                    <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold border ${badge.badge}`}>
                      {badge.icon}
                      <span>{badge.label}</span>
                    </span>
                  </div>

                  <h5 className="text-sm font-semibold text-slate-800 line-clamp-1">
                    {room.displayName || `${room.roomTypeName}`}
                  </h5>
                  <p className="text-xs text-slate-500 mt-0.5">
                    {room.roomTypeName} • ฿{room.baseRate.toLocaleString()}/{rateUnit}
                  </p>

                  <div className="flex items-center gap-2 mt-3 text-[11px] text-slate-500">
                    <span className="flex items-center gap-1">
                      <Building2 className="w-3.5 h-3.5 text-slate-400" />
                      {room.buildingName || '—'}
                    </span>
                    <span>•</span>
                    <span>{room.floorName || '—'}</span>
                  </div>
                </div>

                {/* Quick Action One-Click Footer for Housekeeping */}
                <div
                  className="mt-4 pt-3 border-t border-slate-100 flex items-center justify-between gap-2"
                  onClick={(e) => e.stopPropagation()}
                >
                  {room.operationalStatusCode === RoomOperationalStatus.Dirty && (
                    <button
                      onClick={() => onQuickStatusChange(room.id, RoomOperationalStatus.Cleaning)}
                      className="w-full py-1.5 px-2 bg-sky-50 hover:bg-sky-100 text-sky-800 border border-sky-200 rounded-lg text-xs font-bold flex items-center justify-center gap-1 t-button"
                    >
                      <Clock className="w-3 h-3 text-sky-600" />
                      <span>{t('housekeeping.startCleaning')}</span>
                    </button>
                  )}

                  {room.operationalStatusCode === RoomOperationalStatus.Cleaning && (
                    <button
                      onClick={() => onQuickStatusChange(room.id, RoomOperationalStatus.Clean)}
                      className="w-full py-1.5 px-2 bg-emerald-50 hover:bg-emerald-100 text-emerald-800 border border-emerald-200 rounded-lg text-xs font-bold flex items-center justify-center gap-1 t-button"
                    >
                      <Sparkles className="w-3 h-3 text-emerald-600" />
                      <span>{t('housekeeping.markClean')}</span>
                    </button>
                  )}

                  {room.operationalStatusCode === RoomOperationalStatus.Clean && (
                    <button
                      onClick={() => onQuickStatusChange(room.id, RoomOperationalStatus.Dirty)}
                      className="w-full py-1.5 px-2 bg-rose-50 hover:bg-rose-100 text-rose-800 border border-rose-200 rounded-lg text-xs font-bold flex items-center justify-center gap-1 t-button"
                    >
                      <AlertTriangle className="w-3 h-3 text-rose-600" />
                      <span>{t('housekeeping.markDirty')}</span>
                    </button>
                  )}

                  {room.operationalStatusCode === RoomOperationalStatus.Inspected && (
                    <span className="text-[11px] text-indigo-700 font-semibold flex items-center gap-1 w-full justify-center py-1">
                      <ShieldCheck className="w-3.5 h-3.5" /> {t('housekeeping.inspected')}
                    </span>
                  )}

                  {(room.operationalStatusCode === RoomOperationalStatus.Maintenance || room.operationalStatusCode === RoomOperationalStatus.OutOfService) && (
                    <button
                      onClick={() => onQuickStatusChange(room.id, RoomOperationalStatus.Clean)}
                      className="w-full py-1.5 px-2 bg-slate-100 hover:bg-slate-200 text-slate-800 border border-slate-300 rounded-lg text-xs font-bold flex items-center justify-center gap-1 t-button"
                    >
                      <Sparkles className="w-3 h-3 text-emerald-600" />
                      <span>{t('housekeeping.markClean')}</span>
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
