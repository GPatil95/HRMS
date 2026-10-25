import { AddBusiness, Business, Search } from '@mui/icons-material'
import {
  Box,
  Button,
  InputAdornment,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { platformApi, type TenantSummary } from '../../api/platform'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton, TenantAvatar } from '../../components/ui'

function TenantRow({ t }: { t: TenantSummary }) {
  const navigate = useNavigate()
  return (
    <TableRow hover onClick={() => navigate(`/admin/tenants/${t.id}`)} sx={{ cursor: 'pointer' }}>
      <TableCell>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <TenantAvatar code={t.code} name={t.name} size={32} />
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="body2" sx={{ fontWeight: 650 }}>{t.name}</Typography>
            <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{t.code}</Typography>
          </Box>
        </Box>
      </TableCell>
      <TableCell>{t.tenantNumber}</TableCell>
      <TableCell><StatusChip value={t.status} /></TableCell>
      <TableCell><Typography variant="body2" color="text.secondary" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{t.primaryHost ?? '—'}</Typography></TableCell>
      <TableCell><Typography variant="caption" color="text.secondary">{new Date(t.createdAt).toLocaleDateString()}</Typography></TableCell>
    </TableRow>
  )
}

export function TenantsPage() {
  const [params, setParams] = useSearchParams()
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(25)
  const [search, setSearch] = useState(params.get('q') ?? '')

  useEffect(() => {
    const q = params.get('q')
    if (q !== null && q !== search) setSearch(q)
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
  const q = useQuery({
    queryKey: ['tenants', page, pageSize, active],
    queryFn: () => platformApi.tenants(page + 1, pageSize, active || undefined),
  })

  return (
    <Box>
      <PageHeader
        title="Tenants"
        subtitle="Every tenant runs on its own PostgreSQL database"
        actions={<Button component={Link} to="/admin/tenants/new" variant="contained" startIcon={<AddBusiness />}>Create tenant</Button>}
      />
      <SectionCard>
        <Box sx={{ px: 2.5, py: 2, borderBottom: 1, borderColor: 'divider' }}>
          <TextField
            size="small" placeholder="Search code or name" value={search}
            onChange={(e) => setSearch(e.target.value)}
            sx={{ width: { xs: '100%', sm: 320 } }}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><Search fontSize="small" /></InputAdornment> } }}
          />
        </Box>
        {q.isPending ? <TableSkeleton columns={5} /> : q.error ? <Box sx={{ p: 3 }}><ErrorAlert error={q.error} onRetry={() => void q.refetch()} /></Box> : (
          <>
            {q.data!.items.length === 0 ? (
              <EmptyState
                title={active ? `No tenants match "${active}"` : 'No tenants yet'}
                hint={active ? 'Try a different search.' : 'Create your first tenant to get started.'}
                icon={<Business />}
                action={!active && <Button component={Link} to="/admin/tenants/new" variant="contained" size="small">Create tenant</Button>}
              />
            ) : (
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Tenant</TableCell><TableCell>#</TableCell><TableCell>Status</TableCell>
                    <TableCell>Host</TableCell><TableCell>Created</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>{q.data!.items.map((t) => <TenantRow key={t.id} t={t} />)}</TableBody>
              </Table>
            )}
            <TablePagination
              component="div" count={q.data!.totalCount} page={page}
              onPageChange={(_, p) => setPage(p)} rowsPerPage={pageSize}
              onRowsPerPageChange={(e) => { setPageSize(parseInt(e.target.value, 10)); setPage(0) }}
              rowsPerPageOptions={[10, 25, 50, 100]}
              sx={{ borderTop: 1, borderColor: 'divider' }}
            />
          </>
        )}
      </SectionCard>
    </Box>
  )
}
