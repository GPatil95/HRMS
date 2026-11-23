import { useState } from 'react'
import {
  Alert, Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, IconButton, MenuItem, Table, TableBody, TableCell, TableHead,
  TableRow, TextField, Tooltip, Typography,
} from '@mui/material'
import { Add, Autorenew, Delete, Download, FileUpload, PlayArrow } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  tenantApi, type AttendanceDevice, type AttendancePunch, type DeviceInput,
  type DeviceUserMap, type IngestResult,
} from '../../api/tenant'
import { itemsOf } from '../../api/client'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'

const TF = (p: React.ComponentProps<typeof TextField>) => (
  <TextField size="small" fullWidth slotProps={{ inputLabel: { shrink: true } }} {...p} />
)
const onErr = (toast: (m: string, s?: 'success' | 'error') => void) => ({
  onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
})
const today = () => new Date().toISOString().slice(0, 10)
const fmtTs = (iso?: string | null) => (iso ? new Date(iso).toLocaleString() : '—')

// ---------- Devices ----------

const emptyDevice = (): DeviceInput => ({
  code: '', name: '', vendor: null, branchId: null,
  protocol: 'push_api', timezoneMode: 'utc', deviceTimezone: null, isActive: true,
})

function DeviceDialog({ device, branches, onClose }: {
  device: AttendanceDevice | null; branches: { id: string; name: string }[]; onClose: () => void
}) {
  const toast = useToast(); const qc = useQueryClient()
  const [form, setForm] = useState<DeviceInput>(device ? {
    code: device.code, name: device.name, vendor: device.vendor ?? null, branchId: device.branchId ?? null,
    protocol: device.protocol, timezoneMode: device.timezoneMode, deviceTimezone: device.deviceTimezone ?? null,
    isActive: device.isActive,
  } : emptyDevice())
  const [newToken, setNewToken] = useState<string | null>(null)
  const f = (k: keyof DeviceInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm(s => ({ ...s, [k]: e.target.type === 'checkbox' ? e.target.checked : (e.target.value || null) }))
  const save = useMutation<import('../../api/tenant').DeviceCreatedDto | AttendanceDevice>({
    mutationFn: () => device ? tenantApi.updateAttendanceDevice(device.id, form) : tenantApi.createAttendanceDevice(form),
    onSuccess: (r) => {
      void qc.invalidateQueries({ queryKey: ['attendance-devices'] })
      if (!device && 'token' in r) { setNewToken(r.token); toast('Device created — copy the token now') }
      else { toast('Device saved'); onClose() }
    },
    ...onErr(toast),
  })
  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{device ? `Edit ${device.code}` : 'Register device'}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: 2 }}>
        {newToken ? (
          <Alert severity="warning">
            Token (shown once): <code>{newToken}</code>
            <Button size="small" onClick={() => { void navigator.clipboard.writeText(newToken); toast('Copied') }}>Copy</Button>
          </Alert>
        ) : (
          <>
            <TF label="Code" value={form.code ?? ''} onChange={f('code')} required disabled={!!device} />
            <TF label="Name" value={form.name ?? ''} onChange={f('name')} required />
            <TF label="Vendor" value={form.vendor ?? ''} onChange={f('vendor')} />
            <TF select label="Branch" value={form.branchId ?? ''} onChange={f('branchId')}>
              <MenuItem value="">— none —</MenuItem>
              {branches.map(b => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
            </TF>
            <TF select label="Protocol" value={form.protocol ?? 'push_api'} onChange={f('protocol')}>
              {['push_api', 'pull_poll', 'file_import'].map(p => <MenuItem key={p} value={p}>{p}</MenuItem>)}
            </TF>
            <TF select label="Timezone mode" value={form.timezoneMode ?? 'utc'} onChange={f('timezoneMode')}>
              <MenuItem value="utc">utc — device sends UTC/offset timestamps</MenuItem>
              <MenuItem value="device_local">device_local — naive local time, convert with device timezone</MenuItem>
            </TF>
            {form.timezoneMode === 'device_local' && (
              <TF label="Device timezone (IANA)" value={form.deviceTimezone ?? ''} onChange={f('deviceTimezone')}
                placeholder="Asia/Kolkata" required />
            )}
            <FormControlLabel control={<Checkbox checked={form.isActive ?? true} onChange={f('isActive')} />} label="Active" />
          </>
        )}
      </DialogContent>
      <DialogActions>
        {newToken
          ? <Button onClick={onClose}>Done</Button>
          : <><Button onClick={onClose}>Cancel</Button>
             <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button></>}
      </DialogActions>
    </Dialog>
  )
}

function MapDialog({ device, employees, onClose }: {
  device: AttendanceDevice; employees: { id: string; employeeCode: string; displayName: string }[]; onClose: () => void
}) {
  const toast = useToast(); const qc = useQueryClient()
  const [form, setForm] = useState({ deviceUserCode: '', employeeId: '', effectiveFrom: today(), effectiveTo: '' })
  const save = useMutation({
    mutationFn: () => tenantApi.upsertDeviceUserMap(device.id, {
      deviceUserCode: form.deviceUserCode, employeeId: form.employeeId,
      effectiveFrom: form.effectiveFrom, effectiveTo: form.effectiveTo || null,
    }),
    onSuccess: () => { toast('Mapping saved'); void qc.invalidateQueries({ queryKey: ['device-maps', device.id] }); onClose() },
    ...onErr(toast),
  })
  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Map device user → employee ({device.code})</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: 2 }}>
        <TF label="Device user code" value={form.deviceUserCode} onChange={e => setForm(s => ({ ...s, deviceUserCode: e.target.value }))} required />
        <TF select label="Employee" value={form.employeeId} onChange={e => setForm(s => ({ ...s, employeeId: e.target.value }))} required>
          {employees.map(e => <MenuItem key={e.id} value={e.id}>{e.employeeCode} — {e.displayName}</MenuItem>)}
        </TF>
        <TF type="date" label="Effective from" value={form.effectiveFrom} onChange={e => setForm(s => ({ ...s, effectiveFrom: e.target.value }))} required />
        <TF type="date" label="Effective to (optional)" value={form.effectiveTo} onChange={e => setForm(s => ({ ...s, effectiveTo: e.target.value }))} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>
      </DialogActions>
    </Dialog>
  )
}

function DeviceMaps({ device, employees }: { device: AttendanceDevice; employees: { id: string; employeeCode: string; displayName: string }[] }) {
  const toast = useToast(); const qc = useQueryClient()
  const [mapping, setMapping] = useState(false)
  const maps = useQuery({ queryKey: ['device-maps', device.id], queryFn: () => tenantApi.deviceUserMaps(device.id) })
  const del = useMutation({
    mutationFn: (mapId: string) => tenantApi.deleteDeviceUserMap(device.id, mapId),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['device-maps', device.id] }),
    ...onErr(toast),
  })
  return (
    <SectionCard title={`Mappings — ${device.code}`}
      actions={<Button size="small" startIcon={<Add />} onClick={() => setMapping(true)}>Add mapping</Button>}>
      {maps.isLoading ? <TableSkeleton columns={4} rows={3} /> : maps.error ? <ErrorAlert error={maps.error} /> : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Device user</TableCell><TableCell>Employee</TableCell><TableCell>Effective</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {(maps.data ?? []).map((m: DeviceUserMap) => (
              <TableRow key={m.id}>
                <TableCell>{m.deviceUserCode}</TableCell>
                <TableCell>{m.employeeCode} — {m.employeeName}</TableCell>
                <TableCell>{m.effectiveFrom} → {m.effectiveTo ?? 'open'}</TableCell>
                <TableCell align="right">
                  <IconButton size="small" onClick={() => del.mutate(m.id)}><Delete fontSize="small" /></IconButton>
                </TableCell>
              </TableRow>
            ))}
            {(maps.data ?? []).length === 0 && <TableRow><TableCell colSpan={4}>No mappings — punches from this device stay unmapped until you add one.</TableCell></TableRow>}
          </TableBody>
        </Table>
      )}
      {mapping && <MapDialog device={device} employees={employees} onClose={() => setMapping(false)} />}
    </SectionCard>
  )
}

export function DevicesPage() {
  const toast = useToast()
  const [editing, setEditing] = useState<AttendanceDevice | null | 'new'>(null)
  const [mappingFor, setMappingFor] = useState<AttendanceDevice | null>(null)
  const devices = useQuery({ queryKey: ['attendance-devices'], queryFn: () => tenantApi.attendanceDevices() })
  const branches = useQuery({ queryKey: ['branches'], queryFn: () => tenantApi.branches() })
  const employees = useQuery({ queryKey: ['employees'], queryFn: () => tenantApi.employees() })
  const rotate = useMutation({
    mutationFn: (id: string) => tenantApi.rotateDeviceToken(id),
    onSuccess: (r) => toast(`New token for ${r.device.code}: ${r.token} (shown once)`),
    ...onErr(toast),
  })
  return (
    <Box>
      <PageHeader title="Attendance devices" subtitle="Register biometric devices, manage tokens and map device user codes to employees"
        actions={<Button variant="contained" startIcon={<Add />} onClick={() => setEditing('new')}>Register device</Button>} />
      {devices.isLoading ? <TableSkeleton columns={7} /> : devices.error ? <ErrorAlert error={devices.error} /> : (
        <SectionCard title="Devices">
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Branch</TableCell>
              <TableCell>Protocol</TableCell><TableCell>Timezone</TableCell><TableCell>Last seen</TableCell>
              <TableCell>Status</TableCell><TableCell />
            </TableRow></TableHead>
            <TableBody>
              {(devices.data ?? []).map(d => (
                <TableRow key={d.id}>
                  <TableCell><Button size="small" onClick={() => setMappingFor(m => m?.id === d.id ? null : d)}>{d.code}</Button></TableCell>
                  <TableCell>{d.name}</TableCell>
                  <TableCell>{d.branchName ?? '—'}</TableCell>
                  <TableCell>{d.protocol}</TableCell>
                  <TableCell>{d.timezoneMode}{d.deviceTimezone ? ` (${d.deviceTimezone})` : ''}</TableCell>
                  <TableCell>{fmtTs(d.lastSeenAt)}</TableCell>
                  <TableCell><StatusChip value={d.isActive ? 'active' : 'inactive'} /></TableCell>
                  <TableCell align="right">
                    <Tooltip title="Edit"><IconButton size="small" onClick={() => setEditing(d)}><Add fontSize="small" /></IconButton></Tooltip>
                    <Tooltip title="Rotate token"><IconButton size="small" onClick={() => rotate.mutate(d.id)}><Autorenew fontSize="small" /></IconButton></Tooltip>
                  </TableCell>
                </TableRow>
              ))}
              {(devices.data ?? []).length === 0 && <TableRow><TableCell colSpan={8}>No devices registered.</TableCell></TableRow>}
            </TableBody>
          </Table>
        </SectionCard>
      )}
      {mappingFor && <DeviceMaps device={mappingFor} employees={employees.data ?? []} />}
      {editing && <DeviceDialog device={editing === 'new' ? null : editing} branches={branches.data ?? []} onClose={() => setEditing(null)} />}
    </Box>
  )
}

// ---------- Raw punches viewer ----------

export function PunchesPage() {
  const toast = useToast(); const qc = useQueryClient()
  const [filters, setFilters] = useState({ employeeId: '', source: '', direction: '', from: '', to: '', unmappedOnly: false, includeVoided: false })
  const [voidFor, setVoidFor] = useState<AttendancePunch | null>(null)
  const q = `?${new URLSearchParams({
    ...(filters.employeeId ? { employeeId: filters.employeeId } : {}),
    ...(filters.source ? { source: filters.source } : {}),
    ...(filters.direction ? { direction: filters.direction } : {}),
    ...(filters.from ? { from: filters.from } : {}),
    ...(filters.to ? { to: filters.to } : {}),
    ...(filters.unmappedOnly ? { unmappedOnly: 'true' } : {}),
    ...(filters.includeVoided ? { includeVoided: 'true' } : {}),
    pageSize: '200',
  })}`
  const punches = useQuery({ queryKey: ['attendance-punches', q], queryFn: () => tenantApi.punches(q) })
  const employees = useQuery({ queryKey: ['employees'], queryFn: () => tenantApi.employees() })
  const items = itemsOf(punches.data)
  const f = (k: keyof typeof filters) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setFilters(s => ({ ...s, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }))
  return (
    <Box>
      <PageHeader title="Raw punches" subtitle="Immutable punch records across all sources — device, self, manual, import" />
      <SectionCard title="Filters" sx={{ mb: 2 }}>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(6, 1fr)', gap: 2 }}>
          <TF select label="Employee" value={filters.employeeId} onChange={f('employeeId')}>
            <MenuItem value="">All</MenuItem>
            {(employees.data ?? []).map(e => <MenuItem key={e.id} value={e.id}>{e.employeeCode} — {e.displayName}</MenuItem>)}
          </TF>
          <TF select label="Source" value={filters.source} onChange={f('source')}>
            <MenuItem value="">All</MenuItem>
            {['device', 'self', 'manual', 'import'].map(s => <MenuItem key={s} value={s}>{s}</MenuItem>)}
          </TF>
          <TF select label="Direction" value={filters.direction} onChange={f('direction')}>
            <MenuItem value="">All</MenuItem>
            {['in', 'out', 'unknown'].map(d => <MenuItem key={d} value={d}>{d}</MenuItem>)}
          </TF>
          <TF type="date" label="From" value={filters.from} onChange={f('from')} />
          <TF type="date" label="To" value={filters.to} onChange={f('to')} />
          <Box sx={{ display: 'flex', gap: 1 }}>
            <FormControlLabel control={<Checkbox checked={filters.unmappedOnly} onChange={f('unmappedOnly')} />} label="Unmapped" />
            <FormControlLabel control={<Checkbox checked={filters.includeVoided} onChange={f('includeVoided')} />} label="Voided" />
          </Box>
        </Box>
      </SectionCard>
      {punches.isLoading ? <TableSkeleton columns={9} /> : punches.error ? <ErrorAlert error={punches.error} /> : items.length === 0 ? (
        <EmptyState title="No punches" hint="Adjust the filters or ingest punches first." />
      ) : (
        <SectionCard title={`Punches (${items.length})`}>
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>Employee / device user</TableCell><TableCell>Punched at (UTC)</TableCell>
              <TableCell>Wall time</TableCell><TableCell>Source</TableCell><TableCell>Direction</TableCell>
              <TableCell>Device</TableCell><TableCell>Note</TableCell><TableCell>Status</TableCell><TableCell />
            </TableRow></TableHead>
            <TableBody>
              {items.map(p => (
                <TableRow key={p.id}>
                  <TableCell>{p.employeeId ? `${p.employeeCode} — ${p.employeeName}` : `unmapped: ${p.deviceUserCode ?? '—'}`}</TableCell>
                  <TableCell>{fmtTs(p.punchedAt)}</TableCell>
                  <TableCell>{p.localWallTime}</TableCell>
                  <TableCell>{p.source}</TableCell>
                  <TableCell>{p.direction}</TableCell>
                  <TableCell>{p.deviceCode ?? '—'}</TableCell>
                  <TableCell>{p.note ?? ''}</TableCell>
                  <TableCell>{p.isVoided ? <StatusChip value="voided" /> : 'ok'}</TableCell>
                  <TableCell align="right">
                    {!p.isVoided && <Button size="small" color="warning" onClick={() => setVoidFor(p)}>Void</Button>}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </SectionCard>
      )}
      {voidFor && <VoidDialog punch={voidFor} onClose={() => setVoidFor(null)}
        onDone={() => { void qc.invalidateQueries({ queryKey: ['attendance-punches'] }); setVoidFor(null); toast('Punch voided') }} />}
    </Box>
  )
}

function VoidDialog({ punch, onClose, onDone }: { punch: AttendancePunch; onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [reason, setReason] = useState('')
  const save = useMutation({
    mutationFn: () => tenantApi.voidPunch(punch.id, reason),
    onSuccess: onDone,
    ...onErr(toast),
  })
  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Void punch</DialogTitle>
      <DialogContent>
        <Typography variant="body2" sx={{ mb: 2 }}>
          The punch row stays in the ledger — computation ignores voided punches. This cannot be undone.
        </Typography>
        <TF label="Reason" value={reason} onChange={e => setReason(e.target.value)} required />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" color="warning" disabled={!reason.trim() || save.isPending} onClick={() => save.mutate()}>Void</Button>
      </DialogActions>
    </Dialog>
  )
}

// ---------- Import ----------

export function ImportPunchesPage() {
  const toast = useToast()
  const [file, setFile] = useState<File | null>(null)
  const [deviceId, setDeviceId] = useState('')
  const [map, setMap] = useState({ deviceUserCodeCol: '0', timestampCol: '1', directionCol: '2', employeeCodeCol: '3' })
  const [skipHeader, setSkipHeader] = useState(true)
  const [idem, setIdem] = useState('')
  const [result, setResult] = useState<IngestResult | null>(null)
  const devices = useQuery({ queryKey: ['attendance-devices'], queryFn: () => tenantApi.attendanceDevices(true) })
  const run = useMutation({
    mutationFn: async () => {
      if (!file) throw new Error('Choose a CSV file')
      const fd = new FormData()
      fd.append('file', file); fd.append('purpose', 'attendance.punches.import-csv')
      const up = await tenantApi.uploadFile(fd)
      return tenantApi.importPunchCsv({
        fileId: up.id, deviceId: deviceId || null,
        mapping: {
          deviceUserCodeCol: Number(map.deviceUserCodeCol), timestampCol: Number(map.timestampCol),
          directionCol: Number(map.directionCol), employeeCodeCol: map.employeeCodeCol === '' ? -1 : Number(map.employeeCodeCol),
        },
        idempotencyKey: idem || null, skipHeader,
      })
    },
    onSuccess: (r) => { setResult(r); toast(`Import complete: ${r.accepted} accepted, ${r.rejected} rejected`) },
    ...onErr(toast),
  })
  return (
    <Box>
      <PageHeader title="Import punches" subtitle="CSV import for offline device sync — partial acceptance, rejects kept per row" />
      <SectionCard title="Import" sx={{ mb: 2 }}>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 2 }}>
          <Button component="label" variant="outlined" startIcon={<FileUpload />}>
            {file ? file.name : 'Choose CSV'}
            <input hidden type="file" accept=".csv" onChange={e => setFile(e.target.files?.[0] ?? null)} />
          </Button>
          <TF select label="Device (optional)" value={deviceId} onChange={e => setDeviceId(e.target.value)}>
            <MenuItem value="">— none —</MenuItem>
            {(devices.data ?? []).map(d => <MenuItem key={d.id} value={d.id}>{d.code}</MenuItem>)}
          </TF>
          <TF label="Idempotency key (optional)" value={idem} onChange={e => setIdem(e.target.value)} />
          <TF label="device_user_code column" type="number" value={map.deviceUserCodeCol} onChange={e => setMap(s => ({ ...s, deviceUserCodeCol: e.target.value }))} />
          <TF label="timestamp column" type="number" value={map.timestampCol} onChange={e => setMap(s => ({ ...s, timestampCol: e.target.value }))} />
          <TF label="direction column" type="number" value={map.directionCol} onChange={e => setMap(s => ({ ...s, directionCol: e.target.value }))} />
          <TF label="employee_code column (-1 = none)" type="number" value={map.employeeCodeCol} onChange={e => setMap(s => ({ ...s, employeeCodeCol: e.target.value }))} />
          <FormControlLabel control={<Checkbox checked={skipHeader} onChange={e => setSkipHeader(e.target.checked)} />} label="Skip header row" />
          <Button variant="contained" startIcon={<PlayArrow />} disabled={!file || run.isPending} onClick={() => run.mutate()}>
            {run.isPending ? 'Importing…' : 'Import'}
          </Button>
        </Box>
      </SectionCard>
      {result && (
        <SectionCard title="Result">
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 2 }}>
            <Typography>Received: {result.received}</Typography>
            <Typography>Accepted: {result.accepted}</Typography>
            <Typography>Duplicates: {result.duplicates}</Typography>
            <Typography>Rejected: {result.rejected}</Typography>
            <Typography>Status: <StatusChip value={result.status} /></Typography>
          </Box>
          {result.rejections.length > 0 && (
            <Table size="small" sx={{ mt: 2 }}>
              <TableHead><TableRow><TableCell>Row</TableCell><TableCell>Reason</TableCell></TableRow></TableHead>
              <TableBody>{result.rejections.map(r => <TableRow key={r.index}><TableCell>{r.index}</TableCell><TableCell>{r.reason}</TableCell></TableRow>)}</TableBody>
            </Table>
          )}
        </SectionCard>
      )}
    </Box>
  )
}

// ---------- Import history ----------

export function ImportsPage() {
  const batches = useQuery({ queryKey: ['ingest-batches'], queryFn: () => tenantApi.ingestBatches() })
  return (
    <Box>
      <PageHeader title="Import history" subtitle="Every ingestion batch — push, import, self and manual — with error detail" />
      {batches.isLoading ? <TableSkeleton columns={8} /> : batches.error ? <ErrorAlert error={batches.error} /> : (
        <SectionCard title={`Batches (${(batches.data ?? []).length})`}>
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>When</TableCell><TableCell>Source</TableCell><TableCell>Device</TableCell>
              <TableCell>Received</TableCell><TableCell>Accepted</TableCell><TableCell>Duplicates</TableCell>
              <TableCell>Rejected</TableCell><TableCell>Status</TableCell>
            </TableRow></TableHead>
            <TableBody>
              {(batches.data ?? []).map(b => (
                <TableRow key={b.id}>
                  <TableCell>{fmtTs(b.createdAt)}</TableCell>
                  <TableCell>{b.source}</TableCell>
                  <TableCell>{b.deviceCode ?? '—'}</TableCell>
                  <TableCell>{b.received}</TableCell>
                  <TableCell>{b.accepted}</TableCell>
                  <TableCell>{b.duplicates}</TableCell>
                  <TableCell>
                    {b.rejected}
                    {b.errorSummary && (
                      <Tooltip title={b.errorSummary}><IconButton size="small"><Download fontSize="small" /></IconButton></Tooltip>
                    )}
                  </TableCell>
                  <TableCell><StatusChip value={b.status} /></TableCell>
                </TableRow>
              ))}
              {(batches.data ?? []).length === 0 && <TableRow><TableCell colSpan={8}>No ingestion batches yet.</TableCell></TableRow>}
            </TableBody>
          </Table>
        </SectionCard>
      )}
    </Box>
  )
}

// ---------- My punches ----------

export function MyPunchesPage() {
  const toast = useToast(); const qc = useQueryClient()
  const punches = useQuery({ queryKey: ['my-punches'], queryFn: () => tenantApi.myPunches('?pageSize=200') })
  const punch = useMutation({
    mutationFn: (direction: 'in' | 'out') => tenantApi.selfPunch({ direction }),
    onSuccess: () => { toast('Punched'); void qc.invalidateQueries({ queryKey: ['my-punches'] }) },
    ...onErr(toast),
  })
  const items = itemsOf(punches.data)
  return (
    <Box>
      <PageHeader title="My punches" subtitle="Check in/out and view your own punch history"
        actions={<Box sx={{ display: 'flex', gap: 1 }}>
          <Button variant="contained" onClick={() => punch.mutate('in')} disabled={punch.isPending}>Punch in</Button>
          <Button variant="outlined" onClick={() => punch.mutate('out')} disabled={punch.isPending}>Punch out</Button>
        </Box>} />
      {punches.isLoading ? <TableSkeleton columns={5} /> : punches.error ? <ErrorAlert error={punches.error} /> : items.length === 0 ? (
        <EmptyState title="No punches yet" hint="Use the buttons above to record your first punch." />
      ) : (
        <SectionCard title={`Your punches (${items.length})`}>
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>Punched at (UTC)</TableCell><TableCell>Wall time</TableCell>
              <TableCell>Direction</TableCell><TableCell>Source</TableCell><TableCell>Status</TableCell>
            </TableRow></TableHead>
            <TableBody>
              {items.map(p => (
                <TableRow key={p.id}>
                  <TableCell>{fmtTs(p.punchedAt)}</TableCell>
                  <TableCell>{p.localWallTime}</TableCell>
                  <TableCell>{p.direction}</TableCell>
                  <TableCell>{p.source}</TableCell>
                  <TableCell>{p.isVoided ? <StatusChip value="voided" /> : 'ok'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </SectionCard>
      )}
    </Box>
  )
}
