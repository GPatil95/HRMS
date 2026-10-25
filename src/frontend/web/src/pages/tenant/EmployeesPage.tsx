import { useMemo, useState, type ReactNode } from 'react'
import {
  Avatar, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  Divider, MenuItem, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { tenantApi, type Employee } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'

const initials = (name: string) => name.split(' ').map((p) => p[0]).filter(Boolean).slice(0, 2).join('').toUpperCase()

export function EmployeesPage() {
  const qc = useQueryClient()
  const toast = useToast()
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
      <PageHeader title="Employees" subtitle={`${filtered.length} of ${employees.data?.length ?? 0} people`} actions={
        <Button variant="contained" onClick={() => setCreateOpen(true)}>Add employee</Button>} />
      <Box sx={{ mb: 2, display: 'flex', gap: 1, flexWrap: 'wrap' }}>
        <TextField size="small" placeholder="Search name, code, or email" value={query} onChange={(e) => setQuery(e.target.value)} sx={{ flexGrow: 1, maxWidth: { sm: 340 } }} />
        <TextField select size="small" label="Status" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} sx={{ minWidth: 140 }}>
          <MenuItem value="">All</MenuItem>
          {statuses.map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
        </TextField>
      </Box>
      {employees.error && <ErrorAlert error={employees.error} onRetry={() => employees.refetch()} />}
      {employees.isPending ? <TableSkeleton columns={5} /> : filtered.length === 0 ? (
        <EmptyState title={query || statusFilter ? 'No matching employees' : 'No employees yet'} hint={query ? 'Adjust your search or filters.' : 'Add your first employee to build the directory.'} />
      ) : (
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Employee</TableCell><TableCell>Code</TableCell>
              <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>Department</TableCell>
              <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>Branch</TableCell>
              <TableCell>Status</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {filtered.map((e) => (
              <TableRow key={e.id} hover component={Link} to={`/employees/${e.id}`} sx={{ cursor: 'pointer', textDecoration: 'none' }}>
                <TableCell>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
                    <Avatar sx={{ width: 30, height: 30, fontSize: 12, bgcolor: 'primary.main' }}>{initials(e.displayName)}</Avatar>
                    <Box sx={{ minWidth: 0 }}>
                      <Typography variant="body2" sx={{ fontWeight: 600 }} noWrap>{e.displayName}</Typography>
                      <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>{e.workEmail ?? '—'}</Typography>
                    </Box>
                  </Box>
                </TableCell>
                <TableCell><Typography variant="body2" sx={{ fontFamily: "monospace" }}>{e.employeeCode}</Typography></TableCell>
                <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}><Typography variant="body2" color="text.secondary">{e.departmentId ? deptName.get(e.departmentId) ?? '—' : '—'}</Typography></TableCell>
                <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}><Typography variant="body2" color="text.secondary">{branchName.get(e.branchId) ?? '—'}</Typography></TableCell>
                <TableCell><StatusChip value={e.status} /></TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
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
      <Typography variant="caption" color="text.secondary" sx={{ textTransform: 'uppercase', letterSpacing: 0.6, fontWeight: 600 }}>{label}</Typography>
      <Typography variant="body2" sx={{ fontWeight: 500,  mt: 0.25, wordBreak: 'break-word'  }}>{value ?? '—'}</Typography>
    </Box>
  )
}

export function EmployeeDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const employee = useQuery({ queryKey: ['employees', id], queryFn: () => tenantApi.employee(id) })
  const companies = useQuery({ queryKey: ['org', 'companies'], queryFn: tenantApi.companies })
  const branches = useQuery({ queryKey: ['org', 'branches'], queryFn: tenantApi.branches })
  const departments = useQuery({ queryKey: ['org', 'departments'], queryFn: tenantApi.departments })
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const e: Employee | undefined = employee.data

  const name = (map: { id: string; name: string }[] | undefined, id?: string | null) =>
    map?.find((x) => x.id === id)?.name
  const manager = employees.data?.find((x) => x.id === e?.managerEmployeeId)?.displayName

  return (
    <Box>
      <Button variant="text" onClick={() => navigate('/employees')} sx={{ mb: 1 }}>← Employees</Button>
      <PageHeader
        title={e?.displayName ?? 'Employee'}
        subtitle={e ? `${e.employeeCode} · ${e.status}` : ''}
        actions={e ? <Chip label={e.status} color={e.status === 'Active' ? 'success' : 'default'} size="small" /> : undefined}
      />
      {employee.error && <ErrorAlert error={employee.error} onRetry={() => employee.refetch()} />}
      {employee.isPending ? <TableSkeleton columns={3} /> : e && (
        <SectionCard title="Employment details">
          <Box sx={{ p: 3, pt: 2, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2.5 }}>
            <Field label="Employee code" value={<span style={{ fontFamily: 'monospace' }}>{e.employeeCode}</span>} />
            <Field label="Work email" value={e.workEmail} />
            <Field label="Company" value={name(companies.data, e.companyId)} />
            <Field label="Branch" value={name(branches.data, e.branchId)} />
            <Field label="Department" value={name(departments.data, e.departmentId)} />
            <Field label="Reports to" value={manager} />
          </Box>
          <Divider />
          <Box sx={{ px: 3, py: 2 }}>
            <Typography variant="caption" color="text.secondary">
              Personal details, documents, salary, and other record sections will appear here as those modules are enabled.
            </Typography>
          </Box>
        </SectionCard>
      )}
    </Box>
  )
}
