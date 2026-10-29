import { useEffect, useState } from 'react'
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  MenuItem, Tab, Tabs, TextField, Typography,
} from '@mui/material'
import { Add, Apartment, Close } from '@mui/icons-material'
import { Drawer, IconButton } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { tenantApi, type BranchDetail, type CompanyDetail, type DepartmentDetail, type Employee } from '../../api/tenant'
import { ApiError } from '../../api/client'
import { CenteredSpinner, ErrorAlert, SectionCard, StatusChip } from '../../components/ui'
import { DataList } from '../../components/tenant'
import { useToast } from '../../components/toast'
import {
  AddressesPanel, BankAccountsPanel, BoolInput, ContactsPanel, DocumentsPanel, EffectiveConfigPanel,
  EmployeeSelect, Field, FormGrid, LogoCard, RegistrationsPanel, SaveBar, TextInput,
  WeeklyOffEditor, usePermissions,
  type FormState,
} from './OrgShared'

const COMPANY_TYPES = ['private_limited', 'public_limited', 'llp', 'partnership', 'proprietorship', 'trust', 'society', 'other'].map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
const BRANCH_TYPES = ['head_office', 'regional', 'factory', 'warehouse', 'sales', 'service', 'client_site', 'other'].map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
const DEPT_TYPES = ['hr', 'finance', 'engineering', 'sales', 'operations', 'support', 'marketing', 'admin', 'other'].map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
const PAYROLL_FREQS = ['monthly', 'semi_monthly', 'bi_weekly', 'weekly'].map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
const LOCATION_TYPES = ['office', 'factory', 'warehouse', 'client_site', 'remote', 'hybrid'].map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
const ATTENDANCE_MODES = ['geo', 'face', 'biometric', 'web', 'manual'].map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'].map((m, i) => ({ value: String(i + 1), label: m }))

function ServerErrors({ errors }: { errors: Record<string, string[]> }) {
  const list = Object.values(errors).flat()
  if (list.length === 0) return null
  return <Alert severity="error" sx={{ mb: 2 }}>{list.join(' ')}</Alert>
}

function useDetailSave<T extends { rowVersion?: number; code: string; name: string }>(
  detail: T | undefined, mutFn: (body: Record<string, unknown>) => Promise<unknown>, toastMsg: string,
) {
  const qc = useQueryClient()
  const toast = useToast()
  const [form, setForm] = useState<FormState>({})
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [dirty, setDirty] = useState(false)
  useEffect(() => {
    if (detail && !dirty) {
      const computed = new Set(['addresses', 'registrations', 'contacts', 'employeeNames', 'parentCompanyName', 'branchName',
        'parentDepartmentName', 'companyName', 'managerName', 'hrName', 'createdAt', 'modifiedAt', 'branchCount',
        'employeeCount', 'departmentCount', 'currentHeadcount', 'vacancies', 'logoFileId', 'id'])
      const fields = Object.fromEntries(Object.entries(detail as Record<string, unknown>).filter(([k]) => !computed.has(k)))
      setForm(fields as FormState)
    }
  }, [detail, dirty])

  const set = (next: FormState) => { setForm(next); setDirty(true) }
  const save = useMutation({
    mutationFn: () => mutFn({ ...form, rowVersion: detail?.rowVersion }),
    onSuccess: () => { toast(toastMsg); setDirty(false); setErrors({}); void qc.invalidateQueries({ queryKey: ['org'] }) },
    onError: (e) => {
      if (e instanceof ApiError && e.details) setErrors(e.details)
      else toast(e instanceof Error ? e.message : 'Save failed', 'error')
    },
  })
  return { form, setForm: set, errors, save, dirty }
}

// ============================== COMPANY ==============================

export function CompanyDetailBody({ id }: { id: string }) {
  const [tab, setTab] = useState(0)
  const perms = usePermissions()
  const canEdit = perms.has('org.company.edit')
  const detail = useQuery({ queryKey: ['org', 'company', id], queryFn: () => tenantApi.companyDetail(id) })
  const companies = useQuery({ queryKey: ['org', 'companies'], queryFn: tenantApi.companies })
  const { form, setForm, errors, save } = useDetailSave<CompanyDetail>(
    detail.data, (b) => tenantApi.updateCompany(id, b), 'Company saved')
  const c = detail.data

  if (detail.isPending) return <CenteredSpinner />
  if (detail.error || !c) return <ErrorAlert error={detail.error} onRetry={() => void detail.refetch()} />

  return (
    <Box>
      <Typography variant="h6" sx={{ mb: 0.5 }}>{c.name}</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{c.code}{c.legalName ? ` · ${c.legalName}` : ''}</Typography>
      <Box sx={{ mb: 2, display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        <StatusChip value={c.isActive ? 'Active' : 'Inactive'} />
        {c.parentCompanyName && <Chip size="small" variant="outlined" icon={<Apartment />} label={`Parent: ${c.parentCompanyName}`} />}
        <Chip size="small" variant="outlined" label={`${c.branchCount ?? 0} branches`} />
        <Chip size="small" variant="outlined" label={`${c.employeeCount ?? 0} employees`} />
      </Box>
      <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable" scrollButtons="auto" sx={{ mb: 2 }}>
        {['General', 'Addresses', 'Registrations', 'Finance & Payroll', 'Contacts', 'Documents', 'Policies'].map((t) => <Tab key={t} label={t} />)}
      </Tabs>

      {tab === 0 && (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <LogoCard companyId={id} logoFileId={c.logoFileId} canEdit={canEdit} />
          <SectionCard title="General information">
            <ServerErrors errors={errors} />
            <FormGrid>
              <TextInput label="Code" field="code" form={form} setForm={setForm} required errors={errors} disabled={!canEdit}
                helperText="Unique identifier" />
              <TextInput label="Trade name" field="name" form={form} setForm={setForm} required errors={errors} disabled={!canEdit} />
              <TextInput label="Legal name" field="legalName" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Company type" field="companyType" form={form} setForm={setForm} select options={COMPANY_TYPES} errors={errors} disabled={!canEdit} />
              <TextInput label="Industry" field="industry" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Parent company" field="parentCompanyId" form={form} setForm={setForm} select errors={errors} disabled={!canEdit}
                options={(companies.data ?? []).filter((x) => x.id !== id).map((x) => ({ value: x.id, label: x.name }))} />
              <TextInput label="Incorporation date" field="incorporationDate" form={form} setForm={setForm} type="date" errors={errors} disabled={!canEdit} />
              <TextInput label="Website" field="website" form={form} setForm={setForm} errors={errors} disabled={!canEdit} helperText="https://…" />
              <TextInput label="Official email" field="officialEmail" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Contact number" field="contactNumber" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            </FormGrid>
            {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
          </SectionCard>
          <SectionCard title="Statutory (single-value)">
            <FormGrid>
              <TextInput label="CIN / LLPIN" field="cin" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="PAN" field="pan" form={form} setForm={setForm} errors={errors} disabled={!canEdit} helperText="ABCDE1234F" />
              <TextInput label="TAN" field="tan" form={form} setForm={setForm} errors={errors} disabled={!canEdit} helperText="ABCD12345E" />
              <TextInput label="PF establishment code" field="pfEstablishmentCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="ESIC employer code" field="esicEmployerCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Professional tax reg. no." field="professionalTaxRegNo" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Shops & Establishment" field="shopsEstablishmentRegNo" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Factory licence" field="factoryLicenceNo" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="MSME / Udyam" field="msmeUdyamRegNo" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            </FormGrid>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.5 }}>
              GSTINs and dated registrations live under the Registrations tab — a company may hold several.
            </Typography>
            {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
          </SectionCard>
          <SectionCard title="Status">
            <TextField select size="small" label="Status" value={String(!!form.isActive)} sx={{ minWidth: 200 }}
              onChange={(e) => setForm({ ...form, isActive: e.target.value === 'true' })} disabled={!canEdit}>
              <MenuItem value="true">Active</MenuItem>
              <MenuItem value="false">Inactive</MenuItem>
            </TextField>
            {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
          </SectionCard>
        </Box>
      )}

      {tab === 1 && <AddressesPanel companyId={id} addresses={c.addresses} />}
      {tab === 2 && <RegistrationsPanel owner="companies" ownerId={id} registrations={c.registrations} canEdit={canEdit} />}

      {tab === 3 && (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <SectionCard title="Financial year & payroll defaults">
            <ServerErrors errors={errors} />
            <FormGrid>
              <TextInput label="Currency" field="currency" form={form} setForm={setForm} select errors={errors} disabled={!canEdit}
                options={['INR', 'USD', 'EUR', 'GBP', 'AED', 'SGD'].map((v) => ({ value: v, label: v }))} />
              <TextInput label="FY start month" field="financialYearStartMonth" form={form} setForm={setForm} select options={MONTHS} errors={errors} disabled={!canEdit}
                helperText="April = 4" />
              <TextInput label="Payroll frequency" field="payrollFrequency" form={form} setForm={setForm} select options={PAYROLL_FREQS} errors={errors} disabled={!canEdit} />
              <TextInput label="Salary pay day" field="salaryPayDay" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} helperText="Day of month, 1-31" />
            </FormGrid>
            {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
          </SectionCard>
          <BankAccountsPanel companyId={id} />
        </Box>
      )}

      {tab === 4 && <ContactsPanel companyId={id} contacts={c.contacts} />}
      {tab === 5 && <DocumentsPanel owner="companies" ownerId={id} canEdit={canEdit} />}

      {tab === 6 && (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <SectionCard title="Default policies" subtitle="These are company defaults — branches and departments inherit them unless explicitly overridden.">
            <ServerErrors errors={errors} />
            <FormGrid>
              <TextInput label="Default shift" field="defaultShiftName" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Leave policy" field="defaultLeavePolicyCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Holiday calendar" field="defaultHolidayCalendarCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Attendance policy" field="defaultAttendancePolicyCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Grace (minutes)" field="graceMinutes" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
            </FormGrid>
          </SectionCard>
          <SectionCard title="Weekly-off default">
            <WeeklyOffEditor form={form} setForm={setForm} errors={errors} />
            {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
          </SectionCard>
          <EffectiveConfigPanel level="companies" id={id} />
        </Box>
      )}
    </Box>
  )
}

// ============================== BRANCH ==============================

export function BranchDetailBody({ id }: { id: string }) {
  const [tab, setTab] = useState(0)
  const perms = usePermissions()
  const canEdit = perms.has('org.branch.edit')
  const detail = useQuery({ queryKey: ['org', 'branch', id], queryFn: () => tenantApi.branchDetail(id) })
  const companies = useQuery({ queryKey: ['org', 'companies'], queryFn: tenantApi.companies })
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const { form, setForm, errors, save } = useDetailSave<BranchDetail>(
    detail.data, (b) => tenantApi.updateBranch(id, b), 'Branch saved')
  const b = detail.data
  const emps = employees.data ?? []

  if (detail.isPending) return <CenteredSpinner />
  if (detail.error || !b) return <ErrorAlert error={detail.error} onRetry={() => void detail.refetch()} />

  return (
    <Box>
      <Typography variant="h6" sx={{ mb: 0.5 }}>{b.name}</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{b.code}{b.companyName ? ` · ${b.companyName}` : ''}</Typography>
      <Box sx={{ mb: 2, display: 'flex', gap: 1, flexWrap: 'wrap' }}>
        <StatusChip value={b.isActive ? 'Active' : 'Inactive'} />
        {b.branchType && <Chip size="small" variant="outlined" label={b.branchType.replace(/_/g, ' ')} />}
        <Chip size="small" variant="outlined" label={`${b.employeeCount ?? 0} employees`} />
        <Chip size="small" variant="outlined" label={`${b.departmentCount ?? 0} departments`} />
      </Box>
      <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable" scrollButtons="auto" sx={{ mb: 2 }}>
        {['General', 'Location', 'Registrations', 'Work Schedule', 'Attendance', 'Payroll & Billing', 'Documents'].map((t) => <Tab key={t} label={t} />)}
      </Tabs>

      {tab === 0 && (
        <SectionCard title="General information">
          <ServerErrors errors={errors} />
          <FormGrid>
            <TextInput label="Company" field="companyId" form={form} setForm={setForm} select required errors={errors} disabled={!canEdit}
              options={(companies.data ?? []).map((x) => ({ value: x.id, label: x.name }))} />
            <TextInput label="Code" field="code" form={form} setForm={setForm} required errors={errors} disabled={!canEdit} />
            <TextInput label="Name" field="name" form={form} setForm={setForm} required errors={errors} disabled={!canEdit} />
            <TextInput label="Branch type" field="branchType" form={form} setForm={setForm} select options={BRANCH_TYPES} errors={errors} disabled={!canEdit} />
            <TextInput label="Opening date" field="openingDate" form={form} setForm={setForm} type="date" errors={errors} disabled={!canEdit} />
            <TextInput label="Time zone" field="timeZone" form={form} setForm={setForm} select required errors={errors} disabled={!canEdit}
              options={['Asia/Kolkata', 'UTC', 'Asia/Dubai', 'Asia/Singapore', 'Europe/London', 'America/New_York'].map((v) => ({ value: v, label: v }))} />
            <EmployeeSelect label="Branch manager" value={form.managerEmployeeId as string}
              onChange={(v) => setForm({ ...form, managerEmployeeId: v })} employees={emps} error={errors.managerEmployeeId?.[0]} />
            <EmployeeSelect label="HR admin" value={form.hrEmployeeId as string}
              onChange={(v) => setForm({ ...form, hrEmployeeId: v })} employees={emps} error={errors.hrEmployeeId?.[0]} />
            <TextInput label="Contact phone" field="contactPhone" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="Contact email" field="contactEmail" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
          </FormGrid>
          <Box sx={{ mt: 2 }}>
            <TextField select size="small" label="Status" value={String(!!form.isActive)} sx={{ minWidth: 200 }}
              onChange={(e) => setForm({ ...form, isActive: e.target.value === 'true' })} disabled={!canEdit}>
              <MenuItem value="true">Active</MenuItem>
              <MenuItem value="false">Inactive</MenuItem>
            </TextField>
          </Box>
          {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
        </SectionCard>
      )}

      {tab === 1 && (
        <SectionCard title="Location" subtitle="Address and geolocation used for attendance geofencing and client sites">
          <ServerErrors errors={errors} />
          <FormGrid>
            <TextInput label="Address line 1" field="addressLine1" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="Address line 2" field="addressLine2" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="City" field="city" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="District" field="district" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="State" field="state" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="Country" field="country" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="PIN code" field="pinCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="Work location type" field="workLocationType" form={form} setForm={setForm} select options={LOCATION_TYPES} errors={errors} disabled={!canEdit} />
            <TextInput label="Client site reference" field="clientSiteRef" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="Latitude" field="latitude" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
            <TextInput label="Longitude" field="longitude" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
            <TextInput label="Geofence radius (m)" field="geofenceRadiusMeters" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
          </FormGrid>
          {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
        </SectionCard>
      )}

      {tab === 2 && <RegistrationsPanel owner="branches" ownerId={id} registrations={b.registrations} canEdit={canEdit} />}

      {tab === 3 && (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <SectionCard title="Working schedule" subtitle="Empty fields inherit the company default">
            <ServerErrors errors={errors} />
            <FormGrid>
              <TextInput label="Default shift" field="defaultShiftName" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
              <TextInput label="Work start" field="workStart" form={form} setForm={setForm} type="time" errors={errors} disabled={!canEdit} />
              <TextInput label="Work end" field="workEnd" form={form} setForm={setForm} type="time" errors={errors} disabled={!canEdit} />
              <TextInput label="Break (minutes)" field="breakMinutes" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
              <TextInput label="Holiday calendar" field="holidayCalendarCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            </FormGrid>
          </SectionCard>
          <SectionCard title="Weekly-off override">
            <WeeklyOffEditor form={form} setForm={setForm} errors={errors} />
            {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
          </SectionCard>
          <EffectiveConfigPanel level="branches" id={id} />
        </Box>
      )}

      {tab === 4 && (
        <SectionCard title="Attendance rules">
          <ServerErrors errors={errors} />
          <FormGrid>
            <TextInput label="Grace (minutes)" field="graceMinutes" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
            <BoolInput label="Overtime allowed" field="overtimeAllowed" form={form} setForm={setForm} />
            <TextInput label="Attendance mode" field="attendanceMode" form={form} setForm={setForm} select options={ATTENDANCE_MODES} errors={errors} disabled={!canEdit} />
            <TextInput label="Attendance policy" field="attendancePolicyCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
          </FormGrid>
          {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
        </SectionCard>
      )}

      {tab === 5 && (
        <SectionCard title="Payroll & billing">
          <ServerErrors errors={errors} />
          <FormGrid>
            <TextInput label="Payroll day" field="payrollDay" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} helperText="Day of month, 1-31" />
            <TextInput label="Cost centre" field="costCentre" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
          </FormGrid>
          {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
        </SectionCard>
      )}

      {tab === 6 && <DocumentsPanel owner="branches" ownerId={id} canEdit={canEdit} />}
    </Box>
  )
}

// ============================== DEPARTMENT ==============================

function AssignRoleDialog({ departmentId, open, onClose, employees }: {
  departmentId: string; open: boolean; onClose: () => void; employees: Employee[]
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const [role, setRole] = useState('hod')
  const [employeeId, setEmployeeId] = useState<string | null>(null)
  const [effectiveFrom, setEffectiveFrom] = useState(new Date().toISOString().slice(0, 10))
  const [reason, setReason] = useState('')
  const mut = useMutation({
    mutationFn: () => tenantApi.assignDepartmentRole(departmentId, { role, employeeId, effectiveFrom, reason: reason || undefined }),
    onSuccess: () => { toast('Assignment saved'); void qc.invalidateQueries({ queryKey: ['org'] }); onClose() },
    onError: (e) => toast(e instanceof Error ? e.message : 'Assignment failed', 'error'),
  })
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Assign HOD / alternate</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
        <TextField select size="small" label="Role" value={role} onChange={(e) => setRole(e.target.value)}>
          <MenuItem value="hod">Head of Department</MenuItem>
          <MenuItem value="alt_hod">Alternate HOD</MenuItem>
        </TextField>
        <EmployeeSelect label="Employee (leave empty to clear)" value={employeeId} onChange={setEmployeeId} employees={employees} />
        <TextField size="small" type="date" label="Effective from" slotProps={{ inputLabel: { shrink: true } }}
          value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)}
          helperText="Must be after the current assignment's start date" />
        <TextField size="small" label="Reason" value={reason} onChange={(e) => setReason(e.target.value)} />
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={mut.isPending || !effectiveFrom} onClick={() => mut.mutate()}>{mut.isPending ? 'Saving…' : 'Assign'}</Button>
      </DialogActions>
    </Dialog>
  )
}

export function DepartmentDetailBody({ id }: { id: string }) {
  const [tab, setTab] = useState(0)
  const [assignOpen, setAssignOpen] = useState(false)
  const perms = usePermissions()
  const canEdit = perms.has('org.department.edit')
  const detail = useQuery({ queryKey: ['org', 'department', id], queryFn: () => tenantApi.departmentDetail(id) })
  const assignments = useQuery({ queryKey: ['org', 'assignments', id], queryFn: () => tenantApi.departmentAssignments(id) })
  const branches = useQuery({ queryKey: ['org', 'branches'], queryFn: tenantApi.branches })
  const departments = useQuery({ queryKey: ['org', 'departments'], queryFn: tenantApi.departments })
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const preview = useQuery({
    queryKey: ['org', 'weekly-off-preview', id],
    queryFn: () => {
      const now = new Date()
      const from = now.toISOString().slice(0, 10)
      const to = new Date(now.getTime() + 27 * 86400000).toISOString().slice(0, 10)
      return tenantApi.weeklyOffPreview('departments', id, from, to)
    },
  })
  const { form, setForm, errors, save } = useDetailSave<DepartmentDetail>(
    detail.data, (b) => tenantApi.updateDepartment(id, b), 'Department saved')
  const d = detail.data
  const emps = employees.data ?? []
  const empName = (eid?: string | null) => (eid ? (emps.find((e) => e.id === eid) ? `${emps.find((e) => e.id === eid)!.displayName} (${emps.find((e) => e.id === eid)!.employeeCode})` : eid) : undefined)

  if (detail.isPending) return <CenteredSpinner />
  if (detail.error || !d) return <ErrorAlert error={detail.error} onRetry={() => void detail.refetch()} />

  return (
    <Box>
      <Typography variant="h6" sx={{ mb: 0.5 }}>{d.name}</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{d.code}{d.branchName ? ` · ${d.branchName}` : ''}{d.parentDepartmentName ? ` · under ${d.parentDepartmentName}` : ''}</Typography>
      <Box sx={{ mb: 2, display: 'flex', gap: 1, flexWrap: 'wrap' }}>
        <StatusChip value={d.isActive ? 'Active' : 'Inactive'} />
        {d.departmentType && <Chip size="small" variant="outlined" label={d.departmentType} />}
        <Chip size="small" variant="outlined" label={`${d.currentHeadcount ?? 0} employees`} />
        {d.vacancies != null && <Chip size="small" variant="outlined" color={d.vacancies > 0 ? 'primary' : 'default'} label={`${d.vacancies} vacancies`} />}
      </Box>
      <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable" scrollButtons="auto" sx={{ mb: 2 }}>
        {['General', 'HOD & Hierarchy', 'Weekly Off', 'Work Schedule', 'Leave & Approvals', 'Staffing & Budget'].map((t) => <Tab key={t} label={t} />)}
      </Tabs>

      {tab === 0 && (
        <SectionCard title="General information">
          <ServerErrors errors={errors} />
          <FormGrid>
            <TextInput label="Code" field="code" form={form} setForm={setForm} required errors={errors} disabled={!canEdit} />
            <TextInput label="Name" field="name" form={form} setForm={setForm} required errors={errors} disabled={!canEdit} />
            <TextInput label="Department type" field="departmentType" form={form} setForm={setForm} select options={DEPT_TYPES} errors={errors} disabled={!canEdit} />
            <TextInput label="Branch" field="branchId" form={form} setForm={setForm} select errors={errors} disabled={!canEdit}
              options={(branches.data ?? []).map((x) => ({ value: x.id, label: x.name }))} />
            <TextInput label="Parent department" field="parentDepartmentId" form={form} setForm={setForm} select errors={errors} disabled={!canEdit}
              options={(departments.data ?? []).filter((x) => x.id !== id).map((x) => ({ value: x.id, label: x.name }))} />
            <TextInput label="Cost centre" field="costCentre" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="Effective date" field="effectiveDate" form={form} setForm={setForm} type="date" errors={errors} disabled={!canEdit} />
          </FormGrid>
          <Box sx={{ mt: 2 }}>
            <TextField size="small" multiline rows={2} label="Description" fullWidth
              value={(form.description as string) ?? ''} onChange={(e) => setForm({ ...form, description: e.target.value })} disabled={!canEdit} />
          </Box>
          <Box sx={{ mt: 2 }}>
            <TextField select size="small" label="Status" value={String(!!form.isActive)} sx={{ minWidth: 200 }}
              onChange={(e) => setForm({ ...form, isActive: e.target.value === 'true' })} disabled={!canEdit}>
              <MenuItem value="true">Active</MenuItem>
              <MenuItem value="false">Inactive</MenuItem>
            </TextField>
          </Box>
          {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
        </SectionCard>
      )}

      {tab === 1 && (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <SectionCard title="Current assignments"
            actions={canEdit ? <Button size="small" variant="outlined" startIcon={<Add />} onClick={() => setAssignOpen(true)}>Assign</Button> : undefined}>
            <FormGrid>
              <Field label="Head of Department" value={empName(d.hodEmployeeId)} />
              <Field label="HOD effective from" value={d.hodEffectiveFrom} />
              <Field label="Alternate HOD" value={empName(d.altHodEmployeeId)} />
            </FormGrid>
          </SectionCard>
          <SectionCard title="Assignment history" subtitle="Effective-dated HOD changes — audited">
            <DataList
              columns={['Role', 'Employee', 'From', 'To', 'Reason']}
              rows={assignments.data ?? []}
              rowKey={(a) => a.id}
              renderRow={(a) => [
                <Chip key="r" size="small" label={a.role === 'hod' ? 'HOD' : 'Alt HOD'} variant="outlined" />,
                <Typography key="e" variant="body2">{a.employeeName ?? a.employeeId ?? '—'}</Typography>,
                <Typography key="f" variant="body2">{a.effectiveFrom}</Typography>,
                <Typography key="t" variant="body2">{a.effectiveTo ?? 'current'}</Typography>,
                <Typography key="n" variant="body2" color="text.secondary">{a.reason ?? '—'}</Typography>,
              ]}
            />
            {(assignments.data ?? []).length === 0 && <Typography variant="body2" color="text.secondary" sx={{ py: 1 }}>No assignments yet.</Typography>}
          </SectionCard>
          <SectionCard title="Reporting line">
            <ServerErrors errors={errors} />
            <FormGrid>
              <EmployeeSelect label="Reporting manager" value={form.reportingManagerEmployeeId as string}
                onChange={(v) => setForm({ ...form, reportingManagerEmployeeId: v })} employees={emps} error={errors.reportingManagerEmployeeId?.[0]} />
              <TextInput label="Escalation level" field="escalationLevel" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
            </FormGrid>
            {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
          </SectionCard>
          <AssignRoleDialog departmentId={id} open={assignOpen} onClose={() => setAssignOpen(false)} employees={emps} />
        </Box>
      )}

      {tab === 2 && (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <SectionCard title="Weekly-off override" subtitle="Overrides the branch/company default when set">
            <WeeklyOffEditor form={form} setForm={setForm} errors={errors} />
            {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
          </SectionCard>
          {preview.data && preview.data.offDays.length > 0 && (
            <SectionCard title="Upcoming offs (next 4 weeks)" subtitle={`Resolved from ${preview.data.source} — pattern: ${preview.data.pattern ?? 'none'}`}>
              <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap' }}>
                {preview.data.offDays.map((d) => <Chip key={d} size="small" variant="outlined" label={new Date(d + 'T00:00:00').toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short' })} />)}
              </Box>
            </SectionCard>
          )}
          <EffectiveConfigPanel level="departments" id={id} />
        </Box>
      )}

      {tab === 3 && (
        <SectionCard title="Work schedule">
          <ServerErrors errors={errors} />
          <FormGrid>
            <TextInput label="Default shift" field="defaultShiftName" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <TextInput label="Work start" field="workStart" form={form} setForm={setForm} type="time" errors={errors} disabled={!canEdit} />
            <TextInput label="Work end" field="workEnd" form={form} setForm={setForm} type="time" errors={errors} disabled={!canEdit} />
            <TextInput label="Break (minutes)" field="breakMinutes" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
            <TextInput label="Grace (minutes)" field="graceMinutes" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
            <TextInput label="Holiday calendar" field="holidayCalendarCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
            <BoolInput label="Overtime allowed" field="overtimeAllowed" form={form} setForm={setForm} />
            <BoolInput label="WFH / field eligible" field="wfhAllowed" form={form} setForm={setForm} />
            <TextInput label="Attendance policy" field="attendancePolicyCode" form={form} setForm={setForm} errors={errors} disabled={!canEdit} />
          </FormGrid>
          {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
        </SectionCard>
      )}

      {tab === 4 && (
        <SectionCard title="Leave & approval hierarchy" subtitle="Approvers for leave, outdoor duty, comp-off, overtime and regularization">
          <ServerErrors errors={errors} />
          <FormGrid>
            <EmployeeSelect label="Leave approver" value={form.leaveApproverEmployeeId as string} onChange={(v) => setForm({ ...form, leaveApproverEmployeeId: v })} employees={emps} error={errors.leaveApproverEmployeeId?.[0]} />
            <EmployeeSelect label="Alternate leave approver" value={form.altLeaveApproverEmployeeId as string} onChange={(v) => setForm({ ...form, altLeaveApproverEmployeeId: v })} employees={emps} error={errors.altLeaveApproverEmployeeId?.[0]} />
            <EmployeeSelect label="Outdoor duty approver" value={form.odApproverEmployeeId as string} onChange={(v) => setForm({ ...form, odApproverEmployeeId: v })} employees={emps} error={errors.odApproverEmployeeId?.[0]} />
            <EmployeeSelect label="Comp-off approver" value={form.compOffApproverEmployeeId as string} onChange={(v) => setForm({ ...form, compOffApproverEmployeeId: v })} employees={emps} error={errors.compOffApproverEmployeeId?.[0]} />
            <EmployeeSelect label="Overtime approver" value={form.otApproverEmployeeId as string} onChange={(v) => setForm({ ...form, otApproverEmployeeId: v })} employees={emps} error={errors.otApproverEmployeeId?.[0]} />
            <EmployeeSelect label="Regularization approver" value={form.regularizationApproverEmployeeId as string} onChange={(v) => setForm({ ...form, regularizationApproverEmployeeId: v })} employees={emps} error={errors.regularizationApproverEmployeeId?.[0]} />
          </FormGrid>
          {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
        </SectionCard>
      )}

      {tab === 5 && (
        <SectionCard title="Staffing & budget">
          <Box sx={{ display: 'flex', gap: 4, mb: 3, flexWrap: 'wrap' }}>
            <Field label="Current headcount" value={String(d.currentHeadcount ?? 0)} />
            <Field label="Approved headcount" value={d.approvedHeadcount != null ? String(d.approvedHeadcount) : undefined} />
            <Field label="Vacancies" value={d.vacancies != null ? String(d.vacancies) : undefined} />
          </Box>
          <ServerErrors errors={errors} />
          <FormGrid>
            <TextInput label="Approved headcount" field="approvedHeadcount" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
            <TextInput label="Budget" field="budgetAmount" form={form} setForm={setForm} type="number" errors={errors} disabled={!canEdit} />
            <TextInput label="Work area" field="workArea" form={form} setForm={setForm} errors={errors} disabled={!canEdit} helperText="Location / floor / recruiting region" />
          </FormGrid>
          {canEdit && <SaveBar saving={save.isPending} onSave={() => save.mutate()} />}
        </SectionCard>
      )}
    </Box>
  )
}


export function OrgDetailDrawer({ kind, id, onClose }: { kind: 'companies' | 'branches' | 'departments'; id: string | null; onClose: () => void }) {
  return (
    <Drawer anchor="right" open={!!id} onClose={onClose}
      sx={{ zIndex: (t) => t.zIndex.drawer + 2 }}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 720, md: 960 }, maxWidth: '100%' } } }}>
      <Box sx={{ height: '100%', overflowY: 'auto', p: { xs: 1.5, sm: 2.5 }, position: 'relative' }}>
        <IconButton onClick={onClose} aria-label="Close" size="small"
          sx={{ position: 'absolute', top: 10, right: 10, zIndex: 1 }}>
          <Close />
        </IconButton>
        {id && kind === 'companies' && <CompanyDetailBody id={id} />}
        {id && kind === 'branches' && <BranchDetailBody id={id} />}
        {id && kind === 'departments' && <DepartmentDetailBody id={id} />}
      </Box>
    </Drawer>
  )
}
