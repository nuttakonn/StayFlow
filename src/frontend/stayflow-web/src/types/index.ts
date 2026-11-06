export enum RoomOperationalStatus {
  Clean = 1,
  Dirty = 2,
  Cleaning = 3,
  Inspected = 4,
  OutOfService = 5,
  Maintenance = 6
}

export interface RoomDto {
  id: string;
  organizationId: string;
  propertyId: string;
  roomTypeId: string;
  roomTypeName: string;
  roomTypeCode: string;
  baseRate: number;
  roomNumber: string;
  displayName?: string | null;
  buildingId?: string | null;
  buildingName?: string | null;
  floorId?: string | null;
  floorName?: string | null;
  capacity: number;
  operationalStatus: string;
  operationalStatusCode: RoomOperationalStatus;
  isActive: boolean;
  notes?: string | null;
  createdAtUtc: string;
}

export interface RoomTypeDto {
  id: string;
  propertyId: string;
  name: string;
  code: string;
  baseRate: number;
  standardOccupancy: number;
  maxOccupancy: number;
  description?: string;
  amenitiesJson?: string;
  isActive: boolean;
}

export interface BuildingDto {
  id: string;
  propertyId: string;
  name: string;
  code: string;
  numberOfFloors: number;
  isActive: boolean;
}

export interface FloorDto {
  id: string;
  propertyId: string;
  buildingId?: string | null;
  floorNumber: number;
  name: string;
}

export interface RoomStatusSummaryDto {
  totalRooms: number;
  cleanCount: number;
  dirtyCount: number;
  cleaningCount: number;
  inspectedCount: number;
  outOfServiceCount: number;
  maintenanceCount: number;
}

export interface RoomStatusHistoryDto {
  id: string;
  roomId: string;
  fromStatus: string;
  toStatus: string;
  notes?: string | null;
  changedByUserEmail?: string | null;
  createdAtUtc: string;
}

export interface PropertyDto {
  id: string;
  organizationId: string;
  name: string;
  code: string;
  businessType: string;
  ianaTimeZone: string;
  defaultCurrency: string;
  address?: string;
  contactPhone?: string;
  contactEmail?: string;
  isActive: boolean;
  checkInTime: string;
  checkOutTime: string;
  createdAtUtc: string;
}

// ── Phase 3: Guests ───────────────────────────────────────────────────────
export interface GuestDto {
  id: string;
  organizationId: string;
  fullName: string;
  email?: string | null;
  phone?: string | null;
  nationalId?: string | null;
  passportNumber?: string | null;
  nationality?: string | null;
  notes?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

// ── Phase 3: Reservations ─────────────────────────────────────────────────
export type ReservationStatus =
  | 'Pending'
  | 'Confirmed'
  | 'CheckedIn'
  | 'CheckedOut'
  | 'Cancelled'
  | 'NoShow';

export type ReservationSource =
  | 'WalkIn'
  | 'Phone'
  | 'Email'
  | 'Online'
  | 'OTA'
  | 'Other';

export interface ReservationStatusHistoryItemDto {
  id: string;
  fromStatus?: string | null;
  toStatus: string;
  note?: string | null;
  createdAtUtc: string;
}

export interface ReservationDto {
  id: string;
  organizationId: string;
  propertyId: string;
  guestId: string;
  guestName: string;
  guestEmail?: string | null;
  guestPhone?: string | null;
  roomTypeId?: string | null;
  roomTypeName?: string | null;
  roomId?: string | null;
  roomNumber?: string | null;
  checkInDate: string;   // YYYY-MM-DD
  checkOutDate: string;  // YYYY-MM-DD
  numberOfNights: number;
  adults: number;
  children: number;
  quotedRate: number;
  totalAmount: number;
  currency: string;
  status: ReservationStatus;
  source: ReservationSource;
  specialRequests?: string | null;
  notes?: string | null;
  cancellationReason?: string | null;
  cancelledAtUtc?: string | null;
  checkedInAtUtc?: string | null;
  checkedOutAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  timeline: ReservationStatusHistoryItemDto[];
}

// ── Phase 3: Availability ─────────────────────────────────────────────────
export interface AvailableRoomDto {
  roomId: string;
  roomNumber: string;
  displayName?: string | null;
  operationalStatus: string;
}

export interface AvailabilityRoomTypeDto {
  roomTypeId: string;
  name: string;
  code: string;
  baseRate: number;
  standardOccupancy: number;
  maxOccupancy: number;
  availableRooms: number;
  rooms: AvailableRoomDto[];
}

// ── Phase 4: Stay Operations ──────────────────────────────────────────────
export interface StayOperationItemDto {
  reservationId: string;
  guestId: string;
  guestName: string;
  guestPhone?: string | null;
  guestEmail?: string | null;
  roomId?: string | null;
  roomNumber?: string | null;
  roomTypeName?: string | null;
  checkInDate: string;
  checkOutDate: string;
  numberOfNights: number;
  adults: number;
  children: number;
  status: string;
  totalAmount: number;
  currency: string;
  checkedInAtUtc?: string | null;
  checkedOutAtUtc?: string | null;
}

export interface OperationsDashboardSummaryDto {
  expectedArrivals: number;
  completedArrivals: number;
  expectedDepartures: number;
  completedDepartures: number;
  inHouseGuests: number;
  cleanRooms: number;
  dirtyRooms: number;
  cleaningRooms: number;
  inspectedRooms: number;
  outOfServiceRooms: number;
  maintenanceRooms: number;
}

// ── Phase 5: Billing & Folios ─────────────────────────────────────────────
export interface FolioItemDto {
  id: string;
  type: string;
  description: string;
  unitPrice: number;
  quantity: number;
  amount: number;
  createdAtUtc: string;
}

export interface PaymentDto {
  id: string;
  method: string;
  amount: number;
  currency: string;
  referenceNumber?: string | null;
  notes?: string | null;
  status: string;
  reversalReason?: string | null;
  createdAtUtc: string;
}

export interface FolioDto {
  id: string;
  organizationId: string;
  propertyId: string;
  reservationId: string;
  folioNumber: string;
  status: string; // Open | Closed
  currency: string;
  totalCharges: number;
  totalPayments: number;
  balance: number;
  notes?: string | null;
  createdAtUtc: string;
  closedAtUtc?: string | null;
  items: FolioItemDto[];
  payments: PaymentDto[];
}

// ── Phase 5: Reporting ───────────────────────────────────────────────────
export interface DailyTransactionItemDto {
  paymentId: string;
  folioId: string;
  folioNumber: string;
  method: string;
  amount: number;
  currency: string;
  referenceNumber?: string | null;
  processedAtUtc: string;
}

export interface DailyTransactionSummaryDto {
  date: string;
  totalAmount: number;
  currency: string;
  amountByMethod: Record<string, number>;
  transactions: DailyTransactionItemDto[];
}

export interface OccupancyDayDto {
  date: string;
  totalRooms: number;
  occupiedRooms: number;
  occupancyRate: number;
}

export interface OccupancyReportDto {
  startDate: string;
  endDate: string;
  totalRooms: number;
  averageOccupancyRate: number;
  dailyBreakdown: OccupancyDayDto[];
}

export interface RevenueBreakdownDto {
  category: string;
  amount: number;
  percentage: number;
}

export interface RevenueReportDto {
  startDate: string;
  endDate: string;
  totalRevenue: number;
  currency: string;
  categories: RevenueBreakdownDto[];
}

// ── Phase 6: Apartments & Monthly Rentals ───────────────────────────────
export interface TenantDto {
  id: string;
  organizationId: string;
  propertyId: string;
  fullName: string;
  nationalIdOrPassport: string;
  phoneNumber: string;
  email?: string | null;
  emergencyContactName?: string | null;
  emergencyContactPhone?: string | null;
  isActive: boolean;
  createdAtUtc: string;
}

export interface RentalAgreementDto {
  id: string;
  organizationId: string;
  propertyId: string;
  agreementNumber: string;
  tenantId: string;
  tenantName?: string | null;
  tenantPhone?: string | null;
  roomId: string;
  roomNumber?: string | null;
  startDate: string;
  endDate: string;
  monthlyRent: number;
  securityDeposit: number;
  depositPaid: number;
  billingCycleDay: number;
  status: string; // Draft, Active, Terminated, Expired
  notes?: string | null;
  terminatedAtUtc?: string | null;
  terminationReason?: string | null;
  depositRefundedAmount: number;
  depositDeductedAmount: number;
  createdAtUtc: string;
}

export interface MeterReadingDto {
  id: string;
  propertyId: string;
  roomId: string;
  roomNumber?: string | null;
  utilityType: string; // Electricity, Water
  readingDate: string;
  billingMonth: string;
  previousReading: number;
  currentReading: number;
  unitsUsed: number;
  unitPrice: number;
  totalAmount: number;
  notes?: string | null;
  createdAtUtc: string;
}

export interface LatestRoomMeterReadingsDto {
  roomId: string;
  latestElectricityReading?: number | null;
  latestWaterReading?: number | null;
}

export interface MonthlyInvoiceItemDto {
  id: string;
  itemType: string;
  description: string;
  quantity: number;
  unitPrice: number;
  amount: number;
}

export interface InvoicePaymentRecordDto {
  id: string;
  amount: number;
  method: string;
  referenceNumber?: string | null;
  notes?: string | null;
  createdAtUtc: string;
}

export interface MonthlyInvoiceDto {
  id: string;
  organizationId: string;
  propertyId: string;
  invoiceNumber: string;
  agreementId: string;
  agreementNumber?: string | null;
  tenantId: string;
  tenantName?: string | null;
  roomId: string;
  roomNumber?: string | null;
  billingMonth: string;
  dueDate: string;
  totalAmount: number;
  paidAmount: number;
  balance: number;
  status: string; // Draft, Issued, Paid, PartiallyPaid, Cancelled
  paidAtUtc?: string | null;
  createdAtUtc: string;
  items: MonthlyInvoiceItemDto[];
  payments: InvoicePaymentRecordDto[];
}

export interface GenerateInvoicesSummaryDto {
  totalActiveAgreements: number;
  generatedCount: number;
  skippedCount: number;
  invoices: MonthlyInvoiceDto[];
}

// ── Phase 7: Branding ────────────────────────────────────────────────────────
export interface BrandingDto {
  displayName: string;
  logoUrl?: string | null;
  faviconUrl?: string | null;
  primaryColor: string;
  secondaryColor: string;
  loginTitle?: string | null;
  loginWelcomeText?: string | null;
  footerText?: string | null;
  supportEmail?: string | null;
  supportPhone?: string | null;
  showVendorAttribution: boolean;
}

// ── Phase 7: Subscriptions & Entitlements ───────────────────────────────────
export interface EffectiveEntitlementsDto {
  planKey: string;
  planName: string;
  status: string;
  isActive: boolean;
  trialEndsAtUtc?: string | null;
  maxUsers?: number | null;
  maxProperties?: number | null;
  capabilities: string[];
}

export interface SubscriptionPlanDto {
  id: string;
  key: string;
  name: string;
  isActive: boolean;
  maxUsers?: number | null;
  maxProperties?: number | null;
  trialDays: number;
  capabilities: string[];
}

// ── Night Audit ───────────────────────────────────────────────────────────────
export interface NightAuditLineDto {
  reservationId: string;
  guestName: string;
  roomRate: number;
  currency: string;
  result: 'posted' | 'skipped';
}

export interface NightAuditResultDto {
  businessDate: string;
  postedCount: number;
  skippedCount: number;
  totalPosted: number;
  currency: string;
  lines: NightAuditLineDto[];
}

// ── Maintenance ───────────────────────────────────────────────────────────────
export interface MaintenanceRequestDto {
  id: string;
  propertyId: string;
  roomId: string;
  roomNumber: string;
  title: string;
  description?: string | null;
  priority: string;
  priorityCode: number;
  status: string;
  statusCode: number;
  reportedByEmail?: string | null;
  assignedToEmail?: string | null;
  resolutionNotes?: string | null;
  resolvedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

// ── Guest Stay History ────────────────────────────────────────────────────────
export interface GuestStayDto {
  reservationId: string;
  propertyName: string;
  roomTypeName?: string | null;
  roomNumber?: string | null;
  checkInDate: string;
  checkOutDate: string;
  nights: number;
  adults: number;
  children: number;
  totalAmount: number;
  currency: string;
  status: string;
  createdAtUtc: string;
}

// ── Dashboard ─────────────────────────────────────────────────────────────────
export interface DashboardArrivalDto {
  reservationId: string;
  guestName?: string | null;
  roomNumber?: string | null;
  roomTypeName?: string | null;
  nights: number;
  checkInDate: string;
  checkOutDate: string;
  status: string;
}

export interface RoomStatusCountDto {
  status: string;
  count: number;
}

export interface PropertyDashboardDto {
  todayArrivals: number;
  todayDepartures: number;
  inHouseGuests: number;
  totalActiveRooms: number;
  occupiedRooms: number;
  availableRooms: number;
  occupancyRate: number;
  roomStatusBreakdown: RoomStatusCountDto[];
  openFoliosCount: number;
  totalOutstandingBalance: number;
  todayCollected: number;
  currency: string;
  todayArrivalList: DashboardArrivalDto[];
  todayDepartureList: DashboardArrivalDto[];
}




