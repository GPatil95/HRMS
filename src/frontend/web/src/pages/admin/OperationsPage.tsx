import { CheckCircle, ContentCopy, ExpandMore, Refresh, Sync } from '@mui/icons-material'
import {
  Alert,
  AlertTitle,
  Box,
  Button,
  Collapse,
  Drawer,
  IconButton,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
  Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { platformApi, type ProvisioningOperation } from '../../api/platform'
import { ProvisioningTimeline, stepLabel } from '../../components/ProvisioningTimeline'
import { ReasonDialog } from '../../components/ReasonDialog'
import { useToast } from '../../components/toast'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton, TenantAvatar } from '../../components/ui'

function duration(o: ProvisioningOperation): string {
  if (!o.startedAt) return '—'
  const end = o.completedAt ? new Date(o.completedAt) : new Date()
  const s = Math.max(0, Math.round((end.getTime() - new Date(o.startedAt).getTime()) / 1000))
  return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${s % 60}s`
}

function OperationDrawer({ op, onClose, onRetry, busy }: { op: ProvisioningOperation; onClose: () => void; onRetry: () => void; busy: boolean }) {
  const [techOpen, setTechOpen] = useState(false)
  return (
    <Drawer anchor="right" open onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 460 }, display: 'flex', flexDirection: 'column' } } }}>
      <Box sx={{ p: 3, borderBottom: 1, borderColor: 'divider', display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 2 }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h6" sx={{ fontWeight: 600 }}>Provisioning operation</Typography>
          <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{op.id}</Typography>
        </Box>
        <StatusChip value={op.status} />
      </Box>
      <Box sx={{ p: 3, overflowY: 'auto', flex: 1 }}>
        <Box sx={{ display: 'grid', gap: 1, mb: 3 }}>
          <Typography variant="body2" color="text.secondary">Tenant</Typography>
          <Box component={Link} to={`/admin/tenants/${op.tenantId}`} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, color: 'inherit', textDecoration: 'none' }}>
            <TenantAvatar code={op.tenantCode ?? '?'} name={op.tenantName ?? ''} size={32} />
            <Box>
              <Typography variant="body2" sx={{ fontWeight: 600 }}>{op.tenantName ?? '—'}</Typography>
              <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{op.tenantCode}</Typography>
            </Box>
          </Box>
          <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5, mt: 1 }}>
            <Box><Typography variant="caption" color="text.secondary">Operation</Typography><Typography variant="body2">{op.operation}</Typography></Box>
            <Box><Typography variant="caption" color="text.secondary">Attempt</Typography><Typography variant="body2">{op.attempt || 1}</Typography></Box>
            <Box><Typography variant="caption" color="text.secondary">Current step</Typography><Typography variant="body2">{stepLabel(op.currentStep)}</Typography></Box>
            <Box><Typography variant="caption" color="text.secondary">Duration</Typography><Typography variant="body2">{duration(op)}</Typography></Box>
            <Box sx={{ gridColumn: '1 / -1' }}>
              <Typography variant="caption" color="text.secondary">Correlation ID</Typography>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12, wordBreak: 'break-all' }}>{op.correlationId ?? '—'}</Typography>
                {op.correlationId && (
                  <IconButton size="small" aria-label="Copy correlation ID" onClick={() => void navigator.clipboard.writeText(op.correlationId!)}>
                    <ContentCopy sx={{ fontSize: 13 }} />
                  </IconButton>
                )}
              </Box>
            </Box>
          </Box>
        </Box>

        {op.status === 'Failed' && (
          <Alert severity="error" sx={{ mb: 3 }}>
            <AlertTitle sx={{ fontWeight: 600 }}>Provisioning failed</AlertTitle>
            {op.currentStep ? <>The operation stopped while <strong>{stepLabel(op.currentStep).toLowerCase()}</strong>.</> : 'The operation could not be completed.'}
            {op.error && (
              <Box sx={{ mt: 1 }}>
                <Button size="small" variant="text" color="inherit" endIcon={<ExpandMore sx={{ transform: techOpen ? 'rotate(180deg)' : 'none' }} />} onClick={() => setTechOpen((v) => !v)}>
                  Technical details
                </Button>
                <Collapse in={techOpen}>
                  <Typography variant="caption" component="pre" sx={{ fontFamily: 'monospace', whiteSpace: 'pre-wrap', display: 'block', mt: 0.5, p: 1, borderRadius: 1, bgcolor: 'rgba(0,0,0,0.15)' }}>
                    {op.error}
                  </Typography>
                </Collapse>
              </Box>
            )}
          </Alert>
        )}

        <Typography variant="subtitle2" sx={{ mb: 1.5 }}>Timeline</Typography>
        <ProvisioningTimeline operation={op} />
      </Box>
      {op.status === 'Failed' && (
        <Box sx={{ p: 2, borderTop: 1, borderColor: 'divider' }}>
          <Button variant="contained" fullWidth startIcon={<Sync />} onClick={onRetry} disabled={busy}>Retry provisioning</Button>
        </Box>
      )}
    </Drawer>
  )
}

export function OperationsPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const [page, setPage] = useState(0)
  const [selected, setSelected] = useState<ProvisioningOperation | null>(null)
  const [retryId, setRetryId] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['operations', page], queryFn: () => platformApi.operations(page + 1, 50), refetchInterval: 10000 })
  const retry = useMutation({
    mutationFn: (tenantId: string) => platformApi.retryProvisioning(tenantId),
    onSuccess: () => { toast('Provisioning retry queued'); setRetryId(null); setSelected(null); void qc.invalidateQueries({ queryKey: ['operations'] }); void qc.invalidateQueries({ queryKey: ['tenants'] }) },
    onError: (e) => toast(e instanceof ApiError ? e.message : 'Retry failed', 'error'),
  })

  if (q.isPending) return <Box><PageHeader title="Provisioning & operations" /><SectionCard><TableSkeleton columns={6} /></SectionCard></Box>
  if (q.error) return <Box><PageHeader title="Provisioning & operations" /><ErrorAlert error={q.error} onRetry={() => void q.refetch()} /></Box>

  const items = q.data!.items

  return (
    <Box>
      <PageHeader title="Provisioning & operations" subtitle="Auto-refreshes every 10 seconds"
        actions={<Button size="small" variant="outlined" startIcon={<Refresh />} onClick={() => void q.refetch()}>Refresh</Button>} />
      <SectionCard>
        {items.length === 0 ? (
          <EmptyState title="No operations yet" hint="Provisioning operations appear here when a tenant is created." icon={<CheckCircle color="success" />} />
        ) : (
          <>
            <Box sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Tenant</TableCell><TableCell>Operation</TableCell><TableCell>Status</TableCell>
                    <TableCell>Current step</TableCell><TableCell>Started</TableCell><TableCell>Duration</TableCell>
                    <TableCell>Correlation</TableCell><TableCell align="right">Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {items.map((o) => (
                    <TableRow key={o.id} hover sx={{ cursor: 'pointer' }} onClick={() => setSelected(o)}>
                      <TableCell>
                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                          <TenantAvatar code={o.tenantCode ?? '?'} name={o.tenantName ?? ''} size={30} />
                          <Box>
                            <Typography variant="body2" sx={{ fontWeight: 600 }}>{o.tenantName ?? '—'}</Typography>
                            <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{o.tenantCode}</Typography>
                          </Box>
                        </Box>
                      </TableCell>
                      <TableCell><Typography variant="body2">{o.operation}</Typography></TableCell>
                      <TableCell><StatusChip value={o.status} /></TableCell>
                      <TableCell><Typography variant="body2" color="text.secondary">{stepLabel(o.currentStep)}</Typography></TableCell>
                      <TableCell><Typography variant="body2" color="text.secondary">{o.startedAt ? new Date(o.startedAt).toLocaleString() : '—'}</Typography></TableCell>
                      <TableCell><Typography variant="body2" color="text.secondary">{duration(o)}</Typography></TableCell>
                      <TableCell><Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{o.correlationId ? `${o.correlationId.slice(0, 8)}…` : '—'}</Typography></TableCell>
                      <TableCell align="right">
                        {o.status === 'Failed' && (
                          <Button size="small" variant="outlined" startIcon={<Sync />} disabled={retry.isPending}
                            onClick={(e) => { e.stopPropagation(); setRetryId(o.tenantId) }}>Retry</Button>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
            <TablePagination component="div" count={q.data!.totalCount} page={page} onPageChange={(_, p) => setPage(p)}
              rowsPerPage={50} rowsPerPageOptions={[50]} />
          </>
        )}
      </SectionCard>

      {selected && <OperationDrawer op={selected} onClose={() => setSelected(null)} onRetry={() => setRetryId(selected.tenantId)} busy={retry.isPending} />}
      <ReasonDialog open={retryId !== null} title="Retry provisioning" busy={retry.isPending}
        description="Re-runs the idempotent provisioning pipeline for this tenant."
        confirmLabel="Retry" onClose={() => setRetryId(null)} onConfirm={() => retryId && retry.mutate(retryId)} />
    </Box>
  )
}
