import { useState } from 'react'
import {
  Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, MenuItem, Tab, Table, TableBody, TableCell,
  TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { Add, Edit, Publish, History } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  tenantApi, type DocCategory, type DocCategoryInput, type Letterhead, type LetterheadInput,
  type LetterSignatory, type SignatoryInput, type LetterTemplateFamily, type LetterTemplateVersion,
  type LetterType, type LetterTypeInput, type LetterVersionInput,
} from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'

const TF = (p: React.ComponentProps<typeof TextField>) => (
  <TextField size="small" fullWidth slotProps={{ inputLabel: { shrink: true } }} {...p} />
)

const err = (toast: (m: string, s?: 'success' | 'error' | 'info') => void) => ({
  onError: (e: unknown) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
})

const scopeLabel = (v: { companyId?: string | null; branchId?: string | null }) =>
  v.branchId ? 'Branch' : v.companyId ? 'Company' : 'Tenant-wide'

const windowLabel = (v: { effectiveFrom: string; effectiveTo?: string | null }) =>
  `${v.effectiveFrom}${v.effectiveTo ? ` → ${v.effectiveTo}` : ' →'}`

// ---------- Document categories ----------

const emptyCat = (): DocCategoryInput => ({
  code: '', name: '', requiredForOnboarding: false, retentionYears: undefined,
  confidential: false, trackExpiry: false, isActive: true,
})

function CategoriesPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const q = useQuery({ queryKey: ['doc-categories'], queryFn: tenantApi.docCategories })
  const [dlg, setDlg] = useState<DocCategoryInput | null>(null)
  const f = (k: keyof DocCategoryInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg((d) => d && { ...d, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const save = useMutation({
    mutationFn: () => tenantApi.saveDocCategory(dlg!),
    onSuccess: () => { toast('Category saved'); void qc.invalidateQueries({ queryKey: ['doc-categories'] }); setDlg(null) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })

  return (
    <SectionCard title="Document categories"
      actions={<Button size="small" variant="contained" startIcon={<Add />} onClick={() => setDlg(emptyCat())}>New category</Button>}>
      {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : !q.data?.length ? (
        <EmptyState title="No categories" hint="Create the first document category." />
      ) : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Onboarding</TableCell>
            <TableCell>Retention</TableCell><TableCell>Confidential</TableCell><TableCell>Expiry</TableCell>
            <TableCell>Status</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {q.data.map((c: DocCategory) => (
              <TableRow key={c.id} hover>
                <TableCell><code>{c.code}</code></TableCell>
                <TableCell>{c.name}</TableCell>
                <TableCell>{c.requiredForOnboarding ? 'Yes' : '—'}</TableCell>
                <TableCell>{c.retentionYears != null ? `${c.retentionYears}y` : '—'}</TableCell>
                <TableCell>{c.confidential ? 'Yes' : '—'}</TableCell>
                <TableCell>{c.trackExpiry ? 'Yes' : '—'}</TableCell>
                <TableCell><StatusChip value={c.isActive ? 'active' : 'inactive'} /></TableCell>
                <TableCell align="right">
                  <Button size="small" startIcon={<Edit />} onClick={() => setDlg({
                    code: c.code, name: c.name, description: c.description,
                    requiredForOnboarding: c.requiredForOnboarding, retentionYears: c.retentionYears,
                    confidential: c.confidential, trackExpiry: c.trackExpiry, isActive: c.isActive, rowVersion: c.rowVersion,
                  })}>Edit</Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {dlg && (
        <Dialog open onClose={() => setDlg(null)} maxWidth="sm" fullWidth>
          <DialogTitle>{dlg.rowVersion != null ? 'Edit category' : 'New category'}</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: '12px !important' }}>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 2 }}>
              <TF label="Code" value={dlg.code} onChange={f('code')} disabled={dlg.rowVersion != null} />
              <TF label="Name" value={dlg.name} onChange={f('name')} />
            </Box>
            <TF label="Description" value={dlg.description ?? ''} onChange={f('description')} />
            <TF label="Retention (years)" type="number" value={dlg.retentionYears ?? ''}
              onChange={(e) => setDlg({ ...dlg, retentionYears: e.target.value === '' ? undefined : Number(e.target.value) })} />
            <Box sx={{ display: 'flex', flexWrap: 'wrap', columnGap: 3 }}>
              <FormControlLabel control={<Checkbox checked={dlg.requiredForOnboarding} onChange={f('requiredForOnboarding')} />} label="Required for onboarding" />
              <FormControlLabel control={<Checkbox checked={dlg.confidential} onChange={f('confidential')} />} label="Confidential" />
              <FormControlLabel control={<Checkbox checked={dlg.trackExpiry} onChange={f('trackExpiry')} />} label="Track expiry" />
              <FormControlLabel control={<Checkbox checked={dlg.isActive} onChange={f('isActive')} />} label="Active" />
            </Box>
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2 }}>
            <Button onClick={() => setDlg(null)}>Cancel</Button>
            <Button variant="contained" disabled={!dlg.code.trim() || !dlg.name.trim() || save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? 'Saving…' : 'Save'}
            </Button>
          </DialogActions>
        </Dialog>
      )}
    </SectionCard>
  )
}

// ---------- Letter types ----------

const emptyType = (): LetterTypeInput => ({ code: '', name: '', isActive: true })

function LetterTypesPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const types = useQuery({ queryKey: ['letter-types'], queryFn: tenantApi.letterTypes })
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const signatories = useQuery({ queryKey: ['letter-signatories'], queryFn: tenantApi.letterSignatories })
  const [dlg, setDlg] = useState<LetterTypeInput | null>(null)
  const f = (k: keyof LetterTypeInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg((d) => d && { ...d, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const saveType = useMutation({
    mutationFn: () => tenantApi.saveLetterType(dlg!),
    onSuccess: () => { toast('Letter type saved'); void qc.invalidateQueries({ queryKey: ['letter-types'] }); setDlg(null) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  const signatory = (t: LetterType) =>
    (t.signatoryId && signatories.data?.find((s) => s.id === t.signatoryId)?.name)
    ?? (t.signatoryEmployeeId && employees.data?.find((e) => e.id === t.signatoryEmployeeId)?.displayName)
    ?? '—'

  return (
    <>
      <SectionCard title="Letter types"
        actions={<Button size="small" variant="contained" startIcon={<Add />} onClick={() => setDlg(emptyType())}>New type</Button>}>
        {types.isLoading ? <TableSkeleton /> : types.isError ? <ErrorAlert error={types.error} /> : !types.data?.length ? (
          <EmptyState title="No letter types" hint="Create a type (e.g. Offer Letter), then manage its template versions in the Templates tab." />
        ) : (
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Signatory</TableCell>
              <TableCell>Numbering</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
            </TableRow></TableHead>
            <TableBody>
              {types.data.map((t: LetterType) => (
                <TableRow key={t.id} hover>
                  <TableCell><code>{t.code}</code></TableCell>
                  <TableCell>{t.name}</TableCell>
                  <TableCell>{signatory(t)}</TableCell>
                  <TableCell><code>{t.numberPrefix ?? 'LTR'}</code> · {t.numberReset ?? 'yearly'}</TableCell>
                  <TableCell><StatusChip value={t.isActive ? 'active' : 'inactive'} /></TableCell>
                  <TableCell align="right">
                    <Button size="small" startIcon={<Edit />} onClick={() => setDlg({
                      code: t.code, name: t.name, description: t.description,
                      signatoryEmployeeId: t.signatoryEmployeeId, signatoryId: t.signatoryId,
                      numberPrefix: t.numberPrefix, numberReset: t.numberReset,
                      isActive: t.isActive, rowVersion: t.rowVersion,
                    })}>Edit</Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>

      {dlg && (
        <Dialog open onClose={() => setDlg(null)} maxWidth="sm" fullWidth>
          <DialogTitle>{dlg.rowVersion != null ? 'Edit letter type' : 'New letter type'}</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: '12px !important' }}>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 2 }}>
              <TF label="Code" value={dlg.code} onChange={f('code')} disabled={dlg.rowVersion != null} />
              <TF label="Name" value={dlg.name} onChange={f('name')} />
            </Box>
            <TF label="Description" value={dlg.description ?? ''} onChange={f('description')} />
            <TF select label="Signatory" value={dlg.signatoryId ?? ''}
              onChange={(e) => setDlg({ ...dlg, signatoryId: e.target.value || undefined })}>
              <MenuItem value="">None</MenuItem>
              {(signatories.data ?? []).filter((s) => s.isActive).map((s) => (
                <MenuItem key={s.id} value={s.id}>{s.name}{s.designation ? ` · ${s.designation}` : ''}</MenuItem>
              ))}
            </TF>
            <TF select label="Legacy signatory (employee)" value={dlg.signatoryEmployeeId ?? ''}
              onChange={(e) => setDlg({ ...dlg, signatoryEmployeeId: e.target.value || undefined })}
              helperText="Deprecated — prefer a signatory above.">
              <MenuItem value="">None</MenuItem>
              {employees.data?.map((e) => <MenuItem key={e.id} value={e.id}>{e.displayName} · {e.employeeCode}</MenuItem>)}
            </TF>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
              <TF label="Number prefix" value={dlg.numberPrefix ?? ''}
                onChange={(e) => setDlg({ ...dlg, numberPrefix: e.target.value || undefined })}
                placeholder="LTR" helperText="e.g. OFFER, APPT" />
              <TF select label="Numbering reset" value={dlg.numberReset ?? 'yearly'}
                onChange={(e) => setDlg({ ...dlg, numberReset: e.target.value })}>
                <MenuItem value="yearly">Yearly</MenuItem>
                <MenuItem value="monthly">Monthly</MenuItem>
                <MenuItem value="never">Never</MenuItem>
              </TF>
            </Box>
            <FormControlLabel control={<Checkbox checked={dlg.isActive} onChange={f('isActive')} />} label="Active" />
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2 }}>
            <Button onClick={() => setDlg(null)}>Cancel</Button>
            <Button variant="contained" disabled={!dlg.code.trim() || !dlg.name.trim() || saveType.isPending} onClick={() => saveType.mutate()}>
              {saveType.isPending ? 'Saving…' : 'Save'}
            </Button>
          </DialogActions>
        </Dialog>
      )}
    </>
  )
}

// ---------- Template families & versions ----------

const emptyVersion = (): LetterVersionInput => ({
  name: '', subject: '', body: '', effectiveFrom: new Date().toISOString().slice(0, 10),
  effectiveTo: null, companyId: null, branchId: null, letterheadId: null,
})

function TemplatesPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const types = useQuery({ queryKey: ['letter-types'], queryFn: tenantApi.letterTypes })
  const companies = useQuery({ queryKey: ['companies'], queryFn: tenantApi.companies })
  const branches = useQuery({ queryKey: ['branches'], queryFn: tenantApi.branches })
  const letterheads = useQuery({ queryKey: ['letterheads'], queryFn: tenantApi.letterheads })
  const fields = useQuery({ queryKey: ['letter-merge-fields'], queryFn: tenantApi.letterMergeFields })
  const [typeId, setTypeId] = useState('')
  const [familyId, setFamilyId] = useState<string | null>(null)
  const [famDlg, setFamDlg] = useState('')
  const [ver, setVer] = useState<{ input: LetterVersionInput; id?: string; baseId?: string } | null>(null)
  const [showPreview, setShowPreview] = useState(false)

  const families = useQuery({
    queryKey: ['letter-families', typeId],
    queryFn: () => tenantApi.letterFamilies(typeId),
    enabled: !!typeId,
  })
  const versions = useQuery({
    queryKey: ['letter-versions', familyId],
    queryFn: () => tenantApi.letterFamilyVersions(familyId!),
    enabled: !!familyId,
  })
  const invVersions = () => void qc.invalidateQueries({ queryKey: ['letter-versions', familyId] })
  const invFamilies = () => void qc.invalidateQueries({ queryKey: ['letter-families', typeId] })

  const addFamily = useMutation({
    mutationFn: () => tenantApi.createLetterFamily({ letterTypeId: typeId, name: famDlg.trim() }),
    onSuccess: (r) => { toast('Family created'); invFamilies(); setFamDlg(''); setFamilyId(r.id) },
    ...err(toast),
  })
  const saveVersion = useMutation({
    mutationFn: () => {
      const v = ver!
      return v.id
        ? tenantApi.updateLetterVersion(v.id, v.input)
        : tenantApi.createLetterVersion(familyId!, v.input, v.baseId)
    },
    onSuccess: () => { toast('Draft saved'); invVersions(); invFamilies(); setVer(null) },
    ...err(toast),
  })
  const publish = useMutation({
    mutationFn: (id: string) => tenantApi.publishLetterVersion(id),
    onSuccess: () => { toast('Published'); invVersions() }, ...err(toast),
  })
  const retire = useMutation({
    mutationFn: (id: string) => tenantApi.retireLetterVersion(id),
    onSuccess: () => { toast('Retired'); invVersions() }, ...err(toast),
  })

  const companyName = (id?: string | null) => companies.data?.find((c) => c.id === id)?.name ?? '—'
  const letterheadName = (id?: string | null) => letterheads.data?.find((l) => l.id === id)?.name ?? '—'
  const setV = (k: keyof LetterVersionInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setVer((v) => v && { ...v, input: { ...v.input, [k]: e.target.value || null } })

  return (
    <>
      <SectionCard title="Template families" actions={
        <TF select label="Letter type" value={typeId} sx={{ minWidth: 260 }}
          onChange={(e) => { setTypeId(e.target.value); setFamilyId(null) }}>
          <MenuItem value="">Select a letter type…</MenuItem>
          {(types.data ?? []).filter((t) => t.isActive).map((t) => <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>)}
        </TF>
      }>
        {!typeId ? (
          <EmptyState title="Pick a letter type" hint="Templates are grouped into families; each save creates a new version." />
        ) : families.isLoading ? <TableSkeleton /> : (
          <>
            <Box sx={{ display: 'flex', gap: 1, mb: 2 }}>
              <TF label="New family name" value={famDlg} onChange={(e) => setFamDlg(e.target.value)} sx={{ maxWidth: 320 }} />
              <Button size="small" variant="contained" startIcon={<Add />} disabled={!famDlg.trim() || addFamily.isPending}
                onClick={() => addFamily.mutate()}>Add family</Button>
            </Box>
            {!families.data?.length ? (
              <EmptyState title="No families" hint="Create a family — e.g. 'Standard appointment'." />
            ) : (
              <Table size="small">
                <TableHead><TableRow>
                  <TableCell>Family</TableCell><TableCell>Versions</TableCell><TableCell />
                </TableRow></TableHead>
                <TableBody>
                  {families.data.map((fm: LetterTemplateFamily) => (
                    <TableRow key={fm.id} hover selected={familyId === fm.id} onClick={() => setFamilyId(fm.id)} sx={{ cursor: 'pointer' }}>
                      <TableCell>{fm.name}</TableCell>
                      <TableCell>{fm.versionCount}</TableCell>
                      <TableCell align="right" onClick={(e) => e.stopPropagation()}>
                        <Button size="small" startIcon={<Add />} onClick={() => setVer({ input: emptyVersion() })}>New draft</Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </>
        )}
      </SectionCard>

      {familyId && (
        <SectionCard title={`Versions — ${families.data?.find((x) => x.id === familyId)?.name ?? ''}`}>
          {versions.isLoading ? <TableSkeleton /> : !versions.data?.length ? (
            <EmptyState title="No versions" hint="Create a draft, publish it, and it becomes the active template." />
          ) : (
            <Table size="small">
              <TableHead><TableRow>
                <TableCell>v#</TableCell><TableCell>Name</TableCell><TableCell>Subject</TableCell>
                <TableCell>Status</TableCell><TableCell>Applicability</TableCell><TableCell>Effective</TableCell>
                <TableCell>Letterhead</TableCell><TableCell align="right">Actions</TableCell>
              </TableRow></TableHead>
              <TableBody>
                {versions.data.map((v: LetterTemplateVersion) => (
                  <TableRow key={v.id} hover>
                    <TableCell>v{v.version}</TableCell>
                    <TableCell>{v.name}</TableCell>
                    <TableCell>{v.subject}</TableCell>
                    <TableCell><StatusChip value={v.status} /></TableCell>
                    <TableCell>{scopeLabel(v)}{v.companyId ? ` · ${companyName(v.companyId)}` : ''}</TableCell>
                    <TableCell>{windowLabel(v)}</TableCell>
                    <TableCell>{letterheadName(v.letterheadId)}</TableCell>
                    <TableCell align="right">
                      {v.status === 'draft' && (
                        <>
                          <Button size="small" startIcon={<Edit />} onClick={() => setVer({
                            id: v.id,
                            input: {
                              name: v.name, subject: v.subject, body: v.body, effectiveFrom: v.effectiveFrom,
                              effectiveTo: v.effectiveTo ?? null, companyId: v.companyId ?? null,
                              branchId: v.branchId ?? null, letterheadId: v.letterheadId ?? null, rowVersion: v.rowVersion,
                            },
                          })}>Edit</Button>
                          <Button size="small" startIcon={<Publish />} disabled={publish.isPending}
                            onClick={() => publish.mutate(v.id)}>Publish</Button>
                        </>
                      )}
                      {v.status === 'published' && (
                        <>
                          <Button size="small" startIcon={<History />} onClick={() => setVer({ baseId: v.id, input: { ...emptyVersion(), name: v.name, subject: v.subject, body: v.body, companyId: v.companyId ?? null, branchId: v.branchId ?? null, letterheadId: v.letterheadId ?? null } })}>
                            New draft
                          </Button>
                          <Button size="small" color="error" disabled={retire.isPending} onClick={() => retire.mutate(v.id)}>Retire</Button>
                        </>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </SectionCard>
      )}

      {ver && (
        <Dialog open onClose={() => setVer(null)} maxWidth="md" fullWidth>
          <DialogTitle>{ver.id ? `Edit draft v${versions.data?.find((x) => x.id === ver.id)?.version ?? ''}` : ver.baseId ? 'New draft from published version' : 'New draft version'}</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: '12px !important' }}>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 2 }}>
              <TF label="Version name" value={ver.input.name} onChange={setV('name')} />
              <TF label="Subject" value={ver.input.subject} onChange={setV('subject')} />
            </Box>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
              <TF label="Effective from" type="date" value={ver.input.effectiveFrom} onChange={setV('effectiveFrom')} />
              <TF label="Effective to (optional)" type="date" value={ver.input.effectiveTo ?? ''} onChange={setV('effectiveTo')} />
            </Box>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 2 }}>
              <TF select label="Company (optional)" value={ver.input.companyId ?? ''}
                onChange={(e) => setVer({ ...ver, input: { ...ver.input, companyId: e.target.value || null, branchId: null } })}>
                <MenuItem value="">Tenant-wide</MenuItem>
                {(companies.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
              </TF>
              <TF select label="Branch (optional)" value={ver.input.branchId ?? ''} disabled={!ver.input.companyId}
                onChange={(e) => setVer({ ...ver, input: { ...ver.input, branchId: e.target.value || null } })}>
                <MenuItem value="">Company-wide</MenuItem>
                {(branches.data ?? []).filter((b) => b.companyId === ver.input.companyId).map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
              </TF>
              <TF select label="Letterhead (optional)" value={ver.input.letterheadId ?? ''}
                onChange={(e) => setVer({ ...ver, input: { ...ver.input, letterheadId: e.target.value || null } })}>
                <MenuItem value="">None</MenuItem>
                {(letterheads.data ?? []).filter((l) => l.isActive).map((l) => <MenuItem key={l.id} value={l.id}>{l.name}</MenuItem>)}
              </TF>
            </Box>
            <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
              <TF label="Body (HTML)" multiline minRows={10} value={ver.input.body} onChange={setV('body')}
                helperText={`Fields: ${(fields.data ?? []).map((m) => `{{ ${m.name} }}`).join('  ')}`} />
            </Box>
            <Box>
              <Button size="small" onClick={() => setShowPreview((s) => !s)}>{showPreview ? 'Hide preview' : 'Preview HTML'}</Button>
              {showPreview && (
                <Box component="iframe" sandbox="" srcDoc={ver.input.body}
                  sx={{ width: '100%', height: 280, border: '1px solid', borderColor: 'divider', borderRadius: 1, mt: 1, bgcolor: 'white' }} />
              )}
              <Typography variant="caption" color="text.secondary" sx={{ mt: 0.5, display: 'block' }}>
                Allowed HTML only — no scripts, handlers or external resources; it is validated on save.
              </Typography>
            </Box>
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2 }}>
            <Button onClick={() => setVer(null)}>Cancel</Button>
            <Button variant="contained" disabled={!ver.input.name.trim() || !ver.input.subject.trim() || !ver.input.body.trim() || !ver.input.effectiveFrom || saveVersion.isPending}
              onClick={() => saveVersion.mutate()}>
              {saveVersion.isPending ? 'Saving…' : 'Save draft'}
            </Button>
          </DialogActions>
        </Dialog>
      )}
    </>
  )
}

// ---------- Signatories ----------

const emptySignatory = (): SignatoryInput & { id?: string } => ({ name: '', designation: '', employeeId: null, isActive: true })

function SignatoriesPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const q = useQuery({ queryKey: ['letter-signatories'], queryFn: tenantApi.letterSignatories })
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const [dlg, setDlg] = useState<(SignatoryInput & { id?: string }) | null>(null)
  const save = useMutation({
    mutationFn: () => {
      const d = dlg!
      return d.id ? tenantApi.updateSignatory(d.id, d) : tenantApi.createSignatory(d)
    },
    onSuccess: () => { toast('Signatory saved'); void qc.invalidateQueries({ queryKey: ['letter-signatories'] }); setDlg(null) },
    ...err(toast),
  })
  const empName = (id?: string | null) => employees.data?.find((e) => e.id === id)?.displayName ?? '—'

  return (
    <SectionCard title="Letter signatories"
      actions={<Button size="small" variant="contained" startIcon={<Add />} onClick={() => setDlg(emptySignatory())}>New signatory</Button>}>
      {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : !q.data?.length ? (
        <EmptyState title="No signatories" hint="Signatories provide the name and designation stamped on issued letters." />
      ) : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Name</TableCell><TableCell>Designation</TableCell><TableCell>Linked employee</TableCell>
            <TableCell>Status</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {q.data.map((s: LetterSignatory) => (
              <TableRow key={s.id} hover>
                <TableCell>{s.name}</TableCell>
                <TableCell>{s.designation ?? '—'}</TableCell>
                <TableCell>{s.employeeId ? empName(s.employeeId) : '—'}</TableCell>
                <TableCell><StatusChip value={s.isActive ? 'active' : 'inactive'} /></TableCell>
                <TableCell align="right">
                  <Button size="small" startIcon={<Edit />} onClick={() => setDlg({
                    id: s.id, name: s.name, designation: s.designation ?? '',
                    employeeId: s.employeeId ?? null, isActive: s.isActive, rowVersion: s.rowVersion,
                  })}>Edit</Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {dlg && (
        <Dialog open onClose={() => setDlg(null)} maxWidth="sm" fullWidth>
          <DialogTitle>{dlg.id ? 'Edit signatory' : 'New signatory'}</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: '12px !important' }}>
            <TF label="Name" value={dlg.name} onChange={(e) => setDlg({ ...dlg, name: e.target.value })} />
            <TF label="Designation" value={dlg.designation ?? ''} onChange={(e) => setDlg({ ...dlg, designation: e.target.value || null })} />
            <TF select label="Linked employee (optional)" value={dlg.employeeId ?? ''}
              onChange={(e) => setDlg({ ...dlg, employeeId: e.target.value || null })}>
              <MenuItem value="">None</MenuItem>
              {employees.data?.map((e) => <MenuItem key={e.id} value={e.id}>{e.displayName} · {e.employeeCode}</MenuItem>)}
            </TF>
            <FormControlLabel control={<Checkbox checked={dlg.isActive} onChange={(e) => setDlg({ ...dlg, isActive: e.target.checked })} />} label="Active" />
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2 }}>
            <Button onClick={() => setDlg(null)}>Cancel</Button>
            <Button variant="contained" disabled={!dlg.name.trim() || save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? 'Saving…' : 'Save'}
            </Button>
          </DialogActions>
        </Dialog>
      )}
    </SectionCard>
  )
}

// ---------- Letterheads ----------

const emptyLetterhead = (): LetterheadInput & { id?: string } => ({
  name: '', companyId: null, branchId: null, logoFileId: null,
  headerHtml: '', footerHtml: '', marginMm: 15, isActive: true,
})

function LetterheadsPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const q = useQuery({ queryKey: ['letterheads'], queryFn: tenantApi.letterheads })
  const companies = useQuery({ queryKey: ['companies'], queryFn: tenantApi.companies })
  const branches = useQuery({ queryKey: ['branches'], queryFn: tenantApi.branches })
  const [dlg, setDlg] = useState<(LetterheadInput & { id?: string }) | null>(null)
  const [uploading, setUploading] = useState(false)
  const save = useMutation({
    mutationFn: () => {
      const d = dlg!
      return d.id ? tenantApi.updateLetterhead(d.id, d) : tenantApi.createLetterhead(d)
    },
    onSuccess: () => { toast('Letterhead saved'); void qc.invalidateQueries({ queryKey: ['letterheads'] }); setDlg(null) },
    ...err(toast),
  })
  const uploadLogo = async (file: File) => {
    setUploading(true)
    try {
      const form = new FormData()
      form.append('purpose', 'core.org.logo')
      form.append('file', file)
      const r = await tenantApi.uploadFile(form)
      setDlg((d) => d && { ...d, logoFileId: r.id })
      toast('Logo uploaded')
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Upload failed', 'error')
    } finally {
      setUploading(false)
    }
  }

  return (
    <SectionCard title="Letterheads"
      actions={<Button size="small" variant="contained" startIcon={<Add />} onClick={() => setDlg(emptyLetterhead())}>New letterhead</Button>}>
      {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : !q.data?.length ? (
        <EmptyState title="No letterheads" hint="Letterheads carry the logo, header and footer stamped onto issued PDFs." />
      ) : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Name</TableCell><TableCell>Applicability</TableCell><TableCell>Logo</TableCell>
            <TableCell>Margin</TableCell><TableCell>Status</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {q.data.map((l: Letterhead) => (
              <TableRow key={l.id} hover>
                <TableCell>{l.name}</TableCell>
                <TableCell>{scopeLabel(l)}</TableCell>
                <TableCell>{l.logoFileId ? 'Yes' : '—'}</TableCell>
                <TableCell>{l.marginMm}mm</TableCell>
                <TableCell><StatusChip value={l.isActive ? 'active' : 'inactive'} /></TableCell>
                <TableCell align="right">
                  <Button size="small" startIcon={<Edit />} onClick={() => setDlg({
                    id: l.id, name: l.name, companyId: l.companyId ?? null, branchId: l.branchId ?? null,
                    logoFileId: l.logoFileId ?? null, headerHtml: l.headerHtml ?? '', footerHtml: l.footerHtml ?? '',
                    marginMm: l.marginMm, isActive: l.isActive, rowVersion: l.rowVersion,
                  })}>Edit</Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {dlg && (
        <Dialog open onClose={() => setDlg(null)} maxWidth="md" fullWidth>
          <DialogTitle>{dlg.id ? 'Edit letterhead' : 'New letterhead'}</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: '12px !important' }}>
            <TF label="Name" value={dlg.name} onChange={(e) => setDlg({ ...dlg, name: e.target.value })} />
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 2 }}>
              <TF select label="Company (optional)" value={dlg.companyId ?? ''}
                onChange={(e) => setDlg({ ...dlg, companyId: e.target.value || null, branchId: null })}>
                <MenuItem value="">Tenant-wide</MenuItem>
                {(companies.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
              </TF>
              <TF select label="Branch (optional)" value={dlg.branchId ?? ''} disabled={!dlg.companyId}
                onChange={(e) => setDlg({ ...dlg, branchId: e.target.value || null })}>
                <MenuItem value="">Company-wide</MenuItem>
                {(branches.data ?? []).filter((b) => b.companyId === dlg.companyId).map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
              </TF>
              <TF label="Margin (mm)" type="number" value={dlg.marginMm}
                onChange={(e) => setDlg({ ...dlg, marginMm: Number(e.target.value) || 15 })} />
            </Box>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
              <Button component="label" size="small" variant="outlined" disabled={uploading}>
                {uploading ? 'Uploading…' : 'Upload logo'}
                <input hidden type="file" accept=".png,.jpg,.jpeg,.webp"
                  onChange={(e) => { const fl = e.target.files?.[0]; if (fl) void uploadLogo(fl) }} />
              </Button>
              <Typography variant="caption" color="text.secondary">
                {dlg.logoFileId ? `Logo set (${dlg.logoFileId.slice(0, 8)}…) — embedded into PDFs` : 'No logo'}
              </Typography>
              {dlg.logoFileId && <Button size="small" onClick={() => setDlg({ ...dlg, logoFileId: null })}>Remove</Button>}
            </Box>
            <TF label="Header HTML (optional)" multiline minRows={3} value={dlg.headerHtml ?? ''}
              onChange={(e) => setDlg({ ...dlg, headerHtml: e.target.value || null })} />
            <TF label="Footer HTML (optional)" multiline minRows={3} value={dlg.footerHtml ?? ''}
              onChange={(e) => setDlg({ ...dlg, footerHtml: e.target.value || null })} />
            <FormControlLabel control={<Checkbox checked={dlg.isActive} onChange={(e) => setDlg({ ...dlg, isActive: e.target.checked })} />} label="Active" />
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2 }}>
            <Button onClick={() => setDlg(null)}>Cancel</Button>
            <Button variant="contained" disabled={!dlg.name.trim() || save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? 'Saving…' : 'Save'}
            </Button>
          </DialogActions>
        </Dialog>
      )}
    </SectionCard>
  )
}

// ---------- Merge fields ----------

function MergeFieldsPanel() {
  const q = useQuery({ queryKey: ['letter-merge-fields'], queryFn: tenantApi.letterMergeFields })
  return (
    <SectionCard title="Merge fields">
      {q.isLoading ? <TableSkeleton /> : q.isError ? <ErrorAlert error={q.error} /> : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Field</TableCell><TableCell>Source</TableCell><TableCell>Salary permission</TableCell>
          </TableRow></TableHead>
          <TableBody>
            {(q.data ?? []).map((m) => (
              <TableRow key={m.name} hover>
                <TableCell><code>{`{{ ${m.name} }}`}</code></TableCell>
                <TableCell>{m.description}</TableCell>
                <TableCell>{m.requiresSalaryPermission ? 'Required' : '—'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </SectionCard>
  )
}

export function HrDocsPage() {
  const [tab, setTab] = useState(0)
  const panels = [<CategoriesPanel key="c" />, <LetterTypesPanel key="t" />, <TemplatesPanel key="v" />, <SignatoriesPanel key="s" />, <LetterheadsPanel key="l" />, <MergeFieldsPanel key="m" />]
  return (
    <Box>
      <PageHeader title="HR documents setup" subtitle="Categories, letter types, template versions, signatories and letterheads." />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2, borderBottom: 1, borderColor: 'divider' }}>
        <Tab label="Document categories" />
        <Tab label="Letter types" />
        <Tab label="Template versions" />
        <Tab label="Signatories" />
        <Tab label="Letterheads" />
        <Tab label="Merge fields" />
      </Tabs>
      {panels[tab]}
    </Box>
  )
}
