import { useMemo, useState, type ReactNode } from 'react'
import { Box, TextField, Typography } from '@mui/material'
import { History, Search } from '@mui/icons-material'
import { useQuery } from '@tanstack/react-query'
import { tenantApi } from '../../api/tenant'
import { ComingSoonPage, EmptyState, ErrorAlert, PageHeader, TableSkeleton } from '../../components/ui'
import { DataList } from '../../components/tenant'

export function AuditPage() {
  const report = useQuery({ queryKey: ['reports', 'audit-trail'], queryFn: () => tenantApi.report('audit-trail') })
  const [query, setQuery] = useState('')
  const rows = useMemo(() => {
    const all = (report.data?.rows ?? []).map((cells, i) => ({ key: i, cells }))
    if (!query) return all
    const q = query.toLowerCase()
    return all.filter((r) => r.cells.some((c) => String(c ?? '').toLowerCase().includes(q)))
  }, [report.data, query])

  return (
    <Box>
      <PageHeader title="Audit Log" subtitle="Every recorded tenant action, newest first." />
      {report.data && (
        <Box sx={{ mb: 2, display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
          <TextField
            size="small" placeholder="Search audit entries" value={query}
            onChange={(e) => setQuery(e.target.value)}
            slotProps={{ input: { startAdornment: <Search fontSize="small" sx={{ mr: 1, color: 'text.disabled' }} /> } }}
            sx={{ flexGrow: 1, maxWidth: { sm: 360 } }}
          />
          <Typography variant="caption" color="text.secondary">
            {query ? `${rows.length} of ` : ''}{report.data.totalCount} entries
          </Typography>
        </Box>
      )}
      {report.isPending ? <TableSkeleton columns={5} /> : report.error ? (
        <ErrorAlert error={report.error} onRetry={() => report.refetch()} />
      ) : rows.length === 0 ? (
        <EmptyState
          title={query ? 'No matching entries' : 'No audit entries'}
          hint={query ? 'Try a different search.' : 'Tenant activity will appear here as it is recorded.'}
          icon={<History />}
        />
      ) : (
        <DataList
          columns={report.data!.columns}
          rows={rows}
          rowKey={(r) => r.key}
          renderRow={(r) => r.cells as ReactNode[]}
          pageSize={25}
        />
      )}
    </Box>
  )
}

export function SecurityEventsPage() {
  return (
    <ComingSoonPage
      title="Security Events"
      description="The tenant security-events query API isn't available yet. Sign-in and permission events are being recorded; a dedicated endpoint and this view will arrive in a later milestone."
    />
  )
}
export function TemplatesPage() {
  return (
    <ComingSoonPage
      title="Notification Templates"
      description="The tenant template-management API isn't available yet. System templates are being used for emails; editing will arrive in a later milestone."
    />
  )
}
