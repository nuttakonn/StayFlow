import { useState, useCallback, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { BuildingDto, FloorDto, PropertyDto } from '../types';
import { apiFetch } from '../lib/api';

interface Props {
  property: PropertyDto;
  token: string;
  orgId: string;
  onRefresh?: () => void;
}

type Mode =
  | { type: 'idle' }
  | { type: 'newBuilding' }
  | { type: 'editBuilding'; building: BuildingDto }
  | { type: 'newFloor'; buildingId?: string }
  | { type: 'editFloor'; floor: FloorDto };

export function BuildingFloorManager({ property, token, orgId, onRefresh }: Props) {
  const { t } = useTranslation();
  const [buildings, setBuildings] = useState<BuildingDto[]>([]);
  const [floors, setFloors] = useState<FloorDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [mode, setMode] = useState<Mode>({ type: 'idle' });
  const [formError, setFormError] = useState('');
  const [formLoading, setFormLoading] = useState(false);

  // Building form
  const [bName, setBName] = useState('');
  const [bCode, setBCode] = useState('');
  const [bFloors, setBFloors] = useState(1);

  // Floor form
  const [fBuildingId, setFBuildingId] = useState('');
  const [fNumber, setFNumber] = useState(1);
  const [fName, setFName] = useState('');

  const fetchData = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [b, f] = await Promise.all([
        apiFetch<BuildingDto[]>(`/api/v1/properties/${property.id}/buildings`, token, orgId),
        apiFetch<FloorDto[]>(`/api/v1/properties/${property.id}/floors`, token, orgId),
      ]);
      setBuildings(b);
      setFloors(f);
    } catch {
      setError(t('common.error'));
    } finally {
      setLoading(false);
    }
  }, [property.id, token, orgId, t]);

  useEffect(() => { fetchData(); }, [fetchData]);

  const openNewBuilding = () => {
    setBName(''); setBCode(''); setBFloors(1);
    setFormError('');
    setMode({ type: 'newBuilding' });
  };

  const openEditBuilding = (b: BuildingDto) => {
    setBName(b.name); setBCode(b.code); setBFloors(b.numberOfFloors);
    setFormError('');
    setMode({ type: 'editBuilding', building: b });
  };

  const openNewFloor = (buildingId?: string) => {
    setFBuildingId(buildingId ?? '');
    setFNumber(1); setFName('');
    setFormError('');
    setMode({ type: 'newFloor', buildingId });
  };

  const openEditFloor = (f: FloorDto) => {
    setFBuildingId(f.buildingId ?? '');
    setFNumber(f.floorNumber); setFName(f.name);
    setFormError('');
    setMode({ type: 'editFloor', floor: f });
  };

  const handleSaveBuilding = async () => {
    if (!bName.trim()) { setFormError('Please enter building name'); return; }
    if (!bCode.trim()) { setFormError('Please enter building code'); return; }
    setFormLoading(true); setFormError('');
    try {
      const body = JSON.stringify({ name: bName.trim(), code: bCode.trim(), numberOfFloors: bFloors });
      if (mode.type === 'newBuilding') {
        await apiFetch<BuildingDto>(`/api/v1/properties/${property.id}/buildings`, token, orgId,
          { method: 'POST', headers: { 'Content-Type': 'application/json' }, body });
      } else if (mode.type === 'editBuilding') {
        await apiFetch<BuildingDto>(`/api/v1/properties/${property.id}/buildings/${mode.building.id}`, token, orgId,
          { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body });
      }
      setMode({ type: 'idle' });
      await fetchData();
      onRefresh?.();
    } catch (e: unknown) {
      setFormError(e instanceof Error ? e.message : t('common.error'));
    } finally {
      setFormLoading(false);
    }
  };

  const handleDeactivateBuilding = async (b: BuildingDto) => {
    if (!confirm(`Confirm deactivate building "${b.name}"?`)) return;
    try {
      await apiFetch(`/api/v1/properties/${property.id}/buildings/${b.id}/deactivate`, token, orgId,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
      await fetchData();
      onRefresh?.();
    } catch (e: unknown) {
      alert(e instanceof Error ? e.message : t('common.error'));
    }
  };

  const handleSaveFloor = async () => {
    setFormLoading(true); setFormError('');
    const floorName = fName.trim() || `Floor ${fNumber}`;
    try {
      const body = JSON.stringify({
        buildingId: fBuildingId || null,
        floorNumber: fNumber,
        name: floorName,
      });
      if (mode.type === 'newFloor') {
        await apiFetch<FloorDto>(`/api/v1/properties/${property.id}/floors`, token, orgId,
          { method: 'POST', headers: { 'Content-Type': 'application/json' }, body });
      } else if (mode.type === 'editFloor') {
        await apiFetch<FloorDto>(`/api/v1/properties/${property.id}/floors/${mode.floor.id}`, token, orgId,
          { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body });
      }
      setMode({ type: 'idle' });
      await fetchData();
      onRefresh?.();
    } catch (e: unknown) {
      setFormError(e instanceof Error ? e.message : t('common.error'));
    } finally {
      setFormLoading(false);
    }
  };

  const floorsByBuilding = (buildingId: string) =>
    floors.filter(f => f.buildingId === buildingId).sort((a, b) => a.floorNumber - b.floorNumber);

  const unattachedFloors = floors.filter(f => !f.buildingId).sort((a, b) => a.floorNumber - b.floorNumber);
  const activeBuildings = buildings.filter(b => b.isActive);
  const inactiveBuildings = buildings.filter(b => !b.isActive);

  const isBuilding = mode.type === 'newBuilding' || mode.type === 'editBuilding';
  const isFloor = mode.type === 'newFloor' || mode.type === 'editFloor';

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h3 className="font-semibold text-gray-800">Buildings &amp; Floors</h3>
        <div className="flex gap-2">
          <button onClick={() => openNewFloor()} className="text-sm border px-3 py-1.5 rounded hover:bg-gray-50">
            + Floor
          </button>
          <button onClick={openNewBuilding} className="bg-blue-600 text-white text-sm px-3 py-1.5 rounded hover:bg-blue-700">
            + Building
          </button>
        </div>
      </div>

      {error && <div className="bg-red-50 text-red-700 text-sm px-3 py-2 rounded">{error}</div>}
      {loading && <div className="text-gray-400 text-sm">{t('common.loading')}</div>}

      {/* Inline form */}
      {(isBuilding || isFloor) && (
        <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 space-y-3">
          <h4 className="font-medium text-blue-900 text-sm">
            {mode.type === 'newBuilding' && 'Add New Building'}
            {mode.type === 'editBuilding' && `Edit Building "${(mode as { building: BuildingDto }).building.name}"`}
            {mode.type === 'newFloor' && 'Add New Floor'}
            {mode.type === 'editFloor' && `Edit Floor "${(mode as { floor: FloorDto }).floor.name}"`}
          </h4>

          {isBuilding && (
            <div className="grid grid-cols-3 gap-3">
              <div className="col-span-1">
                <label className="text-xs text-gray-600 block mb-1">Building Code <span className="text-red-500">*</span></label>
                <input value={bCode} onChange={e => setBCode(e.target.value.toUpperCase())}
                  placeholder="A, B, MAIN…" maxLength={10}
                  className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
              <div className="col-span-1">
                <label className="text-xs text-gray-600 block mb-1">Building Name <span className="text-red-500">*</span></label>
                <input value={bName} onChange={e => setBName(e.target.value)}
                  placeholder="Building A, Main Building…"
                  className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
              <div>
                <label className="text-xs text-gray-600 block mb-1">Number of Floors</label>
                <input type="number" min={1} max={100} value={bFloors} onChange={e => setBFloors(Number(e.target.value))}
                  className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
            </div>
          )}

          {isFloor && (
            <div className="grid grid-cols-3 gap-3">
              <div>
                <label className="text-xs text-gray-600 block mb-1">Building</label>
                <select value={fBuildingId} onChange={e => setFBuildingId(e.target.value)}
                  className="w-full border rounded px-2.5 py-1.5 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-blue-500">
                  <option value="">— No Building —</option>
                  {activeBuildings.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}
                </select>
              </div>
              <div>
                <label className="text-xs text-gray-600 block mb-1">Floor Number <span className="text-red-500">*</span></label>
                <input type="number" min={-5} max={200} value={fNumber} onChange={e => setFNumber(Number(e.target.value))}
                  className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
              <div>
                <label className="text-xs text-gray-600 block mb-1">Floor Name</label>
                <input value={fName} onChange={e => setFName(e.target.value)}
                  placeholder={`Floor ${fNumber}`}
                  className="w-full border rounded px-2.5 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
            </div>
          )}

          {formError && <p className="text-red-600 text-sm">{formError}</p>}

          <div className="flex gap-2">
            <button
              onClick={isBuilding ? handleSaveBuilding : handleSaveFloor}
              disabled={formLoading}
              className="bg-blue-600 text-white px-4 py-1.5 rounded text-sm hover:bg-blue-700 disabled:opacity-50"
            >
              {formLoading ? t('common.saving') : t('common.save')}
            </button>
            <button onClick={() => { setMode({ type: 'idle' }); setFormError(''); }}
              className="border px-4 py-1.5 rounded text-sm hover:bg-gray-50">
              {t('common.cancel')}
            </button>
          </div>
        </div>
      )}

      {/* Buildings with their floors */}
      {!loading && activeBuildings.length === 0 && floors.length === 0 && (
        <div className="text-center py-10 text-gray-400 text-sm border rounded-lg">
          No buildings or floors yet — click "+ Building" or "+ Floor" to start
        </div>
      )}

      <div className="space-y-3">
        {activeBuildings.map(b => (
          <div key={b.id} className="border rounded-lg overflow-hidden">
            {/* Building header */}
            <div className="bg-gray-50 px-4 py-2.5 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="bg-blue-100 text-blue-700 text-xs font-bold px-2 py-0.5 rounded">{b.code}</span>
                <span className="font-medium text-gray-800">{b.name}</span>
                <span className="text-xs text-gray-400">{b.numberOfFloors} floors</span>
              </div>
              <div className="flex gap-2">
                <button onClick={() => openNewFloor(b.id)}
                  className="text-xs text-blue-600 hover:underline">+ Floor</button>
                <button onClick={() => openEditBuilding(b)}
                  className="text-xs text-gray-500 hover:text-gray-700">{t('common.edit')}</button>
                <button onClick={() => handleDeactivateBuilding(b)}
                  className="text-xs text-red-500 hover:text-red-700">Deactivate</button>
              </div>
            </div>

            {/* Floors under this building */}
            {floorsByBuilding(b.id).length > 0 ? (
              <div className="divide-y">
                {floorsByBuilding(b.id).map(f => (
                  <div key={f.id} className="px-4 py-2 flex items-center justify-between hover:bg-gray-50">
                    <div className="flex items-center gap-2">
                      <span className="text-xs text-gray-400 w-10 text-right font-mono">{f.floorNumber}</span>
                      <span className="text-sm text-gray-700">{f.name}</span>
                    </div>
                    <button onClick={() => openEditFloor(f)}
                      className="text-xs text-gray-400 hover:text-gray-600">{t('common.edit')}</button>
                  </div>
                ))}
              </div>
            ) : (
              <div className="px-4 py-3 text-xs text-gray-400">No floors yet — click "+ Floor" to add</div>
            )}
          </div>
        ))}

        {/* Free-standing floors (no building) */}
        {unattachedFloors.length > 0 && (
          <div className="border rounded-lg overflow-hidden">
            <div className="bg-gray-50 px-4 py-2.5">
              <span className="text-sm font-medium text-gray-600">Floors (No Building)</span>
            </div>
            <div className="divide-y">
              {unattachedFloors.map(f => (
                <div key={f.id} className="px-4 py-2 flex items-center justify-between hover:bg-gray-50">
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-gray-400 w-10 text-right font-mono">{f.floorNumber}</span>
                    <span className="text-sm text-gray-700">{f.name}</span>
                  </div>
                  <button onClick={() => openEditFloor(f)}
                    className="text-xs text-gray-400 hover:text-gray-600">{t('common.edit')}</button>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Inactive buildings (collapsed) */}
        {inactiveBuildings.length > 0 && (
          <details className="border rounded-lg">
            <summary className="px-4 py-2.5 text-sm text-gray-400 cursor-pointer hover:bg-gray-50">
              Deactivated Buildings ({inactiveBuildings.length})
            </summary>
            <div className="divide-y border-t">
              {inactiveBuildings.map(b => (
                <div key={b.id} className="px-4 py-2 flex items-center justify-between text-gray-400">
                  <span className="text-sm line-through">{b.name} ({b.code})</span>
                </div>
              ))}
            </div>
          </details>
        )}
      </div>
    </div>
  );
}
