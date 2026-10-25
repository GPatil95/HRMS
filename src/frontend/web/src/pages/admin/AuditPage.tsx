import { Box, Paper, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { platformApi } from '../../api/platform'
import { CenteredSpinner, EmptyState, ErrorAlert, PageHeader, StatusChip } from '../../components/ui'

export function AuditPage() {
  const [tab, setTab] = useState(0)
  const audit = useQuery({ queryKey: ['audit'], queryFn: platformApi.audit, enabled: tab === 0 })
  const events = useQuery({ queryKey: ['security-events'], queryFn: platformApi.securityEvents, enabled: tab === 1 })
  const q = tab === 0 ? audit : events

  return (
    <Box>
      <PageHeader title="Audit logs" subtitle="Append-only platform audit trail and security events (latest 100)" />
      <Tabs value={tab} onChange={(_, v) => setTab(v as number)} sx={{ mb: 2 }}>
        <Tab label="Audit trail" /><Tab label="Security events" />
      </Tabs>
      {q.isPending ? <CenteredSpinner /> : q.error ? <ErrorAlert error={q.error} onRetry={() => void q.refetch()} /> : tab === 0 ? (
        (audit.data ?? []).length === 0 ? <EmptyState title="No audit entries" /> : (
          <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
            <Table size="small">
              <TableHead>
                <TableRow><TableCell>When</TableCell><TableCell>Module</TableCell><TableCell>Action</TableCell><TableCell>Entity</TableCell><TableCell>Actor</TableCell><TableCell>Source</TableCell></TableRow>
              </TableHead>
              <TableBody>
                {audit.data!.map((a, i) => (
                  <TableRow key={i}>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>{new Date(a.occurredAt).toLocaleString()}</TableCell>
                    <TableCell><code>{a.moduleCode}</code></TableCell><TableCell>{a.action}</TableCell>
                    <TableCell>{a.entityType}</TableCell><TableCell>{a.actorDisplay ?? a.actorType}</TableCell>
                    <TableCell>{a.source}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Paper>
        )
      ) : (
        (events.data ?? []).length === 0 ? <EmptyState title="No security events" /> : (
          <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
            <Table size="small">
              <TableHead>
                <TableRow><TableCell>When</TableCell><TableCell>Event</TableCell><TableCell>Result</TableCell><TableCell>User</TableCell><TableCell>IP</TableCell></TableRow>
              </TableHead>
              <TableBody>
                {events.data!.map((e, i) => (
                  <TableRow key={i}>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>{new Date(e.occurredAt).toLocaleString()}</TableCell>
                    <TableCell>{e.eventType}</TableCell><TableCell><StatusChip value={e.success ? 'Succeeded' : 'Failed'} /></TableCell>
                    <TableCell>{e.userEmail ?? '—'}</TableCell><TableCell>{e.ipAddress ?? '—'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Paper>
        )
      )}
    </Box>
  )
}
