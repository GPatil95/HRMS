import { CheckCircle, Sync } from '@mui/icons-material'
import { Alert, Box, Button, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, itemsOf } from '../../api/client'
import { platformApi } from '../../api/platform'
import { ReasonDialog } from '../../components/ReasonDialog'
import { useToast } from '../../components/toast'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton, TenantAvatar } from '../../components/ui'

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

  if (q.isPending) return <Box><PageHeader title="Provisioning & operations" /><SectionCard><TableSkeleton columns={4} /></SectionCard></Box>
  if (q.error) return <Box><PageHeader title="Provisioning & operations" /><ErrorAlert error={q.error} onRetry={() => void q.refetch()} /></Box>

  const all = itemsOf(q.data)
  const attention = all.filter((t) => t.status === 'Provisioning' || t.status === 'ProvisioningFailed')

  return (
    <Box>
      <PageHeader title="Provisioning & operations" subtitle="Auto-refreshes every 10 seconds" />
      <SectionCard>
        {attention.length === 0 ? (
          <EmptyState title="Nothing needs attention" hint="No tenants are provisioning or in a failed state." icon={<CheckCircle color="success" />} />
        ) : (
          <>
            <Alert severity="info" sx={{ borderRadius: 0, borderBottom: 1, borderColor: 'divider' }}>{attention.length} tenant(s) provisioning or failed</Alert>
            <Table size="small">
              <TableHead>
                <TableRow><TableCell>Tenant</TableCell><TableCell>Status</TableCell><TableCell>Host</TableCell><TableCell align="right">Actions</TableCell></TableRow>
              </TableHead>
              <TableBody>
                {attention.map((t) => (
                  <TableRow key={t.id} hover>
                    <TableCell>
                      <Box component={Link} to={`/admin/tenants/${t.id}`} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, color: 'inherit', textDecoration: 'none' }}>
                        <TenantAvatar code={t.code} name={t.name} size={32} />
                        <Box>
                          <Typography variant="body2" sx={{ fontWeight: 650 }}>{t.name}</Typography>
                          <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{t.code}</Typography>
                        </Box>
                      </Box>
                    </TableCell>
                    <TableCell><StatusChip value={t.status} /></TableCell>
                    <TableCell><Typography variant="body2" color="text.secondary" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{t.primaryHost ?? '—'}</Typography></TableCell>
                    <TableCell align="right">
                      {t.status === 'ProvisioningFailed'
                        ? <Button size="small" variant="outlined" startIcon={<Sync />} onClick={() => setRetryId(t.id)} disabled={retry.isPending}>Retry</Button>
                        : <Typography variant="caption" color="text.secondary">Running…</Typography>}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </>
        )}
      </SectionCard>
      <ReasonDialog open={retryId !== null} title="Retry provisioning" busy={retry.isPending}
        description="Re-runs the idempotent provisioning pipeline for this tenant."
        confirmLabel="Retry" onClose={() => setRetryId(null)} onConfirm={() => retryId && retry.mutate(retryId)} />
    </Box>
  )
}
