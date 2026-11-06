import { useState, useCallback, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { GuestDto, GuestStayDto } from '../types';
import { apiFetch } from '../lib/api';

interface Props {
  token: string;
  orgId: string;
}

const STATUS_COLOR: Record<string, string> = {
  Pending: 'bg-yellow-100 text-yellow-800',
  Confirmed: 'bg-blue-100 text-blue-800',
  CheckedIn: 'bg-green-100 text-green-800',
  CheckedOut: 'bg-gray-100 text-gray-800',
  Cancelled: 'bg-red-100 text-red-800',
  NoShow: 'bg-orange-100 text-orange-800',
};

function formatDate(d: string, locale: string) {
  return new Date(d).toLocaleDateString(locale, {
    day: '2-digit', month: 'short', year: 'numeric',
  });
}

function formatCurrency(amount: number, currency: string, locale: string) {
  return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(amount);
}

interface GuestFormState {
  fullName: string;
  email: string;
  phone: string;
  nationalId: string;
  passportNumber: string;
  nationality: string;
  notes: string;
}

const emptyForm = (): GuestFormState => ({
  fullName: '', email: '', phone: '', nationalId: '',
  passportNumber: '', nationality: '', notes: '',
});

function guestToForm(g: GuestDto): GuestFormState {
  return {
    fullName: g.fullName,
    email: g.email ?? '',
    phone: g.phone ?? '',
    nationalId: g.nationalId ?? '',
    passportNumber: g.passportNumber ?? '',
    nationality: g.nationality ?? '',
    notes: g.notes ?? '',
  };
}

export function GuestManager({ token, orgId }: Props) {
  const { t, i18n } = useTranslation();
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [guests, setGuests] = useState<GuestDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  // Detail / edit panel
  const [selectedGuest, setSelectedGuest] = useState<GuestDto | null>(null);
  const [stayHistory, setStayHistory] = useState<GuestStayDto[]>([]);
  const [stayLoading, setStayLoading] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [editMode, setEditMode] = useState(false);
  const [form, setForm] = useState<GuestFormState>(emptyForm());
  const [formError, setFormError] = useState('');
  const [formLoading, setFormLoading] = useState(false);
  const [formSuccess, setFormSuccess] = useState('');

  // Debounce search
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 400);
    return () => clearTimeout(t);
  }, [search]);

  const fetchGuests = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
      if (debouncedSearch) params.set('search', debouncedSearch);
      const data = await apiFetch<{ data: GuestDto[]; total: number }>(
        `/api/v1/guests?${params}`, token, orgId
      );
      setGuests(data.data);
      setTotal(data.total);
    } catch {
      setError(t('guests.loadError'));
    } finally {
      setLoading(false);
    }
  }, [token, orgId, page, debouncedSearch, t]);

  useEffect(() => { fetchGuests(); }, [fetchGuests]);

  // Reset page when search changes
  useEffect(() => { setPage(1); }, [debouncedSearch]);

  const loadStayHistory = useCallback(async (guestId: string) => {
    setStayLoading(true);
    setStayHistory([]);
    try {
      const data = await apiFetch<GuestStayDto[]>(
        `/api/v1/guests/${guestId}/reservations`, token, orgId
      );
      setStayHistory(data);
    } catch {
      // non-critical: show empty
    } finally {
      setStayLoading(false);
    }
  }, [token, orgId]);

  const openGuest = useCallback((g: GuestDto) => {
    setSelectedGuest(g);
    setEditMode(false);
    setForm(guestToForm(g));
    setFormError('');
    setFormSuccess('');
    loadStayHistory(g.id);
  }, [loadStayHistory]);

  const handleCreate = async () => {
    if (!form.fullName.trim()) { setFormError(t('walkIn.validationName')); return; }
    setFormLoading(true);
    setFormError('');
    try {
      const created = await apiFetch<GuestDto>('/api/v1/guests', token, orgId, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fullName: form.fullName.trim(),
          email: form.email.trim() || null,
          phone: form.phone.trim() || null,
          nationalId: form.nationalId.trim() || null,
          passportNumber: form.passportNumber.trim() || null,
          nationality: form.nationality.trim() || null,
          notes: form.notes.trim() || null,
        }),
      });
      setShowCreate(false);
      setForm(emptyForm());
      await fetchGuests();
      openGuest(created);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : '';
      setFormError(msg || t('guests.createError'));
    } finally {
      setFormLoading(false);
    }
  };

  const handleUpdate = async () => {
    if (!selectedGuest) return;
    if (!form.fullName.trim()) { setFormError(t('walkIn.validationName')); return; }
    setFormLoading(true);
    setFormError('');
    setFormSuccess('');
    try {
      const updated = await apiFetch<GuestDto>(`/api/v1/guests/${selectedGuest.id}`, token, orgId, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fullName: form.fullName.trim(),
          email: form.email.trim() || null,
          phone: form.phone.trim() || null,
          nationalId: form.nationalId.trim() || null,
          passportNumber: form.passportNumber.trim() || null,
          nationality: form.nationality.trim() || null,
          notes: form.notes.trim() || null,
        }),
      });
      setSelectedGuest(updated);
      setEditMode(false);
      setFormSuccess(t('guests.updateSuccess'));
      await fetchGuests();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : '';
      setFormError(msg || t('common.error'));
    } finally {
      setFormLoading(false);
    }
  };

  const totalPages = Math.ceil(total / pageSize);

  return (
    <div className="flex h-full gap-4">
      {/* Left column: search + list */}
      <div className="w-80 flex-shrink-0 flex flex-col gap-3">
        {/* Search + create */}
        <div className="flex gap-2">
          <input
            type="text"
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder={t('guests.searchPlaceholder')}
            className="flex-1 border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <button
            onClick={() => { setShowCreate(true); setSelectedGuest(null); setForm(emptyForm()); setFormError(''); }}
            className="bg-blue-600 text-white px-3 py-2 rounded text-sm hover:bg-blue-700 whitespace-nowrap"
          >
            + {t('guests.createGuest')}
          </button>
        </div>

        {/* Guest list */}
        <div className="flex-1 overflow-y-auto border rounded bg-white divide-y">
          {loading && (
            <div className="p-4 text-center text-gray-400 text-sm">{t('common.loading')}</div>
          )}
          {!loading && error && (
            <div className="p-4 text-center text-red-500 text-sm">{error}</div>
          )}
          {!loading && !error && guests.length === 0 && (
            <div className="p-4 text-center text-gray-400 text-sm">{t('guests.noGuests')}</div>
          )}
          {guests.map(g => (
            <button
              key={g.id}
              onClick={() => openGuest(g)}
              className={`w-full text-left px-3 py-2.5 hover:bg-blue-50 transition-colors ${selectedGuest?.id === g.id ? 'bg-blue-50 border-l-4 border-l-blue-500' : ''}`}
            >
              <div className="font-medium text-sm text-gray-900 truncate">{g.fullName}</div>
              <div className="text-xs text-gray-500 truncate">{g.phone || g.email || '—'}</div>
            </button>
          ))}
        </div>

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="flex items-center justify-between text-xs text-gray-500">
            <button disabled={page === 1} onClick={() => setPage(p => p - 1)}
              className="px-2 py-1 border rounded disabled:opacity-40 hover:bg-gray-50">
              &#8249; {t('common.back')}
            </button>
            <span>{page} / {totalPages}</span>
            <button disabled={page === totalPages} onClick={() => setPage(p => p + 1)}
              className="px-2 py-1 border rounded disabled:opacity-40 hover:bg-gray-50">
              {t('common.next')} &#8250;
            </button>
          </div>
        )}
        <div className="text-xs text-gray-400 text-center">
          {t('guests.pagination', {
            from: total === 0 ? 0 : (page - 1) * pageSize + 1,
            to: Math.min(page * pageSize, total),
            total,
          })}
        </div>
      </div>

      {/* Right column: create form or guest detail */}
      <div className="flex-1 overflow-y-auto">
        {showCreate && (
          <div className="bg-white border rounded p-5">
            <h3 className="font-semibold text-gray-800 mb-4">{t('guests.createGuest')}</h3>
            <GuestForm
              form={form}
              onChange={f => setForm(f)}
              error={formError}
              loading={formLoading}
              onSubmit={handleCreate}
              onCancel={() => setShowCreate(false)}
              submitLabel={t('guests.createGuest')}
            />
          </div>
        )}

        {!showCreate && selectedGuest && (
          <div className="bg-white border rounded p-5 space-y-5">
            {/* Header */}
            <div className="flex items-start justify-between">
              <div>
                <h3 className="font-semibold text-lg text-gray-800">{selectedGuest.fullName}</h3>
                <p className="text-xs text-gray-400">{t('reservations.createdAt')} {formatDate(selectedGuest.createdAtUtc, i18n.language)}</p>
              </div>
              {!editMode && (
                <button
                  onClick={() => { setEditMode(true); setForm(guestToForm(selectedGuest)); setFormError(''); setFormSuccess(''); }}
                  className="text-sm text-blue-600 hover:text-blue-800 border border-blue-300 px-3 py-1 rounded"
                >
                  {t('common.edit')}
                </button>
              )}
            </div>

            {formSuccess && (
              <div className="bg-green-50 text-green-700 text-sm px-3 py-2 rounded">{formSuccess}</div>
            )}

            {editMode ? (
              <GuestForm
                form={form}
                onChange={f => setForm(f)}
                error={formError}
                loading={formLoading}
                onSubmit={handleUpdate}
                onCancel={() => { setEditMode(false); setFormError(''); }}
                submitLabel={t('common.save')}
              />
            ) : (
              <GuestInfoPanel guest={selectedGuest} />
            )}

            {/* Stay history */}
            <div>
              <h4 className="font-medium text-gray-700 mb-2">{t('guests.stayHistory')}</h4>
              {stayLoading && <p className="text-sm text-gray-400">{t('common.loading')}</p>}
              {!stayLoading && stayHistory.length === 0 && (
                <p className="text-sm text-gray-400">{t('guests.noStayHistory')}</p>
              )}
              {!stayLoading && stayHistory.length > 0 && (
                <div className="divide-y border rounded overflow-hidden">
                  {stayHistory.map(s => (
                    <div key={s.reservationId} className="px-3 py-2.5 text-sm">
                      <div className="flex items-center justify-between gap-2">
                        <div className="min-w-0">
                          <span className="font-medium text-gray-800">{s.propertyName}</span>
                          {s.roomTypeName && (
                            <span className="text-gray-500"> · {s.roomTypeName}</span>
                          )}
                          {s.roomNumber && (
                            <span className="text-gray-500"> {t('common.room')} {s.roomNumber}</span>
                          )}
                        </div>
                        <span className={`flex-shrink-0 px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_COLOR[s.status] ?? 'bg-gray-100 text-gray-700'}`}>
                          {t(`status.${s.status}`, { defaultValue: s.status })}
                        </span>
                      </div>
                      <div className="flex items-center justify-between mt-1 text-gray-500">
                        <span>{formatDate(s.checkInDate, i18n.language)} — {formatDate(s.checkOutDate, i18n.language)} ({s.nights} {t('common.nights')})</span>
                        <span className="font-medium text-gray-700">{formatCurrency(s.totalAmount, s.currency, i18n.language)}</span>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}

        {!showCreate && !selectedGuest && (
          <div className="h-full flex items-center justify-center text-gray-400 text-sm">
            {t('common.noData')} — {t('guests.createGuest')}
          </div>
        )}
      </div>
    </div>
  );
}

function GuestInfoPanel({ guest }: { guest: GuestDto }) {
  const { t } = useTranslation();
  const fields: { key: string; label: string; value: string | null | undefined; wide?: boolean }[] = [
    { key: 'fullName', label: t('common.fullName'), value: guest.fullName },
    { key: 'email', label: t('common.email'), value: guest.email },
    { key: 'phone', label: t('common.phone'), value: guest.phone },
    { key: 'nationalId', label: t('guests.idNumber'), value: guest.nationalId },
    { key: 'passportNumber', label: 'Passport', value: guest.passportNumber },
    { key: 'nationality', label: t('guests.nationality'), value: guest.nationality },
    { key: 'notes', label: t('common.notes'), value: guest.notes, wide: true },
  ];

  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
      {fields.map(f => (
        <div key={f.key} className={f.wide ? 'col-span-2' : ''}>
          <dt className="text-gray-500 text-xs">{f.label}</dt>
          <dd className="text-gray-800 font-medium">{f.value || '—'}</dd>
        </div>
      ))}
    </dl>
  );
}

interface GuestFormProps {
  form: GuestFormState;
  onChange: (f: GuestFormState) => void;
  error: string;
  loading: boolean;
  onSubmit: () => void;
  onCancel: () => void;
  submitLabel: string;
}

function GuestForm({ form, onChange, error, loading, onSubmit, onCancel, submitLabel }: GuestFormProps) {
  const { t } = useTranslation();
  const field = (label: string, key: keyof GuestFormState, type = 'text', required = false) => (
    <div>
      <label className="block text-xs text-gray-600 mb-1">
        {label}{required && <span className="text-red-500 ml-0.5">*</span>}
      </label>
      {key === 'notes' ? (
        <textarea
          rows={2}
          value={form[key]}
          onChange={e => onChange({ ...form, [key]: e.target.value })}
          className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
      ) : (
        <input
          type={type}
          value={form[key]}
          onChange={e => onChange({ ...form, [key]: e.target.value })}
          className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
      )}
    </div>
  );

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="col-span-2">{field(t('common.fullName'), 'fullName', 'text', true)}</div>
        {field(t('common.email'), 'email', 'email')}
        {field(t('common.phone'), 'phone')}
        {field(t('guests.idNumber'), 'nationalId')}
        {field('Passport', 'passportNumber')}
        {field(t('guests.nationality'), 'nationality')}
      </div>
      <div>{field(t('common.notes'), 'notes')}</div>

      {error && <p className="text-red-600 text-sm">{error}</p>}

      <div className="flex gap-2 pt-1">
        <button
          onClick={onSubmit}
          disabled={loading}
          className="bg-blue-600 text-white px-4 py-2 rounded text-sm hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? t('common.saving') : submitLabel}
        </button>
        <button
          onClick={onCancel}
          disabled={loading}
          className="border px-4 py-2 rounded text-sm hover:bg-gray-50"
        >
          {t('common.cancel')}
        </button>
      </div>
    </div>
  );
}
