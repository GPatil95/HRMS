import { useState } from 'react'
import {
  Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, MenuItem, Tab, Table, TableBody, TableCell,
  TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { Add, Edit, Publish, Visibility } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  tenantApi, type AttendanceShift, type ShiftInput, type ShiftVersion, type ShiftVersionInput,
  type WorkCalendar, type WorkCalendarInput, type CalendarDay, type CalendarDayInput,
  type AttendancePolicy, type PolicyInput, type PolicyVersion, type AttendanceRules,
  type ShiftAssignmentInput, type PolicyAssignmentInput,
} from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'

const TF = (p: React.ComponentProps<typeof TextField>) => (
  <TextField size="small" fullWidth slotProps={{ inputLabel: { shrink: true } }} {...p} />
)
const onErr = (toast: (m: string, s?: 'success' | 'error') => void) => ({
  onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
})
const today = () => new Date().toISOString().slice(0, 10)

// ---------- Shifts ----------
const emptyShift = (): ShiftInput => ({ code: '', name: '', isActive: true })
const emptyShiftVersion = (): ShiftVersionInput => ({
  shiftType: 'fixed', startTime: '09:00', endTime: '18:00', breakMinutes: 60, breakPaid: false,
  coreStart: null, coreEnd: null, requiredMinutes: null, graceInMinutes: null, graceOutMinutes: null,
  inWindowBeforeMinutes: 120, outWindowAfterMinutes: 240, otAllowed: true, weeklyOffConfigJson: null,
})

function ShiftVersionDialog({ shiftId, version, onClose }: { shiftId: string; version: ShiftVersion | null; onClose: () => void }) {
  const toast = useToast(); const qc = useQueryClient()
  const [form, setForm] = useState<ShiftVersionInput>(version ? {
    shiftType: version.shiftType, startTime: version.startTime, endTime: version.endTime,
    breakMinutes: version.breakMinutes, breakPaid: version.breakPaid,
    coreStart: version.coreStart ?? null, coreEnd: version.coreEnd ?? null,
    requiredMinutes: version.requiredMinutes ?? null,
    graceInMinutes: version.graceInMinutes ?? null, graceOutMinutes: version.graceOutMinutes ?? null,
    inWindowBeforeMinutes: version.inWindowBeforeMinutes, outWindowAfterMinutes: version.outWindowAfterMinutes,
    otAllowed: version.otAllowed, weeklyOffConfigJson: version.weeklyOffConfigJson ?? null,
  } : emptyShiftVersion())
  const f = (k: keyof ShiftVersionInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm(s => ({ ...s, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }))
  const num = (k: keyof ShiftVersionInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm(s => ({ ...s, [k]: e.target.value === '' ? null : Number(e.target.value) }))
  const save = useMutation({
    mutationFn: () => version ? tenantApi.updateShiftVersion(version.id, form) : tenantApi.createShiftVersion(shiftId, form),
    onSuccess: () => { toast('Version saved'); void qc.invalidateQueries({ queryKey: ['shift-versions', shiftId] }); onClose() },
    ...onErr(toast),
  })
  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{version ? `Edit version ${version.version}` : 'New shift version'}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: 2 }}>
        <TF select label="Shift type" value={form.shiftType} onChange={f('shiftType')}>
          {['fixed', 'flexi', 'open'].map(t => <MenuItem key={t} value={t}>{t}</MenuItem>)}
        </TF>
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          <TF label="Start time" type="time" value={form.startTime} onChange={f('startTime')} />
          <TF label="End time" type="time" value={form.endTime} onChange={f('endTime')} />
          <TF label="Break minutes" type="number" value={form.breakMinutes} onChange={num('breakMinutes')} />
          <TF label="Required minutes (flexi/open)" type="number" value={form.requiredMinutes ?? ''} onChange={num('requiredMinutes')} />
          <TF label="Grace in (min, blank = policy)" type="number" value={form.graceInMinutes ?? ''} onChange={num('graceInMinutes')} />
          <TF label="Grace out (min, blank = policy)" type="number" value={form.graceOutMinutes ?? ''} onChange={num('graceOutMinutes')} />
          <TF label="In window before (min)" type="number" value={form.inWindowBeforeMinutes ?? ''} onChange={num('inWindowBeforeMinutes')} />
          <TF label="Out window after (min)" type="number" value={form.outWindowAfterMinutes ?? ''} onChange={num('outWindowAfterMinutes')} />
        </Box>
        {form.shiftType !== 'fixed' && (
          <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
            <TF label="Core start (flexi)" type="time" value={form.coreStart ?? ''} onChange={f('coreStart')} />
            <TF label="Core end (flexi)" type="time" value={form.coreEnd ?? ''} onChange={f('coreEnd')} />
          </Box>
        )}
        <FormControlLabel control={<Checkbox checked={form.breakPaid} onChange={f('breakPaid')} />} label="Break paid" />
        <FormControlLabel control={<Checkbox checked={form.otAllowed} onChange={f('otAllowed')} />} label="Overtime allowed on this shift" />
        <TF label="Weekly-off config JSON (used when org pattern is shift_based)" multiline minRows={2}
          value={form.weeklyOffConfigJson ?? ''} onChange={f('weeklyOffConfigJson')} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>
      </DialogActions>
    </Dialog>
  )
}

function ShiftVersionsPanel({ shift }: { shift: AttendanceShift }) {
  const toast = useToast(); const qc = useQueryClient()
  const q = useQuery({ queryKey: ['shift-versions', shift.id], queryFn: () => tenantApi.shiftVersions(shift.id) })
  const [editing, setEditing] = useState<ShiftVersion | null | undefined>(undefined)
  const [publishFor, setPublishFor] = useState<ShiftVersion | null>(null)
  const [publishFrom, setPublishFrom] = useState(today())
  const publish = useMutation({
    mutationFn: () => tenantApi.publishShiftVersion(publishFor!.id, publishFrom),
    onSuccess: () => { toast(`Published as of ${publishFrom}`); void qc.invalidateQueries({ queryKey: ['shift-versions', shift.id] }); void qc.invalidateQueries({ queryKey: ['shifts'] }); setPublishFor(null) },
    ...onErr(toast),
  })
  const retire = useMutation({
    mutationFn: (v: ShiftVersion) => tenantApi.retireShiftVersion(v.id, today()),
    onSuccess: () => { toast('Version retired'); void qc.invalidateQueries({ queryKey: ['shift-versions', shift.id] }) },
    ...onErr(toast),
  })
  return (
    <Box sx={{ mt: 2 }}>
      <SectionCard title={`Versions — ${shift.code}`}
        actions={<Button size="small" variant="contained" startIcon={<Add />} onClick={() => setEditing(null)}>New draft</Button>}>
        {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : !q.data?.length ? (
          <EmptyState title="No versions" hint="Create a draft, then publish it with an effective date." />
        ) : (
          <Table size="small"><TableHead><TableRow>
            <TableCell>Ver</TableCell><TableCell>Status</TableCell><TableCell>Window</TableCell>
            <TableCell>Times</TableCell><TableCell>Type</TableCell><TableCell>Grace</TableCell><TableCell />
          </TableRow></TableHead><TableBody>
            {q.data.map(v => (
              <TableRow key={v.id} hover>
                <TableCell>{v.version}</TableCell>
                <TableCell><StatusChip value={v.status} /></TableCell>
                <TableCell>{v.status === 'draft' ? '—' : `${v.effectiveFrom}${v.effectiveTo ? ` → ${v.effectiveTo}` : ' →'}`}</TableCell>
                <TableCell>{v.startTime}–{v.endTime}{v.crossesMidnight ? ' (overnight)' : ''}</TableCell>
                <TableCell>{v.shiftType}</TableCell>
                <TableCell>{v.graceInMinutes ?? 'policy'}/{v.graceOutMinutes ?? 'policy'}</TableCell>
                <TableCell align="right">
                  {v.status === 'draft' && <>
                    <Button size="small" startIcon={<Edit />} onClick={() => setEditing(v)}>Edit</Button>
                    <Button size="small" startIcon={<Publish />} onClick={() => { setPublishFor(v); setPublishFrom(today()) }}>Publish</Button>
                  </>}
                  {v.status === 'published' && <Button size="small" onClick={() => retire.mutate(v)}>Retire</Button>}
                </TableCell>
              </TableRow>
            ))}
          </TableBody></Table>
        )}
      </SectionCard>
      {editing !== undefined && <ShiftVersionDialog shiftId={shift.id} version={editing} onClose={() => setEditing(undefined)} />}
      {publishFor && (
        <Dialog open onClose={() => setPublishFor(null)}>
          <DialogTitle>Publish version {publishFor.version}</DialogTitle>
          <DialogContent sx={{ pt: 2 }}>
            <TF label="Effective from" type="date" value={publishFrom} onChange={e => setPublishFrom(e.target.value)} />
            <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
              The previous published window closes the day before. Backdating needs the backdate permission.
            </Typography>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setPublishFor(null)}>Cancel</Button>
            <Button variant="contained" onClick={() => publish.mutate()} disabled={publish.isPending}>Publish</Button>
          </DialogActions>
        </Dialog>
      )}
    </Box>
  )
}

function ShiftsTab() {
  const qc = useQueryClient(); const toast = useToast()
  const q = useQuery({ queryKey: ['shifts'], queryFn: tenantApi.shifts })
  const [dlg, setDlg] = useState<(ShiftInput & { id?: string }) | null>(null)
  const [expanded, setExpanded] = useState<AttendanceShift | null>(null)
  const f = (k: keyof ShiftInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg(d => d && { ...d, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const save = useMutation({
    mutationFn: () => dlg!.id ? tenantApi.updateShift(dlg!.id, dlg!) : tenantApi.createShift(dlg!),
    onSuccess: () => { toast('Shift saved'); void qc.invalidateQueries({ queryKey: ['shifts'] }); setDlg(null) },
    ...onErr(toast),
  })
  return (
    <SectionCard title="Shifts" actions={
      <Button size="small" variant="contained" startIcon={<Add />} onClick={() => setDlg(emptyShift())}>New shift</Button>}>
      {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : !q.data?.length ? (
        <EmptyState title="No shifts" hint="Create a shift, then publish a version with its effective window." />
      ) : (
        <Table size="small"><TableHead><TableRow>
          <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Versions</TableCell>
          <TableCell>Published window</TableCell><TableCell>Status</TableCell><TableCell />
        </TableRow></TableHead><TableBody>
          {q.data.map(s => (
            <TableRow key={s.id} hover>
              <TableCell><code>{s.code}</code></TableCell>
              <TableCell>{s.name}</TableCell>
              <TableCell>{s.versionCount}</TableCell>
              <TableCell>{s.activeVersion ? `v${s.activeVersion} · ${s.activeFrom}${s.activeTo ? ` → ${s.activeTo}` : ' →'}` : '—'}</TableCell>
              <TableCell><StatusChip value={s.isActive ? 'active' : 'inactive'} /></TableCell>
              <TableCell align="right">
                <Button size="small" startIcon={<Visibility />} onClick={() => setExpanded(expanded?.id === s.id ? null : s)}>Versions</Button>
                <Button size="small" startIcon={<Edit />} onClick={() => setDlg({ id: s.id, code: s.code, name: s.name, isActive: s.isActive })}>Edit</Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody></Table>
      )}
      {expanded && <ShiftVersionsPanel shift={expanded} />}
      {dlg && (
        <Dialog open onClose={() => setDlg(null)}>
          <DialogTitle>{dlg.id ? 'Edit shift' : 'New shift'}</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: 2, minWidth: 360 }}>
            <TF label="Code" value={dlg.code} onChange={f('code')} disabled={!!dlg.id} />
            <TF label="Name" value={dlg.name} onChange={f('name')} />
            <FormControlLabel control={<Checkbox checked={dlg.isActive} onChange={f('isActive')} />} label="Active" />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setDlg(null)}>Cancel</Button>
            <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>
          </DialogActions>
        </Dialog>
      )}
    </SectionCard>
  )
}

// ---------- Calendars ----------
const emptyCal = (): WorkCalendarInput => ({ code: '', name: '', year: null, isDefault: false, isActive: true })
const emptyDay = (date: string): CalendarDayInput => ({ date, dayType: 'holiday', name: '', halfDayMinutes: null, appliesToJson: null })

function CalendarDaysPanel({ cal }: { cal: WorkCalendar }) {
  const toast = useToast(); const qc = useQueryClient()
  const [year, setYear] = useState(cal.year ?? new Date().getFullYear())
  const q = useQuery({ queryKey: ['calendar-days', cal.id, year], queryFn: () => tenantApi.calendarDays(cal.id, `${year}-01-01`, `${year}-12-31`) })
  const [dlg, setDlg] = useState<CalendarDayInput | null>(null)
  const f = (k: keyof CalendarDayInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg(d => d && { ...d, [k]: k === 'halfDayMinutes' ? (e.target.value === '' ? null : Number(e.target.value)) : e.target.value })
  const save = useMutation({
    mutationFn: () => tenantApi.upsertCalendarDays(cal.id, [dlg!]),
    onSuccess: () => { toast('Day saved'); void qc.invalidateQueries({ queryKey: ['calendar-days', cal.id] }); setDlg(null) },
    ...onErr(toast),
  })
  const remove = useMutation({
    mutationFn: (d: CalendarDay) => tenantApi.deleteCalendarDay(cal.id, d.date),
    onSuccess: () => { toast('Day removed'); void qc.invalidateQueries({ queryKey: ['calendar-days', cal.id] }) },
    ...onErr(toast),
  })
  const importCsv = useMutation({
    mutationFn: async (text: string) => {
      const days = text.split(/\r?\n/).map(l => l.trim()).filter(Boolean)
        .map(l => l.split(',').map(c => c.trim()))
        .filter(c => /^\d{4}-\d{2}-\d{2}$/.test(c[0]))
        .map(c => ({ date: c[0], dayType: c[2] || 'holiday', name: c[1] || c[2] || 'Holiday' }) as CalendarDayInput)
      return tenantApi.upsertCalendarDays(cal.id, days)
    },
    onSuccess: r => { toast(`Imported ${r.upserted} days`); void qc.invalidateQueries({ queryKey: ['calendar-days', cal.id] }) },
    ...onErr(toast),
  })
  return (
    <Box sx={{ mt: 2 }}>
      <SectionCard title={`Days — ${cal.code}`} actions={
        <Box sx={{ display: 'flex', gap: 1 }}>
          <TF label="Year" type="number" value={year} onChange={e => setYear(Number(e.target.value))} sx={{ width: 110 }} />
          <Button component="label" size="small">Import CSV<input hidden type="file" accept=".csv" onChange={e => { const f = e.target.files?.[0]; if (f) void f.text().then(t => importCsv.mutate(t)) }} /></Button>
          <Button size="small" variant="contained" startIcon={<Add />} onClick={() => setDlg(emptyDay(today()))}>Add day</Button>
        </Box>}>
        {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : !q.data?.length ? (
          <EmptyState title="No exception days" hint="Add holidays, half days or working overrides. CSV: date,name,type." />
        ) : (
          <Table size="small"><TableHead><TableRow>
            <TableCell>Date</TableCell><TableCell>Type</TableCell><TableCell>Name</TableCell><TableCell>Half-day min</TableCell><TableCell />
          </TableRow></TableHead><TableBody>
            {q.data.map(d => (
              <TableRow key={d.id} hover>
                <TableCell>{d.date}</TableCell>
                <TableCell><StatusChip value={d.dayType} /></TableCell>
                <TableCell>{d.name}</TableCell>
                <TableCell>{d.halfDayMinutes ?? '—'}</TableCell>
                <TableCell align="right">
                  <Button size="small" startIcon={<Edit />} onClick={() => setDlg({ date: d.date, dayType: d.dayType, name: d.name, halfDayMinutes: d.halfDayMinutes ?? null, appliesToJson: d.appliesToJson ?? null })}>Edit</Button>
                  <Button size="small" color="error" onClick={() => remove.mutate(d)}>Delete</Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody></Table>
        )}
      </SectionCard>
      {dlg && (
        <Dialog open onClose={() => setDlg(null)}>
          <DialogTitle>Calendar day</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: 2, minWidth: 360 }}>
            <TF label="Date" type="date" value={dlg.date} onChange={f('date')} />
            <TF select label="Type" value={dlg.dayType} onChange={f('dayType')}>
              {[['holiday', 'Holiday'], ['optional_holiday', 'Optional holiday'], ['working_override', 'Working override'], ['half_day', 'Half day']].map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}
            </TF>
            <TF label="Name" value={dlg.name} onChange={f('name')} />
            {dlg.dayType === 'half_day' && <TF label="Planned minutes" type="number" value={dlg.halfDayMinutes ?? ''} onChange={f('halfDayMinutes')} />}
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setDlg(null)}>Cancel</Button>
            <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>
          </DialogActions>
        </Dialog>
      )}
    </Box>
  )
}

function CalendarsTab() {
  const qc = useQueryClient(); const toast = useToast()
  const q = useQuery({ queryKey: ['calendars'], queryFn: tenantApi.calendars })
  const [dlg, setDlg] = useState<(WorkCalendarInput & { id?: string }) | null>(null)
  const [expanded, setExpanded] = useState<WorkCalendar | null>(null)
  const f = (k: keyof WorkCalendarInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg(d => d && { ...d, [k]: e.target.type === 'checkbox' ? e.target.checked : k === 'year' ? (e.target.value === '' ? null : Number(e.target.value)) : e.target.value })
  const save = useMutation({
    mutationFn: () => dlg!.id ? tenantApi.updateCalendar(dlg!.id, dlg!) : tenantApi.createCalendar(dlg!),
    onSuccess: () => { toast('Calendar saved'); void qc.invalidateQueries({ queryKey: ['calendars'] }); setDlg(null) },
    ...onErr(toast),
  })
  return (
    <SectionCard title="Work calendars" actions={
      <Button size="small" variant="contained" startIcon={<Add />} onClick={() => setDlg(emptyCal())}>New calendar</Button>}>
      {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : !q.data?.length ? (
        <EmptyState title="No calendars" hint="Create a holiday calendar (org masters attach it to company/branch/dept)." />
      ) : (
        <Table size="small"><TableHead><TableRow>
          <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Year</TableCell>
          <TableCell>Days</TableCell><TableCell>Default</TableCell><TableCell>Status</TableCell><TableCell />
        </TableRow></TableHead><TableBody>
          {q.data.map(c => (
            <TableRow key={c.id} hover>
              <TableCell><code>{c.code}</code></TableCell>
              <TableCell>{c.name}</TableCell>
              <TableCell>{c.year ?? '—'}</TableCell>
              <TableCell>{c.dayCount}</TableCell>
              <TableCell>{c.isDefault ? 'Yes' : '—'}</TableCell>
              <TableCell><StatusChip value={c.isActive ? 'active' : 'inactive'} /></TableCell>
              <TableCell align="right">
                <Button size="small" onClick={() => setExpanded(expanded?.id === c.id ? null : c)}>Days</Button>
                <Button size="small" startIcon={<Edit />} onClick={() => setDlg({ id: c.id, code: c.code, name: c.name, year: c.year, isDefault: c.isDefault, isActive: c.isActive })}>Edit</Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody></Table>
      )}
      {expanded && <CalendarDaysPanel cal={expanded} />}
      {dlg && (
        <Dialog open onClose={() => setDlg(null)}>
          <DialogTitle>{dlg.id ? 'Edit calendar' : 'New calendar'}</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: 2, minWidth: 360 }}>
            <TF label="Code" value={dlg.code} onChange={f('code')} disabled={!!dlg.id} />
            <TF label="Name" value={dlg.name} onChange={f('name')} />
            <TF label="Year (blank = recurring)" type="number" value={dlg.year ?? ''} onChange={f('year')} />
            <FormControlLabel control={<Checkbox checked={dlg.isDefault} onChange={f('isDefault')} />} label="Tenant default" />
            <FormControlLabel control={<Checkbox checked={dlg.isActive} onChange={f('isActive')} />} label="Active" />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setDlg(null)}>Cancel</Button>
            <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>
          </DialogActions>
        </Dialog>
      )}
    </SectionCard>
  )
}

// ---------- Policies ----------
const emptyPolicy = (): PolicyInput => ({ code: '', name: '', isDefault: false, isActive: true })
const defaultRules = (): AttendanceRules => ({
  schema: 1,
  late: { graceMinutes: 10, markAfterMinutes: 0, countFrom: 'planned' },
  early: { graceMinutes: 10, markAfterMinutes: 0, countFrom: 'planned' },
  thresholds: { halfDayBelowMinutes: 240, absentBelowMinutes: 120, basis: 'worked' },
  punch: { mode: 'first_last', duplicateWindowSeconds: 60, minSessionMinutes: 5 },
  missingPunch: { status: 'missing_punch', lockTreatment: 'absent', reminderAfterHours: 4 },
  overtime: { enabled: false, minMinutes: 30, roundingBlockMinutes: 15, roundingMode: 'floor', dailyCapMinutes: 240, weeklyCapMinutes: 720, monthlyCapMinutes: 3000, requiresApproval: true, disposition: 'paid' },
  regularization: { windowDays: 7, maxPerPeriod: 4, hrApproval: 'when_quota_exceeded', escalateAfterDays: 3 },
  selfPunch: { allowed: true, geofenceRequired: false, selfieRequired: false },
  payableDaysBasis: 'calendar',
})

function RulesEditor({ value, onChange }: { value: AttendanceRules; onChange: (r: AttendanceRules) => void }) {
  const set = (section: 'late' | 'early', k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    onChange({ ...value, [section]: { ...value[section], [k]: k.startsWith('count') ? e.target.value : Number(e.target.value) } })
  const setT = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    onChange({ ...value, thresholds: { ...value.thresholds, [k]: e.target.value === '' ? 0 : Number(e.target.value) } })
  const setP = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    onChange({ ...value, punch: { ...value.punch, [k]: k === 'mode' ? e.target.value : Number(e.target.value) } })
  const setM = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    onChange({ ...value, missingPunch: { ...value.missingPunch, [k]: k === 'reminderAfterHours' ? Number(e.target.value) : e.target.value } })
  const setO = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    onChange({ ...value, overtime: { ...value.overtime, [k]: e.target.type === 'checkbox' ? e.target.checked : k === 'disposition' || k.endsWith('Band') ? e.target.value : Number(e.target.value) } })
  const setR = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    onChange({ ...value, regularization: { ...value.regularization, [k]: k === 'hrApproval' ? e.target.value : Number(e.target.value) } })
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <Typography variant="subtitle2">Late / early</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 2 }}>
        <TF label="Late grace (min)" type="number" value={value.late?.graceMinutes ?? 0} onChange={set('late', 'graceMinutes')} />
        <TF label="Early grace (min)" type="number" value={value.early?.graceMinutes ?? 0} onChange={set('early', 'graceMinutes')} />
        <TF label="Late mark after (min)" type="number" value={value.late?.markAfterMinutes ?? 0} onChange={set('late', 'markAfterMinutes')} />
        <TF label="Early mark after (min)" type="number" value={value.early?.markAfterMinutes ?? 0} onChange={set('early', 'markAfterMinutes')} />
      </Box>
      <Typography variant="subtitle2">Day thresholds</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 2 }}>
        <TF label="Half day below (min)" type="number" value={value.thresholds?.halfDayBelowMinutes ?? 0} onChange={setT('halfDayBelowMinutes')} />
        <TF label="Absent below (min)" type="number" value={value.thresholds?.absentBelowMinutes ?? 0} onChange={setT('absentBelowMinutes')} />
        <TF select label="Basis" value={value.thresholds?.basis ?? 'worked'} onChange={e => onChange({ ...value, thresholds: { ...value.thresholds, basis: e.target.value } })}>
          {['worked', 'span'].map(v => <MenuItem key={v} value={v}>{v}</MenuItem>)}
        </TF>
      </Box>
      <Typography variant="subtitle2">Punch handling</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 2 }}>
        <TF select label="Mode" value={value.punch?.mode ?? 'first_last'} onChange={setP('mode')}>
          {['first_last', 'pairs'].map(v => <MenuItem key={v} value={v}>{v}</MenuItem>)}
        </TF>
        <TF label="Duplicate window (sec)" type="number" value={value.punch?.duplicateWindowSeconds ?? 0} onChange={setP('duplicateWindowSeconds')} />
        <TF label="Min session (min)" type="number" value={value.punch?.minSessionMinutes ?? 0} onChange={setP('minSessionMinutes')} />
      </Box>
      <Typography variant="subtitle2">Missing punch</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 2 }}>
        <TF select label="Status" value={value.missingPunch?.status ?? 'missing_punch'} onChange={setM('status')}>
          {['missing_punch', 'absent', 'half_day', 'present'].map(v => <MenuItem key={v} value={v}>{v}</MenuItem>)}
        </TF>
        <TF select label="Lock treatment" value={value.missingPunch?.lockTreatment ?? 'absent'} onChange={setM('lockTreatment')}>
          {['absent', 'half_day', 'lop', 'present'].map(v => <MenuItem key={v} value={v}>{v}</MenuItem>)}
        </TF>
        <TF label="Reminder after (hrs)" type="number" value={value.missingPunch?.reminderAfterHours ?? 0} onChange={setM('reminderAfterHours')} />
      </Box>
      <Typography variant="subtitle2">Overtime</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 2 }}>
        <TF label="Min (min)" type="number" value={Number(value.overtime?.minMinutes ?? 0)} onChange={setO('minMinutes')} />
        <TF label="Rounding block" type="number" value={Number(value.overtime?.roundingBlockMinutes ?? 0)} onChange={setO('roundingBlockMinutes')} />
        <TF select label="Rounding" value={String(value.overtime?.roundingMode ?? 'floor')} onChange={setO('roundingMode')}>
          {['floor', 'nearest', 'ceil'].map(v => <MenuItem key={v} value={v}>{v}</MenuItem>)}
        </TF>
        <TF select label="Disposition" value={String(value.overtime?.disposition ?? 'paid')} onChange={setO('disposition')}>
          {['paid', 'comp_off'].map(v => <MenuItem key={v} value={v}>{v}</MenuItem>)}
        </TF>
        <TF label="Daily cap" type="number" value={Number(value.overtime?.dailyCapMinutes ?? 0)} onChange={setO('dailyCapMinutes')} />
        <TF label="Weekly cap" type="number" value={Number(value.overtime?.weeklyCapMinutes ?? 0)} onChange={setO('weeklyCapMinutes')} />
        <TF label="Monthly cap" type="number" value={Number(value.overtime?.monthlyCapMinutes ?? 0)} onChange={setO('monthlyCapMinutes')} />
        <FormControlLabel control={<Checkbox checked={Boolean(value.overtime?.enabled)} onChange={setO('enabled')} />} label="OT enabled" />
      </Box>
      <Typography variant="subtitle2">Regularization</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 2 }}>
        <TF label="Window (days)" type="number" value={Number(value.regularization?.windowDays ?? 0)} onChange={setR('windowDays')} />
        <TF label="Max per period" type="number" value={Number(value.regularization?.maxPerPeriod ?? 0)} onChange={setR('maxPerPeriod')} />
        <TF select label="HR approval" value={String(value.regularization?.hrApproval ?? 'when_quota_exceeded')} onChange={setR('hrApproval')}>
          {['always', 'never', 'when_quota_exceeded'].map(v => <MenuItem key={v} value={v}>{v}</MenuItem>)}
        </TF>
        <TF label="Escalate after (days)" type="number" value={Number(value.regularization?.escalateAfterDays ?? 0)} onChange={setR('escalateAfterDays')} />
      </Box>
      <TF select label="Payable days basis" value={value.payableDaysBasis ?? 'calendar'} onChange={e => onChange({ ...value, payableDaysBasis: e.target.value })}>
        {['calendar', 'working'].map(v => <MenuItem key={v} value={v}>{v}</MenuItem>)}
      </TF>
    </Box>
  )
}

function PolicyVersionsPanel({ policy }: { policy: AttendancePolicy }) {
  const toast = useToast(); const qc = useQueryClient()
  const q = useQuery({ queryKey: ['policy-versions', policy.id], queryFn: () => tenantApi.policyVersions(policy.id) })
  const [editing, setEditing] = useState<PolicyVersion | null | undefined>(undefined)
  const [form, setForm] = useState<AttendanceRules>(defaultRules())
  const [publishFor, setPublishFor] = useState<PolicyVersion | null>(null)
  const [publishFrom, setPublishFrom] = useState(today())
  const save = useMutation({
    mutationFn: () => editing ? tenantApi.updatePolicyVersion(editing.id, { rules: form }) : tenantApi.createPolicyVersion(policy.id, { rules: form }),
    onSuccess: () => { toast('Version saved'); void qc.invalidateQueries({ queryKey: ['policy-versions', policy.id] }); setEditing(undefined) },
    ...onErr(toast),
  })
  const publish = useMutation({
    mutationFn: () => tenantApi.publishPolicyVersion(publishFor!.id, publishFrom),
    onSuccess: () => { toast(`Published as of ${publishFrom}`); void qc.invalidateQueries({ queryKey: ['policy-versions', policy.id] }); void qc.invalidateQueries({ queryKey: ['policies'] }); setPublishFor(null) },
    ...onErr(toast),
  })
  const retire = useMutation({
    mutationFn: (v: PolicyVersion) => tenantApi.retirePolicyVersion(v.id, today()),
    onSuccess: () => { toast('Version retired'); void qc.invalidateQueries({ queryKey: ['policy-versions', policy.id] }) },
    ...onErr(toast),
  })
  return (
    <Box sx={{ mt: 2 }}>
      <SectionCard title={`Versions — ${policy.code}`}
        actions={<Button size="small" variant="contained" startIcon={<Add />} onClick={() => { setEditing(null); setForm(defaultRules()) }}>New draft</Button>}>
        {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : !q.data?.length ? (
          <EmptyState title="No versions" hint="Create a draft with the rule set, then publish." />
        ) : (
          <Table size="small"><TableHead><TableRow>
            <TableCell>Ver</TableCell><TableCell>Status</TableCell><TableCell>Window</TableCell>
            <TableCell>Grace in/out</TableCell><TableCell>Half &lt;</TableCell><TableCell>Absent &lt;</TableCell><TableCell />
          </TableRow></TableHead><TableBody>
            {q.data.map(v => (
              <TableRow key={v.id} hover>
                <TableCell>{v.version}</TableCell>
                <TableCell><StatusChip value={v.status} /></TableCell>
                <TableCell>{v.status === 'draft' ? '—' : `${v.effectiveFrom}${v.effectiveTo ? ` → ${v.effectiveTo}` : ' →'}`}</TableCell>
                <TableCell>{v.rules.late?.graceMinutes ?? 0}/{v.rules.early?.graceMinutes ?? 0}m</TableCell>
                <TableCell>{v.rules.thresholds?.halfDayBelowMinutes ?? 0}m</TableCell>
                <TableCell>{v.rules.thresholds?.absentBelowMinutes ?? 0}m</TableCell>
                <TableCell align="right">
                  {v.status === 'draft' && <>
                    <Button size="small" startIcon={<Edit />} onClick={() => { setEditing(v); setForm(v.rules) }}>Edit</Button>
                    <Button size="small" startIcon={<Publish />} onClick={() => { setPublishFor(v); setPublishFrom(today()) }}>Publish</Button>
                  </>}
                  {v.status === 'published' && <Button size="small" onClick={() => retire.mutate(v)}>Retire</Button>}
                </TableCell>
              </TableRow>
            ))}
          </TableBody></Table>
        )}
      </SectionCard>
      {editing !== undefined && (
        <Dialog open onClose={() => setEditing(undefined)} maxWidth="md" fullWidth>
          <DialogTitle>{editing ? `Edit version ${editing.version}` : 'New policy version'}</DialogTitle>
          <DialogContent sx={{ pt: 2 }}><RulesEditor value={form} onChange={setForm} /></DialogContent>
          <DialogActions>
            <Button onClick={() => setEditing(undefined)}>Cancel</Button>
            <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>
          </DialogActions>
        </Dialog>
      )}
      {publishFor && (
        <Dialog open onClose={() => setPublishFor(null)}>
          <DialogTitle>Publish version {publishFor.version}</DialogTitle>
          <DialogContent sx={{ pt: 2 }}>
            <TF label="Effective from" type="date" value={publishFrom} onChange={e => setPublishFrom(e.target.value)} />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setPublishFor(null)}>Cancel</Button>
            <Button variant="contained" onClick={() => publish.mutate()} disabled={publish.isPending}>Publish</Button>
          </DialogActions>
        </Dialog>
      )}
    </Box>
  )
}

function PoliciesTab() {
  const qc = useQueryClient(); const toast = useToast()
  const q = useQuery({ queryKey: ['policies'], queryFn: tenantApi.policies })
  const [dlg, setDlg] = useState<(PolicyInput & { id?: string }) | null>(null)
  const [expanded, setExpanded] = useState<AttendancePolicy | null>(null)
  const f = (k: keyof PolicyInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg(d => d && { ...d, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const save = useMutation({
    mutationFn: () => dlg!.id ? tenantApi.updatePolicy(dlg!.id, dlg!) : tenantApi.createPolicy(dlg!),
    onSuccess: () => { toast('Policy saved'); void qc.invalidateQueries({ queryKey: ['policies'] }); setDlg(null) },
    ...onErr(toast),
  })
  return (
    <SectionCard title="Attendance policies" actions={
      <Button size="small" variant="contained" startIcon={<Add />} onClick={() => setDlg(emptyPolicy())}>New policy</Button>}>
      {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : !q.data?.length ? (
        <EmptyState title="No policies" hint="Policies hold the typed rules set (grace, thresholds, OT, regularization)." />
      ) : (
        <Table size="small"><TableHead><TableRow>
          <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Versions</TableCell>
          <TableCell>Active version</TableCell><TableCell>Default</TableCell><TableCell>Status</TableCell><TableCell />
        </TableRow></TableHead><TableBody>
          {q.data.map(p => (
            <TableRow key={p.id} hover>
              <TableCell><code>{p.code}</code></TableCell>
              <TableCell>{p.name}</TableCell>
              <TableCell>{p.versionCount}</TableCell>
              <TableCell>{p.activeVersion ? `v${p.activeVersion}` : '—'}</TableCell>
              <TableCell>{p.isDefault ? 'Yes' : '—'}</TableCell>
              <TableCell><StatusChip value={p.isActive ? 'active' : 'inactive'} /></TableCell>
              <TableCell align="right">
                <Button size="small" onClick={() => setExpanded(expanded?.id === p.id ? null : p)}>Versions</Button>
                <Button size="small" startIcon={<Edit />} onClick={() => setDlg({ id: p.id, code: p.code, name: p.name, isDefault: p.isDefault, isActive: p.isActive })}>Edit</Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody></Table>
      )}
      {expanded && <PolicyVersionsPanel policy={expanded} />}
      {dlg && (
        <Dialog open onClose={() => setDlg(null)}>
          <DialogTitle>{dlg.id ? 'Edit policy' : 'New policy'}</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: 2, minWidth: 360 }}>
            <TF label="Code" value={dlg.code} onChange={f('code')} disabled={!!dlg.id} />
            <TF label="Name" value={dlg.name} onChange={f('name')} />
            <FormControlLabel control={<Checkbox checked={dlg.isDefault} onChange={f('isDefault')} />} label="Tenant default" />
            <FormControlLabel control={<Checkbox checked={dlg.isActive} onChange={f('isActive')} />} label="Active" />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setDlg(null)}>Cancel</Button>
            <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>
          </DialogActions>
        </Dialog>
      )}
    </SectionCard>
  )
}

// ---------- Assignments ----------
function AssignmentsTab() {
  const qc = useQueryClient(); const toast = useToast()
  const emps = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const shifts = useQuery({ queryKey: ['shifts'], queryFn: tenantApi.shifts })
  const policies = useQuery({ queryKey: ['policies'], queryFn: tenantApi.policies })
  const assignments = useQuery({ queryKey: ['shift-assignments'], queryFn: () => tenantApi.shiftAssignments() })
  const policyAssignments = useQuery({ queryKey: ['policy-assignments'], queryFn: () => tenantApi.policyAssignments() })
  const [shiftDlg, setShiftDlg] = useState<ShiftAssignmentInput | null>(null)
  const [policyDlg, setPolicyDlg] = useState<PolicyAssignmentInput | null>(null)
  const [rosterEmp, setRosterEmp] = useState('')
  const [rosterFrom, setRosterFrom] = useState(today().slice(0, 8) + '01')
  const [rosterTo, setRosterTo] = useState(today().slice(0, 8) + '28')
  const roster = useQuery({
    queryKey: ['roster', rosterEmp, rosterFrom, rosterTo],
    queryFn: () => tenantApi.roster(rosterEmp, rosterFrom, rosterTo),
    enabled: !!rosterEmp,
  })
  const saveShift = useMutation({
    mutationFn: () => tenantApi.createShiftAssignment(shiftDlg!),
    onSuccess: () => { toast('Assignment saved'); void qc.invalidateQueries({ queryKey: ['shift-assignments'] }); setShiftDlg(null) },
    ...onErr(toast),
  })
  const savePolicy = useMutation({
    mutationFn: () => tenantApi.createPolicyAssignment(policyDlg!),
    onSuccess: () => { toast('Assignment saved'); void qc.invalidateQueries({ queryKey: ['policy-assignments'] }); setPolicyDlg(null) },
    ...onErr(toast),
  })
  const endShift = useMutation({
    mutationFn: (id: string) => tenantApi.endShiftAssignment(id, today()),
    onSuccess: () => { toast('Ended'); void qc.invalidateQueries({ queryKey: ['shift-assignments'] }) }, ...onErr(toast),
  })
  const endPolicy = useMutation({
    mutationFn: (id: string) => tenantApi.endPolicyAssignment(id, today()),
    onSuccess: () => { toast('Ended'); void qc.invalidateQueries({ queryKey: ['policy-assignments'] }) }, ...onErr(toast),
  })
  const empOptions = emps.data?.filter(e => e.status !== 'exited') ?? []
  const picker = (val: string, set: (v: string) => void, label = 'Employee') => (
    <TF select label={label} value={val} onChange={e => set(e.target.value)}>
      {empOptions.map(e => <MenuItem key={e.id} value={e.id}>{e.employeeCode} — {e.displayName}</MenuItem>)}
    </TF>
  )
  return (
    <>
      <SectionCard title="Shift assignments" actions={
        <Button size="small" variant="contained" startIcon={<Add />}
          onClick={() => empOptions.length && setShiftDlg({ employeeId: empOptions[0].id, shiftId: shifts.data?.[0]?.id ?? '', effectiveFrom: today() })}>New assignment</Button>}>
        {assignments.isLoading ? <TableSkeleton /> : assignments.isError ? <ErrorAlert error={assignments.error} /> : !assignments.data?.length ? (
          <EmptyState title="No shift assignments" hint="Without an assignment, the org default shift applies." />
        ) : (
          <Table size="small"><TableHead><TableRow>
            <TableCell>Employee</TableCell><TableCell>Shift</TableCell><TableCell>Window</TableCell><TableCell>Source</TableCell><TableCell />
          </TableRow></TableHead><TableBody>
            {assignments.data.map(a => (
              <TableRow key={a.id} hover>
                <TableCell>{a.employeeCode} — {a.employeeName}</TableCell>
                <TableCell><code>{a.shiftCode}</code> {a.shiftName}</TableCell>
                <TableCell>{a.effectiveFrom}{a.effectiveTo ? ` → ${a.effectiveTo}` : ' →'}</TableCell>
                <TableCell>{a.source}</TableCell>
                <TableCell align="right">{!a.effectiveTo && <Button size="small" onClick={() => endShift.mutate(a.id)}>End today</Button>}</TableCell>
              </TableRow>
            ))}
          </TableBody></Table>
        )}
      </SectionCard>
      <SectionCard title="Policy assignments" sx={{ mt: 2 }} actions={
        <Button size="small" variant="contained" startIcon={<Add />}
          onClick={() => empOptions.length && setPolicyDlg({ employeeId: empOptions[0].id, policyId: policies.data?.[0]?.id ?? '', effectiveFrom: today() })}>New assignment</Button>}>
        {policyAssignments.isLoading ? <TableSkeleton /> : policyAssignments.isError ? <ErrorAlert error={policyAssignments.error} /> : !policyAssignments.data?.length ? (
          <EmptyState title="No policy overrides" hint="Without one, the policy resolves via org policy codes." />
        ) : (
          <Table size="small"><TableHead><TableRow>
            <TableCell>Employee</TableCell><TableCell>Policy</TableCell><TableCell>Window</TableCell><TableCell />
          </TableRow></TableHead><TableBody>
            {policyAssignments.data.map(a => (
              <TableRow key={a.id} hover>
                <TableCell>{a.employeeCode} — {a.employeeName}</TableCell>
                <TableCell><code>{a.policyCode}</code> {a.policyName}</TableCell>
                <TableCell>{a.effectiveFrom}{a.effectiveTo ? ` → ${a.effectiveTo}` : ' →'}</TableCell>
                <TableCell align="right">{!a.effectiveTo && <Button size="small" onClick={() => endPolicy.mutate(a.id)}>End today</Button>}</TableCell>
              </TableRow>
            ))}
          </TableBody></Table>
        )}
      </SectionCard>
      <SectionCard title="Shift roster" sx={{ mt: 2 }}
        actions={<Box sx={{ display: 'flex', gap: 2 }}>
          <Box sx={{ minWidth: 220 }}>{picker(rosterEmp, setRosterEmp)}</Box>
          <TF label="From" type="date" value={rosterFrom} onChange={e => setRosterFrom(e.target.value)} sx={{ width: 160 }} />
          <TF label="To" type="date" value={rosterTo} onChange={e => setRosterTo(e.target.value)} sx={{ width: 160 }} />
        </Box>}>
        {!rosterEmp ? <EmptyState title="Pick an employee" hint="Per-day roster overrides win over assignments and weekly-offs." />
          : roster.isLoading ? <TableSkeleton /> : roster.isError ? <ErrorAlert error={roster.error} />
          : !roster.data?.length ? <EmptyState title="No roster rows" hint="Use the API bulk upsert to fill a period." />
          : (
            <Table size="small"><TableHead><TableRow>
              <TableCell>Date</TableCell><TableCell>Shift</TableCell><TableCell>Off?</TableCell><TableCell>Reason</TableCell><TableCell>Source</TableCell>
            </TableRow></TableHead><TableBody>
              {roster.data.map(r => (
                <TableRow key={`${r.employeeId}-${r.workDate}`} hover>
                  <TableCell>{r.workDate}</TableCell>
                  <TableCell>{r.isOff ? '—' : (shifts.data?.find(s => s.id === r.shiftId)?.code ?? r.shiftId?.slice(0, 8) ?? '—')}</TableCell>
                  <TableCell>{r.isOff ? 'Yes' : '—'}</TableCell>
                  <TableCell>{r.reason ?? '—'}</TableCell>
                  <TableCell>{r.source}</TableCell>
                </TableRow>
              ))}
            </TableBody></Table>
          )}
      </SectionCard>
      {shiftDlg && (
        <Dialog open onClose={() => setShiftDlg(null)}>
          <DialogTitle>Assign shift</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: 2, minWidth: 380 }}>
            {picker(shiftDlg.employeeId, v => setShiftDlg(d => d && { ...d, employeeId: v }))}
            <TF select label="Shift" value={shiftDlg.shiftId} onChange={e => setShiftDlg(d => d && { ...d, shiftId: e.target.value })}>
              {shifts.data?.map(s => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}
            </TF>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
              <TF label="From" type="date" value={shiftDlg.effectiveFrom} onChange={e => setShiftDlg(d => d && { ...d, effectiveFrom: e.target.value })} />
              <TF label="To (blank = open)" type="date" value={shiftDlg.effectiveTo ?? ''} onChange={e => setShiftDlg(d => d && { ...d, effectiveTo: e.target.value || null })} />
            </Box>
            <TF label="Reason" value={shiftDlg.reason ?? ''} onChange={e => setShiftDlg(d => d && { ...d, reason: e.target.value })} />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setShiftDlg(null)}>Cancel</Button>
            <Button variant="contained" onClick={() => saveShift.mutate()} disabled={saveShift.isPending}>Assign</Button>
          </DialogActions>
        </Dialog>
      )}
      {policyDlg && (
        <Dialog open onClose={() => setPolicyDlg(null)}>
          <DialogTitle>Assign policy</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: 2, minWidth: 380 }}>
            {picker(policyDlg.employeeId, v => setPolicyDlg(d => d && { ...d, employeeId: v }))}
            <TF select label="Policy" value={policyDlg.policyId} onChange={e => setPolicyDlg(d => d && { ...d, policyId: e.target.value })}>
              {policies.data?.map(p => <MenuItem key={p.id} value={p.id}>{p.code} — {p.name}</MenuItem>)}
            </TF>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
              <TF label="From" type="date" value={policyDlg.effectiveFrom} onChange={e => setPolicyDlg(d => d && { ...d, effectiveFrom: e.target.value })} />
              <TF label="To (blank = open)" type="date" value={policyDlg.effectiveTo ?? ''} onChange={e => setPolicyDlg(d => d && { ...d, effectiveTo: e.target.value || null })} />
            </Box>
            <TF label="Reason" value={policyDlg.reason ?? ''} onChange={e => setPolicyDlg(d => d && { ...d, reason: e.target.value })} />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setPolicyDlg(null)}>Cancel</Button>
            <Button variant="contained" onClick={() => savePolicy.mutate()} disabled={savePolicy.isPending}>Assign</Button>
          </DialogActions>
        </Dialog>
      )}
    </>
  )
}

// ---------- Config resolver ----------
function ResolverTab() {
  const emps = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const [empId, setEmpId] = useState('')
  const [from, setFrom] = useState(today())
  const [to, setTo] = useState(today())
  const q = useQuery({
    queryKey: ['resolve', empId, from, to],
    queryFn: () => tenantApi.resolveConfigRange(empId, from, to),
    enabled: !!empId,
  })
  return (
    <SectionCard title="Effective configuration resolver"
      actions={<Box sx={{ display: 'flex', gap: 2 }}>
        <Box sx={{ minWidth: 240 }}>
          <TF select label="Employee" value={empId} onChange={e => setEmpId(e.target.value)}>
            {(emps.data ?? []).map(e => <MenuItem key={e.id} value={e.id}>{e.employeeCode} — {e.displayName}</MenuItem>)}
          </TF>
        </Box>
        <TF label="From" type="date" value={from} onChange={e => setFrom(e.target.value)} sx={{ width: 160 }} />
        <TF label="To" type="date" value={to} onChange={e => setTo(e.target.value)} sx={{ width: 160 }} />
      </Box>}>
      {!empId ? <EmptyState title="Pick an employee" hint="Shows which shift, policy, calendar and day-type apply per date — as the engine will see them." />
        : q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} />
        : (
          <Table size="small"><TableHead><TableRow>
            <TableCell>Date</TableCell><TableCell>Day type</TableCell><TableCell>Shift (source)</TableCell>
            <TableCell>Policy (source)</TableCell><TableCell>Calendar (source)</TableCell>
            <TableCell>Planned</TableCell><TableCell>Grace in/out</TableCell>
          </TableRow></TableHead><TableBody>
            {(q.data ?? []).map(r => (
              <TableRow key={r.date} hover>
                <TableCell>{r.date}</TableCell>
                <TableCell><StatusChip value={r.dayType} /></TableCell>
                <TableCell>{r.shiftCode ? `${r.shiftCode} v${r.shiftVersion} (${r.shiftSource})` : '—'}</TableCell>
                <TableCell>{r.policyCode ? `${r.policyCode} v${r.policyVersion} (${r.policySource})` : '—'}</TableCell>
                <TableCell>{r.calendarCode ? `${r.calendarCode} (${r.calendarSource})` : '—'}</TableCell>
                <TableCell>{r.plannedStart ? `${r.plannedStart.slice(11, 16)} → ${r.plannedEnd?.slice(11, 16)} · ${r.plannedMinutes}m` : '—'}</TableCell>
                <TableCell>{r.graceInMinutes}/{r.graceOutMinutes}m</TableCell>
              </TableRow>
            ))}
          </TableBody></Table>
        )}
      {empId && q.data?.length ? (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
          Timezone: {q.data[0].timeZone} · Org path: {q.data[0].orgSource}
        </Typography>
      ) : null}
    </SectionCard>
  )
}

export function AttendanceAdminPage() {
  const [tab, setTab] = useState(0)
  return (
    <Box>
      <PageHeader title="Attendance setup" subtitle="M3.1 masters — versioned, effective-dated configuration" />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
        <Tab label="Shifts" /><Tab label="Calendars" /><Tab label="Policies" />
        <Tab label="Assignments & roster" /><Tab label="Config resolver" />
      </Tabs>
      {tab === 0 && <ShiftsTab />}
      {tab === 1 && <CalendarsTab />}
      {tab === 2 && <PoliciesTab />}
      {tab === 3 && <AssignmentsTab />}
      {tab === 4 && <ResolverTab />}
    </Box>
  )
}
