import { useState } from 'react'
import {
  Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, IconButton, MenuItem, Tab, Table, TableBody, TableCell,
  TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { Add, Delete, Edit } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  tenantApi, type ComponentInput, type SalaryComponent, type SalaryStructure,
  type StructureInput, type StructureLineInput,
} from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'
import { useToast } from '../../components/toast'

const COMP_TYPES = ['earning', 'deduction', 'benefit', 'statutory', 'reimbursement']
const CALC_TYPES = ['fixed', 'percent_of_basic', 'percent_of_gross', 'formula']

const TF = (p: React.ComponentProps<typeof TextField>) => (
  <TextField size="small" fullWidth slotProps={{ inputLabel: { shrink: true } }} {...p} />
)

const emptyComp = (): ComponentInput => ({
  code: '', name: '', type: 'earning', taxable: true, proRata: true,
  partOfCtc: true, partOfGross: true, isBasic: false, displayOrder: 0, isActive: true,
})

function ComponentsPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const q = useQuery({ queryKey: ['salary-components'], queryFn: tenantApi.salaryComponents })
  const [dlg, setDlg] = useState<ComponentInput | null>(null)
  const f = (k: keyof ComponentInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDlg((d) => d && { ...d, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value })
  const save = useMutation({
    mutationFn: () => tenantApi.upsertComponent(dlg!),
    onSuccess: () => { toast('Component saved'); void qc.invalidateQueries({ queryKey: ['salary-components'] }); setDlg(null) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  const del = useMutation({
    mutationFn: (id: string) => tenantApi.deleteComponent(id),
    onSuccess: () => { toast('Component deleted'); void qc.invalidateQueries({ queryKey: ['salary-components'] }) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  if (q.isPending) return <TableSkeleton columns={5} />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => q.refetch()} />
  const rows = [...(q.data ?? [])].sort((a, b) => a.displayOrder - b.displayOrder)
  return (
    <SectionCard title="Salary components" actions={<Button size="small" startIcon={<Add />} onClick={() => setDlg(emptyComp())}>Add</Button>}>
      <Table size="small">
        <TableHead><TableRow>
          <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Type</TableCell>
          <TableCell>Flags</TableCell><TableCell>Order</TableCell><TableCell>Status</TableCell><TableCell />
        </TableRow></TableHead>
        <TableBody>
          {rows.map((c: SalaryComponent) => (
            <TableRow key={c.id}>
              <TableCell sx={{ fontFamily: 'monospace' }}>{c.code}</TableCell>
              <TableCell>{c.name}</TableCell>
              <TableCell><StatusChip value={c.type} /></TableCell>
              <TableCell>
                {[c.isBasic && 'basic', c.isStatutory && 'statutory', c.proRata && 'pro-rata', c.taxable && 'taxable']
                  .filter(Boolean).join(' · ') || '—'}
              </TableCell>
              <TableCell>{c.displayOrder}</TableCell>
              <TableCell><StatusChip value={c.isActive ? 'active' : 'inactive'} /></TableCell>
              <TableCell align="right">
                <IconButton size="small" onClick={() => setDlg({ ...c })}><Edit fontSize="small" /></IconButton>
                {!c.isStatutory && <IconButton size="small" onClick={() => del.mutate(c.id)}><Delete fontSize="small" /></IconButton>}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {dlg && (
        <Dialog open onClose={() => setDlg(null)} fullWidth maxWidth="sm">
          <DialogTitle>{dlg.code ? `Component ${dlg.code}` : 'New component'}</DialogTitle>
          <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
            <TF label="Code" value={dlg.code} onChange={f('code')} required disabled={!!(dlg as { id?: string }).id} />
            <TF label="Name" value={dlg.name} onChange={f('name')} required />
            <TF select label="Type" value={dlg.type} onChange={f('type')}>
              {COMP_TYPES.map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}
            </TF>
            <TF label="Payslip name" value={dlg.payslipName ?? ''} onChange={f('payslipName')} />
            <TF type="number" label="Display order" value={dlg.displayOrder} onChange={f('displayOrder')} />
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
              <FormControlLabel control={<Checkbox checked={dlg.taxable} onChange={f('taxable')} />} label="Taxable" />
              <FormControlLabel control={<Checkbox checked={dlg.proRata} onChange={f('proRata')} />} label="Pro-rata" />
              <FormControlLabel control={<Checkbox checked={dlg.partOfCtc} onChange={f('partOfCtc')} />} label="Part of CTC" />
              <FormControlLabel control={<Checkbox checked={dlg.partOfGross} onChange={f('partOfGross')} />} label="Part of gross" />
              <FormControlLabel control={<Checkbox checked={dlg.isBasic} onChange={f('isBasic')} />} label="Is basic" />
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

const emptyLine = (seq: number): StructureLineInput => ({ salaryComponentId: '', sequence: seq, calcType: 'fixed' })

function StructuresPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const q = useQuery({ queryKey: ['salary-structures'], queryFn: tenantApi.salaryStructures })
  const comps = useQuery({ queryKey: ['salary-components'], queryFn: tenantApi.salaryComponents })
  const [dlg, setDlg] = useState<StructureInput | null>(null)
  const setLine = (i: number, patch: Partial<StructureLineInput>) =>
    setDlg((d) => d && { ...d, lines: d.lines.map((l, j) => j === i ? { ...l, ...patch } : l) })
  const save = useMutation({
    mutationFn: () => tenantApi.upsertStructure(dlg!),
    onSuccess: () => { toast('Structure saved'); void qc.invalidateQueries({ queryKey: ['salary-structures'] }); setDlg(null) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  if (q.isPending) return <TableSkeleton columns={4} />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => q.refetch()} />
  const activeComps = (comps.data ?? []).filter((c) => c.isActive)
  return (
    <SectionCard title="Salary structures" actions={
      <Button size="small" startIcon={<Add />} onClick={() => setDlg({ code: '', name: '', isActive: true, lines: [emptyLine(1)] })}>Add</Button>
    }>
      {(q.data ?? []).length === 0 ? <EmptyState title="No structures" hint="Define a salary structure by combining components." /> : (
        <Table size="small">
          <TableHead><TableRow>
            <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Components</TableCell><TableCell>Status</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {(q.data ?? []).map((s: SalaryStructure) => (
              <TableRow key={s.id}>
                <TableCell sx={{ fontFamily: 'monospace' }}>{s.code}</TableCell>
                <TableCell>{s.name}</TableCell>
                <TableCell>{s.lines.map((l) => l.componentCode).join(', ')}</TableCell>
                <TableCell><StatusChip value={s.isActive ? 'active' : 'inactive'} /></TableCell>
                <TableCell align="right">
                  <IconButton size="small" onClick={() => setDlg({
                    id: s.id, code: s.code, name: s.name, description: s.description, isActive: s.isActive,
                    rowVersion: s.rowVersion,
                    lines: s.lines.map((l) => ({
                      salaryComponentId: l.salaryComponentId, sequence: l.sequence, calcType: l.calcType,
                      amount: l.amount, percent: l.percent, formula: l.formula,
                    })),
                  })}><Edit fontSize="small" /></IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {dlg && (
        <Dialog open onClose={() => setDlg(null)} fullWidth maxWidth="md">
          <DialogTitle>{dlg.id ? `Structure ${dlg.code}` : 'New structure'}</DialogTitle>
          <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 2 }}>
              <TF label="Code" value={dlg.code} onChange={(e) => setDlg({ ...dlg, code: e.target.value })} required disabled={!!dlg.id} />
              <TF label="Name" value={dlg.name} onChange={(e) => setDlg({ ...dlg, name: e.target.value })} required />
            </Box>
            <TF label="Description" value={dlg.description ?? ''} onChange={(e) => setDlg({ ...dlg, description: e.target.value })} />
            <FormControlLabel control={<Checkbox checked={dlg.isActive} onChange={(e) => setDlg({ ...dlg, isActive: e.target.checked })} />} label="Active" />
            <Typography variant="subtitle2">Components</Typography>
            {dlg.lines.map((l, i) => (
              <Box key={i} sx={{ display: 'grid', gridTemplateColumns: '2fr 1.4fr 1fr 1fr 1.6fr auto', gap: 1, alignItems: 'center' }}>
                <TF select label="Component" value={l.salaryComponentId} onChange={(e) => setLine(i, { salaryComponentId: e.target.value })}>
                  {activeComps.map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
                </TF>
                <TF select label="Calc" value={l.calcType} onChange={(e) => setLine(i, { calcType: e.target.value })}>
                  {CALC_TYPES.map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}
                </TF>
                <TF type="number" label="Amount" value={l.amount ?? ''} onChange={(e) => setLine(i, { amount: e.target.value === '' ? undefined : Number(e.target.value) })} />
                <TF type="number" label="Percent" value={l.percent ?? ''} onChange={(e) => setLine(i, { percent: e.target.value === '' ? undefined : Number(e.target.value) })} />
                <TF label="Formula" value={l.formula ?? ''} onChange={(e) => setLine(i, { formula: e.target.value || undefined })} />
                <IconButton size="small" onClick={() => setDlg({ ...dlg, lines: dlg.lines.filter((_, j) => j !== i) })}><Delete fontSize="small" /></IconButton>
              </Box>
            ))}
            <Button size="small" startIcon={<Add />} onClick={() => setDlg({ ...dlg, lines: [...dlg.lines, emptyLine(dlg.lines.length + 1)] })}>Add line</Button>
          </DialogContent>
          <DialogActions sx={{ px: 3, pb: 2 }}>
            <Button onClick={() => setDlg(null)}>Cancel</Button>
            <Button variant="contained"
              disabled={!dlg.code.trim() || !dlg.name.trim() || dlg.lines.some((l) => !l.salaryComponentId) || save.isPending}
              onClick={() => save.mutate()}>
              {save.isPending ? 'Saving…' : 'Save'}
            </Button>
          </DialogActions>
        </Dialog>
      )}
    </SectionCard>
  )
}

export function SalaryMastersPage() {
  const [tab, setTab] = useState(0)
  return (
    <Box>
      <PageHeader title="Salary setup" subtitle="Pay components and reusable salary structures." />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2, borderBottom: 1, borderColor: 'divider' }}>
        <Tab label="Components" />
        <Tab label="Structures" />
      </Tabs>
      {tab === 0 ? <ComponentsPanel /> : <StructuresPanel />}
    </Box>
  )
}
