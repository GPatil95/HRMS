import {
  ArrowBack,
  ContentCopy,
  ExpandMore,
  Key,
  MoreVert,
  OpenInNew,
  PauseCircle,
  PlayCircle,
  Shield,
  Sync,
} from '@mui/icons-material'
import {
  Alert,
  AlertTitle,
  Box,
  Button,
  Collapse,
  IconButton,
  Menu,
  MenuItem,
  Paper,
  Switch,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tabs,
  TextField,
  Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { platformApi, type TenantStatus } from '../../api/platform'
import { ProvisioningTimeline, stepLabel } from '../../components/ProvisioningTimeline'
import { ReasonDialog } from '../../components/ReasonDialog'
import { ResetAdminPasswordDialog } from '../../components/ResetAdminPasswordDialog'
import { useToast } from '../../components/toast'
import { CenteredSpinner, EmptyState, ErrorAlert, SectionCard, StatusChip, TenantAvatar } from '../../components/ui'

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 2, py: 0.9, borderBottom: 1, borderColor: 'divider', '&:last-of-type': { borderBottom: 0 } }}>
      <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>{label}</Typography>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, textAlign: 'right', minWidth: 0, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
        <Typography variant="body2" component="span">{children}</Typography>
      </Box>
    </Box>
  )
}

const dbStateLabel: Record<string, string> = { Online: 'Ready', Provisioning: 'Provisioning', Maintenance: 'Maintenance', Offline: 'Offline' }

export function TenantDetailPage() {
  const { id = '' } = useParams()
  const qc = useQueryClient()
  const toast = useToast()
  const [tab, setTab] = useState(0)
  const q = useQuery({
    queryKey: ['tenant', id], queryFn: () => platformApi.tenant(id),
    refetchInterval: (qq) =>
      ['Pending', 'Running'].includes(qq.state.data?.provisioningStatus ?? '') || qq.state.data?.tenant.status === 'Provisioning' ? 5000 : false,
  })
  const [dialog, setDialog] = useState<null | 'suspend' | 'activate' | 'retry' | 'subscription' | 'adminPw' | { module: string; enabled: boolean }>(null)
  const [menuEl, setMenuEl] = useState<HTMLElement | null>(null)
  const [subStatus, setSubStatus] = useState('Active')
  const [subEndsOn, setSubEndsOn] = useState('')
  const [techOpen, setTechOpen] = useState(false)

  const done = (msg: string) => { toast(msg); void q.refetch(); void qc.invalidateQueries({ queryKey: ['tenants'] }); void qc.invalidateQueries({ queryKey: ['operations'] }) }
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
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => void q.refetch()} />
  const d = q.data!
  const t = d.tenant
  const op = d.operation ?? null
  const provFailed = d.provisioningStatus === 'Failed' || t.status === 'ProvisioningFailed'
  const provRunning = d.provisioningStatus === 'Pending' || d.provisioningStatus === 'Running' || t.status === 'Provisioning'
  const adminExists = op?.steps?.some((s) => s.step === 'create_admin' && s.status === 'completed') ?? false
  const failedStep = op?.steps.find((s) => s.status === 'failed') ?? (op?.status === 'Failed' && op.currentStep ? { step: op.currentStep, status: 'failed', at: null, error: op.error } : null)
  const busy = statusMut.isPending || retryMut.isPending || moduleMut.isPending || subMut.isPending
  const tenantUrl = t.primaryHost ? `${window.location.protocol}//${t.primaryHost}${window.location.port ? `:${window.location.port}` : ''}` : null

  const canSuspend = t.status === 'Active'
  const canActivate = t.status === 'Suspended' || t.status === 'Inactive'
  const dbLabel = d.databaseStatus === 'Provisioning' && provFailed ? 'Provisioning incomplete' : (d.databaseStatus ? dbStateLabel[d.databaseStatus] ?? d.databaseStatus : '—')

  return (
    <Box>
      <Button component={Link} to="/admin/tenants" startIcon={<ArrowBack />} size="small" sx={{ mb: 1.5, color: 'text.secondary' }}>
        All tenants
      </Button>

      {/* ── Header ─────────────────────────────────────────────────── */}
      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 }, mb: 2.5, display: 'flex', flexWrap: 'wrap', gap: 2, alignItems: { sm: 'center' }, flexDirection: { xs: 'column', sm: 'row' }, justifyContent: 'space-between' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, minWidth: 0 }}>
          <TenantAvatar code={t.code} name={t.name} size={52} />
          <Box sx={{ minWidth: 0 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, flexWrap: 'wrap' }}>
              <Typography variant="h5" component="h1">{t.name}</Typography>
              <StatusChip value={t.status} />
            </Box>
            <Typography variant="body2" color="text.secondary" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>
              {t.code}{t.primaryHost ? ` · ${t.primaryHost}` : ''} · Tenant #{t.tenantNumber}
            </Typography>
          </Box>
        </Box>
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center', width: { xs: '100%', sm: 'auto' } }}>
          {tenantUrl && t.status === 'Active' && (
            <Button variant="outlined" size="small" href={tenantUrl} target="_blank" rel="noreferrer" endIcon={<OpenInNew sx={{ fontSize: 16 }} />}>
              Open Tenant
            </Button>
          )}
          {provFailed && (
            <Button variant="contained" color="warning" size="small" startIcon={<Sync />} onClick={() => setDialog('retry')} disabled={busy}>
              Retry provisioning
            </Button>
          )}
          {adminExists && (
            <Button variant="outlined" size="small" startIcon={<Key />} onClick={() => setDialog('adminPw')} disabled={busy}>
              Reset admin password
            </Button>
          )}
          {canSuspend && <Button variant="outlined" color="error" size="small" startIcon={<PauseCircle />} onClick={() => setDialog('suspend')} disabled={busy}>Suspend</Button>}
          {canActivate && <Button variant="outlined" color="success" size="small" startIcon={<PlayCircle />} onClick={() => setDialog('activate')} disabled={busy}>Activate</Button>}
          <IconButton aria-label="More actions" size="small" onClick={(e) => setMenuEl(e.currentTarget)}>
            <MoreVert />
          </IconButton>
          <Menu anchorEl={menuEl} open={!!menuEl} onClose={() => setMenuEl(null)}>
            <MenuItem onClick={() => { setMenuEl(null); setSubStatus(d.subscriptionStatus ?? 'Active'); setDialog('subscription') }}>Change subscription</MenuItem>
          </Menu>
        </Box>
      </Paper>

      {/* ── Failure banner ─────────────────────────────────────────── */}
      {provFailed && (
        <Alert severity="error" sx={{ mb: 2.5 }} action={
          <Button color="inherit" size="small" startIcon={<Sync />} onClick={() => setDialog('retry')} disabled={busy}>Retry</Button>
        }>
          <AlertTitle sx={{ fontWeight: 600 }}>Provisioning failed</AlertTitle>
          {failedStep
            ? <>Tenant provisioning stopped while <strong>{stepLabel(failedStep.step).toLowerCase()}</strong>. Retry provisioning to continue.</>
            : 'Tenant provisioning could not be completed. Retry provisioning to continue.'}
          <Box sx={{ mt: 1 }}>
            <Button
              size="small" variant="text" color="inherit" endIcon={<ExpandMore sx={{ transform: techOpen ? 'rotate(180deg)' : 'none', transition: '150ms' }} />}
              onClick={() => setTechOpen((o) => !o)}
            >
              Technical details
            </Button>
            <Collapse in={techOpen}>
              <Box sx={{ mt: 0.5, p: 1.5, borderRadius: 1, bgcolor: 'rgba(0,0,0,0.15)' }}>
                {(d.provisioningError ?? op?.error) && (
                  <Typography variant="caption" component="pre" sx={{ fontFamily: 'monospace', whiteSpace: 'pre-wrap', display: 'block', mb: 0.5 }}>
                    {d.provisioningError ?? op?.error}
                  </Typography>
                )}
                {op?.correlationId && (
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                    <Typography variant="caption" sx={{ fontFamily: 'monospace' }}>Correlation: {op.correlationId}</Typography>
                    <IconButton size="small" aria-label="Copy correlation ID" onClick={() => void navigator.clipboard.writeText(op.correlationId!)}>
                      <ContentCopy sx={{ fontSize: 13 }} />
                    </IconButton>
                  </Box>
                )}
              </Box>
            </Collapse>
          </Box>
        </Alert>
      )}
      {provRunning && (
        <Alert severity="info" sx={{ mb: 2.5 }}>
          Provisioning in progress{op?.currentStep ? ` — ${stepLabel(op.currentStep).toLowerCase()}` : ''}. This page refreshes automatically.
        </Alert>
      )}

      {/* ── Tabs ───────────────────────────────────────────────────── */}
      <Tabs value={tab} onChange={(_, v) => setTab(v as number)} variant="scrollable" scrollButtons="auto" sx={{ mb: 2.5, borderBottom: 1, borderColor: 'divider' }}>
        <Tab label="Overview" />
        <Tab label="Database" />
        <Tab label="Provisioning" />
        <Tab label="Modules" />
        <Tab label="Security" />
      </Tabs>

      {tab === 0 && (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2.5 }}>
          <SectionCard title="Tenant" subtitle="Identity and lifecycle">
            <Box sx={{ px: 3, pb: 1.5 }}>
              <Field label="Status"><StatusChip value={t.status} /></Field>
              <Field label="Tenant number">#{t.tenantNumber}</Field>
              <Field label="Administrator">
                <Typography component="span" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>
                  {d.adminName ?? '—'}{d.adminEmail ? ` · ${d.adminEmail}` : ''}
                </Typography>
              </Field>
              <Field label="Domain">
                <Typography component="span" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{t.primaryHost ?? '—'}</Typography>
              </Field>
              <Field label="Created">{new Date(t.createdAt).toLocaleString()}</Field>
            </Box>
          </SectionCard>
          <SectionCard title="Subscription" subtitle="Plan and billing state"
            actions={<Button size="small" variant="outlined" onClick={() => { setSubStatus(d.subscriptionStatus ?? 'Active'); setDialog('subscription') }}>Change</Button>}>
            <Box sx={{ px: 3, pb: 1.5 }}>
              <Field label="Plan"><Typography component="span" variant="body2" sx={{ fontFamily: 'monospace' }}>{d.planCode ?? 'Platform default'}</Typography></Field>
              <Field label="Subscription"><StatusChip value={d.subscriptionStatus} /></Field>
              <Field label="Provisioning"><StatusChip value={d.provisioningStatus} /></Field>
            </Box>
          </SectionCard>
        </Box>
      )}

      {tab === 1 && (
        <SectionCard title="Database" subtitle="Tenant database placement and state">
          <Box sx={{ px: 3, pb: 1.5 }}>
            <Field label="Database">
              <Typography component="span" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{d.databaseName ?? '—'}</Typography>
            </Field>
            <Field label="Status"><StatusChip value={dbLabel === 'Ready' ? 'Online' : dbLabel === 'Provisioning incomplete' ? 'ProvisioningFailed' : d.databaseStatus} /></Field>
            {d.databaseStatus === 'Provisioning' && provFailed && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', pb: 1 }}>
                The database may already exist, but provisioning did not complete — retry provisioning to finish setup.
              </Typography>
            )}
            <Field label="Schema version">{d.schemaVersion ?? '—'}</Field>
            <Field label="Provisioning"><StatusChip value={d.provisioningStatus} /></Field>
          </Box>
        </SectionCard>
      )}

      {tab === 2 && (
        <SectionCard title="Provisioning progress" subtitle={op ? `Operation ${op.operation} · attempt ${op.attempt || 1}` : undefined}>
          {op ? (
            <Box sx={{ px: 3, py: 2.5 }}>
              <ProvisioningTimeline operation={op} />
            </Box>
          ) : (
            <EmptyState title="No provisioning operation recorded" icon={<Sync />} />
          )}
        </SectionCard>
      )}

      {tab === 3 && (
        <SectionCard title="Module entitlements" subtitle="Core modules are always on. Optional modules can be enabled per tenant.">
          {d.modules.length === 0 ? <EmptyState title="No modules in catalogue" /> : (
            <Box sx={{ overflowX: 'auto' }}>
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
                        <Switch size="small" checked={m.enabled} disabled={m.kind === 'Core' || busy}
                          onChange={(e) => setDialog({ module: m.code, enabled: e.target.checked })} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          )}
        </SectionCard>
      )}

      {tab === 4 && (
        <SectionCard title="Administrator credentials" subtitle="Actions apply to the tenant admin account only">
          {adminExists ? (
            <Box sx={{ px: 3, py: 2.5, display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2, flexWrap: 'wrap' }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 0 }}>
                <Shield color="action" />
                <Box>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>{d.adminName ?? 'Tenant admin'}</Typography>
                  <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{d.adminEmail}</Typography>
                </Box>
              </Box>
              <Button variant="outlined" size="small" startIcon={<Key />} onClick={() => setDialog('adminPw')}>Reset admin password</Button>
            </Box>
          ) : (
            <EmptyState title="No admin user yet" hint="The tenant admin is created by the 'Create admin' provisioning step." icon={<Shield />} />
          )}
        </SectionCard>
      )}

      {/* ── Dialogs ────────────────────────────────────────────────── */}
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
      <ResetAdminPasswordDialog open={dialog === 'adminPw'} tenantId={id} adminEmail={d.adminEmail} onClose={() => setDialog(null)} />
    </Box>
  )
}
