import { useState } from 'react'
import {
  Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, MenuItem, Tab, Table, TableBody, TableCell,
  TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { Add, Edit } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  tenantApi, type DocCategory, type DocCategoryInput, type LetterTemplateInput,
  type LetterType, type LetterTypeInput,
} from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'

const TF = (p: React.ComponentProps<typeof TextField>) => (
  <TextField size="small" fullWidth slotProps={{ inputLabel: { shrink: true } }} {...p} />
)

const MERGE_FIELDS = [
  'display_name', 'first_name', 'last_name', 'employee_code', 'work_email',
  'employment_type', 'joining_date', 'company_name', 'branch_name',
  'department_name', 'designation_name', 'signatory_name', 'letter_type', 'date',
]

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

// ---------- Letter types & templates ----------

const emptyType = (): LetterTypeInput => ({ code: '', name: '', isActive: true })
const emptyTpl = (typeId: string): LetterTemplateInput => ({ letterTypeId: typeId, name: '', subject: '', body: '', isActive: true })

function LetterTypesPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const types = useQuery({ queryKey: ['letter-types'], queryFn: tenantApi.letterTypes })
  const employees = useQuery({ queryKey: ['employees'], queryFn: tenantApi.employees })
  const [sel, setSel] = useState<string | null>(null)
  const templates = useQuery({
    queryKey: ['letter-templates', sel],
    queryFn: () => tenantApi.letterTemplates(sel!),
    enabled: !!sel,
  })
  const [dlg, setDlg] = useState<LetterTypeInput | null>(null)
  const [tpl, setTpl] = useState<LetterTemplateInput | null>(null)
  const f = (k: keyof LetterTypeInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg((d) => d && { ...d, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const saveType = useMutation({
    mutationFn: () => tenantApi.saveLetterType(dlg!),
    onSuccess: () => { toast('Letter type saved'); void qc.invalidateQueries({ queryKey: ['letter-types'] }); setDlg(null) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  const saveTpl = useMutation({
    mutationFn: () => tenantApi.saveLetterTemplate(tpl!),
    onSuccess: () => { toast('Template saved'); void qc.invalidateQueries({ queryKey: ['letter-templates'] }); setTpl(null) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  const signatory = (id?: string) => employees.data?.find((e) => e.id === id)?.displayName ?? '—'

  return (
    <>
      <SectionCard title="Letter types"
        actions={<Button size="small" variant="contained" startIcon={<Add />} onClick={() => setDlg(emptyType())}>New type</Button>}>
        {types.isLoading ? <TableSkeleton /> : types.isError ? <ErrorAlert error={types.error} /> : !types.data?.length ? (
          <EmptyState title="No letter types" hint="Create a type (e.g. Offer Letter), then add a template to it." />
        ) : (
          <Table size="small">
            <TableHead><TableRow>
              <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Signatory</TableCell>
              <TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
            </TableRow></TableHead>
            <TableBody>
              {types.data.map((t: LetterType) => (
                <TableRow key={t.id} hover selected={sel === t.id} onClick={() => setSel(t.id)} sx={{ cursor: 'pointer' }}>
                  <TableCell><code>{t.code}</code></TableCell>
                  <TableCell>{t.name}</TableCell>
                  <TableCell>{signatory(t.signatoryEmployeeId)}</TableCell>
                  <TableCell><StatusChip value={t.isActive ? 'active' : 'inactive'} /></TableCell>
                  <TableCell align="right" onClick={(e) => e.stopPropagation()}>
                    <Button size="small" startIcon={<Edit />} onClick={() => setDlg({
                      code: t.code, name: t.name, description: t.description,
                      signatoryEmployeeId: t.signatoryEmployeeId, isActive: t.isActive, rowVersion: t.rowVersion,
                    })}>Edit</Button>
                    <Button size="small" onClick={() => { setSel(t.id); setTpl(emptyTpl(t.id)) }}>New template</Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>

      {sel && (
        <SectionCard title={`Templates — ${types.data?.find((t) => t.id === sel)?.name ?? ''}`}
          actions={<Button size="small" startIcon={<Add />} onClick={() => setTpl(emptyTpl(sel))}>New template</Button>}>
          {templates.isLoading ? <TableSkeleton /> : !templates.data?.length ? (
            <EmptyState title="No templates" hint="Add a template — each save creates a new immutable version." />
          ) : (
            <Table size="small">
              <TableHead><TableRow>
                <TableCell>Name</TableCell><TableCell>Version</TableCell><TableCell>Subject</TableCell><TableCell>Status</TableCell>
              </TableRow></TableHead>
              <TableBody>
                {templates.data.map((t) => (
                  <TableRow key={t.id} hover>
                    <TableCell>{t.name}</TableCell>
                    <TableCell>v{t.version}</TableCell>
                    <TableCell>{t.subject}</TableCell>
                    <TableCell><StatusChip value={t.isActive ? 'active' : 'inactive'} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </SectionCard>
      )}

      {dlg && (
        <Dialog open onClose={() => setDlg(null)} maxWidth="sm" fullWidth>
          <DialogTitle>{dlg.rowVersion != null ? 'Edit letter type' : 'New letter type'}</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: '12px !important' }}>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 2 }}>
              <TF label="Code" value={dlg.code} onChange={f('code')} disabled={dlg.rowVersion != null} />
              <TF label="Name" value={dlg.name} onChange={f('name')} />
            </Box>
            <TF label="Description" value={dlg.description ?? ''} onChange={f('description')} />
            <TF select label="Signatory (employee)" value={dlg.signatoryEmployeeId ?? ''}
              onChange={(e) => setDlg({ ...dlg, signatoryEmployeeId: e.target.value || undefined })}>
              <MenuItem value="">None</MenuItem>
              {employees.data?.map((e) => <MenuItem key={e.id} value={e.id}>{e.displayName} · {e.employeeCode}</MenuItem>)}
            </TF>
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

      {tpl && (
        <Dialog open onClose={() => setTpl(null)} maxWidth="md" fullWidth>
          <DialogTitle>New template (next version)</DialogTitle>
          <DialogContent sx={{ display: 'grid', gap: 2, pt: '12px !important' }}>
            <TF label="Name" value={tpl.name} onChange={(e) => setTpl({ ...tpl, name: e.target.value })} />
            <TF label="Subject" value={tpl.subject} onChange={(e) => setTpl({ ...tpl, subject: e.target.value })} />
            <TF label="Body" multiline minRows={10} value={tpl.body} onChange={(e) => setTpl({ ...tpl, body: e.target.value })} />
            <Typography variant="caption" color="text.secondary">
              Merge fields: {MERGE_FIELDS.map((m) => `{{ ${m} }}`).join('  ')}
            </Typography>
            <FormControlLabel control={<Checkbox checked={tpl.isActive} onChange={(e) => setTpl({ ...tpl, isActive: e.target.checked })} />} label="Active" />
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2 }}>
            <Button onClick={() => setTpl(null)}>Cancel</Button>
            <Button variant="contained" disabled={!tpl.name.trim() || !tpl.body.trim() || saveTpl.isPending} onClick={() => saveTpl.mutate()}>
              {saveTpl.isPending ? 'Saving…' : 'Save template'}
            </Button>
          </DialogActions>
        </Dialog>
      )}
    </>
  )
}

export function HrDocsPage() {
  const [tab, setTab] = useState(0)
  return (
    <Box>
      <PageHeader title="HR documents setup" subtitle="Document categories and letter types & templates." />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2, borderBottom: 1, borderColor: 'divider' }}>
        <Tab label="Document categories" />
        <Tab label="Letter types & templates" />
      </Tabs>
      {tab === 0 ? <CategoriesPanel /> : <LetterTypesPanel />}
    </Box>
  )
}
