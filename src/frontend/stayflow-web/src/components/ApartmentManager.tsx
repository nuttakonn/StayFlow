import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
  FileText,
  Zap,
  Droplets,
  CheckCircle2,
  AlertCircle,
  PlusCircle,
  LogOut,
  Building,
  DollarSign
} from 'lucide-react';
import {
  PropertyDto,
  RoomDto,
  TenantDto,
  RentalAgreementDto,
  MeterReadingDto,
  MonthlyInvoiceDto
} from '../types';

interface ApartmentManagerProps {
  property: PropertyDto;
  token: string;
  orgId: string;
  rooms: RoomDto[];
  onRefreshRooms?: () => void;
}

export const ApartmentManager: React.FC<ApartmentManagerProps> = ({
  property,
  token,
  orgId,
  rooms,
  onRefreshRooms
}) => {
  const { t } = useTranslation();
  const [subTab, setSubTab] = useState<'agreements' | 'meters' | 'invoices'>('agreements');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Data states
  const [tenants, setTenants] = useState<TenantDto[]>([]);
  const [agreements, setAgreements] = useState<RentalAgreementDto[]>([]);
  const [meterReadings, setMeterReadings] = useState<MeterReadingDto[]>([]);
  const [invoices, setInvoices] = useState<MonthlyInvoiceDto[]>([]);

  // Filter states
  const [selectedMonth, setSelectedMonth] = useState<string>(() => {
    const now = new Date();
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
  });

  // Modals
  const [showCreateAgreementModal, setShowCreateAgreementModal] = useState(false);
  const [showMeterModal, setShowMeterModal] = useState(false);
  const [showMoveOutModal, setShowMoveOutModal] = useState(false);
  const [selectedAgreementForMoveOut, setSelectedAgreementForMoveOut] = useState<RentalAgreementDto | null>(null);
  const [selectedInvoiceForDetail, setSelectedInvoiceForDetail] = useState<MonthlyInvoiceDto | null>(null);

  // Form states - Create Agreement
  const [newTenantName, setNewTenantName] = useState('');
  const [newTenantPhone, setNewTenantPhone] = useState('');
  const [idDocType, setIdDocType] = useState<'thai_id' | 'passport'>('thai_id');
  const [newTenantIdCard, setNewTenantIdCard] = useState('');
  const [selectedRoomId, setSelectedRoomId] = useState('');
  const [agreementStartDate, setAgreementStartDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [agreementEndDate, setAgreementEndDate] = useState(() => {
    const d = new Date();
    d.setMonth(d.getMonth() + 6);
    return d.toISOString().slice(0, 10);
  });
  const [agreementRent, setAgreementRent] = useState<number>(5500);
  const [agreementDeposit, setAgreementDeposit] = useState<number>(11000);
  const [agreementCycleDay, setAgreementCycleDay] = useState<number>(25);
  const [agreementNotes, setAgreementNotes] = useState('');

  // Form states - Meter Reading
  const [meterRoomId, setMeterRoomId] = useState('');
  const [meterType, setMeterType] = useState<'Electricity' | 'Water'>('Electricity');
  const [meterPrev, setMeterPrev] = useState<number>(0);
  const [meterCurr, setMeterCurr] = useState<number>(0);
  const [meterPrice, setMeterPrice] = useState<number>(7);
  const [meterNotes, setMeterNotes] = useState('');

  // Form states - Move Out Settlement
  const [moveOutReason, setMoveOutReason] = useState('Normal Move-Out (End of Lease)');
  const [moveOutDeduction, setMoveOutDeduction] = useState<number>(500);
  const [moveOutRefund, setMoveOutRefund] = useState<number>(10500);

  // Form states - Invoice Payment
  const [invoicePayAmount, setInvoicePayAmount] = useState<number>(0);
  const [invoicePayMethod, setInvoicePayMethod] = useState('PromptPay');
  const [invoicePayRef, setInvoicePayRef] = useState('');

  const headers = {
    'Authorization': `Bearer ${token}`,
    'X-Organization-Id': orgId,
    'Content-Type': 'application/json'
  };

  useEffect(() => {
    loadAllData();
  }, [property.id, selectedMonth]);

  const loadAllData = async () => {
    setLoading(true);
    setError(null);
    try {
      await Promise.all([
        fetchTenants(),
        fetchAgreements(),
        fetchMeterReadings(),
        fetchInvoices()
      ]);
    } catch (err: any) {
      console.error(err);
      setError(t('apartment.loadError'));
    } finally {
      setLoading(false);
    }
  };

  const fetchTenants = async () => {
    const res = await fetch(`/api/v1/properties/${property.id}/tenants`, { headers });
    if (res.ok) setTenants(await res.json());
  };

  const fetchAgreements = async () => {
    const res = await fetch(`/api/v1/properties/${property.id}/agreements`, { headers });
    if (res.ok) setAgreements(await res.json());
  };

  const fetchMeterReadings = async () => {
    const res = await fetch(`/api/v1/properties/${property.id}/meter-readings?billingMonth=${selectedMonth}`, { headers });
    if (res.ok) setMeterReadings(await res.json());
  };

  const fetchInvoices = async () => {
    const res = await fetch(`/api/v1/properties/${property.id}/invoices?billingMonth=${selectedMonth}`, { headers });
    if (res.ok) setInvoices(await res.json());
  };

  // Helper when selecting room for meter reading to fetch latest reading
  const handleSelectMeterRoom = async (roomId: string) => {
    setMeterRoomId(roomId);
    if (!roomId) return;
    try {
      const res = await fetch(`/api/v1/properties/${property.id}/rooms/${roomId}/meter-readings/latest`, { headers });
      if (res.ok) {
        const data = await res.json();
        if (meterType === 'Electricity') {
          setMeterPrev(data.latestElectricityReading ?? 0);
          setMeterCurr((data.latestElectricityReading ?? 0) + 50);
          setMeterPrice(7);
        } else {
          setMeterPrev(data.latestWaterReading ?? 0);
          setMeterCurr((data.latestWaterReading ?? 0) + 10);
          setMeterPrice(18);
        }
      }
    } catch (e) {
      console.error(e);
    }
  };

  // Actions
  const handleCreateAgreement = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTenantName || !newTenantPhone || !selectedRoomId) {
      alert('Please enter tenant information and select a room');
      return;
    }
    setLoading(true);
    try {
      // 1. Create or get tenant
      let tenantId = '';
      const existing = tenants.find(t => t.phoneNumber === newTenantPhone);
      if (existing) {
        tenantId = existing.id;
      } else {
        const tRes = await fetch(`/api/v1/properties/${property.id}/tenants`, {
          method: 'POST',
          headers,
          body: JSON.stringify({
            fullName: newTenantName,
            phoneNumber: newTenantPhone,
            nationalIdOrPassport: newTenantIdCard
          })
        });
        if (!tRes.ok) throw new Error(t('apartment.createTenantError'));
        const tData = await tRes.json();
        tenantId = tData.id;
      }

      // 2. Create Agreement
      const agrRes = await fetch(`/api/v1/properties/${property.id}/agreements`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          tenantId,
          roomId: selectedRoomId,
          startDate: agreementStartDate,
          endDate: agreementEndDate,
          monthlyRent: agreementRent,
          securityDeposit: agreementDeposit,
          billingCycleDay: agreementCycleDay,
          notes: agreementNotes
        })
      });

      if (!agrRes.ok) {
        const errJson = await agrRes.json();
        throw new Error(errJson.message || t('apartment.overlapError'));
      }

      const agrData = await agrRes.json();

      // 3. Record deposit & activate automatically
      if (agreementDeposit > 0) {
        await fetch(`/api/v1/agreements/${agrData.id}/deposit`, {
          method: 'POST',
          headers,
          body: JSON.stringify({ amount: agreementDeposit })
        });
      }

      await fetch(`/api/v1/agreements/${agrData.id}/activate`, {
        method: 'POST',
        headers
      });

      setSuccessMsg(t('apartment.createAgreementSuccess'));
      setShowCreateAgreementModal(false);
      await loadAllData();
      onRefreshRooms?.();
    } catch (err: any) {
      alert(err.message);
    } finally {
      setLoading(false);
    }
  };

  const handleRecordMeter = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!meterRoomId) return;
    setLoading(true);
    try {
      const res = await fetch(`/api/v1/properties/${property.id}/meter-readings`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          roomId: meterRoomId,
          utilityType: meterType,
          readingDate: new Date().toISOString().slice(0, 10),
          billingMonth: selectedMonth,
          previousReading: meterPrev,
          currentReading: meterCurr,
          unitPrice: meterPrice,
          notes: meterNotes
        })
      });

      if (!res.ok) {
        const errJson = await res.json();
        throw new Error(errJson.message || t('apartment.meterError'));
      }

      setSuccessMsg(t('apartment.meterSuccess', { type: meterType }));
      setShowMeterModal(false);
      await fetchMeterReadings();
    } catch (err: any) {
      alert(err.message);
    } finally {
      setLoading(false);
    }
  };

  const handleGenerateInvoices = async () => {
    if (!confirm(t('apartment.generateConfirm', { month: selectedMonth }))) return;
    setLoading(true);
    try {
      const d = new Date();
      d.setDate(d.getDate() + 10);
      const dueDate = d.toISOString().slice(0, 10);

      const res = await fetch(`/api/v1/properties/${property.id}/invoices/generate-monthly`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          billingMonth: selectedMonth,
          dueDate
        })
      });

      if (res.ok) {
        const data = await res.json();
        setSuccessMsg(t('apartment.generateSuccess', { count: data.generatedCount, skipped: data.skippedCount }));
        await fetchInvoices();
      } else {
        const err = await res.json();
        alert(err.message || 'An error occurred');
      }
    } catch (e) {
      console.error(e);
      alert('A connection error occurred');
    } finally {
      setLoading(false);
    }
  };

  const handleMoveOutSettlement = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedAgreementForMoveOut) return;
    setLoading(true);
    try {
      const res = await fetch(`/api/v1/agreements/${selectedAgreementForMoveOut.id}/terminate`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          reason: moveOutReason,
          refundAmount: moveOutRefund,
          deductionAmount: moveOutDeduction
        })
      });

      if (!res.ok) {
        const err = await res.json();
        throw new Error(err.message || t('apartment.moveOutError'));
      }

      setSuccessMsg(t('apartment.moveOutSuccess'));
      setShowMoveOutModal(false);
      setSelectedAgreementForMoveOut(null);
      await loadAllData();
      onRefreshRooms?.();
    } catch (err: any) {
      alert(err.message);
    } finally {
      setLoading(false);
    }
  };

  const handleRecordInvoicePayment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedInvoiceForDetail) return;
    setLoading(true);
    try {
      const res = await fetch(`/api/v1/invoices/${selectedInvoiceForDetail.id}/payments`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          amount: invoicePayAmount,
          method: invoicePayMethod,
          referenceNumber: invoicePayRef,
          notes: 'Monthly rent payment'
        })
      });

      if (!res.ok) {
        const err = await res.json();
        throw new Error(err.message || t('apartment.paymentError'));
      }

      setSuccessMsg(t('apartment.paymentSuccess'));

      // Refresh specific invoice and list
      const invRes = await fetch(`/api/v1/invoices/${selectedInvoiceForDetail.id}`, { headers });
      if (invRes.ok) setSelectedInvoiceForDetail(await invRes.json());
      await fetchInvoices();
    } catch (err: any) {
      alert(err.message);
    } finally {
      setLoading(false);
    }
  };

  const resetAgreementForm = () => {
    setNewTenantName('');
    setNewTenantPhone('');
    setIdDocType('thai_id');
    setNewTenantIdCard('');
    setSelectedRoomId('');
    setAgreementStartDate(new Date().toISOString().slice(0, 10));
    setAgreementEndDate(() => {
      const d = new Date();
      d.setMonth(d.getMonth() + 6);
      return d.toISOString().slice(0, 10);
    });
    setAgreementRent(5500);
    setAgreementDeposit(11000);
    setAgreementCycleDay(25);
    setAgreementNotes('');
  };

  // Summaries
  const activeAgreementsCount = agreements.filter(a => a.status === 'Active').length;
  const totalInvoiced = invoices.reduce((acc, i) => acc + i.totalAmount, 0);
  const totalPaid = invoices.reduce((acc, i) => acc + i.paidAmount, 0);
  const totalOutstanding = invoices.reduce((acc, i) => acc + i.balance, 0);

  return (
    <div className="space-y-6">
      {/* Banner / Header */}
      <div className="bg-gradient-to-r from-emerald-950 via-teal-900 to-slate-900 text-white p-6 sm:p-8 rounded-3xl shadow-xl flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="space-y-1.5">
          <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-emerald-500/20 text-emerald-300 text-xs font-bold border border-emerald-500/30">
            <Building className="w-3.5 h-3.5" />
            <span>{t('apartment.subtitle')}</span>
          </div>
          <h2 className="text-2xl sm:text-3xl font-black tracking-tight">{t('apartment.title')}</h2>
          <p className="text-xs sm:text-sm text-slate-300">
            {t('apartment.description')}
          </p>
        </div>

        {/* Counter cards */}
        <div className="flex items-center gap-3 flex-wrap">
          <div className="bg-white/10 backdrop-blur-md px-4 py-2.5 rounded-2xl border border-white/10 text-center">
            <span className="text-[10px] text-slate-300 uppercase block font-semibold">{t('apartment.activeAgreements')}</span>
            <span className="text-xl font-black text-emerald-300">{activeAgreementsCount}</span>
          </div>
          <div className="bg-white/10 backdrop-blur-md px-4 py-2.5 rounded-2xl border border-white/10 text-center">
            <span className="text-[10px] text-slate-300 uppercase block font-semibold">{t('apartment.billingMonth')} {selectedMonth}</span>
            <span className="text-xl font-black text-white">฿{totalInvoiced.toLocaleString()}</span>
          </div>
          <div className="bg-emerald-500/20 backdrop-blur-md px-4 py-2.5 rounded-2xl border border-emerald-500/30 text-center">
            <span className="text-[10px] text-emerald-200 uppercase block font-semibold">{t('apartment.totalPaid')}</span>
            <span className="text-xl font-black text-emerald-300">฿{totalPaid.toLocaleString()}</span>
          </div>
          <div className="bg-amber-500/20 backdrop-blur-md px-4 py-2.5 rounded-2xl border border-amber-500/30 text-center">
            <span className="text-[10px] text-amber-200 uppercase block font-semibold">{t('apartment.totalOutstanding')}</span>
            <span className="text-xl font-black text-amber-300">฿{totalOutstanding.toLocaleString()}</span>
          </div>
        </div>
      </div>

      {/* Notifications */}
      {successMsg && (
        <div className="p-4 bg-emerald-50 border border-emerald-200 text-emerald-800 rounded-2xl text-xs font-bold flex items-center justify-between">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 text-emerald-600" />
            <span>{successMsg}</span>
          </div>
          <button onClick={() => setSuccessMsg(null)} className="text-emerald-500 hover:text-emerald-700">✕</button>
        </div>
      )}

      {error && (
        <div className="p-4 bg-rose-50 border border-rose-200 text-rose-800 rounded-2xl text-xs font-bold flex items-center justify-between">
          <div className="flex items-center gap-2">
            <AlertCircle className="w-4 h-4 text-rose-600" />
            <span>{error}</span>
          </div>
          <button onClick={() => setError(null)} className="text-rose-500 hover:text-rose-700">✕</button>
        </div>
      )}

      {/* Navigation Sub-Tabs & Actions */}
      <div className="flex flex-col sm:flex-row items-center justify-between gap-4 bg-white p-3 rounded-2xl border border-slate-200 shadow-sm">
        <div className="flex items-center gap-2 bg-slate-100 p-1 rounded-xl text-xs font-bold overflow-x-auto shrink-0">
          <button
            onClick={() => setSubTab('agreements')}
            className={`px-4 py-2 rounded-lg flex items-center gap-2 transition ${
              subTab === 'agreements' ? 'bg-white text-emerald-700 shadow-xs' : 'text-slate-600 hover:text-slate-900'
            }`}
          >
            <FileText className="w-4 h-4" />
            <span>{t('apartment.agreements')} ({agreements.length})</span>
          </button>
          <button
            onClick={() => setSubTab('meters')}
            className={`px-4 py-2 rounded-lg flex items-center gap-2 transition ${
              subTab === 'meters' ? 'bg-white text-amber-700 shadow-xs' : 'text-slate-600 hover:text-slate-900'
            }`}
          >
            <Zap className="w-4 h-4" />
            <span>{t('apartment.meters')} ({meterReadings.length})</span>
          </button>
          <button
            onClick={() => setSubTab('invoices')}
            className={`px-4 py-2 rounded-lg flex items-center gap-2 transition ${
              subTab === 'invoices' ? 'bg-white text-blue-700 shadow-xs' : 'text-slate-600 hover:text-slate-900'
            }`}
          >
            <DollarSign className="w-4 h-4" />
            <span>{t('apartment.invoices')} ({invoices.length})</span>
          </button>
        </div>

        {/* Action Buttons depending on sub-tab */}
        <div className="flex items-center gap-2 w-full sm:w-auto">
          {subTab === 'agreements' && (
            <button
              onClick={() => { resetAgreementForm(); setShowCreateAgreementModal(true); }}
              className="w-full sm:w-auto px-4 py-2 bg-emerald-600 hover:bg-emerald-700 text-white rounded-xl text-xs font-bold flex items-center justify-center gap-1.5 shadow-sm transition"
            >
              <PlusCircle className="w-4 h-4" />
              <span>{t('apartment.newAgreement')}</span>
            </button>
          )}

          {subTab === 'meters' && (
            <div className="flex items-center gap-2">
              <input
                type="month"
                value={selectedMonth}
                onChange={(e) => setSelectedMonth(e.target.value)}
                className="px-3 py-1.5 border border-slate-300 rounded-xl text-xs font-bold text-slate-700 bg-slate-50"
              />
              <button
                onClick={() => {
                  setMeterRoomId(rooms[0]?.id || '');
                  handleSelectMeterRoom(rooms[0]?.id || '');
                  setShowMeterModal(true);
                }}
                className="px-4 py-2 bg-amber-600 hover:bg-amber-700 text-white rounded-xl text-xs font-bold flex items-center gap-1.5 shadow-sm transition"
              >
                <PlusCircle className="w-4 h-4" />
                <span>{t('apartment.recordMeter')}</span>
              </button>
            </div>
          )}

          {subTab === 'invoices' && (
            <div className="flex items-center gap-2">
              <input
                type="month"
                value={selectedMonth}
                onChange={(e) => setSelectedMonth(e.target.value)}
                className="px-3 py-1.5 border border-slate-300 rounded-xl text-xs font-bold text-slate-700 bg-slate-50"
              />
              <button
                onClick={handleGenerateInvoices}
                className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-xl text-xs font-bold flex items-center gap-1.5 shadow-sm transition"
              >
                <Zap className="w-4 h-4" />
                <span>⚡ {t('apartment.generateInvoices')}</span>
              </button>
            </div>
          )}
        </div>
      </div>

      {/* ── SubTab 1: Rental Agreements ──────────────────────────────────── */}
      {subTab === 'agreements' && (
        <div className="bg-white rounded-2xl border border-slate-200 shadow-sm overflow-hidden">
          <div className="p-4 border-b border-slate-100 flex items-center justify-between">
            <h3 className="font-bold text-slate-900 text-sm">{t('apartment.agreementsTitle')}</h3>
            <span className="text-xs text-slate-500">{t('apartment.agreementsSubtitle')}</span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-600">
              <thead className="bg-slate-50 text-[11px] uppercase tracking-wider text-slate-500 border-b border-slate-200">
                <tr>
                  <th className="py-3 px-4">{t('apartment.agreementNumber')}</th>
                  <th className="py-3 px-4">{t('apartment.room')}</th>
                  <th className="py-3 px-4">{t('apartment.tenant')}</th>
                  <th className="py-3 px-4">{t('apartment.period')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.monthlyRent')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.deposit')}</th>
                  <th className="py-3 px-4 text-center">{t('apartment.agreementStatus')}</th>
                  <th className="py-3 px-4 text-center">{t('apartment.manage')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 font-medium">
                {agreements.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="text-center py-8 text-slate-400">
                      {t('apartment.noAgreements')}
                    </td>
                  </tr>
                ) : (
                  agreements.map((agr) => (
                    <tr key={agr.id} className="hover:bg-slate-50 transition">
                      <td className="py-3 px-4 font-bold text-slate-900">{agr.agreementNumber}</td>
                      <td className="py-3 px-4">
                        <span className="px-2.5 py-1 rounded-lg bg-slate-100 text-slate-800 font-bold">
                          {t('apartment.room')} {agr.roomNumber || '—'}
                        </span>
                      </td>
                      <td className="py-3 px-4">
                        <div className="font-bold text-slate-900">{agr.tenantName || '—'}</div>
                        <div className="text-[11px] text-slate-400">{agr.tenantPhone || '—'}</div>
                      </td>
                      <td className="py-3 px-4 text-slate-500">
                        {agr.startDate} – {agr.endDate}
                      </td>
                      <td className="py-3 px-4 text-right font-bold text-slate-900">
                        ฿{agr.monthlyRent.toLocaleString()}
                      </td>
                      <td className="py-3 px-4 text-right">
                        <span className="font-bold text-emerald-600">฿{agr.depositPaid.toLocaleString()}</span>
                        <span className="text-[10px] text-slate-400 block">/ ฿{agr.securityDeposit.toLocaleString()}</span>
                      </td>
                      <td className="py-3 px-4 text-center">
                        <span className={`px-2.5 py-1 rounded-full text-[10px] font-bold ${
                          agr.status === 'Active' ? 'bg-emerald-100 text-emerald-700 border border-emerald-200' :
                          agr.status === 'Terminated' ? 'bg-slate-100 text-slate-600' :
                          'bg-amber-100 text-amber-700'
                        }`}>
                          {agr.status === 'Active' ? t('apartment.statusActive') :
                           agr.status === 'Terminated' ? t('apartment.statusTerminated') : t('apartment.statusDraft')}
                        </span>
                      </td>
                      <td className="py-3 px-4 text-center">
                        {agr.status === 'Active' ? (
                          <button
                            onClick={() => {
                              setSelectedAgreementForMoveOut(agr);
                              setMoveOutRefund(agr.depositPaid - 500);
                              setMoveOutDeduction(500);
                              setShowMoveOutModal(true);
                            }}
                            className="px-3 py-1 bg-rose-50 hover:bg-rose-100 text-rose-700 border border-rose-200 rounded-lg text-xs font-bold transition flex items-center gap-1 mx-auto"
                          >
                            <LogOut className="w-3.5 h-3.5" />
                            <span>{t('apartment.moveOut')}</span>
                          </button>
                        ) : (
                          <span className="text-[11px] text-slate-400">{t('apartment.cleared')}</span>
                        )}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── SubTab 2: Meter Readings ─────────────────────────────────────── */}
      {subTab === 'meters' && (
        <div className="bg-white rounded-2xl border border-slate-200 shadow-sm overflow-hidden">
          <div className="p-4 border-b border-slate-100 flex items-center justify-between">
            <h3 className="font-bold text-slate-900 text-sm">{t('apartment.metersTitle', { month: selectedMonth })}</h3>
            <span className="text-xs text-slate-500">{t('apartment.metersSubtitle')}</span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-600">
              <thead className="bg-slate-50 text-[11px] uppercase tracking-wider text-slate-500 border-b border-slate-200">
                <tr>
                  <th className="py-3 px-4">{t('apartment.meterRoom')}</th>
                  <th className="py-3 px-4">{t('apartment.meterType')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.meterPrev')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.meterCurrent')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.meterUnits')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.meterUnitPrice')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.meterTotal')}</th>
                  <th className="py-3 px-4">{t('apartment.meterDate')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 font-medium">
                {meterReadings.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="text-center py-8 text-slate-400">
                      {t('apartment.noMeters', { month: selectedMonth })}
                    </td>
                  </tr>
                ) : (
                  meterReadings.map((m) => (
                    <tr key={m.id} className="hover:bg-slate-50 transition">
                      <td className="py-3 px-4 font-bold text-slate-900">
                        <span className="px-2.5 py-1 rounded-lg bg-slate-100 text-slate-800">
                          {t('apartment.room')} {m.roomNumber || '—'}
                        </span>
                      </td>
                      <td className="py-3 px-4 font-bold">
                        {m.utilityType === 'Electricity' ? (
                          <span className="inline-flex items-center gap-1 text-amber-600">
                            <Zap className="w-3.5 h-3.5" /> {t('apartment.meterElectricity')}
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 text-blue-600">
                            <Droplets className="w-3.5 h-3.5" /> {t('apartment.meterWater')}
                          </span>
                        )}
                      </td>
                      <td className="py-3 px-4 text-right text-slate-500">{m.previousReading.toLocaleString()}</td>
                      <td className="py-3 px-4 text-right font-bold text-slate-900">{m.currentReading.toLocaleString()}</td>
                      <td className="py-3 px-4 text-right font-bold text-indigo-600">{m.unitsUsed.toLocaleString()} {t('apartment.unitsUsed')}</td>
                      <td className="py-3 px-4 text-right text-slate-500">฿{m.unitPrice.toFixed(2)}</td>
                      <td className="py-3 px-4 text-right font-bold text-emerald-600">฿{m.totalAmount.toLocaleString()}</td>
                      <td className="py-3 px-4 text-slate-400">{m.readingDate}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── SubTab 3: Monthly Invoices ───────────────────────────────────── */}
      {subTab === 'invoices' && (
        <div className="bg-white rounded-2xl border border-slate-200 shadow-sm overflow-hidden">
          <div className="p-4 border-b border-slate-100 flex items-center justify-between">
            <h3 className="font-bold text-slate-900 text-sm">{t('apartment.invoicesTitle', { month: selectedMonth })}</h3>
            <span className="text-xs text-slate-500">{t('apartment.invoicesSubtitle')}</span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-600">
              <thead className="bg-slate-50 text-[11px] uppercase tracking-wider text-slate-500 border-b border-slate-200">
                <tr>
                  <th className="py-3 px-4">{t('apartment.invoiceNumber')}</th>
                  <th className="py-3 px-4">{t('apartment.room')}</th>
                  <th className="py-3 px-4">{t('apartment.tenant')}</th>
                  <th className="py-3 px-4">{t('apartment.dueDate')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.invoiceTotal')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.invoicePaid')}</th>
                  <th className="py-3 px-4 text-right">{t('apartment.invoiceBalance')}</th>
                  <th className="py-3 px-4 text-center">{t('apartment.invoiceStatus')}</th>
                  <th className="py-3 px-4 text-center">{t('apartment.manage')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 font-medium">
                {invoices.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="text-center py-8 text-slate-400">
                      {t('apartment.noInvoices', { month: selectedMonth })}
                    </td>
                  </tr>
                ) : (
                  invoices.map((inv) => (
                    <tr key={inv.id} className="hover:bg-slate-50 transition">
                      <td className="py-3 px-4 font-bold text-slate-900">{inv.invoiceNumber}</td>
                      <td className="py-3 px-4">
                        <span className="px-2.5 py-1 rounded-lg bg-slate-100 font-bold text-slate-800">
                          {t('apartment.room')} {inv.roomNumber || '—'}
                        </span>
                      </td>
                      <td className="py-3 px-4 font-bold text-slate-900">{inv.tenantName || '—'}</td>
                      <td className="py-3 px-4 text-slate-500">{inv.dueDate}</td>
                      <td className="py-3 px-4 text-right font-bold text-slate-900">
                        ฿{inv.totalAmount.toLocaleString()}
                      </td>
                      <td className="py-3 px-4 text-right text-emerald-600 font-bold">
                        ฿{inv.paidAmount.toLocaleString()}
                      </td>
                      <td className="py-3 px-4 text-right font-black text-rose-600">
                        ฿{inv.balance.toLocaleString()}
                      </td>
                      <td className="py-3 px-4 text-center">
                        <span className={`px-2.5 py-1 rounded-full text-[10px] font-bold ${
                          inv.status === 'Paid' ? 'bg-emerald-100 text-emerald-700' :
                          inv.status === 'PartiallyPaid' ? 'bg-amber-100 text-amber-700' :
                          'bg-blue-100 text-blue-700'
                        }`}>
                          {inv.status === 'Paid' ? t('apartment.invoiceStatusPaid') :
                           inv.status === 'PartiallyPaid' ? t('apartment.invoiceStatusPartial') : t('apartment.invoiceStatusPending')}
                        </span>
                      </td>
                      <td className="py-3 px-4 text-center">
                        <button
                          onClick={() => {
                            setSelectedInvoiceForDetail(inv);
                            setInvoicePayAmount(inv.balance);
                          }}
                          className="px-3 py-1 bg-slate-100 hover:bg-blue-50 text-slate-700 hover:text-blue-600 rounded-lg text-xs font-bold transition"
                        >
                          {t('apartment.viewDetail')}
                        </button>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── Modal: Create Rental Agreement ───────────────────────────────── */}
      {showCreateAgreementModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm">
          <div className="bg-white rounded-3xl shadow-2xl max-w-lg w-full p-6 space-y-4 border border-slate-200">
            <div className="flex items-center justify-between border-b border-slate-100 pb-3">
              <h3 className="text-base font-bold text-slate-900">{t('apartment.createAgreementTitle')}</h3>
              <button onClick={() => { setShowCreateAgreementModal(false); resetAgreementForm(); }} className="text-slate-400 hover:text-slate-600">✕</button>
            </div>

            <form onSubmit={handleCreateAgreement} className="space-y-4">
              <div className="space-y-2">
                <span className="text-xs font-bold text-slate-700">{t('apartment.tenantSection')}</span>
                <div className="grid grid-cols-2 gap-2">
                  <input
                    type="text"
                    required
                    placeholder={t('apartment.tenantNamePlaceholder')}
                    value={newTenantName}
                    onChange={(e) => setNewTenantName(e.target.value)}
                    className="px-3 py-2 border border-slate-300 rounded-xl text-xs"
                  />
                  <input
                    type="tel"
                    inputMode="tel"
                    required
                    placeholder={t('apartment.tenantPhonePlaceholder')}
                    value={newTenantPhone}
                    onChange={(e) => {
                      const val = e.target.value.replace(/[^\d+\-\s]/g, '');
                      setNewTenantPhone(val);
                    }}
                    pattern="[0-9+\-\s]{8,20}"
                    maxLength={20}
                    className="px-3 py-2 border border-slate-300 rounded-xl text-xs"
                  />
                </div>
                <div>
                  <div className="flex items-center gap-3 mb-1.5 text-[11px]">
                    <label className="flex items-center gap-1 cursor-pointer font-semibold text-slate-700">
                      <input
                        type="radio"
                        name="idDocType"
                        checked={idDocType === 'thai_id'}
                        onChange={() => {
                          setIdDocType('thai_id');
                          setNewTenantIdCard('');
                        }}
                      />
                      <span>🪪 {t('apartment.thaiId')}</span>
                    </label>
                    <label className="flex items-center gap-1 cursor-pointer font-semibold text-slate-700">
                      <input
                        type="radio"
                        name="idDocType"
                        checked={idDocType === 'passport'}
                        onChange={() => {
                          setIdDocType('passport');
                          setNewTenantIdCard('');
                        }}
                      />
                      <span>🌐 {t('apartment.passport')}</span>
                    </label>
                  </div>

                  {idDocType === 'thai_id' ? (
                    <input
                      type="text"
                      maxLength={13}
                      placeholder={t('apartment.thaiIdPlaceholder')}
                      value={newTenantIdCard}
                      onChange={(e) => {
                        const val = e.target.value.replace(/\D/g, '');
                        setNewTenantIdCard(val);
                      }}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs font-mono"
                    />
                  ) : (
                    <input
                      type="text"
                      maxLength={20}
                      placeholder={t('apartment.passportPlaceholder')}
                      value={newTenantIdCard}
                      onChange={(e) => {
                        setNewTenantIdCard(e.target.value.toUpperCase());
                      }}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs uppercase font-mono font-bold"
                    />
                  )}
                </div>
              </div>

              <div className="space-y-2 pt-2 border-t border-slate-100">
                <span className="text-xs font-bold text-slate-700">{t('apartment.roomSection')}</span>
                <select
                  required
                  value={selectedRoomId}
                  onChange={(e) => {
                    setSelectedRoomId(e.target.value);
                    const rm = rooms.find(r => r.id === e.target.value);
                    if (rm && rm.baseRate) {
                      setAgreementRent(rm.baseRate);
                      setAgreementDeposit(rm.baseRate * 2);
                    }
                  }}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs bg-white font-bold text-slate-800"
                >
                  <option value="">{t('apartment.selectRoomPrompt')}</option>
                  {rooms.map(r => {
                    const hasActive = agreements.some(a => a.roomId === r.id && a.status === 'Active');
                    return (
                      <option key={r.id} value={r.id} disabled={hasActive}>
                        {t('apartment.room')} {r.roomNumber} ({r.roomTypeName}) - ฿{r.baseRate.toLocaleString()}{t('apartment.perMonth')}{hasActive ? ` 🔒 ${t('apartment.roomOccupied')}` : ''}
                      </option>
                    );
                  })}
                </select>

                <div className="grid grid-cols-2 gap-2">
                  <div>
                    <label className="text-[11px] text-slate-500 block mb-1">{t('apartment.agreementStartDate')}</label>
                    <input
                      type="date"
                      required
                      value={agreementStartDate}
                      onChange={(e) => setAgreementStartDate(e.target.value)}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs"
                    />
                  </div>
                  <div>
                    <label className="text-[11px] text-slate-500 block mb-1">{t('apartment.agreementEndDate')}</label>
                    <input
                      type="date"
                      required
                      value={agreementEndDate}
                      onChange={(e) => setAgreementEndDate(e.target.value)}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs"
                    />
                  </div>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
                  <div>
                    <label className="text-[11px] text-slate-500 block mb-1">{t('apartment.rentLabel')}</label>
                    <input
                      type="number"
                      required
                      value={agreementRent}
                      onChange={(e) => setAgreementRent(Number(e.target.value))}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs font-bold text-slate-800"
                    />
                  </div>
                  <div>
                    <label className="text-[11px] text-slate-500 block mb-1">{t('apartment.depositLabel')}</label>
                    <input
                      type="number"
                      required
                      value={agreementDeposit}
                      onChange={(e) => setAgreementDeposit(Number(e.target.value))}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs font-bold text-emerald-700"
                    />
                  </div>
                  <div>
                    <label className="text-[11px] text-slate-500 block mb-1">{t('apartment.billingCycleDay')}</label>
                    <input
                      type="number"
                      min={1}
                      max={31}
                      required
                      value={agreementCycleDay}
                      onChange={(e) => setAgreementCycleDay(Number(e.target.value))}
                      className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs font-bold"
                    />
                  </div>
                </div>

                <textarea
                  rows={2}
                  placeholder={t('apartment.notesPlaceholder')}
                  value={agreementNotes}
                  onChange={(e) => setAgreementNotes(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs"
                />
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => { setShowCreateAgreementModal(false); resetAgreementForm(); }}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 rounded-xl hover:bg-slate-200"
                >
                  {t('common.cancel')}
                </button>
                <button
                  type="submit"
                  disabled={loading}
                  className="px-5 py-2 text-xs font-bold text-white bg-emerald-600 rounded-xl hover:bg-emerald-700 shadow-md"
                >
                  {loading ? t('apartment.savingAgreement') : t('apartment.saveAgreement')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ── Modal: Record Meter Reading ──────────────────────────────────── */}
      {showMeterModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm">
          <div className="bg-white rounded-3xl shadow-2xl max-w-md w-full p-6 space-y-4 border border-slate-200">
            <div className="flex items-center justify-between border-b border-slate-100 pb-3">
              <h3 className="text-base font-bold text-slate-900">{t('apartment.recordMeterTitle', { month: selectedMonth })}</h3>
              <button onClick={() => setShowMeterModal(false)} className="text-slate-400 hover:text-slate-600">✕</button>
            </div>

            <form onSubmit={handleRecordMeter} className="space-y-4">
              <div>
                <label className="text-xs font-bold text-slate-700 block mb-1">{t('apartment.selectRoomLabel')}</label>
                <select
                  required
                  value={meterRoomId}
                  onChange={(e) => handleSelectMeterRoom(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs font-bold bg-white"
                >
                  {rooms.map(r => (
                    <option key={r.id} value={r.id}>
                      {t('apartment.room')} {r.roomNumber} ({r.roomTypeName})
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="text-xs font-bold text-slate-700 block mb-1">{t('apartment.meterTypeLabel')}</label>
                <div className="grid grid-cols-2 gap-2">
                  <button
                    type="button"
                    onClick={() => {
                      setMeterType('Electricity');
                      setMeterPrice(7);
                      handleSelectMeterRoom(meterRoomId);
                    }}
                    className={`py-2 rounded-xl text-xs font-bold flex items-center justify-center gap-1.5 border transition ${
                      meterType === 'Electricity' ? 'bg-amber-500 text-white border-amber-600 shadow-xs' : 'bg-slate-50 text-slate-700 border-slate-200'
                    }`}
                  >
                    <Zap className="w-4 h-4" />
                    <span>⚡ {t('apartment.electricityBtn')}</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setMeterType('Water');
                      setMeterPrice(18);
                      handleSelectMeterRoom(meterRoomId);
                    }}
                    className={`py-2 rounded-xl text-xs font-bold flex items-center justify-center gap-1.5 border transition ${
                      meterType === 'Water' ? 'bg-blue-600 text-white border-blue-700 shadow-xs' : 'bg-slate-50 text-slate-700 border-slate-200'
                    }`}
                  >
                    <Droplets className="w-4 h-4" />
                    <span>💧 {t('apartment.waterBtn')}</span>
                  </button>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="text-[11px] text-slate-500 block mb-1">{t('apartment.prevReading')}</label>
                  <input
                    type="number"
                    step="any"
                    required
                    value={meterPrev}
                    onChange={(e) => setMeterPrev(Number(e.target.value))}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs bg-slate-50 text-slate-600 font-bold"
                  />
                </div>
                <div>
                  <label className="text-[11px] text-slate-700 font-bold block mb-1">{t('apartment.currentReading')}</label>
                  <input
                    type="number"
                    step="any"
                    required
                    value={meterCurr}
                    onChange={(e) => setMeterCurr(Number(e.target.value))}
                    className="w-full px-3 py-2 border border-indigo-400 rounded-xl text-xs font-black text-indigo-700"
                  />
                </div>
              </div>

              {/* Real-time Units & Total calculation */}
              <div className="p-3 bg-slate-50 border border-slate-200 rounded-xl flex items-center justify-between text-xs">
                <div>
                  <span className="text-slate-500 block">{t('apartment.unitsLabel')}</span>
                  <span className="font-bold text-slate-900">{Math.max(0, meterCurr - meterPrev)} {t('apartment.unitsUsed')}</span>
                </div>
                <div className="text-right">
                  <span className="text-slate-500 block">{t('apartment.totalLabel')}</span>
                  <span className="font-black text-emerald-600 text-sm">
                    ฿{(Math.max(0, meterCurr - meterPrev) * meterPrice).toLocaleString()}
                  </span>
                </div>
              </div>

              <div>
                <label className="text-[11px] text-slate-500 block mb-1">{t('common.notes')}</label>
                <input
                  type="text"
                  placeholder={t('apartment.meterNotesPlaceholder')}
                  value={meterNotes}
                  onChange={(e) => setMeterNotes(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs"
                />
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setShowMeterModal(false)}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 rounded-xl hover:bg-slate-200"
                >
                  {t('common.cancel')}
                </button>
                <button
                  type="submit"
                  disabled={loading}
                  className="px-5 py-2 text-xs font-bold text-white bg-amber-600 rounded-xl hover:bg-amber-700 shadow-md"
                >
                  {t('apartment.recordMeterBtn')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ── Modal: Move Out Settlement ───────────────────────────────────── */}
      {showMoveOutModal && selectedAgreementForMoveOut && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm">
          <div className="bg-white rounded-3xl shadow-2xl max-w-md w-full p-6 space-y-4 border border-slate-200">
            <div className="flex items-center justify-between border-b border-slate-100 pb-3">
              <h3 className="text-base font-bold text-slate-900">{t('apartment.moveOutTitle')}</h3>
              <button onClick={() => setShowMoveOutModal(false)} className="text-slate-400 hover:text-slate-600">✕</button>
            </div>

            <form onSubmit={handleMoveOutSettlement} className="space-y-4">
              <div className="p-3 bg-slate-50 rounded-2xl border border-slate-100 space-y-1 text-xs">
                <div className="flex justify-between">
                  <span className="text-slate-500">{t('apartment.moveOutRoom')}:</span>
                  <span className="font-bold text-slate-900">{t('apartment.room')} {selectedAgreementForMoveOut.roomNumber}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-500">{t('apartment.moveOutTenant')}:</span>
                  <span className="font-bold text-slate-900">{selectedAgreementForMoveOut.tenantName}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-500">{t('apartment.moveOutDeposit')}:</span>
                  <span className="font-bold text-emerald-600">฿{selectedAgreementForMoveOut.depositPaid.toLocaleString()}</span>
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-700 block mb-1">{t('apartment.moveOutReason')}</label>
                <input
                  type="text"
                  required
                  value={moveOutReason}
                  onChange={(e) => setMoveOutReason(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs"
                />
              </div>

              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="text-[11px] text-slate-500 block mb-1">{t('apartment.moveOutDeduction')}</label>
                  <input
                    type="number"
                    value={moveOutDeduction}
                    onChange={(e) => {
                      const ded = Number(e.target.value);
                      setMoveOutDeduction(ded);
                      setMoveOutRefund(Math.max(0, selectedAgreementForMoveOut.depositPaid - ded));
                    }}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs font-bold text-rose-600"
                  />
                </div>
                <div>
                  <label className="text-[11px] text-slate-500 block mb-1">{t('apartment.moveOutRefund')}</label>
                  <input
                    type="number"
                    value={moveOutRefund}
                    onChange={(e) => setMoveOutRefund(Number(e.target.value))}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs font-bold text-emerald-600"
                  />
                </div>
              </div>

              <div className="p-3 bg-amber-50 border border-amber-200 rounded-xl text-xs text-amber-800 leading-relaxed">
                ⚠️ {t('apartment.moveOutWarning')}
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setShowMoveOutModal(false)}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 rounded-xl hover:bg-slate-200"
                >
                  {t('common.cancel')}
                </button>
                <button
                  type="submit"
                  disabled={loading}
                  className="px-5 py-2 text-xs font-bold text-white bg-rose-600 rounded-xl hover:bg-rose-700 shadow-md"
                >
                  {t('apartment.confirmMoveOut')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ── Modal: Invoice Detail & Payment ──────────────────────────────── */}
      {selectedInvoiceForDetail && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm">
          <div className="bg-white rounded-3xl shadow-2xl max-w-lg w-full p-6 space-y-4 border border-slate-200 max-h-[90vh] overflow-y-auto">
            <div className="flex items-center justify-between border-b border-slate-100 pb-3">
              <div>
                <h3 className="text-base font-bold text-slate-900">{t('apartment.invoiceDetailTitle', { number: selectedInvoiceForDetail.invoiceNumber })}</h3>
                <span className="text-xs text-slate-500">{t('apartment.invoiceDetailRoom', { room: selectedInvoiceForDetail.roomNumber, tenant: selectedInvoiceForDetail.tenantName })}</span>
              </div>
              <button onClick={() => setSelectedInvoiceForDetail(null)} className="text-slate-400 hover:text-slate-600">✕</button>
            </div>

            {/* Line Items */}
            <div className="space-y-2">
              <span className="text-xs font-bold text-slate-700">{t('apartment.invoiceLineItems')}:</span>
              <div className="divide-y divide-slate-100 border border-slate-200 rounded-xl overflow-hidden text-xs">
                {selectedInvoiceForDetail.items.map((item) => (
                  <div key={item.id} className="p-3 flex justify-between items-center bg-slate-50/50">
                    <div>
                      <span className="font-bold text-slate-800 block">{item.description}</span>
                      <span className="text-[10px] text-slate-400">{t('apartment.itemCategory')}: {item.itemType}</span>
                    </div>
                    <span className="font-bold text-slate-900">฿{item.amount.toLocaleString()}</span>
                  </div>
                ))}
              </div>
            </div>

            {/* Total Balance Card */}
            <div className="p-4 bg-slate-900 text-white rounded-2xl flex justify-between items-center">
              <div>
                <span className="text-xs text-slate-400 block">{t('apartment.invoiceOutstanding')}</span>
                <span className="text-2xl font-black text-amber-300">
                  ฿{selectedInvoiceForDetail.balance.toLocaleString()}
                </span>
              </div>
              <span className={`px-3 py-1 rounded-full text-xs font-bold ${
                selectedInvoiceForDetail.status === 'Paid' ? 'bg-emerald-500/20 text-emerald-300' : 'bg-rose-500/20 text-rose-300'
              }`}>
                {selectedInvoiceForDetail.status === 'Paid' ? t('apartment.invoiceStatusLabel') : t('apartment.invoiceStatusLabelUnpaid')}
              </span>
            </div>

            {/* Payment Section */}
            {selectedInvoiceForDetail.balance > 0 && (
              <form onSubmit={handleRecordInvoicePayment} className="space-y-3 pt-2 border-t border-slate-100">
                <span className="text-xs font-bold text-slate-800 block">{t('apartment.recordPaymentTitle')}:</span>
                <div className="grid grid-cols-2 gap-2">
                  <input
                    type="number"
                    required
                    value={invoicePayAmount}
                    onChange={(e) => setInvoicePayAmount(Number(e.target.value))}
                    placeholder={t('apartment.paymentAmountLabel')}
                    className="px-3 py-2 border border-slate-300 rounded-xl text-xs font-bold"
                  />
                  <select
                    value={invoicePayMethod}
                    onChange={(e) => setInvoicePayMethod(e.target.value)}
                    className="px-3 py-2 border border-slate-300 rounded-xl text-xs bg-white font-bold"
                  >
                    <option value="PromptPay">📲 {t('billing.methodPromptPay')}</option>
                    <option value="BankTransfer">🏦 {t('billing.methodBankTransfer')}</option>
                    <option value="Cash">💵 {t('billing.methodCash')}</option>
                    <option value="CreditCard">💳 {t('billing.methodCreditCard')}</option>
                  </select>
                </div>
                <input
                  type="text"
                  placeholder={t('apartment.paymentRefLabel')}
                  value={invoicePayRef}
                  onChange={(e) => setInvoicePayRef(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs"
                />
                <button
                  type="submit"
                  disabled={loading}
                  className="w-full py-2.5 bg-emerald-600 hover:bg-emerald-700 text-white rounded-xl text-xs font-bold shadow-md transition"
                >
                  {loading ? t('apartment.recordPaymentSaving') : t('apartment.recordPaymentBtn')}
                </button>
              </form>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
