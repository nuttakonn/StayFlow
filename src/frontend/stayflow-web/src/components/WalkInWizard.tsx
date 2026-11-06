import { useState, useCallback, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { GuestDto, PropertyDto, AvailabilityRoomTypeDto, ReservationDto } from '../types';
import { apiFetch } from '../lib/api';

interface Props {
  property: PropertyDto;
  token: string;
  orgId: string;
  onSuccess: (reservation: ReservationDto) => void;
  onClose: () => void;
}

type Step = 'guest' | 'stay' | 'confirm';

const today = () => new Date().toISOString().slice(0, 10);
const tomorrow = () => {
  const d = new Date();
  d.setDate(d.getDate() + 1);
  return d.toISOString().slice(0, 10);
};

function nightsBetween(a: string, b: string) {
  return Math.max(0, Math.round((new Date(b).getTime() - new Date(a).getTime()) / 86_400_000));
}

export function WalkInWizard({ property, token, orgId, onSuccess, onClose }: Props) {
  const { t } = useTranslation();
  const [step, setStep] = useState<Step>('guest');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  // ── Step 1: Guest ──────────────────────────────────────────────────────────
  const [guestSearch, setGuestSearch] = useState('');
  const [guestResults, setGuestResults] = useState<GuestDto[]>([]);
  const [searchLoading, setSearchLoading] = useState(false);
  const [selectedGuest, setSelectedGuest] = useState<GuestDto | null>(null);
  // Create-new guest fields
  const [newGuestMode, setNewGuestMode] = useState(false);
  const [gFullName, setGFullName] = useState('');
  const [gPhone, setGPhone] = useState('');
  const [gEmail, setGEmail] = useState('');

  // ── Step 2: Stay ───────────────────────────────────────────────────────────
  const [checkInDate] = useState(today());
  const [checkOutDate, setCheckOutDate] = useState(tomorrow());
  const [adults, setAdults] = useState(1);
  const [children, setChildren] = useState(0);
  const [availability, setAvailability] = useState<AvailabilityRoomTypeDto[]>([]);
  const [availLoading, setAvailLoading] = useState(false);
  const [seedingRooms, setSeedingRooms] = useState(false);
  const [selectedRoomTypeId, setSelectedRoomTypeId] = useState('');
  const [selectedRoomId, setSelectedRoomId] = useState('');

  // ── Derived ────────────────────────────────────────────────────────────────
  const nights = nightsBetween(checkInDate, checkOutDate);
  const selectedRoomType = availability.find(rt => rt.roomTypeId === selectedRoomTypeId);
  const totalAmount = (selectedRoomType?.baseRate ?? 0) * nights;

  // Search guests with debounce
  useEffect(() => {
    if (!guestSearch.trim() || newGuestMode) { setGuestResults([]); return; }
    const timer = setTimeout(async () => {
      setSearchLoading(true);
      try {
        const data = await apiFetch<{ data: GuestDto[] }>(
          `/api/v1/guests?search=${encodeURIComponent(guestSearch)}&pageSize=8`, token, orgId
        );
        setGuestResults(data.data);
      } catch { setGuestResults([]); }
      finally { setSearchLoading(false); }
    }, 350);
    return () => clearTimeout(timer);
  }, [guestSearch, newGuestMode, token, orgId]);

  // Load availability when moving to stay step
  const loadAvailability = useCallback(async () => {
    if (!checkOutDate || checkOutDate <= checkInDate) return;
    setAvailLoading(true);
    try {
      const data = await apiFetch<AvailabilityRoomTypeDto[]>(
        `/api/v1/properties/${property.id}/availability?checkInDate=${checkInDate}&checkOutDate=${checkOutDate}`,
        token, orgId
      );
      setAvailability(data);
      if (data.length > 0 && !selectedRoomTypeId) {
        setSelectedRoomTypeId(data[0].roomTypeId);
        if (data[0].rooms.length > 0) setSelectedRoomId(data[0].rooms[0].roomId);
      }
    } catch { setAvailability([]); }
    finally { setAvailLoading(false); }
  }, [property.id, token, orgId, checkInDate, checkOutDate, selectedRoomTypeId]);

  useEffect(() => {
    if (step === 'stay') loadAvailability();
  }, [step, loadAvailability]);

  const handleSeedDemoRooms = async () => {
    setSeedingRooms(true);
    setError('');
    try {
      await apiFetch(`/api/v1/properties/${property.id}/seed-demo-rooms`, token, orgId, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: '{}',
      });
      await loadAvailability();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('walkIn.creatingSampleRoomsError'));
    } finally {
      setSeedingRooms(false);
    }
  };

  // When room type changes, auto-select first available room
  useEffect(() => {
    const rt = availability.find(rt => rt.roomTypeId === selectedRoomTypeId);
    if (rt && rt.rooms.length > 0) setSelectedRoomId(rt.rooms[0].roomId);
    else setSelectedRoomId('');
  }, [selectedRoomTypeId, availability]);

  // ── Handlers ───────────────────────────────────────────────────────────────

  const goToStay = async () => {
    setError('');
    if (newGuestMode) {
      if (!gFullName.trim()) { setError(t('walkIn.validationName')); return; }
    } else {
      if (!selectedGuest) { setError(t('walkIn.validationGuest')); return; }
    }
    setStep('stay');
  };

  const goToConfirm = () => {
    setError('');
    if (!checkOutDate || checkOutDate <= checkInDate) { setError(t('walkIn.validationCheckOut')); return; }
    if (nights < 1) { setError(t('walkIn.validationNights')); return; }
    if (!selectedRoomId) { setError(t('walkIn.validationRoom')); return; }
    setStep('confirm');
  };

  const handleCheckIn = async () => {
    setBusy(true);
    setError('');
    try {
      // 1. Get or create guest
      let guestId: string;
      if (newGuestMode) {
        const g = await apiFetch<GuestDto>('/api/v1/guests', token, orgId, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            fullName: gFullName.trim(),
            phone: gPhone.trim() || null,
            email: gEmail.trim() || null,
          }),
        });
        guestId = g.id;
      } else {
        guestId = selectedGuest!.id;
      }

      // 2. Create reservation (confirmed walk-in)
      const res = await apiFetch<ReservationDto>(
        `/api/v1/reservations`, token, orgId,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            propertyId: property.id,
            guestId,
            checkInDate,
            checkOutDate,
            adults,
            children,
            roomTypeId: selectedRoomTypeId || null,
            roomId: selectedRoomId || null,
            quotedRate: selectedRoomType?.baseRate ?? 0,
            totalAmount,
            currency: property.defaultCurrency ?? 'THB',
            source: 'WalkIn',
            specialRequests: null,
            notes: 'Walk-in quick check-in',
          }),
        }
      );

      // 3. Confirm
      await apiFetch<ReservationDto>(
        `/api/v1/reservations/${res.id}/confirm`, token, orgId,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }
      );

      // 4. Assign room
      await apiFetch<ReservationDto>(
        `/api/v1/reservations/${res.id}/assign-room`, token, orgId,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ roomId: selectedRoomId }),
        }
      );

      // 5. Check in
      const checkedIn = await apiFetch<ReservationDto>(
        `/api/v1/reservations/${res.id}/check-in`, token, orgId,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }
      );

      onSuccess(checkedIn);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('walkIn.checkInError'));
      setBusy(false);
    }
  };

  const selectedRoom = selectedRoomType?.rooms.find(r => r.roomId === selectedRoomId);
  const guestName = newGuestMode ? gFullName : selectedGuest?.fullName ?? '';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg mx-4 flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="px-5 py-4 border-b flex items-center justify-between">
          <div>
            <h2 className="font-semibold text-gray-900">{t('walkIn.title')}</h2>
            <div className="flex items-center gap-1 mt-1">
              {(['guest', 'stay', 'confirm'] as Step[]).map((s, i) => (
                <div key={s} className="flex items-center gap-1">
                  <div className={`w-6 h-6 rounded-full text-xs font-bold flex items-center justify-center ${
                    step === s ? 'bg-blue-600 text-white' :
                    (['guest', 'stay', 'confirm'].indexOf(step) > i) ? 'bg-green-500 text-white' :
                    'bg-gray-200 text-gray-500'
                  }`}>{i + 1}</div>
                  {i < 2 && <div className="w-4 h-px bg-gray-300" />}
                </div>
              ))}
              <span className="text-xs text-gray-500 ml-2">
                {step === 'guest' ? t('walkIn.stepGuest') : step === 'stay' ? t('walkIn.stepStay') : t('walkIn.stepConfirm')}
              </span>
            </div>
          </div>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600 text-xl leading-none">✕</button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-5 py-4 space-y-4">
          {error && <div className="bg-red-50 text-red-700 text-sm px-3 py-2 rounded">{error}</div>}

          {/* ── Step 1: Guest ── */}
          {step === 'guest' && (
            <div className="space-y-4">
              <div className="flex gap-2">
                <button
                  onClick={() => { setNewGuestMode(false); setSelectedGuest(null); setError(''); }}
                  className={`flex-1 py-2 rounded border text-sm font-medium ${!newGuestMode ? 'bg-blue-50 border-blue-400 text-blue-700' : 'border-gray-300 text-gray-600 hover:bg-gray-50'}`}
                >
                  {t('walkIn.searchGuest')}
                </button>
                <button
                  onClick={() => { setNewGuestMode(true); setSelectedGuest(null); setGuestSearch(''); setError(''); }}
                  className={`flex-1 py-2 rounded border text-sm font-medium ${newGuestMode ? 'bg-blue-50 border-blue-400 text-blue-700' : 'border-gray-300 text-gray-600 hover:bg-gray-50'}`}
                >
                  {t('walkIn.createGuest')}
                </button>
              </div>

              {!newGuestMode && (
                <div>
                  <input
                    autoFocus
                    type="text"
                    value={guestSearch}
                    onChange={e => { setGuestSearch(e.target.value); setSelectedGuest(null); }}
                    placeholder={t('walkIn.searchPlaceholder')}
                    className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                  />
                  {searchLoading && <p className="text-xs text-gray-400 mt-1">{t('walkIn.searching')}</p>}
                  {guestResults.length > 0 && !selectedGuest && (
                    <div className="mt-1 border rounded divide-y shadow-sm">
                      {guestResults.map(g => (
                        <button key={g.id} onClick={() => { setSelectedGuest(g); setGuestSearch(g.fullName); setGuestResults([]); }}
                          className="w-full text-left px-3 py-2 hover:bg-blue-50 text-sm">
                          <div className="font-medium text-gray-800">{g.fullName}</div>
                          <div className="text-xs text-gray-500">{g.phone || g.email || '—'}</div>
                        </button>
                      ))}
                    </div>
                  )}
                  {selectedGuest && (
                    <div className="mt-2 bg-green-50 border border-green-200 rounded px-3 py-2 flex items-center justify-between">
                      <div>
                        <div className="text-sm font-medium text-green-800">{selectedGuest.fullName}</div>
                        <div className="text-xs text-green-600">{selectedGuest.phone || selectedGuest.email || '—'}</div>
                      </div>
                      <button onClick={() => { setSelectedGuest(null); setGuestSearch(''); }} className="text-green-600 hover:text-green-800 text-xs">{t('walkIn.changeGuest')}</button>
                    </div>
                  )}
                </div>
              )}

              {newGuestMode && (
                <div className="space-y-3">
                  <div>
                    <label className="text-xs text-gray-600 block mb-1">{t('walkIn.fullNameLabel')}</label>
                    <input autoFocus value={gFullName} onChange={e => setGFullName(e.target.value)}
                      placeholder={t('walkIn.fullNamePlaceholder')}
                      className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="text-xs text-gray-600 block mb-1">{t('walkIn.phoneLabel')}</label>
                      <input value={gPhone} onChange={e => setGPhone(e.target.value)} type="tel"
                        className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                    </div>
                    <div>
                      <label className="text-xs text-gray-600 block mb-1">{t('walkIn.emailLabel')}</label>
                      <input value={gEmail} onChange={e => setGEmail(e.target.value)} type="email"
                        className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                    </div>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* ── Step 2: Stay ── */}
          {step === 'stay' && (
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs text-gray-600 block mb-1">{t('walkIn.checkInDateLabel')}</label>
                  <div className="border rounded px-3 py-2 text-sm bg-gray-50 text-gray-500">{checkInDate} {t('walkIn.today')}</div>
                </div>
                <div>
                  <label className="text-xs text-gray-600 block mb-1">{t('walkIn.checkOutDateLabel')}</label>
                  <input type="date" value={checkOutDate} min={checkInDate}
                    onChange={e => { setCheckOutDate(e.target.value); setSelectedRoomTypeId(''); setSelectedRoomId(''); }}
                    className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs text-gray-600 block mb-1">{t('walkIn.adultsLabel')}</label>
                  <input type="number" min={1} max={10} value={adults} onChange={e => setAdults(Number(e.target.value))}
                    className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
                <div>
                  <label className="text-xs text-gray-600 block mb-1">{t('walkIn.childrenLabel')}</label>
                  <input type="number" min={0} max={10} value={children} onChange={e => setChildren(Number(e.target.value))}
                    className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
              </div>

              {/* Room type + room selection */}
              {availLoading && <p className="text-sm text-gray-400">{t('walkIn.loadingRooms')}</p>}
              {!availLoading && availability.length === 0 && (
                <div className="bg-amber-50 border border-amber-200 rounded-lg p-4 space-y-3">
                  <p className="text-amber-800 text-sm font-medium">{t('walkIn.noRoomsAvailable')}</p>
                  <p className="text-amber-700 text-xs">{t('walkIn.noRoomsHint')}</p>
                  <button
                    onClick={handleSeedDemoRooms}
                    disabled={seedingRooms}
                    className="bg-amber-600 text-white text-sm px-4 py-2 rounded hover:bg-amber-700 disabled:opacity-50"
                  >
                    {seedingRooms ? t('walkIn.creatingSampleRooms') : t('walkIn.createSampleRooms')}
                  </button>
                </div>
              )}
              {!availLoading && availability.length > 0 && (
                <div className="space-y-2">
                  <label className="text-xs text-gray-600 block">{t('walkIn.roomTypeLabel')}</label>
                  <div className="space-y-2">
                    {availability.map(rt => (
                      <label key={rt.roomTypeId}
                        className={`flex items-start gap-3 border rounded-lg p-3 cursor-pointer transition-colors ${selectedRoomTypeId === rt.roomTypeId ? 'border-blue-500 bg-blue-50' : 'hover:bg-gray-50'}`}>
                        <input type="radio" name="roomType" value={rt.roomTypeId}
                          checked={selectedRoomTypeId === rt.roomTypeId}
                          onChange={() => setSelectedRoomTypeId(rt.roomTypeId)}
                          className="mt-0.5" />
                        <div className="flex-1 min-w-0">
                          <div className="font-medium text-sm text-gray-800">{rt.name}
                            <span className="ml-2 text-xs text-gray-400">{rt.availableRooms} {t('walkIn.roomsAvailable')}</span>
                          </div>
                          <div className="text-xs text-gray-500 mt-0.5">
                            {rt.baseRate.toLocaleString('th-TH')} {t('common.baht')}{t('common.perNight')} · {t('common.total')} {nights} {t('common.nights')} = {(rt.baseRate * nights).toLocaleString('th-TH')} {t('common.baht')}
                          </div>
                        </div>
                      </label>
                    ))}
                  </div>

                  {selectedRoomType && selectedRoomType.rooms.length > 0 && (
                    <div>
                      <label className="text-xs text-gray-600 block mb-1">{t('walkIn.roomNumberLabel')}</label>
                      <select value={selectedRoomId} onChange={e => setSelectedRoomId(e.target.value)}
                        className="w-full border rounded px-3 py-2 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-blue-500">
                        <option value="">{t('walkIn.selectRoom')}</option>
                        {selectedRoomType.rooms.map(r => (
                          <option key={r.roomId} value={r.roomId}>{t('walkIn.room')} {r.roomNumber} ({r.operationalStatus})</option>
                        ))}
                      </select>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {/* ── Step 3: Confirm ── */}
          {step === 'confirm' && (
            <div className="space-y-3">
              <div className="bg-gray-50 rounded-lg p-4 space-y-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-gray-500">{t('walkIn.confirmGuest')}</span>
                  <span className="font-medium text-gray-800">{guestName}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-500">{t('walkIn.confirmRoom')}</span>
                  <span className="font-medium text-gray-800">
                    {t('walkIn.room')} {selectedRoom?.roomNumber} · {selectedRoomType?.name}
                  </span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-500">{t('walkIn.confirmCheckIn')}</span>
                  <span className="font-medium text-gray-800">{checkInDate}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-500">{t('walkIn.confirmCheckOut')}</span>
                  <span className="font-medium text-gray-800">{checkOutDate} ({nights} {t('common.nights')})</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-500">{t('walkIn.confirmOccupants')}</span>
                  <span className="font-medium text-gray-800">{adults} / {children}</span>
                </div>
                <div className="flex justify-between pt-2 border-t font-semibold">
                  <span className="text-gray-700">{t('walkIn.confirmTotal')}</span>
                  <span className="text-blue-700 text-base">{totalAmount.toLocaleString('th-TH')} {t('common.baht')}</span>
                </div>
              </div>
              <p className="text-xs text-gray-500">{t('walkIn.confirmHint')}</p>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="px-5 py-4 border-t flex items-center justify-between">
          <button
            onClick={() => {
              if (step === 'stay') setStep('guest');
              else if (step === 'confirm') setStep('stay');
              else onClose();
            }}
            className="text-sm text-gray-500 hover:text-gray-700"
          >
            {step === 'guest' ? t('walkIn.cancelBtn') : t('walkIn.backBtn')}
          </button>

          {step === 'guest' && (
            <button onClick={goToStay}
              className="bg-blue-600 text-white px-5 py-2 rounded text-sm font-medium hover:bg-blue-700">
              {t('walkIn.nextBtn')}
            </button>
          )}
          {step === 'stay' && (
            <button onClick={goToConfirm}
              disabled={!selectedRoomId || nights < 1}
              className="bg-blue-600 text-white px-5 py-2 rounded text-sm font-medium hover:bg-blue-700 disabled:opacity-50">
              {t('walkIn.nextBtn')}
            </button>
          )}
          {step === 'confirm' && (
            <button onClick={handleCheckIn} disabled={busy}
              className="bg-green-600 text-white px-6 py-2 rounded text-sm font-bold hover:bg-green-700 disabled:opacity-50">
              {busy ? t('walkIn.checkingIn') : t('walkIn.confirmBtn')}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
