import { useState } from 'react'
import {
  Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, MenuItem,
  Stack, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { useSearchParams } from 'react-router-dom'
import { Add, Delete, Edit, Publish, Archive, PlayArrow } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { workflowApi, type WorkflowDefinition, type WorkflowDefinitionInput, type WorkflowInstance, type WorkflowStep, type WorkflowUser } from '../../api/workflow'
import { EligibleUserPicker } from './WorkflowUserPicker'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'
import { usePermissions } from './OrgShared'

const approverTypes = [
  ['reporting_manager', 'Reporting manager'], ['department_head', 'Department head'],
  ['department_field', 'Department approver field'], ['role', 'Role'], ['user', 'Specific user'],
] as const
const approverScopes = [['tenant', 'Whole tenant'], ['company', 'Same company'], ['branch', 'Same branch'], ['department', 'Same department'], ['reporting', 'Reporting line']] as const
const escalationTypes = [['none', 'None'], ['assignee_manager', "Assignee's manager"], ['role', 'Role'], ['user', 'User']] as const
const paramHint: Record<string, string> = {
  department_field: 'Department field (e.g. hr_partner_user_id)', role: 'Role code', user: 'User ID',
}

const blankStep = (order: number): WorkflowStep => ({
  stepOrder: order, name: `Stage ${order}`, approverType: 'reporting_manager', approverParam: null, approverScope: 'tenant', approvalMode: 'any',
  minAmount: null, maxAmount: null, fallbackUserId: null, slaHours: null, escalationType: 'none', escalationParam: null,
  allowSendBack: true, allowDelegate: true,
})
const num = (v: string) => (v === '' ? null : Number(v))

function DefinitionDialog({ initial, onClose }: { initial: WorkflowDefinition | null; onClose: () => void }) {
  const toast = useToast(); const qc = useQueryClient()
  const types = useQuery({ queryKey: ['wf-doc-types'], queryFn: workflowApi.documentTypes })
  const [form, setForm] = useState<WorkflowDefinitionInput>(() => initial ? {
    code: initial.code, name: initial.name, documentType: initial.documentType, description: initial.description,
    priority: initial.priority, minAmount: initial.minAmount, maxAmount: initial.maxAmount, companyId: initial.companyId,
    branchId: initial.branchId, departmentId: initial.departmentId, steps: initial.steps, rowVersion: initial.rowVersion,
  } : { code: '', name: '', documentType: '', priority: 0, steps: [blankStep(1)] })
  const setStep = (i: number, patch: Partial<WorkflowStep>) =>
    setForm(f => ({ ...f, steps: f.steps.map((s, j) => (j === i ? { ...s, ...patch } : s)) }))
  const save = useMutation({
    mutationFn: () => initial ? workflowApi.updateDefinition(initial.id, form) : workflowApi.createDefinition(form),
    onSuccess: () => { toast('Draft saved'); void qc.invalidateQueries({ queryKey: ['wf-definitions'] }); onClose() },
    onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  return (
    <Dialog open onClose={onClose} maxWidth="lg" fullWidth>
      <DialogTitle>{initial ? `Edit draft ${initial.code} v${initial.version}` : 'New workflow definition'}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: 2 }}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ pt: 1 }}>
          <TextField size="small" label="Code" value={form.code} disabled={!!initial} onChange={e => setForm(f => ({ ...f, code: e.target.value }))} />
          <TextField size="small" label="Name" value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} sx={{ flex: 1 }} />
          <TextField select size="small" label="Document type" value={form.documentType} disabled={!!initial} sx={{ minWidth: 240 }}
            onChange={e => setForm(f => ({ ...f, documentType: e.target.value }))}>
            {(types.data ?? []).map(t => <MenuItem key={t.code} value={t.code}>{t.name} ({t.code})</MenuItem>)}
          </TextField>
        </Stack>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
          <TextField size="small" type="number" label="Priority" value={form.priority} onChange={e => setForm(f => ({ ...f, priority: Number(e.target.value) }))} />
          <TextField size="small" type="number" label="Min amount" value={form.minAmount ?? ''} onChange={e => setForm(f => ({ ...f, minAmount: num(e.target.value) }))} />
          <TextField size="small" type="number" label="Max amount" value={form.maxAmount ?? ''} onChange={e => setForm(f => ({ ...f, maxAmount: num(e.target.value) }))} />
          <TextField size="small" label="Description" value={form.description ?? ''} onChange={e => setForm(f => ({ ...f, description: e.target.value }))} sx={{ flex: 1 }} />
        </Stack>
        <Typography variant="subtitle2">Steps — steps sharing a stage number run in parallel</Typography>
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>Stage</TableCell><TableCell>Name</TableCell><TableCell>Approver</TableCell><TableCell>Parameter</TableCell><TableCell>Scope</TableCell>
              <TableCell>Mode</TableCell><TableCell>Amount range</TableCell><TableCell>SLA h</TableCell><TableCell>Escalation</TableCell>
              <TableCell>Options</TableCell><TableCell />
            </TableRow></TableHead>
            <TableBody>
              {form.steps.map((s, i) => (
                <TableRow key={i}>
                  <TableCell><TextField size="small" type="number" value={s.stepOrder} onChange={e => setStep(i, { stepOrder: Number(e.target.value) })} sx={{ width: 70 }} /></TableCell>
                  <TableCell><TextField size="small" value={s.name} onChange={e => setStep(i, { name: e.target.value })} sx={{ minWidth: 200 }} /></TableCell>
                  <TableCell>
                    <TextField select size="small" value={s.approverType} onChange={e => setStep(i, { approverType: e.target.value, approverScope: e.target.value === 'role' ? s.approverScope : 'tenant' })} sx={{ minWidth: 170 }}>
                      {approverTypes.map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}
                    </TextField>
                  </TableCell>
                  <TableCell><TextField size="small" placeholder={paramHint[s.approverType] ?? '—'} disabled={!paramHint[s.approverType]} value={s.approverParam ?? ''} onChange={e => setStep(i, { approverParam: e.target.value || null })} sx={{ minWidth: 150 }} /></TableCell>
                  <TableCell>
                    <TextField select size="small" value={s.approverScope ?? 'tenant'} disabled={s.approverType !== 'role'} onChange={e => setStep(i, { approverScope: e.target.value })} sx={{ minWidth: 150 }}>
                      {approverScopes.map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}
                    </TextField>
                  </TableCell>
                  <TableCell>
                    <TextField select size="small" value={s.approvalMode} onChange={e => setStep(i, { approvalMode: e.target.value })}>
                      <MenuItem value="any">Any one</MenuItem><MenuItem value="all">All</MenuItem>
                    </TextField>
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={1}>
                      <TextField size="small" type="number" placeholder="min" value={s.minAmount ?? ''} onChange={e => setStep(i, { minAmount: num(e.target.value) })} sx={{ width: 90 }} />
                      <TextField size="small" type="number" placeholder="max" value={s.maxAmount ?? ''} onChange={e => setStep(i, { maxAmount: num(e.target.value) })} sx={{ width: 90 }} />
                    </Stack>
                  </TableCell>
                  <TableCell><TextField size="small" type="number" value={s.slaHours ?? ''} onChange={e => setStep(i, { slaHours: num(e.target.value) })} sx={{ width: 80 }} /></TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={1}>
                      <TextField select size="small" value={s.escalationType} onChange={e => setStep(i, { escalationType: e.target.value })} sx={{ minWidth: 140 }}>
                        {escalationTypes.map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}
                      </TextField>
                      {(s.escalationType === 'role' || s.escalationType === 'user') &&
                        <TextField size="small" placeholder={s.escalationType === 'role' ? 'Role code' : 'User ID'} value={s.escalationParam ?? ''} onChange={e => setStep(i, { escalationParam: e.target.value || null })} />}
                    </Stack>
                  </TableCell>
                  <TableCell sx={{ whiteSpace: 'nowrap' }}>
                    <FormControlLabel control={<Checkbox size="small" checked={s.allowSendBack} onChange={e => setStep(i, { allowSendBack: e.target.checked })} />} label="Send back" />
                    <FormControlLabel control={<Checkbox size="small" checked={s.allowDelegate} onChange={e => setStep(i, { allowDelegate: e.target.checked })} />} label="Delegate" />
                  </TableCell>
                  <TableCell>
                    <IconButton size="small" aria-label="Remove step" disabled={form.steps.length === 1} onClick={() => setForm(f => ({ ...f, steps: f.steps.filter((_, j) => j !== i) }))}><Delete fontSize="small" /></IconButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
        <Box><Button startIcon={<Add />} onClick={() => setForm(f => ({ ...f, steps: [...f.steps, blankStep(Math.max(0, ...f.steps.map(s => s.stepOrder)) + 1)] }))}>Add step</Button></Box>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={save.isPending || !form.code || !form.name || !form.documentType} onClick={() => save.mutate()}>Save draft</Button>
      </DialogActions>
    </Dialog>
  )
}

function ResumeDialog({ instance, onClose }: { instance: WorkflowInstance; onClose: () => void }) {
  const toast = useToast(); const qc = useQueryClient()
  const [assignee, setAssignee] = useState<WorkflowUser | null>(null)
  const [comment, setComment] = useState('')
  const run = useMutation({
    mutationFn: () => workflowApi.resume(instance.id, assignee?.id, comment || undefined),
    onSuccess: () => { toast('Request resumed'); void qc.invalidateQueries({ queryKey: ['wf-stalled'] }); onClose() },
    onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Resume: {instance.title}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: 2 }}>
        <Typography variant="body2" color="text.secondary">
          Approvers for stage {instance.currentStepOrder} are resolved again. Choose an assignee for any step that still has no approver,
          or leave it empty after fixing the role assignment or reporting line.
        </Typography>
        <EligibleUserPicker label="Assign to (optional)" instanceId={instance.id} value={assignee} onChange={setAssignee} />
        <TextField size="small" multiline minRows={2} label="Comment" value={comment} onChange={e => setComment(e.target.value)} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={run.isPending} onClick={() => run.mutate()}>Resume</Button>
      </DialogActions>
    </Dialog>
  )
}

function StalledRequests() {
  const canResume = usePermissions().has('workflow.instances.manage')
  const [resume, setResume] = useState<WorkflowInstance | null>(null)
  const q = useQuery({ queryKey: ['wf-stalled'], queryFn: workflowApi.stalled })
  return (
    <SectionCard title="Stalled requests" subtitle="Requests with a required step that resolved no approver. Nothing is skipped until an administrator resumes them.">
      {q.isLoading ? <TableSkeleton columns={6} /> : q.error ? <ErrorAlert error={q.error} onRetry={() => void q.refetch()} /> :
        !q.data?.items.length ? <EmptyState title="No stalled requests" hint="Requests appear here when no approver can be resolved." /> : (
          <Box sx={{ overflowX: 'auto' }}>
            <Table size="small">
              <TableHead><TableRow>
                <TableCell>Request</TableCell><TableCell>Document type</TableCell><TableCell>Requester</TableCell><TableCell>Stage</TableCell>
                <TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
              </TableRow></TableHead>
              <TableBody>
                {q.data.items.map(r => (
                  <TableRow key={r.id} hover>
                    <TableCell>{r.title}</TableCell><TableCell>{r.documentType}</TableCell><TableCell>{r.requesterName ?? '—'}</TableCell>
                    <TableCell>{r.currentStepOrder}</TableCell><TableCell><StatusChip value={r.status} /></TableCell>
                    <TableCell align="right">
                      {canResume && <Button size="small" startIcon={<PlayArrow />} onClick={() => setResume(r)}>Resume</Button>}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Box>
        )}
      {resume && <ResumeDialog instance={resume} onClose={() => setResume(null)} />}
    </SectionCard>
  )
}

export function WorkflowDefinitionsPage() {
  const [params, setParams] = useSearchParams()
  const canSeeStalled = usePermissions().has('workflow.instances.manage')
  const tab = canSeeStalled && params.get('tab') === 'stalled' ? 1 : 0
  const toast = useToast(); const qc = useQueryClient()
  const canManage = usePermissions().has('workflow.definitions.manage')
  const [editing, setEditing] = useState<WorkflowDefinition | null | undefined>(undefined)
  const q = useQuery({ queryKey: ['wf-definitions'], queryFn: workflowApi.definitions })
  const done = (msg: string) => ({
    onSuccess: () => { toast(msg); void qc.invalidateQueries({ queryKey: ['wf-definitions'] }) },
    onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  const publish = useMutation({ mutationFn: (id: string) => workflowApi.publishDefinition(id), ...done('Published') })
  const retire = useMutation({ mutationFn: (id: string) => workflowApi.retireDefinition(id), ...done('Retired') })
  const remove = useMutation({ mutationFn: (id: string) => workflowApi.deleteDefinition(id), ...done('Draft deleted') })
  return (
    <Box>
      <PageHeader title="Workflow definitions" subtitle="Versioned approval chains shared by all modules. Published versions are immutable."
        actions={canManage && <Button variant="contained" startIcon={<Add />} onClick={() => setEditing(null)}>New definition</Button>} />
      <Tabs value={tab} onChange={(_, v: number) => setParams(v === 1 ? { tab: 'stalled' } : {})} sx={{ mb: 2 }}>
        <Tab label="Definitions" />{canSeeStalled && <Tab label="Stalled requests" />}
      </Tabs>
      {tab === 1 ? <StalledRequests /> : <SectionCard title="Definitions">
        {q.isLoading ? <TableSkeleton columns={7} /> : q.error ? <ErrorAlert error={q.error} onRetry={() => void q.refetch()} /> :
          !q.data?.length ? <EmptyState title="No workflow definitions" hint="Create a draft, add steps and publish it." /> : (
            <Box sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead><TableRow>
                  <TableCell>Code</TableCell><TableCell>Version</TableCell><TableCell>Name</TableCell><TableCell>Document type</TableCell>
                  <TableCell>Steps</TableCell><TableCell>Effective</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
                </TableRow></TableHead>
                <TableBody>
                  {q.data.map(d => (
                    <TableRow key={d.id} hover>
                      <TableCell>{d.code}</TableCell><TableCell>v{d.version}</TableCell><TableCell>{d.name}</TableCell>
                      <TableCell>{d.documentType}</TableCell><TableCell>{d.steps.length}</TableCell>
                      <TableCell>{d.effectiveFrom ? `${d.effectiveFrom} → ${d.effectiveTo ?? 'open'}` : '—'}</TableCell>
                      <TableCell><StatusChip value={d.status} /></TableCell>
                      <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                        {canManage && d.status === 'draft' && <>
                          <Button size="small" startIcon={<Edit />} onClick={() => setEditing(d)}>Edit</Button>
                          <Button size="small" startIcon={<Publish />} onClick={() => publish.mutate(d.id)}>Publish</Button>
                          <Button size="small" color="error" startIcon={<Delete />} onClick={() => remove.mutate(d.id)}>Delete</Button>
                        </>}
                        {canManage && d.status === 'published' &&
                          <Button size="small" startIcon={<Archive />} onClick={() => retire.mutate(d.id)}>Retire</Button>}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          )}
      </SectionCard>}
      {editing !== undefined && <DefinitionDialog initial={editing} onClose={() => setEditing(undefined)} />}
    </Box>
  )
}
