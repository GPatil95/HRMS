import { AddBusiness, Business, Key, MoreVert, OpenInNew, PauseCircle, PlayCircle, Refresh, Search, Sync } from '@mui/icons-material'
import {
  Box,
  Button,
  Card,
  IconButton,
  InputAdornment,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { platformApi, type TenantStatus, type TenantSummary } from '../../api/platform'
import { ReasonDialog } from '../../components/ReasonDialog'
import { ResetAdminPasswordDialog } from '../../components/ResetAdminPasswordDialog'
import { useToast } from '../../components/toast'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton, TenantAvatar } from '../../components/ui'

const STATUS_OPTIONS: TenantStatus[] = ['Active', 'Provisioning', 'ProvisioningFailed', 'Suspended', 'Inactive']
type SortKey = 'name' | 'created' | 'status'

export function TenantsPage() {
  const theme = useTheme()
  const isMobile = useMediaQuery(theme.breakpoints.down('md'))
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToast()
  const [params, setParams] = useSearchParams()
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(25)
  const [search, setSearch] = useState(params.get('q') ?? '')
  const [statusFilter, setStatusFilter] = useState('')
  const [sort, setSort] = useState<SortKey>('created')
  const [menuEl, setMenuEl] = useState<{ el: HTMLElement; t: TenantSummary } | null>(null)
  const [resetPw, setResetPw] = useState<TenantSummary | null>(null)
  const [reasonDlg, setReasonDlg] = useState<null | { action: 'retry' | 'suspend' | 'activate'; t: TenantSummary }>(null)

  const actionMut = useMutation({
    mutationFn: (p: { action: 'retry' | 'suspend' | 'activate'; id: string; reason: string }) =>
      p.action === 'retry' ? platformApi.retryProvisioning(p.id) : platformApi.setTenantStatus(p.id, p.action === 'suspend' ? 'Suspended' : 'Active', p.reason),
    onSuccess: (_, p) => { toast(p.action === 'retry' ? 'Provisioning retry queued' : 'Tenant status updated'); setReasonDlg(null); void qc.invalidateQueries({ queryKey: ['tenants'] }) },
    onError: (e) => toast(e instanceof ApiError ? e.message : 'Action failed', 'error'),
  })

  useEffect(() => {
    const qq = params.get('q')
    if (qq !== null && qq !== search) setSearch(qq)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params])

  useEffect(() => {
    const t = setTimeout(() => {
      const q = search.trim()
      if (q !== (params.get('q') ?? '')) {
        setParams(q ? { q } : {}, { replace: true })
        setPage(0)
      }
    }, 350)
    return () => clearTimeout(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search])

  const active = (params.get('q') ?? '').trim()
  // Fetch the full first page and filter/sort client-side — the list stays small.
  const q = useQuery({
    queryKey: ['tenants', active],
    queryFn: () => platformApi.tenants(1, 200, active || undefined),
  })

  const filtered = useMemo(() => {
    let items = q.data?.items ?? []
    if (statusFilter) items = items.filter((t) => t.status === statusFilter)
    const sorted = [...items].sort((a, b) =>
      sort === 'name' ? a.name.localeCompare(b.name)
        : sort === 'status' ? a.status.localeCompare(b.status)
          : new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    return sorted
  }, [q.data, statusFilter, sort])
  const paged = filtered.slice(page * pageSize, page * pageSize + pageSize)

  const tenantUrl = (t: TenantSummary) => t.primaryHost ? `${window.location.protocol}//${t.primaryHost}${window.location.port ? `:${window.location.port}` : ''}` : null
  const rowActions = (t: TenantSummary) => [
    t.primaryHost && t.status === 'Active' && { key: 'open', label: 'Open tenant', icon: <OpenInNew fontSize="small" />, run: () => window.open(tenantUrl(t)!, '_blank') },
    (t.status === 'ProvisioningFailed') && { key: 'retry', label: 'Retry provisioning', icon: <Sync fontSize="small" />, run: () => setReasonDlg({ action: 'retry', t }) },
    { key: 'reset', label: 'Reset admin password', icon: <Key fontSize="small" />, run: () => setResetPw(t) },
    t.status === 'Active' && { key: 'suspend', label: 'Suspend', icon: <PauseCircle fontSize="small" />, run: () => setReasonDlg({ action: 'suspend', t }) },
    (t.status === 'Suspended' || t.status === 'Inactive') && { key: 'activate', label: 'Activate', icon: <PlayCircle fontSize="small" />, run: () => setReasonDlg({ action: 'activate', t }) },
  ].filter(Boolean) as { key: string; label: string; icon: React.ReactNode; run: () => void }[]

  const tenantInfo = (t: TenantSummary) => (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 0 }}>
      <TenantAvatar code={t.code} name={t.name} size={32} />
      <Box sx={{ minWidth: 0 }}>
        <Typography variant="body2" sx={{ fontWeight: 650 }}>{t.name}</Typography>
        <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{t.code}</Typography>
      </Box>
    </Box>
  )

  const menuAction = (t: TenantSummary) => (e: React.MouseEvent<HTMLElement>) => { e.stopPropagation(); setMenuEl({ el: e.currentTarget, t }) }

  return (
    <Box>
      <PageHeader
        title="Tenants"
        subtitle="Every tenant runs on its own PostgreSQL database"
        actions={<Button component={Link} to="/admin/tenants/new" variant="contained" startIcon={<AddBusiness />}>Create tenant</Button>}
      />
      <SectionCard>
        {/* Toolbar */}
        <Box sx={{ px: 2.5, py: 2, borderBottom: 1, borderColor: 'divider', display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
          <TextField
            size="small" placeholder="Search code or name" value={search}
            onChange={(e) => setSearch(e.target.value)}
            sx={{ width: { xs: '100%', sm: 300 } }}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><Search fontSize="small" /></InputAdornment> } }}
          />
          <Select size="small" displayEmpty value={statusFilter} onChange={(e) => { setStatusFilter(e.target.value); setPage(0) }} sx={{ minWidth: 160 }}
            renderValue={(v) => v || 'All statuses'}>
            <MenuItem value="">All statuses</MenuItem>
            {STATUS_OPTIONS.map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
          </Select>
          <Select size="small" value={sort} onChange={(e) => setSort(e.target.value as SortKey)} sx={{ minWidth: 140 }}>
            <MenuItem value="created">Newest first</MenuItem>
            <MenuItem value="name">Name A–Z</MenuItem>
            <MenuItem value="status">By status</MenuItem>
          </Select>
          <Box sx={{ flex: 1 }} />
          <IconButton aria-label="Refresh" onClick={() => void q.refetch()}><Refresh fontSize="small" /></IconButton>
        </Box>

        {q.isPending ? <TableSkeleton columns={6} /> : q.error ? <Box sx={{ p: 3 }}><ErrorAlert error={q.error} onRetry={() => void q.refetch()} /></Box> : (
          <>
            {paged.length === 0 ? (
              <EmptyState
                title={active || statusFilter ? 'No tenants match the filters' : 'No tenants yet'}
                hint={active || statusFilter ? 'Try a different search or status.' : 'Create your first tenant to get started.'}
                icon={<Business />}
                action={!active && !statusFilter && <Button component={Link} to="/admin/tenants/new" variant="contained" size="small">Create tenant</Button>}
              />
            ) : isMobile ? (
              <Box sx={{ display: 'grid', gap: 1.5, p: 2 }}>
                {paged.map((t) => (
                  <Card key={t.id} variant="outlined" sx={{ p: 2, cursor: 'pointer' }} onClick={() => navigate(`/admin/tenants/${t.id}`)}>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 1 }}>
                      {tenantInfo(t)}
                      <IconButton size="small" aria-label="Actions" onClick={menuAction(t)}><MoreVert fontSize="small" /></IconButton>
                    </Box>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mt: 1.5 }}>
                      <StatusChip value={t.status} />
                      <Typography variant="caption" color="text.secondary">{new Date(t.createdAt).toLocaleDateString()}</Typography>
                    </Box>
                  </Card>
                ))}
              </Box>
            ) : (
              <Box sx={{ overflowX: 'auto' }}>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Tenant</TableCell><TableCell>#</TableCell><TableCell>Status</TableCell>
                      <TableCell>Host</TableCell><TableCell>Created</TableCell><TableCell align="right">Actions</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {paged.map((t) => (
                      <TableRow key={t.id} hover onClick={() => navigate(`/admin/tenants/${t.id}`)} sx={{ cursor: 'pointer' }}>
                        <TableCell>{tenantInfo(t)}</TableCell>
                        <TableCell>{t.tenantNumber}</TableCell>
                        <TableCell><StatusChip value={t.status} /></TableCell>
                        <TableCell><Typography variant="body2" color="text.secondary" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{t.primaryHost ?? '—'}</Typography></TableCell>
                        <TableCell><Typography variant="caption" color="text.secondary">{new Date(t.createdAt).toLocaleDateString()}</Typography></TableCell>
                        <TableCell align="right">
                          <IconButton size="small" aria-label="Actions" onClick={menuAction(t)}><MoreVert fontSize="small" /></IconButton>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Box>
            )}
            <TablePagination
              component="div" count={filtered.length} page={page}
              onPageChange={(_, p) => setPage(p)} rowsPerPage={pageSize}
              onRowsPerPageChange={(e) => { setPageSize(parseInt(e.target.value, 10)); setPage(0) }}
              rowsPerPageOptions={[10, 25, 50, 100]}
              sx={{ borderTop: 1, borderColor: 'divider' }}
            />
          </>
        )}
      </SectionCard>

      <Menu anchorEl={menuEl?.el} open={!!menuEl} onClose={() => setMenuEl(null)}>
        {menuEl && rowActions(menuEl.t).map((a) => (
          <MenuItem key={a.key} onClick={() => { setMenuEl(null); a.run() }}>
            <ListItemIcon>{a.icon}</ListItemIcon><ListItemText>{a.label}</ListItemText>
          </MenuItem>
        ))}
        {menuEl && (
          <MenuItem component={Link} to={`/admin/tenants/${menuEl.t.id}`} onClick={() => setMenuEl(null)}>
            <ListItemIcon><OpenInNew fontSize="small" /></ListItemIcon><ListItemText>View details</ListItemText>
          </MenuItem>
        )}
      </Menu>

      <ResetAdminPasswordDialog open={!!resetPw} tenantId={resetPw?.id ?? ''} adminEmail={null} onClose={() => setResetPw(null)} />
      <ReasonDialog
        open={!!reasonDlg}
        title={reasonDlg?.action === 'retry' ? 'Retry provisioning' : reasonDlg?.action === 'suspend' ? `Suspend ${reasonDlg.t.code}` : `Activate ${reasonDlg?.t.code}`}
        danger={reasonDlg?.action === 'suspend'}
        busy={actionMut.isPending}
        description={reasonDlg?.action === 'retry' ? 'Re-runs the idempotent provisioning pipeline for this tenant.'
          : reasonDlg?.action === 'suspend' ? 'Suspending blocks every user in this tenant from signing in.'
            : 'Activating restores tenant access.'}
        confirmLabel={reasonDlg?.action === 'retry' ? 'Retry' : reasonDlg?.action === 'suspend' ? 'Suspend' : 'Activate'}
        onClose={() => setReasonDlg(null)}
        onConfirm={(r) => reasonDlg && actionMut.mutate({ action: reasonDlg.action, id: reasonDlg.t.id, reason: r })} />
    </Box>
  )
}
