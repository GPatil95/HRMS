import { useMemo, useState } from 'react'
import {
  Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle,
  IconButton, MenuItem, Tab, Tabs, TextField, Tooltip,
} from '@mui/material'
import { Add, Delete, EditOutlined, PowerSettingsNew } from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  mastersApi, tenantApi,
  type CostCentre, type CostCentreInput, type Designation, type DesignationInput,
  type Grade, type GradeInput, type WorkLocation, type WorkLocationInput,
} from '../../api/tenant'
import { ApiError } from '../../api/client'
import { EmptyState, ErrorAlert, PageHeader, StatusChip, TableSkeleton } from '../../components/ui'
import { DataList } from '../../components/tenant'
import { useToast } from '../../components/toast'
import { usePermissions } from './OrgShared'

type Kind = 'designations' | 'grades' | 'locations' | 'costCentres'

interface Row { id: string; code: string; name: string; extra?: string; isActive: boolean; rowVersion: number; raw: unknown }

const KINDS: { key: Kind; label: string; hint: string }[] = [
  { key: 'designations', label: 'Designations', hint: 'Job titles assigned to employees' },
  { key: 'grades', label: 'Grades', hint: 'Level bands used by payroll and reporting' },
  { key: 'locations', label: 'Locations', hint: 'Offices, floors, desk zones and client sites' },
  { key: 'costCentres', label: 'Cost Centres', hint: 'Cost-allocation buckets for employees' },
]

function useRows(kind: Kind) {
  const designations = useQuery({ queryKey: ['org', 'masters', 'designations'], queryFn: () => mastersApi.designations(), enabled: kind === 'designations' })
  const grades = useQuery({ queryKey: ['org', 'masters', 'grades'], queryFn: () => mastersApi.grades(), enabled: kind === 'grades' })
  const locations = useQuery({ queryKey: ['org', 'masters', 'locations'], queryFn: () => mastersApi.locations(), enabled: kind === 'locations' })
  const costCentres = useQuery({ queryKey: ['org', 'masters', 'costCentres'], queryFn: () => mastersApi.costCentres(), enabled: kind === 'costCentres' })

  const rows: Row[] = useMemo(() => {
    if (kind === 'designations') return (designations.data ?? []).map((d: Designation) => ({ id: d.id, code: d.code, name: d.name, extra: d.level != null ? `L${d.level}` : undefined, isActive: d.isActive, rowVersion: d.rowVersion, raw: d }))
    if (kind === 'grades') return (grades.data ?? []).map((g: Grade) => ({ id: g.id, code: g.code, name: g.name, extra: `#${g.sortOrder}`, isActive: g.isActive, rowVersion: g.rowVersion, raw: g }))
    if (kind === 'locations') return (locations.data ?? []).map((l: WorkLocation) => ({ id: l.id, code: l.code, name: l.name, extra: [l.branchName, l.locationType].filter(Boolean).join(' · ') || undefined, isActive: l.isActive, rowVersion: l.rowVersion, raw: l }))
    return (costCentres.data ?? []).map((c: CostCentre) => ({ id: c.id, code: c.code, name: c.name, extra: c.companyName ?? undefined, isActive: c.isActive, rowVersion: c.rowVersion, raw: c }))
  }, [kind, designations.data, grades.data, locations.data, costCentres.data])

  const pending = kind === 'designations' ? designations.isPending : kind === 'grades' ? grades.isPending : kind === 'locations' ? locations.isPending : costCentres.isPending
  const error = designations.error ?? grades.error ?? locations.error ?? costCentres.error
  return { rows, pending, error }
}

function MasterDialog({ kind, editing, onClose }: { kind: Kind; editing: Row | null; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const companies = useQuery({ queryKey: ['org', 'companies'], queryFn: tenantApi.companies, enabled: kind === 'costCentres' })
  const branches = useQuery({ queryKey: ['org', 'branches'], queryFn: tenantApi.branches, enabled: kind === 'locations' })

  const raw = editing?.raw as (Designation & Grade & WorkLocation & CostCentre) | undefined
  const [form, setForm] = useState({
    code: raw?.code ?? '', name: raw?.name ?? '', level: raw?.level != null ? String(raw.level) : '',
    sortOrder: raw?.sortOrder != null ? String(raw.sortOrder) : '0',
    branchId: raw?.branchId ?? '', companyId: raw?.companyId ?? '',
    locationType: raw?.locationType ?? '', description: raw?.description ?? '',
  })
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value })

  const mut = useMutation({
    mutationFn: async () => {
      const rv = editing?.rowVersion
      if (kind === 'designations') {
        const b: DesignationInput = { code: form.code, name: form.name, level: form.level ? Number(form.level) : null, description: form.description || null, rowVersion: rv }
        return editing ? mastersApi.updateDesignation(editing.id, b) : mastersApi.createDesignation(b)
      }
      if (kind === 'grades') {
        const b: GradeInput = { code: form.code, name: form.name, sortOrder: Number(form.sortOrder) || 0, description: form.description || null, rowVersion: rv }
        return editing ? mastersApi.updateGrade(editing.id, b) : mastersApi.createGrade(b)
      }
      if (kind === 'locations') {
        const b: WorkLocationInput = { code: form.code, name: form.name, branchId: form.branchId || null, locationType: form.locationType || null, description: form.description || null, rowVersion: rv }
        return editing ? mastersApi.updateLocation(editing.id, b) : mastersApi.createLocation(b)
      }
      const b: CostCentreInput = { code: form.code, name: form.name, companyId: form.companyId || null, description: form.description || null, rowVersion: rv }
      return editing ? mastersApi.updateCostCentre(editing.id, b) : mastersApi.createCostCentre(b)
    },
    onSuccess: () => {
      toast(editing ? 'Updated' : 'Created')
      void qc.invalidateQueries({ queryKey: ['org', 'masters'] })
      onClose()
    },
    onError: (e) => {
      const d = e instanceof ApiError ? e.details : undefined
      if (d && typeof d === 'object') setFieldErrors(d as Record<string, string>)
      toast(e instanceof Error ? e.message : 'Save failed', 'error')
    },
  })

  const title = `${editing ? 'Edit' : 'New'} ${KINDS.find(k => k.key === kind)?.label.replace(/s$/, '')}`
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: '12px !important' }}>
        <TextField label="Code" value={form.code} onChange={set('code')} required disabled={!!editing}
          error={!!fieldErrors.code} helperText={fieldErrors.code ?? 'Unique, e.g. SE-2'} slotProps={{ inputLabel: { shrink: true } }} />
        <TextField label="Name" value={form.name} onChange={set('name')} required
          error={!!fieldErrors.name} helperText={fieldErrors.name} slotProps={{ inputLabel: { shrink: true } }} />
        {kind === 'designations' && (
          <TextField label="Level" type="number" value={form.level} onChange={set('level')} slotProps={{ inputLabel: { shrink: true } }} />
        )}
        {kind === 'grades' && (
          <TextField label="Sort order" type="number" value={form.sortOrder} onChange={set('sortOrder')} required slotProps={{ inputLabel: { shrink: true } }} />
        )}
        {kind === 'locations' && (
          <>
            <TextField select label="Branch" value={form.branchId} onChange={set('branchId')} slotProps={{ inputLabel: { shrink: true } }}
              error={!!fieldErrors.branchId} helperText={fieldErrors.branchId}>
              <MenuItem value="">— None —</MenuItem>
              {(branches.data ?? []).map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
            </TextField>
            <TextField select label="Location type" value={form.locationType} onChange={set('locationType')} slotProps={{ inputLabel: { shrink: true } }}
              error={!!fieldErrors.locationType} helperText={fieldErrors.locationType}>
              <MenuItem value="">— None —</MenuItem>
              {['office', 'floor', 'desk_zone', 'client_site', 'remote', 'other'].map((t) => <MenuItem key={t} value={t}>{t.replace('_', ' ')}</MenuItem>)}
            </TextField>
          </>
        )}
        {kind === 'costCentres' && (
          <TextField select label="Company" value={form.companyId} onChange={set('companyId')} slotProps={{ inputLabel: { shrink: true } }}
            error={!!fieldErrors.companyId} helperText={fieldErrors.companyId}>
            <MenuItem value="">— Tenant-wide —</MenuItem>
            {(companies.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
          </TextField>
        )}
        <TextField label="Description" value={form.description} onChange={set('description')} multiline minRows={2} slotProps={{ inputLabel: { shrink: true } }} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!form.code.trim() || !form.name.trim() || mut.isPending} onClick={() => mut.mutate()}>
          {mut.isPending ? 'Saving…' : editing ? 'Save changes' : 'Create'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

export function OrgMastersPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const perms = usePermissions()
  const canEdit = perms.has('org.master.edit')
  const canCreate = perms.has('org.master.create') || canEdit

  const [kind, setKind] = useState<Kind>('designations')
  const { rows, pending, error } = useRows(kind)
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<{ editing: Row | null } | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<Row | null>(null)
  const [pageError, setPageError] = useState<string | null>(null)

  const filtered = useMemo(() => {
    const s = search.trim().toLowerCase()
    return s ? rows.filter((r) => r.code.toLowerCase().includes(s) || r.name.toLowerCase().includes(s) || (r.extra ?? '').toLowerCase().includes(s)) : rows
  }, [rows, search])

  const statusMut = useMutation({
    mutationFn: async (r: Row) => {
      if (kind === 'designations') return mastersApi.setDesignationStatus(r.id, !r.isActive)
      if (kind === 'grades') return mastersApi.setGradeStatus(r.id, !r.isActive)
      if (kind === 'locations') return mastersApi.setLocationStatus(r.id, !r.isActive)
      return mastersApi.setCostCentreStatus(r.id, !r.isActive)
    },
    onSuccess: (_, r) => { toast(r.isActive ? 'Deactivated' : 'Activated'); void qc.invalidateQueries({ queryKey: ['org', 'masters'] }) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })

  const deleteMut = useMutation({
    mutationFn: async (r: Row) => {
      if (kind === 'designations') return mastersApi.deleteDesignation(r.id)
      if (kind === 'grades') return mastersApi.deleteGrade(r.id)
      if (kind === 'locations') return mastersApi.deleteLocation(r.id)
      return mastersApi.deleteCostCentre(r.id)
    },
    onSuccess: () => { toast('Deleted'); setConfirmDelete(null); void qc.invalidateQueries({ queryKey: ['org', 'masters'] }) },
    onError: (e) => { setPageError(e instanceof Error ? e.message : 'Delete failed'); setConfirmDelete(null) },
  })

  const label = KINDS.find((k) => k.key === kind)!
  return (
    <Box>
      <PageHeader
        title="Organization Masters"
        subtitle={label.hint}
        actions={canCreate ? <Button variant="contained" startIcon={<Add />} onClick={() => setDialog({ editing: null })}>New {label.label.replace(/s$/, '')}</Button> : undefined}
      />
      <Tabs value={kind} onChange={(_, v: Kind) => { setKind(v); setSearch('') }} sx={{ mb: 2 }} variant="scrollable">
        {KINDS.map((k) => <Tab key={k.key} value={k.key} label={k.label} />)}
      </Tabs>
      <TextField size="small" placeholder={`Search ${label.label.toLowerCase()}…`} value={search}
        onChange={(e) => setSearch(e.target.value)} sx={{ mb: 2, width: { xs: '100%', sm: 320 } }} />
      {pageError && <Alert severity="warning" onClose={() => setPageError(null)} sx={{ mb: 2 }}>{pageError}</Alert>}
      {error && <ErrorAlert error={error} />}
      {pending ? <TableSkeleton /> : filtered.length === 0 ? (
        <EmptyState title={search ? 'No matches' : `No ${label.label.toLowerCase()} yet`}
          body={search ? 'Try a different search.' : 'Create the first entry to make it available for employees.'} />
      ) : (
        <DataList<Row>
          columns={['Code', 'Name', 'Details', 'Status', '']}
          rows={filtered}
          rowKey={(r) => r.id}
          renderRow={(r) => [
            r.code,
            r.name,
            r.extra ?? '—',
            <StatusChip key="s" status={r.isActive ? 'active' : 'inactive'} />,
            <Box key="a" sx={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
              {canEdit && (
                <>
                  <Tooltip title="Edit"><IconButton size="small" onClick={(e) => { e.stopPropagation(); setDialog({ editing: r }) }}><EditOutlined fontSize="small" /></IconButton></Tooltip>
                  <Tooltip title={r.isActive ? 'Deactivate' : 'Activate'}>
                    <IconButton size="small" disabled={statusMut.isPending} onClick={(e) => { e.stopPropagation(); statusMut.mutate(r) }}><PowerSettingsNew fontSize="small" /></IconButton>
                  </Tooltip>
                  <Tooltip title="Delete"><IconButton size="small" onClick={(e) => { e.stopPropagation(); setConfirmDelete(r) }}><Delete fontSize="small" /></IconButton></Tooltip>
                </>
              )}
            </Box>,
          ]}
        />
      )}

      {dialog && <MasterDialog kind={kind} editing={dialog.editing} onClose={() => setDialog(null)} />}

      <Dialog open={!!confirmDelete} onClose={() => setConfirmDelete(null)}>
        <DialogTitle>Delete {confirmDelete?.code}?</DialogTitle>
        <DialogContent>Referenced records cannot be deleted — deactivate instead. Delete only unreferenced entries.</DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmDelete(null)}>Cancel</Button>
          <Button color="error" variant="contained" disabled={deleteMut.isPending} onClick={() => confirmDelete && deleteMut.mutate(confirmDelete)}>
            {deleteMut.isPending ? 'Deleting…' : 'Delete'}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}
