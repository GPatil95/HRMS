import { Box, Button, Chip, Divider, List, Stack, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { tenantApi } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, LoadingRows } from '../../components/ui'
import { useToast } from '../../components/toast'

export function NotificationsPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const list = useQuery({ queryKey: ['notifications'], queryFn: tenantApi.notifications })
  const markRead = useMutation({
    mutationFn: (id: string) => tenantApi.markRead(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['notifications'] }),
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })
  const markAll = useMutation({
    mutationFn: () => tenantApi.markAllRead(),
    onSuccess: () => { toast('All marked as read'); void qc.invalidateQueries({ queryKey: ['notifications'] }) },
    onError: (e) => toast(e instanceof Error ? e.message : 'Failed', 'error'),
  })

  const items = list.data ?? []
  const unread = items.filter((n) => !n.isRead).length

  return (
    <Box>
      <PageHeader title="Notifications" subtitle={unread ? `${unread} unread` : 'You are all caught up'}
        actions={unread > 0 ? <Button variant="outlined" onClick={() => markAll.mutate()} disabled={markAll.isPending}>Mark all read</Button> : undefined} />
      {list.error && <ErrorAlert error={list.error} onRetry={() => list.refetch()} />}
      <SectionCard>
        {list.isPending ? <LoadingRows /> : items.length === 0 ? (
          <EmptyState title="No notifications" hint="You're all caught up." />
        ) : (
          <List disablePadding>
            {items.map((n, i) => (
              <Box key={n.id}>
                {i > 0 && <Divider />}
                <Box sx={{ px: 3, py: 1.75, display: 'flex', gap: 1.5, alignItems: 'flex-start', bgcolor: n.isRead ? 'transparent' : (t) => `${t.palette.primary.main}0a` }}>
                  <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                      <Typography variant="body2" sx={{ fontWeight: n.isRead ? 500 : 700 }}>{n.title}</Typography>
                      {n.priority === 'High' && <Chip label="High" size="small" color="error" variant="outlined" />}
                    </Box>
                    {n.body && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>{n.body}</Typography>}
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
                      {new Date(n.createdAt).toLocaleString()}
                    </Typography>
                  </Box>
                  <Stack direction="row" spacing={0.5} sx={{ flexShrink: 0 }}>
                    {n.link && <Button size="small" component={Link} to={n.link}>Open</Button>}
                    {!n.isRead && <Button size="small" onClick={() => markRead.mutate(n.id)}>Mark read</Button>}
                  </Stack>
                </Box>
              </Box>
            ))}
          </List>
        )}
      </SectionCard>
    </Box>
  )
}
