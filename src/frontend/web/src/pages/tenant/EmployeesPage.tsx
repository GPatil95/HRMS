import { useMemo, useState, type ReactNode } from 'react'
import {
  Avatar, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  MenuItem, Tab, Tabs, TextField, Typography,
} from '@mui/material'
import { Add, Business, Search } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { tenantApi, type Employee } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { DataList } from '../../components/tenant'
import { useToast } from '../../components/toast'

const initials = (name: string) => name.split(' ').map((p) => p[0]).filter(Boolean).slice(0, 2).join('').toUpperCase()

function EmpAvatar({ name, size = 34 }: { name: string; size?: number }) {
  return <Avatar sx={{ width: size, height: size, fontSize: size * 0.4, fontWeight: 700, bgcolor: 'primary.main' }}>{initials(name)}</Avatar>
}

export function EmployeesPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const navigate = useNavigate()
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const departments = useQuery({ queryKey: ['org', 'departments'], queryFn: tenantApi.departments })
  const branches = useQuery({ queryKey: ['org', 'branches'], queryFn: tenantApi.branches })
  const deptName = useMemo(() => new Map((departments.data ?? []).map((d) => [d.id, d.name])), [departments.data])
  const branchName = useMemo(() => new Map((branches.data ?? []).map((b) => [b.id, b.name])), [branches.data])
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState('')

  const filtered = (employees.data ?? []).filter((e) =>
    (!statusFilter || e.status === statusFilter) &&
    (!query || `${e.displayName} ${e.employeeCode} ${e.workEmail ?? ''}`.toLowerCase().includes(query.toLowerCase())))
  const statuses = useMemo(() => [...new Set((employees.data ?? []).map((e) => e.status))], [employees.data])

  const [createOpen, setCreateOpen] = useState(false)
  const [form, setForm] = useState({ employeeCode: '', displayName: '', workEmail: '', branchId: '', departmentId: '' })
  const field = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value })
  const create = useMutation({
    mutationFn: () => tenantApi.createEmployee({
      employeeCode: form.employeeCode, displayName: form.displayName,
      workEmail: form.workEmail || undefined, branchId: form.branchId,
      departmentId: form.departmentId || undefined,
    }),
    onSuccess: () => {
      toast('Employee created')
      void qc.invalidateQueries({ queryKey: ['employees'] })
      setCreateOpen(false)
      setForm({ employeeCode: '', displayName: '', workEmail: '', branchId: '', departmentId: '' })
    },
    onError: (e) => toast(e instanceof Error ? e.message : 'Create failed', 'error'),
  })

  return (
    <Box>
      <PageHeader
        title="Employees"
        subtitle="Manage employees, employment information and employee records."
        actions={<Button variant="contained" startIcon={<Add />} onClick={() => setCreateOpen(true)}>Add employee</Button>}
      />
      <Box sx={{ mb: 2, display: 'flex', gap: 1, flexWrap: 'wrap' }}>
        <TextField
          size="small" placeholder="Search name, code, or email" value={query}
          onChange={(e) => setQuery(e.target.value)}
          slotProps={{ input: { startAdornment: <Search fontSize="small" sx={{ mr: 1, color: 'text.disabled' }} /> } }}
          sx={{ flexGrow: 1, maxWidth: { sm: 340 } }}
        />
        <TextField select size="small" label="Status" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} sx={{ minWidth: 140 }}>
          <MenuItem value="">All statuses</MenuItem>
          {statuses.map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
        </TextField>
      </Box>
      {employees.error && <Box sx={{ mb: 2 }}><ErrorAlert error={employees.error} onRetry={() => employees.refetch()} /></Box>}
      {employees.isPending ? <TableSkeleton columns={5} /> : filtered.length === 0 ? (
        <EmptyState
          title={query || statusFilter ? 'No matching employees' : 'No employees yet'}
          hint={query || statusFilter ? 'Adjust your search or filters.' : 'Add your first employee to start building the directory.'}
          action={!query && !statusFilter ? <Button variant="contained" size="small" onClick={() => setCreateOpen(true)}>Add employee</Button> : undefined}
        />
      ) : (
        <DataList<Employee>
          columns={['Employee', 'Code', 'Department', 'Branch', 'Status']}
          rows={filtered}
          rowKey={(e) => e.id}
          onRowClick={(e) => navigate(`/employees/${e.id}`)}
          renderRow={(e) => [
            <Box key="e" sx={{ display: 'flex', alignItems: 'center', gap: 1.25, minWidth: 0 }}>
              <EmpAvatar name={e.displayName} />
              <Box sx={{ minWidth: 0 }}>
                <Typography variant="body2" sx={{ fontWeight: 600 }} noWrap>{e.displayName}</Typography>
                <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>{e.workEmail ?? '—'}</Typography>
              </Box>
            </Box>,
            <Typography key="c" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12 }}>{e.employeeCode}</Typography>,
            <Typography key="d" variant="body2" color="text.secondary" sx={{ display: { xs: 'none', md: 'block' } }}>{e.departmentId ? deptName.get(e.departmentId) ?? '—' : '—'}</Typography>,
            <Typography key="b" variant="body2" color="text.secondary" sx={{ display: { xs: 'none', md: 'block' } }}>{branchName.get(e.branchId) ?? '—'}</Typography>,
            <StatusChip key="s" value={e.status} />,
          ]}
          renderCard={(e) => (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <EmpAvatar name={e.displayName} size={38} />
              <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                <Typography variant="body2" sx={{ fontWeight: 650 }} noWrap>{e.displayName}</Typography>
                <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                  {e.employeeCode} · {e.departmentId ? deptName.get(e.departmentId) ?? 'No dept' : branchName.get(e.branchId) ?? '—'}
                </Typography>
              </Box>
              <StatusChip value={e.status} />
            </Box>
          )}
        />
      )}

      <Dialog open={createOpen} onClose={() => setCreateOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>Add employee</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TextField label="Employee code" value={form.employeeCode} onChange={field('employeeCode')} required size="small" helperText="Unique, e.g. EMP-001" />
          <TextField label="Full name" value={form.displayName} onChange={field('displayName')} required size="small" />
          <TextField label="Work email" value={form.workEmail} onChange={field('workEmail')} size="small" type="email" />
          <TextField select label="Branch" value={form.branchId} onChange={field('branchId')} required size="small">
            {(branches.data ?? []).map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
          </TextField>
          <TextField select label="Department" value={form.departmentId} onChange={field('departmentId')} size="small">
            <MenuItem value="">None</MenuItem>
            {(departments.data ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
          </TextField>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setCreateOpen(false)}>Cancel</Button>
          <Button variant="contained" disabled={!form.employeeCode.trim() || !form.displayName.trim() || !form.branchId || create.isPending}
            onClick={() => create.mutate()}>{create.isPending ? 'Creating…' : 'Create'}</Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}

function Field({ label, value }: { label: string; value?: ReactNode }) {
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography variant="caption" color="text.secondary" sx={{ textTransform: 'uppercase', letterSpacing: 0.6, fontWeight: 600, fontSize: 11 }}>{label}</Typography>
      <Typography variant="body2" sx={{ fontWeight: 500, mt: 0.25, wordBreak: 'break-word' }}>{value ?? '—'}</Typography>
    </Box>
  )
}

export function EmployeeDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const [tab, setTab] = useState(0)
  const employee = useQuery({ queryKey: ['employees', id], queryFn: () => tenantApi.employee(id) })
  const companies = useQuery({ queryKey: ['org', 'companies'], queryFn: tenantApi.companies })
  const branches = useQuery({ queryKey: ['org', 'branches'], queryFn: tenantApi.branches })
  const departments = useQuery({ queryKey: ['org', 'departments'], queryFn: tenantApi.departments })
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const e: Employee | undefined = employee.data

  const name = (map: { id: string; name: string }[] | undefined, x?: string | null) =>
    map?.find((m) => m.id === x)?.name
  const manager = employees.data?.find((x) => x.id === e?.managerEmployeeId)?.displayName

  return (
    <Box>
      <Button variant="text" onClick={() => navigate('/employees')} sx={{ mb: 1, ml: -1 }}>← Employees</Button>
      {employee.error && <ErrorAlert error={employee.error} onRetry={() => employee.refetch()} />}
      {employee.isPending ? <TableSkeleton columns={3} /> : e && (
        <>
          <SectionCard sx={{ mb: 2 }}>
            <Box sx={{ p: 3, display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
              <EmpAvatar name={e.displayName} size={56} />
              <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                <Typography variant="h6" sx={{ fontWeight: 700, lineHeight: 1.3 }}>{e.displayName}</Typography>
                <Typography variant="body2" color="text.secondary">{e.employeeCode}{e.workEmail ? ` · ${e.workEmail}` : ''}</Typography>
              </Box>
              <Chip label={e.status} color={e.status === 'Active' ? 'success' : 'default'} size="small" variant="outlined" />
            </Box>
          </SectionCard>

          <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2, borderBottom: 1, borderColor: 'divider' }}
            variant="scrollable" scrollButtons="auto" allowScrollButtonsMobile>
            <Tab label="Overview" />
            <Tab label="Employment" />
            <Tab label="Personal" disabled />
            <Tab label="Salary" disabled />
            <Tab label="Documents" disabled />
          </Tabs>

          {tab === 0 && (
            <SectionCard title="Basic information">
              <Box sx={{ p: 3, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2.5 }}>
                <Field label="Full name" value={e.displayName} />
                <Field label="Work email" value={e.workEmail} />
                <Field label="Status" value={<StatusChip value={e.status} />} />
              </Box>
            </SectionCard>
          )}
          {tab === 1 && (
            <SectionCard title="Employment & organization">
              <Box sx={{ p: 3, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2.5 }}>
                <Field label="Employee code" value={<span style={{ fontFamily: 'monospace' }}>{e.employeeCode}</span>} />
                <Field label="Reports to" value={manager} />
                <Field label="Company" value={name(companies.data, e.companyId)} />
                <Field label="Branch" value={name(branches.data, e.branchId)} />
                <Field label="Department" value={name(departments.data, e.departmentId)} />
              </Box>
              <Box sx={{ px: 3, pb: 2.5 }}>
                <Typography variant="caption" color="text.secondary">
                  Job title, joining date, and other record sections will appear here when those fields are exposed by the API.
                </Typography>
              </Box>
            </SectionCard>
          )}
          {tab > 1 && (
            <EmptyState title="Not available yet" hint="This section needs backend support that isn't in the current API surface." icon={<Business />} />
          )}
        </>
      )}
    </Box>
  )
}
