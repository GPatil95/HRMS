import { useState } from 'react'
import {
  Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, MenuItem,
  Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import { Add, Delete, Edit, Publish, Archive } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { workflowApi, type WorkflowDefinition, type WorkflowDefinitionInput, type WorkflowStep } from '../../api/workflow'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'
import { usePermissions } from './OrgShared'

const approverTypes = [
  ['reporting_manager', 'Reporting manager'], ['department_head', 'Department head'],
  ['department_field', 'Department approver field'], ['role', 'Role'], ['user', 'Specific user'],
] as const
const escalationTypes = [['none', 'None'], ['assignee_manager', "Assignee's manager"], ['role', 'Role'], ['user', 'User']] as const
const paramHint: Record<string, string> = {
  department_field: 'Department field (e.g. hr_partner_user_id)', role: 'Role code', user: 'User ID',
}

const blankStep = (order: number): WorkflowStep => ({
  stepOrder: order, name: `Stage ${order}`, approverType: 'reporting_manager', approverParam: null, approvalMode: 'any',
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
              <TableCell>Stage</TableCell><TableCell>Name</TableCell><TableCell>Approver</TableCell><TableCell>Parameter</TableCell>
              <TableCell>Mode</TableCell><TableCell>Amount range</TableCell><TableCell>SLA h</TableCell><TableCell>Escalation</TableCell>
              <TableCell>Options</TableCell><TableCell />
            </TableRow></TableHead>
            <TableBody>
              {form.steps.map((s, i) => (
                <TableRow key={i}>
                  <TableCell><TextField size="small" type="number" value={s.stepOrder} onChange={e => setStep(i, { stepOrder: Number(e.target.value) })} sx={{ width: 70 }} /></TableCell>
                  <TableCell><TextField size="small" value={s.name} onChange={e => setStep(i, { name: e.target.value })} /></TableCell>
                  <TableCell>
                    <TextField select size="small" value={s.approverType} onChange={e => setStep(i, { approverType: e.target.value })} sx={{ minWidth: 170 }}>
                      {approverTypes.map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}
                    </TextField>
                  </TableCell>
                  <TableCell><TextField size="small" placeholder={paramHint[s.approverType] ?? '—'} disabled={!paramHint[s.approverType]} value={s.approverParam ?? ''} onChange={e => setStep(i, { approverParam: e.target.value || null })} /></TableCell>
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

export function WorkflowDefinitionsPage() {
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
      <SectionCard title="Definitions">
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
      </SectionCard>
      {editing !== undefined && <DefinitionDialog initial={editing} onClose={() => setEditing(undefined)} />}
    </Box>
  )
}
