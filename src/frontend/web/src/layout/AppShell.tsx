import { AccountCircle, Logout, Menu as MenuIcon } from '@mui/icons-material'
import {
  AppBar, Box, Button, Chip, Divider, Drawer, IconButton, List, ListItemButton, ListItemText,
  Menu, MenuItem, Toolbar, Typography, useMediaQuery, useTheme,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useState, type MouseEvent } from 'react'
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { api, isPlatform, type NavigationItem } from '../api/client'
import { useAuth } from '../auth/AuthProvider'
import { platformNav } from './nav'

const drawerWidth = 264

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Box sx={{ mt: 1 }}>
      <Typography variant="overline" sx={{ px: 2, color: 'text.secondary', fontSize: 11, letterSpacing: 1 }}>
        {title}
      </Typography>
      <List dense disablePadding>{children}</List>
    </Box>
  )
}

function AdminNav() {
  const { pathname } = useLocation()
  return (
    <>
      {platformNav.map((s) => (
        <Section key={s.title} title={s.title}>
          {s.items.map((i) =>
            i.disabled ? (
              <ListItemButton key={i.label} disabled sx={{ borderRadius: 1, mx: 1 }}>
                <ListItemText primary={i.label} />
                <Chip label="Soon" size="small" variant="outlined" />
              </ListItemButton>
            ) : (
              <ListItemButton
                key={i.label}
                component={Link}
                to={i.route!}
                selected={pathname === i.route}
                sx={{
                  borderRadius: 1, mx: 1,
                  '&.Mui-selected': { bgcolor: 'primary.main', color: 'primary.contrastText', '&:hover': { bgcolor: 'primary.dark' } },
                }}
              >
                <ListItemText primary={i.label} />
              </ListItemButton>
            ),
          )}
        </Section>
      ))}
    </>
  )
}

function TenantNavList({ items, depth = 0 }: { items: NavigationItem[]; depth?: number }) {
  const { pathname } = useLocation()
  return (
    <List dense disablePadding>
      {[...items].sort((a, b) => a.order - b.order).map((i) => (
        <Box key={i.code}>
          {i.route ? (
            <ListItemButton component={Link} to={i.route} selected={pathname === i.route} sx={{ pl: 2 + depth * 2, borderRadius: 1, mx: 1 }}>
              <ListItemText primary={i.title} />
            </ListItemButton>
          ) : (
            <Typography variant="overline" sx={{ pl: 2 + depth * 2, display: 'block', mt: 1, fontSize: 11, letterSpacing: 1, color: 'text.secondary' }}>
              {i.title}
            </Typography>
          )}
          {i.children.length > 0 && <TenantNavList items={i.children} depth={depth + 1} />}
        </Box>
      ))}
    </List>
  )
}

function UserMenu() {
  const { me, logout } = useAuth()
  const navigate = useNavigate()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const open = (e: MouseEvent<HTMLElement>) => setAnchor(e.currentTarget)
  const close = () => setAnchor(null)
  return (
    <>
      <Button color="inherit" startIcon={<AccountCircle />} onClick={open} sx={{ textTransform: 'none' }}>
        {me?.displayName ?? me?.email ?? 'Account'}
      </Button>
      <Menu anchorEl={anchor} open={!!anchor} onClose={close}>
        <MenuItem disabled>
          <Box>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>{me?.displayName}</Typography>
            <Typography variant="caption" color="text.secondary">{me?.email}</Typography>
          </Box>
        </MenuItem>
        <Divider />
        <MenuItem
          onClick={() => {
            close()
            void logout().then(() => navigate(isPlatform ? '/admin/login' : '/login'))
          }}
        >
          <Logout fontSize="small" sx={{ mr: 1 }} /> Sign out
        </MenuItem>
      </Menu>
    </>
  )
}

export function AppShell() {
  const theme = useTheme()
  const mobile = useMediaQuery(theme.breakpoints.down('md'))
  const [open, setOpen] = useState(false)
  const { pathname } = useLocation()
  const nav = useQuery({ queryKey: ['navigation'], queryFn: () => api<NavigationItem[]>('/api/v1/me/navigation'), enabled: !isPlatform })

  const current = isPlatform
    ? platformNav.flatMap((s) => s.items).find((i) => i.route === pathname)?.label ?? 'Platform admin'
    : 'HRMS'

  const drawer = (
    <Box sx={{ pb: 2 }}>
      <Toolbar sx={{ gap: 1 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 700 }} noWrap>
          {isPlatform ? 'HRMS Platform' : 'HRMS'}
        </Typography>
        {isPlatform && <Chip label="Admin" size="small" color="primary" />}
      </Toolbar>
      {isPlatform ? <AdminNav /> : <TenantNavList items={nav.data ?? []} />}
    </Box>
  )

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <AppBar position="fixed" elevation={0} color="default"
        sx={{ zIndex: (t) => t.zIndex.drawer + 1, borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
        <Toolbar>
          {mobile && (
            <IconButton edge="start" onClick={() => setOpen(true)} sx={{ mr: 1 }}>
              <MenuIcon />
            </IconButton>
          )}
          <Typography variant="h6" color="text.primary" sx={{ flexGrow: 1 }}>{current}</Typography>
          <UserMenu />
        </Toolbar>
      </AppBar>
      <Box component="nav" sx={{ width: { md: drawerWidth }, flexShrink: { md: 0 } }}>
        {mobile ? (
          <Drawer variant="temporary" open={open} onClose={() => setOpen(false)} ModalProps={{ keepMounted: true }}
            sx={{ [`& .MuiDrawer-paper`]: { width: drawerWidth } }}>
            {drawer}
          </Drawer>
        ) : (
          <Drawer variant="permanent" sx={{ [`& .MuiDrawer-paper`]: { width: drawerWidth, boxSizing: 'border-box', borderRight: 1, borderColor: 'divider' } }}>
            {drawer}
          </Drawer>
        )}
      </Box>
      <Box component="main" sx={{ flexGrow: 1, p: { xs: 2, md: 4 }, maxWidth: 1400, width: '100%', mx: 'auto' }}>
        <Toolbar />
        <Outlet />
      </Box>
    </Box>
  )
}
