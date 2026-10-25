import { Box, Paper, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { platformApi } from '../../api/platform'
import { CenteredSpinner, EmptyState, ErrorAlert, PageHeader, StatusChip } from '../../components/ui'

export function ModulesPage() {
  const q = useQuery({ queryKey: ['catalog-modules'], queryFn: platformApi.modules })
  if (q.isPending) return <CenteredSpinner />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => void q.refetch()} />
  const groups: [string, typeof q.data][] = [['Core', q.data!.filter((m) => m.kind === 'Core')], ['Optional', q.data!.filter((m) => m.kind === 'Optional')], ['Sub-modules', q.data!.filter((m) => m.kind === 'SubModule')]]
  return (
    <Box>
      <PageHeader title="Module catalogue" subtitle="Modules tenants can purchase and enable" />
      {groups.map(([title, mods]) => mods!.length > 0 && (
        <Paper key={title} variant="outlined" sx={{ mb: 2 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 600, p: 2, pb: 0 }}>{title}</Typography>
          <Table size="small">
            <TableHead><TableRow><TableCell>Name</TableCell><TableCell>Code</TableCell><TableCell>Parent</TableCell><TableCell>Active</TableCell></TableRow></TableHead>
            <TableBody>
              {mods!.map((m) => (
                <TableRow key={m.code}>
                  <TableCell>{m.name}</TableCell><TableCell><code>{m.code}</code></TableCell>
                  <TableCell>{m.parentCode ?? '—'}</TableCell><TableCell><StatusChip value={m.isActive ? 'Active' : 'Inactive'} /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      ))}
      {q.data!.length === 0 && <EmptyState title="Module catalogue is empty" />}
    </Box>
  )
}

export function PlansPage() {
  const q = useQuery({ queryKey: ['plans'], queryFn: platformApi.plans })
  if (q.isPending) return <CenteredSpinner />
  if (q.error) return <ErrorAlert error={q.error} onRetry={() => void q.refetch()} />
  return (
    <Box>
      <PageHeader title="Subscription plans" />
      {q.data!.length === 0 ? <EmptyState title="No plans defined" /> : (
        <Paper variant="outlined">
          <Table size="small">
            <TableHead>
              <TableRow><TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Version</TableCell><TableCell>Status</TableCell><TableCell>Employee limit</TableCell><TableCell>Public</TableCell></TableRow>
            </TableHead>
            <TableBody>
              {q.data!.map((p) => (
                <TableRow key={`${p.code}@${p.version}`}>
                  <TableCell><code>{p.code}</code></TableCell><TableCell>{p.name}</TableCell><TableCell>{p.version}</TableCell>
                  <TableCell><StatusChip value={p.status} /></TableCell>
                  <TableCell>{p.employeeLimit?.toLocaleString() ?? 'Unlimited'}</TableCell>
                  <TableCell>{p.isPublic ? 'Yes' : 'No'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
    </Box>
  )
}
