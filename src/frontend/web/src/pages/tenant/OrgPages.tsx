import { useMemo, useState } from 'react'
import {
  Avatar, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem,
  TextField, Typography,
} from '@mui/material'
import { AccountTree, Apartment, Store } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { tenantApi, type Branch, type Company, type Department } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, StatusChip, TableSkeleton } from '../../components/ui'
import { DataList } from '../../components/tenant'
import { useToast } from '../../components/toast'

interface EntityRow { id: string; code: string; name: string; isActive: boolean; extra?: string }

function useOrgData(kind: 'companies' | 'branches' | 'departments') {
  const companies = useQuery({ queryKey: ['org', 'companies'], queryFn: tenantApi.companies })
  const branches = useQuery({ queryKey: ['org', 'branches'], queryFn: tenantApi.branches, enabled: kind !== 'companies' })
  const departments = useQuery({ queryKey: ['org', 'departments'], queryFn: tenantApi.departments, enabled: kind === 'departments' })
  const companyName = useMemo(() => new Map((companies.data ?? []).map((c: Company) => [c.id, c.name])), [companies.data])
  const branchName = useMemo(() => new Map((branches.data ?? []).map((b: Branch) => [b.id, b.name])), [branches.data])
  const rows: EntityRow[] = useMemo(() => {
    if (kind === 'companies') return (companies.data ?? []).map((c) => ({ ...c }))
    if (kind === 'branches') return (branches.data ?? []).map((b) => ({ ...b, extra: companyName.get(b.companyId) }))
    return (departments.data ?? []).map((d: Department) => ({ ...d, extra: d.branchId ? branchName.get(d.branchId) : undefined }))
  }, [kind, companies.data, branches.data, departments.data, companyName, branchName])
  const pending = companies.isPending
    || (kind !== 'companies' && branches.isPending)
    || (kind === 'departments' && departments.isPending)
  const error = companies.error ?? branches.error ?? departments.error
  const refetch = () => { void companies.refetch(); void branches.refetch(); void departments.refetch() }
  return { rows, pending, error, refetch, companies: companies.data ?? [], branches: branches.data ?? [] }
}

function CreateDialog({ kind, open, onClose, companies, branches }: {
  kind: 'companies' | 'branches' | 'departments'; open: boolean; onClose: () => void
  companies: Company[]; branches: Branch[]
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const [form, setForm] = useState({ code: '', name: '', legalName: '', companyId: '', branchId: '' })
  const field = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value })

  const mut = useMutation({
    mutationFn: async () => {
      if (kind === 'companies') return tenantApi.createCompany({ code: form.code, name: form.name, legalName: form.legalName || undefined })
      if (kind === 'branches') return tenantApi.createBranch({ companyId: form.companyId, code: form.code, name: form.name })
      return tenantApi.createDepartment({ code: form.code, name: form.name, branchId: form.branchId || undefined })
    },
    onSuccess: () => {
      toast(`${kind === 'companies' ? 'Company' : kind === 'branches' ? 'Branch' : 'Department'} created`)
      void qc.invalidateQueries({ queryKey: ['org'] })
      setForm({ code: '', name: '', legalName: '', companyId: '', branchId: '' })
      onClose()
    },
    onError: (e) => toast(e instanceof Error ? e.message : 'Create failed', 'error'),
  })

  const canSubmit = form.code.trim() && form.name.trim() && (kind !== 'branches' || !!form.companyId)
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{kind === 'companies' ? 'New company' : kind === 'branches' ? 'New branch' : 'New department'}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
        {kind === 'branches' && (
          <TextField select label="Company" value={form.companyId} onChange={field('companyId')} required size="small">
            {companies.map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
          </TextField>
        )}
        {kind === 'departments' && (
          <TextField select label="Branch (optional)" value={form.branchId} onChange={field('branchId')} size="small">
            <MenuItem value="">None</MenuItem>
            {branches.map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
          </TextField>
        )}
        <TextField label="Code" value={form.code} onChange={field('code')} required size="small" helperText="Unique identifier, e.g. HQ, ENG" />
        <TextField label="Name" value={form.name} onChange={field('name')} required size="small" />
        {kind === 'companies' && <TextField label="Legal name" value={form.legalName} onChange={field('legalName')} size="small" />}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!canSubmit || mut.isPending} onClick={() => mut.mutate()}>
          {mut.isPending ? 'Creating…' : 'Create'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

function OrgPage({ kind, title, subtitle, extraHeader }: {
  kind: 'companies' | 'branches' | 'departments'; title: string; subtitle: string; extraHeader?: string
}) {
  const { rows, pending, error, refetch, companies, branches } = useOrgData(kind)
  const [query, setQuery] = useState('')
  const [createOpen, setCreateOpen] = useState(false)
  const filtered = rows.filter((r) =>
    !query || r.name.toLowerCase().includes(query.toLowerCase()) || r.code.toLowerCase().includes(query.toLowerCase()))

  return (
    <Box>
      <PageHeader title={title} subtitle={subtitle}
        actions={<Button variant="contained" onClick={() => setCreateOpen(true)}>New {kind === 'companies' ? 'company' : kind === 'branches' ? 'branch' : 'department'}</Button>} />
      <Box sx={{ mb: 2, display: 'flex', gap: 1 }}>
        <TextField size="small" placeholder="Search by name or code" value={query} onChange={(e) => setQuery(e.target.value)} sx={{ width: { xs: '100%', sm: 320 } }} />
      </Box>
      {error && <ErrorAlert error={error} onRetry={refetch} />}
      {pending ? <TableSkeleton columns={4} /> : filtered.length === 0 ? (
        <EmptyState
          title={query ? 'No matches' : `No ${kind} yet`}
          hint={query ? 'Try a different search.' : {
            companies: 'Create your first company to start organizing your HRMS.',
            branches: 'Add a branch to represent an office or business unit.',
            departments: 'Add a department to organize teams.',
          }[kind]}
          icon={kind === 'companies' ? <Apartment /> : kind === 'branches' ? <Store /> : <AccountTree />}
          action={!query ? <Button variant="contained" size="small" onClick={() => setCreateOpen(true)}>Create {kind === 'companies' ? 'company' : kind === 'branches' ? 'branch' : 'department'}</Button> : undefined}
        />
      ) : (
        <DataList<EntityRow>
          columns={['Code', 'Name', ...(extraHeader ? [extraHeader] : []), 'Status']}
          rows={filtered}
          rowKey={(r) => r.id}
          renderRow={(r) => [
            <Typography key="c" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12 }}>{r.code}</Typography>,
            <Typography key="n" variant="body2" sx={{ fontWeight: 600 }}>{r.name}</Typography>,
            ...(extraHeader ? [<Typography key="x" variant="body2" color="text.secondary">{r.extra ?? '—'}</Typography>] : []),
            <StatusChip key="s" value={r.isActive ? 'Active' : 'Inactive'} />,
          ]}
          renderCard={(r) => (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Avatar sx={{ width: 34, height: 34, fontSize: 12, fontWeight: 700, bgcolor: 'action.hover', color: 'text.secondary' }}>{r.code.slice(0, 3)}</Avatar>
              <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                <Typography variant="body2" sx={{ fontWeight: 650 }} noWrap>{r.name}</Typography>
                <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                  {r.code}{r.extra ? ` · ${r.extra}` : ''}
                </Typography>
              </Box>
              <StatusChip value={r.isActive ? 'Active' : 'Inactive'} />
            </Box>
          )}
        />
      )}
      <CreateDialog kind={kind} open={createOpen} onClose={() => setCreateOpen(false)} companies={companies} branches={branches} />
    </Box>
  )
}

export const CompaniesPage = () => (
  <OrgPage kind="companies" title="Companies" subtitle="Legal entities in your organization" />
)
export const BranchesPage = () => (
  <OrgPage kind="branches" title="Branches" subtitle="Office locations and business units" extraHeader="Company" />
)
export const DepartmentsPage = () => (
  <OrgPage kind="departments" title="Departments" subtitle="Teams and organizational units" extraHeader="Branch" />
)
