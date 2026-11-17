import { useState, type ReactNode } from 'react'
import {
  Avatar, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Divider,
  Drawer, FormControl, FormControlLabel, InputLabel, List, ListItemButton, ListItemText,
  MenuItem, Select, Switch, TextField, Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { tenantApi, type Role, type UserDetail, type UserSummary } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { DataList } from '../../components/tenant'
import { useToast } from '../../components/toast'

function DetailRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2, py: 0.75 }}>
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      <Typography variant="body2" sx={{ fontWeight: 500, textAlign: 'right' }}>{children}</Typography>
    </Box>
  )
}

function UserDrawer({ user, onClose }: { user: UserSummary | null; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const detail = useQuery({ queryKey: ['users', user?.id, 'detail'], queryFn: () => tenantApi.user(user!.id), enabled: !!user })
  const effPerms = useQuery({ queryKey: ['users', user?.id, 'eff'], queryFn: () => tenantApi.effectivePermissions(user!.id), enabled: !!user })
  const roles = useQuery({ queryKey: ['roles'], queryFn: tenantApi.roles, enabled: !!user })
  const d: UserDetail | undefined = detail.data

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ['users'] })
    void qc.invalidateQueries({ queryKey: ['users', user?.id, 'detail'] })
  }
  const status = useMutation({
    mutationFn: (next: string) => tenantApi.setUserStatus(user!.id, next, `Status changed by admin`),
    onSuccess: () => { toast('Status updated'); invalidate() },
    onError: (e) => toast(e instanceof Error ? e.message : 'Update failed', 'error'),
  })
  const revoke = useMutation({
    mutationFn: () => tenantApi.revokeSessions(user!.id),
    onSuccess: () => toast('All sessions revoked'),
    onError: (e) => toast(e instanceof Error ? e.message : 'Revoke failed', 'error'),
  })

  const [assignRole, setAssignRole] = useState('')
  const assign = useMutation({
    mutationFn: () => fetch('/api/v1/users/' + user!.id + '/roles', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'include',
      body: JSON.stringify({ roleId: assignRole }),
    }).then(async (r) => { if (!r.ok) throw new Error(await r.text()); }),
    onSuccess: () => { toast('Role assigned'); setAssignRole(''); invalidate() },
    onError: (e) => toast(e instanceof Error ? e.message : 'Assign failed', 'error'),
  })
  const assignedRoleIds = new Set((d?.roles ?? []).map((r) => r.roleId))
  const availableRoles = (roles.data ?? []).filter((r) => !assignedRoleIds.has(r.id))

  return (
    <Drawer anchor="right" open={!!user} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 420 } } } }}>
      {user && (
        <Box sx={{ p: 2.5 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: 2 }}>
            <Box>
              <Typography variant="h6" sx={{ fontWeight: 700 }}>{user.displayName}</Typography>
              <Typography variant="body2" color="text.secondary">{user.email}</Typography>
            </Box>
            <StatusChip value={user.status} />
          </Box>
          <Divider sx={{ my: 1.5 }} />
          <SectionCard title="Account">
            <Box sx={{ px: 3, pb: 2 }}>
              <DetailRow label="Status">{user.status}</DetailRow>
              <DetailRow label="MFA">{user.mfaEnabled ? 'Enabled' : 'Not enabled'}</DetailRow>
              <DetailRow label="Last sign-in">{user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleString() : 'Never'}</DetailRow>
            </Box>
            <Box sx={{ px: 3, pb: 2.5, display: 'flex', gap: 1, flexWrap: 'wrap' }}>
              {user.status === 'Active' && (
                <Button size="small" variant="outlined" onClick={() => status.mutate('Suspended')} disabled={status.isPending}>Suspend</Button>
              )}
              {user.status !== 'Active' && (
                <Button size="small" variant="contained" onClick={() => status.mutate('Active')} disabled={status.isPending}>Activate</Button>
              )}
              <Button size="small" variant="outlined" color="error" onClick={() => revoke.mutate()} disabled={revoke.isPending}>Revoke sessions</Button>
            </Box>
          </SectionCard>

          <SectionCard title="Roles" sx={{ mt: 2 }}>
            <Box sx={{ p: 2, px: 3 }}>
              {(d?.roles ?? []).length === 0 && <Typography variant="body2" color="text.secondary">No roles assigned.</Typography>}
              <List dense disablePadding>
                {(d?.roles ?? []).map((r) => (
                  <ListItemButton key={r.id} disableRipple sx={{ px: 0 }}>
                    <ListItemText primary={r.roleName} secondary={r.roleCode} />
                    <Chip size="small" label={r.validTo ? `until ${r.validTo}` : 'Active'} variant="outlined" />
                  </ListItemButton>
                ))}
              </List>
              {availableRoles.length > 0 && (
                <Box sx={{ display: 'flex', gap: 1, mt: 1 }}>
                  <FormControl size="small" sx={{ flexGrow: 1 }}>
                    <InputLabel>Assign role</InputLabel>
                    <Select label="Assign role" value={assignRole} onChange={(e) => setAssignRole(e.target.value)}>
                      {availableRoles.map((r: Role) => <MenuItem key={r.id} value={r.id}>{r.name}</MenuItem>)}
                    </Select>
                  </FormControl>
                  <Button variant="contained" size="small" disabled={!assignRole || assign.isPending} onClick={() => assign.mutate()}>Assign</Button>
                </Box>
              )}
            </Box>
          </SectionCard>

          <SectionCard title="Permission overrides" sx={{ mt: 2 }}>
            <Box sx={{ p: 2, px: 3 }}>
              {(d?.overrides ?? []).length === 0
                ? <Typography variant="body2" color="text.secondary">No direct overrides.</Typography>
                : (d?.overrides ?? []).map((o) => (
                  <Box key={o.id} sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', py: 0.5 }}>
                    <Typography variant="body2" sx={{ fontFamily: "monospace", fontSize: 12 }}>{o.permissionCode}</Typography>
                    <Chip size="small" label={o.effect} color={o.effect === 'Allow' ? 'success' : 'error'} variant="outlined" />
                  </Box>
                ))}
            </Box>
          </SectionCard>

          <SectionCard title="Effective permissions" sx={{ mt: 2 }}>
            <Box sx={{ p: 2, px: 3, maxHeight: 200, overflow: 'auto' }}>
              <Typography variant="caption" color="text.secondary">
                {Object.keys(effPerms.data ?? {}).length} permissions resolved from roles + overrides
              </Typography>
              <Box sx={{ mt: 1, display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                {Object.keys(effPerms.data ?? {}).slice(0, 30).map((p) => <Chip key={p} size="small" label={p} variant="outlined" />)}
                {Object.keys(effPerms.data ?? {}).length > 30 && <Chip size="small" label={`+${Object.keys(effPerms.data ?? {}).length - 30} more`} />}
              </Box>
            </Box>
          </SectionCard>
        </Box>
      )}
    </Drawer>
  )
}

export function UsersPage() {
  const users = useQuery({ queryKey: ['users'], queryFn: tenantApi.users })
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<UserSummary | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [form, setForm] = useState({ email: '', displayName: '', roleId: '' })
  const roles = useQuery({ queryKey: ['roles'], queryFn: tenantApi.roles })
  const qc = useQueryClient()
  const toast = useToast()

  const create = useMutation({
    mutationFn: () => tenantApi.createUser({
      email: form.email, displayName: form.displayName,
      roleIds: form.roleId ? [form.roleId] : undefined,
    }),
    onSuccess: () => {
      toast('User created')
      void qc.invalidateQueries({ queryKey: ['users'] })
      setCreateOpen(false); setForm({ email: '', displayName: '', roleId: '' })
    },
    onError: (e) => toast(e instanceof Error ? e.message : 'Create failed', 'error'),
  })

  const filtered = (users.data ?? []).filter((u) =>
    !query || `${u.displayName} ${u.email}`.toLowerCase().includes(query.toLowerCase()))

  return (
    <Box>
      <PageHeader title="Users" subtitle="People who can sign in to this tenant"
        actions={<Button variant="contained" onClick={() => setCreateOpen(true)}>New user</Button>} />
      <Box sx={{ mb: 2 }}>
        <TextField size="small" placeholder="Search name or email" value={query} onChange={(e) => setQuery(e.target.value)} sx={{ width: { xs: '100%', sm: 320 } }} />
      </Box>
      {users.error && <ErrorAlert error={users.error} onRetry={() => users.refetch()} />}
      {users.isPending ? <TableSkeleton columns={4} /> : filtered.length === 0 ? (
        <EmptyState title="No users" hint={query ? 'No users match your search.' : 'Create the first user.'} />
      ) : (
        <DataList<UserSummary>
          columns={['User', 'Last sign-in', 'MFA', 'Status']}
          rows={filtered}
          rowKey={(u) => u.id}
          onRowClick={(u) => setSelected(u)}
          renderRow={(u) => [
            <Box key="u" sx={{ display: 'flex', alignItems: 'center', gap: 1.25, minWidth: 0 }}>
              <Avatar sx={{ width: 32, height: 32, fontSize: 13, fontWeight: 700, bgcolor: 'primary.main' }}>
                {u.displayName.split(' ').map((p) => p[0]).filter(Boolean).slice(0, 2).join('').toUpperCase()}
              </Avatar>
              <Box sx={{ minWidth: 0 }}>
                <Typography variant="body2" sx={{ fontWeight: 600 }} noWrap>{u.displayName}</Typography>
                <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>{u.email}</Typography>
              </Box>
            </Box>,
            <Typography key="l" variant="body2" color="text.secondary">{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : 'Never'}</Typography>,
            <Chip key="m" size="small" label={u.mfaEnabled ? 'On' : 'Off'} variant="outlined" color={u.mfaEnabled ? 'success' : 'default'} />,
            <StatusChip key="s" value={u.status} />,
          ]}
          renderCard={(u) => (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Avatar sx={{ width: 36, height: 36, fontSize: 14, fontWeight: 700, bgcolor: 'primary.main' }}>
                {u.displayName.split(' ').map((p) => p[0]).filter(Boolean).slice(0, 2).join('').toUpperCase()}
              </Avatar>
              <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                <Typography variant="body2" sx={{ fontWeight: 650 }} noWrap>{u.displayName}</Typography>
                <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>{u.email}</Typography>
              </Box>
              <StatusChip value={u.status} />
            </Box>
          )}
        />
      )}
      <UserDrawer user={selected} onClose={() => setSelected(null)} />

      <Dialog open={createOpen} onClose={() => setCreateOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>New user</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TextField label="Email" type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} required size="small" />
          <TextField label="Display name" value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} required size="small" />
          <FormControl size="small">
            <InputLabel>Initial role (optional)</InputLabel>
            <Select label="Initial role (optional)" value={form.roleId} onChange={(e) => setForm({ ...form, roleId: e.target.value })}>
              <MenuItem value="">None</MenuItem>
              {(roles.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{r.name}</MenuItem>)}
            </Select>
          </FormControl>
          <FormControlLabel control={<Switch checked disabled />} label="Invite by email — user sets their own password" />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setCreateOpen(false)}>Cancel</Button>
          <Button variant="contained" disabled={!form.email.trim() || !form.displayName.trim() || create.isPending} onClick={() => create.mutate()}>
            {create.isPending ? 'Creating…' : 'Create & invite'}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}
