import { Alert, Box, Button, Paper, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ApiError, itemsOf } from '../../api/client'
import { platformApi } from '../../api/platform'
import { ReasonDialog } from '../../components/ReasonDialog'
import { useToast } from '../../components/toast'
import { CenteredSpinner, EmptyState, ErrorAlert, PageHeader, StatusChip } from '../../components/ui'
import { useState } from 'react'

export function OperationsPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const [retryId, setRetryId] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['tenants', 'ops'], queryFn: () => platformApi.tenants(1, 200), refetchInterval: 10000 })
  const retry = useMutation({
    mutationFn: (id: string) => platformApi.retryProvisioning(id),
    onSuccess: () => { toast('Provisioning retry queued'); setRetryId(null); void qc.invalidateQueries({ queryKey: ['tenants'] }) },
    onError: (e) => toast(e instanceof ApiError ? e.message : 'Retry failed', 'error'),
  })

  if (q.isPending) return <CenteredSpinner />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => void q.refetch()} />

  const all = itemsOf(q.data)
  const attention = all.filter((t) => t.status === 'Provisioning' || t.status === 'ProvisioningFailed')

  return (
    <Box>
      <PageHeader title="Provisioning & operations" subtitle="Auto-refreshes every 10 seconds" />
      {attention.length === 0 ? (
        <EmptyState title="Nothing needs attention" hint="No tenants are provisioning or in a failed state." />
      ) : (
        <Paper variant="outlined">
          <Alert severity="info" sx={{ borderRadius: 0 }}>{attention.length} tenant(s) provisioning or failed</Alert>
          <Table size="small">
            <TableHead>
              <TableRow><TableCell>Tenant</TableCell><TableCell>Status</TableCell><TableCell>Host</TableCell><TableCell align="right">Actions</TableCell></TableRow>
            </TableHead>
            <TableBody>
              {attention.map((t) => (
                <TableRow key={t.id}>
                  <TableCell><Link to={`/admin/tenants/${t.id}`}><strong>{t.code}</strong> — {t.name}</Link></TableCell>
                  <TableCell><StatusChip value={t.status} /></TableCell>
                  <TableCell>{t.primaryHost ?? '—'}</TableCell>
                  <TableCell align="right">
                    {t.status === 'ProvisioningFailed' && <Button size="small" variant="outlined" onClick={() => setRetryId(t.id)}>Retry</Button>}
                    <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>
                      {t.status === 'Provisioning' ? 'Running…' : ''}
                    </Typography>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
      <ReasonDialog open={retryId !== null} title="Retry provisioning"
        description="Re-runs the idempotent provisioning pipeline for this tenant."
        confirmLabel="Retry" onClose={() => setRetryId(null)} onConfirm={() => retryId && retry.mutate(retryId)} />
    </Box>
  )
}
