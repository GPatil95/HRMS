import { api, post } from './client'

const put = <T>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body: JSON.stringify(body) })
const del = (path: string) => api<undefined>(path, { method: 'DELETE' })
const qs = (o: Record<string, string | number | boolean | undefined | null>) =>
  new URLSearchParams(Object.entries(o).filter(([, v]) => v !== undefined && v !== null && v !== '').map(([k, v]) => [k, String(v)])).toString()
const withKey = <T>(path: string, body: unknown) =>
  api<T>(path, { method: 'POST', body: JSON.stringify(body ?? {}), headers: { 'Idempotency-Key': crypto.randomUUID() } })

export interface PagedResult<T> { items: T[]; page: number; pageSize: number; totalCount: number }
export interface WorkItem {
  id: string; moduleCode: string; itemType: string; sourceRef: string; workflowInstanceId?: string | null
  title: string; dueAt?: string | null; priority: string; status: string; deepLink?: string | null
  createdAt: string; completedAt?: string | null; overdue: boolean; claimed: boolean; claimedByMe: boolean
}
export interface WorkflowTask {
  id: string; instanceId: string; stepOrder: number; name: string; approvalMode: string
  assigneeUserId: string; assigneeName?: string | null; delegatedFromUserId?: string | null; claimedByUserId?: string | null
  status: string; dueAt?: string | null; escalationLevel: number; actedAt?: string | null; comment?: string | null
}
export interface WorkflowInstance {
  id: string; definitionCode: string; definitionVersion: number; documentType: string; documentId: string
  moduleCode: string; title: string; deepLink?: string | null; requesterUserId: string; requesterName?: string | null
  status: string; currentStepOrder: number; startedAt: string; completedAt?: string | null; tasks: WorkflowTask[]
}
export interface WorkflowHistoryEntry {
  id: string; instanceStepId?: string | null; action: string; actorName?: string | null; targetName?: string | null
  comment?: string | null; occurredAt: string
}
export interface WorkflowStep {
  id?: string; stepOrder: number; name: string; approverType: string; approverParam?: string | null; approvalMode: string
  minAmount?: number | null; maxAmount?: number | null; fallbackUserId?: string | null; slaHours?: number | null
  escalationType: string; escalationParam?: string | null; allowSendBack: boolean; allowDelegate: boolean
  approverScope?: string
}
export interface WorkflowUser { id: string; displayName: string; email: string }
export interface WorkflowDefinition {
  id: string; code: string; version: number; name: string; documentType: string; moduleCode: string
  description?: string | null; status: string; effectiveFrom?: string | null; effectiveTo?: string | null; priority: number
  minAmount?: number | null; maxAmount?: number | null; companyId?: string | null; branchId?: string | null
  departmentId?: string | null; rowVersion: number; steps: WorkflowStep[]
}
export interface WorkflowDefinitionInput {
  code: string; name: string; documentType: string; description?: string | null; priority: number
  minAmount?: number | null; maxAmount?: number | null; companyId?: string | null; branchId?: string | null
  departmentId?: string | null; steps: WorkflowStep[]; rowVersion?: number
}
export interface WorkflowDocumentType { code: string; moduleCode: string; name: string }
export interface WorkItemFilter { status?: string; moduleCode?: string; search?: string; overdueOnly?: boolean }

export const workflowApi = {
  myItems: (f: WorkItemFilter) => api<PagedResult<WorkItem>>(`/api/v1/workitems/mine?${qs({ ...f, pageSize: 200 })}`),
  claim: (id: string) => withKey<WorkflowInstance>(`/api/v1/workitems/${id}/claim`, {}),
  complete: (id: string, action: string, comment?: string, sendBackTo?: string) =>
    withKey<WorkflowInstance>(`/api/v1/workitems/${id}/complete`, { action, comment, sendBackTo }),
  delegate: (id: string, toUserId: string, comment?: string) =>
    withKey<WorkflowInstance>(`/api/v1/workitems/${id}/delegate`, { toUserId, comment }),
  instance: (id: string) => api<WorkflowInstance>(`/api/v1/workflow/instances/${id}`),
  history: (id: string) => api<WorkflowHistoryEntry[]>(`/api/v1/workflow/instances/${id}/history`),
  eligibleDelegates: (search: string, page = 1, pageSize = 20) =>
    api<PagedResult<WorkflowUser>>(`/api/v1/workitems/eligible-delegates?${qs({ search, page, pageSize })}`),
  eligibleAssignees: (search: string, page = 1, pageSize = 20) =>
    api<PagedResult<WorkflowUser>>(`/api/v1/workflow/eligible-assignees?${qs({ search, page, pageSize })}`),
  stalled: () => api<PagedResult<WorkflowInstance>>('/api/v1/workflow/instances/stalled?pageSize=100'),
  resume: (id: string, assigneeUserId?: string, comment?: string) =>
    withKey<WorkflowInstance>(`/api/v1/workflow/instances/${id}/resume`, { assigneeUserId: assigneeUserId ?? null, comment }),
  myRequests: () => api<PagedResult<WorkflowInstance>>('/api/v1/workflow/instances?mine=true&pageSize=100'),
  cancel: (id: string, comment?: string) => withKey<WorkflowInstance>(`/api/v1/workflow/instances/${id}/cancel`, { comment }),
  resubmit: (id: string, comment?: string) => withKey<WorkflowInstance>(`/api/v1/workflow/instances/${id}/resubmit`, { comment }),
  documentTypes: () => api<WorkflowDocumentType[]>('/api/v1/workflow/document-types'),
  definitions: () => api<WorkflowDefinition[]>('/api/v1/workflow/definitions'),
  createDefinition: (b: WorkflowDefinitionInput) => post<WorkflowDefinition>('/api/v1/workflow/definitions', b),
  updateDefinition: (id: string, b: WorkflowDefinitionInput) => put<WorkflowDefinition>(`/api/v1/workflow/definitions/${id}`, b),
  deleteDefinition: (id: string) => del(`/api/v1/workflow/definitions/${id}`),
  publishDefinition: (id: string, effectiveFrom?: string) => post<WorkflowDefinition>(`/api/v1/workflow/definitions/${id}/publish`, { effectiveFrom: effectiveFrom || null }),
  retireDefinition: (id: string) => post<WorkflowDefinition>(`/api/v1/workflow/definitions/${id}/retire`, {}),
}
