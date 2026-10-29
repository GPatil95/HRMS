import { ArrowBack, CheckCircle, ContentCopy, Email, Key, Launch, Visibility, VisibilityOff } from '@mui/icons-material'
import {
  Alert,
  Box,
  Button,
  Card,
  CardActionArea,
  IconButton,
  InputAdornment,
  Paper,
  Stack,
  Step,
  StepLabel,
  Stepper,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { platformApi } from '../../api/platform'
import { PageHeader, SectionCard } from '../../components/ui'
import { generatePassword, passwordPolicyError } from '../../utils/passwords'

const CODE_RE = /^[a-z0-9-]+$/
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const STEPS = ['Tenant details', 'Administrator', 'Plan', 'Review', 'Provision']

export function CreateTenantPage() {
  const theme = useTheme()
  const plans = useQuery({ queryKey: ['plans'], queryFn: platformApi.plans })
  const [step, setStep] = useState(0)
  const [form, setForm] = useState({ code: '', name: '', adminName: '', adminEmail: '', planCode: '' })
  const [credentialMode, setCredentialMode] = useState<'invite' | 'password'>('invite')
  const [adminPassword, setAdminPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [touched, setTouched] = useState<Record<string, boolean>>({})
  const [created, setCreated] = useState<{ tenantId: string; operationId: string } | null>(null)
  const tenantDetail = useQuery({
    queryKey: ['tenant', created?.tenantId],
    queryFn: () => platformApi.tenant(created!.tenantId),
    enabled: !!created,
  })
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }))
  const blur = (k: string) => () => setTouched((t) => ({ ...t, [k]: true }))

  const errors = useMemo(() => ({
    code: !form.code ? 'Required' : !CODE_RE.test(form.code) ? 'Lowercase letters, digits and hyphens only' : '',
    name: !form.name.trim() ? 'Required' : '',
    adminName: !form.adminName.trim() ? 'Required' : '',
    adminEmail: !form.adminEmail ? 'Required' : !EMAIL_RE.test(form.adminEmail) ? 'Enter a valid email address' : '',
    adminPassword: credentialMode === 'password' ? (passwordPolicyError(adminPassword) ?? '') : '',
    confirmPassword: credentialMode === 'password' && confirmPassword !== adminPassword ? 'Passwords do not match' : '',
  }), [form, credentialMode, adminPassword, confirmPassword])

  const stepValid = [
    !errors.code && !errors.name,
    !errors.adminName && !errors.adminEmail && !errors.adminPassword && !errors.confirmPassword,
    true,
    true,
    true,
  ]
  const canNext = stepValid[step]

  const create = useMutation({
    mutationFn: () => platformApi.createTenant({
      code: form.code.trim().toLowerCase(), name: form.name.trim(), adminName: form.adminName.trim(),
      adminEmail: form.adminEmail.trim(), planCode: form.planCode || undefined,
      adminPassword: credentialMode === 'password' ? adminPassword : undefined,
    }),
    onSuccess: (r) => { setCreated(r); setStep(4) },
  })

  const touchAll = (keys: string[]) => setTouched((t) => ({ ...t, ...Object.fromEntries(keys.map((k) => [k, true])) }))
  const next = () => {
    if (step === 0 && !canNext) { touchAll(['code', 'name']); return }
    if (step === 1 && !canNext) { touchAll(['adminName', 'adminEmail', 'adminPassword', 'confirmPassword']); return }
    setStep((s) => Math.min(s + 1, STEPS.length - 1))
  }
  const back = () => setStep((s) => Math.max(0, s - 1))

  /* ── Step 5: success ── */
  if (created && step === 4) {
    const host = tenantDetail.data?.tenant.primaryHost
    const tenantUrl = host ? `${window.location.protocol}//${host}${window.location.port ? `:${window.location.port}` : ''}` : null
    return (
      <Box sx={{ maxWidth: 640, mx: 'auto', mt: { xs: 4, md: 8 } }}>
        <Box sx={{ textAlign: 'center', mb: 3 }}>
          <CheckCircle color="success" sx={{ fontSize: 56, mb: 2 }} />
          <Typography variant="h5" sx={{ fontWeight: 700, mb: 1 }}>Tenant created successfully</Typography>
          <Typography color="text.secondary">
            Provisioning has started in the background.
            {credentialMode === 'invite' && ' The tenant admin will receive an invitation email once the database is ready.'}
          </Typography>
        </Box>
        <SectionCard title="Admin credentials" subtitle="Share with the tenant administrator over a secure channel">
          <Stack spacing={1.5} sx={{ p: 3 }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2" color="text.secondary">Tenant</Typography>
              <Typography variant="body2">{form.name.trim()}</Typography>
            </Box>
            {tenantUrl && (
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
                <Typography variant="body2" color="text.secondary">Tenant URL</Typography>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                  <Typography variant="body2" component="a" href={tenantUrl} target="_blank" rel="noreferrer"
                    sx={{ fontFamily: 'monospace', color: 'primary.main', textDecoration: 'none' }}>{tenantUrl}</Typography>
                  <IconButton size="small" aria-label="Copy tenant URL" onClick={() => void navigator.clipboard.writeText(tenantUrl)}><ContentCopy fontSize="small" /></IconButton>
                </Box>
              </Box>
            )}
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2" color="text.secondary">Admin email</Typography>
              <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>{form.adminEmail.trim()}</Typography>
            </Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2" color="text.secondary">Status</Typography>
              <Typography variant="body2" color="text.secondary">{tenantDetail.data?.tenant.status ?? 'Provisioning…'}</Typography>
            </Box>
            {credentialMode === 'password' && (
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
                <Typography variant="body2" color="text.secondary">Temporary password</Typography>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                  <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>{adminPassword}</Typography>
                  <IconButton size="small" aria-label="Copy password" onClick={() => void navigator.clipboard.writeText(adminPassword)}><ContentCopy fontSize="small" /></IconButton>
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
          {tenantUrl && <Button component="a" href={tenantUrl} target="_blank" rel="noreferrer" variant="contained" endIcon={<Launch />}>Open Tenant</Button>}
          <Button component={Link} to={`/admin/tenants/${created.tenantId}`} variant={tenantUrl ? 'outlined' : 'contained'}>View details</Button>
          <Button variant="text" onClick={() => { setCreated(null); setStep(0); setForm({ code: '', name: '', adminName: '', adminEmail: '', planCode: '' }); setAdminPassword(''); setConfirmPassword(''); setCredentialMode('invite'); setTouched({}) }}>
            Create another
          </Button>
        </Stack>
      </Box>
    )
  }

  const fieldError = (k: keyof typeof errors) => (touched[k] ? errors[k] || ' ' : ' ')
  const planName = plans.data?.find((p) => p.code === form.planCode)?.name

  return (
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Button component={Link} to="/admin/tenants" startIcon={<ArrowBack />} size="small" sx={{ mb: 1.5, color: 'text.secondary' }}>All tenants</Button>
      <PageHeader title="Create tenant" subtitle="Provisions a dedicated PostgreSQL database and the tenant admin account" />

      <Stepper activeStep={step} alternativeLabel sx={{ mb: 3.5 }}>
        {STEPS.map((s) => <Step key={s}><StepLabel>{s}</StepLabel></Step>)}
      </Stepper>

      {create.error && (
        <Alert severity="error" sx={{ mb: 2.5 }}>
          {create.error instanceof ApiError ? create.error.message : 'Creation failed'}
        </Alert>
      )}

      {/* Step 1 — Tenant details */}
      {step === 0 && (
        <SectionCard title="Tenant details" subtitle="How the tenant is identified on the platform">
          <Stack spacing={2.5} sx={{ p: 3 }}>
            <TextField label="Tenant code" value={form.code} onChange={set('code')} onBlur={blur('code')} required autoFocus
              error={touched.code && !!errors.code} helperText={touched.code ? errors.code || ' ' : 'Used in the hostname and database name — e.g. acme'}
              slotProps={{ htmlInput: { pattern: '[a-z0-9-]+' } }} />
            <TextField label="Company name" value={form.name} onChange={set('name')} onBlur={blur('name')} required
              error={touched.name && !!errors.name} helperText={fieldError('name')}
              placeholder="Acme Manufacturing Pvt. Ltd." />
            <TextField label="Domain / subdomain" value={form.code ? `${form.code}.localhost` : ''} disabled
              helperText="Derived automatically from the tenant code" slotProps={{ htmlInput: { readOnly: true } }} />
          </Stack>
        </SectionCard>
      )}

      {/* Step 2 — Administrator */}
      {step === 1 && (
        <SectionCard title="Administrator" subtitle="The first user — the email is the login username">
          <Stack spacing={2.5} sx={{ p: 3 }}>
            <TextField label="Admin name" value={form.adminName} onChange={set('adminName')} onBlur={blur('adminName')} required
              error={touched.adminName && !!errors.adminName} helperText={fieldError('adminName')} />
            <TextField label="Admin email" type="email" value={form.adminEmail} onChange={set('adminEmail')} onBlur={blur('adminEmail')} required
              error={touched.adminEmail && !!errors.adminEmail} helperText={touched.adminEmail ? errors.adminEmail || ' ' : 'This is the username the admin signs in with'} />
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>
              {([
                { mode: 'password' as const, title: 'Set temporary password now', desc: 'You generate or type a password and share it securely.', icon: <Key /> },
                { mode: 'invite' as const, title: 'Send invitation', desc: 'The administrator creates their own password from an email link.', icon: <Email /> },
              ]).map((o) => (
                <Card key={o.mode} variant="outlined"
                  sx={{ borderColor: credentialMode === o.mode ? 'primary.main' : 'divider', bgcolor: credentialMode === o.mode ? alpha(theme.palette.primary.main, 0.05) : 'transparent' }}>
                  <CardActionArea onClick={() => setCredentialMode(o.mode)} sx={{ p: 2 }}>
                    <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start' }}>
                      <Box sx={{ color: credentialMode === o.mode ? 'primary.main' : 'text.secondary', mt: 0.25 }}>{o.icon}</Box>
                      <Box>
                        <Typography variant="body2" sx={{ fontWeight: 600 }}>{o.title}</Typography>
                        <Typography variant="caption" color="text.secondary">{o.desc}</Typography>
                      </Box>
                    </Box>
                  </CardActionArea>
                </Card>
              ))}
            </Box>
            {credentialMode === 'password' && (
              <>
                <TextField label="Temporary password" type={showPassword ? 'text' : 'password'} value={adminPassword}
                  onChange={(e) => setAdminPassword(e.target.value)} onBlur={blur('adminPassword')} required
                  error={touched.adminPassword && !!errors.adminPassword}
                  helperText={fieldError('adminPassword') === ' ' ? 'Shown once after creation — stored only as a hash' : errors.adminPassword}
                  slotProps={{
                    input: {
                      endAdornment: (
                        <InputAdornment position="end">
                          <Button size="small" onClick={() => { const p = generatePassword(); setAdminPassword(p); setConfirmPassword(p) }}>Generate</Button>
                          <IconButton size="small" aria-label="Toggle password visibility" onClick={() => setShowPassword((s) => !s)}>{showPassword ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}</IconButton>
                        </InputAdornment>
                      ),
                    },
                  }} />
                <TextField label="Confirm password" type={showPassword ? 'text' : 'password'} value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)} onBlur={blur('confirmPassword')} required
                  error={touched.confirmPassword && !!errors.confirmPassword} helperText={fieldError('confirmPassword')} />
              </>
            )}
            {credentialMode === 'invite' && (
              <Alert severity="info" variant="outlined">The admin gets an email with a secure link to set their own password (Mailpit at :8025 in dev).</Alert>
            )}
          </Stack>
        </SectionCard>
      )}

      {/* Step 3 — Plan */}
      {step === 2 && (
        <SectionCard title="Plan" subtitle="Optional — defaults to the platform default plan">
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2, p: 3 }}>
            <Card variant="outlined"
              sx={{ borderColor: !form.planCode ? 'primary.main' : 'divider', bgcolor: !form.planCode ? alpha(theme.palette.primary.main, 0.05) : 'transparent' }}>
              <CardActionArea onClick={() => setForm((f) => ({ ...f, planCode: '' }))} sx={{ p: 2 }}>
                <Typography variant="body2" sx={{ fontWeight: 600 }}>Platform default</Typography>
                <Typography variant="caption" color="text.secondary">Core modules, no explicit plan</Typography>
              </CardActionArea>
            </Card>
            {(plans.data ?? []).map((p) => (
              <Card key={p.code} variant="outlined"
                sx={{ borderColor: form.planCode === p.code ? 'primary.main' : 'divider', bgcolor: form.planCode === p.code ? alpha(theme.palette.primary.main, 0.05) : 'transparent' }}>
                <CardActionArea onClick={() => setForm((f) => ({ ...f, planCode: p.code }))} sx={{ p: 2 }}>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>{p.name}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {p.code}{p.employeeLimit ? ` · up to ${p.employeeLimit} employees` : ''}
                  </Typography>
                </CardActionArea>
              </Card>
            ))}
          </Box>
          {plans.error && <Alert severity="warning" sx={{ mx: 3, mb: 2 }}>Plans could not be loaded — the platform default will be used.</Alert>}
        </SectionCard>
      )}

      {/* Step 4 — Review */}
      {step === 3 && (
        <SectionCard title="Review" subtitle="Confirm the tenant configuration before provisioning starts">
          <Stack spacing={0} sx={{ px: 3, pb: 1.5 }}>
            {([
              ['Tenant', `${form.name.trim()} (${form.code.trim().toLowerCase()})`],
              ['Domain', `${form.code.trim().toLowerCase() || '—'}.localhost`],
              ['Administrator', `${form.adminName.trim()} · ${form.adminEmail.trim()}`],
              ['Credentials', credentialMode === 'password' ? 'Temporary password set — shown once after creation' : 'Invitation email — admin sets their own password'],
              ['Plan', planName ? `${planName} (${form.planCode})` : 'Platform default'],
            ]).map(([k, v], i) => (
              <Box key={k} sx={{ display: 'flex', justifyContent: 'space-between', gap: 2, py: 1.25, borderBottom: i < 4 ? 1 : 0, borderColor: 'divider' }}>
                <Typography variant="body2" color="text.secondary">{k}</Typography>
                <Typography variant="body2" sx={{ textAlign: 'right' }}>{v}</Typography>
              </Box>
            ))}
          </Stack>
          <Box sx={{ px: 3, pb: 2 }}>
            <Typography variant="caption" color="text.secondary">
              The tenant's database, login and data are created by a retryable background job — nothing is shared between tenants.
            </Typography>
          </Box>
        </SectionCard>
      )}

      {/* Step 5 — Provision (submitting) */}
      {step === 4 && !created && (
        <Paper variant="outlined" sx={{ p: 5, textAlign: 'center' }}>
          <Typography variant="h6" sx={{ mb: 1 }}>{create.isPending ? 'Creating tenant…' : 'Ready to provision'}</Typography>
          <Typography variant="body2" color="text.secondary">
            {create.isPending ? 'Submitting the provisioning request.' : 'Press Provision to create the tenant database and admin account.'}
          </Typography>
        </Paper>
      )}

      {/* Footer nav */}
      {!(step === 4 && created) && (
        <Box sx={{ display: 'flex', justifyContent: 'space-between', mt: 3, gap: 2 }}>
          <Button onClick={back} disabled={step === 0} startIcon={<ArrowBack />}>Back</Button>
          {step < 3
            ? <Button variant="contained" onClick={next} disabled={!canNext}>Continue</Button>
            : step === 3
              ? <Button variant="contained" size="large" onClick={() => create.mutate()} disabled={create.isPending} sx={{ px: 4 }}>
                  {create.isPending ? 'Creating…' : 'Provision tenant'}
                </Button>
              : null}
        </Box>
      )}
    </Box>
  )
}
