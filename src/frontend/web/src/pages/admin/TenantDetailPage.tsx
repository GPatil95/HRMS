import { Alert, Box, Button, MenuItem, Paper, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { platformApi, type TenantStatus } from '../../api/platform'
import { ReasonDialog } from '../../components/ReasonDialog'
import { useToast } from '../../components/toast'
import { CenteredSpinner, EmptyState, ErrorAlert, PageHeader, StatusChip } from '../../components/ui'

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">{label}</Typography>
      <Typography variant="body2">{children}</Typography>
    </Box>
  )
}

export function TenantDetailPage() {
  const { id = '' } = useParams()
  const qc = useQueryClient()
  const toast = useToast()
  const q = useQuery({ queryKey: ['tenant', id], queryFn: () => platformApi.tenant(id), refetchInterval: (qq) =>
    ['Provisioning', 'Pending', 'Running'].includes(qq.state.data?.provisioningStatus ?? '') || qq.state.data?.tenant.status === 'Provisioning' ? 5000 : false })
  const [dialog, setDialog] = useState<null | 'suspend' | 'activate' | 'retry' | 'subscription' | { module: string; enabled: boolean }>(null)
  const [subStatus, setSubStatus] = useState('Active')
  const [subEndsOn, setSubEndsOn] = useState('')

  const done = (msg: string) => { toast(msg); void q.refetch(); void qc.invalidateQueries({ queryKey: ['tenants'] }) }
  const fail = (e: unknown) => toast(e instanceof ApiError ? e.message : 'Action failed', 'error')

  const statusMut = useMutation({
    mutationFn: (p: { status: TenantStatus; reason: string }) => platformApi.setTenantStatus(id, p.status, p.reason),
    onSuccess: () => done('Tenant status updated'), onError: fail, onSettled: () => setDialog(null),
  })
  const retryMut = useMutation({
    mutationFn: () => platformApi.retryProvisioning(id),
    onSuccess: () => done('Provisioning retry queued'), onError: fail, onSettled: () => setDialog(null),
  })
  const moduleMut = useMutation({
    mutationFn: (p: { module: string; enabled: boolean; reason: string }) => platformApi.setModule(id, p.module, p.enabled, p.reason),
    onSuccess: () => done('Module entitlement updated'), onError: fail, onSettled: () => setDialog(null),
  })
  const subMut = useMutation({
    mutationFn: (p: { status: string; endsOn: string | null; reason: string }) => platformApi.setSubscription(id, p.status, p.endsOn, p.reason),
    onSuccess: () => done('Subscription updated'), onError: fail, onSettled: () => setDialog(null),
  })

  if (q.isPending) return <CenteredSpinner />
  if (q.error) return <ErrorAlert error={q.error} />
  const d = q.data!
  const t = d.tenant
  const provFailed = d.provisioningStatus === 'Failed' || t.status === 'ProvisioningFailed'
  const busy = statusMut.isPending || retryMut.isPending || moduleMut.isPending || subMut.isPending

  return (
    <Box>
      <PageHeader
        title={`${t.code} — ${t.name}`}
        subtitle={t.primaryHost ?? undefined}
        actions={
          <>
            <Button component={Link} to="/admin/tenants" variant="outlined">All tenants</Button>
            {provFailed && <Button variant="contained" color="warning" onClick={() => setDialog('retry')}>Retry provisioning</Button>}
            {t.status === 'Active'
              ? <Button variant="outlined" color="warning" onClick={() => setDialog('suspend')}>Suspend</Button>
              : <Button variant="outlined" color="success" onClick={() => setDialog('activate')}>Activate</Button>}
          </>
        }
      />
      {provFailed && d.provisioningError && <Alert severity="error" sx={{ mb: 2 }}>Provisioning failed: {d.provisioningError}</Alert>}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2, mb: 2 }}>
        <Paper variant="outlined" sx={{ p: 2.5, display: 'grid', gap: 1.5 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>Tenant</Typography>
          <Field label="Status"><StatusChip value={t.status} /></Field>
          <Field label="Tenant number">{t.tenantNumber}</Field>
          <Field label="Database">{d.databaseName ?? '—'} <StatusChip value={d.databaseStatus} /></Field>
          <Field label="Schema version">{d.schemaVersion ?? '—'}</Field>
          <Field label="Created">{new Date(t.createdAt).toLocaleString()}</Field>
        </Paper>
        <Paper variant="outlined" sx={{ p: 2.5, display: 'grid', gap: 1.5 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>Subscription & provisioning</Typography>
          <Field label="Plan">{d.planCode ?? '—'}</Field>
          <Field label="Subscription"><StatusChip value={d.subscriptionStatus} /></Field>
          <Field label="Provisioning"><StatusChip value={d.provisioningStatus} /></Field>
          <Button size="small" variant="outlined" onClick={() => { setSubStatus(d.subscriptionStatus ?? 'Active'); setDialog('subscription') }} sx={{ justifySelf: 'start' }}>
            Change subscription
          </Button>
        </Paper>
      </Box>

      <Paper variant="outlined" sx={{ p: 2.5 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 600 }} gutterBottom>Module entitlements</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          Core modules are always on. Optional modules can be purchased and enabled per tenant.
        </Typography>
        {d.modules.length === 0 ? <EmptyState title="No modules in catalogue" /> : (
          <Table size="small">
            <TableHead>
              <TableRow><TableCell>Module</TableCell><TableCell>Kind</TableCell><TableCell>Source</TableCell><TableCell>Valid to</TableCell><TableCell align="right">Enabled</TableCell></TableRow>
            </TableHead>
            <TableBody>
              {d.modules.map((m) => (
                <TableRow key={m.code}>
                  <TableCell><strong>{m.name}</strong><Typography variant="caption" color="text.secondary" sx={{ display: "block" }}>{m.code}</Typography></TableCell>
                  <TableCell><StatusChip value={m.kind} /></TableCell>
                  <TableCell>{m.source ?? '—'}</TableCell>
                  <TableCell>{m.validTo ?? '—'}</TableCell>
                  <TableCell align="right">
                    <Switch
                      size="small" checked={m.enabled} disabled={m.kind === 'Core' || busy}
                      onChange={(e) => setDialog({ module: m.code, enabled: e.target.checked })}
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Paper>

      <ReasonDialog open={dialog === 'suspend'} title="Suspend tenant"
        description={`Suspending ${t.code} blocks all tenant users from signing in.`}
        confirmLabel="Suspend" onClose={() => setDialog(null)} onConfirm={(r) => statusMut.mutate({ status: 'Suspended', reason: r })} />
      <ReasonDialog open={dialog === 'activate'} title="Activate tenant"
        description={`Activating ${t.code} restores tenant access.`}
        confirmLabel="Activate" onClose={() => setDialog(null)} onConfirm={(r) => statusMut.mutate({ status: 'Active', reason: r })} />
      <ReasonDialog open={dialog === 'retry'} title="Retry provisioning"
        description="Re-runs the idempotent provisioning pipeline for this tenant."
        confirmLabel="Retry" onClose={() => setDialog(null)} onConfirm={() => retryMut.mutate()} />
      <ReasonDialog open={dialog === 'subscription'} title="Change subscription"
        description="Updates the tenant's subscription status."
        confirmLabel="Save" onClose={() => setDialog(null)}
        extra={
          <Box sx={{ display: 'grid', gap: 2 }}>
            <TextField select label="Status" value={subStatus} onChange={(e) => setSubStatus(e.target.value)} fullWidth>
              {['Trial', 'Active', 'PastDue', 'Grace', 'Suspended'].map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
            </TextField>
            <TextField label="Ends on (optional)" type="date" value={subEndsOn} onChange={(e) => setSubEndsOn(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} fullWidth />
          </Box>
        }
        onConfirm={(r) => subMut.mutate({ status: subStatus, endsOn: subEndsOn || null, reason: r })} />
      {typeof dialog === 'object' && dialog !== null && (
        <ReasonDialog open title={`${dialog.enabled ? 'Enable' : 'Disable'} module`}
          description={`${dialog.enabled ? 'Enable' : 'Disable'} "${dialog.module}" for ${t.code}. Disabling takes effect immediately for all tenant users.`}
          confirmLabel={dialog.enabled ? 'Enable' : 'Disable'} onClose={() => setDialog(null)}
          onConfirm={(r) => moduleMut.mutate({ module: dialog.module, enabled: dialog.enabled, reason: r })} />
      )}
    </Box>
  )
}
