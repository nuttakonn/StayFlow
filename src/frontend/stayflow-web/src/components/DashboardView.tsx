import { useEffect, useState, useCallback } from 'react';
import {
  ArrowDownToLine,
  ArrowUpFromLine,
  BedDouble,
  Users,
  CircleDot,
  Banknote,
  AlertCircle,
  RefreshCw,
  TrendingUp,
  CheckCircle2,
} from 'lucide-react';
import { PropertyDto, PropertyDashboardDto, DashboardArrivalDto } from '../types';
import { apiFetch } from '../lib/api';
import { useTranslation } from 'react-i18next';

interface Props {
  property: PropertyDto;
  token: string;
  orgId: string;
  onNavigate?: (tab: string) => void;
}

const STATUS_COLOR: Record<string, string> = {
  Clean: 'bg-emerald-100 text-emerald-700',
  Dirty: 'bg-amber-100 text-amber-700',
  Cleaning: 'bg-sky-100 text-sky-700',
  Inspected: 'bg-indigo-100 text-indigo-700',
  OutOfService: 'bg-rose-100 text-rose-700',
  Maintenance: 'bg-orange-100 text-orange-700',
};

function ArrivalRow({ item, label }: { item: DashboardArrivalDto; label?: string }) {
  const { t } = useTranslation();

  const getReservationStatusLabel = (status: string) => {
    switch (status) {
      case 'Confirmed':  return t('status.Confirmed');
      case 'CheckedIn':  return t('status.CheckedIn');
      case 'CheckedOut': return t('status.CheckedOut');
      case 'Pending':    return t('status.Pending');
      case 'Cancelled':  return t('status.Cancelled');
      case 'NoShow':     return t('status.NoShow');
      default:           return status;
    }
  };

  return (
    <div className="flex items-center justify-between py-2.5 border-b border-slate-100 last:border-0 gap-3">
      <div className="min-w-0">
        <div className="font-semibold text-sm text-slate-800 truncate">
          {item.guestName ?? '—'}
        </div>
        <div className="text-[11px] text-slate-400 mt-0.5">
          {item.roomTypeName ?? '-'}
          {item.roomNumber ? ` · ${t('common.room')} ${item.roomNumber}` : ''}
          {' · '}{item.nights} {t('common.nights')}
        </div>
      </div>
      <div className="shrink-0 text-right">
        {label && (
          <div className="text-[10px] font-bold text-slate-400 uppercase mb-0.5">{label}</div>
        )}
        <span className={`px-2 py-0.5 text-[10px] font-bold rounded-full ${
          item.status === 'CheckedIn' ? 'bg-emerald-100 text-emerald-700' :
          item.status === 'Confirmed' ? 'bg-blue-100 text-blue-700' :
          'bg-slate-100 text-slate-500'
        }`}>
          {getReservationStatusLabel(item.status)}
        </span>
      </div>
    </div>
  );
}

export function DashboardView({ property, token, orgId, onNavigate }: Props) {
  const { t } = useTranslation();
  const [data, setData] = useState<PropertyDashboardDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

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

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const result = await apiFetch<PropertyDashboardDto>(
        `/api/v1/properties/${property.id}/dashboard`,
        token,
        orgId
      );
      setData(result);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('dashboard.loadError'));
    } finally {
      setLoading(false);
    }
  }, [property.id, token, orgId]);

  useEffect(() => {
    load();
  }, [load]);

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20 text-slate-400">
        <RefreshCw className="w-5 h-5 animate-spin mr-2" />
        {t('common.loadingData')}
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-rose-50 border border-rose-200 rounded-2xl p-6 flex items-start gap-3">
        <AlertCircle className="w-5 h-5 text-rose-500 shrink-0 mt-0.5" />
        <div>
          <div className="font-bold text-rose-800 text-sm">{t('dashboard.loadError')}</div>
          <div className="text-xs text-rose-600 mt-1">{error}</div>
          <button onClick={load} className="mt-3 px-3 py-1.5 text-xs font-bold bg-rose-600 text-white rounded-lg hover:bg-rose-700">
            {t('common.retry')}
          </button>
        </div>
      </div>
    );
  }

  if (!data) return null;

  const fmt = (n: number) => n.toLocaleString('th-TH');

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-black text-slate-900">{property.name}</h2>
          <p className="text-xs text-slate-400 mt-0.5">
            {t('dashboard.today')} {new Date().toLocaleDateString('th-TH', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}
          </p>
        </div>
        <button
          onClick={load}
          className="flex items-center gap-1.5 text-xs font-semibold text-slate-500 hover:text-slate-700 bg-slate-100 hover:bg-slate-200 px-3 py-2 rounded-xl transition-colors"
        >
          <RefreshCw className="w-3.5 h-3.5" />
          {t('common.refresh')}
        </button>
      </div>

      {/* KPI Row */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        {/* Today Arrivals */}
        <button
          onClick={() => onNavigate?.('operations')}
          className="p-4 rounded-2xl bg-white border border-slate-200 hover:border-blue-300 hover:shadow-sm transition-all text-left group"
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="w-8 h-8 rounded-xl bg-blue-100 flex items-center justify-center">
              <ArrowDownToLine className="w-4 h-4 text-blue-600" />
            </div>
            <span className="text-xs font-bold text-slate-500 uppercase">{t('dashboard.arrivals')}</span>
          </div>
          <div className="text-3xl font-black text-blue-700">{data.todayArrivals}</div>
        </button>

        {/* In House */}
        <button
          onClick={() => onNavigate?.('operations')}
          className="p-4 rounded-2xl bg-white border border-slate-200 hover:border-emerald-300 hover:shadow-sm transition-all text-left group"
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="w-8 h-8 rounded-xl bg-emerald-100 flex items-center justify-center">
              <Users className="w-4 h-4 text-emerald-600" />
            </div>
            <span className="text-xs font-bold text-slate-500 uppercase">{t('dashboard.inHouse')}</span>
          </div>
          <div className="text-3xl font-black text-emerald-700">{data.inHouseGuests}</div>
        </button>

        {/* Today Departures */}
        <button
          onClick={() => onNavigate?.('operations')}
          className="p-4 rounded-2xl bg-white border border-slate-200 hover:border-amber-300 hover:shadow-sm transition-all text-left group"
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="w-8 h-8 rounded-xl bg-amber-100 flex items-center justify-center">
              <ArrowUpFromLine className="w-4 h-4 text-amber-600" />
            </div>
            <span className="text-xs font-bold text-slate-500 uppercase">{t('dashboard.departures')}</span>
          </div>
          <div className="text-3xl font-black text-amber-700">{data.todayDepartures}</div>
        </button>

        {/* Occupancy */}
        <div className="p-4 rounded-2xl bg-white border border-slate-200">
          <div className="flex items-center gap-2 mb-2">
            <div className="w-8 h-8 rounded-xl bg-indigo-100 flex items-center justify-center">
              <BedDouble className="w-4 h-4 text-indigo-600" />
            </div>
            <span className="text-xs font-bold text-slate-500 uppercase">{t('dashboard.occupancyRate')}</span>
          </div>
          <div className="text-3xl font-black text-indigo-700">{data.occupancyRate}%</div>
          <div className="text-[11px] text-slate-400 mt-1">
            {data.occupiedRooms}/{data.totalActiveRooms} {t('common.room')}
          </div>
          {/* Mini occupancy bar */}
          <div className="mt-2 w-full bg-slate-100 rounded-full h-1.5">
            <div
              className="h-1.5 rounded-full bg-indigo-500 transition-all"
              style={{ width: `${Math.min(100, data.occupancyRate)}%` }}
            />
          </div>
        </div>
      </div>

      {/* Financial Row */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <button
          onClick={() => onNavigate?.('billing')}
          className="p-4 rounded-2xl bg-white border border-slate-200 hover:border-emerald-300 hover:shadow-sm transition-all text-left"
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="w-8 h-8 rounded-xl bg-emerald-100 flex items-center justify-center">
              <TrendingUp className="w-4 h-4 text-emerald-600" />
            </div>
            <span className="text-xs font-bold text-slate-500 uppercase">{t('dashboard.totalRevenue')}</span>
          </div>
          <div className="text-2xl font-black text-emerald-700">
            {fmt(data.todayCollected)}
            <span className="text-xs font-semibold text-slate-400 ml-1">{data.currency}</span>
          </div>
        </button>

        <button
          onClick={() => onNavigate?.('billing')}
          className="p-4 rounded-2xl bg-white border border-slate-200 hover:border-rose-300 hover:shadow-sm transition-all text-left"
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="w-8 h-8 rounded-xl bg-rose-100 flex items-center justify-center">
              <Banknote className="w-4 h-4 text-rose-600" />
            </div>
            <span className="text-xs font-bold text-slate-500 uppercase">{t('billing.totalOutstanding')}</span>
          </div>
          <div className="text-2xl font-black text-rose-700">
            {fmt(data.totalOutstandingBalance)}
            <span className="text-xs font-semibold text-slate-400 ml-1">{data.currency}</span>
          </div>
          <div className="text-[11px] text-slate-400 mt-1">{data.openFoliosCount} {t('billing.openFolios')}</div>
        </button>

        {/* Room Status Summary */}
        <div className="p-4 rounded-2xl bg-white border border-slate-200">
          <div className="flex items-center gap-2 mb-3">
            <div className="w-8 h-8 rounded-xl bg-slate-100 flex items-center justify-center">
              <CircleDot className="w-4 h-4 text-slate-600" />
            </div>
            <span className="text-xs font-bold text-slate-500 uppercase">{t('common.status')}</span>
          </div>
          <div className="space-y-1.5">
            {data.roomStatusBreakdown.length === 0 ? (
              <div className="text-xs text-slate-400">{t('common.noData')}</div>
            ) : data.roomStatusBreakdown.map(s => (
              <div key={s.status} className="flex items-center justify-between text-xs">
                <span className={`px-2 py-0.5 rounded-full font-semibold ${STATUS_COLOR[s.status] ?? 'bg-slate-100 text-slate-600'}`}>
                  {getStatusLabel(s.status)}
                </span>
                <span className="font-bold text-slate-700">{s.count}</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Today's Arrivals & Departures */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {/* Arrivals */}
        <div className="bg-white rounded-2xl border border-slate-200 p-5">
          <div className="flex items-center justify-between mb-4">
            <h3 className="font-black text-sm text-slate-900 flex items-center gap-2">
              <ArrowDownToLine className="w-4 h-4 text-blue-500" />
              {t('dashboard.arrivals')}
            </h3>
            <span className="text-xs font-bold text-blue-600 bg-blue-50 px-2 py-0.5 rounded-full">
              {data.todayArrivals}
            </span>
          </div>

          {data.todayArrivalList.length === 0 ? (
            <div className="text-center py-8 text-slate-400 space-y-1">
              <CheckCircle2 className="w-8 h-8 mx-auto text-slate-200" />
              <div className="text-xs">{t('dashboard.noActivity')}</div>
            </div>
          ) : (
            <div>
              {data.todayArrivalList.map(item => (
                <ArrivalRow key={item.reservationId} item={item} />
              ))}
            </div>
          )}
        </div>

        {/* Departures */}
        <div className="bg-white rounded-2xl border border-slate-200 p-5">
          <div className="flex items-center justify-between mb-4">
            <h3 className="font-black text-sm text-slate-900 flex items-center gap-2">
              <ArrowUpFromLine className="w-4 h-4 text-amber-500" />
              {t('dashboard.departures')}
            </h3>
            <span className="text-xs font-bold text-amber-600 bg-amber-50 px-2 py-0.5 rounded-full">
              {data.todayDepartures}
            </span>
          </div>

          {data.todayDepartureList.length === 0 ? (
            <div className="text-center py-8 text-slate-400 space-y-1">
              <CheckCircle2 className="w-8 h-8 mx-auto text-slate-200" />
              <div className="text-xs">{t('dashboard.noActivity')}</div>
            </div>
          ) : (
            <div>
              {data.todayDepartureList.map(item => (
                <ArrivalRow key={item.reservationId} item={item} />
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
