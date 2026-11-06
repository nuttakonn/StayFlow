import { useState, useCallback, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { MaintenanceRequestDto, PropertyDto, RoomDto } from '../types';
import { apiFetch } from '../lib/api';

interface Props {
  property: PropertyDto;
  token: string;
  orgId: string;
  rooms: RoomDto[];
}

// ── Config ────────────────────────────────────────────────────────────────────

const PRIORITY_CFG: Record<string, { labelKey: string; badge: string; dot: string }> = {
  Low:    { labelKey: 'maintenance.priorityLow',    badge: 'bg-gray-100 text-gray-700',   dot: 'bg-gray-400'   },
  Medium: { labelKey: 'maintenance.priorityMedium', badge: 'bg-blue-100 text-blue-700',   dot: 'bg-blue-500'   },
  High:   { labelKey: 'maintenance.priorityHigh',   badge: 'bg-orange-100 text-orange-700', dot: 'bg-orange-500' },
  Urgent: { labelKey: 'maintenance.priorityUrgent', badge: 'bg-red-100 text-red-700',    dot: 'bg-red-500'    },
};

const STATUS_CFG: Record<string, { labelKey: string; badge: string; col: string }> = {
  Open:       { labelKey: 'maintenance.statusOpen',       badge: 'bg-yellow-100 text-yellow-800', col: 'border-yellow-300' },
  InProgress: { labelKey: 'maintenance.statusInProgress', badge: 'bg-blue-100 text-blue-800',    col: 'border-blue-300'   },
  Resolved:   { labelKey: 'maintenance.statusResolved',   badge: 'bg-green-100 text-green-800',  col: 'border-green-300'  },
  Closed:     { labelKey: 'maintenance.statusClosed',     badge: 'bg-gray-100 text-gray-700',    col: 'border-gray-300'   },
};

const PRIORITY_OPTIONS = [
  { value: 1, labelKey: 'maintenance.priorityLow' },
  { value: 2, labelKey: 'maintenance.priorityMedium' },
  { value: 3, labelKey: 'maintenance.priorityHigh' },
  { value: 4, labelKey: 'maintenance.priorityUrgent' },
];

const STATUS_TRANSITIONS: Record<string, { value: number; labelKey: string }[]> = {
  Open:       [{ value: 2, labelKey: 'maintenance.startBtn' }, { value: 4, labelKey: 'maintenance.closeBtn' }],
  InProgress: [{ value: 3, labelKey: 'maintenance.resolveBtn' }, { value: 1, labelKey: 'maintenance.reopenBtn' }],
  Resolved:   [{ value: 4, labelKey: 'maintenance.closeBtn' }, { value: 2, labelKey: 'maintenance.reopenBtn' }],
  Closed:     [{ value: 1, labelKey: 'maintenance.reopenBtn' }],
};

function formatDate(d: string, locale: string) {
  return new Date(d).toLocaleDateString(locale, { day: '2-digit', month: 'short', year: 'numeric' });
}

// ── Component ─────────────────────────────────────────────────────────────────

export function MaintenanceView({ property, token, orgId, rooms }: Props) {
  const { t } = useTranslation();
  const [items, setItems] = useState<MaintenanceRequestDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [filterStatus, setFilterStatus] = useState<string>(''); // '' = all open+inprogress
  const [selected, setSelected] = useState<MaintenanceRequestDto | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  // Create form
  const [createForm, setCreateForm] = useState({ roomId: '', title: '', description: '', priority: 2 });
  const [createError, setCreateError] = useState('');
  const [createLoading, setCreateLoading] = useState(false);

  // Status update
  const [statusModal, setStatusModal] = useState<{ item: MaintenanceRequestDto; newStatus: number; labelKey: string } | null>(null);
  const [statusNote, setStatusNote] = useState('');
  const [statusAssign, setStatusAssign] = useState('');
  const [statusLoading, setStatusLoading] = useState(false);
  const [statusError, setStatusError] = useState('');

  const fetchItems = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const params = new URLSearchParams();
      if (filterStatus) params.set('status', filterStatus);
      const data = await apiFetch<MaintenanceRequestDto[]>(
        `/api/v1/properties/${property.id}/maintenance?${params}`, token, orgId
      );
      setItems(data);
    } catch {
      setError(t('maintenance.loadError'));
    } finally {
      setLoading(false);
    }
  }, [property.id, token, orgId, filterStatus, t]);

  useEffect(() => { fetchItems(); }, [fetchItems]);

  const handleCreate = async () => {
    if (!createForm.roomId) { setCreateError(t('walkIn.validationRoom')); return; }
    if (!createForm.title.trim()) { setCreateError('Please enter a title'); return; }
    setCreateLoading(true);
    setCreateError('');
    try {
      const created = await apiFetch<MaintenanceRequestDto>(
        `/api/v1/properties/${property.id}/maintenance`, token, orgId,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            roomId: createForm.roomId,
            title: createForm.title.trim(),
            description: createForm.description.trim() || null,
            priority: createForm.priority,
          }),
        }
      );
      setShowCreate(false);
      setCreateForm({ roomId: '', title: '', description: '', priority: 2 });
      setSelected(created);
      await fetchItems();
    } catch (e: unknown) {
      setCreateError(e instanceof Error ? e.message : t('maintenance.createError'));
    } finally {
      setCreateLoading(false);
    }
  };

  const confirmStatusUpdate = async () => {
    if (!statusModal) return;
    setStatusLoading(true);
    setStatusError('');
    try {
      const updated = await apiFetch<MaintenanceRequestDto>(
        `/api/v1/properties/${property.id}/maintenance/${statusModal.item.id}/status`,
        token, orgId,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            newStatus: statusModal.newStatus,
            assignedToEmail: statusAssign.trim() || null,
            resolutionNotes: statusNote.trim() || null,
          }),
        }
      );
      setStatusModal(null);
      setSelected(updated);
      await fetchItems();
    } catch (e: unknown) {
      setStatusError(e instanceof Error ? e.message : t('maintenance.updateError'));
    } finally {
      setStatusLoading(false);
    }
  };

  const countByStatus = (s: string) => items.filter(i => i.status === s || (s === '' && ['Open', 'InProgress'].includes(i.status))).length;

  const displayItems = filterStatus
    ? items.filter(i => i.status === filterStatus)
    : items.filter(i => ['Open', 'InProgress'].includes(i.status));

  const filterTabs = [
    { key: '', label: `${t('maintenance.statusOpen')} + ${t('maintenance.statusInProgress')}` },
    { key: 'Open', label: t('maintenance.statusOpen') },
    { key: 'InProgress', label: t('maintenance.statusInProgress') },
    { key: 'Resolved', label: t('maintenance.statusResolved') },
    { key: 'Closed', label: t('maintenance.statusClosed') },
  ];

  return (
    <div className="flex gap-4 h-full">
      {/* Left: filters + list */}
      <div className="w-80 flex-shrink-0 flex flex-col gap-3">
        {/* Status filter tabs */}
        <div className="flex gap-1 flex-wrap">
          {filterTabs.map(f => (
            <button
              key={f.key}
              onClick={() => setFilterStatus(f.key)}
              className={`text-xs px-2.5 py-1 rounded-full border transition-colors ${
                filterStatus === f.key
                  ? 'bg-blue-600 text-white border-blue-600'
                  : 'border-gray-300 text-gray-600 hover:bg-gray-50'
              }`}
            >
              {f.label}
              {f.key === '' && countByStatus('') > 0 && (
                <span className="ml-1 bg-red-500 text-white rounded-full text-xs px-1">{countByStatus('')}</span>
              )}
            </button>
          ))}
        </div>

        {/* Create button */}
        <button
          onClick={() => { setShowCreate(true); setSelected(null); setCreateError(''); }}
          className="bg-blue-600 text-white py-2 rounded text-sm hover:bg-blue-700 font-medium"
        >
          + {t('maintenance.createRequest')}
        </button>

        {/* List */}
        <div className="flex-1 overflow-y-auto border rounded bg-white divide-y">
          {loading && <div className="p-4 text-center text-gray-400 text-sm">{t('common.loading')}</div>}
          {!loading && error && <div className="p-4 text-center text-red-500 text-sm">{error}</div>}
          {!loading && !error && displayItems.length === 0 && (
            <div className="p-4 text-center text-gray-400 text-sm">{t('maintenance.noRequests')}</div>
          )}
          {displayItems.map(item => {
            const pCfg = PRIORITY_CFG[item.priority] ?? PRIORITY_CFG['Medium'];
            const sCfg = STATUS_CFG[item.status] ?? STATUS_CFG['Open'];
            return (
              <button
                key={item.id}
                onClick={() => { setSelected(item); setShowCreate(false); }}
                className={`w-full text-left px-3 py-2.5 hover:bg-blue-50 transition-colors ${selected?.id === item.id ? 'bg-blue-50 border-l-4 border-l-blue-500' : ''}`}
              >
                <div className="flex items-start gap-2">
                  <span className={`mt-1.5 w-2 h-2 rounded-full flex-shrink-0 ${pCfg.dot}`} />
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium text-gray-900 truncate">{item.title}</div>
                    <div className="flex items-center gap-1.5 mt-0.5">
                      <span className="text-xs text-gray-500">{t('common.room')} {item.roomNumber}</span>
                      <span className={`text-xs px-1.5 rounded-full ${sCfg.badge}`}>{t(sCfg.labelKey)}</span>
                    </div>
                  </div>
                </div>
              </button>
            );
          })}
        </div>
        <div className="text-xs text-gray-400 text-center">{t('common.total')}: {items.length}</div>
      </div>

      {/* Right: create form or detail */}
      <div className="flex-1 overflow-y-auto">
        {showCreate && (
          <div className="bg-white border rounded p-5 space-y-4">
            <h3 className="font-semibold text-gray-800">{t('maintenance.createRequest')}</h3>

            <div>
              <label className="text-xs text-gray-600 block mb-1">{t('common.room')} <span className="text-red-500">*</span></label>
              <select
                value={createForm.roomId}
                onChange={e => setCreateForm(f => ({ ...f, roomId: e.target.value }))}
                className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                <option value="">{t('walkIn.selectRoom')}</option>
                {rooms.filter(r => r.isActive).sort((a, b) => a.roomNumber.localeCompare(b.roomNumber)).map(r => (
                  <option key={r.id} value={r.id}>{t('common.room')} {r.roomNumber} — {r.roomTypeName}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="text-xs text-gray-600 block mb-1">Title <span className="text-red-500">*</span></label>
              <input
                type="text"
                value={createForm.title}
                onChange={e => setCreateForm(f => ({ ...f, title: e.target.value }))}
                placeholder="e.g. AC not cooling, pipe leak..."
                className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <div>
              <label className="text-xs text-gray-600 block mb-1">{t('common.description')}</label>
              <textarea
                rows={3}
                value={createForm.description}
                onChange={e => setCreateForm(f => ({ ...f, description: e.target.value }))}
                placeholder={t('maintenance.descriptionPlaceholder')}
                className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <div>
              <label className="text-xs text-gray-600 block mb-1">{t('maintenance.priority')}</label>
              <div className="flex gap-2">
                {PRIORITY_OPTIONS.map(p => (
                  <button
                    key={p.value}
                    onClick={() => setCreateForm(f => ({ ...f, priority: p.value }))}
                    className={`flex-1 py-1.5 rounded border text-xs font-medium transition-colors ${
                      createForm.priority === p.value
                        ? 'bg-blue-600 text-white border-blue-600'
                        : 'border-gray-300 text-gray-600 hover:bg-gray-50'
                    }`}
                  >
                    {t(p.labelKey)}
                  </button>
                ))}
              </div>
            </div>

            {createError && <p className="text-red-600 text-sm">{createError}</p>}

            <div className="flex gap-2">
              <button
                onClick={handleCreate}
                disabled={createLoading}
                className="bg-blue-600 text-white px-4 py-2 rounded text-sm hover:bg-blue-700 disabled:opacity-50"
              >
                {createLoading ? t('common.saving') : t('maintenance.createRequest')}
              </button>
              <button onClick={() => setShowCreate(false)} className="border px-4 py-2 rounded text-sm hover:bg-gray-50">
                {t('common.cancel')}
              </button>
            </div>
          </div>
        )}

        {!showCreate && selected && (
          <MaintenanceDetail
            item={selected}
            onTransition={(item, newStatus, labelKey) => {
              setStatusModal({ item, newStatus, labelKey });
              setStatusNote('');
              setStatusAssign(item.assignedToEmail ?? '');
              setStatusError('');
            }}
          />
        )}

        {!showCreate && !selected && (
          <div className="h-full flex items-center justify-center text-gray-400 text-sm">
            {t('common.noData')} — {t('maintenance.createRequest')}
          </div>
        )}
      </div>

      {/* Status update modal */}
      {statusModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-xl p-5 w-96 space-y-3">
            <h3 className="font-semibold text-gray-800">{t(statusModal.labelKey)}</h3>
            <p className="text-sm text-gray-600 truncate">"{statusModal.item.title}"</p>

            {statusModal.newStatus === 2 && (
              <div>
                <label className="text-xs text-gray-500 block mb-1">{t('maintenance.assignedTo')} ({t('common.email')})</label>
                <input
                  type="email"
                  value={statusAssign}
                  onChange={e => setStatusAssign(e.target.value)}
                  placeholder="Technician responsible..."
                  className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
              </div>
            )}

            {(statusModal.newStatus === 3 || statusModal.newStatus === 4) && (
              <div>
                <label className="text-xs text-gray-500 block mb-1">{t('common.notes')}</label>
                <textarea
                  rows={2}
                  value={statusNote}
                  onChange={e => setStatusNote(e.target.value)}
                  placeholder={t('maintenance.notePlaceholder')}
                  className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
              </div>
            )}

            {statusError && <p className="text-red-600 text-sm">{statusError}</p>}

            <div className="flex gap-2">
              <button
                onClick={confirmStatusUpdate}
                disabled={statusLoading}
                className="flex-1 bg-blue-600 text-white py-2 rounded text-sm hover:bg-blue-700 disabled:opacity-50"
              >
                {statusLoading ? t('common.saving') : t('common.confirm')}
              </button>
              <button
                onClick={() => setStatusModal(null)}
                disabled={statusLoading}
                className="flex-1 border py-2 rounded text-sm hover:bg-gray-50"
              >
                {t('common.cancel')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Detail panel ──────────────────────────────────────────────────────────────

function MaintenanceDetail({
  item,
  onTransition,
}: {
  item: MaintenanceRequestDto;
  onTransition: (item: MaintenanceRequestDto, newStatus: number, labelKey: string) => void;
}) {
  const { t, i18n } = useTranslation();
  const pCfg = PRIORITY_CFG[item.priority] ?? PRIORITY_CFG['Medium'];
  const sCfg = STATUS_CFG[item.status] ?? STATUS_CFG['Open'];
  const transitions = STATUS_TRANSITIONS[item.status] ?? [];

  return (
    <div className="bg-white border rounded p-5 space-y-4">
      {/* Header */}
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="font-semibold text-lg text-gray-800">{item.title}</h3>
          <p className="text-sm text-gray-500 mt-0.5">{t('common.room')} {item.roomNumber} · {t('maintenance.createdAt')} {formatDate(item.createdAtUtc, i18n.language)}</p>
        </div>
        <div className="flex flex-col items-end gap-1.5 flex-shrink-0">
          <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${sCfg.badge}`}>{t(sCfg.labelKey)}</span>
          <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${pCfg.badge}`}>{t(pCfg.labelKey)}</span>
        </div>
      </div>

      {/* Description */}
      {item.description && (
        <div className="bg-gray-50 rounded p-3 text-sm text-gray-700">{item.description}</div>
      )}

      {/* Meta grid */}
      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        {item.reportedByEmail && (
          <div>
            <dt className="text-xs text-gray-500">Reported by</dt>
            <dd className="text-gray-800">{item.reportedByEmail}</dd>
          </div>
        )}
        {item.assignedToEmail && (
          <div>
            <dt className="text-xs text-gray-500">{t('maintenance.assignedTo')}</dt>
            <dd className="text-gray-800">{item.assignedToEmail}</dd>
          </div>
        )}
        {item.resolvedAtUtc && (
          <div>
            <dt className="text-xs text-gray-500">{t('maintenance.resolvedAt')}</dt>
            <dd className="text-gray-800">{formatDate(item.resolvedAtUtc, i18n.language)}</dd>
          </div>
        )}
        {item.updatedAtUtc && (
          <div>
            <dt className="text-xs text-gray-500">Last updated</dt>
            <dd className="text-gray-800">{formatDate(item.updatedAtUtc, i18n.language)}</dd>
          </div>
        )}
      </dl>

      {/* Resolution notes */}
      {item.resolutionNotes && (
        <div>
          <p className="text-xs text-gray-500 mb-1">{t('common.notes')}</p>
          <div className="bg-green-50 border border-green-200 rounded p-3 text-sm text-green-800">
            {item.resolutionNotes}
          </div>
        </div>
      )}

      {/* Transition actions */}
      {transitions.length > 0 && (
        <div>
          <p className="text-xs text-gray-500 mb-2">{t('common.actions')}</p>
          <div className="flex flex-wrap gap-2">
            {transitions.map(tr => (
              <button
                key={tr.value}
                onClick={() => onTransition(item, tr.value, tr.labelKey)}
                className="px-4 py-1.5 rounded border text-sm font-medium text-gray-700 border-gray-300 hover:bg-gray-50 transition-colors"
              >
                {t(tr.labelKey)}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
