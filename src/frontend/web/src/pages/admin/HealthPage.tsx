import { Dns, Favorite, Verified } from '@mui/icons-material'
import { Box, Paper, Typography, alpha, useTheme } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { platformApi } from '../../api/platform'
import { ErrorAlert, PageHeader, StatusChip } from '../../components/ui'

function HealthCard({ kind, title, subtitle, icon }: { kind: 'live' | 'ready'; title: string; subtitle: string; icon: ReactElement }) {
  const theme = useTheme()
  const q = useQuery({ queryKey: ['health', kind], queryFn: () => platformApi.health(kind), refetchInterval: 15000, retry: false })
  const unhealthy = q.data !== undefined && (String(q.data).toLowerCase().includes('unhealth') || String(q.data).toLowerCase().includes('fail'))
  const ok = q.data !== undefined && !unhealthy
  const color = q.isPending || q.error ? theme.palette.text.disabled : ok ? theme.palette.success.main : theme.palette.error.main
  return (
    <Paper variant="outlined" sx={{ p: 3 }}>
      <Box sx={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', mb: 1 }}>
        <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(color, 0.12), color }}>
          {icon}
        </Box>
        {q.isPending ? <StatusChip value="Running" /> : q.error ? <StatusChip value="Offline" /> : <StatusChip value={unhealthy ? 'Failed' : 'Online'} />}
      </Box>
      <Typography variant="subtitle1">{title}</Typography>
      <Typography variant="caption" color="text.secondary">{subtitle}</Typography>
      {q.error && <Box sx={{ mt: 1.5 }}><ErrorAlert error={q.error} /></Box>}
      {typeof q.data === 'string' && <Typography variant="caption" color="text.secondary" sx={{ mt: 1.5, display: 'block' }} component="pre">{q.data}</Typography>}
    </Paper>
  )
}

export function HealthPage() {
  return (
    <Box>
      <PageHeader title="System health" subtitle="API liveness and readiness — refreshes every 15 seconds" />
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2.5 }}>
        <HealthCard kind="live" title="Liveness" subtitle="/health/live — process is up" icon={<Favorite />} />
        <HealthCard kind="ready" title="Readiness" subtitle="/health/ready — includes database connectivity" icon={<Dns />} />
      </Box>
      <Paper variant="outlined" sx={{ p: 3, mt: 2.5, display: 'flex', alignItems: 'center', gap: 2 }}>
        <Verified color="primary" />
        <Box>
          <Typography variant="subtitle1">API version v1</Typography>
          <Typography variant="caption" color="text.secondary">URL-segment versioning — /api/v1/…</Typography>
        </Box>
      </Paper>
    </Box>
  )
}
