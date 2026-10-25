import { ArrowBack, ContentCopy, Key, PauseCircle, PlayCircle, Sync, Visibility, VisibilityOff } from '@mui/icons-material'
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  IconButton,
  InputAdornment,
  MenuItem,
  Paper,
  Radio,
  RadioGroup,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { platformApi, type TenantStatus } from '../../api/platform'
import { ReasonDialog } from '../../components/ReasonDialog'
import { useToast } from '../../components/toast'
import { CenteredSpinner, EmptyState, ErrorAlert, SectionCard, StatusChip, TenantAvatar } from '../../components/ui'

function generatePassword(): string {
  const letters = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ'
  const all = letters + '23456789'
  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)
  const chars = Array.from(bytes, (b) => all[b % all.length])
  chars[0] = letters[bytes[0] % letters.length]
  chars[15] = String(2 + (bytes[15] % 8))
  return chars.join('')
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 2, py: 0.75, borderBottom: 1, borderColor: 'divider', '&:last-of-type': { borderBottom: 0 } }}>
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, textAlign: 'right' }}>
        <Typography variant="body2" component="span" sx={{ fontFamily: 'inherit' }}>{children}</Typography>
      </Box>
    </Box>
  )
}

export function TenantDetailPage() {
  const { id = '' } = useParams()
  const qc = useQueryClient()
  const toast = useToast()
  const q = useQuery({
    queryKey: ['tenant', id], queryFn: () => platformApi.tenant(id),
    refetchInterval: (qq) =>
      ['Provisioning', 'Pending', 'Running'].includes(qq.state.data?.provisioningStatus ?? '') || qq.state.data?.tenant.status === 'Provisioning' ? 5000 : false,
  })
  const [dialog, setDialog] = useState<null | 'suspend' | 'activate' | 'retry' | 'subscription' | 'adminPw' | { module: string; enabled: boolean }>(null)
  const [subStatus, setSubStatus] = useState('Active')
  const [subEndsOn, setSubEndsOn] = useState('')
  const [pwMode, setPwMode] = useState<'email' | 'set'>('email')
  const [pwValue, setPwValue] = useState('')
  const [pwShow, setPwShow] = useState(false)
  const [pwDone, setPwDone] = useState<null | { password?: string }>(null)

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
  const pwMut = useMutation({
    mutationFn: () => platformApi.resetAdminPassword(id, pwMode === 'set' ? pwValue : undefined),
    onSuccess: () => setPwDone(pwMode === 'set' ? { password: pwValue } : {}),
    onError: fail,
  })

  if (q.isPending) return <CenteredSpinner />
  if (q.error) return <ErrorAlert error={q.error} />
  const d = q.data!
  const t = d.tenant
  const provFailed = d.provisioningStatus === 'Failed' || t.status === 'ProvisioningFailed'
  const busy = statusMut.isPending || retryMut.isPending || moduleMut.isPending || subMut.isPending

  return (
    <Box>
      <Button component={Link} to="/admin/tenants" startIcon={<ArrowBack />} size="small" sx={{ mb: 1.5, color: 'text.secondary' }}>
        All tenants
      </Button>
      <Paper variant="outlined" sx={{ p: 3, mb: 2.5, display: 'flex', flexWrap: 'wrap', gap: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, minWidth: 0 }}>
          <TenantAvatar code={t.code} name={t.name} size={52} />
          <Box sx={{ minWidth: 0 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, flexWrap: 'wrap' }}>
              <Typography variant="h5" sx={{ fontWeight: 700 }}>{t.name}</Typography>
              <StatusChip value={t.status} />
            </Box>
            <Typography variant="body2" color="text.secondary" sx={{ fontFamily: 'monospace', fontSize: 13 }}>
              {t.code}{t.primaryHost ? ` · ${t.primaryHost}` : ''} · #{t.tenantNumber}
            </Typography>
          </Box>
        </Box>
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
          {provFailed && <Button variant="contained" color="warning" startIcon={<Sync />} onClick={() => setDialog('retry')} disabled={busy}>Retry provisioning</Button>}
          <Button variant="outlined" startIcon={<Key />} onClick={() => { setPwDone(null); setPwMode('email'); setPwValue(''); setDialog('adminPw') }} disabled={busy}>Reset admin password</Button>
          {t.status === 'Active'
            ? <Button variant="outlined" color="error" startIcon={<PauseCircle />} onClick={() => setDialog('suspend')} disabled={busy}>Suspend</Button>
            : <Button variant="outlined" color="success" startIcon={<PlayCircle />} onClick={() => setDialog('activate')} disabled={busy}>Activate</Button>}
        </Box>
      </Paper>

      {provFailed && d.provisioningError && <Alert severity="error" sx={{ mb: 2.5 }}>Provisioning failed: {d.provisioningError}</Alert>}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2.5, mb: 2.5 }}>
        <SectionCard title="Tenant" subtitle="Identity and database">
          <Box sx={{ px: 3, pb: 1.5 }}>
            <Field label="Status"><StatusChip value={t.status} /></Field>
            <Field label="Tenant number">{t.tenantNumber}</Field>
            <Field label="Admin">
              <Typography component="span" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>
                {d.adminName ?? '—'}{d.adminEmail ? ` · ${d.adminEmail}` : ''}
              </Typography>
            </Field>
            <Field label="Database">
              <Typography component="span" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{d.databaseName ?? '—'}</Typography>
              <StatusChip value={d.databaseStatus} />
            </Field>
            <Field label="Schema version">{d.schemaVersion ?? '—'}</Field>
            <Field label="Created">{new Date(t.createdAt).toLocaleString()}</Field>
          </Box>
        </SectionCard>
        <SectionCard title="Subscription & provisioning" subtitle="Plan and billing state"
          actions={<Button size="small" variant="outlined" onClick={() => { setSubStatus(d.subscriptionStatus ?? 'Active'); setDialog('subscription') }}>Change</Button>}>
          <Box sx={{ px: 3, pb: 1.5 }}>
            <Field label="Plan"><Typography component="span" variant="body2" sx={{ fontFamily: 'monospace' }}>{d.planCode ?? 'Platform default'}</Typography></Field>
            <Field label="Subscription"><StatusChip value={d.subscriptionStatus} /></Field>
            <Field label="Provisioning"><StatusChip value={d.provisioningStatus} /></Field>
          </Box>
        </SectionCard>
      </Box>

      <SectionCard title="Module entitlements" subtitle="Core modules are always on. Optional modules can be purchased and enabled per tenant.">
        {d.modules.length === 0 ? <EmptyState title="No modules in catalogue" /> : (
          <Table size="small">
            <TableHead>
              <TableRow><TableCell>Module</TableCell><TableCell>Kind</TableCell><TableCell>Source</TableCell><TableCell>Valid to</TableCell><TableCell align="right">Enabled</TableCell></TableRow>
            </TableHead>
            <TableBody>
              {d.modules.map((m) => (
                <TableRow key={m.code} hover>
                  <TableCell>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>{m.name}</Typography>
                    <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{m.code}</Typography>
                  </TableCell>
                  <TableCell><StatusChip value={m.kind} /></TableCell>
                  <TableCell><Typography variant="body2" color="text.secondary">{m.source ?? '—'}</Typography></TableCell>
                  <TableCell><Typography variant="body2" color="text.secondary">{m.validTo ?? '—'}</Typography></TableCell>
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
      </SectionCard>

      <ReasonDialog open={dialog === 'suspend'} title="Suspend tenant" danger busy={statusMut.isPending}
        description={`Suspending ${t.code} blocks every user in this tenant from signing in and using the product. This is recorded in the platform audit log.`}
        confirmLabel="Suspend tenant" onClose={() => setDialog(null)} onConfirm={(r) => statusMut.mutate({ status: 'Suspended', reason: r })} />
      <ReasonDialog open={dialog === 'activate'} title="Activate tenant" busy={statusMut.isPending}
        description={`Activating ${t.code} restores tenant access.`}
        confirmLabel="Activate" onClose={() => setDialog(null)} onConfirm={(r) => statusMut.mutate({ status: 'Active', reason: r })} />
      <ReasonDialog open={dialog === 'retry'} title="Retry provisioning" busy={retryMut.isPending}
        description="Re-runs the idempotent provisioning pipeline for this tenant."
        confirmLabel="Retry" onClose={() => setDialog(null)} onConfirm={() => retryMut.mutate()} />
      <ReasonDialog open={dialog === 'subscription'} title="Change subscription" busy={subMut.isPending}
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
        <ReasonDialog open title={`${dialog.enabled ? 'Enable' : 'Disable'} module`} danger={!dialog.enabled} busy={moduleMut.isPending}
          description={`${dialog.enabled ? 'Enable' : 'Disable'} "${dialog.module}" for ${t.code}. Disabling takes effect immediately for all tenant users.`}
          confirmLabel={dialog.enabled ? 'Enable' : 'Disable'} onClose={() => setDialog(null)}
          onConfirm={(r) => moduleMut.mutate({ module: dialog.module, enabled: dialog.enabled, reason: r })} />
      )}
      <Dialog open={dialog === 'adminPw'} onClose={() => setDialog(null)} maxWidth="xs" fullWidth>
        <DialogTitle>Reset admin password</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2 }}>
          {pwDone ? (
            <>
              <Alert severity="success">
                {pwDone.password ? 'Password updated — all admin sessions were signed out.' : `Reset email sent to ${d.adminEmail}.`}
              </Alert>
              {pwDone.password && (
                <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
                  <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>{pwDone.password}</Typography>
                  <IconButton size="small" onClick={() => void navigator.clipboard.writeText(pwDone.password!)}><ContentCopy fontSize="small" /></IconButton>
                </Box>
              )}
              {pwDone.password && <Alert severity="warning" variant="outlined">Shown only here — it is not stored anywhere.</Alert>}
            </>
          ) : (
            <>
              <Typography variant="body2" color="text.secondary">
                Tenant admin: <Box component="span" sx={{ fontFamily: 'monospace' }}>{d.adminEmail ?? '—'}</Box>
              </Typography>
              <RadioGroup value={pwMode} onChange={(e) => setPwMode(e.target.value as 'email' | 'set')}>
                <FormControlLabel value="email" control={<Radio size="small" />} label="Email a password-reset link" />
                <FormControlLabel value="set" control={<Radio size="small" />} label="Set a temporary password now" />
              </RadioGroup>
              {pwMode === 'set' && (
                <TextField label="New password" type={pwShow ? 'text' : 'password'} value={pwValue}
                  onChange={(e) => setPwValue(e.target.value)} required
                  error={pwValue.length > 0 && pwValue.length < 12}
                  helperText="At least 12 characters, with a letter and a digit. All existing sessions are signed out."
                  slotProps={{
                    input: {
                      endAdornment: (
                        <InputAdornment position="end">
                          <Button size="small" onClick={() => setPwValue(generatePassword())}>Generate</Button>
                          <IconButton size="small" onClick={() => setPwShow((s) => !s)}>{pwShow ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}</IconButton>
                        </InputAdornment>
                      ),
                    },
                  }} />
              )}
            </>
          )}
        </DialogContent>
        <DialogActions>
          {pwDone
            ? <Button onClick={() => setDialog(null)}>Done</Button>
            : (
              <>
                <Button onClick={() => setDialog(null)}>Cancel</Button>
                <Button variant="contained" disabled={pwMut.isPending || (pwMode === 'set' && pwValue.length < 12)} onClick={() => pwMut.mutate()}>
                  {pwMut.isPending ? 'Working…' : pwMode === 'email' ? 'Send reset email' : 'Set password'}
                </Button>
              </>
            )}
        </DialogActions>
      </Dialog>
    </Box>
  )
}
