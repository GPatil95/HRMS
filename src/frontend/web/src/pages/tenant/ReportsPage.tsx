import { useState } from 'react'
import {
  Box, Chip, List, ListItemButton, ListItemText, Table, TableBody,
  TableCell, TableHead, TableRow, Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { tenantApi } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, TableSkeleton } from '../../components/ui'

export function ReportsPage() {
  const reports = useQuery({ queryKey: ['reports'], queryFn: tenantApi.reports })
  const [code, setCode] = useState<string | null>(null)
  const result = useQuery({ queryKey: ['reports', code], queryFn: () => tenantApi.report(code!), enabled: !!code })

  return (
    <Box>
      <PageHeader title="Reports" subtitle="Run and export reports for this tenant" />
      {reports.error && <ErrorAlert error={reports.error} onRetry={() => reports.refetch()} />}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '280px 1fr' }, gap: 2 }}>
        <SectionCard title="Available reports">
          {reports.isPending ? <TableSkeleton columns={1} rows={4} /> : (reports.data ?? []).length === 0 ? (
            <EmptyState title="No reports" hint="No reports are available for your permissions." />
          ) : (
            <List dense disablePadding>
              {(reports.data ?? []).map((r) => (
                <ListItemButton key={r.code} selected={code === r.code} onClick={() => setCode(r.code)}>
                  <ListItemText primary={r.title} secondary={r.moduleCode} />
                </ListItemButton>
              ))}
            </List>
          )}
        </SectionCard>
        <SectionCard
          title={code ? (reports.data ?? []).find((r) => r.code === code)?.title ?? 'Report' : 'Results'}
          actions={code && <Chip size="small" label={code} variant="outlined" />}>
          {!code ? (
            <EmptyState title="Select a report" hint="Choose a report on the left to see results." />
          ) : result.isPending ? <TableSkeleton columns={4} /> : result.error ? (
            <Box sx={{ p: 2 }}><ErrorAlert error={result.error} onRetry={() => result.refetch()} /></Box>
          ) : result.data ? (
            <>
              <Box sx={{ px: 3, pb: 1 }}>
                <Typography variant="caption" color="text.secondary">{result.data.totalCount} rows</Typography>
              </Box>
              <Box sx={{ overflowX: 'auto' }}>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      {result.data.columns.map((c) => <TableCell key={c}><Typography variant="caption" sx={{ fontWeight: 700 }}>{c}</Typography></TableCell>)}
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {result.data.rows.map((row, i) => (
                      <TableRow key={i} hover>
                        {row.map((cell, j) => <TableCell key={j}><Typography variant="body2">{cell ?? '—'}</Typography></TableCell>)}
                      </TableRow>
                    ))}
                    {result.data.rows.length === 0 && (
                      <TableRow><TableCell colSpan={result.data.columns.length}><EmptyState title="No rows" hint="The report returned no data." /></TableCell></TableRow>
                    )}
                  </TableBody>
                </Table>
              </Box>
            </>
          ) : null}
        </SectionCard>
      </Box>
      <Box sx={{ mt: 1 }}>
        <Typography variant="caption" color="text.secondary">
          Export is available on reports where you have export permission, via the report code (e.g. <code>GET /api/v1/reports/{`{code}`}/export</code>).
        </Typography>
      </Box>
    </Box>
  )
}
