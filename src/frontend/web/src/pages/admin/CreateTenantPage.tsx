import { ArrowBack, CheckCircle, ContentCopy, Launch, Visibility, VisibilityOff } from '@mui/icons-material'
import {
  Alert,
  Box,
  Button,
  Divider,
  FormControlLabel,
  IconButton,
  InputAdornment,
  MenuItem,
  Radio,
  RadioGroup,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useMemo, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { platformApi } from '../../api/platform'
import { PageHeader, SectionCard } from '../../components/ui'

const CODE_RE = /^[a-z0-9-]+$/
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

function generatePassword(): string {
  const letters = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ'
  const all = letters + '23456789'
  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)
  const chars = Array.from(bytes, (b) => all[b % all.length])
  chars[0] = letters[bytes[0] % letters.length] // letter first
  chars[15] = String(2 + (bytes[15] % 8)) // digit last
  return chars.join('')
}

export function CreateTenantPage() {
  const plans = useQuery({ queryKey: ['plans'], queryFn: platformApi.plans })
  const [form, setForm] = useState({ code: '', name: '', adminName: '', adminEmail: '', planCode: '' })
  const [credentialMode, setCredentialMode] = useState<'invite' | 'password'>('invite')
  const [adminPassword, setAdminPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [touched, setTouched] = useState<Record<string, boolean>>({})
  const [created, setCreated] = useState<{ tenantId: string; operationId: string; host?: string } | null>(null)
  const tenantDetail = useQuery({
    queryKey: ['tenant', created?.tenantId],
    queryFn: () => platformApi.tenant(created!.tenantId),
    enabled: !!created,
  })
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }))
  const blur = (k: keyof typeof form) => () => setTouched((t) => ({ ...t, [k]: true }))

  const errors = useMemo(() => ({
    code: !form.code ? 'Required' : !CODE_RE.test(form.code) ? 'Lowercase letters, digits and hyphens only' : '',
    name: !form.name.trim() ? 'Required' : '',
    adminName: !form.adminName.trim() ? 'Required' : '',
    adminEmail: !form.adminEmail ? 'Required' : !EMAIL_RE.test(form.adminEmail) ? 'Enter a valid email address' : '',
    adminPassword: credentialMode === 'password' && adminPassword.length < 12 ? 'At least 12 characters, with a letter and a digit' : '',
  }), [form, credentialMode, adminPassword])
  const valid = Object.values(errors).every((e) => !e)

  const create = useMutation({
    mutationFn: () => platformApi.createTenant({
      code: form.code.trim().toLowerCase(), name: form.name.trim(), adminName: form.adminName.trim(),
      adminEmail: form.adminEmail.trim(), planCode: form.planCode || undefined,
      adminPassword: credentialMode === 'password' ? adminPassword : undefined,
    }),
    onSuccess: (r) => setCreated(r),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setTouched({ code: true, name: true, adminName: true, adminEmail: true, adminPassword: true })
    if (valid) create.mutate()
  }

  if (created) {
    const host = tenantDetail.data?.tenant.primaryHost
    const tenantUrl = host ? `${window.location.protocol}//${host}${window.location.port ? `:${window.location.port}` : ''}` : null
    return (
      <Box sx={{ maxWidth: 640, mx: 'auto', mt: { xs: 4, md: 8 } }}>
        <Box sx={{ textAlign: 'center', mb: 3 }}>
          <CheckCircle color="success" sx={{ fontSize: 56, mb: 2 }} />
          <Typography variant="h5" sx={{ fontWeight: 700, mb: 1 }}>Tenant created</Typography>
          <Typography color="text.secondary">
            Provisioning has started in the background.
            {credentialMode === 'invite' && ' The tenant admin will receive an invitation email once the database is ready.'}
          </Typography>
        </Box>
        <SectionCard title="Admin credentials" subtitle="Share with the tenant administrator over a secure channel">
          <Stack spacing={1.5} sx={{ p: 3 }}>
            {tenantUrl && (
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
                <Typography variant="body2" color="text.secondary">Tenant URL</Typography>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                  <Typography variant="body2" component="a" href={tenantUrl} target="_blank" rel="noreferrer"
                    sx={{ fontFamily: 'monospace', color: 'primary.main', textDecoration: 'none' }}>{tenantUrl}</Typography>
                  <IconButton size="small" onClick={() => void navigator.clipboard.writeText(tenantUrl)}><ContentCopy fontSize="small" /></IconButton>
                </Box>
              </Box>
            )}
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2" color="text.secondary">Admin username</Typography>
              <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>{form.adminEmail.trim()}</Typography>
            </Box>
            {credentialMode === 'password' && (
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
                <Typography variant="body2" color="text.secondary">Temporary password</Typography>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                  <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>{adminPassword}</Typography>
                  <IconButton size="small" onClick={() => void navigator.clipboard.writeText(adminPassword)}><ContentCopy fontSize="small" /></IconButton>
                </Box>
              </Box>
            )}
            {credentialMode === 'password' && (
              <Alert severity="warning" variant="outlined">This password is shown only here — it is not stored anywhere. The admin can change it after signing in.</Alert>
            )}
            {credentialMode === 'invite' && (
              <Alert severity="info" variant="outlined">No password exists yet — the admin sets their own via the emailed invitation link (check Mailpit in dev).</Alert>
            )}
          </Stack>
        </SectionCard>
        <Stack direction="row" spacing={1.5} sx={{ justifyContent: 'center', flexWrap: 'wrap', mt: 3 }}>
          <Button component={Link} to={`/admin/tenants/${created.tenantId}`} variant="contained" endIcon={<Launch />}>Open tenant</Button>
          <Button component={Link} to="/admin/operations" variant="outlined">Watch provisioning</Button>
          <Button variant="text" onClick={() => { setCreated(null); setForm({ code: '', name: '', adminName: '', adminEmail: '', planCode: '' }); setAdminPassword(''); setCredentialMode('invite'); setTouched({}) }}>
            Create another
          </Button>
        </Stack>
      </Box>
    )
  }

  const fieldError = (k: keyof typeof errors) => touched[k] ? errors[k] || ' ' : ' '

  return (
    <Box sx={{ maxWidth: 680, mx: 'auto' }}>
      <Button component={Link} to="/admin/tenants" startIcon={<ArrowBack />} size="small" sx={{ mb: 1.5, color: 'text.secondary' }}>All tenants</Button>
      <PageHeader title="Create tenant" subtitle="Provisions a dedicated PostgreSQL database and the tenant admin account" />
      {create.error && (
        <Alert severity="error" sx={{ mb: 2.5 }}>
          {create.error instanceof ApiError ? create.error.message : 'Creation failed'}
        </Alert>
      )}
      <Box component="form" onSubmit={submit}>
        <SectionCard title="Tenant" subtitle="How the tenant is identified on the platform" sx={{ mb: 2.5 }}>
          <Stack spacing={2.5} sx={{ p: 3 }}>
            <TextField label="Tenant code" value={form.code} onChange={set('code')} onBlur={blur('code')} required autoFocus
              error={touched.code && !!errors.code} helperText={touched.code ? errors.code || ' ' : 'Used in the hostname and database name — e.g. acme'}
              slotProps={{ htmlInput: { pattern: '[a-z0-9-]+' } }} />
            <TextField label="Company name" value={form.name} onChange={set('name')} onBlur={blur('name')} required
              error={touched.name && !!errors.name} helperText={fieldError('name')}
              placeholder="Acme Manufacturing Pvt. Ltd." />
          </Stack>
        </SectionCard>
        <SectionCard title="Tenant administrator" subtitle="The first user — the email is the login username" sx={{ mb: 2.5 }}>
          <Stack spacing={2.5} sx={{ p: 3 }}>
            <TextField label="Admin name" value={form.adminName} onChange={set('adminName')} onBlur={blur('adminName')} required
              error={touched.adminName && !!errors.adminName} helperText={fieldError('adminName')} />
            <TextField label="Admin email" type="email" value={form.adminEmail} onChange={set('adminEmail')} onBlur={blur('adminEmail')} required
              error={touched.adminEmail && !!errors.adminEmail} helperText={touched.adminEmail ? errors.adminEmail || ' ' : 'This is the username the admin signs in with'} />
            <RadioGroup row value={credentialMode} onChange={(e) => setCredentialMode(e.target.value as 'invite' | 'password')}>
              <FormControlLabel value="invite" control={<Radio size="small" />} label="Send invitation email" />
              <FormControlLabel value="password" control={<Radio size="small" />} label="Set temporary password" />
            </RadioGroup>
            {credentialMode === 'invite'
              ? <Alert severity="info" variant="outlined">The admin gets an email with a secure link to set their own password (Mailpit at :8025 in dev).</Alert>
              : (
                <TextField label="Temporary password" type={showPassword ? 'text' : 'password'} value={adminPassword}
                  onChange={(e) => setAdminPassword(e.target.value)} onBlur={() => setTouched((t) => ({ ...t, adminPassword: true }))} required
                  error={touched.adminPassword && !!errors.adminPassword}
                  helperText={fieldError('adminPassword') === ' ' ? 'Shown once after creation — stored only as a hash' : errors.adminPassword}
                  slotProps={{
                    input: {
                      endAdornment: (
                        <InputAdornment position="end">
                          <Button size="small" onClick={() => setAdminPassword(generatePassword())}>Generate</Button>
                          <IconButton size="small" onClick={() => setShowPassword((s) => !s)}>{showPassword ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}</IconButton>
                        </InputAdornment>
                      ),
                    },
                  }} />
              )}
          </Stack>
        </SectionCard>
        <SectionCard title="Plan" subtitle="Optional — defaults to the platform default plan" sx={{ mb: 3 }}>
          <Stack spacing={2.5} sx={{ p: 3 }}>
            <TextField select label="Plan" value={form.planCode} onChange={set('planCode')}
              helperText={plans.error ? 'Plans could not be loaded — the platform default will be used' : ' '}>
              <MenuItem value="">Platform default</MenuItem>
              {(plans.data ?? []).map((p) => <MenuItem key={p.code} value={p.code}>{p.name} ({p.code})</MenuItem>)}
            </TextField>
          </Stack>
        </SectionCard>
        <Divider sx={{ mb: 3 }} />
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 2 }}>
          <Typography variant="caption" color="text.secondary" sx={{ maxWidth: 420 }}>
            The tenant's database, login and data are created by a retryable background job — nothing is shared between tenants.
          </Typography>
          <Button type="submit" variant="contained" size="large" disabled={create.isPending} sx={{ px: 4 }}>
            {create.isPending ? 'Creating…' : 'Create tenant'}
          </Button>
        </Box>
      </Box>
    </Box>
  )
}
