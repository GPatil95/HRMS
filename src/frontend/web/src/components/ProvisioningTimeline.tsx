import { CheckCircle, Error as ErrorIcon, RadioButtonUnchecked } from '@mui/icons-material'
import { Box, CircularProgress, Typography } from '@mui/material'
import type { ProvisioningOperation } from '../api/platform'

/** Canonical provisioning pipeline order — matches ProvisioningSteps in the backend. */
export const PROVISIONING_STEPS: { key: string; label: string }[] = [
  { key: 'allocate_cluster', label: 'Allocate cluster' },
  { key: 'store_secrets', label: 'Store secrets' },
  { key: 'create_roles', label: 'Create roles' },
  { key: 'create_database', label: 'Create database' },
  { key: 'configure_database', label: 'Configure database' },
  { key: 'migrate', label: 'Migrate' },
  { key: 'grants', label: 'Grants' },
  { key: 'tenant_identity', label: 'Tenant identity' },
  { key: 'seed', label: 'Seed' },
  { key: 'create_admin', label: 'Create admin' },
  { key: 'verify', label: 'Verify' },
  { key: 'activate', label: 'Activate' },
]

export function stepLabel(step?: string | null): string {
  const found = PROVISIONING_STEPS.find((s) => s.key === step)
  return found ? found.label : (step ?? '—')
}

type StepState = 'completed' | 'running' | 'failed' | 'pending'

function stepState(op: ProvisioningOperation, stepKey: string): { state: StepState; at?: string | null; error?: string | null } {
  const rec = op.steps.find((s) => s.step === stepKey)
  if (rec?.status === 'completed') return { state: 'completed', at: rec.at }
  if (rec?.status === 'failed' || (op.status === 'Failed' && rec)) return { state: 'failed', at: rec?.at, error: rec?.error ?? op.error }
  if (op.status === 'Running' && op.currentStep === stepKey) return { state: 'running', at: rec?.at }
  return { state: 'pending' }
}

const iconSx = { completed: 'success.main', running: 'info.main', failed: 'error.main', pending: 'text.disabled' } as const

export function ProvisioningTimeline({ operation }: { operation: ProvisioningOperation }) {
  return (
    <Box component="ol" sx={{ listStyle: 'none', m: 0, p: 0 }}>
      {PROVISIONING_STEPS.map((s, i) => {
        const { state, at, error } = stepState(operation, s.key)
        const last = i === PROVISIONING_STEPS.length - 1
        return (
          <Box component="li" key={s.key} sx={{ display: 'flex', gap: 1.5, position: 'relative', pb: last ? 0 : 2 }}>
            {!last && <Box sx={{ position: 'absolute', left: 10, top: 24, bottom: 0, width: 2, bgcolor: 'divider' }} />}
            <Box sx={{ width: 22, height: 22, display: 'grid', placeItems: 'center', color: iconSx[state], flexShrink: 0, bgcolor: 'background.paper', zIndex: 1 }}>
              {state === 'completed' && <CheckCircle sx={{ fontSize: 20 }} />}
              {state === 'failed' && <ErrorIcon sx={{ fontSize: 20 }} />}
              {state === 'running' && <CircularProgress size={16} thickness={5} />}
              {state === 'pending' && <RadioButtonUnchecked sx={{ fontSize: 18 }} />}
            </Box>
            <Box sx={{ flex: 1, minWidth: 0, pt: '1px' }}>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 1, flexWrap: 'wrap' }}>
                <Typography variant="body2" sx={{ fontWeight: state === 'pending' ? 400 : 600, color: state === 'pending' ? 'text.disabled' : 'text.primary' }}>
                  {i + 1}. {s.label}
                </Typography>
                {at && (
                  <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>
                    {new Date(at).toLocaleString()}
                  </Typography>
                )}
              </Box>
              {state === 'failed' && error && (
                <Typography variant="caption" color="error" sx={{ display: 'block', mt: 0.25, wordBreak: 'break-word' }}>{error}</Typography>
              )}
            </Box>
          </Box>
        )
      })}
    </Box>
  )
}
