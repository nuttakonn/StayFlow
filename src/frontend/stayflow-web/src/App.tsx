import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Building2,
  BedDouble,
  Hotel,
  Compass,
  Settings2,
  ChevronDown,
  PlusCircle,
  Zap,
  CalendarDays,
  ClipboardList,
  BellRing,
  ReceiptText,
  Menu,
  X,
  Clock,
  MapPin,
  Users,
  Brush,
  Wrench
} from 'lucide-react';
import { RoomDto, RoomStatusSummaryDto, RoomTypeDto, BuildingDto, FloorDto, RoomOperationalStatus, PropertyDto, EffectiveEntitlementsDto, BrandingDto } from './types';
import { RoomRack } from './components/RoomRack';
import { RoomDetailModal } from './components/RoomDetailModal';
import { CreateRoomModal } from './components/CreateRoomModal';
import { RoomTypeManagerModal } from './components/RoomTypeManagerModal';
import ReservationManager from './components/ReservationManager';
import AvailabilityCalendar from './components/AvailabilityCalendar';
import { StayOperationsView } from './components/StayOperationsView';
import { BillingManager } from './components/BillingManager';
import { ApartmentManager } from './components/ApartmentManager';
import { DashboardView } from './components/DashboardView';
import { GuestManager } from './components/GuestManager';
import { HousekeepingView } from './components/HousekeepingView';
import { MaintenanceView } from './components/MaintenanceView';
import { SettingsPage } from './components/SettingsPage';
import LanguageSwitcher from './components/LanguageSwitcher';
import { fetchWithAuth, getStoredSession, saveSession, clearSession, updateStoredLanguage, API_BASE, UserSession } from './lib/api';
import { LoginPage } from './components/LoginPage';
import i18nSingleton, { isSupportedLanguage } from './i18n';

export function App() {
  const { t } = useTranslation();
  const [session, setSession] = useState<UserSession | null>(() => getStoredSession());
  const [activeProperty, setActiveProperty] = useState<PropertyDto | null>(null);
  const [properties, setProperties] = useState<PropertyDto[]>([]);
  const [rooms, setRooms] = useState<RoomDto[]>([]);
  const [roomTypes, setRoomTypes] = useState<RoomTypeDto[]>([]);
  const [buildings, setBuildings] = useState<BuildingDto[]>([]);
  const [floors, setFloors] = useState<FloorDto[]>([]);
  const [summary, setSummary] = useState<RoomStatusSummaryDto | null>(null);

  const [loading, setLoading] = useState(true);
  const [selectedRoom, setSelectedRoom] = useState<RoomDto | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [showCreatePropertyModal, setShowCreatePropertyModal] = useState(false);
  const [showRoomTypesModal, setShowRoomTypesModal] = useState(false);
  const [activeTab, setActiveTab] = useState<'rooms' | 'reservations' | 'calendar' | 'operations' | 'housekeeping' | 'maintenance' | 'billing' | 'apartments' | 'guests' | 'overview' | 'settings'>('rooms');
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [entitlements, setEntitlements] = useState<EffectiveEntitlementsDto | null>(null);
  const [branding, setBranding] = useState<BrandingDto | null>(null);

  // New Property Form State
  const [newPropName, setNewPropName] = useState('');
  const [newPropCode, setNewPropCode] = useState('');
  const [newPropType, setNewPropType] = useState<number>(1);
  const [creatingProperty, setCreatingProperty] = useState(false);

  // Load properties when session becomes available
  useEffect(() => {
    if (session) initializeOrFetchProperties();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session?.token]);

  // The account-level language preference wins over browser detection, so a
  // user's choice follows them to any device they sign in from.
  // Use the i18n singleton (not the useTranslation wrapper) so a language
  // change — which creates a new wrapper object in react-i18next v16 — does
  // NOT re-trigger this effect and fight with the user's manual switch.
  useEffect(() => {
    const stored = getStoredSession()?.user?.preferredLanguage;
    if (isSupportedLanguage(stored) && stored !== i18nSingleton.language) {
      i18nSingleton.changeLanguage(stored);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loading]);

  // When active property changes, load its rooms, types, buildings, floors + branding
  useEffect(() => {
    if (activeProperty) {
      loadPropertyData(activeProperty.id);
      loadBrandingAndEntitlements(activeProperty.id);
    }
  }, [activeProperty]);

  const loadBrandingAndEntitlements = async (propertyId: string) => {
    const session = getStoredSession();
    if (!session?.organizationId) return;
    try {
      const [brandRes, entRes] = await Promise.all([
        fetchWithAuth(`/api/v1/branding?organizationId=${session.organizationId}&propertyId=${propertyId}`),
        fetchWithAuth(`/api/v1/organizations/${session.organizationId}/subscription`),
      ]);
      if (brandRes.ok) setBranding(await brandRes.json());
      if (entRes.ok) setEntitlements(await entRes.json());
    } catch {
      // non-fatal: branding/entitlements are enhancement layers
    }
  };

  const handleAuthenticated = async (auth: { accessToken: string; refreshToken: string; activeOrganizationId: string; user: UserSession['user'] }) => {
    const newSession: UserSession = {
      token: auth.accessToken,
      refreshToken: auth.refreshToken,
      organizationId: auth.activeOrganizationId,
      user: auth.user,
    };
    // Preserve whatever language the user had selected before logging in
    if (isSupportedLanguage(i18nSingleton.language)) {
      updateStoredLanguage(i18nSingleton.language);
    }
    setSession(newSession);
  };

  const handleSignOut = () => {
    clearSession();
    setSession(null);
    setProperties([]);
    setActiveProperty(null);
    setRooms([]);
    setRoomTypes([]);
    setBuildings([]);
    setFloors([]);
    setSummary(null);
    setBranding(null);
    setEntitlements(null);
    setActiveTab('rooms');
  };

  const initializeOrFetchProperties = async () => {
    setLoading(true);
    try {
      const res = await fetchWithAuth('/api/v1/properties');
      if (res.ok) {
        const data: PropertyDto[] = await res.json();
        setProperties(data);
        if (data.length > 0) setActiveProperty(data[0]);
      } else if (res.status === 401) {
        handleSignOut();
      }
    } catch (e) {
      console.error('Error fetching properties', e);
    } finally {
      setLoading(false);
    }
  };

  const triggerQuickDemo = async () => {
    try {
      const res = await fetch(API_BASE + '/api/v1/auth/quick-demo', { method: 'POST' });
      if (res.ok) {
        const auth = await res.json();
        saveSession(auth.accessToken, auth.refreshToken, auth.activeOrganizationId, auth.user);
        if (isSupportedLanguage(i18nSingleton.language)) updateStoredLanguage(i18nSingleton.language);
        await initializeOrFetchProperties();
      }
    } catch (e) {
      console.error('Failed to trigger demo', e);
    }
  };

  const loadPropertyData = async (propId: string) => {
    setLoading(true);
    try {
      const [roomsRes, summaryRes, typesRes, bldRes, floorsRes] = await Promise.all([
        fetchWithAuth(`/api/v1/properties/${propId}/rooms`),
        fetchWithAuth(`/api/v1/properties/${propId}/rooms/summary`),
        fetchWithAuth(`/api/v1/properties/${propId}/room-types`),
        fetchWithAuth(`/api/v1/properties/${propId}/buildings`),
        fetchWithAuth(`/api/v1/properties/${propId}/floors`),
      ]);

      if (roomsRes.ok) setRooms(await roomsRes.json());
      if (summaryRes.ok) setSummary(await summaryRes.json());
      if (typesRes.ok) setRoomTypes(await typesRes.json());
      if (bldRes.ok) setBuildings(await bldRes.json());
      if (floorsRes.ok) setFloors(await floorsRes.json());
    } catch (e) {
      console.error('Error loading property data', e);
    } finally {
      setLoading(false);
    }
  };

  const handleQuickStatusChange = async (roomId: string, newStatus: RoomOperationalStatus) => {
    try {
      const res = await fetchWithAuth(`/api/v1/rooms/${roomId}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          newStatus: Number(newStatus),
          notes: t('room.quickStatusNote'),
        }),
      });

      if (res.ok && activeProperty) {
        await loadPropertyData(activeProperty.id);
      }
    } catch (e) {
      console.error('Failed to change status', e);
    }
  };

  const handleModalStatusChange = async (roomId: string, newStatus: RoomOperationalStatus, notes: string) => {
    const res = await fetchWithAuth(`/api/v1/rooms/${roomId}/status`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        newStatus: Number(newStatus),
        notes: notes.trim() || undefined,
      }),
    });

    if (res.ok && activeProperty) {
      const updatedRoom: RoomDto = await res.json();
      setSelectedRoom(updatedRoom);
      await loadPropertyData(activeProperty.id);
    }
  };

  const handleCreateProperty = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newPropName.trim() || !newPropCode.trim()) return;

    try {
      setCreatingProperty(true);
      const res = await fetchWithAuth('/api/v1/properties', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: newPropName.trim(),
          code: newPropCode.trim().toUpperCase(),
          businessType: newPropType,
          ianaTimeZone: 'Asia/Bangkok',
          defaultCurrency: 'THB',
        }),
      });

      if (res.ok) {
        const createdProp: PropertyDto = await res.json();
        setProperties((prev) => [...prev, createdProp]);
        setActiveProperty(createdProp);
        setShowCreatePropertyModal(false);
        setNewPropName('');
        setNewPropCode('');
      }
    } catch (e) {
      console.error('Error creating property', e);
    } finally {
      setCreatingProperty(false);
    }
  };

  if (!session) {
    return <LoginPage onAuthenticated={handleAuthenticated} />;
  }

  return (
    <div className="min-h-screen bg-slate-100 text-slate-800 flex flex-col font-sans">
      {/* Top Navigation Bar */}
      <header className="bg-white border-b border-slate-200 px-4 sm:px-6 py-3.5 flex items-center justify-between sticky top-0 z-30 shadow-xs">
        <div className="flex items-center space-x-4">
          <div className="flex items-center space-x-3">
            {branding?.logoUrl ? (
              <img
                src={branding.logoUrl}
                alt={branding.displayName}
                className="w-10 h-10 rounded-xl object-contain border border-slate-200 bg-white p-0.5 shadow-sm"
                onError={(e) => { (e.target as HTMLImageElement).style.display = 'none'; }}
              />
            ) : (
              <div
                className="w-10 h-10 rounded-xl text-white flex items-center justify-center shadow-md"
                style={{ background: branding ? `linear-gradient(135deg, ${branding.primaryColor}, ${branding.secondaryColor})` : 'linear-gradient(135deg, #1E40AF, #0EA5E9)' }}
              >
                <Building2 className="w-5 h-5" />
              </div>
            )}
            <div>
              <div className="flex items-center gap-2">
                <h1 className="text-lg font-black text-slate-900 tracking-tight">{branding?.displayName ?? t('app.name')}</h1>
                <span className="px-2 py-0.5 rounded-full text-[10px] font-black uppercase tracking-wider bg-slate-100 text-slate-500 border border-slate-200">
                  v1.0
                </span>
              </div>
              <p className="hidden sm:block text-[11px] text-slate-400">{t('app.tagline')}</p>
            </div>
          </div>

          {/* Property Dropdown Selector */}
          {properties.length > 0 && (
            <div className="hidden md:flex items-center pl-4 border-l border-slate-200 gap-2">
              <div className="relative">
                <select
                  value={activeProperty?.id || ''}
                  onChange={(e) => {
                    const found = properties.find((p) => p.id === e.target.value);
                    if (found) {
                      setActiveProperty(found);
                      if (found.businessType === 'Apartment' && ['reservations', 'calendar', 'operations'].includes(activeTab)) {
                        setActiveTab('apartments');
                      } else if (found.businessType === 'Hotel' && activeTab === 'apartments') {
                        setActiveTab('rooms');
                      }
                    }
                  }}
                  className="appearance-none bg-slate-50 hover:bg-slate-100 border border-slate-200 rounded-xl py-1.5 pl-3 pr-8 text-xs font-bold text-slate-700 focus:outline-none focus:ring-2 focus:ring-blue-500 cursor-pointer t-button"
                >
                  {properties.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.businessType === 'Apartment' ? '🏢' : '🏨'} {p.name} ({p.code})
                    </option>
                  ))}
                </select>
                <ChevronDown className="w-3.5 h-3.5 text-slate-400 absolute right-2.5 top-1/2 -translate-y-1/2 pointer-events-none" />
              </div>

              <button
                onClick={() => setShowCreatePropertyModal(true)}
                title={t('property.create')}
                aria-label={t('property.create')}
                className="p-1.5 text-slate-500 hover:text-blue-600 bg-slate-50 hover:bg-blue-50 border border-slate-200 rounded-lg t-button"
              >
                <PlusCircle className="w-4 h-4" />
              </button>
            </div>
          )}
        </div>

        {/* Right side: language switcher + demo + user + sign-out + hamburger */}
        <div className="flex items-center gap-2">
          <LanguageSwitcher />
          <button
            onClick={triggerQuickDemo}
            title={t('demo.loadSampleHint')}
            className="hidden sm:inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold text-amber-700 bg-amber-50 hover:bg-amber-100 rounded-xl border border-amber-200 t-button"
          >
            <Zap className="w-3.5 h-3.5 text-amber-500 fill-amber-500" />
            <span>{t('demo.loadSample')}</span>
          </button>

          {/* User info + sign out */}
          <div className="hidden sm:flex items-center gap-2 pl-2 border-l border-slate-200">
            <div className="flex items-center gap-2">
              <div className="w-8 h-8 rounded-full bg-blue-600 flex items-center justify-center text-white text-xs font-black select-none">
                {session.user.fullName?.charAt(0).toUpperCase() ?? '?'}
              </div>
              <div className="hidden lg:block leading-tight">
                <p className="text-xs font-bold text-slate-800 max-w-[120px] truncate">{session.user.fullName}</p>
                <p className="text-[10px] text-slate-400">{session.user.role ?? 'Owner'}</p>
              </div>
            </div>
            <button
              onClick={handleSignOut}
              title={t('auth.signOut')}
              className="px-3 py-1.5 text-xs font-bold text-slate-600 bg-slate-100 hover:bg-red-50 hover:text-red-600 rounded-xl border border-slate-200 hover:border-red-200 transition t-button"
            >
              {t('auth.signOut')}
            </button>
          </div>

          <button
            onClick={() => setSidebarOpen((o) => !o)}
            className="md:hidden p-2 rounded-lg text-slate-600 hover:bg-slate-100 t-button"
            aria-label={t('nav.toggleMenu')}
          >
            <Menu className="w-5 h-5" />
          </button>
        </div>
      </header>

      {/* Body: sidebar + main */}
      <div className="flex flex-1 overflow-hidden">

        {/* Sidebar overlay (mobile) */}
        {sidebarOpen && (
          <div
            className="fixed inset-0 bg-black/40 z-20 md:hidden"
            onClick={() => setSidebarOpen(false)}
          />
        )}

        {/* Sidebar */}
        {(() => {
          const isHotel = !activeProperty || activeProperty.businessType === 'Hotel' || activeProperty.businessType === 'Hybrid';
          const isApartment = activeProperty?.businessType === 'Apartment' || activeProperty?.businessType === 'Hybrid';
          const caps = entitlements?.capabilities ?? [];
          const canHotelReservations = caps.length === 0 || caps.includes('hotel.reservations');
          const canApartmentRentals = caps.length === 0 || caps.includes('apartment.rentals');

          const navItem = (
            tab: typeof activeTab,
            icon: React.ReactNode,
            label: string,
            activeColor: string
          ) => (
            <button
              key={tab}
              onClick={() => { setActiveTab(tab); setSidebarOpen(false); }}
              className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-xl text-sm font-bold transition-colors ${
                activeTab === tab
                  ? `bg-white shadow-xs ${activeColor}`
                  : 'text-slate-600 hover:bg-white/60 hover:text-slate-900'
              }`}
            >
              {icon}
              <span>{label}</span>
            </button>
          );

          return (
            <aside
              className={`fixed top-[69px] left-0 bottom-0 z-20 w-60 bg-slate-100 border-r border-slate-200 flex flex-col py-4 px-2 gap-1 overflow-y-auto transition-transform duration-200
                ${sidebarOpen ? 'translate-x-0' : '-translate-x-full'}
                md:relative md:top-auto md:bottom-auto md:translate-x-0 md:flex md:shrink-0`}
            >
              {/* Mobile-only header inside sidebar */}
              <div className="md:hidden px-2 mb-3 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-black text-slate-400 uppercase tracking-wider">{t('nav.menu')}</span>
                  <button onClick={() => setSidebarOpen(false)} aria-label={t('common.close')} className="p-1 rounded-lg hover:bg-slate-200">
                    <X className="w-4 h-4 text-slate-500" />
                  </button>
                </div>
                {/* Property selector (mobile) */}
                {properties.length > 0 && (
                  <div className="space-y-1">
                    <span className="text-[10px] font-bold text-slate-400 uppercase tracking-wider block px-1">{t('property.label')}</span>
                    <select
                      value={activeProperty?.id || ''}
                      onChange={(e) => {
                        const found = properties.find((p) => p.id === e.target.value);
                        if (found) {
                          setActiveProperty(found);
                          setSidebarOpen(false);
                          if (found.businessType === 'Apartment' && ['reservations', 'calendar', 'operations'].includes(activeTab)) {
                            setActiveTab('apartments');
                          } else if (found.businessType === 'Hotel' && activeTab === 'apartments') {
                            setActiveTab('rooms');
                          }
                        }
                      }}
                      className="w-full appearance-none bg-white border border-slate-200 rounded-xl py-2 pl-3 pr-3 text-xs font-bold text-slate-700 focus:outline-none focus:ring-2 focus:ring-blue-500"
                    >
                      {properties.map((p) => (
                        <option key={p.id} value={p.id}>
                          {p.businessType === 'Apartment' ? '🏢' : '🏨'} {p.name}
                        </option>
                      ))}
                    </select>
                    <button
                      onClick={() => { setShowCreatePropertyModal(true); setSidebarOpen(false); }}
                      className="w-full flex items-center gap-2 px-3 py-2 text-xs font-bold text-blue-600 bg-blue-50 hover:bg-blue-100 rounded-xl border border-blue-100"
                    >
                      <PlusCircle className="w-3.5 h-3.5" />
                      {t('property.create')}
                    </button>
                  </div>
                )}
                <div className="border-t border-slate-200 pt-2" />
              </div>

              {navItem('rooms', <BedDouble className="w-4 h-4" />, t('nav.rooms'), 'text-blue-600')}

              {isHotel && canHotelReservations && (<>
                {navItem('reservations', <ClipboardList className="w-4 h-4" />, t('nav.reservations'), 'text-indigo-600')}
                {navItem('calendar', <CalendarDays className="w-4 h-4" />, t('nav.calendar'), 'text-emerald-600')}
                {navItem('operations', <BellRing className="w-4 h-4" />, t('nav.operations'), 'text-amber-600')}
                {navItem('housekeeping', <Brush className="w-4 h-4" />, t('nav.housekeeping'), 'text-cyan-600')}
                {navItem('maintenance', <Wrench className="w-4 h-4" />, t('nav.maintenance'), 'text-rose-600')}
              </>)}

              {isApartment && canApartmentRentals && navItem('apartments', <Building2 className="w-4 h-4" />, t('nav.apartments'), 'text-emerald-700')}

              {navItem('billing', <ReceiptText className="w-4 h-4" />, t('nav.billing'), 'text-purple-600')}
              {navItem('guests', <Users className="w-4 h-4" />, t('nav.guests'), 'text-teal-600')}
              {navItem('overview', <Compass className="w-4 h-4" />, t('nav.overview'), 'text-blue-600')}

              {/* Settings + Sign Out — pinned at the bottom */}
              <div className="mt-auto pt-3 border-t border-slate-200 space-y-1">
                {navItem('settings', <Settings2 className="w-4 h-4" />, t('nav.settings'), 'text-slate-700')}
                <button
                  onClick={handleSignOut}
                  className="w-full flex items-center gap-3 px-4 py-2.5 rounded-xl text-sm font-bold text-red-500 hover:bg-red-50 hover:text-red-600 transition-colors"
                >
                  <Users className="w-4 h-4" />
                  <span>{t('auth.signOut')}</span>
                </button>
              </div>
            </aside>
          );
        })()}

        {/* Main Workspace */}
        <main className="flex-1 overflow-y-auto p-4 sm:p-6 md:p-8 space-y-6">
        {/* Active Property Banner */}
        {activeProperty && (() => {
          const isHotel = activeProperty.businessType === 'Hotel' || activeProperty.businessType === 'Hybrid';
          const isApartment = activeProperty.businessType === 'Apartment';
          const typeLabel = isApartment ? t('property.apartmentSystem') : t('property.hotelSystem');
          const typeIcon = isApartment ? <Building2 className="w-3.5 h-3.5 text-amber-300" /> : <Hotel className="w-3.5 h-3.5 text-amber-300" />;

          return (
            <div className="bg-gradient-to-r from-slate-900 via-indigo-950 to-blue-900 rounded-3xl text-white p-6 sm:p-8 shadow-xl relative overflow-hidden">
              <div className="absolute right-0 top-0 translate-x-10 -translate-y-10 w-96 h-96 bg-blue-500/10 rounded-full blur-3xl pointer-events-none"></div>

              <div className="relative z-10 flex flex-col md:flex-row md:items-center justify-between gap-4">
                <div className="space-y-1.5">
                  <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-white/10 text-xs font-bold text-blue-200 backdrop-blur-md">
                    {typeIcon}
                    <span>{typeLabel}</span>
                  </div>
                  <h2 className="text-2xl sm:text-3xl font-black tracking-tight">{activeProperty.name}</h2>
                  <div className="flex items-center gap-3 text-xs sm:text-sm text-slate-300 flex-wrap">
                    {activeProperty.address && (
                      <span className="flex items-center gap-1">
                        <MapPin className="w-3.5 h-3.5 text-slate-400" />
                        {activeProperty.address}
                      </span>
                    )}
                    {isHotel && (
                      <span className="flex items-center gap-1">
                        <Clock className="w-3.5 h-3.5 text-slate-400" />
                        {t('property.checkInOut', {
                          checkIn: activeProperty.checkInTime,
                          checkOut: activeProperty.checkOutTime,
                        })}
                      </span>
                    )}
                  </div>
                </div>

                {/* Quick Counter Badges */}
                <div className="flex items-center gap-3 flex-wrap">
                  <div className="bg-white/10 backdrop-blur-md px-4 py-2.5 rounded-2xl border border-white/10 text-center">
                    <span className="text-[10px] text-slate-300 uppercase block font-semibold">{t('property.totalRooms')}</span>
                    <span className="text-xl font-black text-white">{summary?.totalRooms ?? 0}</span>
                  </div>
                  <div className="bg-emerald-500/20 backdrop-blur-md px-4 py-2.5 rounded-2xl border border-emerald-500/30 text-center">
                    <span className="text-[10px] text-emerald-200 uppercase block font-semibold">{t('property.readyToSell')}</span>
                    <span className="text-xl font-black text-emerald-300">{summary?.cleanCount ?? 0}</span>
                  </div>
                  <div className="bg-rose-500/20 backdrop-blur-md px-4 py-2.5 rounded-2xl border border-rose-500/30 text-center">
                    <span className="text-[10px] text-rose-200 uppercase block font-semibold">{t('property.needsCleaning')}</span>
                    <span className="text-xl font-black text-rose-300">{summary?.dirtyCount ?? 0}</span>
                  </div>
                </div>
              </div>
            </div>
          );
        })()}

        {/* Tab 1: Room Rack (Primary Operations) */}
        {activeTab === 'rooms' && (
          <RoomRack
            rooms={rooms}
            summary={summary}
            loading={loading}
            businessType={activeProperty?.businessType}
            onSelectRoom={(r) => setSelectedRoom(r)}
            onOpenCreate={() => {
              if (activeProperty) {
                setShowCreateModal(true);
              } else {
                setShowCreatePropertyModal(true);
              }
            }}
            onOpenRoomTypes={() => setShowRoomTypesModal(true)}
            onRefresh={() => activeProperty && loadPropertyData(activeProperty.id)}
            onQuickStatusChange={handleQuickStatusChange}
          />
        )}

        {/* Tab 2: Reservations (Phase 3) */}
        {activeTab === 'reservations' && activeProperty && (() => {
          const session = getStoredSession();
          return session ? (
            <ReservationManager
              property={activeProperty}
              token={session.token}
              orgId={session.organizationId ?? ''}
            />
          ) : null;
        })()}

        {/* Tab 3: Availability Calendar (Phase 3) */}
        {activeTab === 'calendar' && activeProperty && (() => {
          const session = getStoredSession();
          return session ? (
            <AvailabilityCalendar
              property={activeProperty}
              token={session.token}
              orgId={session.organizationId ?? ''}
            />
          ) : null;
        })()}

        {/* Tab 4: Stay Operations & Front Desk (Phase 4) */}
        {activeTab === 'operations' && activeProperty && (() => {
          const session = getStoredSession();
          return session ? (
            <StayOperationsView
              property={activeProperty}
              token={session.token}
              orgId={session.organizationId ?? ''}
              onRefreshRooms={() => loadPropertyData(activeProperty.id)}
            />
          ) : null;
        })()}

        {/* Tab 4b: Housekeeping */}
        {activeTab === 'housekeeping' && activeProperty && (() => {
          const session = getStoredSession();
          return session ? (
            <HousekeepingView
              property={activeProperty}
              token={session.token}
              orgId={session.organizationId ?? ''}
            />
          ) : null;
        })()}

        {/* Tab 4c: Maintenance */}
        {activeTab === 'maintenance' && activeProperty && (() => {
          const session = getStoredSession();
          return session ? (
            <MaintenanceView
              property={activeProperty}
              token={session.token}
              orgId={session.organizationId ?? ''}
              rooms={rooms}
            />
          ) : null;
        })()}

        {/* Tab 5: Billing & Reporting (Phase 5) */}
        {activeTab === 'billing' && activeProperty && (() => {
          const session = getStoredSession();
          return session ? (
            <BillingManager
              property={activeProperty}
              token={session.token}
              orgId={session.organizationId ?? ''}
            />
          ) : null;
        })()}

        {/* Tab 6: Apartment Workflow (Phase 6) */}
        {activeTab === 'apartments' && activeProperty && (() => {
          const session = getStoredSession();
          return session ? (
            <ApartmentManager
              property={activeProperty}
              token={session.token}
              orgId={session.organizationId ?? ''}
              rooms={rooms}
              onRefreshRooms={() => loadPropertyData(activeProperty.id)}
            />
          ) : null;
        })()}

        {/* Tab 7: Guest Profile Manager */}
        {activeTab === 'guests' && (() => {
          const session = getStoredSession();
          return session ? (
            <GuestManager
              token={session.token}
              orgId={session.organizationId ?? ''}
            />
          ) : null;
        })()}

        {/* Tab 8: Operational Dashboard */}
        {activeTab === 'overview' && (() => {
          const session = getStoredSession();
          return session && activeProperty ? (
            <DashboardView
              property={activeProperty}
              token={session.token}
              orgId={session.organizationId ?? ''}
              onNavigate={(tab) => setActiveTab(tab as typeof activeTab)}
            />
          ) : (
            <div className="text-center py-16 text-slate-400 text-sm">
              {t('property.selectPrompt')}
            </div>
          );
        })()}

        {/* Tab 8: Settings — Branding, Subscription, Team, Audit, Property (Phase 7/9) */}
        {activeTab === 'settings' && (
          <SettingsPage
            activeProperty={activeProperty}
            roomTypes={roomTypes}
            onPropertyUpdated={(updated) => {
              setActiveProperty(updated);
              setProperties((prev) => prev.map((p) => p.id === updated.id ? updated : p));
            }}
            onBrandingUpdated={(updated) => setBranding(updated)}
          />
        )}
      </main>
      </div>{/* end sidebar+main flex container */}

      {/* Room Detail Modal */}
      {selectedRoom && (
        <RoomDetailModal
          room={selectedRoom}
          roomTypes={roomTypes}
          buildings={buildings}
          floors={floors}
          onClose={() => setSelectedRoom(null)}
          onStatusChange={handleModalStatusChange}
          onRoomUpdated={() => {
            if (activeProperty) loadPropertyData(activeProperty.id);
          }}
        />
      )}

      {/* Room Types & Rates Manager Modal */}
      {showRoomTypesModal && activeProperty && (
        <RoomTypeManagerModal
          propertyId={activeProperty.id}
          roomTypes={roomTypes}
          onClose={() => setShowRoomTypesModal(false)}
          onRefresh={() => loadPropertyData(activeProperty.id)}
        />
      )}

      {/* Create Room Modal */}
      {showCreateModal && activeProperty && (
        <CreateRoomModal
          propertyId={activeProperty.id}
          roomTypes={roomTypes}
          buildings={buildings}
          floors={floors}
          onClose={() => setShowCreateModal(false)}
          onCreated={() => loadPropertyData(activeProperty.id)}
        />
      )}

      {/* Create Property Modal */}
      {showCreatePropertyModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm t-modal-overlay">
          <div className="bg-white rounded-2xl shadow-2xl max-w-md w-full border border-slate-200 overflow-hidden t-modal-content p-6 space-y-4">
            <div className="flex items-center justify-between border-b border-slate-100 pb-3">
              <h3 className="text-base font-bold text-slate-900">{t('property.createTitle')}</h3>
              <button onClick={() => setShowCreatePropertyModal(false)} className="text-slate-400 hover:text-slate-600">
                ✕
              </button>
            </div>

            <form onSubmit={handleCreateProperty} className="space-y-4">
              <div>
                <label className="text-xs font-semibold text-slate-700 block mb-1">{t('property.nameLabel')}</label>
                <input
                  type="text"
                  required
                  placeholder={t('property.namePlaceholder')}
                  value={newPropName}
                  onChange={(e) => setNewPropName(e.target.value)}
                  className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500"
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">{t('property.codeLabel')}</label>
                  <input
                    type="text"
                    required
                    placeholder={t('property.codePlaceholder')}
                    value={newPropCode}
                    onChange={(e) => setNewPropCode(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 uppercase font-bold"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-700 block mb-1">{t('property.businessTypeLabel')}</label>
                  <select
                    value={newPropType}
                    onChange={(e) => setNewPropType(Number(e.target.value))}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl text-sm focus:ring-2 focus:ring-blue-500 bg-white"
                  >
                    <option value={1}>🏨 {t('property.typeHotel')}</option>
                    <option value={2}>🏢 {t('property.typeApartment')}</option>
                  </select>
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setShowCreatePropertyModal(false)}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 rounded-xl hover:bg-slate-200 t-button"
                >
                  {t('common.cancel')}
                </button>
                <button
                  type="submit"
                  disabled={creatingProperty}
                  className="px-5 py-2 text-xs font-bold text-white bg-blue-600 rounded-xl hover:bg-blue-700 t-button shadow-md"
                >
                  {t('property.submit')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Footer */}
      <footer className="border-t border-slate-200 bg-white py-4 px-6 text-center text-xs text-slate-400 mt-auto">
        {branding?.footerText
          ? branding.footerText
          : `StayFlow PMS © ${new Date().getFullYear()}`}
        {branding && !branding.showVendorAttribution ? '' : (branding?.footerText ? ' — Powered by StayFlow PMS' : ' — Production Ready Hospitality System')}
      </footer>
    </div>
  );
}

export default App;
