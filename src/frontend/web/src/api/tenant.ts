import { api, itemsOf, post, type Paged } from './client'
import { auditQuery, type AuditEntry, type AuditFieldChange, type AuditQuery } from './platform'

export type { AuditEntry, AuditFieldChange, AuditQuery }

const put = <T>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body: JSON.stringify(body) })
const del = (path: string) => api<undefined>(path, { method: 'DELETE' })

export interface Company { id: string; code: string; name: string; isActive: boolean }
export interface Branch { id: string; companyId: string; code: string; name: string; isActive: boolean }
export interface Department { id: string; code: string; name: string; branchId?: string | null; isActive: boolean }
export interface Employee {
  id: string; employeeCode: string; displayName: string
  firstName?: string | null; middleName?: string | null; lastName?: string | null
  workEmail?: string | null; workPhone?: string | null
  companyId: string; branchId: string; departmentId?: string | null
  designationId?: string | null; gradeId?: string | null; locationId?: string | null; costCentreId?: string | null
  managerEmployeeId?: string | null; userId?: string | null
  employmentType?: string | null; joiningDate?: string | null
  probationEndDate?: string | null; confirmationDate?: string | null
  status: string; rowVersion: number
}
export interface CreateEmployeeInput {
  employeeCode: string; displayName: string; workEmail?: string; branchId: string
  departmentId?: string; managerEmployeeId?: string; preJoining?: boolean
  firstName?: string; middleName?: string; lastName?: string; workPhone?: string
  designationId?: string; gradeId?: string; locationId?: string; costCentreId?: string
  employmentType?: string; joiningDate?: string; probationEndDate?: string
}
export interface UpdateEmployeeInput {
  displayName: string; firstName?: string; middleName?: string; lastName?: string
  workEmail?: string; workPhone?: string; rowVersion?: number
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
  createEmployee: (body: CreateEmployeeInput) => post<{ id: string }>('/api/v1/employees', body),
  updateEmployee: (id: string, b: UpdateEmployeeInput) => put<{ id: string }>(`/api/v1/employees/${id}`, b),

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

  // ---- Employee sections ----
  employeePersonal: (id: string) => api<Personal>(`/api/v1/employees/${id}/personal`),
  savePersonal: (id: string, b: PersonalInput) => put(`/api/v1/employees/${id}/personal`, b),
  employeeContacts: (id: string) => api<EmpContact[]>(`/api/v1/employees/${id}/contacts`),
  addEmpContact: (id: string, b: EmpContactInput) => post<{ id: string }>(`/api/v1/employees/${id}/contacts`, b),
  updateEmpContact: (id: string, cid: string, b: EmpContactInput) => put(`/api/v1/employees/${id}/contacts/${cid}`, b),
  deleteEmpContact: (id: string, cid: string) => del(`/api/v1/employees/${id}/contacts/${cid}`),
  employeeBank: (id: string) => api<EmpBank[]>(`/api/v1/employees/${id}/bank-accounts`),
  addBank: (id: string, b: BankInput) => post<{ id: string }>(`/api/v1/employees/${id}/bank-accounts`, b),
  replaceBank: (id: string, aid: string, b: BankInput) => post<{ id: string }>(`/api/v1/employees/${id}/bank-accounts/${aid}/replace`, b),
  verifyBank: (id: string, aid: string, status: string, note?: string) => api<undefined>(`/api/v1/employees/${id}/bank-accounts/${aid}/verify`, { method: 'PATCH', body: JSON.stringify({ status, note }) }),
  employeeStatutory: (id: string) => api<Statutory>(`/api/v1/employees/${id}/statutory`),
  saveStatutory: (id: string, b: StatutoryInput) => put(`/api/v1/employees/${id}/statutory`, b),
  employeeHistory: (id: string) => api<HistoryRow[]>(`/api/v1/employees/${id}/history`),
  lifecycleEvents: (id: string) => api<LifecycleEvent[]>(`/api/v1/employees/${id}/lifecycle-events`),
  createLifecycleEvent: (id: string, b: LifecycleEventInput) => post<{ id: string }>(`/api/v1/employees/${id}/lifecycle-events`, b),
  lifecycleAction: (eventId: string, action: 'submit' | 'approve' | 'cancel' | 'reject', reason?: string) =>
    post<undefined>(`/api/v1/employees/lifecycle-events/${eventId}/${action}`, action === 'reject' ? { reason } : undefined),
  linkUser: (id: string, userId: string) => put(`/api/v1/employees/${id}/user`, { userId }),
  unlinkUser: (id: string) => del(`/api/v1/employees/${id}/user`),

  // ---- Salary ----
  salaryComponents: () => api<SalaryComponent[]>('/api/v1/salary/components'),
  upsertComponent: (b: ComponentInput) => post<{ id: string }>('/api/v1/salary/components', b),
  deleteComponent: (id: string) => del(`/api/v1/salary/components/${id}`),
  salaryStructures: () => api<SalaryStructure[]>('/api/v1/salary/structures'),
  upsertStructure: (b: StructureInput) => post<{ id: string }>('/api/v1/salary/structures', b),
  salaryAssignments: (empId: string) => api<SalaryAssignment[]>(`/api/v1/employees/${empId}/salary-assignments`),
  createAssignment: (empId: string, b: AssignmentInput) => post<{ id: string }>(`/api/v1/employees/${empId}/salary-assignments`, b),
  assignmentAction: (id: string, action: 'submit' | 'approve' | 'cancel' | 'reject', reason?: string) =>
    post<undefined>(`/api/v1/employees/salary-assignments/${id}/${action}`, action === 'reject' ? { reason } : undefined),

  // ---- Documents & letters ----
  docCategories: () => api<DocCategory[]>('/api/v1/employee-docs/categories'),
  letterTypes: () => api<LetterType[]>('/api/v1/employee-docs/letter-types'),
  letterTemplates: (typeId: string) => api<LetterTemplate[]>(`/api/v1/employee-docs/letter-types/${typeId}/templates`),
  saveLetterTemplate: (b: LetterTemplateInput) => post<{ id: string }>(`/api/v1/employee-docs/letter-types/${b.letterTypeId}/templates`, b),
  employeeDocs: (id: string) => api<EmpDocument[]>(`/api/v1/employees/${id}/documents`),
  uploadDoc: (id: string, form: FormData) => api<{ id: string }>(`/api/v1/employees/${id}/documents`, { method: 'POST', body: form }),
  verifyDoc: (id: string, docId: string, status: string, note?: string) =>
    api<undefined>(`/api/v1/employees/${id}/documents/${docId}/verify`, { method: 'PATCH', body: JSON.stringify({ status, note }) }),
  missingDocs: (id: string) => api<DocCategory[]>(`/api/v1/employees/${id}/documents/missing`),
  employeeLetters: (id: string) => api<EmpLetter[]>(`/api/v1/employees/${id}/letters`),
  issueLetter: (id: string, letterTypeId: string, letterTemplateId?: string) =>
    post<{ id: string }>(`/api/v1/employees/${id}/letters`, { letterTypeId, letterTemplateId }),
  revokeLetter: (letterId: string, reason?: string) => post<undefined>(`/api/v1/employees/letters/${letterId}/revoke`, { reason }),
  letterDownloadUrl: (letterId: string) => `/api/v1/employees/letters/${letterId}/download`,
  fileDownloadUrl: (fileId: string) => `/api/v1/files/${fileId}/content`,
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

// ---- Employee section types ----
export interface Personal {
  dateOfBirth?: string; gender?: string; maritalStatus?: string; nationality?: string;
  bloodGroup?: string; personalEmail?: string; personalPhone?: string;
  currentAddress?: Record<string, unknown>; permanentAddress?: Record<string, unknown>;
  rowVersion: number;
}
export type PersonalInput = Omit<Personal, 'rowVersion'> & { rowVersion?: number };

export interface EmpContact {
  id: string; kind: string; name: string; relation?: string; dateOfBirth?: string;
  phone?: string; email?: string; isPrimary: boolean; nomineeSharePct?: number; rowVersion: number;
}
export type EmpContactInput = Omit<EmpContact, 'id' | 'rowVersion'> & { rowVersion?: number };

export interface EmpBank {
  id: string; bankName: string; branchName?: string; accountMasked: string; ifsc: string;
  holderName?: string; isPrimary: boolean; effectiveFrom: string; effectiveTo?: string;
  verificationStatus: string; verificationNote?: string; rowVersion: number;
}
export interface BankInput {
  bankName: string; branchName?: string; accountNumber: string; ifsc: string;
  holderName?: string; isPrimary: boolean; effectiveFrom: string; rowVersion?: number;
}

export interface Statutory {
  panMasked?: string; uan?: string; pfNumber?: string; pfApplicable: boolean; esiNumber?: string;
  esiApplicable: boolean; ptState?: string; lwfApplicable: boolean; aadhaarLast4?: string;
  passportMasked?: string; taxRegime?: string; rowVersion: number;
}
export interface StatutoryInput {
  pan?: string; uan?: string; pfNumber?: string; pfApplicable: boolean; esiNumber?: string;
  esiApplicable: boolean; ptState?: string; lwfApplicable: boolean; aadhaarLast4?: string;
  passport?: string; taxRegime?: string; rowVersion?: number;
}

export interface HistoryRow {
  id: string; eventType: string; employmentType: string; effectiveFrom: string; effectiveTo?: string;
  companyId: string; branchId: string; departmentId?: string; designationId?: string; gradeId?: string;
  locationId?: string; costCentreId?: string; managerEmployeeId?: string; reason?: string;
  payrollAffecting: boolean; rowVersion: number;
}

export interface LifecyclePayload {
  companyId?: string; branchId?: string; departmentId?: string; designationId?: string;
  gradeId?: string; locationId?: string; costCentreId?: string; managerEmployeeId?: string;
  employmentType?: string; joiningDate?: string; probationEndDate?: string;
  confirmationDate?: string; lastWorkingDate?: string; exitReason?: string;
}
export interface LifecycleEvent {
  id: string; employeeId: string; eventType: string; status: string; effectiveDate: string;
  reason?: string; requestedBy: string; requestedAt: string; approvedBy?: string; approvedAt?: string;
  rejectedBy?: string; rejectionReason?: string; effectiveAt?: string; rowVersion: number;
}
export interface LifecycleEventInput {
  eventType: string; effectiveDate: string; reason?: string; payload?: LifecyclePayload; submit?: boolean;
}

// ---- Salary types ----
export interface SalaryComponent {
  id: string; code: string; name: string; type: string; payslipName?: string;
  taxable: boolean; proRata: boolean; partOfCtc: boolean; partOfGross: boolean;
  isStatutory: boolean; isBasic: boolean; displayOrder: number; isActive: boolean; rowVersion: number;
}
export type ComponentInput = Omit<SalaryComponent, 'id' | 'isStatutory' | 'rowVersion'> & { id?: string; rowVersion?: number };

export interface SalaryLine {
  salaryComponentId: string; componentCode: string; componentName: string; componentType: string;
  sequence: number; calcType: string; amount?: number; percent?: number; formula?: string; resolvedAmount?: number;
}
export interface StructureLine extends SalaryLine { id: string }
export interface SalaryStructure {
  id: string; code: string; name: string; description?: string; isActive: boolean;
  lines: StructureLine[]; rowVersion: number;
}
export interface StructureLineInput {
  salaryComponentId: string; sequence: number; calcType: string; amount?: number; percent?: number; formula?: string;
}
export interface StructureInput {
  id?: string; code: string; name: string; description?: string; isActive: boolean;
  lines: StructureLineInput[]; rowVersion?: number;
}

export interface SalaryAssignment {
  id: string; employeeId: string; salaryStructureId: string; structureName: string;
  ctcAmount: number; currency?: string; effectiveFrom: string; effectiveTo?: string;
  status: string; reason?: string; requestedBy?: string; requestedAt: string;
  approvedBy?: string; approvedAt?: string; lines: SalaryLine[]; rowVersion: number;
}
export interface AssignmentInput {
  salaryStructureId: string; ctcAmount: number; currency?: string;
  effectiveFrom: string; reason?: string; submit?: boolean;
}

// ---- Documents & letters types ----
export interface DocCategory {
  id: string; code: string; name: string; description?: string; requiredForOnboarding: boolean;
  retentionYears?: number; confidential: boolean; trackExpiry: boolean; isActive: boolean; rowVersion: number;
}
export interface EmpDocument {
  id: string; employeeId: string; categoryId: string; categoryCode: string; categoryName: string;
  fileId: string; fileName: string; label?: string; documentNumber?: string;
  issuedOn?: string; expiresOn?: string; status: string; verifiedBy?: string; verifiedAt?: string;
  verificationNote?: string; replacedById?: string; version: number; rowVersion: number;
}
export interface LetterType {
  id: string; code: string; name: string; description?: string; signatoryEmployeeId?: string;
  isActive: boolean; rowVersion: number;
}
export interface LetterTemplate {
  id: string; letterTypeId: string; name: string; version: number; subject: string; body: string;
  isActive: boolean; rowVersion: number;
}
export interface LetterTemplateInput {
  letterTypeId: string; name: string; subject: string; body: string; isActive: boolean; rowVersion?: number;
}
export interface EmpLetter {
  id: string; employeeId: string; letterTypeId: string; letterType: string; subject: string;
  renderedText: string; templateVersion: number; status: string; issuedBy: string; issuedAt: string;
  revokedBy?: string; revokedAt?: string; revokeReason?: string; rowVersion: number;
}
