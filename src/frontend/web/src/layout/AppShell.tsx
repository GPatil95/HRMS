import { AppBar, Box, Button, Drawer, List, ListItemButton, ListItemText, Toolbar, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link, Outlet, useLocation } from 'react-router-dom'
import { api, isPlatform, type NavigationItem } from '../api/client'
import { useAuth } from '../auth/AuthProvider'

const drawerWidth = 248

// Platform admin menu is static; the backend still enforces every platform permission.
const platformNav: NavigationItem[] = [
  { code: 'p.home', title: 'Overview', route: '/admin', order: 0, moduleCode: 'platform', children: [] },
  { code: 'p.tenants', title: 'Tenants', route: '/admin/tenants', order: 1, moduleCode: 'platform', children: [] },
]

function NavList({ items, depth = 0 }: { items: NavigationItem[]; depth?: number }) {
  const { pathname } = useLocation()
  return (
    <List dense disablePadding>
      {[...items].sort((a, b) => a.order - b.order).map((i) => (
        <Box key={i.code}>
          {i.route ? (
            <ListItemButton component={Link} to={i.route} selected={pathname === i.route} sx={{ pl: 2 + depth * 2 }}>
              <ListItemText primary={i.title} />
            </ListItemButton>
          ) : (
            <Typography variant="overline" sx={{ pl: 2 + depth * 2, display: 'block', mt: 1 }}>{i.title}</Typography>
          )}
          {i.children.length > 0 && <NavList items={i.children} depth={depth + 1} />}
        </Box>
      ))}
    </List>
  )
}

export function AppShell() {
  const { me, logout } = useAuth()
  const nav = useQuery({ queryKey: ['navigation'], queryFn: () => api<NavigationItem[]>('/api/v1/me/navigation'), enabled: !isPlatform })
  const items = isPlatform ? platformNav : (nav.data ?? [])
  return (
    <Box sx={{ display: 'flex' }}>
      <AppBar position="fixed" sx={{ zIndex: (t) => t.zIndex.drawer + 1 }}>
        <Toolbar>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>{isPlatform ? 'HRMS Platform Admin' : 'HRMS'}</Typography>
          <Typography variant="body2" sx={{ mr: 2 }}>{me?.displayName}</Typography>
          <Button color="inherit" onClick={() => void logout()}>Sign out</Button>
        </Toolbar>
      </AppBar>
      <Drawer variant="permanent" sx={{ width: drawerWidth, [`& .MuiDrawer-paper`]: { width: drawerWidth, boxSizing: 'border-box' } }}>
        <Toolbar />
        <NavList items={items} />
      </Drawer>
      <Box component="main" sx={{ flexGrow: 1, p: 3 }}>
        <Toolbar />
        <Outlet />
      </Box>
    </Box>
  )
}
