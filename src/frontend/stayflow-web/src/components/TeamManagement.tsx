import { Fragment, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Users, UserPlus, Trash2, Shield, ChevronDown, ChevronUp,
  CheckCircle2, AlertCircle, Crown, RefreshCw, Building2, Plus, X
} from 'lucide-react';
import { fetchWithAuth, getStoredSession } from '../lib/api';

interface PropertyAssignment {
  propertyMembershipId: string;
  propertyId: string;
  propertyName: string;
  role: string;
}

interface Member {
  membershipId: string;
  userId: string;
  email: string;
  fullName: string;
  role: string;
  createdAtUtc: string;
  propertyAssignments: PropertyAssignment[];
}

const ROLE_OPTIONS = ['Owner', 'Admin', 'Manager', 'Staff', 'Viewer'];

const ROLE_COLOR: Record<string, string> = {
  Owner:   'bg-amber-100 text-amber-700 border-amber-200',
  Admin:   'bg-purple-100 text-purple-700 border-purple-200',
  Manager: 'bg-blue-100 text-blue-700 border-blue-200',
  Staff:   'bg-emerald-100 text-emerald-700 border-emerald-200',
  Viewer:  'bg-slate-100 text-slate-600 border-slate-200',
};

export function TeamManagement() {
  const { t } = useTranslation();
  const ROLE_LABEL: Record<string, string> = {
    Owner: t('team.roleOwner'),
    Admin: t('team.roleAdmin'),
    Manager: t('team.roleManager'),
    Staff: t('team.roleStaff'),
    Viewer: t('team.roleViewer'),
  };
  const [members, setMembers] = useState<Member[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // Add member form
  const [showAddForm, setShowAddForm] = useState(false);
  const [newEmail, setNewEmail] = useState('');
  const [newName, setNewName] = useState('');
  const [newRole, setNewRole] = useState('Staff');
  const [adding, setAdding] = useState(false);

  // Role change
  const [changingRole, setChangingRole] = useState<string | null>(null);
  const [sessionStale, setSessionStale] = useState(false);
  const [recovering, setRecovering] = useState(false);

  const recoverSession = async () => {
    setSessionStale(true);
    setRecovering(true);
    try {
      const r = await fetch('/api/v1/auth/quick-demo', { method: 'POST' });
      if (r.ok) {
        const auth = await r.json();
        localStorage.setItem('stayflow_auth_token', auth.accessToken);
        if (auth.refreshToken) localStorage.setItem('stayflow_refresh_token', auth.refreshToken);
        if (auth.activeOrganizationId) localStorage.setItem('stayflow_active_org_id', auth.activeOrganizationId);
        if (auth.user) localStorage.setItem('stayflow_user', JSON.stringify(auth.user));
      }
    } catch { /* reload anyway */ }
    setTimeout(() => window.location.reload(), 1500);
  };

  // Remove confirm
  const [confirmRemove, setConfirmRemove] = useState<Member | null>(null);
  const [removing, setRemoving] = useState(false);

  // Property assignments
  const [expandedMember, setExpandedMember] = useState<string | null>(null);
  const [properties, setProperties] = useState<{ id: string; name: string }[]>([]);
  const [addingProperty, setAddingProperty] = useState<string | null>(null); // membershipId being assigned
  const [newPropId, setNewPropId] = useState('');
  const [newPropRole, setNewPropRole] = useState('Staff');
  const [savingProp, setSavingProp] = useState(false);
  const [removingProp, setRemovingProp] = useState<string | null>(null);

  const session = getStoredSession();
  const orgId = session?.organizationId;

  useEffect(() => {
    loadMembers();
    loadProperties();
  }, []);

  const loadProperties = async () => {
    try {
      const res = await fetchWithAuth('/api/v1/properties');
      if (res.ok) {
        const data = await res.json();
        const list = Array.isArray(data) ? data : (data.items ?? []);
        setProperties(list.map((p: { id: string; name: string }) => ({ id: p.id, name: p.name })));
      } else if (res.status === 403 || res.status === 401) {
        await recoverSession();
      }
    } catch { /* non-critical */ }
  };

  const handleAssignProperty = async (membershipId: string) => {
    if (!orgId || !newPropId) return;
    setSavingProp(true);
    try {
      const res = await fetchWithAuth(
        `/api/v1/organizations/${orgId}/members/${membershipId}/properties`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ propertyId: newPropId, role: newPropRole }),
        }
      );
      if (res.ok) {
        setSuccess(t('team.assignPropertySuccess'));
        setAddingProperty(null);
        setNewPropId('');
        setNewPropRole('Staff');
        await loadMembers();
        setTimeout(() => setSuccess(null), 3000);
      } else if (res.status === 403 || res.status === 401) {
        await recoverSession();
      } else {
        const err = await res.json().catch(() => ({}));
        setError(`[${res.status}] ${err?.detail ?? t('team.assignPropertyError')}`);
      }
    } finally {
      setSavingProp(false);
    }
  };

  const handleRemovePropertyAssignment = async (membershipId: string, propertyMembershipId: string) => {
    if (!orgId) return;
    setRemovingProp(propertyMembershipId);
    try {
      const res = await fetchWithAuth(
        `/api/v1/organizations/${orgId}/members/${membershipId}/properties/${propertyMembershipId}`,
        { method: 'DELETE' }
      );
      if (res.ok || res.status === 204) {
        await loadMembers();
      } else {
        const err = await res.json().catch(() => ({}));
        setError(err?.detail ?? t('team.assignPropertyError'));
      }
    } finally {
      setRemovingProp(null);
    }
  };

  const loadMembers = async () => {
    if (!orgId) return;
    setLoading(true);
    setError(null);
    setSessionStale(false);
    try {
      const res = await fetchWithAuth(`/api/v1/organizations/${orgId}`);
      if (res.ok) {
        const data = await res.json();
        const updated = (data.members ?? []).map((m: Member & { propertyAssignments?: PropertyAssignment[] }) => ({
          ...m,
          propertyAssignments: m.propertyAssignments ?? [],
        }));
        setMembers(updated);
        // Close expanded panel if that member is now Owner/Admin (no longer needs property assignment)
        setExpandedMember(prev => {
          if (!prev) return prev;
          const m = updated.find((x: Member) => x.membershipId === prev);
          return m && (m.role === 'Owner' || m.role === 'Admin') ? null : prev;
        });
      } else if (res.status === 403 || res.status === 404) {
        await recoverSession();
      } else {
        setError(`[${res.status}] ${t('team.loadError')}`);
      }
    } catch {
      setError(t('team.loadError2'));
    } finally {
      setLoading(false);
    }
  };

  const handleAddMember = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!orgId) return;
    setAdding(true);
    setError(null);
    try {
      const res = await fetchWithAuth(`/api/v1/organizations/${orgId}/members`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: newEmail, fullName: newName, role: newRole }),
      });
      if (res.ok) {
        setSuccess(t('team.addSuccess', { name: newName || newEmail }));
        setNewEmail(''); setNewName(''); setNewRole('Staff');
        setShowAddForm(false);
        await loadMembers();
        setTimeout(() => setSuccess(null), 4000);
      } else {
        const err = await res.json().catch(() => ({}));
        setError(err?.detail ?? t('team.addError'));
      }
    } catch {
      setError(t('team.loadError2'));
    } finally {
      setAdding(false);
    }
  };

  const handleChangeRole = async (member: Member, newRole: string) => {
    if (!orgId) return;
    setChangingRole(member.membershipId);
    setSessionStale(false);
    try {
      const res = await fetchWithAuth(
        `/api/v1/organizations/${orgId}/members/${member.membershipId}`,
        { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ newRole }) }
      );
      if (res.ok) {
        setSuccess(t('team.changeRoleSuccess', { name: member.fullName, role: ROLE_LABEL[newRole] ?? newRole }));
        await loadMembers();
        setTimeout(() => setSuccess(null), 3000);
      } else if (res.status === 403 || res.status === 404) {
        const err = await res.json().catch(() => ({}));
        const detail = err?.detail ?? '';
        if (detail.includes('Only') || res.status === 404) {
          await recoverSession();
        } else {
          setError(`[${res.status}] ${detail || t('team.changeRoleError')}`);
        }
      } else {
        const err = await res.json().catch(() => ({}));
        setError(`[${res.status}] ${err?.detail ?? t('team.changeRoleError')}`);
      }
    } finally {
      setChangingRole(null);
    }
  };

  const handleRemoveMember = async () => {
    if (!orgId || !confirmRemove) return;
    setRemoving(true);
    try {
      const res = await fetchWithAuth(
        `/api/v1/organizations/${orgId}/members/${confirmRemove.membershipId}`,
        { method: 'DELETE' }
      );
      if (res.ok || res.status === 204) {
        setSuccess(t('team.removeSuccess', { name: confirmRemove.fullName }));
        setConfirmRemove(null);
        await loadMembers();
        setTimeout(() => setSuccess(null), 3000);
      } else {
        const err = await res.json().catch(() => ({}));
        setError(err?.detail ?? t('team.removeError'));
        setConfirmRemove(null);
      }
    } finally {
      setRemoving(false);
    }
  };

  if (loading) return <div className="bg-white rounded-2xl border border-slate-200 p-8 text-center text-slate-400 text-sm">{t('common.loading')}</div>;

  return (
    <div className="space-y-5">
      {/* Header row */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h3 className="text-base font-black text-slate-900 flex items-center gap-2">
            <Users className="w-4 h-4 text-blue-500" />
            {t('team.title')}
          </h3>
          <p className="text-xs text-slate-400 mt-0.5">{t('team.membersCount', { count: members.length })}</p>
        </div>
        <button
          onClick={() => { setShowAddForm((v) => !v); setError(null); }}
          className="flex items-center gap-2 px-4 py-2 text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 rounded-xl shadow-md"
        >
          <UserPlus className="w-4 h-4" />
          {t('team.addMember')}
        </button>
      </div>

      {/* Notifications */}
      {sessionStale && (
        <div className="p-4 bg-amber-50 border border-amber-200 rounded-xl">
          <p className="flex items-center gap-2 text-xs font-bold text-amber-800">
            <RefreshCw className={`w-4 h-4 shrink-0 ${recovering ? 'animate-spin' : ''}`} />
            {recovering ? t('team.sessionRecovering') : t('team.sessionExpired')}
          </p>
          {recovering && (
            <p className="text-xs text-amber-700 mt-1">{t('team.sessionReloading')}</p>
          )}
        </div>
      )}
      {error && (
        <div className="flex items-center gap-2 p-3 bg-rose-50 border border-rose-200 rounded-xl text-xs text-rose-700 font-medium">
          <AlertCircle className="w-4 h-4 shrink-0" />{error}
          <button onClick={() => setError(null)} className="ml-auto text-rose-400 hover:text-rose-600">✕</button>
        </div>
      )}
      {success && (
        <div className="flex items-center gap-2 p-3 bg-emerald-50 border border-emerald-200 rounded-xl text-xs text-emerald-700 font-medium">
          <CheckCircle2 className="w-4 h-4 shrink-0" />{success}
        </div>
      )}

      {/* Add member form */}
      {showAddForm && (
        <div className="bg-blue-50 border border-blue-200 rounded-2xl p-5">
          <h4 className="text-sm font-black text-blue-800 mb-3">{t('team.addMemberTitle')}</h4>
          <form onSubmit={handleAddMember} className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div>
                <label className="text-xs font-semibold text-slate-600 block mb-1">{t('team.emailLabel')}</label>
                <input
                  type="email"
                  required
                  value={newEmail}
                  onChange={(e) => setNewEmail(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 bg-white"
                  placeholder={t('team.emailPlaceholder')}
                />
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-600 block mb-1">{t('team.nameLabel')}</label>
                <input
                  type="text"
                  required
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 bg-white"
                  placeholder={t('team.namePlaceholder')}
                />
              </div>
            </div>
            <div className="max-w-xs">
              <label className="text-xs font-semibold text-slate-600 block mb-1">{t('team.roleLabel')}</label>
              <select
                value={newRole}
                onChange={(e) => setNewRole(e.target.value)}
                className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm bg-white focus:ring-2 focus:ring-blue-500"
              >
                {ROLE_OPTIONS.filter(r => r !== 'Owner').map(r => (
                  <option key={r} value={r}>{ROLE_LABEL[r]} ({r})</option>
                ))}
              </select>
            </div>
            <div className="flex gap-2">
              <button
                type="submit"
                disabled={adding}
                className="px-4 py-2 text-xs font-bold text-white bg-blue-600 hover:bg-blue-700 rounded-xl disabled:opacity-60"
              >
                {adding ? t('team.adding') : t('team.addBtn')}
              </button>
              <button
                type="button"
                onClick={() => setShowAddForm(false)}
                className="px-4 py-2 text-xs font-semibold text-slate-600 bg-white hover:bg-slate-100 rounded-xl border border-slate-200"
              >
                {t('common.cancel')}
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Members table */}
      <div className="bg-white rounded-2xl border border-slate-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 bg-slate-50">
                <th className="text-left px-5 py-3 text-xs font-black text-slate-500 uppercase tracking-wider">{t('team.memberColumn')}</th>
                <th className="text-left px-5 py-3 text-xs font-black text-slate-500 uppercase tracking-wider hidden sm:table-cell">{t('team.emailColumn')}</th>
                <th className="text-left px-5 py-3 text-xs font-black text-slate-500 uppercase tracking-wider">{t('team.orgRoleColumn')}</th>
                <th className="text-left px-5 py-3 text-xs font-black text-slate-500 uppercase tracking-wider hidden md:table-cell">{t('team.propertyAccessColumn')}</th>
                <th className="px-5 py-3"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {members.map((m) => (
                <Fragment key={m.membershipId}>
                  <tr className="hover:bg-slate-50 transition-colors">
                    <td className="px-5 py-3.5">
                      <div className="flex items-center gap-3">
                        <div className="w-8 h-8 rounded-full bg-gradient-to-br from-blue-500 to-indigo-600 text-white flex items-center justify-center text-xs font-black shrink-0">
                          {m.fullName.charAt(0).toUpperCase()}
                        </div>
                        <div>
                          <p className="font-semibold text-slate-900 text-xs flex items-center gap-1">
                            {m.role === 'Owner' && <Crown className="w-3 h-3 text-amber-500" />}
                            {m.fullName}
                          </p>
                          <p className="text-[10px] text-slate-400 sm:hidden">{m.email}</p>
                        </div>
                      </div>
                    </td>
                    <td className="px-5 py-3.5 hidden sm:table-cell">
                      <span className="text-xs text-slate-500">{m.email}</span>
                    </td>
                    <td className="px-5 py-3.5">
                      {m.role === 'Owner' ? (
                        <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-[10px] font-black border ${ROLE_COLOR[m.role]}`}>
                          <Crown className="w-2.5 h-2.5" />
                          {ROLE_LABEL[m.role]}
                        </span>
                      ) : (
                        <div className="relative inline-block">
                          <select
                            value={m.role}
                            onChange={(e) => handleChangeRole(m, e.target.value)}
                            disabled={changingRole === m.membershipId}
                            className={`appearance-none pr-6 pl-2.5 py-1 rounded-full text-[10px] font-black border cursor-pointer focus:outline-none focus:ring-2 focus:ring-blue-400 ${ROLE_COLOR[m.role]} disabled:opacity-60`}
                          >
                            {ROLE_OPTIONS.filter(r => r !== 'Owner').map(r => (
                              <option key={r} value={r}>{ROLE_LABEL[r]}</option>
                            ))}
                          </select>
                          <ChevronDown className="w-2.5 h-2.5 absolute right-1.5 top-1/2 -translate-y-1/2 pointer-events-none opacity-60" />
                        </div>
                      )}
                    </td>
                    <td className="px-5 py-3.5 hidden md:table-cell">
                      {m.role === 'Owner' || m.role === 'Admin' ? (
                        <span className="text-[10px] text-slate-400 italic">{t('team.allProperties')}</span>
                      ) : (
                        <button
                          onClick={() => setExpandedMember(expandedMember === m.membershipId ? null : m.membershipId)}
                          className="flex items-center gap-1 text-[10px] font-semibold text-blue-600 hover:text-blue-800"
                        >
                          <Building2 className="w-3 h-3" />
                          {m.propertyAssignments.length > 0
                            ? t('team.propertiesCount', { count: m.propertyAssignments.length })
                            : t('team.noProperties')}
                          {expandedMember === m.membershipId
                            ? <ChevronUp className="w-3 h-3" />
                            : <ChevronDown className="w-3 h-3" />}
                        </button>
                      )}
                    </td>
                    <td className="px-5 py-3.5 text-right">
                      {m.role !== 'Owner' && (
                        <button
                          onClick={() => setConfirmRemove(m)}
                          className="p-1.5 text-slate-400 hover:text-rose-500 hover:bg-rose-50 rounded-lg transition-colors"
                          title={t('team.removeFromOrg')}
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      )}
                    </td>
                  </tr>
                  {/* Property assignment panel */}
                  {expandedMember === m.membershipId && m.role !== 'Owner' && m.role !== 'Admin' && (
                    <tr className="bg-blue-50/40">
                      <td colSpan={5} className="px-5 py-3">
                        <div className="space-y-2">
                          <p className="text-[10px] font-black text-slate-500 uppercase tracking-wider flex items-center gap-1">
                            <Building2 className="w-3 h-3" />{t('team.propertyAccessLabel', { name: m.fullName })}
                          </p>
                          {m.propertyAssignments.length === 0 && (
                            <p className="text-xs text-slate-400">{t('team.noPropertiesAssigned')}</p>
                          )}
                          <div className="flex flex-wrap gap-2">
                            {m.propertyAssignments.map(a => (
                              <span key={a.propertyMembershipId} className="inline-flex items-center gap-1.5 px-2.5 py-1 bg-white border border-slate-200 rounded-lg text-[10px] font-semibold text-slate-700">
                                <Building2 className="w-3 h-3 text-blue-400" />
                                {a.propertyName}
                                <span className={`px-1.5 py-0.5 rounded-full text-[9px] font-black border ${ROLE_COLOR[a.role] ?? 'bg-slate-100 text-slate-500 border-slate-200'}`}>
                                  {ROLE_LABEL[a.role] ?? a.role}
                                </span>
                                <button
                                  onClick={() => handleRemovePropertyAssignment(m.membershipId, a.propertyMembershipId)}
                                  disabled={removingProp === a.propertyMembershipId}
                                  className="ml-0.5 text-slate-300 hover:text-rose-500 disabled:opacity-40"
                                >
                                  <X className="w-3 h-3" />
                                </button>
                              </span>
                            ))}
                          </div>
                          {/* Add property assignment inline form */}
                          {addingProperty === m.membershipId ? (
                            <div className="flex flex-wrap items-end gap-2 mt-1">
                              <div>
                                <label className="text-[10px] font-semibold text-slate-500 block mb-0.5">{t('team.propertyLabel')}</label>
                                <select
                                  value={newPropId}
                                  onChange={(e) => setNewPropId(e.target.value)}
                                  className="px-2 py-1.5 border border-slate-300 rounded-lg text-xs bg-white focus:ring-2 focus:ring-blue-400"
                                >
                                  <option value="">{t('team.selectPropertyPrompt')}</option>
                                  {properties
                                    .filter(p => !m.propertyAssignments.some(a => a.propertyId === p.id))
                                    .map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                                </select>
                              </div>
                              <div>
                                <label className="text-[10px] font-semibold text-slate-500 block mb-0.5">{t('team.roleLabel')}</label>
                                <select
                                  value={newPropRole}
                                  onChange={(e) => setNewPropRole(e.target.value)}
                                  className="px-2 py-1.5 border border-slate-300 rounded-lg text-xs bg-white focus:ring-2 focus:ring-blue-400"
                                >
                                  {ROLE_OPTIONS.filter(r => r !== 'Owner' && r !== 'Admin').map(r => (
                                    <option key={r} value={r}>{ROLE_LABEL[r]} ({r})</option>
                                  ))}
                                </select>
                              </div>
                              <button
                                onClick={() => handleAssignProperty(m.membershipId)}
                                disabled={savingProp || !newPropId}
                                className="px-3 py-1.5 text-xs font-bold text-white bg-blue-600 hover:bg-blue-700 rounded-lg disabled:opacity-50"
                              >
                                {savingProp ? t('common.saving') : t('team.assignProperty')}
                              </button>
                              <button
                                onClick={() => { setAddingProperty(null); setNewPropId(''); }}
                                className="px-3 py-1.5 text-xs font-semibold text-slate-600 bg-white hover:bg-slate-100 rounded-lg border border-slate-200"
                              >
                                {t('common.cancel')}
                              </button>
                            </div>
                          ) : (
                            <button
                              onClick={() => { setAddingProperty(m.membershipId); setNewPropId(''); setNewPropRole('Staff'); }}
                              className="flex items-center gap-1 px-2.5 py-1.5 text-[10px] font-bold text-blue-700 bg-blue-100 hover:bg-blue-200 rounded-lg border border-blue-200 mt-1"
                            >
                              <Plus className="w-3 h-3" />{t('team.assignProperty')}
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Role legend */}
      <div className="bg-slate-50 rounded-xl border border-slate-200 p-4 space-y-3">
        <div className="flex items-center gap-2">
          <Shield className="w-3.5 h-3.5 text-slate-400" />
          <span className="text-xs font-black text-slate-500 uppercase tracking-wider">{t('team.roleLegendTitle')}</span>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2 text-xs text-slate-600">
          <span><strong>{t('team.roleOwner')} (Owner)</strong> — {t('team.roleLegendOwner')}</span>
          <span><strong>{t('team.roleAdmin')} (Admin)</strong> — {t('team.roleLegendAdmin')}</span>
          <span><strong>{t('team.roleManager')} (Manager)</strong> — {t('team.roleLegendManager')}</span>
          <span><strong>{t('team.roleStaff')} (Staff)</strong> — {t('team.roleLegendStaff')}</span>
          <span><strong>{t('team.roleViewer')} (Viewer)</strong> — {t('team.roleLegendViewer')}</span>
        </div>
        <div className="flex items-start gap-2 p-2.5 bg-blue-50 rounded-lg border border-blue-100">
          <Building2 className="w-3.5 h-3.5 text-blue-400 shrink-0 mt-0.5" />
          <p className="text-[10px] text-blue-700">
            {t('team.propertyNote')}
          </p>
        </div>
      </div>

      {/* Remove confirm dialog */}
      {confirmRemove && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm">
          <div className="bg-white rounded-2xl shadow-2xl max-w-sm w-full p-6 space-y-4">
            <div className="flex items-start gap-3">
              <div className="w-10 h-10 rounded-xl bg-rose-100 flex items-center justify-center shrink-0">
                <Trash2 className="w-5 h-5 text-rose-600" />
              </div>
              <div>
                <h4 className="font-black text-slate-900">{t('team.removeMemberTitle')}</h4>
                <p className="text-sm text-slate-500 mt-1">
                  {t('team.removeMemberMsg', { name: confirmRemove.fullName, email: confirmRemove.email })}
                </p>
              </div>
            </div>
            <div className="flex gap-2 justify-end">
              <button
                onClick={() => setConfirmRemove(null)}
                className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 hover:bg-slate-200 rounded-xl"
              >
                {t('common.cancel')}
              </button>
              <button
                onClick={handleRemoveMember}
                disabled={removing}
                className="px-4 py-2 text-xs font-bold text-white bg-rose-600 hover:bg-rose-700 rounded-xl disabled:opacity-60"
              >
                {removing ? t('team.removing') : t('team.removeBtn')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
