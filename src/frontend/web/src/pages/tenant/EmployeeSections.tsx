import { useEffect, useRef, useState, type ReactNode } from 'react'
import {
  Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, IconButton, MenuItem, Stack, Table, TableBody, TableCell, TableHead, TableRow,
  TextField, Typography,
} from '@mui/material'
import { Add, Delete, Download, Edit, Upload } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  mastersApi, tenantApi, type BankInput, type EmpContactInput, type DocCategory, type Employee,
  type LifecycleEvent, type Personal, type Statutory,
} from '../../api/tenant'
import { EmptyState, ErrorAlert, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'

const dateStr = (v?: string | null) => v ? v.slice(0, 10) : ''

function useInv(empId: string, keys: string[]) {
  const qc = useQueryClient()
  return () => keys.forEach((k) => void qc.invalidateQueries({ queryKey: ['emp', empId, k] }))
}

function useErr(toast: (m: string, s?: 'error') => void) {
  return { onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error') }
}

function EditDialog({ open, title, onClose, onSave, saving, children }: {
  open: boolean; title: string; onClose: () => void; onSave: () => void; saving?: boolean; children: ReactNode
}) {
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>{children}</DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={onSave} disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
      </DialogActions>
    </Dialog>
  )
}

const TF = (p: React.ComponentProps<typeof TextField>) => (
  <TextField size="small" fullWidth slotProps={{ inputLabel: { shrink: true } }} {...p} />
)

// ===================== Personal =====================
function AddressFields({ value, onChange }: { value: Record<string, string>; onChange: (v: Record<string, string>) => void }) {
  const f = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) => onChange({ ...value, [k]: e.target.value })
  return (
    <>
      <TF label="Line 1" value={value.line1 ?? ''} onChange={f('line1')} />
      <TF label="City" value={value.city ?? ''} onChange={f('city')} />
      <TF label="State" value={value.state ?? ''} onChange={f('state')} />
      <TF label="PIN" value={value.pin ?? ''} onChange={f('pin')} />
    </>
  )
}

export function PersonalTab({ empId }: { empId: string }) {
  const toast = useToast()
  const q = useQuery({ queryKey: ['emp', empId, 'personal'], queryFn: () => tenantApi.employeePersonal(empId) })
  const [form, setForm] = useState<Personal | null>(null)
  useEffect(() => { if (q.data && !form) setForm(q.data) }, [q.data, form])
  const inv = useInv(empId, ['personal'])
  const save = useMutation({
    mutationFn: () => tenantApi.savePersonal(empId, form!),
    onSuccess: () => { toast('Personal details saved'); inv() },
    ...useErr(toast),
  })
  if (q.isPending || !form) return <TableSkeleton columns={2} />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => q.refetch()} />
  const f = (k: keyof Personal) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value })
  return (
    <SectionCard title="Personal details" actions={<Button variant="contained" size="small" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>}>
      <Box sx={{ p: 2.5, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>
        <TF type="date" label="Date of birth" value={dateStr(form.dateOfBirth)} onChange={f('dateOfBirth')} />
        <TF select label="Gender" value={form.gender ?? ''} onChange={f('gender')}>
          {['', 'male', 'female', 'other'].map((g) => <MenuItem key={g} value={g}>{g || '—'}</MenuItem>)}
        </TF>
        <TF select label="Marital status" value={form.maritalStatus ?? ''} onChange={f('maritalStatus')}>
          {['', 'single', 'married', 'divorced', 'widowed'].map((g) => <MenuItem key={g} value={g}>{g || '—'}</MenuItem>)}
        </TF>
        <TF label="Nationality" value={form.nationality ?? ''} onChange={f('nationality')} />
        <TF label="Blood group" value={form.bloodGroup ?? ''} onChange={f('bloodGroup')} />
        <TF label="Personal email" type="email" value={form.personalEmail ?? ''} onChange={f('personalEmail')} />
        <TF label="Personal phone" value={form.personalPhone ?? ''} onChange={f('personalPhone')} />
      </Box>
      <Box sx={{ px: 2.5, pb: 2.5, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>
        <Box>
          <Typography variant="subtitle2" sx={{ mb: 1 }}>Current address</Typography>
          <Stack spacing={1.5}>
            <AddressFields value={(form.currentAddress ?? {}) as Record<string, string>}
              onChange={(v) => setForm({ ...form, currentAddress: v })} />
          </Stack>
        </Box>
        <Box>
          <Stack direction="row" sx={{ mb: 1, alignItems: 'center' }}>
            <Typography variant="subtitle2" sx={{ flexGrow: 1 }}>Permanent address</Typography>
            <Button size="small" onClick={() => setForm({ ...form, permanentAddress: form.currentAddress })}>Same as current</Button>
          </Stack>
          <Stack spacing={1.5}>
            <AddressFields value={(form.permanentAddress ?? {}) as Record<string, string>}
              onChange={(v) => setForm({ ...form, permanentAddress: v })} />
          </Stack>
        </Box>
      </Box>
    </SectionCard>
  )
}

// ===================== Contacts =====================
const CONTACT_KINDS = ['emergency', 'family', 'nominee']
const emptyContact = (): EmpContactInput => ({ kind: 'emergency', name: '', relation: '', isPrimary: false })

export function ContactsTab({ empId }: { empId: string }) {
  const toast = useToast()
  const inv = useInv(empId, ['contacts'])
  const q = useQuery({ queryKey: ['emp', empId, 'contacts'], queryFn: () => tenantApi.employeeContacts(empId) })
  const [dlg, setDlg] = useState<{ id?: string; form: EmpContactInput } | null>(null)
  const f = (k: keyof EmpContactInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg((d) => d && { ...d, form: { ...d.form, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value } })
  const save = useMutation({
    mutationFn: () => dlg!.id
      ? tenantApi.updateEmpContact(empId, dlg!.id!, dlg!.form)
      : tenantApi.addEmpContact(empId, dlg!.form),
    onSuccess: () => { toast('Contact saved'); inv(); setDlg(null) }, ...useErr(toast),
  })
  const del = useMutation({
    mutationFn: (cid: string) => tenantApi.deleteEmpContact(empId, cid),
    onSuccess: () => { toast('Contact removed'); inv() }, ...useErr(toast),
  })
  if (q.isPending) return <TableSkeleton columns={4} />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => q.refetch()} />
  const rows = q.data ?? []
  return (
    <SectionCard title="Contacts & nominees" actions={<Button size="small" startIcon={<Add />} onClick={() => setDlg({ form: emptyContact() })}>Add</Button>}>
      {rows.length === 0 ? <EmptyState title="No contacts" hint="Add an emergency contact, family member or nominee." /> : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Name</TableCell><TableCell>Kind</TableCell><TableCell>Relation</TableCell>
            <TableCell>Phone</TableCell><TableCell>Primary</TableCell><TableCell>Nominee %</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {rows.map((c) => (
              <TableRow key={c.id}>
                <TableCell>{c.name}</TableCell>
                <TableCell><StatusChip value={c.kind} /></TableCell>
                <TableCell>{c.relation ?? '—'}</TableCell>
                <TableCell>{c.phone ?? '—'}</TableCell>
                <TableCell>{c.isPrimary ? <Chip label="Primary" size="small" color="primary" /> : '—'}</TableCell>
                <TableCell>{c.nomineeSharePct != null ? `${c.nomineeSharePct}%` : '—'}</TableCell>
                <TableCell align="right">
                  <IconButton size="small" onClick={() => setDlg({ id: c.id, form: { ...c } })}><Edit fontSize="small" /></IconButton>
                  <IconButton size="small" onClick={() => del.mutate(c.id)}><Delete fontSize="small" /></IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {dlg && (
        <EditDialog open title={dlg.id ? 'Edit contact' : 'Add contact'} onClose={() => setDlg(null)} onSave={() => save.mutate()} saving={save.isPending}>
          <TF select label="Kind" value={dlg.form.kind} onChange={f('kind')}>
            {CONTACT_KINDS.map((k) => <MenuItem key={k} value={k}>{k}</MenuItem>)}
          </TF>
          <TF label="Name" value={dlg.form.name} onChange={f('name')} required />
          <TF label="Relation" value={dlg.form.relation ?? ''} onChange={f('relation')} />
          <TF label="Phone" value={dlg.form.phone ?? ''} onChange={f('phone')} />
          <TF label="Email" value={dlg.form.email ?? ''} onChange={f('email')} />
          <TF type="date" label="Date of birth" value={dateStr(dlg.form.dateOfBirth)} onChange={f('dateOfBirth')} />
          <FormControlLabel control={<Checkbox checked={dlg.form.isPrimary} onChange={f('isPrimary')} />} label="Primary contact" />
          <TF type="number" label="Nominee share %" value={dlg.form.nomineeSharePct ?? ''} onChange={f('nomineeSharePct')} />
        </EditDialog>
      )}
    </SectionCard>
  )
}

// ===================== Bank =====================
const emptyBank = (): BankInput => ({ bankName: '', accountNumber: '', ifsc: '', isPrimary: false, effectiveFrom: new Date().toISOString().slice(0, 10) })

export function BankTab({ empId }: { empId: string }) {
  const toast = useToast()
  const inv = useInv(empId, ['bank'])
  const q = useQuery({ queryKey: ['emp', empId, 'bank'], queryFn: () => tenantApi.employeeBank(empId) })
  const [dlg, setDlg] = useState<{ replaceId?: string; form: BankInput } | null>(null)
  const f = (k: keyof BankInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg((d) => d && { ...d, form: { ...d.form, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value } })
  const save = useMutation({
    mutationFn: () => dlg!.replaceId ? tenantApi.replaceBank(empId, dlg!.replaceId, dlg!.form) : tenantApi.addBank(empId, dlg!.form),
    onSuccess: () => { toast('Bank account saved'); inv(); setDlg(null) }, ...useErr(toast),
  })
  const verify = useMutation({
    mutationFn: ({ aid, status }: { aid: string; status: string }) => tenantApi.verifyBank(empId, aid, status),
    onSuccess: () => { toast('Verification updated'); inv() }, ...useErr(toast),
  })
  if (q.isPending) return <TableSkeleton columns={4} />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => q.refetch()} />
  const rows = q.data ?? []
  return (
    <SectionCard title="Bank accounts" actions={<Button size="small" startIcon={<Add />} onClick={() => setDlg({ form: emptyBank() })}>Add account</Button>}>
      {rows.length === 0 ? <EmptyState title="No bank accounts" hint="Add a salary account for payroll." /> : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Bank</TableCell><TableCell>Account</TableCell><TableCell>IFSC</TableCell>
            <TableCell>Primary</TableCell><TableCell>Effective</TableCell><TableCell>Verification</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {rows.map((b) => (
              <TableRow key={b.id}>
                <TableCell>{b.bankName}{b.branchName ? ` · ${b.branchName}` : ''}</TableCell>
                <TableCell sx={{ fontFamily: 'monospace' }}>{b.accountMasked}</TableCell>
                <TableCell sx={{ fontFamily: 'monospace' }}>{b.ifsc}</TableCell>
                <TableCell>{b.isPrimary ? <Chip label="Primary" size="small" color="primary" /> : '—'}</TableCell>
                <TableCell>{b.effectiveFrom}{b.effectiveTo ? ` → ${b.effectiveTo}` : ''}</TableCell>
                <TableCell><StatusChip value={b.verificationStatus} /></TableCell>
                <TableCell align="right">
                  {b.verificationStatus !== 'verified' && (
                    <Button size="small" onClick={() => verify.mutate({ aid: b.id, status: 'verified' })}>Verify</Button>
                  )}
                  <Button size="small" onClick={() => setDlg({ replaceId: b.id, form: { ...emptyBank(), isPrimary: b.isPrimary } })}>Replace</Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {dlg && (
        <EditDialog open title={dlg.replaceId ? 'Replace account' : 'Add bank account'} onClose={() => setDlg(null)} onSave={() => save.mutate()} saving={save.isPending}>
          <TF label="Bank name" value={dlg.form.bankName} onChange={f('bankName')} required />
          <TF label="Branch" value={dlg.form.branchName ?? ''} onChange={f('branchName')} />
          <TF label="Account number" value={dlg.form.accountNumber} onChange={f('accountNumber')} required />
          <TF label="IFSC" value={dlg.form.ifsc} onChange={f('ifsc')} required />
          <TF label="Account holder" value={dlg.form.holderName ?? ''} onChange={f('holderName')} />
          <TF type="date" label="Effective from" value={dlg.form.effectiveFrom} onChange={f('effectiveFrom')} />
          <FormControlLabel control={<Checkbox checked={dlg.form.isPrimary} onChange={f('isPrimary')} />} label="Primary (salary) account" />
        </EditDialog>
      )}
    </SectionCard>
  )
}

// ===================== Statutory =====================
export function StatutoryTab({ empId }: { empId: string }) {
  const toast = useToast()
  const inv = useInv(empId, ['statutory'])
  const q = useQuery({ queryKey: ['emp', empId, 'statutory'], queryFn: () => tenantApi.employeeStatutory(empId) })
  const [form, setForm] = useState<(Statutory & { pan?: string; passport?: string }) | null>(null)
  useEffect(() => { if (q.data && !form) setForm(q.data) }, [q.data, form])
  const save = useMutation({
    mutationFn: () => tenantApi.saveStatutory(empId, {
      pfApplicable: form!.pfApplicable, esiApplicable: form!.esiApplicable, lwfApplicable: form!.lwfApplicable,
      uan: form!.uan, pfNumber: form!.pfNumber, esiNumber: form!.esiNumber, ptState: form!.ptState,
      aadhaarLast4: form!.aadhaarLast4, taxRegime: form!.taxRegime,
      pan: form!.pan || undefined, passport: form!.passport || undefined, rowVersion: form!.rowVersion,
    }),
    onSuccess: () => { toast('Statutory details saved'); inv() }, ...useErr(toast),
  })
  if (q.isPending || !form) return <TableSkeleton columns={2} />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => q.refetch()} />
  const f = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm({ ...form, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  return (
    <SectionCard title="Statutory & tax" actions={<Button variant="contained" size="small" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>}>
      <Box sx={{ p: 2.5, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>
        <TF label={`PAN${form.panMasked ? ` (current: ${form.panMasked})` : ''}`} value={form.pan ?? ''} onChange={f('pan')}
          placeholder={form.panMasked ?? 'ABCDE1234F'} helperText="Leave blank to keep the stored value" />
        <TF label="UAN" value={form.uan ?? ''} onChange={f('uan')} />
        <TF label="PF number" value={form.pfNumber ?? ''} onChange={f('pfNumber')} />
        <TF label="ESI number" value={form.esiNumber ?? ''} onChange={f('esiNumber')} />
        <TF label="PT state" value={form.ptState ?? ''} onChange={f('ptState')} />
        <TF label="Aadhaar (last 4)" value={form.aadhaarLast4 ?? ''} onChange={f('aadhaarLast4')} slotProps={{ htmlInput: { maxLength: 4 }, inputLabel: { shrink: true } }} />
        <TF label={`Passport${form.passportMasked ? ` (current: ${form.passportMasked})` : ''}`} value={form.passport ?? ''} onChange={f('passport')} />
        <TF select label="Tax regime" value={form.taxRegime ?? ''} onChange={f('taxRegime')}>
          {['', 'old', 'new'].map((r) => <MenuItem key={r} value={r}>{r || '—'}</MenuItem>)}
        </TF>
      </Box>
      <Box sx={{ px: 2.5, pb: 2, display: 'flex', gap: 3, flexWrap: 'wrap' }}>
        <FormControlLabel control={<Checkbox checked={form.pfApplicable} onChange={f('pfApplicable')} />} label="PF applicable" />
        <FormControlLabel control={<Checkbox checked={form.esiApplicable} onChange={f('esiApplicable')} />} label="ESI applicable" />
        <FormControlLabel control={<Checkbox checked={form.lwfApplicable} onChange={f('lwfApplicable')} />} label="LWF applicable" />
      </Box>
    </SectionCard>
  )
}

// ===================== Documents =====================
export function DocumentsTab({ empId }: { empId: string }) {
  const toast = useToast()
  const inv = useInv(empId, ['documents', 'missing'])
  const q = useQuery({ queryKey: ['emp', empId, 'documents'], queryFn: () => tenantApi.employeeDocs(empId) })
  const missing = useQuery({ queryKey: ['emp', empId, 'missing'], queryFn: () => tenantApi.missingDocs(empId) })
  const cats = useQuery({ queryKey: ['doc-categories'], queryFn: tenantApi.docCategories })
  const [dlg, setDlg] = useState(false)
  const [form, setForm] = useState({ categoryId: '', label: '', documentNumber: '', issuedOn: '', expiresOn: '' })
  const fileRef = useRef<HTMLInputElement>(null)
  const upload = useMutation({
    mutationFn: () => {
      const fd = new FormData()
      const file = fileRef.current?.files?.[0]
      if (!file) throw new Error('Choose a file')
      fd.append('file', file)
      fd.append('categoryId', form.categoryId)
      if (form.label) fd.append('label', form.label)
      if (form.documentNumber) fd.append('documentNumber', form.documentNumber)
      if (form.issuedOn) fd.append('issuedOn', form.issuedOn)
      if (form.expiresOn) fd.append('expiresOn', form.expiresOn)
      return tenantApi.uploadDoc(empId, fd)
    },
    onSuccess: () => { toast('Document uploaded'); inv(); setDlg(false) }, ...useErr(toast),
  })
  const verify = useMutation({
    mutationFn: ({ docId, status }: { docId: string; status: string }) => tenantApi.verifyDoc(empId, docId, status),
    onSuccess: () => { toast('Verification updated'); inv() }, ...useErr(toast),
  })
  if (q.isPending) return <TableSkeleton columns={5} />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => q.refetch()} />
  const rows = q.data ?? []
  return (
    <>
      {(missing.data?.length ?? 0) > 0 && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          Missing onboarding documents: {(missing.data ?? []).map((c: DocCategory) => c.name).join(', ')}
        </Alert>
      )}
      <SectionCard title="Documents" actions={<Button size="small" startIcon={<Upload />} onClick={() => setDlg(true)}>Upload</Button>}>
        {rows.length === 0 ? <EmptyState title="No documents" hint="Upload employee documents — ID proofs, certificates, forms." /> : (
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>Document</TableCell><TableCell>Category</TableCell><TableCell>Expiry</TableCell>
              <TableCell>Status</TableCell><TableCell>v</TableCell><TableCell />
            </TableRow></TableHead>
            <TableBody>
              {rows.map((d) => (
                <TableRow key={d.id}>
                  <TableCell>
                    <a href={tenantApi.fileDownloadUrl(d.fileId)} target="_blank" rel="noreferrer" style={{ color: 'inherit' }}>
                      {d.label || d.fileName}
                    </a>
                  </TableCell>
                  <TableCell>{d.categoryName}</TableCell>
                  <TableCell>{d.expiresOn ?? '—'}</TableCell>
                  <TableCell><StatusChip value={d.status} /></TableCell>
                  <TableCell>{d.version}</TableCell>
                  <TableCell align="right">
                    <IconButton size="small" href={tenantApi.fileDownloadUrl(d.fileId)}><Download fontSize="small" /></IconButton>
                    {d.status === 'pending' && (
                      <>
                        <Button size="small" onClick={() => verify.mutate({ docId: d.id, status: 'verified' })}>Verify</Button>
                        <Button size="small" color="error" onClick={() => verify.mutate({ docId: d.id, status: 'rejected' })}>Reject</Button>
                      </>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>
      <Dialog open={dlg} onClose={() => setDlg(false)} fullWidth maxWidth="sm">
        <DialogTitle>Upload document</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TF select label="Category" value={form.categoryId} required
            onChange={(e) => setForm({ ...form, categoryId: e.target.value })}>
            {(cats.data ?? []).filter((c) => c.isActive).map((c) => (
              <MenuItem key={c.id} value={c.id}>{c.name}{c.requiredForOnboarding ? ' *' : ''}</MenuItem>
            ))}
          </TF>
          <TF label="Label" value={form.label} onChange={(e) => setForm({ ...form, label: e.target.value })} />
          <TF label="Document number" value={form.documentNumber} onChange={(e) => setForm({ ...form, documentNumber: e.target.value })} />
          <TF type="date" label="Issued on" value={form.issuedOn} onChange={(e) => setForm({ ...form, issuedOn: e.target.value })} />
          <TF type="date" label="Expires on" value={form.expiresOn} onChange={(e) => setForm({ ...form, expiresOn: e.target.value })} />
          <input ref={fileRef} type="file" accept=".pdf,.png,.jpg,.jpeg,.webp" />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setDlg(false)}>Cancel</Button>
          <Button variant="contained" disabled={!form.categoryId || upload.isPending} onClick={() => upload.mutate()}>
            {upload.isPending ? 'Uploading…' : 'Upload'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  )
}

// ===================== Letters =====================
export function LettersTab({ empId }: { empId: string }) {
  const toast = useToast()
  const inv = useInv(empId, ['letters'])
  const q = useQuery({ queryKey: ['emp', empId, 'letters'], queryFn: () => tenantApi.employeeLetters(empId) })
  const types = useQuery({ queryKey: ['letter-types'], queryFn: tenantApi.letterTypes })
  const [dlg, setDlg] = useState(false)
  const [viewId, setViewId] = useState<string | null>(null)
  const [issue, setIssue] = useState({ typeId: '', templateId: '' })
  const templates = useQuery({
    queryKey: ['letter-templates', issue.typeId],
    queryFn: () => tenantApi.letterTemplates(issue.typeId),
    enabled: !!issue.typeId,
  })
  const issueMut = useMutation({
    mutationFn: () => tenantApi.issueLetter(empId, issue.typeId, issue.templateId || undefined),
    onSuccess: () => { toast('Letter issued'); inv(); setDlg(false); setIssue({ typeId: '', templateId: '' }) }, ...useErr(toast),
  })
  const revoke = useMutation({
    mutationFn: (letterId: string) => tenantApi.revokeLetter(letterId),
    onSuccess: () => { toast('Letter revoked'); inv() }, ...useErr(toast),
  })
  if (q.isPending) return <TableSkeleton columns={4} />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => q.refetch()} />
  const rows = q.data ?? []
  const viewed = rows.find((l) => l.id === viewId)
  return (
    <SectionCard title="Letters" actions={<Button size="small" startIcon={<Add />} onClick={() => setDlg(true)}>Issue letter</Button>}>
      {rows.length === 0 ? <EmptyState title="No letters" hint="Issue a letter from a template — offer, confirmation, experience." /> : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Type</TableCell><TableCell>Subject</TableCell><TableCell>Issued</TableCell>
            <TableCell>Status</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {rows.map((l) => (
              <TableRow key={l.id}>
                <TableCell>{l.letterType}</TableCell>
                <TableCell><Button size="small" onClick={() => setViewId(l.id)}>{l.subject}</Button></TableCell>
                <TableCell>{l.issuedAt.slice(0, 10)}</TableCell>
                <TableCell><StatusChip value={l.status} /></TableCell>
                <TableCell align="right">
                  <IconButton size="small" href={tenantApi.letterDownloadUrl(l.id)}><Download fontSize="small" /></IconButton>
                  {l.status === 'issued' && <Button size="small" color="error" onClick={() => revoke.mutate(l.id)}>Revoke</Button>}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <Dialog open={dlg} onClose={() => setDlg(false)} fullWidth maxWidth="sm">
        <DialogTitle>Issue letter</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TF select label="Letter type" value={issue.typeId} onChange={(e) => setIssue({ typeId: e.target.value, templateId: '' })} required>
            {(types.data ?? []).filter((t) => t.isActive).map((t) => <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>)}
          </TF>
          <TF select label="Template" value={issue.templateId} onChange={(e) => setIssue({ ...issue, templateId: e.target.value })}>
            <MenuItem value="">Default template</MenuItem>
            {(templates.data ?? []).filter((t) => t.isActive).map((t) => <MenuItem key={t.id} value={t.id}>{t.name} (v{t.version})</MenuItem>)}
          </TF>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setDlg(false)}>Cancel</Button>
          <Button variant="contained" disabled={!issue.typeId || issueMut.isPending} onClick={() => issueMut.mutate()}>
            {issueMut.isPending ? 'Issuing…' : 'Issue'}
          </Button>
        </DialogActions>
      </Dialog>
      <Dialog open={!!viewed} onClose={() => setViewId(null)} fullWidth maxWidth="md">
        <DialogTitle>{viewed?.subject}</DialogTitle>
        <DialogContent>
          <Typography component="pre" sx={{ whiteSpace: 'pre-wrap', fontFamily: 'inherit', fontSize: 14 }}>
            {viewed?.renderedText}
          </Typography>
        </DialogContent>
        <DialogActions><Button onClick={() => setViewId(null)}>Close</Button></DialogActions>
      </Dialog>
    </SectionCard>
  )
}

// ===================== Salary =====================
const money = (n?: number) => n == null ? '—' : n.toLocaleString('en-IN', { maximumFractionDigits: 2 })

export function SalaryTab({ empId }: { empId: string }) {
  const toast = useToast()
  const inv = useInv(empId, ['salary'])
  const q = useQuery({ queryKey: ['emp', empId, 'salary'], queryFn: () => tenantApi.salaryAssignments(empId) })
  const structures = useQuery({ queryKey: ['salary-structures'], queryFn: tenantApi.salaryStructures })
  const [dlg, setDlg] = useState(false)
  const [form, setForm] = useState({ structureId: '', ctc: '', effectiveFrom: new Date().toISOString().slice(0, 10), reason: '' })
  const create = useMutation({
    mutationFn: () => tenantApi.createAssignment(empId, {
      salaryStructureId: form.structureId, ctcAmount: Number(form.ctc),
      effectiveFrom: form.effectiveFrom, reason: form.reason || undefined, submit: true,
    }),
    onSuccess: () => { toast('Assignment submitted'); inv(); setDlg(false) }, ...useErr(toast),
  })
  const act = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'submit' | 'approve' | 'cancel' | 'reject' }) =>
      tenantApi.assignmentAction(id, action),
    onSuccess: () => { toast('Done'); inv() }, ...useErr(toast),
  })
  if (q.isPending) return <TableSkeleton columns={5} />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => q.refetch()} />
  const rows = q.data ?? []
  return (
    <SectionCard title="Salary assignments" actions={<Button size="small" startIcon={<Add />} onClick={() => setDlg(true)}>New assignment</Button>}>
      {rows.length === 0 ? <EmptyState title="No salary assignments" hint="Assign a salary structure effective from a date." /> : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Structure</TableCell><TableCell>CTC</TableCell><TableCell>Effective</TableCell>
            <TableCell>Status</TableCell><TableCell>Lines</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {rows.map((a) => (
              <TableRow key={a.id}>
                <TableCell>{a.structureName ?? '—'}</TableCell>
                <TableCell>₹{money(a.ctcAmount)}</TableCell>
                <TableCell>{a.effectiveFrom}{a.effectiveTo ? ` → ${a.effectiveTo}` : ''}</TableCell>
                <TableCell><StatusChip value={a.status} /></TableCell>
                <TableCell>
                  {a.lines.length > 0
                    ? `${a.lines.length} components · ₹${money(a.lines.reduce((s, l) => s + (l.resolvedAmount ?? 0), 0))}/mo`
                    : '—'}
                </TableCell>
                <TableCell align="right">
                  {a.status === 'draft' && <Button size="small" onClick={() => act.mutate({ id: a.id, action: 'submit' })}>Submit</Button>}
                  {a.status === 'submitted' && (
                    <>
                      <Button size="small" color="success" onClick={() => act.mutate({ id: a.id, action: 'approve' })}>Approve</Button>
                      <Button size="small" color="error" onClick={() => act.mutate({ id: a.id, action: 'reject' })}>Reject</Button>
                    </>
                  )}
                  {['draft', 'submitted', 'approved'].includes(a.status) && (
                    <Button size="small" onClick={() => act.mutate({ id: a.id, action: 'cancel' })}>Cancel</Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <Dialog open={dlg} onClose={() => setDlg(false)} fullWidth maxWidth="sm">
        <DialogTitle>New salary assignment</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TF select label="Structure" value={form.structureId} onChange={(e) => setForm({ ...form, structureId: e.target.value })} required>
            {(structures.data ?? []).filter((s) => s.isActive).map((s) => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}
          </TF>
          <TF type="number" label="CTC (annual ₹)" value={form.ctc} onChange={(e) => setForm({ ...form, ctc: e.target.value })} required />
          <TF type="date" label="Effective from" value={form.effectiveFrom} onChange={(e) => setForm({ ...form, effectiveFrom: e.target.value })} />
          <TF label="Reason" value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setDlg(false)}>Cancel</Button>
          <Button variant="contained" disabled={!form.ctc || !form.structureId || create.isPending} onClick={() => create.mutate()}>
            {create.isPending ? 'Creating…' : 'Create & submit'}
          </Button>
        </DialogActions>
      </Dialog>
    </SectionCard>
  )
}

// ===================== Lifecycle & history =====================
const EVENT_TYPES = [
  'joining', 'confirmation', 'probation_extension', 'transfer', 'promotion',
  'manager_change', 'branch_change', 'rejoin', 'exit', 'correction',
]

export function LifecycleTab({ emp }: { emp: Employee }) {
  const toast = useToast()
  const inv = useInv(emp.id, ['history', 'lifecycle'])
  const history = useQuery({ queryKey: ['emp', emp.id, 'history'], queryFn: () => tenantApi.employeeHistory(emp.id) })
  const events = useQuery({ queryKey: ['emp', emp.id, 'lifecycle'], queryFn: () => tenantApi.lifecycleEvents(emp.id) })
  const designations = useQuery({ queryKey: ['masters', 'designations'], queryFn: () => mastersApi.designations(false) })
  const departments = useQuery({ queryKey: ['org', 'departments'], queryFn: tenantApi.departments })
  const branches = useQuery({ queryKey: ['org', 'branches'], queryFn: tenantApi.branches })
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const [dlg, setDlg] = useState(false)
  const [form, setForm] = useState({
    eventType: 'transfer', effectiveDate: new Date().toISOString().slice(0, 10), reason: '',
    branchId: '', departmentId: '', designationId: '', managerEmployeeId: '', employmentType: '',
    lastWorkingDate: '', exitReason: '',
  })
  const name = (list: { id: string; name?: string; displayName?: string }[] | undefined, id?: string | null) =>
    list?.find((x) => x.id === id)?.name ?? list?.find((x) => x.id === id)?.displayName ?? '—'
  const deptOf = (id?: string | null) => departments.data?.find((d) => d.id === id)?.name ?? '—'
  const create = useMutation({
    mutationFn: () => tenantApi.createLifecycleEvent(emp.id, {
      eventType: form.eventType, effectiveDate: form.effectiveDate,
      reason: form.reason || undefined, submit: true,
      payload: {
        branchId: form.branchId || undefined,
        departmentId: form.departmentId || undefined,
        designationId: form.designationId || undefined,
        managerEmployeeId: form.managerEmployeeId || undefined,
        employmentType: form.employmentType || undefined,
        lastWorkingDate: form.lastWorkingDate || undefined,
        exitReason: form.exitReason || undefined,
      },
    }),
    onSuccess: () => { toast('Event submitted'); inv(); setDlg(false) }, ...useErr(toast),
  })
  const act = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'submit' | 'approve' | 'cancel' | 'reject' }) =>
      tenantApi.lifecycleAction(id, action),
    onSuccess: () => { toast('Done'); inv() }, ...useErr(toast),
  })
  const f = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value })
  if (history.isPending || events.isPending) return <TableSkeleton columns={5} />
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <SectionCard title="Lifecycle events"
        actions={<Button size="small" startIcon={<Add />} onClick={() => setDlg(true)}>New event</Button>}>
        {(events.data ?? []).length === 0 ? <EmptyState title="No events" hint="Create a transfer, promotion, confirmation or exit event." /> : (
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>Event</TableCell><TableCell>Effective</TableCell><TableCell>Status</TableCell>
              <TableCell>Reason</TableCell><TableCell />
            </TableRow></TableHead>
            <TableBody>
              {(events.data ?? []).map((ev: LifecycleEvent) => (
                <TableRow key={ev.id}>
                  <TableCell>{ev.eventType}</TableCell>
                  <TableCell>{ev.effectiveDate}</TableCell>
                  <TableCell><StatusChip value={ev.status} /></TableCell>
                  <TableCell>{ev.reason ?? ev.rejectionReason ?? '—'}</TableCell>
                  <TableCell align="right">
                    {ev.status === 'draft' && <Button size="small" onClick={() => act.mutate({ id: ev.id, action: 'submit' })}>Submit</Button>}
                    {ev.status === 'submitted' && (
                      <>
                        <Button size="small" color="success" onClick={() => act.mutate({ id: ev.id, action: 'approve' })}>Approve</Button>
                        <Button size="small" color="error" onClick={() => act.mutate({ id: ev.id, action: 'reject' })}>Reject</Button>
                      </>
                    )}
                    {['draft', 'submitted', 'approved'].includes(ev.status) && (
                      <Button size="small" onClick={() => act.mutate({ id: ev.id, action: 'cancel' })}>Cancel</Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>
      <SectionCard title="Employment history">
        {(history.data ?? []).length === 0 ? <EmptyState title="No history rows" /> : (
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>Event</TableCell><TableCell>Period</TableCell><TableCell>Branch</TableCell>
              <TableCell>Department</TableCell><TableCell>Designation</TableCell><TableCell>Manager</TableCell>
            </TableRow></TableHead>
            <TableBody>
              {(history.data ?? []).map((h) => (
                <TableRow key={h.id}>
                  <TableCell>{h.eventType}{h.payrollAffecting ? ' ⚠' : ''}</TableCell>
                  <TableCell>{h.effectiveFrom} → {h.effectiveTo ?? 'present'}</TableCell>
                  <TableCell>{name(branches.data, h.branchId)}</TableCell>
                  <TableCell>{deptOf(h.departmentId)}</TableCell>
                  <TableCell>{name(designations.data, h.designationId)}</TableCell>
                  <TableCell>{name(employees.data, h.managerEmployeeId)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>
      <Dialog open={dlg} onClose={() => setDlg(false)} fullWidth maxWidth="sm">
        <DialogTitle>New lifecycle event</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TF select label="Event type" value={form.eventType} onChange={f('eventType')}>
            {EVENT_TYPES.map((t) => <MenuItem key={t} value={t}>{t.replace(/_/g, ' ')}</MenuItem>)}
          </TF>
          <TF type="date" label="Effective date" value={form.effectiveDate} onChange={f('effectiveDate')} required />
          <TF label="Reason" value={form.reason} onChange={f('reason')} />
          {['transfer', 'branch_change', 'correction'].includes(form.eventType) && (
            <TF select label="New branch" value={form.branchId} onChange={f('branchId')}>
              {(branches.data ?? []).map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
            </TF>
          )}
          {['transfer', 'correction'].includes(form.eventType) && (
            <TF select label="New department" value={form.departmentId} onChange={f('departmentId')}>
              {(departments.data ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
            </TF>
          )}
          {['promotion', 'correction'].includes(form.eventType) && (
            <TF select label="New designation" value={form.designationId} onChange={f('designationId')}>
              {(designations.data ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
            </TF>
          )}
          {['manager_change', 'correction'].includes(form.eventType) && (
            <TF select label="New manager" value={form.managerEmployeeId} onChange={f('managerEmployeeId')}>
              {(employees.data ?? []).filter((x) => x.id !== emp.id).map((x) => <MenuItem key={x.id} value={x.id}>{x.displayName}</MenuItem>)}
            </TF>
          )}
          {form.eventType === 'exit' && (
            <>
              <TF type="date" label="Last working date" value={form.lastWorkingDate} onChange={f('lastWorkingDate')} />
              <TF select label="Exit reason" value={form.exitReason} onChange={f('exitReason')}>
                {['resignation', 'termination', 'retirement', 'absconding'].map((r) => <MenuItem key={r} value={r}>{r}</MenuItem>)}
              </TF>
            </>
          )}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setDlg(false)}>Cancel</Button>
          <Button variant="contained" disabled={create.isPending} onClick={() => create.mutate()}>
            {create.isPending ? 'Submitting…' : 'Create & submit'}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}
