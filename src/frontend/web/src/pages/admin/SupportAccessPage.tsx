import { Shield } from '@mui/icons-material'
import { Box, Button, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useMutation, useQuery } from '@tanstack/react-query'
import { ApiError } from '../../api/client'
import { platformApi } from '../../api/platform'
import { useToast } from '../../components/toast'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'

export function SupportAccessPage() {
  const toast = useToast()
  const q = useQuery({ queryKey: ['support-grants'], queryFn: platformApi.supportGrants })
  const act = useMutation({
    mutationFn: (p: { id: string; action: 'approve' | 'reject' | 'revoke' }) =>
      p.action === 'revoke' ? platformApi.revokeGrant(p.id) : platformApi.decideGrant(p.id, p.action === 'approve'),
    onSuccess: () => { toast('Updated'); void q.refetch() },
    onError: (e) => toast(e instanceof ApiError ? e.message : 'Action failed', 'error'),
  })

  return (
    <Box>
      <PageHeader title="Support access" subtitle="Time-limited, approval-based platform access to tenant data — every access is audited" />
      <SectionCard sx={{ overflowX: 'auto' }}>
        {q.isPending ? <TableSkeleton columns={7} /> : q.error ? <Box sx={{ p: 3 }}><ErrorAlert error={q.error} onRetry={() => void q.refetch()} /></Box>
        : (q.data ?? []).length === 0 ? <EmptyState title="No support grants" hint="Requests for tenant-data access appear here for approval." icon={<Shield />} /> : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Ticket</TableCell><TableCell>Tenant</TableCell><TableCell>Level</TableCell><TableCell>Duration</TableCell>
                <TableCell>Status</TableCell><TableCell>Expires</TableCell><TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {q.data!.map((g) => (
                <TableRow key={g.id} hover>
                  <TableCell><Typography variant="body2" sx={{ fontWeight: 600 }}>{g.ticketRef || '—'}</Typography></TableCell>
                  <TableCell><Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12.5 }} color="text.secondary">{g.tenantId.slice(0, 8)}…</Typography></TableCell>
                  <TableCell><StatusChip value={g.accessLevel} /></TableCell>
                  <TableCell>{g.durationMinutes}m</TableCell>
                  <TableCell><StatusChip value={g.status} /></TableCell>
                  <TableCell><Typography variant="body2" color="text.secondary">{g.expiresAt ? new Date(g.expiresAt).toLocaleString() : '—'}</Typography></TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                    {g.status === 'Requested' && (
                      <>
                        <Button size="small" onClick={() => act.mutate({ id: g.id, action: 'approve' })} disabled={act.isPending}>Approve</Button>
                        <Button size="small" color="error" onClick={() => act.mutate({ id: g.id, action: 'reject' })} disabled={act.isPending}>Reject</Button>
                      </>
                    )}
                    {g.status === 'Approved' && <Button size="small" color="warning" onClick={() => act.mutate({ id: g.id, action: 'revoke' })} disabled={act.isPending}>Revoke</Button>}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>
    </Box>
  )
}
