import { Box, Button, Paper, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useMutation, useQuery } from '@tanstack/react-query'
import { ApiError } from '../../api/client'
import { platformApi } from '../../api/platform'
import { useToast } from '../../components/toast'
import { CenteredSpinner, EmptyState, ErrorAlert, PageHeader, StatusChip } from '../../components/ui'

export function SupportAccessPage() {
  const toast = useToast()
  const q = useQuery({ queryKey: ['support-grants'], queryFn: platformApi.supportGrants })
  const act = useMutation({
    mutationFn: (p: { id: string; action: 'approve' | 'reject' | 'revoke' }) =>
      p.action === 'revoke' ? platformApi.revokeGrant(p.id) : platformApi.decideGrant(p.id, p.action === 'approve'),
    onSuccess: () => { toast('Updated'); void q.refetch() },
    onError: (e) => toast(e instanceof ApiError ? e.message : 'Action failed', 'error'),
  })

  if (q.isPending) return <CenteredSpinner />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => void q.refetch()} />

  return (
    <Box>
      <PageHeader title="Support access" subtitle="Time-limited, approval-based platform access to tenant data — every access is audited" />
      {(q.data ?? []).length === 0 ? <EmptyState title="No support grants" hint="Requests for tenant-data access appear here for approval." /> : (
        <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Ticket</TableCell><TableCell>Tenant</TableCell><TableCell>Level</TableCell><TableCell>Duration</TableCell>
                <TableCell>Status</TableCell><TableCell>Expires</TableCell><TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {q.data!.map((g) => (
                <TableRow key={g.id}>
                  <TableCell>{g.ticketRef || '—'}</TableCell>
                  <TableCell><Typography variant="caption" component="code">{g.tenantId.slice(0, 8)}…</Typography></TableCell>
                  <TableCell><StatusChip value={g.accessLevel} /></TableCell>
                  <TableCell>{g.durationMinutes}m</TableCell>
                  <TableCell><StatusChip value={g.status} /></TableCell>
                  <TableCell>{g.expiresAt ? new Date(g.expiresAt).toLocaleString() : '—'}</TableCell>
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
        </Paper>
      )}
    </Box>
  )
}
