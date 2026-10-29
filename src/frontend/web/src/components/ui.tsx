import {
  Alert,
  AlertTitle,
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Paper,
  Skeleton,
  Stack,
  Typography,
  alpha,
  useTheme,
  type ChipProps,
} from '@mui/material'
import { ArrowForward } from '@mui/icons-material'
import type { ReactElement, ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <Box sx={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', mb: 3.5, gap: 2, flexWrap: 'wrap' }}>
      <Box>
        <Typography variant="h5" component="h1">{title}</Typography>
        {subtitle && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>{subtitle}</Typography>}
      </Box>
      {actions && <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>{actions}</Stack>}
    </Box>
  )
}

export function SectionCard({ title, subtitle, actions, children, sx }: {
  title?: string
  subtitle?: string
  actions?: ReactNode
  children: ReactNode
  sx?: object
}) {
  return (
    <Paper variant="outlined" sx={{ overflow: 'hidden', ...sx }}>
      {(title || actions) && (
        <Box sx={{ px: 3, pt: 2.5, pb: title ? 1.5 : 2.5, display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 2 }}>
          <Box>
            {title && <Typography variant="subtitle1">{title}</Typography>}
            {subtitle && <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>{subtitle}</Typography>}
          </Box>
          {actions}
        </Box>
      )}
      {children}
    </Paper>
  )
}

export function KpiCard({ label, value, hint, icon, tone = 'primary', to }: {
  label: string
  value: ReactNode
  hint?: string
  icon: ReactElement
  tone?: 'primary' | 'success' | 'info' | 'warning' | 'error'
  to?: string
}) {
  const theme = useTheme()
  const color = theme.palette[tone].main
  const body = (
    <Card
      variant="outlined"
      sx={{
        transition: 'box-shadow 160ms ease, transform 160ms ease, border-color 160ms ease',
        '&:hover': { borderColor: alpha(color, 0.4), boxShadow: `0 6px 24px -12px ${alpha(color, 0.45)}`, transform: 'translateY(-1px)' },
      }}
    >
      <CardContent sx={{ p: 2.5, '&:last-child': { pb: 2.5 } }}>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
          <Box>
            <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 500 }}>{label}</Typography>
            <Typography variant="h4" sx={{ fontWeight: 700, mt: 0.75, letterSpacing: -0.5 }}>{value}</Typography>
          </Box>
          <Box sx={{
            p: 1, borderRadius: 2, display: 'grid', placeItems: 'center',
            bgcolor: alpha(color, theme.palette.mode === 'dark' ? 0.18 : 0.1), color,
          }}>
            {icon}
          </Box>
        </Box>
        {hint && <Typography variant="caption" color="text.secondary" sx={{ mt: 0.75, display: 'block' }}>{hint}</Typography>}
      </CardContent>
    </Card>
  )
  return to ? <Box component={Link} to={to} sx={{ textDecoration: 'none', display: 'block' }}>{body}</Box> : body
}

export function TenantAvatar({ code, name, size = 36 }: { code: string; name?: string; size?: number }) {
  const theme = useTheme()
  const initials = (name || code)
    .split(/[\s-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]!.toUpperCase())
    .join('')
  return (
    <Avatar
      variant="rounded"
      sx={{
        width: size, height: size, fontSize: size * 0.38, fontWeight: 700, borderRadius: 1.5,
        bgcolor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.22 : 0.12),
        color: theme.palette.primary.main,
      }}
    >
      {initials}
    </Avatar>
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
  const c = tone[value]
  return <Chip size="small" label={value} color={c ?? 'default'} variant={c ? 'filled' : 'outlined'} />
}

export function ErrorAlert({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const e = error instanceof ApiError ? error : null
  return (
    <Alert severity="error" action={onRetry ? <Button size="small" color="inherit" onClick={onRetry}>Retry</Button> : undefined}>
      <AlertTitle>{e ? e.message : 'Something went wrong.'}</AlertTitle>
      {e?.correlationId && <>Reference: <code>{e.correlationId}</code></>}
    </Alert>
  )
}

export function TableSkeleton({ columns = 5, rows = 6 }: { columns?: number; rows?: number }) {
  return (
    <Box sx={{ px: 1 }}>
      {Array.from({ length: rows }).map((_, i) => (
        <Box key={i} sx={{ display: 'flex', gap: 3, py: 1.75, borderBottom: i < rows - 1 ? 1 : 0, borderColor: 'divider' }}>
          {Array.from({ length: columns }).map((_, j) => (
            <Skeleton key={j} variant="text" width={`${[22, 20, 14, 16, 12][j % 5]}%`} sx={{ fontSize: '1rem' }} />
          ))}
        </Box>
      ))}
    </Box>
  )
}

export function LoadingRows({ rows = 5 }: { rows?: number }) {
  return <Stack spacing={1}>{Array.from({ length: rows }, (_, i) => <Skeleton key={i} variant="rounded" height={44} />)}</Stack>
}

export function CenteredSpinner() {
  return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
}

export function EmptyState({ title, hint, icon, action }: { title: string; hint?: string; icon?: ReactElement; action?: ReactNode }) {
  const theme = useTheme()
  return (
    <Box sx={{ p: { xs: 4, sm: 7 }, textAlign: 'center' }}>
      {icon && (
        <Box sx={{
          mx: 'auto', mb: 2, width: 56, height: 56, borderRadius: '50%', display: 'grid', placeItems: 'center',
          bgcolor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.18 : 0.08), color: 'primary.main',
        }}>
          {icon}
        </Box>
      )}
      <Typography variant="subtitle1" color="text.secondary">{title}</Typography>
      {hint && <Typography variant="body2" color="text.disabled" sx={{ mt: 0.75 }}>{hint}</Typography>}
      {action && <Box sx={{ mt: 2 }}>{action}</Box>}
    </Box>
  )
}

export function ViewAllLink({ to, children }: { to: string; children?: ReactNode }) {
  return (
    <Button component={Link} to={to} size="small" endIcon={<ArrowForward sx={{ fontSize: '14px !important' }} />} sx={{ fontWeight: 600 }}>
      {children ?? 'View all'}
    </Button>
  )
}

export function ComingSoonPage({ title, description }: { title: string; description?: string }) {
  return (
    <Box>
      <PageHeader title={title} subtitle={description} />
      <Paper variant="outlined" sx={{ p: { xs: 5, sm: 9 }, textAlign: 'center' }}>
        <Chip label="Coming soon" color="primary" variant="outlined" sx={{ mb: 2.5, fontWeight: 600 }} />
        <Typography variant="h6" sx={{ mb: 1 }}>This area is on the platform roadmap</Typography>
        <Typography color="text.secondary" sx={{ maxWidth: 460, mx: 'auto' }}>
          The backend API for this feature does not exist yet, so the page stays disabled rather than showing placeholder data.
        </Typography>
      </Paper>
    </Box>
  )
}
