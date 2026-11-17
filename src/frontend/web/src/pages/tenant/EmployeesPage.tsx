import { useMemo, useState, type ReactNode } from 'react'
import {
  Avatar, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, MenuItem, Tab, Tabs, TextField, Typography,
} from '@mui/material'
import { Add, Edit, Search } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { mastersApi, tenantApi, type Employee } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { DataList } from '../../components/tenant'
import { useToast } from '../../components/toast'
import {
  BankTab, ContactsTab, DocumentsTab, LettersTab, LifecycleTab, PersonalTab, SalaryTab, StatutoryTab,
} from './EmployeeSections'

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
  const designations = useQuery({ queryKey: ['masters', 'designations'], queryFn: () => mastersApi.designations(false), enabled: createOpen })
  const grades = useQuery({ queryKey: ['masters', 'grades'], queryFn: () => mastersApi.grades(false), enabled: createOpen })
  const locations = useQuery({ queryKey: ['masters', 'locations'], queryFn: () => mastersApi.locations(false), enabled: createOpen })
  const costCentres = useQuery({ queryKey: ['masters', 'cost-centres'], queryFn: () => mastersApi.costCentres(false), enabled: createOpen })
  const emptyForm = {
    employeeCode: '', displayName: '', firstName: '', lastName: '', workEmail: '', workPhone: '',
    branchId: '', departmentId: '', designationId: '', gradeId: '', locationId: '', costCentreId: '',
    managerEmployeeId: '', employmentType: 'permanent', joiningDate: '', probationEndDate: '', preJoining: false,
  }
  const [form, setForm] = useState(emptyForm)
  const field = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm({ ...form, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const create = useMutation({
    mutationFn: () => tenantApi.createEmployee({
      employeeCode: form.employeeCode || ' ', displayName: form.displayName,
      workEmail: form.workEmail || undefined, branchId: form.branchId,
      departmentId: form.departmentId || undefined,
      managerEmployeeId: form.managerEmployeeId || undefined,
      firstName: form.firstName || undefined, lastName: form.lastName || undefined,
      workPhone: form.workPhone || undefined,
      designationId: form.designationId || undefined, gradeId: form.gradeId || undefined,
      locationId: form.locationId || undefined, costCentreId: form.costCentreId || undefined,
      employmentType: form.employmentType || undefined,
      joiningDate: form.joiningDate || undefined, probationEndDate: form.probationEndDate || undefined,
      preJoining: form.preJoining || undefined,
    }),
    onSuccess: () => {
      toast('Employee created')
      void qc.invalidateQueries({ queryKey: ['employees'] })
      setCreateOpen(false)
      setForm(emptyForm)
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

      <Dialog open={createOpen} onClose={() => setCreateOpen(false)} fullWidth maxWidth="md">
        <DialogTitle>Add employee</DialogTitle>
        <DialogContent sx={{ pt: 1 }}>
          <Typography variant="subtitle2" sx={{ mb: 1.5 }}>Identity</Typography>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2, mb: 2.5 }}>
            <TextField label="Employee code" value={form.employeeCode} onChange={field('employeeCode')} size="small"
              helperText="Leave blank to auto-generate from the numbering series" />
            <TextField label="Display name" value={form.displayName} onChange={field('displayName')} required size="small" />
            <TextField label="First name" value={form.firstName} onChange={field('firstName')} size="small" />
            <TextField label="Last name" value={form.lastName} onChange={field('lastName')} size="small" />
            <TextField label="Work email" value={form.workEmail} onChange={field('workEmail')} size="small" type="email" />
            <TextField label="Work phone" value={form.workPhone} onChange={field('workPhone')} size="small" />
          </Box>
          <Typography variant="subtitle2" sx={{ mb: 1.5 }}>Employment</Typography>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>
            <TextField select label="Branch" value={form.branchId} onChange={field('branchId')} required size="small">
              {(branches.data ?? []).map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
            </TextField>
            <TextField select label="Department" value={form.departmentId} onChange={field('departmentId')} size="small">
              <MenuItem value="">None</MenuItem>
              {(departments.data ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
            </TextField>
            <TextField select label="Designation" value={form.designationId} onChange={field('designationId')} size="small">
              <MenuItem value="">None</MenuItem>
              {(designations.data ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
            </TextField>
            <TextField select label="Grade" value={form.gradeId} onChange={field('gradeId')} size="small">
              <MenuItem value="">None</MenuItem>
              {(grades.data ?? []).map((g) => <MenuItem key={g.id} value={g.id}>{g.name}</MenuItem>)}
            </TextField>
            <TextField select label="Work location" value={form.locationId} onChange={field('locationId')} size="small">
              <MenuItem value="">None</MenuItem>
              {(locations.data ?? []).map((l) => <MenuItem key={l.id} value={l.id}>{l.name}</MenuItem>)}
            </TextField>
            <TextField select label="Cost centre" value={form.costCentreId} onChange={field('costCentreId')} size="small">
              <MenuItem value="">None</MenuItem>
              {(costCentres.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
            </TextField>
            <TextField select label="Reports to" value={form.managerEmployeeId} onChange={field('managerEmployeeId')} size="small">
              <MenuItem value="">None</MenuItem>
              {(employees.data ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{x.displayName}</MenuItem>)}
            </TextField>
            <TextField select label="Employment type" value={form.employmentType} onChange={field('employmentType')} size="small">
              {['permanent', 'probation', 'contract', 'intern', 'consultant'].map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}
            </TextField>
            <TextField type="date" label="Joining date" value={form.joiningDate} onChange={field('joiningDate')} size="small"
              slotProps={{ inputLabel: { shrink: true } }} />
            <TextField type="date" label="Probation end" value={form.probationEndDate} onChange={field('probationEndDate')} size="small"
              slotProps={{ inputLabel: { shrink: true } }} />
          </Box>
          <FormControlLabel sx={{ mt: 1 }}
            control={<Checkbox checked={form.preJoining} onChange={field('preJoining')} />}
            label="Pre-joining (onboarding — activate later via a joining event)" />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setCreateOpen(false)}>Cancel</Button>
          <Button variant="contained" disabled={!form.displayName.trim() || !form.branchId || create.isPending}
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
            <Tab label="Personal" />
            <Tab label="Contacts & Bank" />
            <Tab label="Statutory" />
            <Tab label="Documents" />
            <Tab label="Letters" />
            <Tab label="Salary" />
            <Tab label="Lifecycle" />
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
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <EmploymentPanel e={e} manager={manager} companies={companies.data} branches={branches.data} departments={departments.data} />
            </Box>
          )}
          {tab === 2 && <PersonalTab empId={e.id} />}
          {tab === 3 && (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <ContactsTab empId={e.id} />
              <BankTab empId={e.id} />
            </Box>
          )}
          {tab === 4 && <StatutoryTab empId={e.id} />}
          {tab === 5 && <DocumentsTab empId={e.id} />}
          {tab === 6 && <LettersTab empId={e.id} />}
          {tab === 7 && <SalaryTab empId={e.id} />}
          {tab === 8 && <LifecycleTab emp={e} />}
        </>
      )}
    </Box>
  )
}

function EmploymentPanel({ e, manager, companies, branches, departments }: {
  e: Employee; manager?: string
  companies?: { id: string; name: string }[]
  branches?: { id: string; name: string }[]
  departments?: { id: string; name: string }[]
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const designations = useQuery({ queryKey: ['masters', 'designations'], queryFn: () => mastersApi.designations() })
  const grades = useQuery({ queryKey: ['masters', 'grades'], queryFn: () => mastersApi.grades() })
  const locations = useQuery({ queryKey: ['masters', 'locations'], queryFn: () => mastersApi.locations() })
  const costCentres = useQuery({ queryKey: ['masters', 'cost-centres'], queryFn: () => mastersApi.costCentres() })
  const users = useQuery({ queryKey: ['users'], queryFn: tenantApi.users })
  const name = (map: { id: string; name: string }[] | undefined, x?: string | null) => map?.find((m) => m.id === x)?.name
  const linkedUser = users.data?.find((u) => u.employeeId === e.id)
  const linkable = (users.data ?? []).filter((u) => !u.employeeId)
  const [edit, setEdit] = useState(false)
  const [link, setLink] = useState('')
  const [form, setForm] = useState({ displayName: e.displayName, firstName: e.firstName ?? '', middleName: e.middleName ?? '', lastName: e.lastName ?? '', workEmail: e.workEmail ?? '', workPhone: e.workPhone ?? '' })
  const inv = () => { void qc.invalidateQueries({ queryKey: ['employees', e.id] }); void qc.invalidateQueries({ queryKey: ['employees'] }); void qc.invalidateQueries({ queryKey: ['users'] }) }
  const f = (k: keyof typeof form) => (ev: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: ev.target.value })
  const save = useMutation({
    mutationFn: () => tenantApi.updateEmployee(e.id, { ...form, rowVersion: e.rowVersion }),
    onSuccess: () => { toast('Employee updated'); inv(); setEdit(false) },
    onError: (x) => toast(x instanceof Error ? x.message : 'Update failed', 'error'),
  })
  const linkMut = useMutation({
    mutationFn: () => tenantApi.linkUser(e.id, link),
    onSuccess: () => { toast('User linked'); inv(); setLink('') },
    onError: (x) => toast(x instanceof Error ? x.message : 'Link failed', 'error'),
  })
  const unlink = useMutation({
    mutationFn: () => tenantApi.unlinkUser(e.id),
    onSuccess: () => { toast('User unlinked'); inv() },
    onError: (x) => toast(x instanceof Error ? x.message : 'Unlink failed', 'error'),
  })
  return (
    <>
      <SectionCard title="Employment & organization"
        actions={<Button size="small" startIcon={<Edit />} onClick={() => setEdit(true)}>Edit basics</Button>}>
        <Box sx={{ p: 3, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2.5 }}>
          <Field label="Employee code" value={<span style={{ fontFamily: 'monospace' }}>{e.employeeCode}</span>} />
          <Field label="Employment type" value={e.employmentType} />
          <Field label="Joining date" value={e.joiningDate} />
          <Field label="Probation end" value={e.probationEndDate} />
          <Field label="Confirmed on" value={e.confirmationDate} />
          <Field label="Reports to" value={manager} />
          <Field label="Company" value={name(companies, e.companyId)} />
          <Field label="Branch" value={name(branches, e.branchId)} />
          <Field label="Department" value={name(departments, e.departmentId)} />
          <Field label="Designation" value={name(designations.data, e.designationId)} />
          <Field label="Grade" value={name(grades.data, e.gradeId)} />
          <Field label="Work location" value={name(locations.data, e.locationId)} />
          <Field label="Cost centre" value={name(costCentres.data, e.costCentreId)} />
        </Box>
        <Box sx={{ px: 3, pb: 2.5 }}>
          <Typography variant="caption" color="text.secondary">
            Changes to org assignment, designation and manager go through lifecycle events (Lifecycle tab), not this form.
          </Typography>
        </Box>
      </SectionCard>

      <SectionCard title="Login account (ESS readiness)">
        <Box sx={{ p: 3, display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
          {linkedUser ? (
            <>
              <Chip label={linkedUser.email} color="primary" variant="outlined" />
              <Typography variant="body2" color="text.secondary">This employee can sign in with the linked login.</Typography>
              <Button size="small" color="error" onClick={() => unlink.mutate()} disabled={unlink.isPending}>Unlink</Button>
            </>
          ) : e.userId ? (
            <Typography variant="body2">Linked user id: {e.userId}</Typography>
          ) : (
            <>
              <TextField select size="small" label="Link a login" value={link} onChange={(ev) => setLink(ev.target.value)} sx={{ minWidth: 260 }}
                helperText="Only logins not yet linked to an employee are listed">
                {linkable.map((u) => <MenuItem key={u.id} value={u.id}>{u.email}</MenuItem>)}
              </TextField>
              <Button variant="contained" size="small" disabled={!link || linkMut.isPending} onClick={() => linkMut.mutate()}>Link</Button>
            </>
          )}
        </Box>
      </SectionCard>

      <Dialog open={edit} onClose={() => setEdit(false)} fullWidth maxWidth="sm">
        <DialogTitle>Edit basic info</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TextField label="Display name" value={form.displayName} onChange={f('displayName')} required size="small" />
          <TextField label="First name" value={form.firstName} onChange={f('firstName')} size="small" />
          <TextField label="Middle name" value={form.middleName} onChange={f('middleName')} size="small" />
          <TextField label="Last name" value={form.lastName} onChange={f('lastName')} size="small" />
          <TextField label="Work email" value={form.workEmail} onChange={f('workEmail')} size="small" type="email" />
          <TextField label="Work phone" value={form.workPhone} onChange={f('workPhone')} size="small" />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setEdit(false)}>Cancel</Button>
          <Button variant="contained" disabled={!form.displayName.trim() || save.isPending} onClick={() => save.mutate()}>
            {save.isPending ? 'Saving…' : 'Save'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  )
}
