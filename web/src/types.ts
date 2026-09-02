export type Screen = 'dashboard' | 'events' | 'tickets' | 'audit' | 'configuration' | 'messages' | 'users' | 'import' | 'importLogs' | 'reports' | 'admin' | 'clients' | 'manualValidation' | 'loginLog' | 'auditTrail' | 'turnstiles';

export interface TurnstileMonitorView {
  deviceId: string;
  status: string;
  firstSeenAt: string;
  lastSeenAt: string;
  online: boolean;
  firmware?: string | null;
  boardId?: string | null;
  serialId?: string | null;
  ipLocal?: string | null;
  media?: string | null;
  deviceRegistrationId?: string | null;
  deviceName?: string | null;
  deviceActive?: boolean | null;
  gateId?: string | null;
  gateName?: string | null;
  eventId?: string | null;
  eventName?: string | null;
}

export interface LoginLogEntry {
  id: string;
  userId: string | null;
  userName: string;
  displayName: string;
  outcome: string; // Success | InvalidPassword | UnknownUser | Blocked | Inactive
  ip: string | null;
  userAgent: string | null;
  createdAt: string;
}

export interface LoginLogPage {
  page: number;
  pageSize: number;
  total: number;
  data: LoginLogEntry[];
}

export interface AuditTrailEntry {
  id: string;
  userId: string | null;
  userName: string | null;
  action: string;
  method: string;
  path: string;
  targetId: string | null;
  statusCode: number;
  summary: string | null;
  ip: string | null;
  userAgent: string | null;
  createdAt: string;
}

export interface AuditTrailPage {
  page: number;
  pageSize: number;
  total: number;
  data: AuditTrailEntry[];
}

export interface VenueView {
  id: string;
  name: string;
  address?: string | null;
  city?: string | null;
  state?: string | null;
  capacity?: number | null;
}

export interface EventView {
  id: string;
  venueId: string;
  name: string;
  organizer?: string | null;
  startsAt: string;
  endsAt: string;
  status: string;
  clientId?: string | null;
  clientName?: string | null;
}

export type GateOperationMode = 'EntryValidatedExitFree' | 'EntryAndExitValidated';

export interface GateView {
  id: string;
  venueId: string;
  name: string;
  code?: string | null;
  active: boolean;
  operationMode: GateOperationMode;
}

export type DeviceType = 'Legacy' | 'Serial' | 'Vcom' | 'Mqtt' | 'Simulator';

export interface DeviceView {
  id: string;
  eventId: string;
  gateId: string;
  gateName: string;
  gateCode?: string | null;
  name: string;
  identifier?: string | null;
  deviceType: DeviceType;
  active: boolean;
  lastSeenAt?: string | null;
  configurationJson?: string | null;
}

export interface SectorView {
  id: string;
  eventId: string;
  name: string;
  capacity?: number | null;
  active: boolean;
}

export interface GateSectorView {
  id: string;
  eventId: string;
  gateId: string;
  gateName: string;
  sectorId: string;
  sectorName: string;
  direction: string;
  activeFrom?: string | null;
  activeUntil?: string | null;
  active: boolean;
}

export interface TicketView {
  id: string;
  eventId: string;
  batchId?: string | null;
  ticketTypeId: string;
  externalId: string;
  code: string;
  maximumUses: number;
  uses: number;
  maximumEntries: number;
  entriesUsed: number;
  peopleInside: number;
  status: string;
  metadataJson?: string | null;
  createdAt: string;
  sectorId?: string | null;
  sectorName?: string | null;
  batchName?: string | null;
}

export interface AccessMessageView {
  id: string;
  eventId: string;
  code: string;
  title: string;
  message: string;
  backgroundStart: string;
  backgroundEnd: string;
  titleColor: string;
  messageColor: string;
  titleSize: string;
  messageSize: string;
  titleBold: boolean;
  messageBold: boolean;
  active: boolean;
  system: boolean;
  updatedAt: string;
  /** true = este evento tem um override próprio; false = está herdando o padrão global */
  isCustomized: boolean;
}

/** Template padrão global — vale para todos os eventos sem override. */
export interface AccessMessageTemplateView {
  id: string;
  code: string;
  title: string;
  message: string;
  backgroundStart: string;
  backgroundEnd: string;
  titleColor: string;
  messageColor: string;
  titleSize: string;
  messageSize: string;
  titleBold: boolean;
  messageBold: boolean;
  active: boolean;
  updatedAt: string;
}

export interface AccessValidationResult {
  attemptId: string;
  approved: boolean;
  decision: string;
  credentialType: string;
  reason?: string | null;
  reasonCode?: string | null;
  message?: string | null;
  presentation?: {
    title: string;
    message: string;
    backgroundStart: string;
    backgroundEnd: string;
    titleColor: string;
    messageColor: string;
    titleSize: string;
    messageSize: string;
    titleBold: boolean;
    messageBold: boolean;
  } | null;
  staffCredentialId?: string | null;
  staffMemberId?: string | null;
  staffName?: string | null;
  idempotentReplay: boolean;
  channel: string;
  direction: string;
  ticketId?: string | null;
  maximumEntries?: number | null;
  entriesUsed?: number | null;
  peopleInside?: number | null;
  armAction: string;
  pictogram: string;
}

export interface AccessValidationPayload {
  credentialCode?: string;
  eventId: string;
  gateId: string;
  sectorId?: string | null;
  direction?: string;
  idempotencyKey: string;
  deviceId?: string | null;
}

export interface AttemptView {
  attemptId: string;
  eventId: string;
  ticketId?: string | null;
  staffCredentialId?: string | null;
  staffMemberId?: string | null;
  gateId: string;
  sectorId?: string | null;
  deviceId?: string | null;
  credentialType: string;
  direction: string;
  channel?: string;
  armAction?: string;
  pictogram?: string;
  decision: string;
  reason?: string | null;
  status: string;
  credentialCodeMasked: string;
  ticketExternalId?: string | null;
  staffName?: string | null;
  gateName?: string | null;
  sectorName?: string | null;
  deviceName?: string | null;
  ticketSectorName?: string | null;
  appDeviceLabel?: string | null;
  requestedAt: string;
  createdAt: string;
}

export interface AttemptPage {
  data: AttemptView[];
  page: number;
  pageSize: number;
  total: number;
  hasNext: boolean;
}

export interface CountView {
  key: string;
  count: number;
}

export interface AttemptSummary {
  eventId: string;
  from?: string | null;
  to?: string | null;
  totalAttempts: number;
  approved: number;
  rejected: number;
  approvalRate: number;
  byCredentialType: CountView[];
  byDirection: CountView[];
  byDecision: CountView[];
  byGate: CountView[];
  byReason: CountView[];
  bySector: CountView[];
}

export interface SessionView {
  userId: string;
  userName: string;
  displayName: string;
  active: boolean;
  permissions: string[];
}

export interface UserView {
  id: string;
  userName: string;
  displayName: string;
  active: boolean;
  eventScope: 'Todos' | 'Ativos' | 'Especificos';
  lastLoginAt?: string | null;
  blockedUntil?: string | null;
  roles: { id: string; name: string; systemRole: boolean }[];
  authorizedEventIds: string[];
  authorizedGateIds: string[];
  physicalAccessEnabled: boolean;
  accessBadgeCode?: string | null;
  staffMemberId?: string | null;
}

export interface RoleView {
  id: string;
  name: string;
  description?: string | null;
  active: boolean;
  systemRole: boolean;
  permissions: string[];
}

export interface PermissionView {
  code: string;
  description: string;
  privileged: boolean;
}

export interface ImportView {
  id: string;
  eventId: string;
  ticketTypeId?: string | null;
  batchId?: string | null;
  fileName: string;
  status: 'pending' | 'processing' | 'done' | 'failed';
  mode: string;
  totalRows: number;
  processed: number;
  inserted: number;
  updated: number;
  skipped: number;
  errors: number;
  errorMessage?: string | null;
  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
}

export interface ImportRowView {
  rowNumber: number;
  code: string;
  externalId?: string | null;
  maxEntries: number;
  status: string;
  errorDetail?: string | null;
  sectorName?: string | null;
}

export interface CsvPreviewResult {
  detectedSeparator: string;
  sampleRows: string[][];
  totalRows: number;
  headers: string[];
}

// ── Relatórios ────────────────────────────────────────────────────────────────
export interface ValidationSummary {
  totalAttempts: number;
  approved: number;
  rejected: number;
  approvalRate: number;
  uniqueTickets: number;
  totalTickets: number;
  coverageRate: number;
  peopleInside: number;
  entriesToday: number;
  exitsToday: number;
}

export interface GateReport { gateId: string; gateName: string; attempts: number; approved: number; rejected: number; approvalRate: number; uniqueTickets: number; }
export interface SectorReport { sectorId: string; sectorName: string; totalTickets: number; validatedTickets: number; coverageRate: number; peopleInside: number; capacity?: number | null; }
export interface HourlyReport { hour: string; approved: number; rejected: number; total: number; }
export interface RejectionReason { code: string; reason: string; count: number; percentage: number; }
export interface RecentAttempt { attemptId: string; credentialCode: string; ticketExternalId?: string | null; decision: string; gateName?: string | null; sectorName?: string | null; direction: string; requestedAt: string; reason?: string | null; }

export interface ValidationReport {
  summary: ValidationSummary;
  byGate: GateReport[];
  bySector: SectorReport[];
  byHour: HourlyReport[];
  rejectionReasons: RejectionReason[];
  recentAttempts: RecentAttempt[];
  timeZone: string;
  generatedAt: string;
}

export interface BulkStatusResult { updated: number; notFound: number; }
export interface DataSummary { eventName: string; tickets: number; attempts: number; imports: number; }

// ── Clientes ──────────────────────────────────────────────────────────────────
export interface ClientView {
  id: string;
  name: string;
  document?: string | null;
  email?: string | null;
  phone?: string | null;
  active: boolean;
  notes?: string | null;
  eventCount: number;
  createdAt: string;
}

// ── Resumo de tickets por evento ──────────────────────────────────────────────
export interface TicketSummaryView {
  total: number;
  active: number;
  used: number;
  cancelled: number;
  revoked: number;
  peopleInside: number;
}
