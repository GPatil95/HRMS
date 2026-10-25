import { Box, Button, Paper, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { itemsOf } from '../../api/client'
import { platformApi } from '../../api/platform'
import { CenteredSpinner, EmptyState, ErrorAlert, PageHeader, StatCard, StatusChip } from '../../components/ui'

export function OverviewPage() {
  const navigate = useNavigate()
  const tenants = useQuery({ queryKey: ['tenants', 'overview'], queryFn: () => platformApi.tenants(1, 200) })
  const events = useQuery({ queryKey: ['security-events'], queryFn: platformApi.securityEvents })

  if (tenants.isPending) return <CenteredSpinner />
  if (tenants.error) return <ErrorAlert error={tenants.error} onRetry={() => void tenants.refetch()} />

  const all = itemsOf(tenants.data)
  const count = (s: string) => all.filter((t) => t.status === s).length
  const recent = (events.data ?? []).slice(0, 8)

  return (
    <Box>
      <PageHeader
        title="Platform overview"
        subtitle="Tenants, provisioning status and recent security activity"
        actions={<Button component={Link} to="/admin/tenants/new" variant="contained">Create tenant</Button>}
      />
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 2, mb: 3 }}>
        <StatCard label="Total tenants" value={tenants.data?.totalCount ?? all.length} />
        <StatCard label="Active" value={count('Active')} color="success.main" />
        <StatCard label="Provisioning" value={count('Provisioning')} color="info.main" />
        <StatCard label="Provisioning failed" value={count('ProvisioningFailed')} color={count('ProvisioningFailed') ? 'error.main' : undefined} />
        <StatCard label="Suspended / inactive" value={count('Suspended') + count('Inactive')} color="warning.main" />
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 600 }} gutterBottom>Recent tenants</Typography>
          {all.length === 0 ? <EmptyState title="No tenants yet" hint="Create your first tenant to get started." /> : (
            <Table size="small">
              <TableHead><TableRow><TableCell>Tenant</TableCell><TableCell>Status</TableCell><TableCell>Created</TableCell></TableRow></TableHead>
              <TableBody>
                {all.slice(0, 6).map((t) => (
                  <TableRow key={t.id} hover onClick={() => navigate(`/admin/tenants/${t.id}`)} sx={{ cursor: 'pointer' }}>
                    <TableCell><strong>{t.code}</strong> — {t.name}</TableCell>
                    <TableCell><StatusChip value={t.status} /></TableCell>
                    <TableCell>{new Date(t.createdAt).toLocaleDateString()}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          <Button component={Link} to="/admin/tenants" size="small" sx={{ mt: 1 }}>View all</Button>
        </Paper>
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 600 }} gutterBottom>Recent security events</Typography>
          {events.isPending && <CenteredSpinner />}
          {events.error && <ErrorAlert error={events.error} />}
          {recent.length === 0 && !events.isPending && !events.error && <EmptyState title="No security events recorded" />}
          {recent.length > 0 && (
            <Table size="small">
              <TableHead><TableRow><TableCell>When</TableCell><TableCell>Event</TableCell><TableCell>Result</TableCell></TableRow></TableHead>
              <TableBody>
                {recent.map((e, i) => (
                  <TableRow key={i}>
                    <TableCell>{new Date(e.occurredAt).toLocaleString()}</TableCell>
                    <TableCell>{e.eventType}</TableCell>
                    <TableCell><StatusChip value={e.success ? 'Succeeded' : 'Failed'} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          <Button component={Link} to="/admin/audit" size="small" sx={{ mt: 1 }}>Open audit logs</Button>
        </Paper>
      </Box>
    </Box>
  )
}
