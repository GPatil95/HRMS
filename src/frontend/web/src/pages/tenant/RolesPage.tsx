import { useMemo, useState } from 'react'
import {
  Accordion, AccordionDetails, AccordionSummary, Box, Button, Chip, Dialog,
  DialogActions, DialogContent, DialogTitle, Divider, TextField, Typography,
} from '@mui/material'
import { ExpandMore } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { tenantApi, type PermissionDef, type Role, type RolePermission } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'

const groupByModule = (perms: PermissionDef[]) => {
  const m = new Map<string, PermissionDef[]>()
  for (const p of perms) {
    const g = m.get(p.moduleCode) ?? []
    g.push(p)
    m.set(p.moduleCode, g)
  }
  return [...m.entries()].sort((a, b) => a[0].localeCompare(b[0]))
}

function RoleEditor({ open, role, onClose }: { open: boolean; role: Role | null; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const catalog = useQuery({ queryKey: ['roles', 'permissions'], queryFn: tenantApi.rolePermissions, enabled: open })
  const [form, setForm] = useState({ code: role?.code ?? '', name: role?.name ?? '', description: role?.description ?? '' })
  const [selected, setSelected] = useState<Set<string>>(
    () => new Set((role?.permissions ?? []).map((p: RolePermission) => p.permissionCode)))
  const toggle = (code: string) => {
    const s = new Set(selected)
    if (s.has(code)) s.delete(code); else s.add(code)
    setSelected(s)
  }
  const save = useMutation({
    mutationFn: () => {
      const permissions = [...selected].map((permissionCode) => ({ permissionCode, scopeLevel: 'Global' }))
      return role
        ? tenantApi.updateRole(role.id, { ...form, permissions })
        : tenantApi.createRole({ ...form, permissions })
    },
    onSuccess: () => {
      toast(role ? 'Role updated' : 'Role created')
      void qc.invalidateQueries({ queryKey: ['roles'] })
      onClose()
    },
    onError: (e) => toast(e instanceof Error ? e.message : 'Save failed', 'error'),
  })

  const groups = useMemo(() => groupByModule(catalog.data ?? []), [catalog.data])
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md" scroll="paper">
      <DialogTitle>{role ? `Edit ${role.name}` : 'New role'}</DialogTitle>
      <DialogContent>
        <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', pt: 1, mb: 2 }}>
          <TextField label="Code" size="small" value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} required disabled={role?.isSystem} />
          <TextField label="Name" size="small" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required sx={{ flexGrow: 1 }} />
          <TextField label="Description" size="small" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} fullWidth />
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ textTransform: 'uppercase', letterSpacing: 0.8, fontWeight: 600 }}>
          Permissions ({selected.size} selected)
        </Typography>
        <Box sx={{ mt: 1 }}>
          {groups.map(([mod, perms]) => (
            <Accordion key={mod} disableGutters elevation={0} sx={{ border: 1, borderColor: 'divider', borderRadius: 1, mb: 0.5, '&:before': { display: 'none' } }}>
              <AccordionSummary expandIcon={<ExpandMore />} sx={{ minHeight: 44 }}>
                <Typography variant="body2" sx={{ fontWeight: 600 }}>{mod}</Typography>
                <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>
                  {perms.filter((p) => selected.has(p.code)).length}/{perms.length}
                </Typography>
              </AccordionSummary>
              <AccordionDetails sx={{ pt: 0 }}>
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75 }}>
                  {perms.map((p) => (
                    <Chip key={p.code} size="small" label={`${p.action} — ${p.code.split('.').pop()}`}
                      color={selected.has(p.code) ? 'primary' : 'default'} variant={selected.has(p.code) ? 'filled' : 'outlined'}
                      onClick={() => toggle(p.code)} title={p.description} />
                  ))}
                </Box>
              </AccordionDetails>
            </Accordion>
          ))}
        </Box>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!form.code.trim() || !form.name.trim() || save.isPending || role?.isSystem} onClick={() => save.mutate()}>
          {save.isPending ? 'Saving…' : 'Save role'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

export function RolesPage() {
  const roles = useQuery({ queryKey: ['roles'], queryFn: tenantApi.roles })
  const [edit, setEdit] = useState<{ open: boolean; role: Role | null }>({ open: false, role: null })

  return (
    <Box>
      <PageHeader title="Roles & Permissions" subtitle="What each role can do in this tenant"
        actions={<Button variant="contained" onClick={() => setEdit({ open: true, role: null })}>New role</Button>} />
      {roles.error && <ErrorAlert error={roles.error} onRetry={() => roles.refetch()} />}
      {roles.isPending ? <TableSkeleton columns={4} /> : (roles.data ?? []).length === 0 ? (
        <EmptyState title="No roles" hint="Roles are created during provisioning; add custom ones here." />
      ) : (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
          {(roles.data ?? []).map((r) => (
            <SectionCard key={r.id} title={r.name}
              actions={
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  {r.isSystem && <Chip size="small" label="System" variant="outlined" />}
                  <Typography variant="caption" color="text.secondary">{r.userCount} users</Typography>
                  {!r.isSystem && <Button size="small" onClick={() => setEdit({ open: true, role: r })}>Edit</Button>}
                </Box>
              }>
              <Box sx={{ px: 3, pb: 2 }}>
                <Typography variant="body2" color="text.secondary">{r.description || r.code}</Typography>
              </Box>
              <Divider />
              <Box sx={{ px: 3, py: 1.5, display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                {r.permissions.slice(0, 8).map((p) => (
                  <Chip key={p.permissionCode} size="small" label={p.permissionCode} variant="outlined" />
                ))}
                {r.permissions.length > 8 && <Chip size="small" label={`+${r.permissions.length - 8}`} />}
              </Box>
            </SectionCard>
          ))}
        </Box>
      )}
      <RoleEditor open={edit.open} role={edit.role} onClose={() => setEdit({ open: false, role: null })} />
    </Box>
  )
}
