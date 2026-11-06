import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import type { PropertyDto, ReservationDto } from '../types';
import { apiFetch } from '../lib/api';

interface Props {
  property: PropertyDto;
  token: string;
  orgId: string;
}

interface CalendarRoom {
  id: string;
  number: string;
  typeName: string;
  baseRate: number;
}

interface CalendarReservation {
  id: string;
  roomId: string;
  guestName: string;
  checkInDate: string;
  checkOutDate: string;
  status: string;
  nights: number;
}

const STATUS_COLORS_CALENDAR: Record<string, string> = {
  Pending: 'bg-amber-400',
  Confirmed: 'bg-blue-500',
  CheckedIn: 'bg-emerald-500',
  CheckedOut: 'bg-slate-300',
  Cancelled: 'bg-red-200',
  NoShow: 'bg-purple-300',
};

export default function AvailabilityCalendar({ property, token, orgId }: Props) {
  const { t, i18n } = useTranslation();
  const [rooms, setRooms] = useState<CalendarRoom[]>([]);
  const [reservations, setReservations] = useState<CalendarReservation[]>([]);
  const [loading, setLoading] = useState(false);
  const [startDate, setStartDate] = useState<Date>(() => {
    const d = new Date();
    d.setDate(d.getDate() - 2); // show 2 days before today
    return d;
  });
  const DAYS = 30;

  const dates = Array.from({ length: DAYS }, (_, i) => {
    const d = new Date(startDate);
    d.setDate(startDate.getDate() + i);
    return d;
  });

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      // Fetch rooms
      const rawRoomData = await apiFetch<any>(
        `/api/v1/properties/${property.id}/rooms?isActive=true`,
        token, orgId
      );
      const roomList: any[] = Array.isArray(rawRoomData) ? rawRoomData : (rawRoomData?.data ?? []);
      setRooms(roomList.map((r) => ({
        id: r.id,
        number: r.roomNumber,
        typeName: r.roomTypeName ?? 'Standard',
        baseRate: r.baseRate ?? 0,
      })));

      // Fetch reservations with room assignments
      const resData = await apiFetch<any>(
        `/api/v1/reservations?propertyId=${property.id}&pageSize=200`,
        token, orgId
      );
      const allRes: ReservationDto[] = Array.isArray(resData) ? resData : (resData?.data ?? []);
      setReservations(allRes
        .filter(r => r.roomId && r.status !== 'Cancelled' && r.status !== 'NoShow')
        .map(r => ({
          id: r.id,
          roomId: r.roomId!,
          guestName: r.guestName,
          checkInDate: r.checkInDate,
          checkOutDate: r.checkOutDate,
          status: r.status,
          nights: r.numberOfNights,
        })));
    } finally {
      setLoading(false);
    }
  }, [property.id, token, orgId]);

  useEffect(() => { fetchData(); }, [fetchData]);

  const isoDate = (d: Date) => d.toISOString().slice(0, 10);

  // Find reservation for a room on a specific date
  const getReservationForCell = (roomId: string, date: Date) => {
    const dateStr = isoDate(date);
    return reservations.find(r =>
      r.roomId === roomId &&
      r.checkInDate <= dateStr &&
      dateStr < r.checkOutDate // half-open interval
    );
  };

  // Find if date is check-in date
  const isCheckIn = (roomId: string, date: Date) => {
    const dateStr = isoDate(date);
    return reservations.find(r => r.roomId === roomId && r.checkInDate === dateStr);
  };

  const today = isoDate(new Date());

  const moveWeek = (delta: number) => {
    const d = new Date(startDate);
    d.setDate(d.getDate() + delta * 7);
    setStartDate(d);
  };

  const goToday = () => {
    const d = new Date();
    d.setDate(d.getDate() - 2);
    setStartDate(d);
  };

  return (
    <div className="space-y-4">
      {/* Controls */}
      <div className="flex items-center gap-3 flex-wrap">
        <button onClick={() => moveWeek(-1)} className="px-3 py-2 bg-white border border-slate-200 rounded-xl text-slate-600 hover:bg-slate-50 t-button">{t('calendar.previousWeek')}</button>
        <button onClick={goToday} className="px-4 py-2 bg-indigo-50 border border-indigo-200 text-indigo-700 rounded-xl font-medium hover:bg-indigo-100 t-button">{t('calendar.today')}</button>
        <button onClick={() => moveWeek(1)} className="px-3 py-2 bg-white border border-slate-200 rounded-xl text-slate-600 hover:bg-slate-50 t-button">{t('calendar.nextWeek')}</button>
        <span className="text-sm text-slate-500 ml-2">
          {dates[0].toLocaleDateString(i18n.language, { month: 'long', year: 'numeric' })}
          {dates[0].getMonth() !== dates[DAYS - 1].getMonth() &&
            ` – ${dates[DAYS - 1].toLocaleDateString(i18n.language, { month: 'long', year: 'numeric' })}`
          }
        </span>
        <button onClick={fetchData} className="ml-auto px-3 py-2 text-slate-400 hover:text-slate-600 t-button">{t('calendar.refresh')}</button>
      </div>

      {/* Legend */}
      <div className="flex gap-4 flex-wrap text-xs">
        {[
          { color: 'bg-blue-500', label: t('calendar.legendConfirmed') },
          { color: 'bg-emerald-500', label: t('calendar.legendCheckedIn') },
          { color: 'bg-amber-400', label: t('calendar.legendPending') },
          { color: 'bg-slate-300', label: t('calendar.legendCheckedOut') },
        ].map(l => (
          <div key={l.label} className="flex items-center gap-1.5">
            <div className={`w-3 h-3 rounded ${l.color}`} />
            <span className="text-slate-600">{l.label}</span>
          </div>
        ))}
        <div className="flex items-center gap-1.5">
          <div className="w-3 h-3 rounded bg-white border border-slate-200" />
          <span className="text-slate-600">{t('calendar.available')}</span>
        </div>
      </div>

      {loading ? (
        <div className="text-center py-12 text-slate-400">{t('calendar.loading')}</div>
      ) : (
        <div className="overflow-x-auto rounded-2xl border border-slate-200 shadow-sm">
          <div style={{ minWidth: Math.max(800, 140 + DAYS * 38) }}>
            {/* Header row: dates */}
            <div className="flex bg-slate-50 border-b border-slate-200 sticky top-0 z-10">
              {/* Room column header */}
              <div className="flex-shrink-0 w-36 px-3 py-2 text-xs font-semibold text-slate-500 border-r border-slate-200">
                {t('calendar.roomColumn')}
              </div>
              {dates.map(d => {
                const ds = isoDate(d);
                const isToday = ds === today;
                const isWeekend = d.getDay() === 0 || d.getDay() === 6;
                return (
                  <div key={ds} className={`flex-shrink-0 w-9 text-center py-2 border-r border-slate-100 ${isToday ? 'bg-indigo-100' : isWeekend ? 'bg-slate-100' : ''}`}>
                    <div className={`text-xs font-bold ${isToday ? 'text-indigo-700' : 'text-slate-600'}`}>
                      {d.getDate()}
                    </div>
                    <div className={`text-[10px] ${isToday ? 'text-indigo-500' : 'text-slate-400'}`}>
                      {d.toLocaleDateString(i18n.language, { weekday: 'short' })}
                    </div>
                  </div>
                );
              })}
            </div>

            {/* Room rows */}
            {rooms.length === 0 ? (
              <div className="text-center py-8 text-slate-400 text-sm">
                {t('calendar.noRoomsInSystem')}
              </div>
            ) : (
              rooms.map((room, ri) => (
                <div key={room.id} className={`flex items-stretch border-b border-slate-100 ${ri % 2 === 0 ? 'bg-white' : 'bg-slate-50/40'}`}>
                  {/* Room info */}
                  <div className="flex-shrink-0 w-36 px-3 py-2 border-r border-slate-200 flex flex-col justify-center">
                    <div className="font-semibold text-slate-800 text-sm">{room.number}</div>
                    <div className="text-xs text-slate-400 truncate">{room.typeName}</div>
                  </div>

                  {/* Date cells */}
                  {dates.map(d => {
                    const ds = isoDate(d);
                    const res = getReservationForCell(room.id, d);
                    const checkin = isCheckIn(room.id, d);
                    const isToday = ds === today;

                    return (
                      <div key={ds}
                        className={`flex-shrink-0 w-9 h-10 border-r border-slate-100 relative ${isToday ? 'bg-indigo-50' : ''}`}
                        title={res ? `${res.guestName} (${res.status})` : t('calendar.available')}
                      >
                        {res && (
                          <div className={`absolute inset-y-1 ${checkin ? 'inset-x-1 rounded' : 'left-0 right-0 rounded-r'} ${STATUS_COLORS_CALENDAR[res.status] ?? 'bg-slate-300'} opacity-80`}>
                            {checkin && (
                              <div className="text-white text-[9px] font-bold px-0.5 leading-none pt-0.5 truncate">
                                {res.guestName.split(' ')[0]}
                              </div>
                            )}
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              ))
            )}
          </div>
        </div>
      )}

      <div className="text-xs text-slate-400 text-center">
        {t('calendar.summary', { rooms: rooms.length, reservations: reservations.length })}
      </div>
    </div>
  );
}
