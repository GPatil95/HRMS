import { api, itemsOf, post, type Paged } from './client'
import { auditQuery, type AuditEntry, type AuditFieldChange, type AuditQuery } from './platform'

export type { AuditEntry, AuditFieldChange, AuditQuery }

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
  permissions: async () => {
    const d = await api<unknown>('/api/v1/me/permissions')
    if (Array.isArray(d)) return d as string[]
    return Object.keys(((d as { grants?: Record<string, unknown> })?.grants) ?? {})
  },

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

  // ---- Org masters (company/branch/department detail) ----
  companyDetail: (id: string) => api<CompanyDetail>(`/api/v1/org/companies/${id}`),
  updateCompany: (id: string, body: Record<string, unknown>) => put(`/api/v1/org/companies/${id}`, body),
  addAddress: (cid: string, body: AddressInput) => post<{ id: string }>(`/api/v1/org/companies/${cid}/addresses`, body),
  updateAddress: (cid: string, aid: string, body: AddressInput) => put(`/api/v1/org/companies/${cid}/addresses/${aid}`, body),
  deleteAddress: (cid: string, aid: string) => del(`/api/v1/org/companies/${cid}/addresses/${aid}`),
  addRegistration: (owner: 'companies' | 'branches', oid: string, body: RegistrationInput) =>
    post<{ id: string }>(`/api/v1/org/${owner}/${oid}/registrations`, body),
  updateRegistration: (owner: 'companies' | 'branches', oid: string, rid: string, body: RegistrationInput) =>
    put(`/api/v1/org/${owner}/${oid}/registrations/${rid}`, body),
  deleteRegistration: (owner: 'companies' | 'branches', oid: string, rid: string) =>
    del(`/api/v1/org/${owner}/${oid}/registrations/${rid}`),
  addContact: (cid: string, body: ContactInput) => post<{ id: string }>(`/api/v1/org/companies/${cid}/contacts`, body),
  updateContact: (cid: string, id: string, body: ContactInput) => put(`/api/v1/org/companies/${cid}/contacts/${id}`, body),
  deleteContact: (cid: string, id: string) => del(`/api/v1/org/companies/${cid}/contacts/${id}`),
  bankAccounts: (cid: string) => api<BankAccount[]>(`/api/v1/org/companies/${cid}/bank-accounts`),
  addBankAccount: (cid: string, body: BankAccountInput) => post<{ id: string }>(`/api/v1/org/companies/${cid}/bank-accounts`, body),
  updateBankAccount: (cid: string, id: string, body: BankAccountInput) => put(`/api/v1/org/companies/${cid}/bank-accounts/${id}`, body),
  deleteBankAccount: (cid: string, id: string) => del(`/api/v1/org/companies/${cid}/bank-accounts/${id}`),
  uploadLogo: (cid: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api<{ fileId: string }>(`/api/v1/org/companies/${cid}/logo`, { method: 'POST', body: form })
  },
  removeLogo: (cid: string) => del(`/api/v1/org/companies/${cid}/logo`),
  logoUrl: (cid: string) => `/api/v1/org/companies/${cid}/logo`,
  documents: (owner: 'companies' | 'branches', oid: string) => api<OrgDocument[]>(`/api/v1/org/${owner}/${oid}/documents`),
  uploadDocument: (owner: 'companies' | 'branches', oid: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api<OrgDocument>(`/api/v1/org/${owner}/${oid}/documents`, { method: 'POST', body: form })
  },
  deleteDocument: (owner: 'companies' | 'branches', oid: string, fid: string) => del(`/api/v1/org/${owner}/${oid}/documents/${fid}`),

  branchDetail: (id: string) => api<BranchDetail>(`/api/v1/org/branches/${id}`),
  updateBranch: (id: string, body: Record<string, unknown>) => put(`/api/v1/org/branches/${id}`, body),
  departmentDetail: (id: string) => api<DepartmentDetail>(`/api/v1/org/departments/${id}`),
  updateDepartment: (id: string, body: Record<string, unknown>) => put(`/api/v1/org/departments/${id}`, body),
  departmentAssignments: (id: string) => api<DepartmentAssignment[]>(`/api/v1/org/departments/${id}/assignments`),
  assignDepartmentRole: (id: string, body: { role: string; employeeId?: string | null; effectiveFrom: string; reason?: string }) =>
    post(`/api/v1/org/departments/${id}/hod`, body),
  effectiveConfig: (level: 'companies' | 'branches' | 'departments' | 'employees', id: string) =>
    api<EffectiveConfig>(`/api/v1/org/${level}/${id}/effective-config`),
  weeklyOffPreview: (level: 'companies' | 'branches' | 'departments', id: string, from: string, to: string) =>
    api<WeeklyOffPreview>(`/api/v1/org/${level}/${id}/weekly-off-preview?from=${from}&to=${to}`),

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

  auditEntries: auditQuery('/api/v1/audit/entries'),
}

// ---- Org masters types ----
export interface OrgAddress { id: string; addressType: string; line1?: string; line2?: string; landmark?: string; country?: string; state?: string; district?: string; city?: string; pinCode?: string; contactPerson?: string; contactEmail?: string; contactPhone?: string }
export type AddressInput = Omit<OrgAddress, 'id'>
export interface Registration { id: string; registrationType: string; registrationNumber: string; jurisdiction?: string; registrationSubtype?: string; effectiveFrom?: string; effectiveTo?: string; isActive: boolean; expiresInDays?: number | null }
export type RegistrationInput = Omit<Registration, 'id' | 'expiresInDays'>
export interface OrgContact { id: string; contactType: string; name: string; designation?: string; email?: string; phone?: string }
export type ContactInput = Omit<OrgContact, 'id'>
export interface BankAccount { id: string; bankName: string; branchName?: string; accountNumber: string; ifsc?: string; isPrimary: boolean }
export type BankAccountInput = Omit<BankAccount, 'id'>
export interface OrgDocument { id: string; fileName: string; contentType: string; sizeBytes: number; uploadedAt: string }
export interface CompanyDetail extends Company {
  legalName?: string; companyType?: string; industry?: string; parentCompanyId?: string; incorporationDate?: string
  website?: string; officialEmail?: string; contactNumber?: string; logoFileId?: string | null; parentCompanyName?: string
  cin?: string; pan?: string; tan?: string; pfEstablishmentCode?: string; esicEmployerCode?: string
  professionalTaxRegNo?: string; shopsEstablishmentRegNo?: string; factoryLicenceNo?: string; msmeUdyamRegNo?: string
  currency?: string; financialYearStartMonth?: number; payrollFrequency?: string; salaryPayDay?: number
  defaultLeavePolicyCode?: string; defaultHolidayCalendarCode?: string; defaultAttendancePolicyCode?: string
  defaultShiftName?: string; weeklyOffPattern?: string; weeklyOffConfigJson?: string; graceMinutes?: number
  rowVersion?: number; createdAt?: string; modifiedAt?: string; branchCount?: number; employeeCount?: number
  addresses: OrgAddress[]; registrations: Registration[]; contacts: OrgContact[]
}
export interface BranchDetail extends Branch {
  timeZone?: string; branchType?: string; openingDate?: string; managerEmployeeId?: string; hrEmployeeId?: string
  contactPhone?: string; contactEmail?: string; addressLine1?: string; addressLine2?: string; city?: string; district?: string
  state?: string; country?: string; pinCode?: string; workLocationType?: string; clientSiteRef?: string
  latitude?: number; longitude?: number; geofenceRadiusMeters?: number
  defaultShiftName?: string; workStart?: string; workEnd?: string; breakMinutes?: number
  weeklyOffPattern?: string; weeklyOffConfigJson?: string; holidayCalendarCode?: string; graceMinutes?: number
  overtimeAllowed?: boolean; attendanceMode?: string; attendancePolicyCode?: string; payrollDay?: number; costCentre?: string
  rowVersion?: number; companyName?: string; managerName?: string; hrName?: string; employeeCount?: number; departmentCount?: number
  registrations: Registration[]
}
export interface DepartmentDetail extends Department {
  parentDepartmentId?: string; departmentType?: string; description?: string; costCentre?: string; effectiveDate?: string
  reportingManagerEmployeeId?: string; hodEmployeeId?: string; altHodEmployeeId?: string; hodEffectiveFrom?: string
  defaultShiftName?: string; workStart?: string; workEnd?: string; breakMinutes?: number
  weeklyOffPattern?: string; weeklyOffConfigJson?: string; holidayCalendarCode?: string; graceMinutes?: number
  overtimeAllowed?: boolean; wfhAllowed?: boolean; attendancePolicyCode?: string
  leaveApproverEmployeeId?: string; altLeaveApproverEmployeeId?: string; odApproverEmployeeId?: string
  compOffApproverEmployeeId?: string; otApproverEmployeeId?: string; regularizationApproverEmployeeId?: string
  escalationLevel?: number; approvedHeadcount?: number; budgetAmount?: number; workArea?: string
  rowVersion?: number; branchName?: string; parentDepartmentName?: string
  currentHeadcount?: number; vacancies?: number; employeeNames: Record<string, string>
}
export interface DepartmentAssignment { id: string; role: string; employeeId?: string; employeeName?: string; effectiveFrom: string; effectiveTo?: string; reason?: string; createdAt: string }
export interface EffectiveSetting { key: string; label: string; value: unknown; source: string; sourceId?: string; sourceName?: string; isOverride: boolean }
export interface EffectiveConfig { level: string; id: string; settings: EffectiveSetting[] }
export interface WeeklyOffPreview { pattern?: string; source: string; offDays: string[] }

// ---------------- M2.1 org reference masters ----------------
export interface Designation { id: string; code: string; name: string; level?: number | null; description?: string | null; isActive: boolean; rowVersion: number }
export interface Grade { id: string; code: string; name: string; sortOrder: number; description?: string | null; isActive: boolean; rowVersion: number }
export interface WorkLocation { id: string; code: string; name: string; branchId?: string | null; branchName?: string | null; locationType?: string | null; description?: string | null; isActive: boolean; rowVersion: number }
export interface CostCentre { id: string; code: string; name: string; companyId?: string | null; companyName?: string | null; description?: string | null; isActive: boolean; rowVersion: number }

export interface DesignationInput { code: string; name: string; level?: number | null; description?: string | null; rowVersion?: number }
export interface GradeInput { code: string; name: string; sortOrder: number; description?: string | null; rowVersion?: number }
export interface WorkLocationInput { code: string; name: string; branchId?: string | null; locationType?: string | null; description?: string | null; rowVersion?: number }
export interface CostCentreInput { code: string; name: string; companyId?: string | null; description?: string | null; rowVersion?: number }

const mastersBase = '/api/v1/org'
export const mastersApi = {
  designations: (includeInactive = true) => api<Designation[]>(`${mastersBase}/designations?includeInactive=${includeInactive}`),
  createDesignation: (b: DesignationInput) => post<{ id: string }>(`${mastersBase}/designations`, b),
  updateDesignation: (id: string, b: DesignationInput) => put(`${mastersBase}/designations/${id}`, b),
  setDesignationStatus: (id: string, isActive: boolean) => api<undefined>(`${mastersBase}/designations/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) }),
  deleteDesignation: (id: string) => del(`${mastersBase}/designations/${id}`),

  grades: (includeInactive = true) => api<Grade[]>(`${mastersBase}/grades?includeInactive=${includeInactive}`),
  createGrade: (b: GradeInput) => post<{ id: string }>(`${mastersBase}/grades`, b),
  updateGrade: (id: string, b: GradeInput) => put(`${mastersBase}/grades/${id}`, b),
  setGradeStatus: (id: string, isActive: boolean) => api<undefined>(`${mastersBase}/grades/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) }),
  deleteGrade: (id: string) => del(`${mastersBase}/grades/${id}`),

  locations: (includeInactive = true) => api<WorkLocation[]>(`${mastersBase}/locations?includeInactive=${includeInactive}`),
  createLocation: (b: WorkLocationInput) => post<{ id: string }>(`${mastersBase}/locations`, b),
  updateLocation: (id: string, b: WorkLocationInput) => put(`${mastersBase}/locations/${id}`, b),
  setLocationStatus: (id: string, isActive: boolean) => api<undefined>(`${mastersBase}/locations/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) }),
  deleteLocation: (id: string) => del(`${mastersBase}/locations/${id}`),

  costCentres: (includeInactive = true) => api<CostCentre[]>(`${mastersBase}/cost-centres?includeInactive=${includeInactive}`),
  createCostCentre: (b: CostCentreInput) => post<{ id: string }>(`${mastersBase}/cost-centres`, b),
  updateCostCentre: (id: string, b: CostCentreInput) => put(`${mastersBase}/cost-centres/${id}`, b),
  setCostCentreStatus: (id: string, isActive: boolean) => api<undefined>(`${mastersBase}/cost-centres/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) }),
  deleteCostCentre: (id: string) => del(`${mastersBase}/cost-centres/${id}`),
}
