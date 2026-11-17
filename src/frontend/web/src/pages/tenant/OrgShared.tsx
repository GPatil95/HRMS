import { useMemo, useState, type ReactNode } from 'react'
import {
  Alert, Autocomplete, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent,
  DialogTitle, Divider, FormControlLabel, IconButton, MenuItem, Switch, TextField, Tooltip, Typography,
} from '@mui/material'
import { CloudUpload, Delete, Download, InsertDriveFile, OpenInNew } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { tenantApi, type BankAccount, type BankAccountInput, type Employee, type OrgContact, type ContactInput,
  type OrgAddress, type AddressInput, type OrgDocument, type Registration, type RegistrationInput } from '../../api/tenant'
import { ApiError } from '../../api/client'
import { EmptyState, SectionCard } from '../../components/ui'
import { DataList } from '../../components/tenant'
import { useToast } from '../../components/toast'

export type FormState = Record<string, unknown>

export function Field({ label, value }: { label: string; value?: ReactNode }) {
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography variant="caption" color="text.secondary" sx={{ textTransform: 'uppercase', letterSpacing: 0.6, fontWeight: 600, fontSize: 11 }}>{label}</Typography>
      <Typography variant="body2" sx={{ fontWeight: 500, mt: 0.25, wordBreak: 'break-word' }}>{value ?? '—'}</Typography>
    </Box>
  )
}

const grid = { display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', md: '1fr 1fr 1fr' }, gap: 2 }

export function TextInput({ label, field, form, setForm, type, select, options, required, helperText, errors, disabled }: {
  label: string; field: string; form: FormState; setForm: (f: FormState) => void
  type?: string; select?: boolean; options?: { value: string; label: string }[]; required?: boolean
  helperText?: string; errors?: Record<string, string[]>; disabled?: boolean
}) {
  const err = errors?.[field]?.[0]
  const raw = form[field]
  const value = type === 'number' ? (raw ?? '') : (typeof raw === 'string' ? raw : raw == null ? '' : String(raw))
  return (
    <TextField size="small" label={label} required={required} select={select} fullWidth
      type={type === 'date' || type === 'time' ? type : undefined}
      value={value}
      onChange={(e) => setForm({ ...form, [field]: type === 'number' ? (e.target.value === '' ? null : Number(e.target.value)) : e.target.value })}
      error={!!err} helperText={err ?? helperText} disabled={disabled}
      slotProps={type === 'date' || type === 'time' ? { inputLabel: { shrink: true } } : undefined}>
      {select && [<MenuItem key="" value="">None</MenuItem>, ...(options ?? []).map((o) => <MenuItem key={o.value} value={o.value}>{o.label}</MenuItem>)]}
    </TextField>
  )
}

export function BoolInput({ label, field, form, setForm }: { label: string; field: string; form: FormState; setForm: (f: FormState) => void }) {
  const v = form[field] as boolean | null | undefined
  return (
    <TextField size="small" select fullWidth label={label} value={v == null ? '' : v ? 'yes' : 'no'}
      onChange={(e) => setForm({ ...form, [field]: e.target.value === '' ? null : e.target.value === 'yes' })}>
      <MenuItem value="">Inherit / not set</MenuItem>
      <MenuItem value="yes">Yes</MenuItem>
      <MenuItem value="no">No</MenuItem>
    </TextField>
  )
}

export function FormGrid({ children }: { children: ReactNode }) {
  return <Box sx={grid}>{children}</Box>
}

export function EmployeeSelect({ label, value, onChange, employees, error }: {
  label: string; value?: string | null; onChange: (id: string | null) => void; employees: Employee[]; error?: string
}) {
  const current = employees.find((e) => e.id === value) ?? null
  return (
    <Autocomplete size="small" options={employees} value={current}
      getOptionLabel={(e) => `${e.displayName} (${e.employeeCode})`}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      onChange={(_, v) => onChange(v?.id ?? null)}
      renderInput={(p) => <TextField {...p} label={label} error={!!error} helperText={error} />} />
  )
}

const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
export const WEEKLY_OFF_PATTERNS = [
  { value: 'fixed', label: 'Fixed days off' },
  { value: 'alternate_saturday', label: 'Alternate Saturdays (nth-weekday rules)' },
  { value: 'nth_weekday', label: 'Nth-weekday pattern (e.g. 1st & 3rd Saturday)' },
  { value: 'rotating', label: 'Rotating cycle' },
  { value: 'shift_based', label: 'Shift-based (resolved by shift roster)' },
]

interface WeeklyOffConfig { days?: number[]; nthWeekdays?: { weekday: number; weeks: number[] }[]; rotation?: { anchor: string; cycleDays: number; offOffsets: number[] }; effectiveFrom?: string; effectiveTo?: string }

function parseConfig(json?: string | null): WeeklyOffConfig {
  if (!json) return {}
  try { return JSON.parse(json) as WeeklyOffConfig } catch { return {} }
}

export function WeeklyOffEditor({ form, setForm, errors }: { form: FormState; setForm: (f: FormState) => void; errors?: Record<string, string[]> }) {
  const pattern = (form.weeklyOffPattern as string) || ''
  const cfg = parseConfig(form.weeklyOffConfigJson as string)
  const err = errors?.weeklyOffPattern?.[0]

  const write = (next: WeeklyOffConfig) => setForm({ ...form, weeklyOffConfigJson: JSON.stringify({ ...next, days: next.days ?? [], nthWeekdays: next.nthWeekdays ?? [] }) })

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <TextField select size="small" fullWidth label="Weekly-off pattern" value={pattern}
        onChange={(e) => setForm({ ...form, weeklyOffPattern: e.target.value || null })}
        error={!!err} helperText={err ?? 'Empty = inherit from the parent level'}>
        <MenuItem value="">Inherit / not set</MenuItem>
        {WEEKLY_OFF_PATTERNS.map((p) => <MenuItem key={p.value} value={p.value}>{p.label}</MenuItem>)}
      </TextField>
      {(pattern === 'fixed') && (
        <Box>
          <Typography variant="caption" color="text.secondary">Days off each week</Typography>
          <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap', mt: 0.5 }}>
            {WEEKDAYS.map((d, i) => {
              const on = cfg.days?.includes(i) ?? false
              return <Chip key={d} label={d} size="small" color={on ? 'primary' : 'default'} variant={on ? 'filled' : 'outlined'}
                onClick={() => write({ ...cfg, days: on ? cfg.days!.filter((x) => x !== i) : [...(cfg.days ?? []), i].sort() })} />
            })}
          </Box>
        </Box>
      )}
      {(pattern === 'alternate_saturday' || pattern === 'nth_weekday') && (
        <Box>
          <Typography variant="caption" color="text.secondary">Nth-weekday rules (e.g. 2nd & 4th Saturday)</Typography>
          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', mt: 0.5 }}>
            <TextField select size="small" label="Weekday" value={String(cfg.nthWeekdays?.[0]?.weekday ?? 6)}
              onChange={(e) => write({ ...cfg, nthWeekdays: [{ weekday: Number(e.target.value), weeks: cfg.nthWeekdays?.[0]?.weeks ?? [] }] })} sx={{ minWidth: 160 }}>
              {WEEKDAYS.map((d, i) => <MenuItem key={d} value={i}>{d}</MenuItem>)}
            </TextField>
            <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap', alignItems: 'center' }}>
              {[1, 2, 3, 4, 5].map((w) => {
                const on = cfg.nthWeekdays?.[0]?.weeks?.includes(w) ?? false
                return <Chip key={w} label={`${w === 1 ? '1st' : w === 2 ? '2nd' : w === 3 ? '3rd' : w + 'th'}`} size="small"
                  color={on ? 'primary' : 'default'} variant={on ? 'filled' : 'outlined'}
                  onClick={() => {
                    const weeks = on ? cfg.nthWeekdays![0].weeks.filter((x) => x !== w) : [...(cfg.nthWeekdays?.[0]?.weeks ?? []), w].sort()
                    write({ ...cfg, nthWeekdays: [{ weekday: cfg.nthWeekdays?.[0]?.weekday ?? 6, weeks }] })
                  }} />
              })}
            </Box>
          </Box>
          {(cfg.nthWeekdays?.[0]?.weeks?.length ?? 0) > 0 && (
            <FormControlLabel sx={{ mt: 1 }}
              control={<Switch size="small" checked={cfg.days?.includes(0) ?? false}
                onChange={(_, on) => write({ ...cfg, days: on ? [0] : [] })} />}
              label="Also every Sunday off" />
          )}
        </Box>
      )}
      {pattern === 'rotating' && (
        <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
          <TextField size="small" type="number" label="Cycle length (days)" value={cfg.rotation?.cycleDays ?? 14} sx={{ maxWidth: 180 }}
            onChange={(e) => {
              const cycleDays = Math.max(2, Math.min(60, Number(e.target.value) || 2))
              write({ ...cfg, rotation: { anchor: cfg.rotation?.anchor ?? '2026-01-01', cycleDays, offOffsets: cfg.rotation?.offOffsets ?? [cycleDays - 1].filter((o) => o < cycleDays) } })
            }} />
          <TextField size="small" type="date" label="Cycle anchor" slotProps={{ inputLabel: { shrink: true } }}
            value={cfg.rotation?.anchor ?? ''}
            onChange={(e) => write({ ...cfg, rotation: { anchor: e.target.value, cycleDays: cfg.rotation?.cycleDays ?? 14, offOffsets: cfg.rotation?.offOffsets ?? [] } })} />
          <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap', alignItems: 'center' }}>
            {Array.from({ length: cfg.rotation?.cycleDays ?? 0 }, (_, i) => {
              const on = cfg.rotation?.offOffsets.includes(i) ?? false
              return <Chip key={i} label={`D${i + 1}`} size="small" color={on ? 'primary' : 'default'} variant={on ? 'filled' : 'outlined'}
                onClick={() => write({ ...cfg, rotation: { ...cfg.rotation!, offOffsets: on ? cfg.rotation!.offOffsets.filter((x) => x !== i) : [...cfg.rotation!.offOffsets, i].sort() } })} />
            })}
          </Box>
        </Box>
      )}
      {pattern === 'shift_based' && (
        <Alert severity="info" variant="outlined">Weekly offs follow the employee's shift roster — no day configuration needed here.</Alert>
      )}
      {pattern && pattern !== 'shift_based' && (
        <FormGrid>
          <TextField size="small" type="date" label="Effective from" slotProps={{ inputLabel: { shrink: true } }}
            value={cfg.effectiveFrom ?? ''} onChange={(e) => write({ ...cfg, effectiveFrom: e.target.value || undefined })} />
          <TextField size="small" type="date" label="Effective to" slotProps={{ inputLabel: { shrink: true } }}
            value={cfg.effectiveTo ?? ''} onChange={(e) => write({ ...cfg, effectiveTo: e.target.value || undefined })} />
        </FormGrid>
      )}
    </Box>
  )
}

export function EffectiveConfigPanel({ level, id, hide }: { level: 'companies' | 'branches' | 'departments' | 'employees'; id: string; hide?: string[] }) {
  const cfg = useQuery({ queryKey: ['org', 'effective', level, id], queryFn: () => tenantApi.effectiveConfig(level, id) })
  const settings = (cfg.data?.settings ?? []).filter((s) => !hide?.includes(s.key))
  if (settings.length === 0) return null
  return (
    <SectionCard title="Effective configuration" subtitle="Resolved by precedence: department → branch → company. Company defaults never overwrite explicit lower-level settings.">
      <DataList
        columns={['Setting', 'Effective value', 'Source']}
        rows={settings}
        rowKey={(s) => s.key}
        renderRow={(s) => [
          <Typography key="k" variant="body2">{s.label}</Typography>,
          <Typography key="v" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12 }}>
            {s.value == null ? '—' : typeof s.value === 'boolean' ? (s.value ? 'Yes' : 'No') : String(s.value)}
          </Typography>,
          s.value == null
            ? <Typography key="s" variant="body2" color="text.disabled">not set</Typography>
            : <Chip key="s" size="small" variant={s.isOverride ? 'filled' : 'outlined'} color={s.isOverride ? 'primary' : 'default'}
                label={`${s.source}${s.sourceName ? ` · ${s.sourceName}` : ''}`} />,
        ]}
      />
    </SectionCard>
  )
}

export function RegistrationsPanel({ owner, ownerId, registrations, canEdit }: {
  owner: 'companies' | 'branches'; ownerId: string; registrations: Registration[]; canEdit: boolean
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<Registration | null>(null)
  const blank: RegistrationInput = { registrationType: 'gstin', registrationNumber: '', jurisdiction: '', registrationSubtype: '', isActive: true }
  const [form, setForm] = useState<RegistrationInput>(blank)
  const [errors, setErrors] = useState<Record<string, string[]>>({})

  const invalidate = () => void qc.invalidateQueries({ queryKey: ['org'] })
  const mut = useMutation({
    mutationFn: async () => {
      if (editing) return tenantApi.updateRegistration(owner, ownerId, editing.id, form)
      return tenantApi.addRegistration(owner, ownerId, form)
    },
    onSuccess: () => { toast(editing ? 'Registration updated' : 'Registration added'); invalidate(); setOpen(false); setErrors({}) },
    onError: (e) => {
      if (e instanceof ApiError && e.details) setErrors(e.details)
      else toast(e instanceof Error ? e.message : 'Save failed', 'error')
    },
  })
  const del = useMutation({
    mutationFn: (rid: string) => tenantApi.deleteRegistration(owner, ownerId, rid),
    onSuccess: () => { toast('Registration removed'); invalidate() },
    onError: (e) => toast(e instanceof Error ? e.message : 'Delete failed', 'error'),
  })

  const openEdit = (r?: Registration) => {
    setEditing(r ?? null)
    setErrors({})
    setForm(r ? { registrationType: r.registrationType, registrationNumber: r.registrationNumber, jurisdiction: r.jurisdiction ?? '', registrationSubtype: r.registrationSubtype ?? '', effectiveFrom: r.effectiveFrom, effectiveTo: r.effectiveTo, isActive: r.isActive } : blank)
    setOpen(true)
  }

  return (
    <SectionCard title="Statutory registrations" subtitle="GSTIN, PF, ESIC, PT and other registrations — a company or branch can hold several."
      actions={canEdit ? <Button size="small" variant="outlined" onClick={() => openEdit()}>Add registration</Button> : undefined}>
      {registrations.length === 0 ? (
        <EmptyState title="No registrations" hint="Add GSTIN, PF, ESIC or other statutory registrations." />
      ) : (
        <DataList
          columns={['Type', 'Number', 'Jurisdiction', 'Validity', 'Status', '']}
          rows={registrations}
          rowKey={(r) => r.id}
          renderRow={(r) => [
            <Chip key="t" size="small" label={r.registrationType.toUpperCase()} variant="outlined" />,
            <Typography key="n" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12 }}>{r.registrationNumber}</Typography>,
            <Typography key="j" variant="body2" color="text.secondary">{r.jurisdiction ?? '—'}</Typography>,
            <Typography key="v" variant="body2" color="text.secondary">
              {r.effectiveFrom ?? '…'} → {r.effectiveTo ?? 'ongoing'}
              {r.expiresInDays != null && r.expiresInDays >= 0 && r.expiresInDays <= 30 && (
                <Chip size="small" color="warning" label={`expires in ${r.expiresInDays}d`} sx={{ ml: 1 }} />
              )}
            </Typography>,
            <Typography key="s" variant="body2">{r.isActive ? 'Active' : 'Inactive'}</Typography>,
            canEdit ? (
              <Box key="a" sx={{ display: 'flex', gap: 0.5, justifyContent: 'flex-end' }}>
                <Button size="small" onClick={() => openEdit(r)}>Edit</Button>
                <IconButton size="small" color="error" onClick={() => del.mutate(r.id)} aria-label="Delete"><Delete fontSize="small" /></IconButton>
              </Box>
            ) : <Box key="a" />,
          ]}
        />
      )}
      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? 'Edit registration' : 'Add registration'}</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TextInput label="Type" field="registrationType" form={form as FormState} setForm={setForm as (f: FormState) => void} select required errors={errors}
            options={['gstin', 'pf', 'esic', 'pt', 'shops_establishment', 'factory_licence', 'labour_licence', 'msme', 'lwf', 'other'].map((t) => ({ value: t, label: t.toUpperCase().replace(/_/g, ' ') }))} />
          <TextInput label="Registration number" field="registrationNumber" form={form as FormState} setForm={setForm as (f: FormState) => void} required errors={errors}
            helperText={form.registrationType === 'gstin' ? '15 characters, e.g. 29ABCDE1234F1Z5' : undefined} />
          <TextInput label="Jurisdiction" field="jurisdiction" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors}
            helperText={form.registrationType === 'gstin' ? 'Defaults to the GSTIN state code' : undefined} />
          <TextInput label="Subtype" field="registrationSubtype" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors}
            helperText={form.registrationType === 'gstin' ? 'regular / composition / SEZ / ISD' : undefined} />
          <FormGrid>
            <TextInput label="Valid from" field="effectiveFrom" form={form as FormState} setForm={setForm as (f: FormState) => void} type="date" errors={errors} />
            <TextInput label="Valid to" field="effectiveTo" form={form as FormState} setForm={setForm as (f: FormState) => void} type="date" errors={errors} />
          </FormGrid>
          <FormControlLabel control={<Switch checked={form.isActive ?? true} onChange={(_, v) => setForm({ ...form, isActive: v })} />} label="Active" />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setOpen(false)}>Cancel</Button>
          <Button variant="contained" disabled={mut.isPending || !form.registrationNumber} onClick={() => mut.mutate()}>
            {mut.isPending ? 'Saving…' : 'Save'}
          </Button>
        </DialogActions>
      </Dialog>
    </SectionCard>
  )
}

export function AddressesPanel({ companyId, addresses }: { companyId: string; addresses: OrgAddress[] }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<OrgAddress | null>(null)
  const blank: AddressInput = { addressType: 'registered', line1: '', line2: '', landmark: '', country: 'India', state: '', district: '', city: '', pinCode: '', contactPerson: '', contactEmail: '', contactPhone: '' }
  const [form, setForm] = useState<AddressInput>(blank)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const invalidate = () => void qc.invalidateQueries({ queryKey: ['org'] })

  const mut = useMutation({
    mutationFn: async () => editing ? tenantApi.updateAddress(companyId, editing.id, form) : tenantApi.addAddress(companyId, form),
    onSuccess: () => { toast(editing ? 'Address updated' : 'Address added'); invalidate(); setOpen(false); setErrors({}) },
    onError: (e) => {
      if (e instanceof ApiError && e.details) setErrors(e.details)
      else toast(e instanceof Error ? e.message : 'Save failed', 'error')
    },
  })
  const del = useMutation({
    mutationFn: (aid: string) => tenantApi.deleteAddress(companyId, aid),
    onSuccess: () => { toast('Address removed'); invalidate() },
    onError: (e) => toast(e instanceof Error ? e.message : 'Delete failed', 'error'),
  })

  const openEdit = (a?: OrgAddress) => {
    setEditing(a ?? null); setErrors({})
    setForm(a ? { ...a } : blank)
    setOpen(true)
  }
  const oneLine = (a: OrgAddress) => [a.line1, a.line2, a.city, a.district, a.state, a.pinCode, a.country].filter(Boolean).join(', ')

  return (
    <SectionCard title="Addresses" actions={<Button size="small" variant="outlined" onClick={() => openEdit()}>Add address</Button>}>
      {addresses.length === 0 ? <EmptyState title="No addresses" hint="Add registered, corporate, mailing and billing addresses." /> : (
        <DataList
          columns={['Type', 'Address', 'Contact', '']}
          rows={addresses}
          rowKey={(a) => a.id}
          renderRow={(a) => [
            <Chip key="t" size="small" label={a.addressType} variant="outlined" />,
            <Typography key="a" variant="body2">{oneLine(a)}</Typography>,
            <Typography key="c" variant="body2" color="text.secondary">{[a.contactPerson, a.contactPhone, a.contactEmail].filter(Boolean).join(' · ') || '—'}</Typography>,
            <Box key="x" sx={{ display: 'flex', gap: 0.5, justifyContent: 'flex-end' }}>
              <Button size="small" onClick={() => openEdit(a)}>Edit</Button>
              <IconButton size="small" color="error" onClick={() => del.mutate(a.id)} aria-label="Delete"><Delete fontSize="small" /></IconButton>
            </Box>,
          ]}
        />
      )}
      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? 'Edit address' : 'Add address'}</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TextInput label="Type" field="addressType" form={form as FormState} setForm={setForm as (f: FormState) => void} select required errors={errors}
            options={['registered', 'corporate', 'mailing', 'billing', 'other'].map((t) => ({ value: t, label: t.charAt(0).toUpperCase() + t.slice(1) }))} />
          <TextInput label="Address line 1" field="line1" form={form as FormState} setForm={setForm as (f: FormState) => void} required errors={errors} />
          <TextInput label="Address line 2" field="line2" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
          <TextInput label="Landmark" field="landmark" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
          <FormGrid>
            <TextInput label="City" field="city" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
            <TextInput label="District" field="district" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
          </FormGrid>
          <FormGrid>
            <TextInput label="State" field="state" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
            <TextInput label="Country" field="country" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
            <TextInput label="PIN code" field="pinCode" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
          </FormGrid>
          <Divider />
          <FormGrid>
            <TextInput label="Contact person" field="contactPerson" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
            <TextInput label="Contact email" field="contactEmail" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
            <TextInput label="Contact phone" field="contactPhone" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
          </FormGrid>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setOpen(false)}>Cancel</Button>
          <Button variant="contained" disabled={mut.isPending || !form.line1} onClick={() => mut.mutate()}>{mut.isPending ? 'Saving…' : 'Save'}</Button>
        </DialogActions>
      </Dialog>
    </SectionCard>
  )
}

export function ContactsPanel({ companyId, contacts }: { companyId: string; contacts: OrgContact[] }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<OrgContact | null>(null)
  const blank: ContactInput = { contactType: 'signatory', name: '', designation: '', email: '', phone: '' }
  const [form, setForm] = useState<ContactInput>(blank)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const invalidate = () => void qc.invalidateQueries({ queryKey: ['org'] })
  const mut = useMutation({
    mutationFn: async () => editing ? tenantApi.updateContact(companyId, editing.id, form) : tenantApi.addContact(companyId, form),
    onSuccess: () => { toast(editing ? 'Contact updated' : 'Contact added'); invalidate(); setOpen(false); setErrors({}) },
    onError: (e) => {
      if (e instanceof ApiError && e.details) setErrors(e.details)
      else toast(e instanceof Error ? e.message : 'Save failed', 'error')
    },
  })
  const del = useMutation({
    mutationFn: (id: string) => tenantApi.deleteContact(companyId, id),
    onSuccess: () => { toast('Contact removed'); invalidate() },
    onError: (e) => toast(e instanceof Error ? e.message : 'Delete failed', 'error'),
  })
  const openEdit = (c?: OrgContact) => { setEditing(c ?? null); setErrors({}); setForm(c ? { ...c } : blank); setOpen(true) }

  return (
    <SectionCard title="Contacts" subtitle="Authorised signatories and HR / payroll / finance contacts"
      actions={<Button size="small" variant="outlined" onClick={() => openEdit()}>Add contact</Button>}>
      {contacts.length === 0 ? <EmptyState title="No contacts" hint="Add signatories and function contacts." /> : (
        <DataList
          columns={['Type', 'Name', 'Designation', 'Email', 'Phone', '']}
          rows={contacts}
          rowKey={(c) => c.id}
          renderRow={(c) => [
            <Chip key="t" size="small" label={c.contactType} variant="outlined" />,
            <Typography key="n" variant="body2" sx={{ fontWeight: 600 }}>{c.name}</Typography>,
            <Typography key="d" variant="body2" color="text.secondary">{c.designation ?? '—'}</Typography>,
            <Typography key="e" variant="body2">{c.email ?? '—'}</Typography>,
            <Typography key="p" variant="body2" color="text.secondary">{c.phone ?? '—'}</Typography>,
            <Box key="x" sx={{ display: 'flex', gap: 0.5, justifyContent: 'flex-end' }}>
              <Button size="small" onClick={() => openEdit(c)}>Edit</Button>
              <IconButton size="small" color="error" onClick={() => del.mutate(c.id)} aria-label="Delete"><Delete fontSize="small" /></IconButton>
            </Box>,
          ]}
        />
      )}
      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? 'Edit contact' : 'Add contact'}</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TextInput label="Type" field="contactType" form={form as FormState} setForm={setForm as (f: FormState) => void} select required errors={errors}
            options={['signatory', 'hr', 'payroll', 'finance', 'other'].map((t) => ({ value: t, label: t.charAt(0).toUpperCase() + t.slice(1) }))} />
          <TextInput label="Name" field="name" form={form as FormState} setForm={setForm as (f: FormState) => void} required errors={errors} />
          <FormGrid>
            <TextInput label="Designation" field="designation" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
            <TextInput label="Email" field="email" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
            <TextInput label="Phone" field="phone" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
          </FormGrid>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setOpen(false)}>Cancel</Button>
          <Button variant="contained" disabled={mut.isPending || !form.name} onClick={() => mut.mutate()}>{mut.isPending ? 'Saving…' : 'Save'}</Button>
        </DialogActions>
      </Dialog>
    </SectionCard>
  )
}

export function BankAccountsPanel({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const toast = useToast()
  const accounts = useQuery({ queryKey: ['org', 'banks', companyId], queryFn: () => tenantApi.bankAccounts(companyId), retry: false })
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<BankAccount | null>(null)
  const blank: BankAccountInput = { bankName: '', branchName: '', accountNumber: '', ifsc: '', isPrimary: false }
  const [form, setForm] = useState<BankAccountInput>(blank)
  const [errors, setErrors] = useState<Record<string, string[]>>({})

  const rows = accounts.data ?? []
  const forbidden = accounts.error instanceof ApiError && accounts.error.status === 403
  const mut = useMutation({
    mutationFn: async () => editing ? tenantApi.updateBankAccount(companyId, editing.id, form) : tenantApi.addBankAccount(companyId, form),
    onSuccess: () => { toast(editing ? 'Account updated' : 'Account added'); void qc.invalidateQueries({ queryKey: ['org', 'banks', companyId] }); setOpen(false); setErrors({}) },
    onError: (e) => {
      if (e instanceof ApiError && e.details) setErrors(e.details)
      else toast(e instanceof Error ? e.message : 'Save failed', 'error')
    },
  })
  const del = useMutation({
    mutationFn: (id: string) => tenantApi.deleteBankAccount(companyId, id),
    onSuccess: () => { toast('Account removed'); void qc.invalidateQueries({ queryKey: ['org', 'banks', companyId] }) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Delete failed', 'error'),
  })
  const openEdit = (b?: BankAccount) => { setEditing(b ?? null); setErrors({}); setForm(b ? { ...b } : blank); setOpen(true) }

  return (
    <SectionCard title="Bank accounts" subtitle="Salary and billing accounts — visible only with company edit rights"
      actions={!forbidden ? <Button size="small" variant="outlined" onClick={() => openEdit()}>Add account</Button> : undefined}>
      {forbidden ? (
        <Alert severity="info" variant="outlined">You need the company edit permission to view bank accounts.</Alert>
      ) : rows.length === 0 ? <EmptyState title="No bank accounts" hint="Add the account used for salary payouts and invoicing." /> : (
        <DataList
          columns={['Bank', 'Account', 'IFSC', 'Primary', '']}
          rows={rows}
          rowKey={(b) => b.id}
          renderRow={(b) => [
            <Typography key="b" variant="body2" sx={{ fontWeight: 600 }}>{b.bankName}{b.branchName ? ` · ${b.branchName}` : ''}</Typography>,
            <Typography key="a" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12 }}>{b.accountNumber}</Typography>,
            <Typography key="i" variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12 }}>{b.ifsc ?? '—'}</Typography>,
            b.isPrimary ? <Chip key="p" size="small" color="primary" label="Primary" /> : <Typography key="p" variant="body2" color="text.secondary">—</Typography>,
            <Box key="x" sx={{ display: 'flex', gap: 0.5, justifyContent: 'flex-end' }}>
              <Button size="small" onClick={() => openEdit(b)}>Edit</Button>
              <IconButton size="small" color="error" onClick={() => del.mutate(b.id)} aria-label="Delete"><Delete fontSize="small" /></IconButton>
            </Box>,
          ]}
        />
      )}
      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? 'Edit bank account' : 'Add bank account'}</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <FormGrid>
            <TextInput label="Bank name" field="bankName" form={form as FormState} setForm={setForm as (f: FormState) => void} required errors={errors} />
            <TextInput label="Branch" field="branchName" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} />
          </FormGrid>
          <FormGrid>
            <TextInput label="Account number" field="accountNumber" form={form as FormState} setForm={setForm as (f: FormState) => void} required errors={errors} />
            <TextInput label="IFSC" field="ifsc" form={form as FormState} setForm={setForm as (f: FormState) => void} errors={errors} helperText="e.g. HDFC0001234" />
          </FormGrid>
          <FormControlLabel control={<Switch checked={form.isPrimary} onChange={(_, v) => setForm({ ...form, isPrimary: v })} />} label="Primary account" />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setOpen(false)}>Cancel</Button>
          <Button variant="contained" disabled={mut.isPending || !form.bankName || !form.accountNumber} onClick={() => mut.mutate()}>{mut.isPending ? 'Saving…' : 'Save'}</Button>
        </DialogActions>
      </Dialog>
    </SectionCard>
  )
}

export function DocumentsPanel({ owner, ownerId, canEdit }: { owner: 'companies' | 'branches'; ownerId: string; canEdit: boolean }) {
  const qc = useQueryClient()
  const toast = useToast()
  const docs = useQuery({ queryKey: ['org', 'docs', owner, ownerId], queryFn: () => tenantApi.documents(owner, ownerId) })
  const upload = useMutation({
    mutationFn: (file: File) => tenantApi.uploadDocument(owner, ownerId, file),
    onSuccess: () => { toast('Document uploaded'); void qc.invalidateQueries({ queryKey: ['org', 'docs', owner, ownerId] }) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Upload failed', 'error'),
  })
  const del = useMutation({
    mutationFn: (fid: string) => tenantApi.deleteDocument(owner, ownerId, fid),
    onSuccess: () => { toast('Document deleted'); void qc.invalidateQueries({ queryKey: ['org', 'docs', owner, ownerId] }) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Delete failed', 'error'),
  })

  return (
    <SectionCard title="Documents" subtitle="Certificates and licences (PDF or image, max 10 MB) — served through the authenticated file service, never public URLs"
      actions={canEdit ? (
        <Button size="small" variant="outlined" component="label" startIcon={<CloudUpload />} disabled={upload.isPending}>
          {upload.isPending ? 'Uploading…' : 'Upload'}
          <input hidden type="file" accept=".pdf,.png,.jpg,.jpeg" onChange={(e) => { const f = e.target.files?.[0]; if (f) upload.mutate(f); e.target.value = '' }} />
        </Button>
      ) : undefined}>
      {(docs.data ?? []).length === 0 ? <EmptyState title="No documents" hint="Upload registration certificates or licences." icon={<InsertDriveFile />} /> : (
        <DataList
          columns={['File', 'Type', 'Size', 'Uploaded', '']}
          rows={docs.data ?? []}
          rowKey={(d) => d.id}
          renderRow={(d: OrgDocument) => [
            <Typography key="f" variant="body2" sx={{ fontWeight: 600 }}>{d.fileName}</Typography>,
            <Typography key="t" variant="body2" color="text.secondary">{d.contentType}</Typography>,
            <Typography key="s" variant="body2" color="text.secondary">{(d.sizeBytes / 1024).toFixed(0)} KB</Typography>,
            <Typography key="u" variant="body2" color="text.secondary">{new Date(d.uploadedAt).toLocaleDateString()}</Typography>,
            <Box key="x" sx={{ display: 'flex', gap: 0.5, justifyContent: 'flex-end' }}>
              <Tooltip title="Download"><IconButton size="small" component="a" href={`/api/v1/org/${owner}/${ownerId}/documents/${d.id}/content`} download aria-label="Download"><Download fontSize="small" /></IconButton></Tooltip>
              {canEdit && <Tooltip title="Delete"><IconButton size="small" color="error" onClick={() => del.mutate(d.id)} aria-label="Delete"><Delete fontSize="small" /></IconButton></Tooltip>}
            </Box>,
          ]}
        />
      )}
    </SectionCard>
  )
}

export function LogoCard({ companyId, logoFileId, canEdit }: { companyId: string; logoFileId?: string | null; canEdit: boolean }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [preview, setPreview] = useState<string | null>(null)
  const [pendingFile, setPendingFile] = useState<File | null>(null)
  const logoSrc = pendingFile ? preview! : tenantApi.logoUrl(companyId) + `?v=${logoFileId ?? '0'}`

  const upload = useMutation({
    mutationFn: () => tenantApi.uploadLogo(companyId, pendingFile!),
    onSuccess: () => {
      toast('Logo uploaded')
      setPendingFile(null)
      void qc.invalidateQueries({ queryKey: ['org'] })
    },
    onError: (e) => toast(e instanceof Error ? e.message : 'Upload failed', 'error'),
  })
  const remove = useMutation({
    mutationFn: () => tenantApi.removeLogo(companyId),
    onSuccess: () => { toast('Logo removed'); void qc.invalidateQueries({ queryKey: ['org'] }) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Remove failed', 'error'),
  })

  const pick = (file?: File) => {
    if (!file) return
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) { toast('Choose a PNG, JPEG or WebP image', 'error'); return }
    if (file.size > 2 * 1024 * 1024) { toast('Logo must be under 2 MB', 'error'); return }
    setPreview(URL.createObjectURL(file))
    setPendingFile(file)
  }

  return (
    <SectionCard title="Logo" subtitle="PNG, JPEG or WebP · 16–4096 px · max 2 MB — shown on ID cards, letters and payslips">
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
        <Box sx={{
          width: 96, height: 96, borderRadius: 2, border: '1px dashed', borderColor: 'divider',
          display: 'flex', alignItems: 'center', justifyContent: 'center', overflow: 'hidden', bgcolor: 'action.hover',
        }}>
          {(logoFileId || pendingFile) ? (
            <Box component="img" src={logoSrc} alt="Company logo" sx={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }}
              onError={(e) => { (e.target as HTMLImageElement).style.display = 'none' }} />
          ) : (
            <Typography variant="caption" color="text.secondary">No logo</Typography>
          )}
        </Box>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
          {pendingFile ? (
            <>
              <Typography variant="caption" color="text.secondary">Preview — {pendingFile.name}</Typography>
              <Box sx={{ display: 'flex', gap: 1 }}>
                <Button size="small" variant="contained" onClick={() => upload.mutate()} disabled={upload.isPending}>{upload.isPending ? 'Uploading…' : 'Save logo'}</Button>
                <Button size="small" onClick={() => setPendingFile(null)}>Cancel</Button>
              </Box>
            </>
          ) : canEdit ? (
            <>
              <Button size="small" variant="outlined" component="label" startIcon={<CloudUpload />}>
                {logoFileId ? 'Replace' : 'Upload logo'}
                <input hidden type="file" accept="image/png,image/jpeg,image/webp" onChange={(e) => { pick(e.target.files?.[0]); e.target.value = '' }} />
              </Button>
              {logoFileId && <Button size="small" color="error" onClick={() => remove.mutate()} disabled={remove.isPending}>Remove</Button>}
            </>
          ) : null}
        </Box>
      </Box>
    </SectionCard>
  )
}

export function usePermissions(): Set<string> {
  const q = useQuery({ queryKey: ['me', 'permissions'], queryFn: tenantApi.permissions, staleTime: 60_000 })
  return useMemo(() => new Set(q.data ?? []), [q.data])
}

export function OpenLink({ to, label }: { to: string; label?: string }) {
  return <Button size="small" component="a" href={to} endIcon={<OpenInNew fontSize="small" />}>{label ?? 'Open'}</Button>
}

export function SaveBar({ saving, onSave, disabled }: { saving: boolean; onSave: () => void; disabled?: boolean }) {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'flex-end', mt: 1 }}>
      <Button variant="contained" onClick={onSave} disabled={saving || disabled}
        startIcon={saving ? <CircularProgress size={14} color="inherit" /> : undefined}>
        {saving ? 'Saving…' : 'Save changes'}
      </Button>
    </Box>
  )
}
