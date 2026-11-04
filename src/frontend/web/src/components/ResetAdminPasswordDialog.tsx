import { ContentCopy, Visibility, VisibilityOff } from '@mui/icons-material'
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  IconButton,
  InputAdornment,
  LinearProgress,
  Radio,
  RadioGroup,
  TextField,
  Typography,
} from '@mui/material'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError } from '../api/client'
import { platformApi } from '../api/platform'
import { generatePassword, passwordPolicyError, passwordStrength } from '../utils/passwords'
import { useToast } from './toast'

/** "Reset Admin Password" dialog — email a reset link, or set a temporary password directly
 * (hashed server-side; all admin sessions are revoked). */
export function ResetAdminPasswordDialog({ open, tenantId, adminEmail, onClose }: {
  open: boolean
  tenantId: string
  adminEmail?: string | null
  onClose: () => void
}) {
  const toast = useToast()
  const [mode, setMode] = useState<'email' | 'set'>('email')
  const [pw, setPw] = useState('')
  const [pw2, setPw2] = useState('')
  const [show, setShow] = useState(false)
  const [done, setDone] = useState<null | { password?: string }>(null)

  const mut = useMutation({
    mutationFn: () => platformApi.resetAdminPassword(tenantId, mode === 'set' ? pw : undefined),
    onSuccess: () => setDone(mode === 'set' ? { password: pw } : {}),
    onError: (e) => toast(e instanceof ApiError ? e.message : 'Reset failed', 'error'),
  })

  const pwErr = pw ? passwordPolicyError(pw) : null
  const mismatch = pw2.length > 0 && pw2 !== pw
  const canSubmit = mode === 'email' || (!!pw && !pwErr && pw === pw2)
  const strength = passwordStrength(pw)

  const close = () => { setDone(null); setMode('email'); setPw(''); setPw2(''); setShow(false); onClose() }

  return (
    <Dialog open={open} onClose={mut.isPending ? undefined : close} maxWidth="xs" fullWidth>
      <DialogTitle sx={{ fontWeight: 600 }}>Reset Admin Password</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2 }}>
        {done ? (
          <>
            <Alert severity="success">
              {done.password ? 'Password updated — all admin sessions were signed out.' : `Reset email sent to ${adminEmail}.`}
            </Alert>
            {done.password && (
              <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
                <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>{done.password}</Typography>
                <IconButton size="small" onClick={() => void navigator.clipboard.writeText(done.password!)} aria-label="Copy password"><ContentCopy fontSize="small" /></IconButton>
              </Box>
            )}
            {done.password && <Alert severity="warning" variant="outlined">Shown only here — it is not stored anywhere.</Alert>}
          </>
        ) : (
          <>
            <Typography variant="body2" color="text.secondary">
              Choose how to reset the credentials for <Box component="span" sx={{ fontFamily: 'monospace' }}>{adminEmail ?? 'the tenant admin'}</Box>.
            </Typography>
            <RadioGroup value={mode} onChange={(e) => { setMode(e.target.value as 'email' | 'set'); setDone(null) }}>
              <FormControlLabel value="email" control={<Radio size="small" />} label="Email a password-reset link" />
              <FormControlLabel value="set" control={<Radio size="small" />} label="Set a temporary password now" />
            </RadioGroup>
            {mode === 'set' && (
              <>
                <TextField
                  label="New password" type={show ? 'text' : 'password'} value={pw} required
                  onChange={(e) => setPw(e.target.value)}
                  error={!!pwErr}
                  helperText={pwErr ?? ' '}
                  slotProps={{
                    input: {
                      endAdornment: (
                        <InputAdornment position="end">
                          <Button size="small" onClick={() => { const p = generatePassword(); setPw(p); setPw2(p) }}>Generate</Button>
                          <IconButton size="small" aria-label="Toggle password visibility" onClick={() => setShow((s) => !s)}>
                            {show ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}
                          </IconButton>
                        </InputAdornment>
                      ),
                    },
                  }}
                />
                <TextField
                  label="Confirm password" type={show ? 'text' : 'password'} value={pw2} required
                  onChange={(e) => setPw2(e.target.value)}
                  error={mismatch}
                  helperText={mismatch ? 'Passwords do not match' : ' '}
                />
                <Box>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                    <Typography variant="caption" color="text.secondary">Password strength</Typography>
                    <Typography variant="caption" color="text.secondary">{pw ? strength.label : ''}</Typography>
                  </Box>
                  <LinearProgress variant="determinate" value={(strength.score / 4) * 100}
                    color={strength.score >= 3 ? 'success' : strength.score >= 2 ? 'warning' : 'error'} sx={{ height: 6, borderRadius: 3 }} />
                  <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
                    Requirements: 12+ characters, at least one letter and one digit. Avoid common passwords.
                  </Typography>
                </Box>
              </>
            )}
            <Alert severity="warning" variant="outlined">
              Resetting the password will revoke the administrator's existing sessions.
            </Alert>
          </>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        {done
          ? <Button onClick={close}>Done</Button>
          : (
            <>
              <Button onClick={close} disabled={mut.isPending}>Cancel</Button>
              <Button variant="contained" disabled={!canSubmit || mut.isPending} onClick={() => mut.mutate()}>
                {mut.isPending ? 'Working…' : mode === 'email' ? 'Send reset email' : 'Reset Password'}
              </Button>
            </>
          )}
      </DialogActions>
    </Dialog>
  )
}
