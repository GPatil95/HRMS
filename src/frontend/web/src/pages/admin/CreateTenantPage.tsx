import { Alert, Box, Button, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { platformApi } from '../../api/platform'
import { PageHeader } from '../../components/ui'

export function CreateTenantPage() {
  const plans = useQuery({ queryKey: ['plans'], queryFn: platformApi.plans })
  const [form, setForm] = useState({ code: '', name: '', adminName: '', adminEmail: '', planCode: '' })
  const [created, setCreated] = useState<{ tenantId: string; operationId: string } | null>(null)
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }))

  const create = useMutation({
    mutationFn: () => platformApi.createTenant({ ...form, code: form.code.trim().toLowerCase(), planCode: form.planCode || undefined }),
    onSuccess: setCreated,
  })

  const submit = (e: FormEvent) => { e.preventDefault(); create.mutate() }
  const valid = /^[a-z0-9-]+$/.test(form.code) && form.name && /.+@.+\..+/.test(form.adminEmail) && form.adminName

  if (created) {
    return (
      <Box>
        <PageHeader title="Tenant created" />
        <Alert severity="success" sx={{ mb: 2, maxWidth: 640 }}>
          Provisioning has started in the background. The admin user will receive an invitation email once the tenant database is ready.
        </Alert>
        <Stack direction="row" spacing={1}>
          <Button component={Link} to={`/admin/tenants/${created.tenantId}`} variant="contained">Open tenant</Button>
          <Button component={Link} to="/admin/operations" variant="outlined">Watch provisioning</Button>
          <Button variant="text" onClick={() => { setCreated(null); setForm({ code: '', name: '', adminName: '', adminEmail: '', planCode: '' }) }}>Create another</Button>
        </Stack>
      </Box>
    )
  }

  return (
    <Box>
      <PageHeader title="Create tenant" subtitle="Provisions a dedicated PostgreSQL database and sends the tenant admin an invitation" />
      <Paper variant="outlined" sx={{ p: 3, maxWidth: 560 }}>
        {create.error && <Alert severity="error" sx={{ mb: 2 }}>{create.error instanceof ApiError ? create.error.message : 'Creation failed'}</Alert>}
        <Stack component="form" spacing={2.5} onSubmit={submit}>
          <TextField label="Tenant code" value={form.code} onChange={set('code')} required autoFocus
            helperText="Lowercase letters, digits and hyphens — used in the tenant's hostname and database name" slotProps={{ htmlInput: { pattern: '[a-z0-9-]+' } }} />
          <TextField label="Company name" value={form.name} onChange={set('name')} required />
          <TextField label="Admin name" value={form.adminName} onChange={set('adminName')} required />
          <TextField label="Admin email" type="email" value={form.adminEmail} onChange={set('adminEmail')} required
            helperText="Receives the invitation to set a password" />
          <TextField select label="Plan" value={form.planCode} onChange={set('planCode')}
            helperText={plans.error ? 'Plans could not be loaded — the platform default will be used' : 'Optional — defaults to the platform default plan'}>
            <MenuItem value="">Platform default</MenuItem>
            {(plans.data ?? []).map((p) => <MenuItem key={p.code} value={p.code}>{p.name} ({p.code})</MenuItem>)}
          </TextField>
          <Typography variant="caption" color="text.secondary">
            The tenant's database, login and data are created by a retryable background job — nothing is shared between tenants.
          </Typography>
          <Button type="submit" variant="contained" disabled={!valid || create.isPending}>
            {create.isPending ? 'Creating…' : 'Create tenant'}
          </Button>
        </Stack>
      </Paper>
    </Box>
  )
}
