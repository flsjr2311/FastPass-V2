import type {
  AccessValidationResult,
  AccessValidationPayload,
  AccessMessageView,
  AccessMessageTemplateView,
  SessionView,
  UserView,
  RoleView,
  PermissionView,
  ClientView,
  StaffCredentialView,
  StaffAccessView,
  TicketSummaryView,
  AttemptPage,
  AttemptSummary,
  DeviceType,
  DeviceView,
  EventView,
  GateOperationMode,
  GateSectorView,
  GateView,
  SectorView,
  TicketView,
  VenueView,
} from './types';

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    credentials: 'same-origin', // cookie enviado automaticamente na mesma origem (proxy Vite)
    headers: {
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  });
  if (!response.ok) {
    const detail = await response.text();
    let message = detail || `API retornou HTTP ${response.status}`;
    try {
      const parsed = JSON.parse(detail) as { error?: string };
      if (parsed.error) message = parsed.error;
    } catch {
      // Mantém o texto original quando a API não devolve JSON.
    }
    throw new Error(message);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export const api = {
  // ── Auth ──────────────────────────────────────────────────────────────────
  login: (userName: string, password: string) =>
    request<SessionView>('/api/auth/login', { method: 'POST', body: JSON.stringify({ userName, password }) }),
  me: () => request<SessionView>('/api/auth/me'),
  logout: () => request<{ message: string }>('/api/auth/logout', { method: 'POST' }),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<{ message: string }>('/api/auth/password', { method: 'PUT', body: JSON.stringify({ currentPassword, newPassword }) }),

  // ── Usuários e perfis ─────────────────────────────────────────────────────
  listUsers: (activeOnly = true) => request<UserView[]>(`/api/users?activeOnly=${activeOnly}`),
  createUser: (payload: { userName: string; displayName: string; password: string; roleIds: string[]; eventScope?: string; eventIds?: string[]; gateIds?: string[]; physicalAccessEnabled?: boolean; accessBadgeCode?: string }) =>
    request<UserView>('/api/users', { method: 'POST', body: JSON.stringify(payload) }),
  updateUser: (userId: string, payload: { displayName: string; active: boolean; roleIds: string[]; eventScope?: string; eventIds?: string[]; gateIds?: string[]; physicalAccessEnabled?: boolean; accessBadgeCode?: string }) =>
    request<UserView>(`/api/users/${userId}`, { method: 'PUT', body: JSON.stringify(payload) }),
  unblockUser: (userId: string) =>
    request<{ message: string }>(`/api/users/${userId}/unblock`, { method: 'POST' }),
  listRoles: () => request<RoleView[]>('/api/roles'),
  createRole: (payload: { name: string; description?: string }) =>
    request<RoleView>('/api/roles', { method: 'POST', body: JSON.stringify(payload) }),
  updateRole: (roleId: string, payload: { name: string; description?: string; active: boolean }) =>
    request<RoleView>(`/api/roles/${roleId}`, { method: 'PUT', body: JSON.stringify(payload) }),
  setRolePermissions: (roleId: string, permissionCodes: string[]) =>
    request<RoleView>(`/api/roles/${roleId}/permissions`, { method: 'PUT', body: JSON.stringify({ permissionCodes }) }),
  deleteRole: (roleId: string) =>
    request<void>(`/api/roles/${roleId}`, { method: 'DELETE' }),

  deleteEvent: (eventId: string) =>
    request<{ message: string }>(`/api/events/${eventId}`, { method: 'DELETE' }),
  cancelTicket: (eventId: string, ticketId: string) =>
    request<{ message: string }>(`/api/events/${eventId}/tickets/${ticketId}`, { method: 'DELETE' }),
  listPermissions: () => request<PermissionView[]>('/api/permissions'),

  // ── Clientes ──────────────────────────────────────────────────────────────
  listClients: (activeOnly = true) => request<ClientView[]>(`/api/clients?activeOnly=${activeOnly}`),
  createClient: (payload: { name: string; document?: string; email?: string; phone?: string; notes?: string }) =>
    request<ClientView>('/api/clients', { method: 'POST', body: JSON.stringify(payload) }),
  updateClient: (clientId: string, payload: { name: string; document?: string; email?: string; phone?: string; notes?: string; active: boolean }) =>
    request<ClientView>(`/api/clients/${clientId}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteClient: (clientId: string) =>
    request<{ message: string }>(`/api/clients/${clientId}`, { method: 'DELETE' }),

  listVenues: () => request<VenueView[]>('/api/venues'),
  createVenue: (payload: { name: string; address?: string; city?: string; state?: string; capacity?: number }) =>
    request<VenueView>('/api/venues', { method: 'POST', body: JSON.stringify(payload) }),
  listEvents: () => request<EventView[]>('/api/events'),
  createEvent: (payload: { venueId: string; name: string; startsAt: string; endsAt: string; organizer?: string; clientId?: string; status: string }) =>
    request<EventView>('/api/events', { method: 'POST', body: JSON.stringify(payload) }),
  listGates: (eventId: string) => request<GateView[]>(`/api/events/${eventId}/gates`),
  setGateOperationMode: (eventId: string, gateId: string, operationMode: GateOperationMode) =>
    request<GateView>(`/api/events/${eventId}/gates/${gateId}/operation-mode`, {
      method: 'PUT',
      body: JSON.stringify({ operationMode }),
    }),
  createGate: (eventId: string, payload: { name: string; code?: string }) =>
    request<GateView>(`/api/events/${eventId}/gates`, { method: 'POST', body: JSON.stringify(payload) }),
  listDevices: (eventId: string, gateId?: string, activeOnly = false) => request<DeviceView[]>(`/api/events/${eventId}/devices?${new URLSearchParams({ ...(gateId ? { gateId } : {}), activeOnly: String(activeOnly) }).toString()}`),
  createDevice: (eventId: string, gateId: string, payload: { name: string; identifier?: string; deviceType: DeviceType; configurationJson?: string }) =>
    request<DeviceView>(`/api/events/${eventId}/gates/${gateId}/devices`, {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  updateDevice: (eventId: string, gateId: string, deviceId: string, payload: { name: string; identifier?: string; deviceType: DeviceType; configurationJson?: string; active: boolean }) =>
    request<DeviceView>(`/api/events/${eventId}/gates/${gateId}/devices/${deviceId}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),
  listSectors: (eventId: string) => request<SectorView[]>(`/api/events/${eventId}/sectors`),
  createSector: (eventId: string, payload: { name: string; capacity?: number }) =>
    request<SectorView>(`/api/events/${eventId}/sectors`, { method: 'POST', body: JSON.stringify(payload) }),
  listGateSectors: (eventId: string) => request<GateSectorView[]>(`/api/events/${eventId}/gate-sectors`),
  createGateSector: (eventId: string, payload: { gateId: string; sectorId: string; direction: string; activeFrom?: string; activeUntil?: string }) =>
    request<GateSectorView>(`/api/events/${eventId}/gate-sectors`, {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  removeGateSector: (eventId: string, gateSectorId: string) =>
    request<void>(`/api/events/${eventId}/gate-sectors/${gateSectorId}`, { method: 'DELETE' }),
  removeGate: (eventId: string, gateId: string) =>
    request<void>(`/api/events/${eventId}/gates/${gateId}`, { method: 'DELETE' }),
  removeSector: (eventId: string, sectorId: string) =>
    request<void>(`/api/events/${eventId}/sectors/${sectorId}`, { method: 'DELETE' }),
  // ── Staff / Crachás ───────────────────────────────────────────────────────
  listStaff: (activeOnly = true) => request<StaffCredentialView[]>(`/api/staff?activeOnly=${activeOnly}`),
  createStaff: (payload: { name: string; badgeCode: string; employeeCode?: string; department?: string; jobTitle?: string; validFrom?: string; validUntil?: string }) =>
    request<StaffCredentialView>('/api/staff', { method: 'POST', body: JSON.stringify(payload) }),
  updateStaff: (staffId: string, payload: { name: string; badgeCode: string; employeeCode?: string; department?: string; jobTitle?: string; validFrom?: string; validUntil?: string; active: boolean }) =>
    request<StaffCredentialView>(`/api/staff/${staffId}`, { method: 'PUT', body: JSON.stringify(payload) }),
  grantStaffAccess: (staffId: string, eventId: string, payload: { gateId?: string; sectorId?: string; direction: string; profile: string }) =>
    request<StaffAccessView>(`/api/staff/${staffId}/events/${eventId}/access`, { method: 'POST', body: JSON.stringify(payload) }),
  listStaffAccess: (staffId: string, eventId?: string) =>
    request<StaffAccessView[]>(`/api/staff/${staffId}/access${eventId ? `?eventId=${eventId}` : ''}`),

  listTickets: (eventId: string) => request<TicketView[]>(`/api/events/${eventId}/tickets`),
  getTicketSummary: (eventId: string) => request<TicketSummaryView>(`/api/events/${eventId}/tickets/summary`),
  exportEventZip: (eventId: string) => `${API_BASE_URL}/api/events/${eventId}/export`,
  validateApp: (payload: AccessValidationPayload) =>
    request<AccessValidationResult>('/api/access/app/validate', {
      method: 'POST',
      body: JSON.stringify({ ...payload, direction: 'Entry' }),
    }),
  validateTurnstile: (payload: AccessValidationPayload) =>
    request<AccessValidationResult>('/api/access/turnstile/validate', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  listAttempts: (eventId: string) => request<AttemptPage>(`/api/events/${eventId}/access-attempts?page=1&pageSize=20`),
  getSummary: (eventId: string) => request<AttemptSummary>(`/api/events/${eventId}/access-attempts/summary`),
  listAccessMessages: (eventId: string) => request<AccessMessageView[]>(`/api/events/${eventId}/access-messages`),
  updateAccessMessage: (eventId: string, code: string, payload: { title: string; message: string; backgroundStart: string; backgroundEnd: string; titleColor: string; messageColor: string; titleSize: string; messageSize: string; titleBold: boolean; messageBold: boolean; active: boolean }) =>
    request<AccessMessageView>(`/api/events/${eventId}/access-messages/${code}`, { method: 'PUT', body: JSON.stringify(payload) }),
  restoreAccessMessageDefault: (eventId: string, code: string) =>
    request<{ message: string }>(`/api/events/${eventId}/access-messages/${code}`, { method: 'DELETE' }),

  // ── Mensagens padrão globais (valem para todos os eventos) ──────────────────
  listMessageTemplates: () => request<AccessMessageTemplateView[]>('/api/message-templates'),
  updateMessageTemplate: (code: string, payload: { title: string; message: string; backgroundStart: string; backgroundEnd: string; titleColor: string; messageColor: string; titleSize: string; messageSize: string; titleBold: boolean; messageBold: boolean; active: boolean }) =>
    request<AccessMessageTemplateView>(`/api/message-templates/${code}`, { method: 'PUT', body: JSON.stringify(payload) }),

  // ── Importação de ingressos ───────────────────────────────────────────────
  previewImport: (eventId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return fetch(`${API_BASE_URL}/api/events/${eventId}/ticket-imports/preview`, {
      method: 'POST', credentials: 'same-origin', body: form,
    }).then(async (r) => {
      if (!r.ok) { const t = await r.text(); throw new Error(t || `HTTP ${r.status}`); }
      return r.json() as Promise<import('./types').CsvPreviewResult>;
    });
  },
  startImport: (eventId: string, file: File, opts: {
    ticketTypeId?: string; batchId?: string; separator: string;
    colCode: number; colExternalId?: number; colMaxEntries?: number;
    defaultMaxEntries: number; mode: string;
    colStatus?: number; defaultStatus?: string;
    colSector?: number; defaultSectorId?: string;
  }) => {
    const form = new FormData();
    form.append('file', file);
    form.append('separator', opts.separator);
    form.append('colCode', String(opts.colCode));
    if (opts.colExternalId != null) form.append('colExternalId', String(opts.colExternalId));
    if (opts.colMaxEntries != null) form.append('colMaxEntries', String(opts.colMaxEntries));
    form.append('defaultMaxEntries', String(opts.defaultMaxEntries));
    form.append('mode', opts.mode);
    if (opts.colStatus != null) form.append('colStatus', String(opts.colStatus));
    if (opts.defaultStatus) form.append('defaultStatus', opts.defaultStatus);
    if (opts.colSector != null) form.append('colSector', String(opts.colSector));
    if (opts.defaultSectorId) form.append('defaultSectorId', opts.defaultSectorId);
    if (opts.ticketTypeId) form.append('ticketTypeId', opts.ticketTypeId);
    if (opts.batchId) form.append('batchId', opts.batchId);
    return fetch(`${API_BASE_URL}/api/events/${eventId}/ticket-imports`, {
      method: 'POST', credentials: 'same-origin', body: form,
    }).then(async (r) => {
      if (!r.ok) {
        const t = await r.text();
        let msg = t; let unknownSectorNames: string[] | undefined;
        try { const parsed = JSON.parse(t); msg = parsed.error ?? t; unknownSectorNames = parsed.unknownSectorNames; } catch { /* corpo não era JSON */ }
        const err = new Error(msg) as Error & { unknownSectorNames?: string[] };
        if (unknownSectorNames) err.unknownSectorNames = unknownSectorNames;
        throw err;
      }
      return r.json() as Promise<import('./types').ImportView>;
    });
  },
  listImports: (eventId: string) => request<import('./types').ImportView[]>(`/api/events/${eventId}/ticket-imports`),
  getImportDetail: (eventId: string, importId: string) =>
    request<{ import: import('./types').ImportView; rows: import('./types').ImportRowView[] }>(`/api/events/${eventId}/ticket-imports/${importId}`),

  // ── Relatórios ────────────────────────────────────────────────────────────
  getValidationReport: (eventId: string, params?: { from?: string; to?: string; gateId?: string; sectorId?: string; timeZone?: string }) => {
    const q = new URLSearchParams();
    if (params?.from) q.set('from', params.from);
    if (params?.to) q.set('to', params.to);
    if (params?.gateId) q.set('gateId', params.gateId);
    if (params?.sectorId) q.set('sectorId', params.sectorId);
    q.set('timeZone', params?.timeZone ?? Intl.DateTimeFormat().resolvedOptions().timeZone);
    return request<import('./types').ValidationReport>(`/api/events/${eventId}/reports/validation?${q.toString()}`);
  },
  exportValidationCsv: (eventId: string) => `${API_BASE_URL}/api/events/${eventId}/reports/validation/export`,

  // ── Operacional ───────────────────────────────────────────────────────────
  bulkUpdateTicketStatus: (eventId: string, ticketIds: string[], status: string) =>
    request<import('./types').BulkStatusResult>(`/api/events/${eventId}/tickets/bulk-status`, {
      method: 'PUT', body: JSON.stringify({ ticketIds, status }),
    }),
  getTicketHistory: (eventId: string, ticketId: string) =>
    request<AttemptPage>(`/api/events/${eventId}/tickets/${ticketId}/history`),
  resetAccessLog: (eventId: string) =>
    request<{ message: string }>(`/api/events/${eventId}/access-attempts/reset`, { method: 'DELETE' }),
  getDataSummary: (eventId: string) =>
    request<import('./types').DataSummary>(`/api/events/${eventId}/admin/data-summary`),
  deleteEventData: (eventId: string, confirmation: string) =>
    request<{ message: string }>(`/api/events/${eventId}/admin/delete-data`, {
      method: 'POST', body: JSON.stringify({ confirmation }),
    }),
};

export { API_BASE_URL };

