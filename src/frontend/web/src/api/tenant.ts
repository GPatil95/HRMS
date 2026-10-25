import { api, itemsOf, post, type Paged } from './client'

const put = <T>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body: JSON.stringify(body) })
const del = (path: string) => api<undefined>(path, { method: 'DELETE' })

export interface Company { id: string; code: string; name: string; isActive: boolean }
export interface Branch { id: string; companyId: string; code: string; name: string; isActive: boolean }
export interface Department { id: string; code: string; name: string; branchId?: string | null; isActive: boolean }
export interface Employee {
  id: string; employeeCode: string; displayName: string; workEmail?: string | null
  companyId: string; branchId: string; departmentId?: string | null; managerEmployeeId?: string | null
  status: string
}
export interface UserSummary {
  id: string; email: string; displayName: string; status: string
  employeeId?: string | null; mfaEnabled: boolean; lastLoginAt?: string | null
}
export interface RoleAssignment {
  id: string; roleId: string; roleCode: string; roleName: string
  validFrom: string; validTo?: string | null
  targets: { kind: string; targetId: string }[]
}
export interface UserOverride {
  id: string; permissionCode: string; effect: string; scopeLevel?: string | null
  targetKind?: string | null; targetIds: string[]; validFrom: string; validTo?: string | null; reason: string
}
export interface UserDetail { user: UserSummary; roles: RoleAssignment[]; overrides: UserOverride[] }
export interface RolePermission { permissionCode: string; scopeLevel: string }
export interface Role {
  id: string; code: string; name: string; description?: string | null
  isSystem: boolean; userCount: number; permissions: RolePermission[]
}
export interface PermissionDef {
  code: string; moduleCode: string; kind: string; action: string
  description: string; supportsScope: boolean; isSensitive: boolean
}
export interface TenantNotification {
  id: string; type: string; title: string; body?: string | null; link?: string | null
  priority: string; isRead: boolean; createdAt: string
}
export interface DashboardWidget {
  id: string; widgetCode: string; title: string; type: string
  x: number; y: number; width: number; height: number
}
export interface TenantDashboard { id: string; code: string; name: string; widgets: DashboardWidget[] }
export interface ReportDescriptor {
  code: string; title: string; moduleCode: string
  viewPermission: string; exportPermission: string
}
export interface ReportResult { columns: string[]; rows: (string | null)[][]; totalCount: number }

export const tenantApi = {
  me: () => api<{ userId: string; email: string; displayName: string }>('/api/v1/auth/me'),
  permissions: () => api<string[]>('/api/v1/me/permissions'),

  dashboard: () => api<TenantDashboard>('/api/v1/dashboard'),
  widgetData: (code: string) => api<unknown>(`/api/v1/dashboard/widgets/${encodeURIComponent(code)}`),

  employees: () => api<Paged<Employee>>('/api/v1/employees').then(itemsOf),
  employee: (id: string) => api<Employee>(`/api/v1/employees/${id}`),
  createEmployee: (body: { employeeCode: string; displayName: string; workEmail?: string; branchId: string; departmentId?: string; managerEmployeeId?: string }) =>
    post<Employee>('/api/v1/employees', body),

  companies: () => api<Company[]>('/api/v1/org/companies'),
  createCompany: (body: { code: string; name: string; legalName?: string }) => post<Company>('/api/v1/org/companies', body),
  branches: () => api<Branch[]>('/api/v1/org/branches'),
  createBranch: (body: { companyId: string; code: string; name: string }) => post<Branch>('/api/v1/org/branches', body),
  departments: () => api<Department[]>('/api/v1/org/departments'),
  createDepartment: (body: { code: string; name: string; branchId?: string }) => post<Department>('/api/v1/org/departments', body),

  users: () => api<Paged<UserSummary>>('/api/v1/users').then(itemsOf),
  user: (id: string) => api<UserDetail>(`/api/v1/users/${id}`),
  createUser: (body: { email: string; displayName: string; employeeId?: string; roleIds?: string[] }) =>
    post<{ id: string }>('/api/v1/users', body),
  setUserStatus: (id: string, status: string, reason?: string) =>
    post(`/api/v1/users/${id}/status`, { status, reason }),
  effectivePermissions: (id: string) => api<Record<string, unknown[]>>(`/api/v1/users/${id}/effective-permissions`),
  revokeSessions: (id: string) => post(`/api/v1/users/${id}/sessions/revoke`, {}),

  roles: () => api<Role[]>('/api/v1/roles'),
  role: (id: string) => api<Role>(`/api/v1/roles/${id}`),
  rolePermissions: () => api<PermissionDef[]>('/api/v1/roles/permissions'),
  createRole: (body: { code: string; name: string; description?: string; permissions: RolePermission[] }) =>
    post<Role>('/api/v1/roles', body),
  updateRole: (id: string, body: { code: string; name: string; description?: string; permissions: RolePermission[] }) =>
    put<Role>(`/api/v1/roles/${id}`, body),
  deleteRole: (id: string) => del(`/api/v1/roles/${id}`),

  reports: () => api<ReportDescriptor[]>('/api/v1/reports'),
  report: (code: string) => api<ReportResult>(`/api/v1/reports/${encodeURIComponent(code)}`),

  notifications: () => api<Paged<TenantNotification>>('/api/v1/notifications').then(itemsOf),
  unreadCount: () => api<{ count: number } | number>('/api/v1/notifications/unread-count'),
  markRead: (id: string) => post(`/api/v1/notifications/${id}/read`, {}),
  markAllRead: () => post('/api/v1/notifications/read-all', {}),
}
