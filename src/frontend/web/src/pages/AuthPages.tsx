import { Alert, Box, Button, Card, CardContent, Link as MuiLink, Stack, TextField, Typography } from '@mui/material'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ApiError, authBase, isPlatform, post, setCsrf } from '../api/client'
import { useAuth } from '../auth/AuthProvider'

const home = isPlatform ? '/admin' : '/'
const loginPath = isPlatform ? '/admin/login' : '/login'

function AuthCard({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', bgcolor: 'grey.100' }}>
      <Card sx={{ width: 380 }}>
        <CardContent>
          <Typography variant="h5" gutterBottom>{title}</Typography>
          {children}
        </CardContent>
      </Card>
    </Box>
  )
}

const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong.')

export function LoginPage() {
  const { refresh } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [mfaToken, setMfaToken] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
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
    }
  }

  return (
    <AuthCard title={isPlatform ? 'Platform administration' : 'Sign in'}>
      <Stack component="form" spacing={2} onSubmit={submit}>
        {error && <Alert severity="error">{error}</Alert>}
        {mfaToken ? (
          <TextField label="Authentication code" value={code} onChange={(e) => setCode(e.target.value)} autoFocus required />
        ) : (
          <>
            <TextField label="Email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoFocus required />
            <TextField label="Password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          </>
        )}
        <Button type="submit" variant="contained">{mfaToken ? 'Verify' : 'Sign in'}</Button>
        <MuiLink component={Link} to={isPlatform ? '/admin/forgot-password' : '/forgot-password'}>Forgot password?</MuiLink>
      </Stack>
    </AuthCard>
  )
}

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [done, setDone] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    await post(`${authBase}/forgot-password`, { email }).catch(() => undefined)
    setDone(true)
  }
  return (
    <AuthCard title="Reset password">
      {done ? (
        <Alert severity="info">If an account exists for that email, a reset link has been sent.</Alert>
      ) : (
        <Stack component="form" spacing={2} onSubmit={submit}>
          <TextField label="Email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
          <Button type="submit" variant="contained">Send reset link</Button>
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
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (password !== confirm) return setError('Passwords do not match.')
    try {
      await post(`${authBase}/reset-password`, { token: params.get('token') ?? '', newPassword: password })
      navigate(loginPath)
    } catch (err) {
      setError(message(err))
    }
  }
  return (
    <AuthCard title={title}>
      <Stack component="form" spacing={2} onSubmit={submit}>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField label="New password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        <TextField label="Confirm password" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} required />
        <Button type="submit" variant="contained">Set password</Button>
      </Stack>
    </AuthCard>
  )
}
