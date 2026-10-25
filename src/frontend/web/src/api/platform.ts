import { api, post } from './client'

export type TenantStatus = 'Provisioning' | 'ProvisioningFailed' | 'Active' | 'Suspended' | 'Inactive'
export type ModuleKind = 'Core' | 'Optional' | 'SubModule'
export type SubscriptionStatus = 'Trial' | 'Active' | 'PastDue' | 'Grace' | 'Suspended' | string
export type SupportGrantStatus = 'Requested' | 'Approved' | 'Rejected' | 'Revoked' | 'Expired'

export interface TenantSummary {
  id: string
  tenantNumber: number
  code: string
  name: string
  status: TenantStatus
  primaryHost?: string | null
  createdAt: string
}

export interface TenantModule {
  code: string
  name: string
  kind: ModuleKind
  enabled: boolean
  source?: 'Plan' | 'AddOn' | 'Trial' | 'Manual' | null
  validTo?: string | null
}

export interface TenantDetail {
  tenant: TenantSummary
  databaseName?: string | null
  databaseStatus?: 'Provisioning' | 'Online' | 'Maintenance' | 'Offline' | null
  schemaVersion?: string | null
  planCode?: string | null
  subscriptionStatus?: SubscriptionStatus | null
  provisioningStatus?: 'Pending' | 'Running' | 'Succeeded' | 'Failed' | null
  provisioningError?: string | null
  modules: TenantModule[]
}

export interface CatalogModule {
  code: string
  name: string
  kind: ModuleKind
  parentCode?: string | null
  sortOrder: number
  isActive: boolean
}

export interface SubscriptionPlan {
  code: string
  name: string
  version: number
  status: 'Draft' | 'Published' | 'Retired'
  isPublic: boolean
  employeeLimit?: number | null
}

export interface AuditEntry {
  occurredAt: string
  tenantId?: string | null
  moduleCode: string
  entityType: string
  entityId: string
  action: string
  actorType: string
  actorDisplay?: string | null
  source: string
  ipAddress?: string | null
  correlationId?: string | null
  reason?: string | null
}

export interface SecurityEvent {
  occurredAt: string
  tenantId?: string | null
  eventType: string
  success: boolean
  userEmail?: string | null
  ipAddress?: string | null
  correlationId?: string | null
  details?: string | null
}

export interface SupportGrant {
  id: string
  tenantId: string
  platformUserId: string
  ticketRef: string
  reason: string
  accessLevel: 'MetadataOnly' | 'ReadOnly' | 'ReadWrite'
  durationMinutes: number
  status: SupportGrantStatus
  startsAt?: string | null
  expiresAt?: string | null
}

export const platformApi = {
  tenants: (page: number, pageSize: number, search?: string) =>
    api<{ items: TenantSummary[]; page: number; pageSize: number; totalCount: number }>(
      `/api/v1/platform/tenants?page=${page}&pageSize=${pageSize}${search ? `&search=${encodeURIComponent(search)}` : ''}`),
  tenant: (id: string) => api<TenantDetail>(`/api/v1/platform/tenants/${id}`),
  createTenant: (b: { code: string; name: string; adminEmail: string; adminName: string; planCode?: string }) =>
    post<{ tenantId: string; operationId: string }>('/api/v1/platform/tenants', b),
  setTenantStatus: (id: string, status: TenantStatus, reason: string) =>
    post(`/api/v1/platform/tenants/${id}/status`, { status, reason }),
  retryProvisioning: (id: string) => post(`/api/v1/platform/tenants/${id}/retry-provisioning`),
  setModule: (id: string, moduleCode: string, enabled: boolean, reason: string) =>
    api(`/api/v1/platform/tenants/${id}/modules`, { method: 'PUT', body: JSON.stringify({ moduleCode, enabled, reason, validTo: null }) }),
  setSubscription: (id: string, status: string, endsOn: string | null, reason: string) =>
    api(`/api/v1/platform/tenants/${id}/subscription`, { method: 'PUT', body: JSON.stringify({ status, endsOn, reason }) }),
  modules: () => api<CatalogModule[]>('/api/v1/platform/catalog/modules'),
  plans: () => api<SubscriptionPlan[]>('/api/v1/platform/plans'),
  audit: () => api<AuditEntry[]>('/api/v1/platform/audit'),
  securityEvents: () => api<SecurityEvent[]>('/api/v1/platform/security-events'),
  supportGrants: () => api<SupportGrant[]>('/api/v1/platform/support-grants'),
  decideGrant: (id: string, approve: boolean) => post(`/api/v1/platform/support-grants/${id}/${approve ? 'approve' : 'reject'}`),
  revokeGrant: (id: string) => post(`/api/v1/platform/support-grants/${id}/revoke`),
  health: (kind: 'live' | 'ready') => api<string>(`/health/${kind}`),
}
