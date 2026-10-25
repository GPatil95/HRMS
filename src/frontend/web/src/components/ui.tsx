import { Alert, AlertTitle, Box, Chip, CircularProgress, Paper, Skeleton, Stack, Typography, type ChipProps } from '@mui/material'
import type { ReactNode } from 'react'
import { ApiError } from '../api/client'

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <Box sx={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', mb: 3, gap: 2, flexWrap: 'wrap' }}>
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 600 }}>{title}</Typography>
        {subtitle && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{subtitle}</Typography>}
      </Box>
      {actions && <Stack direction="row" spacing={1}>{actions}</Stack>}
    </Box>
  )
}

export function StatCard({ label, value, hint, color }: { label: string; value: ReactNode; hint?: string; color?: string }) {
  return (
    <Paper variant="outlined" sx={{ p: 2.5 }}>
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      <Typography variant="h4" sx={{ fontWeight: 700, mt: 0.5, color }}>{value}</Typography>
      {hint && <Typography variant="caption" color="text.secondary">{hint}</Typography>}
    </Paper>
  )
}

const tone: Record<string, ChipProps['color']> = {
  Active: 'success', Online: 'success', Succeeded: 'success', Published: 'success', Approved: 'success',
  Provisioning: 'info', Pending: 'info', Running: 'info', Trial: 'info', Requested: 'info', MetadataOnly: 'info',
  Suspended: 'warning', Maintenance: 'warning', PastDue: 'warning', Grace: 'warning', Draft: 'warning', ReadOnly: 'warning',
  ProvisioningFailed: 'error', Failed: 'error', Offline: 'error', Rejected: 'error', Revoked: 'error', Retired: 'error', ReadWrite: 'error',
  Inactive: 'default', Expired: 'default', Core: 'default', Optional: 'primary', SubModule: 'secondary',
}

export function StatusChip({ value }: { value?: string | null }) {
  if (!value) return <Chip size="small" label="—" variant="outlined" />
  return <Chip size="small" label={value} color={tone[value] ?? 'default'} variant={tone[value] ? 'filled' : 'outlined'} />
}

export function ErrorAlert({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const e = error instanceof ApiError ? error : null
  return (
    <Alert severity="error" action={onRetry ? <a role="button" style={{ cursor: 'pointer' }} onClick={onRetry}>Retry</a> : undefined}>
      <AlertTitle>{e ? e.message : 'Something went wrong.'}</AlertTitle>
      {e?.correlationId && <>Reference: <code>{e.correlationId}</code></>}
    </Alert>
  )
}

export function LoadingRows({ rows = 5 }: { rows?: number }) {
  return <Stack spacing={1}>{Array.from({ length: rows }, (_, i) => <Skeleton key={i} variant="rounded" height={44} />)}</Stack>
}

export function CenteredSpinner() {
  return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
}

export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <Paper variant="outlined" sx={{ p: 6, textAlign: 'center' }}>
      <Typography variant="h6" color="text.secondary">{title}</Typography>
      {hint && <Typography variant="body2" color="text.disabled" sx={{ mt: 1 }}>{hint}</Typography>}
    </Paper>
  )
}

export function ComingSoonPage({ title }: { title: string }) {
  return (
    <Box>
      <PageHeader title={title} />
      <Paper variant="outlined" sx={{ p: 6, textAlign: 'center' }}>
        <Chip label="Coming soon" color="primary" variant="outlined" sx={{ mb: 2 }} />
        <Typography color="text.secondary">The backend API for this area does not exist yet.</Typography>
      </Paper>
    </Box>
  )
}
