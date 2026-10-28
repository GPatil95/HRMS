import { useMemo } from 'react'
import { Box, Button, Chip, Divider, Grid, Paper, Skeleton, Stack, Typography, alpha, useTheme } from '@mui/material'
import { AssessmentOutlined, BadgeOutlined, Groups, MailOutlined, NotificationsActive, PersonOff } from '@mui/icons-material'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { tenantApi, type DashboardWidget } from '../../api/tenant'
import { EmptyState, ErrorAlert, SectionCard } from '../../components/ui'
import { useAuth } from '../../auth/AuthProvider'

const initials = (name?: string) => (name ?? '?').split(' ').map((p) => p[0]).filter(Boolean).slice(0, 2).join('').toUpperCase()

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
        <Box sx={{ px: 3, pb: 2.5 }}>
          {q.isPending ? <Skeleton variant="rounded" height={48} /> : q.error ? (
            <Typography variant="caption" color="text.secondary">Data unavailable</Typography>
          ) : (
            <WidgetValue data={q.data} />
          )}
        </Box>
      </SectionCard>
    </Grid>
  )
}

const kpis = [
  { key: 'total', label: 'Total employees', icon: Groups, to: '/employees', tone: 'primary' as const },
  { key: 'active', label: 'Active', icon: BadgeOutlined, to: '/employees', tone: 'success' as const },
  { key: 'inactive', label: 'Inactive', icon: PersonOff, to: '/employees', tone: 'warning' as const },
  { key: 'unread', label: 'Unread notifications', icon: NotificationsActive, to: '/notifications', tone: 'info' as const },
]

export function DashboardPage() {
  const { me } = useAuth()
  const theme = useTheme()
  const dash = useQuery({ queryKey: ['tenant-dashboard'], queryFn: tenantApi.dashboard })
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const notifications = useQuery({ queryKey: ['notifications'], queryFn: tenantApi.notifications })

  const total = employees.data?.length ?? 0
  const active = employees.data?.filter((e) => e.status === 'Active').length ?? 0
  const unread = notifications.data?.filter((n) => !n.isRead).length ?? 0
  const recent = (notifications.data ?? []).slice(0, 6)
  const widgets = dash.data?.widgets ?? []

  const values: Record<string, number | string> = useMemo(() => ({
    total: employees.isPending ? '…' : employees.error ? '—' : total,
    active: employees.isPending ? '…' : employees.error ? '—' : active,
    inactive: employees.isPending ? '…' : employees.error ? '—' : total - active,
    unread: notifications.isPending ? '…' : notifications.error ? '—' : unread,
  }), [employees.isPending, employees.error, notifications.isPending, notifications.error, total, active, unread])

  const today = new Date().toLocaleDateString(undefined, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })
  const hour = new Date().getHours()
  const greeting = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening'

  return (
    <Box>
      <Paper
        variant="outlined"
        sx={{
          p: { xs: 2.5, sm: 3 }, mb: 2.5, display: 'flex', alignItems: 'center', gap: 2.5,
          background: (t) => `linear-gradient(120deg, ${alpha(t.palette.primary.main, t.palette.mode === 'dark' ? 0.14 : 0.07)} 0%, transparent 60%)`,
        }}
      >
        <Box sx={{ width: 52, height: 52, borderRadius: '14px', display: 'grid', placeItems: 'center', bgcolor: 'primary.main', color: 'primary.contrastText', fontWeight: 800, fontSize: 18, flexShrink: 0 }}>
          {initials(me?.displayName)}
        </Box>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h5" sx={{ fontWeight: 700, lineHeight: 1.25 }}>
            {greeting}{me?.displayName ? `, ${me.displayName.split(' ')[0]}` : ''}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>{today}</Typography>
        </Box>
        {unread > 0 && (
          <Chip
            component={Link} to="/notifications" clickable
            icon={<NotificationsActive />}
            label={`${unread} unread`}
            color="primary" variant="outlined"
            sx={{ display: { xs: 'none', sm: 'inline-flex' } }}
          />
        )}
      </Paper>

      <Grid container spacing={2} sx={{ mb: 2.5 }}>
        {kpis.map((k) => {
          const Icon = k.icon
          const color = theme.palette[k.tone].main
          return (
            <Grid key={k.key} size={{ xs: 6, sm: 3 }}>
              <Paper component={Link} to={k.to} variant="outlined" sx={{ display: 'block', p: 2, textDecoration: 'none', transition: 'border-color 160ms ease', '&:hover': { borderColor: alpha(color, 0.5) } }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                  <Box sx={{ p: 0.75, borderRadius: 1.5, display: 'grid', placeItems: 'center', bgcolor: alpha(color, theme.palette.mode === 'dark' ? 0.2 : 0.12), color }}>
                    <Icon sx={{ fontSize: 18 }} />
                  </Box>
                </Box>
                {employees.isPending || notifications.isPending ? (
                  <Skeleton width={48} height={34} />
                ) : (
                  <Typography variant="h4" sx={{ fontWeight: 700, letterSpacing: -0.5, lineHeight: 1.1 }}>{values[k.key]}</Typography>
                )}
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>{k.label}</Typography>
              </Paper>
            </Grid>
          )
        })}
      </Grid>

      {dash.error && <Box sx={{ mb: 2 }}><ErrorAlert error={dash.error} onRetry={() => dash.refetch()} /></Box>}
      {dash.isPending ? (
        <Grid container spacing={2}>{[0, 1].map((i) => <Grid key={i} size={{ xs: 12, md: 6 }}><Skeleton variant="rounded" height={150} /></Grid>)}</Grid>
      ) : widgets.length > 0 ? (
        <Grid container spacing={2} sx={{ mb: 0.5 }}>
          {widgets.map((w) => <WidgetCard key={w.id} w={w} />)}
        </Grid>
      ) : null}

      <Grid container spacing={2} sx={{ mt: 0.5 }}>
        <Grid size={{ xs: 12, md: 7 }}>
          <SectionCard title="Recent notifications"
            actions={<Button component={Link} to="/notifications" size="small">View all</Button>}>
            {notifications.isPending ? (
              <Box sx={{ p: 2 }}>{[0, 1, 2].map((i) => <Skeleton key={i} height={40} sx={{ mb: 1 }} />)}</Box>
            ) : recent.length === 0 ? (
              <EmptyState title="No notifications" hint="You're all caught up." icon={<MailOutlined />} />
            ) : (
              <Stack divider={<Divider />}>
                {recent.map((n) => (
                  <Box key={n.id} sx={{ px: 3, py: 1.5, display: 'flex', alignItems: 'center', gap: 1.5 }}>
                    <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                      <Typography variant="body2" sx={{ fontWeight: n.isRead ? 400 : 650 }} noWrap>{n.title}</Typography>
                      <Typography variant="caption" color="text.secondary">{new Date(n.createdAt).toLocaleString()}</Typography>
                    </Box>
                    {!n.isRead && <Box sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: 'primary.main', flexShrink: 0 }} />}
                  </Box>
                ))}
              </Stack>
            )}
          </SectionCard>
        </Grid>
        <Grid size={{ xs: 12, md: 5 }}>
          <SectionCard title="Quick actions">
            <Box sx={{ p: 2.5, display: 'flex', gap: 1, flexWrap: 'wrap' }}>
              <Button component={Link} to="/employees" variant="outlined" size="small" startIcon={<Groups />}>Employee directory</Button>
              <Button component={Link} to="/org/companies" variant="outlined" size="small" startIcon={<BadgeOutlined />}>Organization</Button>
              <Button component={Link} to="/reports" variant="outlined" size="small" startIcon={<AssessmentOutlined />}>Reports</Button>
              <Button component={Link} to="/admin/users" variant="outlined" size="small" startIcon={<PersonOff />}>Manage users</Button>
            </Box>
          </SectionCard>
        </Grid>
      </Grid>
    </Box>
  )
}
