import { Box, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { tenantApi } from '../../api/tenant'
import { ComingSoonPage, EmptyState, ErrorAlert, PageHeader, SectionCard, TableSkeleton } from '../../components/ui'

export function AuditPage() {
  const report = useQuery({ queryKey: ['reports', 'audit-trail'], queryFn: () => tenantApi.report('audit-trail') })
  return (
    <Box>
      <PageHeader title="Audit Log" subtitle="Tenant audit trail (report view)" />
      <SectionCard>
        {report.isPending ? <TableSkeleton columns={5} /> : report.error ? (
          <Box sx={{ p: 2 }}><ErrorAlert error={report.error} onRetry={() => report.refetch()} /></Box>
        ) : report.data && report.data.rows.length > 0 ? (
          <>
            <Box sx={{ px: 3, py: 1.5 }}>
              <Typography variant="caption" color="text.secondary">{report.data.totalCount} entries</Typography>
            </Box>
            <Box sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead>
                  <TableRow>{report.data.columns.map((c) => <TableCell key={c}><Typography variant="caption" sx={{ fontWeight: 700 }}>{c}</Typography></TableCell>)}</TableRow>
                </TableHead>
                <TableBody>
                  {report.data.rows.map((row, i) => (
                    <TableRow key={i} hover>{row.map((cell, j) => <TableCell key={j}><Typography variant="body2">{cell ?? '—'}</Typography></TableCell>)}</TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          </>
        ) : (
          <EmptyState title="No audit entries" hint="Tenant activity will appear here as it is recorded." />
        )}
      </SectionCard>
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
