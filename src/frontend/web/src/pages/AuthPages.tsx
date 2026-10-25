import {
  Alert, Avatar, Box, Button, CircularProgress,
  IconButton, InputAdornment, Link as MuiLink, Paper, Stack, TextField, Tooltip, Typography, alpha, useTheme,
} from '@mui/material'
import { Brightness4, Brightness7, BusinessCenter, Dns, Security, Visibility, VisibilityOff } from '@mui/icons-material'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ApiError, authBase, isPlatform, post, setCsrf } from '../api/client'
import { useAuth } from '../auth/AuthProvider'
import { useThemePrefs } from '../theme/tokens'

const home = isPlatform ? '/admin' : '/'
const loginPath = isPlatform ? '/admin/login' : '/login'

const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong.')

function BrandPanel() {
  const theme = useTheme()
  const dark = theme.palette.mode === 'dark'
  const points = [
    { icon: <BusinessCenter sx={{ fontSize: 18 }} />, text: 'Multi-tenant HR platform — one workspace per company' },
    { icon: <Security sx={{ fontSize: 18 }} />, text: 'Isolation, audit and access control built in' },
    { icon: <Dns sx={{ fontSize: 18 }} />, text: 'Modular entitlements enabled per tenant' },
  ]
  return (
    <Box sx={{
      display: { xs: 'none', md: 'flex' }, flexDirection: 'column', justifyContent: 'space-between',
      width: { md: '42%', lg: '38%' }, p: 5,
      bgcolor: dark ? 'background.paper' : 'primary.main',
      background: dark
        ? `linear-gradient(160deg, ${alpha(theme.palette.primary.main, 0.14)} 0%, transparent 55%)`
        : `linear-gradient(160deg, ${theme.palette.primary.dark} 0%, ${theme.palette.primary.main} 100%)`,
      color: dark ? 'text.primary' : 'primary.contrastText',
      borderRight: dark ? 1 : 0, borderColor: 'divider',
    }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
        <Avatar variant="rounded" sx={{ width: 36, height: 36, fontWeight: 800, fontSize: 16, bgcolor: dark ? 'primary.main' : alpha('#000', 0.2), color: '#fff', borderRadius: 1.5 }}>H</Avatar>
        <Typography variant="subtitle1" sx={{ fontWeight: 800, letterSpacing: 0.2 }}>HRMS Platform</Typography>
      </Box>
      <Box>
        <Typography variant="h4" sx={{ fontWeight: 750, mb: 1.5, lineHeight: 1.25 }}>
          {isPlatform ? 'Platform administration' : 'Your workspace'}
        </Typography>
        <Typography variant="body2" sx={{ opacity: 0.85, mb: 4, maxWidth: 340, lineHeight: 1.6 }}>
          {isPlatform
            ? 'Provision tenants, manage subscriptions and modules, and monitor the health of the platform from one console.'
            : 'Sign in to manage your organization.'}
        </Typography>
        <Stack spacing={2}>
          {points.map((p) => (
            <Box key={p.text} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, opacity: 0.9 }}>
              <Avatar variant="rounded" sx={{ width: 32, height: 32, bgcolor: dark ? alpha(theme.palette.primary.main, 0.15) : alpha('#fff', 0.15), color: 'inherit', borderRadius: 1.5 }}>
                {p.icon}
              </Avatar>
              <Typography variant="body2">{p.text}</Typography>
            </Box>
          ))}
        </Stack>
      </Box>
      <Typography variant="caption" sx={{ opacity: 0.6 }}>© HRMS Platform</Typography>
    </Box>
  )
}

function AuthCard({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  const theme = useTheme()
  const prefs = useThemePrefs()
  return (
    <Box sx={{ minHeight: '100vh', display: 'flex', bgcolor: 'background.default' }}>
      <BrandPanel />
      <Box sx={{ flexGrow: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', p: { xs: 2, sm: 4 }, position: 'relative' }}>
        <Tooltip title={theme.palette.mode === 'dark' ? 'Light mode' : 'Dark mode'}>
          <IconButton
            onClick={() => prefs.setMode(theme.palette.mode === 'dark' ? 'light' : 'dark')}
            sx={{ position: 'absolute', top: 16, right: 16, color: 'text.secondary' }} aria-label="Toggle color mode"
          >
            {theme.palette.mode === 'dark' ? <Brightness7 /> : <Brightness4 />}
          </IconButton>
        </Tooltip>
        <Box sx={{ display: { xs: 'flex', md: 'none' }, alignItems: 'center', gap: 1.25, mb: 4 }}>
          <Avatar variant="rounded" sx={{ width: 32, height: 32, fontWeight: 800, fontSize: 15, bgcolor: 'primary.main', borderRadius: 1.5 }}>H</Avatar>
          <Typography variant="subtitle1" sx={{ fontWeight: 800 }}>HRMS Platform</Typography>
        </Box>
        <Paper variant="outlined" sx={{ width: '100%', maxWidth: 400, p: { xs: 3, sm: 4 }, borderRadius: 2.5 }}>
          <Typography variant="h5" sx={{ mb: 0.5 }}>{title}</Typography>
          {subtitle && <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>{subtitle}</Typography>}
          {!subtitle && <Box sx={{ mb: 3 }} />}
          {children}
        </Paper>
      </Box>
    </Box>
  )
}

function PasswordField({ label, value, onChange, autoComplete }: { label: string; value: string; onChange: (v: string) => void; autoComplete?: string }) {
  const [show, setShow] = useState(false)
  return (
    <TextField
      label={label} type={show ? 'text' : 'password'} value={value}
      onChange={(e) => onChange(e.target.value)} required fullWidth autoComplete={autoComplete}
      slotProps={{
        input: {
          endAdornment: (
            <InputAdornment position="end">
              <IconButton onClick={() => setShow(!show)} edge="end" size="small" aria-label={show ? 'Hide password' : 'Show password'}>
                {show ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}
              </IconButton>
            </InputAdornment>
          ),
        },
      }}
    />
  )
}

export function LoginPage() {
  const { refresh } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [mfaToken, setMfaToken] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const r = mfaToken
        ? await post<{ csrfToken: string }>(`${authBase}/mfa`, { mfaToken, code })
        : await post<{ csrfToken?: string; mfaRequired?: boolean; mfaToken?: string }>(`${authBase}/login`, { email, password })
      if ('mfaRequired' in r && r.mfaRequired) {
        setMfaToken(r.mfaToken ?? null)
        return
      }
      setCsrf(r.csrfToken ?? null)
      await refresh()
      navigate(home)
    } catch (err) {
      setError(message(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthCard
      title={mfaToken ? 'Two-factor verification' : isPlatform ? 'Sign in' : 'Sign in'}
      subtitle={mfaToken ? 'Enter the code from your authenticator app.' : isPlatform ? 'Platform administration console' : undefined}
    >
      <Stack component="form" spacing={2.25} onSubmit={submit}>
        {error && <Alert severity="error">{error}</Alert>}
        {mfaToken ? (
          <TextField label="Authentication code" value={code} onChange={(e) => setCode(e.target.value)} autoFocus required fullWidth
            slotProps={{ htmlInput: { inputMode: 'numeric', autoComplete: 'one-time-code' } }} />
        ) : (
          <>
            <TextField label="Email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoFocus required fullWidth autoComplete="email" />
            <PasswordField label="Password" value={password} onChange={setPassword} autoComplete="current-password" />
          </>
        )}
        <Button type="submit" variant="contained" size="large" disabled={busy} sx={{ py: 1.2 }}>
          {busy ? <CircularProgress size={20} color="inherit" /> : mfaToken ? 'Verify' : 'Sign in'}
        </Button>
        {!mfaToken && (
          <Box sx={{ textAlign: 'center' }}>
            <MuiLink component={Link} to={isPlatform ? '/admin/forgot-password' : '/forgot-password'} variant="body2" sx={{ fontWeight: 600 }}>
              Forgot password?
            </MuiLink>
          </Box>
        )}
      </Stack>
    </AuthCard>
  )
}

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    await post(`${authBase}/forgot-password`, { email }).catch(() => undefined)
    setBusy(false)
    setDone(true)
  }
  return (
    <AuthCard title="Reset password" subtitle="We'll email you a reset link if the account exists.">
      {done ? (
        <Stack spacing={2.25}>
          <Alert severity="info">If an account exists for that email, a reset link has been sent.</Alert>
          <MuiLink component={Link} to={loginPath} variant="body2" sx={{ fontWeight: 600, textAlign: 'center' }}>Back to sign in</MuiLink>
        </Stack>
      ) : (
        <Stack component="form" spacing={2.25} onSubmit={submit}>
          <TextField label="Email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required fullWidth autoComplete="email" autoFocus />
          <Button type="submit" variant="contained" size="large" disabled={busy} sx={{ py: 1.2 }}>
            {busy ? <CircularProgress size={20} color="inherit" /> : 'Send reset link'}
          </Button>
          <MuiLink component={Link} to={loginPath} variant="body2" sx={{ fontWeight: 600, textAlign: 'center' }}>Back to sign in</MuiLink>
        </Stack>
      )}
    </AuthCard>
  )
}

/** Used for both invitation acceptance and password reset; the password policy is enforced by the API. */
export function SetPasswordPage({ title }: { title: string }) {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (password !== confirm) return setError('Passwords do not match.')
    setBusy(true)
    try {
      await post(`${authBase}/reset-password`, { token: params.get('token') ?? '', newPassword: password })
      navigate(loginPath)
    } catch (err) {
      setError(message(err))
      setBusy(false)
    }
  }
  return (
    <AuthCard title={title}>
      <Stack component="form" spacing={2.25} onSubmit={submit}>
        {error && <Alert severity="error">{error}</Alert>}
        <PasswordField label="New password" value={password} onChange={setPassword} autoComplete="new-password" />
        <PasswordField label="Confirm password" value={confirm} onChange={setConfirm} autoComplete="new-password" />
        <Button type="submit" variant="contained" size="large" disabled={busy} sx={{ py: 1.2 }}>
          {busy ? <CircularProgress size={20} color="inherit" /> : 'Set password'}
        </Button>
      </Stack>
    </AuthCard>
  )
}
