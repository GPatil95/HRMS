import {
  AddBusiness,
  CheckCircle,
  Error as ErrorOutline,
  Key,
  Login,
  Logout,
  PauseCircle,
  Sync,
  TableRows,
  VerifiedUser,
  WarningAmber,
} from '@mui/icons-material'
import {
  Box,
  Button,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
  alpha,
  useTheme,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { itemsOf } from '../../api/client'
import { platformApi, type SecurityEvent } from '../../api/platform'
import {
  EmptyState,
  ErrorAlert,
  KpiCard,
  PageHeader,
  SectionCard,
  StatusChip,
  TableSkeleton,
  TenantAvatar,
  ViewAllLink,
} from '../../components/ui'

const eventIcons: [RegExp, ReactElement][] = [
  [/LOGIN_SUCCEEDED/i, <Login fontSize="small" />],
  [/LOGIN_FAILED|FAILED/i, <ErrorOutline fontSize="small" />],
  [/LOGOUT/i, <Logout fontSize="small" />],
  [/TENANT/i, <TableRows fontSize="small" />],
  [/SUBSCRIPTION/i, <VerifiedUser fontSize="small" />],
]

function EventIcon({ type }: { type: string }) {
  const theme = useTheme()
  const icon = eventIcons.find(([re]) => re.test(type))?.[1] ?? <Key fontSize="small" />
  return (
    <Box sx={{
      width: 30, height: 30, borderRadius: 1.5, display: 'grid', placeItems: 'center', flexShrink: 0,
      bgcolor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.18 : 0.08), color: 'primary.main',
    }}>
      {icon}
    </Box>
  )
}

function EventRow({ e }: { e: SecurityEvent }) {
  return (
    <TableRow hover>
      <TableCell sx={{ width: 40 }}><EventIcon type={e.eventType} /></TableCell>
      <TableCell>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>{e.eventType}</Typography>
        <Typography variant="caption" color="text.secondary">{e.userEmail ?? 'system'}</Typography>
      </TableCell>
      <TableCell><StatusChip value={e.success ? 'Succeeded' : 'Failed'} /></TableCell>
      <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
        <Typography variant="caption" color="text.secondary">{new Date(e.occurredAt).toLocaleString()}</Typography>
      </TableCell>
    </TableRow>
  )
}

function HealthRow({ label, ok, detail }: { label: string; ok: boolean | null; detail?: string }) {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', py: 0.9, borderBottom: 1, borderColor: 'divider', '&:last-of-type': { borderBottom: 0 } }}>
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      {ok === null
        ? <Typography variant="caption" color="text.disabled">{detail ?? 'Not monitored'}</Typography>
        : <StatusChip value={ok ? 'Active' : 'Failed'} />}
    </Box>
  )
}

export function OverviewPage() {
  const navigate = useNavigate()
  const tenants = useQuery({ queryKey: ['tenants', 'overview'], queryFn: () => platformApi.tenants(1, 200) })
  const events = useQuery({ queryKey: ['security-events'], queryFn: platformApi.securityEvents })
  const ops = useQuery({ queryKey: ['operations', 'overview'], queryFn: () => platformApi.operations(1, 50), refetchInterval: 15000 })
  const health = useQuery({ queryKey: ['health', 'ready'], queryFn: () => platformApi.health('ready'), retry: 0 })

  if (tenants.isPending) {
    return (
      <Box>
        <PageHeader title="Platform overview" />
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 2, mb: 3 }}>
          {Array.from({ length: 5 }).map((_, i) => <TableSkeleton key={i} columns={1} rows={2} />)}
        </Box>
        <SectionCard><TableSkeleton /></SectionCard>
      </Box>
    )
  }
  if (tenants.error) return <ErrorAlert error={tenants.error} onRetry={() => void tenants.refetch()} />

  const all = itemsOf(tenants.data)
  const count = (s: string) => all.filter((t) => t.status === s).length
  const failed = count('ProvisioningFailed')
  const suspended = count('Suspended') + count('Inactive')
  const recent = (events.data ?? []).slice(0, 8)

  return (
    <Box>
      <PageHeader
        title="Platform overview"
        subtitle="Tenants, provisioning status and recent security activity"
        actions={<Button component={Link} to="/admin/tenants/new" variant="contained" startIcon={<AddBusiness />}>Create tenant</Button>}
      />
      <Box sx={{ display: 'flex', gap: 1, mb: 3, flexWrap: 'wrap' }}>
        <Button component={Link} to="/admin/tenants/new" size="small" variant="outlined" startIcon={<AddBusiness fontSize="small" />}>New tenant</Button>
        <Button component={Link} to="/admin/tenants" size="small" variant="outlined" startIcon={<TableRows fontSize="small" />}>All tenants</Button>
        <Button component={Link} to="/admin/operations" size="small" variant="outlined" startIcon={<Sync fontSize="small" />}>Operations</Button>
        <Button component={Link} to="/admin/audit" size="small" variant="outlined" startIcon={<Key fontSize="small" />}>Audit logs</Button>
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(210px, 1fr))', gap: 2, mb: 3.5 }}>
        <KpiCard label="Total tenants" value={tenants.data?.totalCount ?? all.length} icon={<TableRows />} hint="Every tenant has its own database" to="/admin/tenants" />
        <KpiCard label="Active" value={count('Active')} icon={<CheckCircle />} tone="success" hint="Serving traffic" to="/admin/tenants" />
        <KpiCard label="Provisioning" value={count('Provisioning')} icon={<Sync />} tone="info" hint="Setup in progress" to="/admin/operations" />
        <KpiCard label="Provisioning failed" value={failed} icon={<WarningAmber />} tone={failed ? 'error' : 'primary'} hint={failed ? 'Needs retry' : 'None'} to="/admin/operations" />
        <KpiCard label="Suspended / inactive" value={suspended} icon={<PauseCircle />} tone={suspended ? 'warning' : 'primary'} hint="Access blocked" to="/admin/tenants" />
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1.2fr 1fr' }, gap: 2.5, mb: 2.5 }}>
        <SectionCard title="Provisioning health" subtitle="Latest provisioning operations" actions={<ViewAllLink to="/admin/operations" />}>
          {ops.isPending && <TableSkeleton columns={3} />}
          {ops.error && <Box sx={{ p: 3 }}><ErrorAlert error={ops.error} /></Box>}
          {ops.data && (
            <Box sx={{ px: 3, py: 2, display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 2 }}>
              {(['Succeeded', 'Running', 'Failed'] as const).map((s) => {
                const n = ops.data.items.filter((o) => o.status === s || (s === 'Running' && o.status === 'Pending')).length
                return (
                  <Box key={s} sx={{ textAlign: 'center', py: 1.5, borderRadius: 2, border: 1, borderColor: 'divider' }}>
                    <Typography variant="h4" sx={{ fontWeight: 700, color: s === 'Failed' && n ? 'error.main' : s === 'Succeeded' ? 'success.main' : 'info.main' }}>{n}</Typography>
                    <Typography variant="caption" color="text.secondary">{s === 'Running' ? 'In progress' : s}</Typography>
                  </Box>
                )
              })}
            </Box>
          )}
        </SectionCard>
        <SectionCard title="System health" subtitle="Live readiness signals">
          <Box sx={{ px: 3, pb: 1.5 }}>
            <HealthRow label="API" ok={true} detail="Responding" />
            <HealthRow label="PostgreSQL / Redis" ok={health.isPending ? null : !health.error} detail={health.isPending ? 'Checking…' : health.error ? 'Unreachable' : 'Ready'} />
            <HealthRow label="Mail service" ok={null} />
            <HealthRow label="Background jobs" ok={null} />
          </Box>
        </SectionCard>
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1.2fr 1fr' }, gap: 2.5 }}>
        <SectionCard title="Recent tenants" subtitle="Newest tenants on the platform" actions={<ViewAllLink to="/admin/tenants" />}>
          {all.length === 0 ? (
            <EmptyState title="No tenants yet" hint="Create your first tenant to get started." icon={<AddBusiness />}
              action={<Button component={Link} to="/admin/tenants/new" variant="contained" size="small">Create tenant</Button>} />
          ) : (
            <Table size="small">
              <TableHead><TableRow><TableCell>Tenant</TableCell><TableCell>Status</TableCell><TableCell align="right">Created</TableCell></TableRow></TableHead>
              <TableBody>
                {all.slice(0, 6).map((t) => (
                  <TableRow key={t.id} hover onClick={() => navigate(`/admin/tenants/${t.id}`)} sx={{ cursor: 'pointer' }}>
                    <TableCell>
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                        <TenantAvatar code={t.code} name={t.name} size={32} />
                        <Box>
                          <Typography variant="body2" sx={{ fontWeight: 650 }}>{t.name}</Typography>
                          <Typography variant="caption" color="text.secondary">{t.code}</Typography>
                        </Box>
                      </Box>
                    </TableCell>
                    <TableCell><StatusChip value={t.status} /></TableCell>
                    <TableCell align="right"><Typography variant="caption" color="text.secondary">{new Date(t.createdAt).toLocaleDateString()}</Typography></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </SectionCard>
        <SectionCard title="Recent security events" subtitle="Latest platform security signals" actions={<ViewAllLink to="/admin/audit">Open audit logs</ViewAllLink>}>
          {events.isPending && <TableSkeleton columns={3} />}
          {events.error && <ErrorAlert error={events.error} />}
          {recent.length === 0 && !events.isPending && !events.error && <EmptyState title="No security events recorded" icon={<VerifiedUser />} />}
          {recent.length > 0 && (
            <Table size="small">
              <TableBody>{recent.map((e, i) => <EventRow key={i} e={e} />)}</TableBody>
            </Table>
          )}
        </SectionCard>
      </Box>
    </Box>
  )
}
