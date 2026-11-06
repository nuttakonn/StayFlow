import { useEffect, useState } from 'react';
import { History, Filter, RefreshCw, AlertCircle } from 'lucide-react';
import { fetchWithAuth } from '../lib/api';
import { useTranslation } from 'react-i18next';

interface AuditEvent {
  id: string;
  organizationId?: string;
  propertyId?: string;
  actorId?: string;
  actorEmail?: string;
  timestampUtc: string;
  entityName: string;
  entityId: string;
  action: string;
  changesJson?: string;
}

const ACTION_COLOR: Record<string, string> = {
  Created:   'bg-emerald-100 text-emerald-700',
  Updated:   'bg-blue-100 text-blue-700',
  Deleted:   'bg-rose-100 text-rose-700',
  CheckedIn: 'bg-indigo-100 text-indigo-700',
  CheckedOut:'bg-amber-100 text-amber-700',
  Cancelled: 'bg-slate-100 text-slate-600',
  Confirmed: 'bg-teal-100 text-teal-700',
};

function actionColor(action: string) {
  for (const [key, cls] of Object.entries(ACTION_COLOR)) {
    if (action.includes(key)) return cls;
  }
  return 'bg-slate-100 text-slate-600';
}

function formatChanges(json?: string): React.ReactNode {
  if (!json) return null;
  try {
    const obj = JSON.parse(json);
    return (
      <div className="mt-1 flex flex-wrap gap-1">
        {Object.entries(obj).map(([k, v]) => (
          <span key={k} className="text-[9px] px-1.5 py-0.5 bg-slate-100 rounded font-mono text-slate-500">
            {k}: {String(v)}
          </span>
        ))}
      </div>
    );
  } catch {
    return <span className="text-[9px] text-slate-400">{json.slice(0, 60)}</span>;
  }
}

export function AuditLogView() {
  const { t } = useTranslation();
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [filterEntity, setFilterEntity] = useState('');
  const [filterAction, setFilterAction] = useState('');
  const [filterActor, setFilterActor] = useState('');
  const [limit, setLimit] = useState(50);

  useEffect(() => { loadAudit(); }, []);

  const loadAudit = async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({ limit: String(limit) });
      if (filterEntity) params.set('entityName', filterEntity);
      if (filterAction) params.set('action', filterAction);
      if (filterActor)  params.set('actorEmail', filterActor);
      const res = await fetchWithAuth(`/api/v1/audit?${params}`);
      if (res.ok) setEvents(await res.json());
      else setError(t('audit.loadError'));
    } catch {
      setError(t('audit.loadError2'));
    } finally {
      setLoading(false);
    }
  };

  const handleFilter = (e: React.FormEvent) => { e.preventDefault(); loadAudit(); };

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h3 className="text-base font-black text-slate-900 flex items-center gap-2">
            <History className="w-4 h-4 text-indigo-500" />
            {t('audit.title')}
          </h3>
          <p className="text-xs text-slate-400 mt-0.5">{t('audit.subtitle', { count: events.length })}</p>
        </div>
        <button
          onClick={loadAudit}
          className="flex items-center gap-2 px-3 py-1.5 text-xs font-bold text-slate-600 bg-slate-100 hover:bg-slate-200 rounded-xl"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          {t('audit.refresh')}
        </button>
      </div>

      {/* Filters */}
      <form onSubmit={handleFilter} className="bg-white rounded-2xl border border-slate-200 p-4">
        <div className="flex items-center gap-2 mb-3">
          <Filter className="w-3.5 h-3.5 text-slate-400" />
          <span className="text-xs font-black text-slate-500 uppercase tracking-wider">{t('audit.filterTitle')}</span>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
          <div>
            <label className="text-[10px] font-semibold text-slate-500 block mb-1">{t('audit.entityLabel')}</label>
            <input
              type="text"
              value={filterEntity}
              onChange={(e) => setFilterEntity(e.target.value)}
              placeholder={t('audit.entityPlaceholder')}
              className="w-full px-3 py-1.5 border border-slate-300 rounded-xl text-xs focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label className="text-[10px] font-semibold text-slate-500 block mb-1">{t('audit.actionLabel')}</label>
            <input
              type="text"
              value={filterAction}
              onChange={(e) => setFilterAction(e.target.value)}
              placeholder={t('audit.actionPlaceholder')}
              className="w-full px-3 py-1.5 border border-slate-300 rounded-xl text-xs focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label className="text-[10px] font-semibold text-slate-500 block mb-1">{t('audit.actorLabel')}</label>
            <input
              type="text"
              value={filterActor}
              onChange={(e) => setFilterActor(e.target.value)}
              placeholder={t('audit.actorPlaceholder')}
              className="w-full px-3 py-1.5 border border-slate-300 rounded-xl text-xs focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label className="text-[10px] font-semibold text-slate-500 block mb-1">{t('audit.limitLabel')}</label>
            <select
              value={limit}
              onChange={(e) => setLimit(Number(e.target.value))}
              className="w-full px-3 py-1.5 border border-slate-300 rounded-xl text-xs bg-white focus:ring-2 focus:ring-blue-500"
            >
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
              <option value={200}>200</option>
            </select>
          </div>
        </div>
        <div className="mt-3">
          <button
            type="submit"
            className="px-4 py-1.5 text-xs font-bold text-white bg-indigo-600 hover:bg-indigo-700 rounded-xl"
          >
            {t('audit.searchBtn')}
          </button>
        </div>
      </form>

      {/* Error */}
      {error && (
        <div className="flex items-center gap-2 p-3 bg-rose-50 border border-rose-200 rounded-xl text-xs text-rose-700">
          <AlertCircle className="w-4 h-4" />{error}
        </div>
      )}

      {/* Events list */}
      {loading ? (
        <div className="bg-white rounded-2xl border border-slate-200 p-8 text-center text-slate-400 text-sm">{t('audit.loading')}</div>
      ) : events.length === 0 ? (
        <div className="bg-white rounded-2xl border border-slate-200 p-8 text-center text-slate-400 text-sm">
          {t('audit.noEvents')}
        </div>
      ) : (
        <div className="bg-white rounded-2xl border border-slate-200 overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50">
                  <th className="text-left px-5 py-3 font-black text-slate-500 uppercase tracking-wider whitespace-nowrap">{t('audit.colTime')}</th>
                  <th className="text-left px-5 py-3 font-black text-slate-500 uppercase tracking-wider whitespace-nowrap">{t('audit.colActor')}</th>
                  <th className="text-left px-5 py-3 font-black text-slate-500 uppercase tracking-wider whitespace-nowrap">{t('audit.colEntity')}</th>
                  <th className="text-left px-5 py-3 font-black text-slate-500 uppercase tracking-wider whitespace-nowrap">{t('audit.colAction')}</th>
                  <th className="text-left px-5 py-3 font-black text-slate-500 uppercase tracking-wider hidden lg:table-cell">{t('audit.colDetails')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {events.map((ev) => (
                  <tr key={ev.id} className="hover:bg-slate-50 transition-colors align-top">
                    <td className="px-5 py-3 whitespace-nowrap text-slate-500">
                      <div>{new Date(ev.timestampUtc).toLocaleDateString('th-TH')}</div>
                      <div className="text-slate-400">{new Date(ev.timestampUtc).toLocaleTimeString('th-TH', { hour: '2-digit', minute: '2-digit' })}</div>
                    </td>
                    <td className="px-5 py-3">
                      <span className="text-slate-700 font-medium">{ev.actorEmail ?? '—'}</span>
                    </td>
                    <td className="px-5 py-3">
                      <div className="font-semibold text-slate-800">{ev.entityName}</div>
                      <div className="text-slate-400 font-mono text-[9px]">{ev.entityId.slice(0, 8)}…</div>
                    </td>
                    <td className="px-5 py-3">
                      <span className={`px-2 py-0.5 rounded-full font-bold whitespace-nowrap ${actionColor(ev.action)}`}>
                        {ev.action}
                      </span>
                    </td>
                    <td className="px-5 py-3 hidden lg:table-cell max-w-xs">
                      {formatChanges(ev.changesJson)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
