import { useState, type ReactNode } from 'react'
import {
  Box, Chip, List, ListItemButton, ListItemText, Typography,
} from '@mui/material'
import { AssessmentOutlined } from '@mui/icons-material'
import { useQuery } from '@tanstack/react-query'
import { tenantApi } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, TableSkeleton } from '../../components/ui'
import { DataList } from '../../components/tenant'

export function ReportsPage() {
  const reports = useQuery({ queryKey: ['reports'], queryFn: tenantApi.reports })
  const [code, setCode] = useState<string | null>(null)
  const result = useQuery({ queryKey: ['reports', code], queryFn: () => tenantApi.report(code!), enabled: !!code })

  return (
    <Box>
      <PageHeader title="Reports" subtitle="View the reports available to your role." />
      {reports.error && <ErrorAlert error={reports.error} onRetry={() => reports.refetch()} />}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '280px 1fr' }, gap: 2 }}>
        <SectionCard title="Available reports">
          {reports.isPending ? <TableSkeleton columns={1} rows={4} /> : (reports.data ?? []).length === 0 ? (
            <EmptyState title="No reports" hint="No reports are available for your permissions." />
          ) : (
            <List dense disablePadding>
              {(reports.data ?? []).map((r) => (
                <ListItemButton key={r.code} selected={code === r.code} onClick={() => setCode(r.code)} sx={{ py: 1.5 }}>
                  <ListItemText
                    primary={r.title}
                    secondary={<Chip label={r.moduleCode} size="small" variant="outlined" sx={{ mt: 0.5, height: 20, fontSize: 11 }} />}
                    slotProps={{ primary: { variant: 'body2', sx: { fontWeight: code === r.code ? 650 : 500 } } }}
                  />
                </ListItemButton>
              ))}
            </List>
          )}
        </SectionCard>
        <SectionCard
          title={code ? (reports.data ?? []).find((r) => r.code === code)?.title ?? 'Report' : 'Results'}
          actions={code && <Chip size="small" label={code} variant="outlined" />}>
          {!code ? (
            <EmptyState title="Select a report" hint="Choose a report on the left to see results." icon={<AssessmentOutlined />} />
          ) : result.isPending ? <TableSkeleton columns={4} /> : result.error ? (
            <Box sx={{ p: 2 }}><ErrorAlert error={result.error} onRetry={() => result.refetch()} /></Box>
          ) : result.data ? (
            <>
              <Box sx={{ px: 3, pb: 1 }}>
                <Typography variant="caption" color="text.secondary">{result.data.totalCount} rows</Typography>
              </Box>
              {result.data.rows.length === 0 ? (
                <EmptyState title="No rows" hint="The report returned no data." />
              ) : (
                <Box sx={{ px: 2, pb: 2 }}>
                  <DataList
                    columns={result.data.columns}
                    rows={result.data.rows.map((cells, i) => ({ key: i, cells }))}
                    rowKey={(r) => r.key}
                    renderRow={(r) => r.cells as ReactNode[]}
                    pageSize={25}
                  />
                </Box>
              )}
            </>
          ) : null}
        </SectionCard>
      </Box>
    </Box>
  )
}
