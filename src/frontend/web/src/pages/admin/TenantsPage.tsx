import { Box, Button, InputAdornment, Paper, Table, TableBody, TableCell, TableHead, TablePagination, TableRow, TextField } from '@mui/material'
import Search from '@mui/icons-material/Search'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { platformApi, type TenantSummary } from '../../api/platform'
import { CenteredSpinner, EmptyState, ErrorAlert, PageHeader, StatusChip } from '../../components/ui'

export function TenantsPage() {
  const navigate = useNavigate()
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(25)
  const [search, setSearch] = useState('')
  const q = useQuery({
    queryKey: ['tenants', page, pageSize, search],
    queryFn: () => platformApi.tenants(page + 1, pageSize, search || undefined),
  })

  const row = (t: TenantSummary) => (
    <TableRow key={t.id} hover onClick={() => navigate(`/admin/tenants/${t.id}`)} sx={{ cursor: 'pointer' }}>
      <TableCell><strong>{t.code}</strong></TableCell>
      <TableCell>{t.name}</TableCell>
      <TableCell>{t.tenantNumber}</TableCell>
      <TableCell><StatusChip value={t.status} /></TableCell>
      <TableCell>{t.primaryHost ?? '—'}</TableCell>
      <TableCell>{new Date(t.createdAt).toLocaleDateString()}</TableCell>
    </TableRow>
  )

  return (
    <Box>
      <PageHeader
        title="Tenants"
        subtitle="Every tenant runs on its own PostgreSQL database"
        actions={<Button component={Link} to="/admin/tenants/new" variant="contained">Create tenant</Button>}
      />
      <TextField
        size="small" placeholder="Search code or name" value={search}
        onChange={(e) => { setSearch(e.target.value); setPage(0) }}
        sx={{ mb: 2, width: 320 }}
        slotProps={{ input: { startAdornment: <InputAdornment position="start"><Search fontSize="small" /></InputAdornment> } }}
      />
      {q.isPending ? <CenteredSpinner /> : q.error ? <ErrorAlert error={q.error} onRetry={() => void q.refetch()} /> : (
        <Paper variant="outlined">
          {q.data!.items.length === 0 ? <EmptyState title="No tenants found" hint={search ? 'Try a different search.' : 'Create your first tenant.'} /> : (
            <>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>#</TableCell>
                    <TableCell>Status</TableCell><TableCell>Host</TableCell><TableCell>Created</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>{q.data!.items.map(row)}</TableBody>
              </Table>
              <TablePagination
                component="div" count={q.data!.totalCount} page={page}
                onPageChange={(_, p) => setPage(p)} rowsPerPage={pageSize}
                onRowsPerPageChange={(e) => { setPageSize(parseInt(e.target.value, 10)); setPage(0) }}
                rowsPerPageOptions={[10, 25, 50, 100]}
              />
            </>
          )}
        </Paper>
      )}
    </Box>
  )
}
