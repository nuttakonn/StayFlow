import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import type { ReservationDto, GuestDto, AvailabilityRoomTypeDto, PropertyDto, FolioDto } from '../types';
import { apiFetch } from '../lib/api';

interface Props {
  property: PropertyDto;
  token: string;
  orgId: string;
}

const STATUS_COLORS: Record<string, { bg: string; text: string; dot: string }> = {
  Pending: { bg: 'bg-amber-50 border-amber-200', text: 'text-amber-800', dot: 'bg-amber-400' },
  Confirmed: { bg: 'bg-blue-50 border-blue-200', text: 'text-blue-800', dot: 'bg-blue-500' },
  CheckedIn: { bg: 'bg-emerald-50 border-emerald-200', text: 'text-emerald-800', dot: 'bg-emerald-500' },
  CheckedOut: { bg: 'bg-slate-50 border-slate-200', text: 'text-slate-700', dot: 'bg-slate-400' },
  Cancelled: { bg: 'bg-red-50 border-red-200', text: 'text-red-700', dot: 'bg-red-400' },
  NoShow: { bg: 'bg-purple-50 border-purple-200', text: 'text-purple-800', dot: 'bg-purple-400' },
};

function StatusBadge({ status }: { status: string }) {
  const { t } = useTranslation();
  const c = STATUS_COLORS[status] ?? STATUS_COLORS.Pending;
  return (
    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold border ${c.bg} ${c.text}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${c.dot}`} />
      {t('status.' + status, status)}
    </span>
  );
}

function formatDate(d: string) {
  return new Date(d + 'T00:00:00').toLocaleDateString('th-TH', {
    year: 'numeric', month: 'short', day: 'numeric'
  });
}

function formatCurrency(n: number, currency = 'THB') {
  return new Intl.NumberFormat('th-TH', { style: 'currency', currency, maximumFractionDigits: 0 }).format(n);
}

// ── Create Reservation Modal ───────────────────────────────────────────────
interface CreateReservationModalProps {
  property: PropertyDto;
  token: string;
  orgId: string;
  onClose: () => void;
  onCreated: () => void;
}

function CreateReservationModal({ property, token, orgId, onClose, onCreated }: CreateReservationModalProps) {
  const { t } = useTranslation();
  const [step, setStep] = useState<'dates' | 'guest' | 'room' | 'confirm'>('dates');
  const [checkIn, setCheckIn] = useState('');
  const [checkOut, setCheckOut] = useState('');
  const [adults, setAdults] = useState(2);
  const [children, setChildren] = useState(0);
  const [guestSearch, setGuestSearch] = useState('');
  const [guests, setGuests] = useState<GuestDto[]>([]);
  const [selectedGuest, setSelectedGuest] = useState<GuestDto | null>(null);
  const [showNewGuestForm, setShowNewGuestForm] = useState(false);
  const [newGuestName, setNewGuestName] = useState('');
  const [newGuestPhone, setNewGuestPhone] = useState('');
  const [newGuestEmail, setNewGuestEmail] = useState('');
  const [availability, setAvailability] = useState<AvailabilityRoomTypeDto[]>([]);
  const [selectedRoomTypeId, setSelectedRoomTypeId] = useState('');
  const [selectedRoomId, setSelectedRoomId] = useState('');
  const [quotedRate, setQuotedRate] = useState(0);
  const [source, setSource] = useState('Phone');
  const [specialRequests, setSpecialRequests] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const tonight = new Date();
  const tomorrow = new Date(tonight);
  tomorrow.setDate(tomorrow.getDate() + 1);
  const dayAfter = new Date(tonight);
  dayAfter.setDate(dayAfter.getDate() + 2);

  useEffect(() => {
    if (!checkIn) setCheckIn(tomorrow.toISOString().slice(0, 10));
    if (!checkOut) setCheckOut(dayAfter.toISOString().slice(0, 10));
  }, []);

  const searchGuests = useCallback(async (q: string) => {
    if (q.length < 1) { setGuests([]); return; }
    const data = await apiFetch<{ data: GuestDto[] }>(`/api/v1/guests?search=${encodeURIComponent(q)}&pageSize=10`, token, orgId);
    setGuests(data.data ?? []);
  }, [token, orgId]);

  useEffect(() => {
    const timer = setTimeout(() => searchGuests(guestSearch), 300);
    return () => clearTimeout(timer);
  }, [guestSearch, searchGuests]);

  const fetchAvailability = async () => {
    if (!checkIn || !checkOut) return;
    setLoading(true);
    try {
      const data = await apiFetch<AvailabilityRoomTypeDto[]>(
        `/api/v1/properties/${property.id}/availability?checkInDate=${checkIn}&checkOutDate=${checkOut}`,
        token, orgId);
      setAvailability(data);
    } finally {
      setLoading(false);
    }
  };

  const createNewGuest = async () => {
    if (!newGuestName.trim()) return;
    setLoading(true);
    try {
      const g = await apiFetch<GuestDto>('/api/v1/guests', token, orgId, {
        method: 'POST',
        body: JSON.stringify({ fullName: newGuestName, phone: newGuestPhone, email: newGuestEmail })
      });
      setSelectedGuest(g);
      setShowNewGuestForm(false);
    } catch {
      setError(t('reservations.createGuestError'));
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async () => {
    if (!selectedGuest || !checkIn || !checkOut) return;
    setLoading(true);
    setError('');
    try {
      const nights = Math.round((new Date(checkOut).getTime() - new Date(checkIn).getTime()) / 86400000);
      const total = quotedRate * nights;
      await apiFetch('/api/v1/reservations', token, orgId, {
        method: 'POST',
        body: JSON.stringify({
          propertyId: property.id,
          guestId: selectedGuest.id,
          checkInDate: checkIn,
          checkOutDate: checkOut,
          adults,
          children,
          roomTypeId: selectedRoomTypeId || null,
          roomId: selectedRoomId || null,
          quotedRate,
          totalAmount: total,
          currency: property.defaultCurrency,
          source,
          specialRequests,
          notes: null
        })
      });
      onCreated();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('common.error'));
    } finally {
      setLoading(false);
    }
  };

  const nights = checkIn && checkOut
    ? Math.max(0, Math.round((new Date(checkOut).getTime() - new Date(checkIn).getTime()) / 86400000))
    : 0;

  const stepLabel = step === 'dates' ? t('reservations.stepDates')
    : step === 'guest' ? t('reservations.stepGuest')
    : step === 'room' ? t('reservations.stepRoom')
    : t('reservations.stepConfirm');

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" style={{ background: 'rgba(0,0,0,0.5)' }}>
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto t-modal-content">
        {/* Header */}
        <div className="sticky top-0 bg-white border-b border-slate-100 px-6 py-4 flex items-center justify-between z-10">
          <div>
            <h2 className="text-xl font-bold text-slate-800">🛎️ {t('reservations.createTitle')}</h2>
            <p className="text-sm text-slate-500">{property.name}</p>
          </div>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600 text-2xl leading-none">&times;</button>
        </div>

        {/* Steps indicator */}
        <div className="flex items-center gap-1 px-6 py-3 bg-slate-50">
          {(['dates', 'guest', 'room', 'confirm'] as const).map((s, i) => (
            <div key={s} className="flex items-center gap-1">
              <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold transition-all ${
                step === s ? 'bg-indigo-600 text-white' :
                ['dates','guest','room','confirm'].indexOf(step) > i ? 'bg-emerald-500 text-white' :
                'bg-slate-200 text-slate-500'
              }`}>{i + 1}</div>
              {i < 3 && <div className="w-6 h-0.5 bg-slate-200" />}
            </div>
          ))}
          <div className="ml-2 text-sm text-slate-500">{stepLabel}</div>
        </div>

        <div className="p-6 space-y-4">
          {error && (
            <div className="bg-red-50 border border-red-200 text-red-700 rounded-lg px-4 py-3 text-sm">
              ⚠️ {error}
            </div>
          )}

          {/* Step 1: Dates */}
          {step === 'dates' && (
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-semibold text-slate-700 mb-1">📅 {t('reservations.checkInDate')}</label>
                  <input type="date" value={checkIn} onChange={e => setCheckIn(e.target.value)}
                    className="w-full border border-slate-200 rounded-xl px-4 py-3 text-slate-800 focus:ring-2 focus:ring-indigo-300 focus:border-indigo-400 outline-none" />
                </div>
                <div>
                  <label className="block text-sm font-semibold text-slate-700 mb-1">📅 {t('reservations.checkOutDate')}</label>
                  <input type="date" value={checkOut} onChange={e => setCheckOut(e.target.value)}
                    className="w-full border border-slate-200 rounded-xl px-4 py-3 text-slate-800 focus:ring-2 focus:ring-indigo-300 focus:border-indigo-400 outline-none" />
                </div>
              </div>
              {nights > 0 && (
                <div className="bg-indigo-50 border border-indigo-100 rounded-xl px-4 py-3 text-indigo-700 text-sm font-medium">
                  🌙 {nights} {t('common.nights')}
                </div>
              )}
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-semibold text-slate-700 mb-1">👨‍👩‍👧 {t('reservations.adults')}</label>
                  <div className="flex items-center gap-2">
                    <button onClick={() => setAdults(Math.max(1, adults - 1))} className="w-10 h-10 rounded-xl bg-slate-100 hover:bg-slate-200 font-bold text-slate-600 t-button">-</button>
                    <span className="text-xl font-bold w-8 text-center text-slate-800">{adults}</span>
                    <button onClick={() => setAdults(adults + 1)} className="w-10 h-10 rounded-xl bg-slate-100 hover:bg-slate-200 font-bold text-slate-600 t-button">+</button>
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-semibold text-slate-700 mb-1">👶 {t('reservations.children')}</label>
                  <div className="flex items-center gap-2">
                    <button onClick={() => setChildren(Math.max(0, children - 1))} className="w-10 h-10 rounded-xl bg-slate-100 hover:bg-slate-200 font-bold text-slate-600 t-button">-</button>
                    <span className="text-xl font-bold w-8 text-center text-slate-800">{children}</span>
                    <button onClick={() => setChildren(children + 1)} className="w-10 h-10 rounded-xl bg-slate-100 hover:bg-slate-200 font-bold text-slate-600 t-button">+</button>
                  </div>
                </div>
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1">📞 {t('reservations.sourceLabel')}</label>
                <select value={source} onChange={e => setSource(e.target.value)}
                  className="w-full border border-slate-200 rounded-xl px-4 py-3 text-slate-800 focus:ring-2 focus:ring-indigo-300 outline-none">
                  <option value="Phone">{t('reservations.sourcePhone')}</option>
                  <option value="WalkIn">Walk-in</option>
                  <option value="Email">{t('reservations.sourceEmail')}</option>
                  <option value="Online">{t('reservations.sourceOnline')}</option>
                  <option value="OTA">OTA (Agoda/Booking)</option>
                  <option value="Other">{t('common.other')}</option>
                </select>
              </div>
              <button
                onClick={() => setStep('guest')}
                disabled={!checkIn || !checkOut || nights <= 0}
                className="w-full py-3 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-40 text-white font-semibold rounded-xl t-button"
              >
                {t('reservations.nextGuest')}
              </button>
            </div>
          )}

          {/* Step 2: Guest */}
          {step === 'guest' && (
            <div className="space-y-4">
              {selectedGuest ? (
                <div className="bg-emerald-50 border border-emerald-200 rounded-xl px-4 py-4 flex items-center justify-between">
                  <div>
                    <div className="font-semibold text-emerald-800">{selectedGuest.fullName}</div>
                    <div className="text-sm text-emerald-600">{selectedGuest.phone ?? selectedGuest.email ?? '—'}</div>
                  </div>
                  <button onClick={() => setSelectedGuest(null)} className="text-emerald-600 hover:text-emerald-800 text-sm underline">{t('common.change')}</button>
                </div>
              ) : (
                <>
                  <div>
                    <label className="block text-sm font-semibold text-slate-700 mb-1">🔍 {t('reservations.searchGuest')}</label>
                    <input
                      type="text" value={guestSearch}
                      onChange={e => setGuestSearch(e.target.value)}
                      placeholder={t('reservations.searchGuestPlaceholder')}
                      className="w-full border border-slate-200 rounded-xl px-4 py-3 text-slate-800 focus:ring-2 focus:ring-indigo-300 outline-none"
                    />
                  </div>
                  {guests.length > 0 && (
                    <div className="border border-slate-200 rounded-xl divide-y divide-slate-100 max-h-48 overflow-y-auto">
                      {guests.map(g => (
                        <button key={g.id} onClick={() => { setSelectedGuest(g); setGuestSearch(''); }}
                          className="w-full text-left px-4 py-3 hover:bg-slate-50 t-button">
                          <div className="font-medium text-slate-800">{g.fullName}</div>
                          <div className="text-sm text-slate-500">{g.phone ?? g.email ?? '—'}</div>
                        </button>
                      ))}
                    </div>
                  )}
                  {!showNewGuestForm && (
                    <button onClick={() => setShowNewGuestForm(true)}
                      className="w-full py-2.5 border-2 border-dashed border-indigo-200 rounded-xl text-indigo-600 hover:border-indigo-400 text-sm font-medium t-button">
                      + {t('reservations.addNewGuest')}
                    </button>
                  )}
                  {showNewGuestForm && (
                    <div className="border border-indigo-200 rounded-xl p-4 space-y-3 bg-indigo-50">
                      <h4 className="font-semibold text-indigo-800">{t('reservations.newGuestTitle')}</h4>
                      <input placeholder={t('reservations.fullNameRequired')} value={newGuestName} onChange={e => setNewGuestName(e.target.value)}
                        className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm" />
                      <input
                        type="tel"
                        inputMode="tel"
                        placeholder={t('reservations.phonePlaceholder')}
                        value={newGuestPhone}
                        onChange={e => setNewGuestPhone(e.target.value.replace(/[^\d+\-\s]/g, ''))}
                        pattern="[0-9+\-\s]{8,20}"
                        maxLength={20}
                        className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm"
                      />
                      <input placeholder={t('common.email')} value={newGuestEmail} onChange={e => setNewGuestEmail(e.target.value)}
                        className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm" />
                      <div className="flex gap-2">
                        <button onClick={createNewGuest} disabled={loading || !newGuestName}
                          className="flex-1 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium disabled:opacity-40 t-button">
                          {t('common.save')}
                        </button>
                        <button onClick={() => setShowNewGuestForm(false)} className="flex-1 py-2 bg-slate-100 text-slate-600 rounded-lg text-sm t-button">
                          {t('common.cancel')}
                        </button>
                      </div>
                    </div>
                  )}
                </>
              )}
              <div className="flex gap-3">
                <button onClick={() => setStep('dates')} className="flex-1 py-3 bg-slate-100 text-slate-600 rounded-xl font-medium t-button">← {t('common.back')}</button>
                <button onClick={() => { fetchAvailability(); setStep('room'); }}
                  disabled={!selectedGuest}
                  className="flex-1 py-3 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-40 text-white font-semibold rounded-xl t-button">
                  {t('reservations.nextRoom')}
                </button>
              </div>
            </div>
          )}

          {/* Step 3: Room */}
          {step === 'room' && (
            <div className="space-y-4">
              {loading ? (
                <div className="text-center py-8 text-slate-400">{t('reservations.checkingRooms')}</div>
              ) : availability.length === 0 ? (
                <div className="text-center py-8 text-slate-500">
                  <div className="text-4xl mb-2">😔</div>
                  <div className="font-medium">{t('reservations.noRoomsAvailable')}</div>
                </div>
              ) : (
                <div className="space-y-3">
                  <div className="text-sm text-slate-500">{t('reservations.selectRoomType')} ({formatDate(checkIn)} – {formatDate(checkOut)})</div>
                  {availability.map(rt => (
                    <div key={rt.roomTypeId}
                      onClick={() => { setSelectedRoomTypeId(rt.roomTypeId); setSelectedRoomId(''); setQuotedRate(rt.baseRate); }}
                      className={`border-2 rounded-xl p-4 cursor-pointer t-card ${
                        selectedRoomTypeId === rt.roomTypeId
                          ? 'border-indigo-500 bg-indigo-50'
                          : 'border-slate-200 hover:border-indigo-300'
                      }`}>
                      <div className="flex items-center justify-between mb-2">
                        <div className="font-semibold text-slate-800">{rt.name}</div>
                        <div className="text-indigo-700 font-bold">{formatCurrency(rt.baseRate, 'THB')}<span className="text-xs font-normal text-slate-500">/{t('common.night')}</span></div>
                      </div>
                      <div className="flex items-center gap-3 text-sm text-slate-500">
                        <span>👥 {rt.standardOccupancy}–{rt.maxOccupancy} {t('common.persons')}</span>
                        <span className="text-emerald-600 font-medium">✅ {t('reservations.available')} {rt.availableRooms} {t('common.rooms')}</span>
                      </div>
                      {selectedRoomTypeId === rt.roomTypeId && rt.rooms.length > 0 && (
                        <div className="mt-3 pt-3 border-t border-indigo-200 grid grid-cols-2 sm:grid-cols-3 gap-2">
                          <div
                            className={`text-center py-2 rounded-lg text-sm cursor-pointer border ${selectedRoomId === '' ? 'border-indigo-400 bg-indigo-100 font-semibold' : 'border-slate-200 hover:bg-slate-50'}`}
                            onClick={e => { e.stopPropagation(); setSelectedRoomId(''); }}>
                            {t('reservations.noRoomAssigned')}
                          </div>
                          {rt.rooms.map(r => (
                            <div key={r.roomId}
                              onClick={e => { e.stopPropagation(); setSelectedRoomId(r.roomId); }}
                              className={`text-center py-2 rounded-lg text-sm cursor-pointer border ${selectedRoomId === r.roomId ? 'border-indigo-500 bg-indigo-600 text-white font-semibold' : 'border-slate-200 hover:bg-slate-50'}`}>
                              {r.roomNumber}
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1">💬 {t('reservations.specialRequests')}</label>
                <textarea value={specialRequests} onChange={e => setSpecialRequests(e.target.value)}
                  placeholder={t('reservations.specialRequestsPlaceholder')} rows={2}
                  className="w-full border border-slate-200 rounded-xl px-4 py-3 text-slate-800 text-sm focus:ring-2 focus:ring-indigo-300 outline-none resize-none" />
              </div>
              <div className="flex gap-3">
                <button onClick={() => setStep('guest')} className="flex-1 py-3 bg-slate-100 text-slate-600 rounded-xl font-medium t-button">← {t('common.back')}</button>
                <button onClick={() => setStep('confirm')} disabled={!selectedRoomTypeId}
                  className="flex-1 py-3 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-40 text-white font-semibold rounded-xl t-button">
                  {t('reservations.nextConfirm')}
                </button>
              </div>
            </div>
          )}

          {/* Step 4: Confirm */}
          {step === 'confirm' && selectedGuest && (
            <div className="space-y-4">
              <div className="bg-slate-50 rounded-xl p-4 space-y-3 text-sm">
                <div className="flex justify-between">
                  <span className="text-slate-500">{t('reservations.guest')}</span>
                  <span className="font-semibold text-slate-800">{selectedGuest.fullName}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-500">{t('reservations.checkIn')}</span>
                  <span className="font-semibold">{formatDate(checkIn)}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-500">{t('reservations.checkOut')}</span>
                  <span className="font-semibold">{formatDate(checkOut)}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-500">{t('reservations.nights')}</span>
                  <span className="font-semibold">{nights} {t('common.nights')}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-500">{t('reservations.occupants')}</span>
                  <span className="font-semibold">{adults} {t('reservations.adults')}{children > 0 ? `, ${children} ${t('reservations.children')}` : ''}</span>
                </div>
                {selectedRoomId && (
                  <div className="flex justify-between">
                    <span className="text-slate-500">{t('common.room')}</span>
                    <span className="font-semibold text-indigo-700">
                      {availability.find(rt => rt.roomTypeId === selectedRoomTypeId)?.rooms.find(r => r.roomId === selectedRoomId)?.roomNumber}
                    </span>
                  </div>
                )}
                {quotedRate > 0 && (
                  <div className="flex justify-between border-t border-slate-200 pt-3">
                    <span className="text-slate-700 font-semibold">{t('common.total')}</span>
                    <span className="text-xl font-bold text-indigo-700">{formatCurrency(quotedRate * nights, property.defaultCurrency)}</span>
                  </div>
                )}
              </div>
              <div className="flex gap-3">
                <button onClick={() => setStep('room')} className="flex-1 py-3 bg-slate-100 text-slate-600 rounded-xl font-medium t-button">← {t('common.back')}</button>
                <button onClick={handleSubmit} disabled={loading}
                  className="flex-1 py-3 bg-emerald-600 hover:bg-emerald-700 disabled:opacity-40 text-white font-bold rounded-xl t-button">
                  {loading ? `⏳ ${t('common.saving')}...` : `✅ ${t('reservations.confirmBooking')}`}
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Reservation Detail Modal ───────────────────────────────────────────────
interface ReservationDetailModalProps {
  reservation: ReservationDto;
  token: string;
  orgId: string;
  onClose: () => void;
  onRefresh: () => void;
}

function ReservationDetailModal({ reservation: initial, token, orgId, onClose, onRefresh }: ReservationDetailModalProps) {
  const { t } = useTranslation();
  const [reservation, setReservation] = useState(initial);
  const [loading, setLoading] = useState(false);
  const [cancelReason, setCancelReason] = useState('');
  const [showCancelForm, setShowCancelForm] = useState(false);
  const [error, setError] = useState('');

  // Folio
  const [folio, setFolio] = useState<FolioDto | null>(null);
  const [folioLoading, setFolioLoading] = useState(false);
  const [showFolio, setShowFolio] = useState(false);
  const [quickPayMethod, setQuickPayMethod] = useState('PromptPay');
  const [quickPayAmount, setQuickPayAmount] = useState(0);
  const [quickPayRef, setQuickPayRef] = useState('');
  const [folioActionLoading, setFolioActionLoading] = useState(false);
  const [folioError, setFolioError] = useState('');
  const [folioSuccess, setFolioSuccess] = useState('');

  const loadFolio = async () => {
    setFolioLoading(true);
    setFolioError('');
    try {
      const data = await apiFetch<FolioDto>(
        `/api/v1/properties/${reservation.propertyId}/reservations/${reservation.id}/folio`,
        token, orgId,
        { method: 'POST', body: JSON.stringify({}) }
      );
      setFolio(data);
      setQuickPayAmount(data.balance > 0 ? data.balance : 0);
      setShowFolio(true);
    } catch (e: unknown) {
      setFolioError(e instanceof Error ? e.message : t('billing.loadFolioError'));
    } finally {
      setFolioLoading(false);
    }
  };

  const handleQuickPay = async () => {
    if (!folio || quickPayAmount <= 0) return;
    setFolioActionLoading(true);
    setFolioError('');
    try {
      const updated = await apiFetch<FolioDto>(
        `/api/v1/properties/${reservation.propertyId}/folios/${folio.id}/payments`,
        token, orgId,
        { method: 'POST', body: JSON.stringify({ method: quickPayMethod, amount: Number(quickPayAmount), referenceNumber: quickPayRef.trim() || null }) }
      );
      setFolio(updated);
      setQuickPayAmount(updated.balance > 0 ? updated.balance : 0);
      setQuickPayRef('');
      setFolioSuccess(t('billing.paymentRecorded'));
      setTimeout(() => setFolioSuccess(''), 3000);
    } catch (e: unknown) {
      setFolioError(e instanceof Error ? e.message : t('billing.recordPaymentError'));
    } finally {
      setFolioActionLoading(false);
    }
  };

  const handleCloseFolio = async () => {
    if (!folio || folio.balance !== 0) return;
    setFolioActionLoading(true);
    setFolioError('');
    try {
      const updated = await apiFetch<FolioDto>(
        `/api/v1/properties/${reservation.propertyId}/folios/${folio.id}/close`,
        token, orgId,
        { method: 'POST' }
      );
      setFolio(updated);
      setFolioSuccess(t('billing.folioClosed'));
      setTimeout(() => setFolioSuccess(''), 3000);
    } catch (e: unknown) {
      setFolioError(e instanceof Error ? e.message : t('billing.closeFolioError'));
    } finally {
      setFolioActionLoading(false);
    }
  };

  // Room assignment
  const [showRoomPicker, setShowRoomPicker] = useState(false);
  const [loadingRooms, setLoadingRooms] = useState(false);
  const [roomAvailability, setRoomAvailability] = useState<AvailabilityRoomTypeDto[]>([]);
  const [selectedRoomId, setSelectedRoomId] = useState('');

  const loadRooms = async () => {
    setLoadingRooms(true);
    try {
      const data = await apiFetch<AvailabilityRoomTypeDto[]>(
        `/api/v1/properties/${reservation.propertyId}/availability?checkInDate=${reservation.checkInDate}&checkOutDate=${reservation.checkOutDate}`,
        token, orgId
      );
      setRoomAvailability(data ?? []);
    } catch { /* show empty */ }
    finally { setLoadingRooms(false); }
  };

  const assignRoom = async () => {
    if (!selectedRoomId) return;
    setLoading(true);
    setError('');
    try {
      const updated = await apiFetch<ReservationDto>(
        `/api/v1/reservations/${reservation.id}/assign-room`,
        token, orgId,
        { method: 'POST', body: JSON.stringify({ roomId: selectedRoomId }) }
      );
      setReservation(updated);
      setShowRoomPicker(false);
      setSelectedRoomId('');
      onRefresh();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('reservations.assignRoomError'));
    } finally {
      setLoading(false);
    }
  };

  const action = async (path: string, body?: object) => {
    setLoading(true);
    setError('');
    try {
      const updated = await apiFetch<ReservationDto>(`/api/v1/reservations/${reservation.id}/${path}`, token, orgId, {
        method: 'POST', body: body ? JSON.stringify(body) : undefined
      });
      setReservation(updated);
      onRefresh();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('common.error'));
    } finally {
      setLoading(false);
    }
  };

  const r = reservation;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" style={{ background: 'rgba(0,0,0,0.55)' }}>
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-lg max-h-[90vh] overflow-y-auto t-modal-content">
        <div className="sticky top-0 bg-white border-b border-slate-100 px-6 py-4 flex items-center justify-between">
          <div>
            <div className="flex items-center gap-2">
              <span className="text-xl">🛎️</span>
              <span className="font-bold text-slate-800">{r.guestName}</span>
              <StatusBadge status={r.status} />
            </div>
            <div className="text-sm text-slate-500 mt-0.5">
              {formatDate(r.checkInDate)} → {formatDate(r.checkOutDate)} ({r.numberOfNights} {t('common.nights')})
            </div>
          </div>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600 text-2xl">&times;</button>
        </div>

        <div className="p-6 space-y-5">
          {error && <div className="bg-red-50 border border-red-200 text-red-700 rounded-lg px-4 py-2 text-sm">⚠️ {error}</div>}

          {/* Info grid */}
          <div className="grid grid-cols-2 gap-3 text-sm">
            {[
              [`👤 ${t('reservations.guest')}`, r.guestName],
              [`📞 ${t('common.phone')}`, r.guestPhone ?? '—'],
              [`📧 ${t('common.email')}`, r.guestEmail ?? '—'],
              [`🏠 ${t('reservations.roomType')}`, r.roomTypeName ?? '—'],
              [`🔑 ${t('common.room')}`, r.roomNumber ?? t('reservations.notAssigned')],
              [`👥 ${t('reservations.occupants')}`, `${r.adults} ${t('reservations.adults')}${r.children > 0 ? ` + ${r.children} ${t('reservations.children')}` : ''}`],
              [`💵 ${t('reservations.ratePerNight')}`, formatCurrency(r.quotedRate, r.currency)],
              [`💰 ${t('common.total')}`, formatCurrency(r.totalAmount, r.currency)],
              [`📲 ${t('reservations.source')}`, r.source],
            ].map(([label, val]) => (
              <div key={label}>
                <div className="text-xs text-slate-400 font-medium">{label}</div>
                <div className="font-semibold text-slate-800 truncate">{val}</div>
              </div>
            ))}
          </div>

          {r.specialRequests && (
            <div className="bg-amber-50 border border-amber-100 rounded-lg px-3 py-2 text-sm">
              <span className="font-medium text-amber-800">💬 {t('reservations.specialRequests')}:</span> {r.specialRequests}
            </div>
          )}

          {/* Folio Section */}
          <div className="border border-slate-200 rounded-xl overflow-hidden">
            <button
              onClick={() => { if (!showFolio) loadFolio(); else setShowFolio(false); }}
              disabled={folioLoading}
              className="w-full flex items-center justify-between px-4 py-3 bg-slate-50 hover:bg-slate-100 transition-colors text-sm font-bold text-slate-700"
            >
              <span className="flex items-center gap-2">
                <span>💳</span>
                <span>{t('billing.folio')}</span>
                {folio && (
                  <span className={`ml-1 px-2 py-0.5 rounded-full text-xs font-bold ${
                    folio.balance > 0 ? 'bg-rose-100 text-rose-700' : 'bg-emerald-100 text-emerald-700'
                  }`}>
                    {folio.balance > 0 ? `${t('billing.outstanding')} ${folio.balance.toLocaleString()}` : `✓ ${t('billing.paidFull')}`} {folio.currency}
                  </span>
                )}
              </span>
              <span className="text-slate-400 text-xs">{folioLoading ? `${t('common.loading')}...` : showFolio ? '▲' : `▼ ${t('common.view')}`}</span>
            </button>

            {showFolio && folio && (
              <div className="p-4 space-y-3 border-t border-slate-200 bg-white">
                {folioError && (
                  <div className="text-xs text-rose-600 bg-rose-50 border border-rose-200 rounded-lg px-3 py-2">{folioError}</div>
                )}
                {folioSuccess && (
                  <div className="text-xs text-emerald-700 bg-emerald-50 border border-emerald-200 rounded-lg px-3 py-2">✅ {folioSuccess}</div>
                )}

                {/* Balance summary */}
                <div className="grid grid-cols-3 gap-2 text-center text-xs">
                  <div className="p-2 bg-slate-50 rounded-lg">
                    <div className="text-slate-400 font-semibold">{t('billing.totalCharges')}</div>
                    <div className="font-bold text-slate-800">{folio.totalCharges.toLocaleString()}</div>
                  </div>
                  <div className="p-2 bg-slate-50 rounded-lg">
                    <div className="text-slate-400 font-semibold">{t('billing.paid')}</div>
                    <div className="font-bold text-emerald-700">{folio.totalPayments.toLocaleString()}</div>
                  </div>
                  <div className={`p-2 rounded-lg ${folio.balance > 0 ? 'bg-rose-50' : 'bg-emerald-50'}`}>
                    <div className="text-slate-400 font-semibold">{t('billing.balance')}</div>
                    <div className={`font-black ${folio.balance > 0 ? 'text-rose-700' : 'text-emerald-700'}`}>
                      {folio.balance.toLocaleString()}
                    </div>
                  </div>
                </div>

                {/* Charges list (compact) */}
                {folio.items.length > 0 && (
                  <div className="border border-slate-100 rounded-lg divide-y divide-slate-100">
                    {folio.items.map(item => (
                      <div key={item.id} className="flex items-center justify-between px-3 py-1.5 text-xs">
                        <span className="text-slate-700 truncate max-w-[65%]">{item.description}</span>
                        <span className="font-bold text-slate-800 shrink-0">{item.amount.toLocaleString()} {folio.currency}</span>
                      </div>
                    ))}
                  </div>
                )}

                {/* Quick pay form */}
                {folio.status === 'Open' && folio.balance > 0 && (
                  <div className="bg-indigo-50 border border-indigo-100 rounded-xl p-3 space-y-2">
                    <div className="text-xs font-bold text-indigo-800">{t('billing.recordPaymentTitle')}</div>
                    <div className="grid grid-cols-2 gap-2">
                      <select
                        value={quickPayMethod}
                        onChange={e => setQuickPayMethod(e.target.value)}
                        className="px-2 py-1.5 border border-slate-200 rounded-lg text-xs bg-white"
                      >
                        <option value="PromptPay">📱 PromptPay</option>
                        <option value="Cash">💵 {t('billing.methodCash')}</option>
                        <option value="CreditCard">💳 {t('billing.methodCreditCard')}</option>
                        <option value="BankTransfer">🏦 {t('billing.methodBankTransfer')}</option>
                      </select>
                      <input
                        type="number"
                        value={quickPayAmount}
                        onChange={e => setQuickPayAmount(Number(e.target.value))}
                        className="px-2 py-1.5 border border-slate-200 rounded-lg text-xs font-bold text-emerald-700"
                        placeholder={t('billing.amountLabel')}
                      />
                    </div>
                    <input
                      type="text"
                      value={quickPayRef}
                      onChange={e => setQuickPayRef(e.target.value)}
                      className="w-full px-2 py-1.5 border border-slate-200 rounded-lg text-xs"
                      placeholder={t('billing.referenceNumberPlaceholder')}
                    />
                    <button
                      onClick={handleQuickPay}
                      disabled={folioActionLoading || quickPayAmount <= 0}
                      className="w-full py-2 bg-indigo-600 hover:bg-indigo-700 text-white text-xs font-bold rounded-lg disabled:opacity-40 t-button"
                    >
                      {folioActionLoading ? `${t('common.saving')}...` : `✓ ${t('billing.recordPaymentBtn')}`}
                    </button>
                  </div>
                )}

                {/* Close folio */}
                {folio.status === 'Open' && folio.balance === 0 && (
                  <button
                    onClick={handleCloseFolio}
                    disabled={folioActionLoading}
                    className="w-full py-2 bg-slate-800 hover:bg-slate-900 text-white text-xs font-bold rounded-lg disabled:opacity-40 t-button"
                  >
                    {folioActionLoading ? `${t('common.saving')}...` : `✓ ${t('billing.closeFolioBtn')}`}
                  </button>
                )}

                {folio.status === 'Closed' && (
                  <div className="text-center text-xs text-emerald-700 font-bold py-1">
                    ✅ {t('billing.folioClosed')} — {folio.folioNumber}
                  </div>
                )}
              </div>
            )}

            {showFolio && folioError && !folio && (
              <div className="p-4 text-xs text-rose-600">{folioError}</div>
            )}
          </div>

          {/* Action buttons */}
          <div className="grid grid-cols-2 gap-2">
            {r.status === 'Pending' && (
              <button onClick={() => action('confirm')} disabled={loading}
                className="col-span-2 py-3 bg-blue-600 hover:bg-blue-700 text-white font-semibold rounded-xl t-button disabled:opacity-40">
                ✅ {t('reservations.confirmAction')}
              </button>
            )}
            {r.status === 'Confirmed' && !r.roomNumber && (
              <div className="col-span-2 space-y-2">
                {!showRoomPicker ? (
                  <button
                    onClick={() => { setShowRoomPicker(true); loadRooms(); }}
                    className="w-full py-3 bg-indigo-600 hover:bg-indigo-700 text-white font-bold rounded-xl t-button"
                  >
                    🔑 {t('reservations.assignRoom')}
                  </button>
                ) : (
                  <div className="bg-indigo-50 border border-indigo-200 rounded-xl p-3 space-y-3">
                    <p className="text-sm font-bold text-indigo-800">{t('reservations.selectRoomFor')} {r.checkInDate} → {r.checkOutDate}</p>
                    {loadingRooms ? (
                      <p className="text-xs text-slate-400 text-center py-2">{t('reservations.loadingRooms')}</p>
                    ) : roomAvailability.length === 0 ? (
                      <p className="text-xs text-rose-600 text-center py-2">{t('reservations.noRoomsAvailable')}</p>
                    ) : (
                      <div className="space-y-2 max-h-44 overflow-y-auto pr-1">
                        {roomAvailability.map(rt => (
                          <div key={rt.roomTypeId}>
                            <p className="text-[10px] font-black text-slate-500 uppercase tracking-wider mb-1">
                              {rt.name} — {rt.availableRooms} {t('reservations.roomsAvailable')}
                            </p>
                            <div className="flex flex-wrap gap-1.5">
                              {rt.rooms.map(room => (
                                <button
                                  key={room.roomId}
                                  onClick={() => setSelectedRoomId(room.roomId)}
                                  className={`px-3 py-1.5 text-xs font-bold rounded-lg border transition-all ${
                                    selectedRoomId === room.roomId
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
                        onClick={assignRoom}
                        disabled={!selectedRoomId || loading}
                        className="flex-1 py-2 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-40 text-white font-bold text-sm rounded-xl t-button"
                      >
                        {loading ? `${t('common.saving')}...` : `✓ ${t('reservations.confirmRoom')}`}
                      </button>
                      <button
                        onClick={() => { setShowRoomPicker(false); setSelectedRoomId(''); }}
                        className="px-4 py-2 bg-slate-100 hover:bg-slate-200 text-slate-600 text-sm rounded-xl t-button"
                      >
                        {t('common.cancel')}
                      </button>
                    </div>
                  </div>
                )}
              </div>
            )}
            {r.status === 'Confirmed' && r.roomNumber && (
              <button onClick={() => action('check-in')} disabled={loading}
                className="col-span-2 py-3 bg-emerald-600 hover:bg-emerald-700 text-white font-bold rounded-xl t-button disabled:opacity-40">
                🏨 {t('reservations.checkInAction')}
              </button>
            )}
            {r.status === 'CheckedIn' && (
              <button onClick={() => action('check-out')} disabled={loading}
                className="col-span-2 py-3 bg-slate-700 hover:bg-slate-800 text-white font-bold rounded-xl t-button disabled:opacity-40">
                👋 {t('reservations.checkOutAction')}
              </button>
            )}
            {(r.status === 'Pending' || r.status === 'Confirmed') && (
              <>
                <button onClick={() => action('no-show')} disabled={loading || r.status !== 'Confirmed'}
                  className="py-2.5 bg-purple-100 hover:bg-purple-200 text-purple-800 text-sm font-medium rounded-xl t-button disabled:opacity-40">
                  😶 {t('reservations.noShowAction')}
                </button>
                <button onClick={() => setShowCancelForm(true)} disabled={loading}
                  className="py-2.5 bg-red-50 hover:bg-red-100 text-red-700 text-sm font-medium rounded-xl t-button">
                  ❌ {t('reservations.cancelAction')}
                </button>
              </>
            )}
          </div>

          {showCancelForm && (
            <div className="bg-red-50 border border-red-200 rounded-xl p-4 space-y-3">
              <h4 className="font-semibold text-red-800">{t('reservations.cancelReason')}</h4>
              <input value={cancelReason} onChange={e => setCancelReason(e.target.value)}
                placeholder={t('reservations.cancelReasonPlaceholder')}
                className="w-full border border-red-200 rounded-lg px-3 py-2 text-sm" />
              <div className="flex gap-2">
                <button onClick={() => action('cancel', { reason: cancelReason })} disabled={!cancelReason}
                  className="flex-1 py-2 bg-red-600 text-white rounded-lg text-sm font-medium disabled:opacity-40 t-button">
                  {t('reservations.confirmCancel')}
                </button>
                <button onClick={() => setShowCancelForm(false)} className="flex-1 py-2 bg-slate-100 text-slate-600 rounded-lg text-sm t-button">
                  {t('common.cancel')}
                </button>
              </div>
            </div>
          )}

          {/* Timeline */}
          <div>
            <h4 className="text-sm font-semibold text-slate-600 mb-3">📋 {t('reservations.statusHistory')}</h4>
            <div className="space-y-2">
              {r.timeline.map((tl, i) => (
                <div key={tl.id} className="flex items-start gap-3">
                  <div className={`w-7 h-7 rounded-full flex items-center justify-center flex-shrink-0 text-xs font-bold ${
                    i === r.timeline.length - 1 ? 'bg-indigo-600 text-white' : 'bg-slate-200 text-slate-500'
                  }`}>{i + 1}</div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <StatusBadge status={tl.toStatus} />
                      {tl.fromStatus && <span className="text-xs text-slate-400">← {t('status.' + tl.fromStatus, tl.fromStatus)}</span>}
                    </div>
                    {tl.note && <div className="text-xs text-slate-500 mt-0.5">{tl.note}</div>}
                    <div className="text-xs text-slate-400 mt-0.5">
                      {new Date(tl.createdAtUtc).toLocaleString('th-TH')}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Main Reservation Manager ───────────────────────────────────────────────
export default function ReservationManager({ property, token, orgId }: Props) {
  const { t } = useTranslation();
  const [reservations, setReservations] = useState<ReservationDto[]>([]);
  const [total, setTotal] = useState(0);
  const [statusFilter, setStatusFilter] = useState('');
  const [dateFilter, setDateFilter] = useState('');
  const [loading, setLoading] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [selected, setSelected] = useState<ReservationDto | null>(null);

  const fetchReservations = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ propertyId: property.id, pageSize: '50' });
      if (statusFilter) params.append('status', statusFilter);
      if (dateFilter) params.append('checkInDate', dateFilter);
      const data = await apiFetch<{ data: ReservationDto[]; total: number }>(`/api/v1/reservations?${params}`, token, orgId);
      setReservations(data.data ?? []);
      setTotal(data.total ?? 0);
    } finally {
      setLoading(false);
    }
  }, [property.id, token, orgId, statusFilter, dateFilter]);

  useEffect(() => { fetchReservations(); }, [fetchReservations]);

  const today = new Date().toISOString().slice(0, 10);
  const todayArrivals = reservations.filter(r => r.checkInDate === today && r.status === 'Confirmed').length;
  const inHouse = reservations.filter(r => r.status === 'CheckedIn').length;
  const todayDepartures = reservations.filter(r => r.checkOutDate === today && r.status === 'CheckedIn').length;
  const pending = reservations.filter(r => r.status === 'Pending').length;

  return (
    <div className="space-y-5">
      {/* Stats Dashboard */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        {[
          { icon: '🛬', label: t('reservations.todayArrivals'), value: todayArrivals, color: 'bg-blue-50 border-blue-200 text-blue-700' },
          { icon: '🏨', label: t('reservations.inHouse'), value: inHouse, color: 'bg-emerald-50 border-emerald-200 text-emerald-700' },
          { icon: '🛫', label: t('reservations.todayDepartures'), value: todayDepartures, color: 'bg-amber-50 border-amber-200 text-amber-700' },
          { icon: '⏳', label: t('status.Pending'), value: pending, color: 'bg-purple-50 border-purple-200 text-purple-700' },
        ].map(s => (
          <div key={s.label} className={`border rounded-2xl p-4 flex flex-col items-center gap-1 t-card ${s.color}`}>
            <div className="text-3xl">{s.icon}</div>
            <div className="text-3xl font-bold">{s.value}</div>
            <div className="text-xs font-medium opacity-75">{s.label}</div>
          </div>
        ))}
      </div>

      {/* Controls */}
      <div className="flex flex-wrap gap-3 items-center">
        <div className="flex gap-2 flex-wrap">
          {(['', 'Pending', 'Confirmed', 'CheckedIn', 'CheckedOut', 'Cancelled'] as const).map(s => (
            <button key={s}
              onClick={() => setStatusFilter(s)}
              className={`px-3 py-1.5 rounded-full text-sm font-medium border t-pill ${
                statusFilter === s
                  ? 'bg-indigo-600 text-white border-indigo-600'
                  : 'bg-white text-slate-600 border-slate-200 hover:border-indigo-300'
              }`}>
              {s === '' ? `🔄 ${t('common.all')}` : t('status.' + s, s)}
            </button>
          ))}
        </div>
        <input type="date" value={dateFilter} onChange={e => setDateFilter(e.target.value)}
          className="border border-slate-200 rounded-xl px-3 py-2 text-sm text-slate-700 focus:ring-2 focus:ring-indigo-300 outline-none" />
        {dateFilter && <button onClick={() => setDateFilter('')} className="text-sm text-slate-400 hover:text-slate-600">✕</button>}
        <div className="ml-auto">
          <button onClick={() => setShowCreate(true)}
            className="flex items-center gap-2 px-5 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white font-semibold rounded-xl t-button">
            + {t('reservations.createTitle')}
          </button>
        </div>
      </div>

      {/* Reservation List */}
      {loading ? (
        <div className="text-center py-12 text-slate-400">{t('common.loading')}...</div>
      ) : reservations.length === 0 ? (
        <div className="text-center py-16 text-slate-400">
          <div className="text-5xl mb-3">📋</div>
          <div className="font-medium">{t('reservations.noReservations')}</div>
          <div className="text-sm mt-1">{t('reservations.noReservationsHint')}</div>
        </div>
      ) : (
        <div className="space-y-2">
          <div className="text-sm text-slate-500 mb-1">{t('common.showing')} {reservations.length} {t('common.of')} {total}</div>
          {reservations.map(r => (
            <button key={r.id} onClick={() => setSelected(r)}
              className="w-full text-left bg-white border border-slate-200 hover:border-indigo-300 rounded-2xl p-4 flex items-center gap-4 t-card group transition-all">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-semibold text-slate-800 truncate">{r.guestName}</span>
                  <StatusBadge status={r.status} />
                </div>
                <div className="text-sm text-slate-500">
                  {formatDate(r.checkInDate)} → {formatDate(r.checkOutDate)} · {r.numberOfNights} {t('common.nights')}
                  {r.roomNumber && <span className="ml-2 text-indigo-600 font-medium">· {t('common.room')} {r.roomNumber}</span>}
                </div>
                <div className="text-sm text-slate-400 mt-0.5">
                  👥 {r.adults}{r.children > 0 ? `+${r.children}` : ''} {t('common.persons')}
                  {r.totalAmount > 0 && <span className="ml-2 font-medium text-slate-600">{formatCurrency(r.totalAmount, r.currency)}</span>}
                </div>
              </div>
              <div className="text-slate-300 group-hover:text-indigo-400 text-xl">›</div>
            </button>
          ))}
        </div>
      )}

      {showCreate && (
        <CreateReservationModal
          property={property}
          token={token}
          orgId={orgId}
          onClose={() => setShowCreate(false)}
          onCreated={() => { setShowCreate(false); fetchReservations(); }}
        />
      )}

      {selected && (
        <ReservationDetailModal
          reservation={selected}
          token={token}
          orgId={orgId}
          onClose={() => setSelected(null)}
          onRefresh={() => { fetchReservations(); setSelected(null); }}
        />
      )}
    </div>
  );
}
