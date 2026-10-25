import { Box, Paper, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { platformApi } from '../../api/platform'
import { ErrorAlert, PageHeader, StatCard, StatusChip } from '../../components/ui'

function HealthCard({ kind, title }: { kind: 'live' | 'ready'; title: string }) {
  const q = useQuery({ queryKey: ['health', kind], queryFn: () => platformApi.health(kind), refetchInterval: 15000, retry: false })
  return (
    <Paper variant="outlined" sx={{ p: 3 }}>
      <Typography variant="subtitle1" sx={{ fontWeight: 600 }} gutterBottom>{title}</Typography>
      {q.isPending ? <StatusChip value="Running" /> : q.error ? <ErrorAlert error={q.error} /> : (
        <StatusChip value={String(q.data).toLowerCase().includes('unhealth') || String(q.data).toLowerCase().includes('fail') ? 'Failed' : 'Online'} />
      )}
      {typeof q.data === 'string' && <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }} component="pre">{q.data}</Typography>}
    </Paper>
  )
}

export function HealthPage() {
  return (
    <Box>
      <PageHeader title="System health" subtitle="API liveness and readiness — refreshes every 15 seconds" />
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
        <HealthCard kind="live" title="Liveness (/health/live)" />
        <HealthCard kind="ready" title="Readiness (/health/ready — includes database)" />
      </Box>
      <Box sx={{ mt: 2 }}>
        <StatCard label="API version" value="v1" hint="URL-segment versioning — /api/v1/…" />
      </Box>
    </Box>
  )
}
