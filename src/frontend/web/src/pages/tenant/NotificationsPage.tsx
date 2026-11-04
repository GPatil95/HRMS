import { useState } from 'react'
import { Box, Button, Chip, Divider, List, Stack, Tab, Tabs, Typography } from '@mui/material'
import {
  AccountTreeOutlined, BadgeOutlined, InfoOutlined, MailOutlined,
  NotificationsOutlined, SecurityOutlined,
} from '@mui/icons-material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { tenantApi } from '../../api/tenant'
import { EmptyState, ErrorAlert, PageHeader, SectionCard, LoadingRows } from '../../components/ui'
import { useToast } from '../../components/toast'

const typeIcon = (type: string) => {
  const t = type.toLowerCase()
  if (t.includes('security') || t.includes('auth')) return <SecurityOutlined fontSize="small" />
  if (t.includes('employee') || t.includes('user')) return <BadgeOutlined fontSize="small" />
  if (t.includes('org') || t.includes('company') || t.includes('branch')) return <AccountTreeOutlined fontSize="small" />
  if (t.includes('mail') || t.includes('invite')) return <MailOutlined fontSize="small" />
  return <InfoOutlined fontSize="small" />
}

const relTime = (iso: string) => {
  const d = new Date(iso)
  const diff = Date.now() - d.getTime()
  const m = Math.floor(diff / 60000)
  if (m < 1) return 'just now'
  if (m < 60) return `${m}m ago`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h}h ago`
  const days = Math.floor(h / 24)
  if (days < 7) return `${days}d ago`
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}

export function NotificationsPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const [filter, setFilter] = useState(0)
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
  const shown = filter === 0 ? items : items.filter((n) => !n.isRead)

  return (
    <Box>
      <PageHeader title="Notifications" subtitle={unread ? `${unread} unread` : 'You are all caught up'}
        actions={unread > 0 ? <Button variant="outlined" size="small" onClick={() => markAll.mutate()} disabled={markAll.isPending}>Mark all read</Button> : undefined} />
      {list.error && <ErrorAlert error={list.error} onRetry={() => list.refetch()} />}
      <Tabs value={filter} onChange={(_, v) => setFilter(v)} sx={{ mb: 2 }}>
        <Tab label={`All${items.length ? ` (${items.length})` : ''}`} />
        <Tab label={`Unread${unread ? ` (${unread})` : ''}`} />
      </Tabs>
      <SectionCard>
        {list.isPending ? <LoadingRows /> : shown.length === 0 ? (
          <EmptyState title={filter === 1 ? 'No unread notifications' : 'No notifications'} hint="You're all caught up." icon={<NotificationsOutlined />} />
        ) : (
          <List disablePadding>
            {shown.map((n, i) => (
              <Box key={n.id}>
                {i > 0 && <Divider />}
                <Box sx={{ px: { xs: 2, sm: 3 }, py: 1.75, display: 'flex', gap: 1.5, alignItems: 'flex-start', bgcolor: n.isRead ? 'transparent' : (t) => `${t.palette.primary.main}0a` }}>
                  <Box sx={{ mt: 0.25, p: 0.75, borderRadius: 1.5, display: 'grid', placeItems: 'center', bgcolor: 'action.hover', color: 'text.secondary', flexShrink: 0 }}>
                    {typeIcon(n.type)}
                  </Box>
                  <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                      <Typography variant="body2" sx={{ fontWeight: n.isRead ? 500 : 700 }}>{n.title}</Typography>
                      {n.priority === 'High' && <Chip label="High" size="small" color="error" variant="outlined" />}
                      {!n.isRead && <Box sx={{ width: 7, height: 7, borderRadius: '50%', bgcolor: 'primary.main' }} />}
                    </Box>
                    {n.body && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>{n.body}</Typography>}
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
                      {relTime(n.createdAt)} · {new Date(n.createdAt).toLocaleString()}
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
