import { ListAlt } from '@mui/icons-material'
import { Box, Button, MenuItem, Stack, TextField } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { tenantApi, type AuditQuery } from '../../api/tenant'
import { AuditTrailTable } from '../../components/AuditTrailTable'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, TableSkeleton } from '../../components/ui'

const ACTIONS = ['create', 'update', 'delete', 'download', 'export', 'approve', 'revoke', 'issue']

export function AuditPage() {
  const [draft, setDraft] = useState<AuditQuery>({ take: 100 })
  const [applied, setApplied] = useState<AuditQuery>({ take: 100 })

  const q = useQuery({ queryKey: ['tenant-audit', applied], queryFn: () => tenantApi.auditEntries(applied) })

  const set = (k: keyof AuditQuery) => (e: { target: { value: string } }) =>
    setDraft((d) => ({ ...d, [k]: e.target.value || undefined }))

  return (
    <Box>
      <PageHeader title="Audit log" subtitle="Field-level audit trail — expand a row to see old and new values" />
      <SectionCard sx={{ mb: 2, p: 2 }}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ flexWrap: 'wrap' }} useFlexGap>
          <TextField size="small" label="Module" value={draft.moduleCode ?? ''} onChange={set('moduleCode')} placeholder="hr.employee" sx={{ minWidth: 160 }} />
          <TextField size="small" label="Entity type" value={draft.entityType ?? ''} onChange={set('entityType')} placeholder="Employee" sx={{ minWidth: 160 }} />
          <TextField size="small" label="Record ID" value={draft.entityId ?? ''} onChange={set('entityId')} sx={{ minWidth: 200 }} />
          <TextField size="small" select label="Action" value={draft.action ?? ''} onChange={set('action')} sx={{ minWidth: 130 }}>
            <MenuItem value="">All</MenuItem>
            {ACTIONS.map((a) => <MenuItem key={a} value={a}>{a}</MenuItem>)}
          </TextField>
          <TextField size="small" type="date" label="From" value={draft.from ?? ''} onChange={set('from')} slotProps={{ inputLabel: { shrink: true } }} />
          <TextField size="small" type="date" label="To" value={draft.to ?? ''} onChange={set('to')} slotProps={{ inputLabel: { shrink: true } }} />
          <Button variant="contained" onClick={() => setApplied(draft)}>Apply</Button>
          <Button onClick={() => { const d = { take: 100 }; setDraft(d); setApplied(d) }}>Reset</Button>
        </Stack>
      </SectionCard>
      <SectionCard sx={{ overflowX: 'auto' }}>
        {q.isPending ? <TableSkeleton columns={6} />
          : q.error ? <Box sx={{ p: 3 }}><ErrorAlert error={q.error} onRetry={() => void q.refetch()} /></Box>
          : (q.data ?? []).length === 0 ? <EmptyState title="No audit entries" icon={<ListAlt />} />
          : <AuditTrailTable entries={q.data!} />}
      </SectionCard>
    </Box>
  )
}
