import {
  ChevronLeft,
  DarkMode,
  ExpandMore,
  LightMode,
  Logout,
  Menu as MenuIcon,
  Monitor,
  NotificationsNone,
  Search,
  ViewSidebar,
} from '@mui/icons-material'
import {
  AppBar,
  Avatar,
  Badge,
  Box,
  Breadcrumbs,
  Button,
  Chip,
  Divider,
  Drawer,
  IconButton,
  InputAdornment,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Popover,
  TextField,
  Toolbar,
  Tooltip,
  Typography,
  alpha,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent, type MouseEvent } from 'react'
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { api, isPlatform, type NavigationItem } from '../api/client'
import { platformApi } from '../api/platform'
import { useAuth } from '../auth/AuthProvider'
import { accents, accentKeys, useThemePrefs, type ThemeMode } from '../theme/tokens'
import { EmptyState, StatusChip } from '../components/ui'
import { breadcrumbFor, platformNav } from './nav'

const DRAWER_W = 268
const DRAWER_COLLAPSED_W = 76
const COLLAPSE_KEY = 'hrms.admin.sidebar'

function Section({ title, collapsed, children }: { title: string; collapsed: boolean; children: React.ReactNode }) {
  return (
    <Box sx={{ mt: 1 }}>
      {!collapsed && (
        <Typography variant="overline" sx={{ px: 3, color: 'text.disabled', fontSize: 10.5, letterSpacing: 1.2, fontWeight: 700 }}>
          {title}
        </Typography>
      )}
      <List dense disablePadding sx={{ mt: collapsed ? 0 : 0.5 }}>{children}</List>
    </Box>
  )
}

function NavButton({ item, collapsed }: { item: { label: string; route?: string; disabled?: boolean; icon: React.ReactElement }; collapsed: boolean }) {
  const { pathname } = useLocation()
  const theme = useTheme()
  const selected = !!item.route && pathname === item.route
  const btn = (
    <ListItemButton
      component={item.route ? Link : 'div'}
      to={item.route}
      disabled={item.disabled && !item.route}
      selected={selected}
      sx={{
        mx: 1.5, mb: 0.25, py: 0.9, px: collapsed ? 0 : 1.25, minHeight: 40,
        opacity: item.disabled ? 0.65 : 1,
        justifyContent: collapsed ? 'center' : 'flex-start',
        '&.Mui-selected': {
          bgcolor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.18 : 0.1),
          color: 'primary.main',
          '& .MuiListItemIcon-root': { color: 'primary.main' },
          '&:hover': { bgcolor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.24 : 0.14) },
        },
        '&.Mui-disabled': { opacity: 0.55 },
      }}
    >
      <ListItemIcon sx={{ minWidth: collapsed ? 0 : 36, color: 'text.secondary', '& .MuiSvgIcon-root': { fontSize: 20 } }}>
        {item.icon}
      </ListItemIcon>
      {!collapsed && <ListItemText primary={item.label} slotProps={{ primary: { sx: { fontSize: 13.5, fontWeight: selected ? 650 : 500 } } }} />}
      {!collapsed && item.disabled && <Chip label="Soon" size="small" variant="outlined" sx={{ height: 20, fontSize: 10.5 }} />}
    </ListItemButton>
  )
  return collapsed ? <Tooltip title={item.label} placement="right">{btn}</Tooltip> : btn
}

function AdminNav({ collapsed }: { collapsed: boolean }) {
  return (
    <>
      {platformNav.map((s) => (
        <Section key={s.title} title={s.title} collapsed={collapsed}>
          {s.items.map((i) => <NavButton key={i.label} item={i} collapsed={collapsed} />)}
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
            <ListItemButton component={Link} to={i.route} selected={pathname === i.route}
              sx={{ pl: 2 + depth * 2, py: 0.9, mx: 1.5, '&.Mui-selected': { bgcolor: (t) => alpha(t.palette.primary.main, 0.1), color: 'primary.main' } }}>
              <ListItemText primary={i.title} slotProps={{ primary: { sx: { fontSize: 13.5 } } }} />
            </ListItemButton>
          ) : (
            <Typography variant="overline" sx={{ pl: 2 + depth * 2, display: 'block', mt: 1, fontSize: 10.5, letterSpacing: 1.2, color: 'text.disabled' }}>
              {i.title}
            </Typography>
          )}
          {i.children.length > 0 && <TenantNavList items={i.children} depth={depth + 1} />}
        </Box>
      ))}
    </List>
  )
}

function NotificationsMenu() {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const events = useQuery({ queryKey: ['security-events', 'bell'], queryFn: platformApi.securityEvents, enabled: isPlatform })
  const latest = (events.data ?? []).slice(0, 5)
  const failed = latest.filter((e) => !e.success).length
  return (
    <>
      <Tooltip title="Recent security activity">
        <IconButton onClick={(e: MouseEvent<HTMLElement>) => setAnchor(e.currentTarget)} aria-label="Recent activity" sx={{ color: 'text.secondary' }}>
          <Badge badgeContent={failed} color="error"><NotificationsNone /></Badge>
        </IconButton>
      </Tooltip>
      <Popover
        anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{ paper: { sx: { width: 380, mt: 1 } } }}
      >
        <Box sx={{ px: 2.5, py: 2, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Typography variant="subtitle2">Recent security activity</Typography>
          <Button component={Link} to="/admin/audit" size="small" onClick={() => setAnchor(null)}>Audit logs</Button>
        </Box>
        <Divider />
        {latest.length === 0 ? (
          <EmptyState title="No security events" hint={events.error ? 'Could not load events.' : 'Nothing recorded yet.'} />
        ) : (
          <List dense disablePadding>
            {latest.map((e, i) => (
              <ListItemButton key={i} component={Link} to="/admin/audit" onClick={() => setAnchor(null)} sx={{ alignItems: 'flex-start', py: 1.25 }}>
                <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>{e.eventType}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {e.userEmail ?? 'system'} · {new Date(e.occurredAt).toLocaleString()}
                  </Typography>
                </Box>
                <StatusChip value={e.success ? 'Succeeded' : 'Failed'} />
              </ListItemButton>
            ))}
          </List>
        )}
      </Popover>
    </>
  )
}

function ThemeMenu() {
  const { mode, accent, setMode, setAccent } = useThemePrefs()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const modes: { value: ThemeMode; label: string; icon: React.ReactElement }[] = [
    { value: 'light', label: 'Light', icon: <LightMode fontSize="small" /> },
    { value: 'dark', label: 'Dark', icon: <DarkMode fontSize="small" /> },
    { value: 'system', label: 'System', icon: <Monitor fontSize="small" /> },
  ]
  return (
    <>
      <Tooltip title="Theme">
        <IconButton onClick={(e: MouseEvent<HTMLElement>) => setAnchor(e.currentTarget)} aria-label="Theme settings" sx={{ color: 'text.secondary' }}>
          {mode === 'dark' ? <DarkMode /> : mode === 'light' ? <LightMode /> : <Monitor />}
        </IconButton>
      </Tooltip>
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}>
        <Box sx={{ px: 2, py: 1 }}>
          <Typography variant="caption" color="text.secondary" sx={{ fontWeight: 700, letterSpacing: 0.5 }}>APPEARANCE</Typography>
        </Box>
        {modes.map((m) => (
          <MenuItem key={m.value} selected={mode === m.value} onClick={() => setMode(m.value)} sx={{ gap: 1.25 }}>
            {m.icon} {m.label}
          </MenuItem>
        ))}
        <Divider sx={{ my: 1 }} />
        <Box sx={{ px: 2, py: 1 }}>
          <Typography variant="caption" color="text.secondary" sx={{ fontWeight: 700, letterSpacing: 0.5 }}>ACCENT</Typography>
        </Box>
        <Box sx={{ display: 'flex', gap: 1, px: 2, pb: 1.5 }}>
          {accentKeys.map((k) => (
            <Tooltip key={k} title={accents[k].label}>
              <IconButton
                size="small" onClick={() => setAccent(k)} aria-label={`${accents[k].label} accent`}
                sx={{
                  width: 26, height: 26, bgcolor: accents[k].main,
                  border: accent === k ? '2px solid' : '2px solid transparent',
                  borderColor: accent === k ? 'text.primary' : 'transparent',
                  '&:hover': { bgcolor: accents[k].dark },
                }}
              />
            </Tooltip>
          ))}
        </Box>
      </Menu>
    </>
  )
}

function UserMenu() {
  const { me, logout } = useAuth()
  const navigate = useNavigate()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const close = () => setAnchor(null)
  const initial = (me?.displayName ?? me?.email ?? '?')[0]!.toUpperCase()
  return (
    <>
      <Button
        onClick={(e: MouseEvent<HTMLElement>) => setAnchor(e.currentTarget)}
        endIcon={<ExpandMore />} sx={{ textTransform: 'none', color: 'text.primary', pl: 0.5 }}
        startIcon={
          <Avatar sx={{ width: 32, height: 32, fontSize: 14, fontWeight: 700, bgcolor: 'primary.main' }}>{initial}</Avatar>
        }
      >
        <Box sx={{ display: { xs: 'none', sm: 'block' }, textAlign: 'left' }}>
          <Typography variant="body2" sx={{ fontWeight: 600, lineHeight: 1.2 }}>{me?.displayName ?? 'Account'}</Typography>
          <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1 }}>{me?.email}</Typography>
        </Box>
      </Button>
      <Menu anchorEl={anchor} open={!!anchor} onClose={close} transformOrigin={{ vertical: 'top', horizontal: 'right' }} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}>
        <MenuItem disabled>
          <Box>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>{me?.displayName}</Typography>
            <Typography variant="caption" color="text.secondary">{me?.email}</Typography>
          </Box>
        </MenuItem>
        <Divider />
        <MenuItem onClick={() => { close(); void logout().then(() => navigate(isPlatform ? '/admin/login' : '/login')) }}>
          <Logout fontSize="small" sx={{ mr: 1.5 }} /> Sign out
        </MenuItem>
      </Menu>
    </>
  )
}

export function AppShell() {
  const theme = useTheme()
  const mobile = useMediaQuery(theme.breakpoints.down('md'))
  const [mobileOpen, setMobileOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_KEY) === '1')
  const [search, setSearch] = useState('')
  const { pathname } = useLocation()
  useEffect(() => {
    if (!pathname.startsWith('/admin/tenants')) setSearch('')
  }, [pathname])
  const navigate = useNavigate()
  const nav = useQuery({ queryKey: ['navigation'], queryFn: () => api<NavigationItem[]>('/api/v1/me/navigation'), enabled: !isPlatform })

  const width = collapsed ? DRAWER_COLLAPSED_W : DRAWER_W
  const toggleCollapse = () => {
    const next = !collapsed
    setCollapsed(next)
    localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0')
  }

  const crumbs = isPlatform ? breadcrumbFor(pathname) : []

  const submitSearch = (e: FormEvent) => {
    e.preventDefault()
    if (search.trim()) navigate(`/admin/tenants?q=${encodeURIComponent(search.trim())}`)
  }

  const drawer = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Toolbar sx={{ gap: 1.25, px: collapsed ? 1 : 2, justifyContent: collapsed ? 'center' : 'flex-start' }}>
        <Avatar
          variant="rounded"
          sx={{ width: 34, height: 34, fontWeight: 800, fontSize: 15, bgcolor: 'primary.main', borderRadius: 2 }}
        >
          H
        </Avatar>
        {!collapsed && (
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="subtitle2" noWrap sx={{ fontWeight: 800, letterSpacing: 0.2 }}>
              {isPlatform ? 'HRMS Platform' : 'HRMS'}
            </Typography>
            {isPlatform && <Typography variant="caption" color="text.secondary">Admin console</Typography>}
          </Box>
        )}
      </Toolbar>
      <Box sx={{ flexGrow: 1, overflowY: 'auto', overflowX: 'hidden', pb: 2 }}>
        {isPlatform ? <AdminNav collapsed={collapsed && !mobile} /> : <TenantNavList items={nav.data ?? []} />}
      </Box>
    </Box>
  )

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar
        position="fixed" elevation={0} color="default"
        sx={{ borderBottom: 1, borderColor: 'divider', bgcolor: alpha(theme.palette.background.default, 0.85), backdropFilter: 'blur(10px)', zIndex: (t) => t.zIndex.drawer + 1 }}
      >
        <Toolbar sx={{ gap: 1 }}>
          {mobile && (
            <IconButton edge="start" onClick={() => setMobileOpen(true)} aria-label="Open navigation">
              <MenuIcon />
            </IconButton>
          )}
          {!mobile && isPlatform && (
            <Tooltip title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}>
              <IconButton onClick={toggleCollapse} aria-label="Toggle sidebar">
                {collapsed ? <ViewSidebar /> : <ChevronLeft />}
              </IconButton>
            </Tooltip>
          )}
          {crumbs.length > 0 && (
            <Breadcrumbs separator="›" sx={{ display: { xs: 'none', sm: 'flex' } }}>
              {crumbs.map((c, i) => (
                <Typography key={c} variant="body2" color={i === crumbs.length - 1 ? 'text.primary' : 'text.secondary'} sx={{ fontWeight: i === crumbs.length - 1 ? 650 : 500 }}>
                  {c}
                </Typography>
              ))}
            </Breadcrumbs>
          )}
          <Box sx={{ flexGrow: 1 }} />
          {isPlatform && (
            <Box component="form" onSubmit={submitSearch} sx={{ display: { xs: 'none', md: 'block' } }}>
              <TextField
                size="small" placeholder="Search tenants…" value={search}
                onChange={(e) => setSearch(e.target.value)}
                sx={{ width: 220, '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: 'background.paper' } }}
                slotProps={{ input: { startAdornment: <InputAdornment position="start"><Search fontSize="small" /></InputAdornment> } }}
              />
            </Box>
          )}
          {isPlatform && <NotificationsMenu />}
          <ThemeMenu />
          <UserMenu />
        </Toolbar>
      </AppBar>

      <Box component="nav" sx={{ width: { md: width }, flexShrink: { md: 0 }, transition: theme.transitions.create('width') }}>
        {mobile ? (
          <Drawer variant="temporary" open={mobileOpen} onClose={() => setMobileOpen(false)} ModalProps={{ keepMounted: true }}
            sx={{ '& .MuiDrawer-paper': { width: DRAWER_W, borderRight: 0 } }}>
            {drawer}
          </Drawer>
        ) : (
          <Drawer variant="permanent"
            sx={{ '& .MuiDrawer-paper': { width, boxSizing: 'border-box', borderRight: 1, borderColor: 'divider', transition: theme.transitions.create('width', { duration: theme.transitions.duration.shorter }), overflowX: 'hidden', bgcolor: 'background.paper' } }}>
            {drawer}
          </Drawer>
        )}
      </Box>

      <Box component="main" sx={{ flexGrow: 1, width: '100%', minWidth: 0 }}>
        <Toolbar />
        <Box sx={{ px: { xs: 2, sm: 3, md: 4 }, py: { xs: 2, md: 3.5 }, maxWidth: 1440, mx: 'auto' }}>
          <Outlet />
        </Box>
      </Box>
    </Box>
  )
}
