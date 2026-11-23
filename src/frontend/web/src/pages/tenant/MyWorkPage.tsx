import { useState } from 'react'
import {
  Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, Switch,
  Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { Check, Close, History, PanTool, Reply, Send } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { workflowApi, type WorkItem, type WorkItemFilter, type WorkflowInstance } from '../../api/workflow'
import { tenantApi } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'

const fmt = (d?: string | null) => (d ? new Date(d).toLocaleString() : '—')
type ActionKind = 'approve' | 'reject' | 'send_back' | 'delegate'

function HistoryDialog({ instanceId, onClose }: { instanceId: string; onClose: () => void }) {
  const inst = useQuery({ queryKey: ['wf-instance', instanceId], queryFn: () => workflowApi.instance(instanceId) })
  const hist = useQuery({ queryKey: ['wf-history', instanceId], queryFn: () => workflowApi.history(instanceId) })
  return (
    <Dialog open onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>{inst.data?.title ?? 'Workflow history'}</DialogTitle>
      <DialogContent>
        {inst.data && (
          <Stack direction="row" spacing={1} sx={{ mb: 2, flexWrap: "wrap" }}>
            <StatusChip value={inst.data.status} />
            <Chip size="small" label={`${inst.data.definitionCode} v${inst.data.definitionVersion}`} />
            <Chip size="small" label={`Requested by ${inst.data.requesterName ?? '—'}`} />
          </Stack>
        )}
        {inst.data && (
          <Table size="small" sx={{ mb: 2 }}>
            <TableHead><TableRow><TableCell>Stage</TableCell><TableCell>Step</TableCell><TableCell>Assignee</TableCell><TableCell>Status</TableCell><TableCell>Due</TableCell></TableRow></TableHead>
            <TableBody>
              {inst.data.tasks.map(t => (
                <TableRow key={t.id}>
                  <TableCell>{t.stepOrder}</TableCell><TableCell>{t.name}</TableCell><TableCell>{t.assigneeName ?? t.assigneeUserId}</TableCell>
                  <TableCell><StatusChip value={t.status} /></TableCell><TableCell>{fmt(t.dueAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        {hist.isLoading ? <TableSkeleton columns={4} rows={4} /> : hist.error ? <ErrorAlert error={hist.error} /> : (
          <Table size="small">
            <TableHead><TableRow><TableCell>When</TableCell><TableCell>Action</TableCell><TableCell>By</TableCell><TableCell>Comment</TableCell></TableRow></TableHead>
            <TableBody>
              {(hist.data ?? []).map(h => (
                <TableRow key={h.id}>
                  <TableCell>{fmt(h.occurredAt)}</TableCell>
                  <TableCell>{h.action}{h.targetName ? ` → ${h.targetName}` : ''}</TableCell>
                  <TableCell>{h.actorName ?? 'System'}</TableCell>
                  <TableCell>{h.comment ?? ''}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Close</Button></DialogActions>
    </Dialog>
  )
}

function ActionDialog({ item, kind, onClose }: { item: WorkItem; kind: ActionKind; onClose: () => void }) {
  const toast = useToast(); const qc = useQueryClient()
  const [comment, setComment] = useState('')
  const [sendBackTo, setSendBackTo] = useState('requester')
  const [toUserId, setToUserId] = useState('')
  const users = useQuery({ queryKey: ['users'], queryFn: tenantApi.users, enabled: kind === 'delegate', retry: false })
  const needsComment = kind === 'reject' || kind === 'send_back'
  const run = useMutation({
    mutationFn: () => kind === 'delegate'
      ? workflowApi.delegate(item.id, toUserId, comment || undefined)
      : workflowApi.complete(item.id, kind, comment || undefined, kind === 'send_back' ? sendBackTo : undefined),
    onSuccess: (r: WorkflowInstance) => {
      toast(`Done — request is now ${r.status.replace('_', ' ')}`)
      void qc.invalidateQueries({ queryKey: ['my-work'] }); onClose()
    },
    onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  const title = { approve: 'Approve', reject: 'Reject', send_back: 'Send back', delegate: 'Delegate' }[kind]
  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{title}: {item.title}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: 2 }}>
        {kind === 'delegate' && (users.data ? (
          <TextField select size="small" label="Delegate to" value={toUserId} onChange={e => setToUserId(e.target.value)}>
            {users.data.filter(u => u.status === 'Active').map(u => <MenuItem key={u.id} value={u.id}>{u.displayName} ({u.email})</MenuItem>)}
          </TextField>
        ) : (
          <TextField size="small" label="Delegate user ID" value={toUserId} onChange={e => setToUserId(e.target.value)} />
        ))}
        {kind === 'send_back' && (
          <TextField select size="small" label="Send back to" value={sendBackTo} onChange={e => setSendBackTo(e.target.value)}>
            <MenuItem value="requester">Requester</MenuItem>
            <MenuItem value="previous">Previous stage</MenuItem>
          </TextField>
        )}
        <TextField size="small" multiline minRows={3} label={needsComment ? 'Comment (required)' : 'Comment'} value={comment} onChange={e => setComment(e.target.value)} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" color={kind === 'reject' ? 'error' : 'primary'} disabled={run.isPending || (needsComment && !comment.trim()) || (kind === 'delegate' && !toUserId)}
          onClick={() => run.mutate()}>{title}</Button>
      </DialogActions>
    </Dialog>
  )
}

function InboxTab() {
  const toast = useToast(); const qc = useQueryClient()
  const [filter, setFilter] = useState<WorkItemFilter>({ status: 'open' })
  const [action, setAction] = useState<{ item: WorkItem; kind: ActionKind } | null>(null)
  const [history, setHistory] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['my-work', filter], queryFn: () => workflowApi.myItems(filter) })
  const claim = useMutation({
    mutationFn: (id: string) => workflowApi.claim(id),
    onSuccess: () => { toast('Claimed'); void qc.invalidateQueries({ queryKey: ['my-work'] }) },
    onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  const modules = [...new Set((q.data?.items ?? []).map(i => i.moduleCode))]
  return (
    <SectionCard title="Inbox" subtitle="Approvals and tasks assigned to you across all modules">
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 2 }}>
        <TextField select size="small" label="Status" value={filter.status ?? 'open'} onChange={e => setFilter(f => ({ ...f, status: e.target.value }))} sx={{ minWidth: 140 }}>
          {['open', 'completed', 'cancelled', 'all'].map(s => <MenuItem key={s} value={s}>{s}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="Module" value={filter.moduleCode ?? ''} onChange={e => setFilter(f => ({ ...f, moduleCode: e.target.value || undefined }))} sx={{ minWidth: 160 }}>
          <MenuItem value="">All modules</MenuItem>
          {modules.map(m => <MenuItem key={m} value={m}>{m}</MenuItem>)}
        </TextField>
        <TextField size="small" label="Search" value={filter.search ?? ''} onChange={e => setFilter(f => ({ ...f, search: e.target.value }))} />
        <FormControlLabel control={<Switch checked={!!filter.overdueOnly} onChange={e => setFilter(f => ({ ...f, overdueOnly: e.target.checked }))} />} label="Overdue only" />
      </Stack>
      {q.isLoading ? <TableSkeleton columns={6} /> : q.error ? <ErrorAlert error={q.error} onRetry={() => void q.refetch()} /> :
        !q.data?.items.length ? <EmptyState title="Nothing waiting on you" hint="New approvals will appear here." /> : (
          <Box sx={{ overflowX: 'auto' }}>
            <Table size="small">
              <TableHead><TableRow><TableCell>Title</TableCell><TableCell>Module</TableCell><TableCell>Received</TableCell><TableCell>Due</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell></TableRow></TableHead>
              <TableBody>
                {q.data.items.map(i => {
                  const actionable = i.status === 'open' && i.itemType === 'workflow.approval' && (!i.claimed || i.claimedByMe)
                  return (
                    <TableRow key={i.id} hover>
                      <TableCell>
                        <Typography variant="body2" sx={{ fontWeight: 600 }}>{i.title}</Typography>
                        {i.claimed && <Chip size="small" label={i.claimedByMe ? 'Claimed by you' : 'Claimed by another approver'} sx={{ mt: 0.5 }} />}
                      </TableCell>
                      <TableCell>{i.moduleCode}</TableCell>
                      <TableCell>{fmt(i.createdAt)}</TableCell>
                      <TableCell>{i.overdue ? <Chip size="small" color="error" label={fmt(i.dueAt)} /> : fmt(i.dueAt)}</TableCell>
                      <TableCell><StatusChip value={i.status} /></TableCell>
                      <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                        {i.workflowInstanceId && <Button size="small" startIcon={<History />} onClick={() => setHistory(i.workflowInstanceId!)}>History</Button>}
                        {actionable && !i.claimed && <Button size="small" startIcon={<PanTool />} onClick={() => claim.mutate(i.id)}>Claim</Button>}
                        {actionable && <>
                          <Button size="small" color="success" startIcon={<Check />} onClick={() => setAction({ item: i, kind: 'approve' })}>Approve</Button>
                          <Button size="small" color="error" startIcon={<Close />} onClick={() => setAction({ item: i, kind: 'reject' })}>Reject</Button>
                          <Button size="small" startIcon={<Reply />} onClick={() => setAction({ item: i, kind: 'send_back' })}>Send back</Button>
                          <Button size="small" startIcon={<Send />} onClick={() => setAction({ item: i, kind: 'delegate' })}>Delegate</Button>
                        </>}
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </Box>
        )}
      {action && <ActionDialog item={action.item} kind={action.kind} onClose={() => setAction(null)} />}
      {history && <HistoryDialog instanceId={history} onClose={() => setHistory(null)} />}
    </SectionCard>
  )
}

function MyRequestsTab() {
  const toast = useToast(); const qc = useQueryClient()
  const [history, setHistory] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['my-requests'], queryFn: workflowApi.myRequests })
  const act = useMutation({
    mutationFn: ({ id, kind }: { id: string; kind: 'cancel' | 'resubmit' }) => kind === 'cancel' ? workflowApi.cancel(id) : workflowApi.resubmit(id),
    onSuccess: () => { toast('Updated'); void qc.invalidateQueries({ queryKey: ['my-requests'] }); void qc.invalidateQueries({ queryKey: ['my-work'] }) },
    onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  return (
    <SectionCard title="My requests" subtitle="Workflows you started">
      {q.isLoading ? <TableSkeleton columns={5} /> : q.error ? <ErrorAlert error={q.error} /> : !q.data?.items.length ? <EmptyState title="No requests yet" /> : (
        <Table size="small">
          <TableHead><TableRow><TableCell>Title</TableCell><TableCell>Type</TableCell><TableCell>Started</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell></TableRow></TableHead>
          <TableBody>
            {q.data.items.map(r => (
              <TableRow key={r.id} hover>
                <TableCell>{r.title}</TableCell><TableCell>{r.documentType}</TableCell><TableCell>{fmt(r.startedAt)}</TableCell>
                <TableCell><StatusChip value={r.status} /></TableCell>
                <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                  <Button size="small" startIcon={<History />} onClick={() => setHistory(r.id)}>History</Button>
                  {r.status === 'sent_back' && <Button size="small" onClick={() => act.mutate({ id: r.id, kind: 'resubmit' })}>Resubmit</Button>}
                  {(r.status === 'in_progress' || r.status === 'sent_back') && <Button size="small" color="error" onClick={() => act.mutate({ id: r.id, kind: 'cancel' })}>Withdraw</Button>}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {history && <HistoryDialog instanceId={history} onClose={() => setHistory(null)} />}
    </SectionCard>
  )
}

export function MyWorkPage() {
  const [tab, setTab] = useState(0)
  return (
    <Box>
      <PageHeader title="My Work" subtitle="Unified approvals inbox" />
      <Tabs value={tab} onChange={(_, v: number) => setTab(v)} sx={{ mb: 2 }}>
        <Tab label="Inbox" /><Tab label="My requests" />
      </Tabs>
      {tab === 0 ? <InboxTab /> : <MyRequestsTab />}
    </Box>
  )
}
