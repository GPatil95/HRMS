import { Box, Chip, Grid, Skeleton, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { tenantApi, type DashboardWidget } from '../../api/tenant'
import { EmptyState, ErrorAlert, KpiCard, PageHeader, SectionCard } from '../../components/ui'
import { useAuth } from '../../auth/AuthProvider'
import { Groups } from '@mui/icons-material'

function WidgetValue({ data }: { data: unknown }) {
  if (data == null) return <Typography variant="h5" sx={{ fontWeight: 700 }}>—</Typography>
  if (typeof data === 'number' || typeof data === 'string') {
    return <Typography variant="h5" sx={{ fontWeight: 700 }}>{String(data)}</Typography>
  }
  if (Array.isArray(data)) {
    if (data.length === 0) return <Typography variant="body2" color="text.secondary">Nothing to show.</Typography>
    return (
      <Stack spacing={0.5} sx={{ maxHeight: 180, overflow: 'auto' }}>
        {data.slice(0, 12).map((item, i) => (
          <Typography key={i} variant="body2" color="text.secondary" noWrap>
            {typeof item === 'object' && item !== null
              ? Object.values(item as Record<string, unknown>).slice(0, 3).map(String).join(' · ')
              : String(item)}
          </Typography>
        ))}
      </Stack>
    )
  }
  if (typeof data === 'object') {
    const entries = Object.entries(data as Record<string, unknown>).filter(([, v]) => v != null && typeof v !== 'object')
    if (entries.length === 0) return <Typography variant="body2" color="text.secondary">No data.</Typography>
    if (entries.length === 1) {
      return <Typography variant="h5" sx={{ fontWeight: 700 }}>{String(entries[0][1])}</Typography>
    }
    return (
      <Stack spacing={0.75}>
        {entries.slice(0, 6).map(([k, v]) => (
          <Box key={k} sx={{ display: 'flex', justifyContent: 'space-between', gap: 1 }}>
            <Typography variant="body2" color="text.secondary" sx={{ textTransform: 'capitalize' }}>{k.replace(/([A-Z])/g, ' $1')}</Typography>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>{String(v)}</Typography>
          </Box>
        ))}
      </Stack>
    )
  }
  return null
}

function WidgetCard({ w }: { w: DashboardWidget }) {
  const q = useQuery({ queryKey: ['tenant-widget', w.widgetCode], queryFn: () => tenantApi.widgetData(w.widgetCode), retry: 1 })
  return (
    <Grid size={{ xs: 12, sm: w.width > 6 ? 12 : 6, md: w.width }}>
      <SectionCard title={w.title} sx={{ height: '100%', minHeight: 120 }}>
        {q.isPending ? <Skeleton variant="rounded" height={48} /> : q.error ? (
          <Typography variant="caption" color="text.secondary">Data unavailable</Typography>
        ) : (
          <WidgetValue data={q.data} />
        )}
      </SectionCard>
    </Grid>
  )
}

export function DashboardPage() {
  const { me } = useAuth()
  const dash = useQuery({ queryKey: ['tenant-dashboard'], queryFn: tenantApi.dashboard })
  const employees = useQuery({ queryKey: ['employees', 'kpi'], queryFn: tenantApi.employees, retry: 0 })
  const notifications = useQuery({ queryKey: ['notifications', 'dash'], queryFn: tenantApi.notifications, retry: 0 })

  const widgets = dash.data?.widgets ?? []
  const totalEmployees = employees.data?.length
  const activeEmployees = employees.data?.filter((e) => e.status === 'Active').length
  const unread = notifications.data?.filter((n) => !n.isRead).length ?? 0
  const recent = (notifications.data ?? []).slice(0, 6)

  return (
    <Box>
      <PageHeader
        title="Dashboard"
        subtitle={`Welcome back${me?.displayName ? `, ${me.displayName}` : ''} · ${new Date().toLocaleDateString(undefined, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })}`}
      />
      <Grid container spacing={2} sx={{ mb: 2.5 }}>
        {[
          { label: 'Total employees', value: employees.error ? '—' : (totalEmployees ?? '…'), to: '/employees' },
          { label: 'Active employees', value: employees.error ? '—' : (activeEmployees ?? '…'), to: '/employees' },
          { label: 'Unread notifications', value: notifications.error ? '—' : unread, to: '/notifications' },
        ].map((k) => (
          <Grid key={k.label} size={{ xs: 12, sm: 4 }}>
            <KpiCard label={k.label} value={k.value} to={k.to} icon={<Groups />} />
          </Grid>
        ))}
      </Grid>

      {dash.error && <ErrorAlert error={dash.error} onRetry={() => dash.refetch()} />}
      {dash.isPending ? (
        <Grid container spacing={2}>{[0, 1, 2].map((i) => <Grid key={i} size={{ xs: 12, md: 4 }}><Skeleton variant="rounded" height={140} /></Grid>)}</Grid>
      ) : widgets.length === 0 && !dash.error ? null : (
        <Grid container spacing={2}>
          {widgets.map((w) => <WidgetCard key={w.id} w={w} />)}
        </Grid>
      )}

      <Grid container spacing={2} sx={{ mt: 0.5 }}>
        <Grid size={{ xs: 12, md: 6 }}>
          <SectionCard title="Recent notifications" actions={<Link to="/notifications">View all</Link>}>
            {recent.length === 0 ? (
              <EmptyState title="No notifications" hint="You're all caught up." />
            ) : (
              <Stack spacing={0} divider={<Box sx={{ borderTop: 1, borderColor: 'divider' }} />}>
                {recent.map((n) => (
                  <Box key={n.id} sx={{ py: 1.25, display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                      <Typography variant="body2" sx={{ fontWeight: n.isRead ? 400 : 650 }} noWrap>{n.title}</Typography>
                      <Typography variant="caption" color="text.secondary">{new Date(n.createdAt).toLocaleString()}</Typography>
                    </Box>
                    {!n.isRead && <Chip label="New" size="small" color="primary" variant="outlined" />}
                  </Box>
                ))}
              </Stack>
            )}
          </SectionCard>
        </Grid>
        <Grid size={{ xs: 12, md: 6 }}>
          <SectionCard title="Quick actions">
            <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
              <Link to="/employees" style={{ textDecoration: 'none' }}><Chip label="Employee directory" variant="outlined" clickable /></Link>
              <Link to="/org/companies" style={{ textDecoration: 'none' }}><Chip label="Companies" variant="outlined" clickable /></Link>
              <Link to="/reports" style={{ textDecoration: 'none' }}><Chip label="Reports" variant="outlined" clickable /></Link>
              <Link to="/admin/users" style={{ textDecoration: 'none' }}><Chip label="Manage users" variant="outlined" clickable /></Link>
            </Box>
          </SectionCard>
        </Grid>
      </Grid>
    </Box>
  )
}
