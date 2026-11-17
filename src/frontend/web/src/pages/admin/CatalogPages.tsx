import { Inventory, Payments } from '@mui/icons-material'
import { Box, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { platformApi } from '../../api/platform'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, StatusChip, TableSkeleton } from '../../components/ui'

export function ModulesPage() {
  const q = useQuery({ queryKey: ['catalog-modules'], queryFn: platformApi.modules })
  if (q.isPending) return <Box><PageHeader title="Module catalogue" /><SectionCard><TableSkeleton /></SectionCard></Box>
  if (q.error) return <Box><PageHeader title="Module catalogue" /><ErrorAlert error={q.error} onRetry={() => void q.refetch()} /></Box>
  const groups: [string, typeof q.data][] = [['Core', q.data!.filter((m) => m.kind === 'Core')], ['Optional', q.data!.filter((m) => m.kind === 'Optional')], ['Sub-modules', q.data!.filter((m) => m.kind === 'SubModule')]]
  return (
    <Box>
      <PageHeader title="Module catalogue" subtitle="Modules tenants can purchase and enable" />
      {q.data!.length === 0 && <SectionCard><EmptyState title="Module catalogue is empty" icon={<Inventory />} /></SectionCard>}
      {groups.map(([title, mods]) => mods!.length > 0 && (
        <SectionCard key={title} title={title} sx={{ mb: 2.5 }}>
          <Table size="small">
            <TableHead><TableRow><TableCell>Name</TableCell><TableCell>Code</TableCell><TableCell>Parent</TableCell><TableCell>Active</TableCell></TableRow></TableHead>
            <TableBody>
              {mods!.map((m) => (
                <TableRow key={m.code} hover>
                  <TableCell><Typography variant="body2" sx={{ fontWeight: 600 }}>{m.name}</Typography></TableCell>
                  <TableCell><Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{m.code}</Typography></TableCell>
                  <TableCell><Typography variant="body2" color="text.secondary" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{m.parentCode ?? '—'}</Typography></TableCell>
                  <TableCell><StatusChip value={m.isActive ? 'Active' : 'Inactive'} /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </SectionCard>
      ))}
    </Box>
  )
}

export function PlansPage() {
  const q = useQuery({ queryKey: ['plans'], queryFn: platformApi.plans })
  if (q.isPending) return <Box><PageHeader title="Subscription plans" /><SectionCard><TableSkeleton /></SectionCard></Box>
  if (q.error) return <Box><PageHeader title="Subscription plans" /><ErrorAlert error={q.error} onRetry={() => void q.refetch()} /></Box>
  return (
    <Box>
      <PageHeader title="Subscription plans" subtitle="Plans a tenant can be placed on" />
      <SectionCard sx={{ overflowX: 'auto' }}>
        {q.data!.length === 0 ? <EmptyState title="No plans defined" icon={<Payments />} /> : (
          <Table size="small">
            <TableHead>
              <TableRow><TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Version</TableCell><TableCell>Status</TableCell><TableCell>Employee limit</TableCell><TableCell>Public</TableCell></TableRow>
            </TableHead>
            <TableBody>
              {q.data!.map((p) => (
                <TableRow key={`${p.code}@${p.version}`} hover>
                  <TableCell><Typography variant="body2" sx={{ fontFamily: 'monospace', fontWeight: 600 }}>{p.code}</Typography></TableCell>
                  <TableCell>{p.name}</TableCell><TableCell>{p.version}</TableCell>
                  <TableCell><StatusChip value={p.status} /></TableCell>
                  <TableCell>{p.employeeLimit?.toLocaleString() ?? 'Unlimited'}</TableCell>
                  <TableCell>{p.isPublic ? 'Yes' : 'No'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>
    </Box>
  )
}
