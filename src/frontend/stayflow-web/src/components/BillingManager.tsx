import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import type { PropertyDto, FolioDto, DailyTransactionSummaryDto, RevenueReportDto, OccupancyReportDto } from '../types';
import { apiFetch } from '../lib/api';

interface Props {
  property: PropertyDto;
  token: string;
  orgId: string;
}

export function BillingManager({ property, token, orgId }: Props) {
  const { t, i18n } = useTranslation();
  const [activeSubTab, setActiveSubTab] = useState<'folios' | 'reports'>('folios');
  const [folios, setFolios] = useState<FolioDto[]>([]);
  const [selectedFolio, setSelectedFolio] = useState<FolioDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');

  // Add charge modal state
  const [showAddCharge, setShowAddCharge] = useState(false);
  const [chargeType, setChargeType] = useState('Minibar');
  const [chargeDesc, setChargeDesc] = useState('');
  const [chargePrice, setChargePrice] = useState(100);
  const [chargeQty, setChargeQty] = useState(1);

  // Record payment modal state
  const [showRecordPayment, setShowRecordPayment] = useState(false);
  const [paymentMethod, setPaymentMethod] = useState('PromptPay');
  const [paymentAmount, setPaymentAmount] = useState(0);
  const [paymentRef, setPaymentRef] = useState('');
  const [paymentNotes, setPaymentNotes] = useState('');

  // Reverse payment modal state
  const [showReversePayment, setShowReversePayment] = useState(false);
  const [reversingPaymentId, setReversingPaymentId] = useState('');
  const [reverseReason, setReverseReason] = useState('');

  // Reports state
  const [dailyTx, setDailyTx] = useState<DailyTransactionSummaryDto | null>(null);
  const [revenueRep, setRevenueRep] = useState<RevenueReportDto | null>(null);
  const [occupancyRep, setOccupancyRep] = useState<OccupancyReportDto | null>(null);

  // Reports date filters
  const todayStr = new Date().toISOString().slice(0, 10);
  const thirtyDaysAgo = new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
  const [reportDate, setReportDate] = useState(todayStr);         // for daily-transactions
  const [reportStart, setReportStart] = useState(thirtyDaysAgo); // for revenue & occupancy
  const [reportEnd, setReportEnd] = useState(todayStr);

  const fetchFolios = useCallback(async () => {
    setLoading(true);
    try {
      const data = await apiFetch<FolioDto[]>(`/api/v1/properties/${property.id}/folios`, token, orgId);
      setFolios(data ?? []);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('common.error'));
    } finally {
      setLoading(false);
    }
  }, [property.id, token, orgId]);

  const fetchReports = useCallback(async () => {
    try {
      const [tx, rev, occ] = await Promise.all([
        apiFetch<DailyTransactionSummaryDto>(`/api/v1/properties/${property.id}/reports/daily-transactions?date=${reportDate}`, token, orgId),
        apiFetch<RevenueReportDto>(`/api/v1/properties/${property.id}/reports/revenue?startDate=${reportStart}&endDate=${reportEnd}`, token, orgId),
        apiFetch<OccupancyReportDto>(`/api/v1/properties/${property.id}/reports/occupancy?startDate=${reportStart}&endDate=${reportEnd}`, token, orgId),
      ]);
      setDailyTx(tx);
      setRevenueRep(rev);
      setOccupancyRep(occ);
    } catch {
      // reports fallback
    }
  }, [property.id, token, orgId, reportDate, reportStart, reportEnd]);

  useEffect(() => {
    if (activeSubTab === 'folios') {
      fetchFolios();
    } else {
      fetchReports();
    }
  }, [activeSubTab, fetchFolios, fetchReports]);

  const handleAddCharge = async () => {
    if (!selectedFolio || !chargeDesc.trim() || chargePrice <= 0 || chargeQty <= 0) return;
    setActionLoading(true);
    setError('');
    try {
      const updated = await apiFetch<FolioDto>(`/api/v1/properties/${property.id}/folios/${selectedFolio.id}/charges`, token, orgId, {
        method: 'POST',
        body: JSON.stringify({
          type: chargeType,
          description: chargeDesc.trim(),
          unitPrice: Number(chargePrice),
          quantity: Number(chargeQty)
        })
      });
      setSelectedFolio(updated);
      setShowAddCharge(false);
      setChargeDesc('');
      setChargePrice(100);
      setChargeQty(1);
      setSuccessMsg(t('billing.chargeSuccess'));
      setTimeout(() => setSuccessMsg(''), 3000);
      await fetchFolios();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('billing.chargeError'));
    } finally {
      setActionLoading(false);
    }
  };

  const handleRecordPayment = async () => {
    if (!selectedFolio || paymentAmount <= 0) return;
    setActionLoading(true);
    setError('');
    try {
      const updated = await apiFetch<FolioDto>(`/api/v1/properties/${property.id}/folios/${selectedFolio.id}/payments`, token, orgId, {
        method: 'POST',
        body: JSON.stringify({
          method: paymentMethod,
          amount: Number(paymentAmount),
          referenceNumber: paymentRef.trim() || null,
          notes: paymentNotes.trim() || null
        })
      });
      setSelectedFolio(updated);
      setShowRecordPayment(false);
      setPaymentAmount(0);
      setPaymentRef('');
      setPaymentNotes('');
      setSuccessMsg(t('billing.paymentSuccess'));
      setTimeout(() => setSuccessMsg(''), 3000);
      await fetchFolios();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('billing.paymentError'));
    } finally {
      setActionLoading(false);
    }
  };

  const handleCloseFolio = async () => {
    if (!selectedFolio) return;
    setActionLoading(true);
    setError('');
    try {
      const updated = await apiFetch<FolioDto>(`/api/v1/properties/${property.id}/folios/${selectedFolio.id}/close`, token, orgId, {
        method: 'POST'
      });
      setSelectedFolio(updated);
      setSuccessMsg(t('billing.closeFolioSuccess'));
      setTimeout(() => setSuccessMsg(''), 3000);
      await fetchFolios();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('billing.closeFolioError'));
    } finally {
      setActionLoading(false);
    }
  };

  const handleReversePayment = async () => {
    if (!selectedFolio || !reversingPaymentId || !reverseReason.trim()) return;
    setActionLoading(true);
    setError('');
    try {
      const updated = await apiFetch<FolioDto>(
        `/api/v1/properties/${property.id}/folios/${selectedFolio.id}/payments/${reversingPaymentId}/reverse`,
        token, orgId,
        { method: 'POST', body: JSON.stringify({ reason: reverseReason.trim() }) }
      );
      setSelectedFolio(updated);
      setShowReversePayment(false);
      setReversingPaymentId('');
      setReverseReason('');
      setSuccessMsg(t('billing.reverseSuccess'));
      setTimeout(() => setSuccessMsg(''), 3000);
      await fetchFolios();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('billing.reverseError'));
    } finally {
      setActionLoading(false);
    }
  };

  const totalOutstanding = folios.filter(f => f.status === 'Open').reduce((acc, f) => acc + f.balance, 0);
  const totalCollected = folios.reduce((acc, f) => acc + f.totalPayments, 0);

  const getMethodLabel = (method: string) => {
    switch (method) {
      case 'Cash': return t('billing.methodCash');
      case 'PromptPay': return t('billing.methodPromptPay');
      case 'CreditCard': return t('billing.methodCreditCard');
      case 'BankTransfer': return t('billing.methodBankTransfer');
      default: return method;
    }
  };

  const getCategoryLabel = (category: string) => {
    switch (category) {
      case 'RoomCharge': return t('billing.categoryRoom');
      case 'Minibar': return t('billing.categoryMinibar');
      case 'FoodAndBeverage': return t('billing.categoryFnB');
      case 'ExtraBed': return t('billing.categoryExtraBed');
      case 'Laundry': return t('billing.categoryLaundry');
      case 'DamageOrLoss': return t('billing.categoryDamage');
      case 'Adjustment': return t('billing.categoryAdjustment');
      default: return category;
    }
  };

  return (
    <div className="space-y-6">
      {/* Financial Summary Top Bar */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <div className="p-4 rounded-2xl bg-white border border-slate-200 t-card">
          <div className="text-xs text-slate-400 font-bold uppercase mb-1">{t('billing.totalCollected')}</div>
          <div className="text-2xl font-black text-emerald-600">
            {totalCollected.toLocaleString()} <span className="text-xs font-semibold text-slate-400">{property.defaultCurrency}</span>
          </div>
          <div className="text-xs text-slate-400 mt-1">{t('billing.fromAllFolios')}</div>
        </div>

        <div className="p-4 rounded-2xl bg-white border border-slate-200 t-card">
          <div className="text-xs text-slate-400 font-bold uppercase mb-1">{t('billing.totalOutstanding')}</div>
          <div className="text-2xl font-black text-rose-600">
            {totalOutstanding.toLocaleString()} <span className="text-xs font-semibold text-slate-400">{property.defaultCurrency}</span>
          </div>
          <div className="text-xs text-slate-400 mt-1">{t('billing.mustCollect')}</div>
        </div>

        <div className="p-4 rounded-2xl bg-white border border-slate-200 t-card">
          <div className="text-xs text-slate-400 font-bold uppercase mb-1">{t('billing.openFolios')}</div>
          <div className="text-2xl font-black text-indigo-600">
            {folios.filter(f => f.status === 'Open').length}
          </div>
          <div className="text-xs text-slate-400 mt-1">{t('billing.closedCount')} {folios.filter(f => f.status === 'Closed').length}</div>
        </div>

        <div className="p-4 rounded-2xl bg-white border border-slate-200 t-card">
          <div className="text-xs text-slate-400 font-bold uppercase mb-1">{t('billing.occupancyAvg')}</div>
          <div className="text-2xl font-black text-blue-600">
            {occupancyRep?.averageOccupancyRate ?? 0}%
          </div>
          <div className="text-xs text-slate-400 mt-1">{t('billing.last7Days')}</div>
        </div>
      </div>

      {/* Notifications */}
      {error && (
        <div className="bg-rose-50 border border-rose-200 text-rose-700 px-4 py-3 rounded-2xl text-sm flex items-center justify-between">
          <span>⚠️ {error}</span>
          <button onClick={() => setError('')} className="text-rose-400 hover:text-rose-600">✕</button>
        </div>
      )}
      {successMsg && (
        <div className="bg-emerald-50 border border-emerald-200 text-emerald-800 px-4 py-3 rounded-2xl text-sm flex items-center justify-between">
          <span>✅ {successMsg}</span>
          <button onClick={() => setSuccessMsg('')} className="text-emerald-400 hover:text-emerald-600">✕</button>
        </div>
      )}

      {/* Main Tab Controls */}
      <div className="flex bg-slate-100 p-1 rounded-2xl border border-slate-200 max-w-md gap-1">
        <button
          onClick={() => setActiveSubTab('folios')}
          className={`flex-1 py-2 text-xs font-bold rounded-xl t-pill transition-all ${
            activeSubTab === 'folios' ? 'bg-white text-indigo-600 shadow-xs' : 'text-slate-600 hover:text-slate-900'
          }`}
        >
          💳 {t('billing.folios')}
        </button>
        <button
          onClick={() => setActiveSubTab('reports')}
          className={`flex-1 py-2 text-xs font-bold rounded-xl t-pill transition-all ${
            activeSubTab === 'reports' ? 'bg-white text-emerald-600 shadow-xs' : 'text-slate-600 hover:text-slate-900'
          }`}
        >
          📊 {t('billing.reports')}
        </button>
      </div>

      {/* Sub-Tab 1: Folios List */}
      {activeSubTab === 'folios' && (
        <div className="bg-white rounded-3xl border border-slate-200 p-6 shadow-sm space-y-4">
          <div className="flex items-center justify-between pb-3 border-b border-slate-100">
            <h3 className="font-bold text-slate-900 text-base flex items-center gap-2">
              <span>💳</span>
              <span>{t('billing.allFolios')}</span>
            </h3>
            <button
              onClick={fetchFolios}
              disabled={loading}
              className="px-3 py-1.5 text-xs font-semibold text-slate-600 bg-slate-100 hover:bg-slate-200 rounded-xl t-button"
            >
              🔄 {t('common.refresh')}
            </button>
          </div>

          {folios.length === 0 ? (
            <div className="text-center py-12 text-slate-400 space-y-2">
              <div className="text-4xl">📄</div>
              <div className="font-semibold text-sm">{t('billing.noFolios')}</div>
              <div className="text-xs text-slate-400">{t('billing.noFoliosHint')}</div>
            </div>
          ) : (
            <div className="space-y-3">
              {folios.map(folio => (
                <div 
                  key={folio.id} 
                  onClick={() => setSelectedFolio(folio)}
                  className="border border-slate-200 rounded-2xl p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4 hover:border-indigo-400 cursor-pointer t-card"
                >
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-mono font-bold text-indigo-700">{folio.folioNumber}</span>
                      <span className={`px-2 py-0.5 text-xs font-bold rounded-full ${
                        folio.status === 'Closed' ? 'bg-slate-100 text-slate-600' : 'bg-emerald-100 text-emerald-800'
                      }`}>
                        {folio.status === 'Closed' ? t('billing.folioClosed') : t('billing.folioOpen')}
                      </span>
                    </div>
                    <div className="text-xs text-slate-500">
                      {folio.items.length} {t('billing.folioCharges')} • {folio.payments.length} {t('billing.folioPayments')}
                    </div>
                    {folio.notes && <div className="text-xs text-slate-400 italic">{t('billing.folioNotes')}: {folio.notes}</div>}
                  </div>

                  <div className="flex sm:flex-col items-end justify-between sm:justify-center border-t sm:border-t-0 pt-2 sm:pt-0">
                    <div className="text-xs text-slate-400">{t('billing.folioBalance')}</div>
                    <div className={`text-lg font-black ${folio.balance > 0 ? 'text-rose-600' : 'text-emerald-600'}`}>
                      {folio.balance.toLocaleString()} <span className="text-xs font-bold">{folio.currency}</span>
                    </div>
                    <div className="text-[11px] text-slate-400">
                      {t('billing.folioChargesLabel')}: {folio.totalCharges.toLocaleString()} {folio.currency}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Sub-Tab 2: Operational & Financial Reports */}
      {activeSubTab === 'reports' && (
        <div className="space-y-6">
          {/* Date Filter Controls */}
          <div className="bg-white rounded-2xl border border-slate-200 p-4 space-y-3">
            <h4 className="text-xs font-bold text-slate-700 uppercase">{t('billing.dateFilter')}</h4>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              <div>
                <label className="text-[11px] font-semibold text-slate-500 block mb-1">{t('billing.dailyDate')}</label>
                <input
                  type="date"
                  value={reportDate}
                  max={todayStr}
                  onChange={e => setReportDate(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs focus:ring-2 focus:ring-indigo-400"
                />
              </div>
              <div>
                <label className="text-[11px] font-semibold text-slate-500 block mb-1">{t('billing.revenueStartDate')}</label>
                <input
                  type="date"
                  value={reportStart}
                  max={reportEnd}
                  onChange={e => setReportStart(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs focus:ring-2 focus:ring-indigo-400"
                />
              </div>
              <div>
                <label className="text-[11px] font-semibold text-slate-500 block mb-1">{t('billing.revenueEndDate')}</label>
                <input
                  type="date"
                  value={reportEnd}
                  min={reportStart}
                  max={todayStr}
                  onChange={e => setReportEnd(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-xs focus:ring-2 focus:ring-indigo-400"
                />
              </div>
            </div>
            <button
              onClick={fetchReports}
              className="px-4 py-2 text-xs font-bold text-white bg-indigo-600 hover:bg-indigo-700 rounded-xl transition-colors"
            >
              🔍 {t('billing.loadReport')}
            </button>
          </div>

          {/* Daily Cashier Report */}
          <div className="bg-white rounded-3xl border border-slate-200 p-6 shadow-sm space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100">
              <h3 className="font-bold text-slate-900 text-base flex items-center gap-2">
                <span>💵</span>
                <span>{t('billing.dailyReport')}</span>
              </h3>
              <span className="text-xs text-slate-400 font-medium">{t('billing.dailyDate2')}: {dailyTx?.date ?? reportDate}</span>
            </div>

            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              {['Cash', 'PromptPay', 'CreditCard', 'BankTransfer'].map(m => (
                <div key={m} className="p-3 rounded-2xl bg-slate-50 border border-slate-100">
                  <div className="text-xs text-slate-500 font-semibold">
                    {getMethodLabel(m)}
                  </div>
                  <div className="text-lg font-bold text-slate-800 mt-1">
                    {(dailyTx?.amountByMethod[m] ?? 0).toLocaleString()} {property.defaultCurrency}
                  </div>
                </div>
              ))}
            </div>

            <div className="p-4 rounded-2xl bg-indigo-50 border border-indigo-100 flex items-center justify-between">
              <span className="font-bold text-indigo-900 text-sm">{t('billing.dailyTotal')}</span>
              <span className="text-2xl font-black text-indigo-700">
                {(dailyTx?.totalAmount ?? 0).toLocaleString()} {property.defaultCurrency}
              </span>
            </div>
          </div>

          {/* Revenue by Category Breakdown */}
          <div className="bg-white rounded-3xl border border-slate-200 p-6 shadow-sm space-y-4">
            <h3 className="font-bold text-slate-900 text-base flex items-center gap-2 pb-3 border-b border-slate-100">
              <span>📈</span>
              <span>{t('billing.revenueReport')}</span>
            </h3>

            {(!revenueRep || revenueRep.categories.length === 0) ? (
              <div className="text-center py-8 text-slate-400 text-sm">{t('billing.noRevenue')}</div>
            ) : (
              <div className="space-y-3">
                <div className="p-3 rounded-xl bg-emerald-50 border border-emerald-100 flex items-center justify-between mb-2">
                  <span className="text-xs font-bold text-emerald-800">{t('billing.totalRevenue')}</span>
                  <span className="text-lg font-black text-emerald-700">
                    {revenueRep.totalRevenue.toLocaleString()} {revenueRep.currency}
                  </span>
                </div>
                {revenueRep.categories.map(cat => (
                  <div key={cat.category} className="space-y-1">
                    <div className="flex justify-between text-xs font-bold text-slate-700">
                      <span>
                        {getCategoryLabel(cat.category)}
                      </span>
                      <span>{cat.amount.toLocaleString()} {property.defaultCurrency} ({cat.percentage}%)</span>
                    </div>
                    <div className="w-full bg-slate-100 rounded-full h-2.5 overflow-hidden">
                      <div
                        className="bg-indigo-600 h-2.5 rounded-full"
                        style={{ width: `${Math.min(100, cat.percentage)}%` }}
                      />
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Occupancy Report */}
          <div className="bg-white rounded-3xl border border-slate-200 p-6 shadow-sm space-y-4">
            <h3 className="font-bold text-slate-900 text-base flex items-center gap-2 pb-3 border-b border-slate-100">
              <span>🏨</span>
              <span>{t('billing.occupancyReport')}</span>
            </h3>
            {(!occupancyRep || occupancyRep.dailyBreakdown.length === 0) ? (
              <div className="text-center py-8 text-slate-400 text-sm">{t('billing.noOccupancy')}</div>
            ) : (
              <div className="space-y-3">
                <div className="p-3 rounded-xl bg-indigo-50 border border-indigo-100 flex items-center justify-between">
                  <span className="text-xs font-bold text-indigo-800">{t('billing.avgOccupancy')} ({t('billing.statsThisPeriod', { start: reportStart, end: reportEnd })})</span>
                  <span className="text-lg font-black text-indigo-700">{occupancyRep.averageOccupancyRate}%</span>
                </div>
                <div className="max-h-60 overflow-y-auto space-y-1 pr-1">
                  {occupancyRep.dailyBreakdown.map(day => (
                    <div key={day.date} className="flex items-center gap-3 text-xs">
                      <span className="w-24 text-slate-500 shrink-0">{day.date}</span>
                      <div className="flex-1 bg-slate-100 rounded-full h-2 overflow-hidden">
                        <div
                          className="h-2 rounded-full bg-indigo-500"
                          style={{ width: `${Math.min(100, day.occupancyRate)}%` }}
                        />
                      </div>
                      <span className="w-14 text-right font-semibold text-slate-700">
                        {day.occupiedRooms}/{day.totalRooms} ({day.occupancyRate}%)
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Folio Detail Modal */}
      {selectedFolio && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm t-modal-overlay">
          <div className="bg-white rounded-3xl shadow-2xl max-w-2xl w-full border border-slate-200 overflow-hidden t-modal-content p-6 space-y-4 max-h-[90vh] overflow-y-auto">
            {/* Modal Header */}
            <div className="flex items-center justify-between border-b border-slate-100 pb-3">
              <div>
                <div className="flex items-center gap-2">
                  <h3 className="text-lg font-black text-slate-900">{selectedFolio.folioNumber}</h3>
                  <span className={`px-2.5 py-0.5 text-xs font-bold rounded-full ${
                    selectedFolio.status === 'Closed' ? 'bg-slate-100 text-slate-600' : 'bg-emerald-100 text-emerald-800'
                  }`}>
                    {selectedFolio.status === 'Closed' ? t('billing.folioClosed') : t('billing.folioOpen')}
                  </span>
                </div>
                <p className="text-xs text-slate-400">{t('billing.folioCreated')}: {new Date(selectedFolio.createdAtUtc).toLocaleString(i18n.language)}</p>
              </div>
              <button onClick={() => setSelectedFolio(null)} className="text-slate-400 hover:text-slate-600 text-lg">✕</button>
            </div>

            {/* Balances Box */}
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 p-4 bg-slate-50 border border-slate-200 rounded-2xl text-center">
              <div>
                <div className="text-xs text-slate-400 font-semibold">{t('billing.totalCharges')}</div>
                <div className="text-lg font-bold text-slate-800">{selectedFolio.totalCharges.toLocaleString()} {selectedFolio.currency}</div>
              </div>
              <div>
                <div className="text-xs text-slate-400 font-semibold">{t('billing.totalPaid')}</div>
                <div className="text-lg font-bold text-emerald-600">{selectedFolio.totalPayments.toLocaleString()} {selectedFolio.currency}</div>
              </div>
              <div>
                <div className="text-xs text-slate-400 font-semibold">{t('billing.balance')}</div>
                <div className={`text-lg font-black ${selectedFolio.balance > 0 ? 'text-rose-600' : 'text-emerald-700'}`}>
                  {selectedFolio.balance.toLocaleString()} {selectedFolio.currency}
                </div>
              </div>
            </div>

            {/* Charges List */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-slate-700 uppercase">{t('billing.chargesSection')}</span>
                {selectedFolio.status === 'Open' && (
                  <button
                    onClick={() => setShowAddCharge(true)}
                    className="text-xs font-bold text-indigo-600 hover:text-indigo-800"
                  >
                    + {t('billing.addCharge')}
                  </button>
                )}
              </div>

              <div className="border border-slate-200 rounded-xl overflow-hidden divide-y divide-slate-100 text-xs">
                {selectedFolio.items.map(item => (
                  <div key={item.id} className="p-2.5 flex items-center justify-between">
                    <div>
                      <div className="font-semibold text-slate-800">{item.description}</div>
                      <div className="text-[11px] text-slate-400">{t('common.category')}: {item.type} • {t('common.qty')} {item.quantity} x {item.unitPrice.toLocaleString()}</div>
                    </div>
                    <div className="font-bold text-slate-800">{item.amount.toLocaleString()} {selectedFolio.currency}</div>
                  </div>
                ))}
              </div>
            </div>

            {/* Payments List */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-slate-700 uppercase">{t('billing.paymentsSection')}</span>
                {selectedFolio.status === 'Open' && (
                  <button
                    onClick={() => {
                      setPaymentAmount(selectedFolio.balance > 0 ? selectedFolio.balance : 0);
                      setShowRecordPayment(true);
                    }}
                    className="text-xs font-bold text-emerald-600 hover:text-emerald-800"
                  >
                    + {t('billing.recordPayment')}
                  </button>
                )}
              </div>

              {selectedFolio.payments.length === 0 ? (
                <div className="p-3 bg-slate-50 border border-slate-200 rounded-xl text-center text-xs text-slate-400">
                  {t('billing.noPayments')}
                </div>
              ) : (
                <div className="border border-slate-200 rounded-xl overflow-hidden divide-y divide-slate-100 text-xs">
                  {selectedFolio.payments.map(p => (
                    <div key={p.id} className="p-2.5 flex items-center justify-between gap-2">
                      <div className="min-w-0">
                        <div className="font-semibold text-slate-800">
                          {getMethodLabel(p.method)} • {p.amount.toLocaleString()} {p.currency}
                          {p.status === 'Reversed' && <span className="ml-2 text-rose-600 font-bold">[{t('billing.reversed')}]</span>}
                        </div>
                        <div className="text-[11px] text-slate-400">
                          {new Date(p.createdAtUtc).toLocaleString(i18n.language)} {p.referenceNumber && `• ${t('common.reference')}: ${p.referenceNumber}`}
                        </div>
                        {p.reversalReason && <div className="text-[11px] text-rose-400 italic">{t('billing.reverseReason')}: {p.reversalReason}</div>}
                      </div>
                      <div className="flex items-center gap-2 shrink-0">
                        <div className={`font-bold ${p.status === 'Reversed' ? 'text-slate-300 line-through' : 'text-emerald-600'}`}>
                          +{p.amount.toLocaleString()} {p.currency}
                        </div>
                        {selectedFolio.status === 'Open' && p.status === 'Completed' && (
                          <button
                            onClick={() => { setReversingPaymentId(p.id); setReverseReason(''); setShowReversePayment(true); }}
                            className="text-[10px] font-bold text-rose-500 hover:text-rose-700 px-2 py-0.5 border border-rose-200 rounded-md hover:bg-rose-50 transition-colors"
                          >
                            {t('billing.reversePayment')}
                          </button>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* Modal Actions */}
            <div className="flex items-center justify-between pt-3 border-t border-slate-100">
              <button
                type="button"
                onClick={() => setSelectedFolio(null)}
                className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 rounded-xl hover:bg-slate-200 t-button"
              >
                {t('common.close')}
              </button>

              {selectedFolio.status === 'Open' && (
                <button
                  type="button"
                  disabled={actionLoading || selectedFolio.balance !== 0}
                  onClick={handleCloseFolio}
                  className="px-5 py-2 text-xs font-bold text-white bg-slate-900 rounded-xl hover:bg-slate-800 disabled:opacity-40 t-button shadow-md"
                >
                  {selectedFolio.balance === 0 ? `✓ ${t('billing.closeFolio')}` : t('billing.closeFolioBalance', { amount: selectedFolio.balance.toLocaleString() })}
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Add Charge Sub-Modal */}
      {showAddCharge && (
        <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs">
          <div className="bg-white rounded-3xl p-6 max-w-sm w-full space-y-4 shadow-2xl border border-slate-200">
            <h4 className="font-bold text-sm text-slate-900">{t('billing.addChargeTitle')}</h4>
            <div>
              <label className="text-xs font-bold text-slate-700 block mb-1">{t('billing.chargeType')}</label>
              <select
                value={chargeType}
                onChange={e => setChargeType(e.target.value)}
                className="w-full px-3 py-2 border rounded-xl text-xs bg-white"
              >
                <option value="Minibar">{t('billing.categoryMinibar')}</option>
                <option value="FoodAndBeverage">{t('billing.categoryFnB')}</option>
                <option value="ExtraBed">{t('billing.categoryExtraBed')}</option>
                <option value="Laundry">{t('billing.categoryLaundry')}</option>
                <option value="DamageOrLoss">{t('billing.categoryDamage')}</option>
                <option value="Other">Other</option>
              </select>
            </div>
            <div>
              <label className="text-xs font-bold text-slate-700 block mb-1">{t('billing.chargeDescription')}</label>
              <input
                type="text"
                placeholder={t('billing.chargeDescriptionPlaceholder')}
                value={chargeDesc}
                onChange={e => setChargeDesc(e.target.value)}
                className="w-full px-3 py-2 border rounded-xl text-xs"
              />
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="text-xs font-bold text-slate-700 block mb-1">{t('billing.chargeUnitPrice')}</label>
                <input
                  type="number"
                  value={chargePrice}
                  onChange={e => setChargePrice(Number(e.target.value))}
                  className="w-full px-3 py-2 border rounded-xl text-xs"
                />
              </div>
              <div>
                <label className="text-xs font-bold text-slate-700 block mb-1">{t('billing.chargeQty')}</label>
                <input
                  type="number"
                  value={chargeQty}
                  onChange={e => setChargeQty(Number(e.target.value))}
                  className="w-full px-3 py-2 border rounded-xl text-xs"
                />
              </div>
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <button onClick={() => setShowAddCharge(false)} className="px-3 py-1.5 text-xs text-slate-600 bg-slate-100 rounded-lg">{t('common.cancel')}</button>
              <button
                onClick={handleAddCharge}
                disabled={actionLoading || !chargeDesc.trim()}
                className="px-4 py-1.5 text-xs font-bold text-white bg-indigo-600 rounded-lg disabled:opacity-40"
              >
                {t('billing.chargeSave')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Reverse Payment Sub-Modal */}
      {showReversePayment && (
        <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs">
          <div className="bg-white rounded-3xl p-6 max-w-sm w-full space-y-4 shadow-2xl border border-rose-200">
            <h4 className="font-bold text-sm text-rose-800">⚠️ {t('billing.reversePaymentTitle')}</h4>
            <p className="text-xs text-slate-500">{t('billing.reversePaymentDesc')}</p>
            <div>
              <label className="text-xs font-bold text-slate-700 block mb-1">{t('billing.reverseReason')}</label>
              <input
                type="text"
                placeholder={t('billing.reverseReasonPlaceholder')}
                value={reverseReason}
                onChange={e => setReverseReason(e.target.value)}
                className="w-full px-3 py-2 border border-rose-200 rounded-xl text-xs focus:ring-2 focus:ring-rose-400"
                autoFocus
              />
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <button onClick={() => setShowReversePayment(false)} className="px-3 py-1.5 text-xs text-slate-600 bg-slate-100 rounded-lg">{t('common.cancel')}</button>
              <button
                onClick={handleReversePayment}
                disabled={actionLoading || !reverseReason.trim()}
                className="px-4 py-1.5 text-xs font-bold text-white bg-rose-600 rounded-lg disabled:opacity-40"
              >
                {t('billing.confirmReverse')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Record Payment Sub-Modal */}
      {showRecordPayment && (
        <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs">
          <div className="bg-white rounded-3xl p-6 max-w-sm w-full space-y-4 shadow-2xl border border-slate-200">
            <h4 className="font-bold text-sm text-slate-900">{t('billing.recordPaymentTitle')}</h4>
            <div>
              <label className="text-xs font-bold text-slate-700 block mb-1">{t('billing.paymentMethod')}</label>
              <select
                value={paymentMethod}
                onChange={e => setPaymentMethod(e.target.value)}
                className="w-full px-3 py-2 border rounded-xl text-xs bg-white"
              >
                <option value="PromptPay">{getMethodLabel('PromptPay')}</option>
                <option value="Cash">{getMethodLabel('Cash')}</option>
                <option value="CreditCard">{getMethodLabel('CreditCard')}</option>
                <option value="BankTransfer">{getMethodLabel('BankTransfer')}</option>
              </select>
            </div>
            <div>
              <label className="text-xs font-bold text-slate-700 block mb-1">{t('billing.paymentAmount')} ({property.defaultCurrency})</label>
              <input
                type="number"
                value={paymentAmount}
                onChange={e => setPaymentAmount(Number(e.target.value))}
                className="w-full px-3 py-2 border rounded-xl text-sm font-bold text-emerald-700"
              />
            </div>
            <div>
              <label className="text-xs font-bold text-slate-700 block mb-1">{t('billing.paymentRef')}</label>
              <input
                type="text"
                placeholder={t('billing.paymentRefPlaceholder')}
                value={paymentRef}
                onChange={e => setPaymentRef(e.target.value)}
                className="w-full px-3 py-2 border rounded-xl text-xs"
              />
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <button onClick={() => setShowRecordPayment(false)} className="px-3 py-1.5 text-xs text-slate-600 bg-slate-100 rounded-lg">{t('common.cancel')}</button>
              <button
                onClick={handleRecordPayment}
                disabled={actionLoading || paymentAmount <= 0}
                className="px-4 py-1.5 text-xs font-bold text-white bg-emerald-600 rounded-lg disabled:opacity-40"
              >
                {t('billing.recordPayment')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
