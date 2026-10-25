import {
  AppBar, Avatar, Badge, Box, Breadcrumbs, Chip, Collapse, Divider, Drawer, IconButton,
  InputAdornment, Link as MLink, List, ListItemButton, ListItemIcon, ListItemText, Menu,
  MenuItem, Popover, Stack, TextField, Toolbar, Tooltip, Typography,
  alpha, useMediaQuery, useTheme,
} from '@mui/material'
import {
  Brightness4, Brightness7, ChevronLeft, ChevronRight, ExpandMore, KeyboardArrowDown,
  Logout, Menu as MenuIcon, NotificationsNone, Search, SettingsSuggest, Tune, ViewSidebar,
} from '@mui/icons-material'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent, type MouseEvent, type ReactNode } from 'react'
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { api, isPlatform, type NavigationItem } from '../api/client'
import { platformApi } from '../api/platform'
import { useAuth } from '../auth/AuthProvider'
import { useThemePrefs } from '../theme/tokens'
import { EmptyState, StatusChip } from '../components/ui'
import { CustomizePanel } from './CustomizePanel'
import { breadcrumbFor, platformNav, type NavItem } from './nav'

const DRAWER_W = 264
const DRAWER_FOLDED_W = 68

function Section({ title, collapsed, open, onToggle, children }: {
  title: string; collapsed: boolean; open: boolean; onToggle: () => void; children: ReactNode
}) {
  return (
    <Box sx={{ mt: 0.75 }}>
      {!collapsed && (
        <ListItemButton onClick={onToggle} disableRipple
          sx={{ mx: 1.5, py: 0.4, minHeight: 28, borderRadius: 1, '&:hover .nav-chevron': { opacity: 1 } }}>
          <Typography variant="overline" sx={{
            flexGrow: 1, color: 'text.disabled', fontSize: 10, letterSpacing: 1.4, fontWeight: 700,
          }}>
            {title}
          </Typography>
          <KeyboardArrowDown className="nav-chevron" sx={{
            fontSize: 14, color: 'text.disabled', opacity: open ? 0 : 0.6,
            transform: open ? 'rotate(180deg)' : 'none', transition: 'transform 200ms ease, opacity 150ms ease',
          }} />
        </ListItemButton>
      )}
      <Collapse in={collapsed || open} timeout={200}>
        <List dense disablePadding sx={{ mt: 0.25 }}>{children}</List>
      </Collapse>
    </Box>
  )
}

function NavButton({ item, collapsed }: { item: NavItem; collapsed: boolean }) {
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
        mx: 1.5, mb: 0.25, py: 0.75, px: collapsed ? 0 : 1.25, minHeight: 38,
        opacity: item.disabled ? 0.6 : 1,
        justifyContent: collapsed ? 'center' : 'flex-start',
        color: 'text.secondary',
        '&.Mui-selected': {
          bgcolor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.16 : 0.08),
          color: 'primary.main',
          boxShadow: `inset 2px 0 0 ${theme.palette.primary.main}`,
          '& .MuiListItemIcon-root': { color: 'primary.main' },
          '&:hover': { bgcolor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.22 : 0.12) },
        },
      }}
    >
      <ListItemIcon sx={{ minWidth: collapsed ? 0 : 32, color: 'text.disabled', '& .MuiSvgIcon-root': { fontSize: 19 } }}>
        {item.icon}
      </ListItemIcon>
      {!collapsed && <ListItemText primary={item.label} slotProps={{ primary: { sx: { fontSize: 13, fontWeight: selected ? 650 : 500 } } }} />}
      {!collapsed && item.disabled && <Chip label="Soon" size="small" variant="outlined" sx={{ height: 18, fontSize: 10 }} />}
    </ListItemButton>
  )
  return collapsed ? <Tooltip title={item.label} placement="right">{btn}</Tooltip> : btn
}

function AdminNav({ collapsed }: { collapsed: boolean }) {
  const [openGroups, setOpenGroups] = useState<Record<string, boolean>>({})
  return (
    <>
      {platformNav.map((s) => {
        const open = openGroups[s.title] ?? true
        return (
          <Section key={s.title} title={s.title} collapsed={collapsed} open={open}
            onToggle={() => setOpenGroups((g) => ({ ...g, [s.title]: !open }))}>
            {s.items.map((i) => <NavButton key={i.label} item={i} collapsed={collapsed} />)}
          </Section>
        )
      })}
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
              sx={{ pl: 2 + depth * 2, py: 0.9, mx: 1.5, color: 'text.secondary', '&.Mui-selected': { bgcolor: (t) => alpha(t.palette.primary.main, 0.08), color: 'primary.main' } }}>
              <ListItemText primary={i.title} slotProps={{ primary: { sx: { fontSize: 13 } } }} />
            </ListItemButton>
          ) : (
            <Typography variant="overline" sx={{ pl: 2 + depth * 2, display: 'block', mt: 1, fontSize: 10, letterSpacing: 1.4, color: 'text.disabled' }}>
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
        <Box sx={{ px: 2.5, py: 1.75, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Typography variant="subtitle2">Recent security activity</Typography>
          <MLink component={Link} to="/admin/audit" variant="caption" onClick={() => setAnchor(null)} sx={{ fontWeight: 600 }}>Audit logs</MLink>
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
                  <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                    {e.userEmail ?? 'system'} · {e.occurredAt ? new Date(e.occurredAt).toLocaleString() : ''}
                  </Typography>
                </Box>
                <StatusChip value={e.success ? 'succeeded' : 'failed'} />
              </ListItemButton>
            ))}
          </List>
        )}
      </Popover>
    </>
  )
}

function UserCard({ collapsed, onCustomize }: { collapsed?: boolean; onCustomize: () => void }) {
  const { me, logout } = useAuth()
  const navigate = useNavigate()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const close = () => setAnchor(null)
  const initial = (me?.displayName ?? me?.email ?? '?')[0]!.toUpperCase()
  const trigger = (
    <ListItemButton onClick={(e: MouseEvent<HTMLElement>) => setAnchor(e.currentTarget)}
      sx={collapsed
        ? { mx: 'auto', width: 44, height: 44, borderRadius: '50%', justifyContent: 'center' }
        : { mx: 1.5, px: 1.25, py: 1, borderRadius: 1.5, '&:hover': { bgcolor: (t) => alpha(t.palette.text.primary, 0.05) } }}>
      <Avatar sx={{ width: 34, height: 34, fontSize: 14, fontWeight: 700, bgcolor: 'primary.main', mr: collapsed ? 0 : 1.25 }}>
        {initial}
      </Avatar>
      {!collapsed && (
        <>
          <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            <Typography variant="body2" noWrap sx={{ fontWeight: 600, lineHeight: 1.2 }}>{me?.displayName ?? 'Admin'}</Typography>
            <Typography variant="caption" noWrap color="text.secondary" sx={{ lineHeight: 1.2, display: 'block' }}>{me?.email}</Typography>
          </Box>
          <ExpandMore sx={{ fontSize: 18, color: 'text.disabled' }} />
        </>
      )}
    </ListItemButton>
  )
  return (
    <>
      {collapsed ? <Tooltip title="Account" placement="right">{trigger}</Tooltip> : trigger}
      <Menu anchorEl={anchor} open={!!anchor} onClose={close}
        anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
        transformOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        slotProps={{ paper: { sx: { minWidth: 220 } } }}>
        <Box sx={{ px: 2, py: 1.5 }}>
          <Typography variant="body2" sx={{ fontWeight: 650 }}>{me?.displayName}</Typography>
          <Typography variant="caption" color="text.secondary">{me?.email}</Typography>
        </Box>
        <Divider />
        <MenuItem disabled dense>Profile <Chip label="Soon" size="small" variant="outlined" sx={{ ml: 'auto', height: 18, fontSize: 10 }} /></MenuItem>
        <MenuItem disabled dense>Security <Chip label="Soon" size="small" variant="outlined" sx={{ ml: 'auto', height: 18, fontSize: 10 }} /></MenuItem>
        <MenuItem dense onClick={() => { close(); onCustomize() }}>
          <Tune fontSize="small" sx={{ mr: 1.5, color: 'text.secondary' }} /> Appearance
        </MenuItem>
        <Divider />
        <MenuItem dense onClick={() => { close(); void logout().then(() => navigate(isPlatform ? '/admin/login' : '/login')) }}>
          <Logout fontSize="small" sx={{ mr: 1.5, color: 'text.secondary' }} /> Sign out
        </MenuItem>
      </Menu>
    </>
  )
}

function SearchField() {
  const [search, setSearch] = useState('')
  const { pathname } = useLocation()
  const navigate = useNavigate()
  useEffect(() => {
    if (!pathname.startsWith('/admin/tenants')) setSearch('')
  }, [pathname])
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (search.trim()) navigate(`/admin/tenants?q=${encodeURIComponent(search.trim())}`)
  }
  return (
    <Box component="form" onSubmit={submit} sx={{ display: { xs: 'none', md: 'block' } }}>
      <TextField
        size="small" placeholder="Search tenants, modules, users…" value={search}
        onChange={(e) => setSearch(e.target.value)} aria-label="Search"
        sx={{
          width: 300,
          '& .MuiOutlinedInput-root': {
            borderRadius: 2, bgcolor: (t) => alpha(t.palette.text.primary, 0.04),
            '& fieldset': { borderColor: 'transparent' },
            '&:hover fieldset': { borderColor: 'divider' },
            '&.Mui-focused fieldset': { borderColor: 'primary.main', borderWidth: 1 },
          },
        }}
        slotProps={{
          input: {
            startAdornment: <InputAdornment position="start"><Search fontSize="small" sx={{ color: 'text.disabled' }} /></InputAdornment>,
            endAdornment: (
              <InputAdornment position="end">
                <Box component="kbd" sx={{
                  px: 0.75, py: 0.1, borderRadius: 0.75, border: 1, borderColor: 'divider',
                  fontSize: 10.5, fontFamily: 'inherit', color: 'text.disabled', lineHeight: 1.6,
                }}>/</Box>
              </InputAdornment>
            ),
          },
        }}
      />
    </Box>
  )
}

export function AppShell() {
  const theme = useTheme()
  const prefs = useThemePrefs()
  const mobile = useMediaQuery(theme.breakpoints.down('md'))
  const [mobileOpen, setMobileOpen] = useState(false)
  const [customizeOpen, setCustomizeOpen] = useState(false)
  const [hovered, setHovered] = useState(false)
  const { pathname } = useLocation()
  const nav = useQuery({ queryKey: ['navigation'], queryFn: () => api<NavigationItem[]>('/api/v1/me/navigation'), enabled: !isPlatform })

  const foldedMode = prefs.sidebar === 'folded' || prefs.sidebar === 'hover'
  const collapsed = !mobile && foldedMode && !(prefs.sidebar === 'hover' && hovered)
  const floating = prefs.navStyle === 'floating' && !mobile
  const width = collapsed ? DRAWER_FOLDED_W : DRAWER_W
  const railWidth = foldedMode ? DRAWER_FOLDED_W : DRAWER_W

  const crumbs = isPlatform ? breadcrumbFor(pathname) : []
  const containerMax = prefs.container === 'boxed' ? 1200 : prefs.container === 'fluid' ? '100%' : 1440

  const drawer = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Toolbar sx={{ gap: 1.25, px: collapsed ? 0 : 2, justifyContent: collapsed ? 'center' : 'flex-start', minHeight: 60 }}>
        <Avatar variant="rounded" sx={{ width: 32, height: 32, fontWeight: 800, fontSize: 15, bgcolor: 'primary.main', borderRadius: 1.5 }}>H</Avatar>
        {!collapsed && (
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="subtitle2" noWrap sx={{ fontWeight: 800, letterSpacing: 0.2, lineHeight: 1.2 }}>
              {isPlatform ? 'HRMS Platform' : 'HRMS'}
            </Typography>
            {isPlatform && <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1 }}>Admin console</Typography>}
          </Box>
        )}
      </Toolbar>
      <Box sx={{ flexGrow: 1, overflowY: 'auto', overflowX: 'hidden', pb: 1 }}>
        {isPlatform ? <AdminNav collapsed={collapsed} /> : <TenantNavList items={nav.data ?? []} />}
      </Box>
      <Divider />
      <Box sx={{ py: 1 }}><UserCard collapsed={collapsed} onCustomize={() => setCustomizeOpen(true)} /></Box>
    </Box>
  )

  const sidebarPaperSx = {
    width,
    boxSizing: 'border-box' as const,
    borderRight: floating ? 0 : 1,
    borderColor: 'divider',
    transition: theme.transitions.create('width', { duration: theme.transitions.duration.short }),
    overflowX: 'hidden' as const,
    bgcolor: 'background.paper',
    ...(floating && {
      m: 1.5, mr: 0, borderRadius: 2.5, border: 1,
      height: 'calc(100% - 24px)',
      boxShadow: theme.palette.mode === 'dark' ? '0 4px 16px rgba(0,0,0,0.35)' : '0 4px 16px rgba(29,41,57,0.08)',
    }),
    ...(prefs.sidebar === 'hover' && {
      position: 'fixed' as const,
      zIndex: theme.zIndex.drawer + 2,
      height: '100%',
      ...(hovered && { boxShadow: theme.palette.mode === 'dark' ? '0 8px 24px rgba(0,0,0,0.5)' : '0 8px 24px rgba(29,41,57,0.15)' }),
    }),
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar
        position={prefs.navbar === 'sticky' ? 'sticky' : 'static'} elevation={0} color="default"
        sx={{
          top: 0, zIndex: theme.zIndex.drawer + 1,
          borderBottom: 1, borderColor: 'divider',
          bgcolor: alpha(theme.palette.background.default, 0.85), backdropFilter: 'blur(10px)',
          color: 'text.primary', backgroundImage: 'none',
        }}
      >
        <Toolbar sx={{ gap: 1, minHeight: 60 }}>
          {mobile && (
            <IconButton edge="start" onClick={() => setMobileOpen(true)} aria-label="Open navigation">
              <MenuIcon />
            </IconButton>
          )}
          {!mobile && (
            <Tooltip title={foldedMode ? 'Expand sidebar' : 'Collapse sidebar'}>
              <IconButton
                onClick={() => prefs.setSidebar(foldedMode ? 'expanded' : 'folded')}
                aria-label="Toggle sidebar" sx={{ color: 'text.secondary' }}
              >
                {foldedMode ? <ViewSidebar /> : <ChevronLeft />}
              </IconButton>
            </Tooltip>
          )}
          {crumbs.length > 0 && (
            <Breadcrumbs separator={<ChevronRight sx={{ fontSize: 14, color: 'text.disabled' }} />} sx={{ display: { xs: 'none', sm: 'flex' } }}>
              {crumbs.map((c, i) => (
                <Typography key={c} variant="body2" color={i === crumbs.length - 1 ? 'text.primary' : 'text.secondary'} sx={{ fontWeight: i === crumbs.length - 1 ? 650 : 500 }}>
                  {c}
                </Typography>
              ))}
            </Breadcrumbs>
          )}
          <Box sx={{ flexGrow: 1 }} />
          {isPlatform && <SearchField />}
          {isPlatform && <NotificationsMenu />}
          <Tooltip title={theme.palette.mode === 'dark' ? 'Switch to light' : 'Switch to dark'}>
            <IconButton
              onClick={() => prefs.setMode(theme.palette.mode === 'dark' ? 'light' : 'dark')}
              aria-label="Toggle color mode" sx={{ color: 'text.secondary' }}
            >
              {theme.palette.mode === 'dark' ? <Brightness7 /> : <Brightness4 />}
            </IconButton>
          </Tooltip>
          <Tooltip title="Customize appearance">
            <IconButton onClick={() => setCustomizeOpen(true)} aria-label="Customize" sx={{ color: 'text.secondary' }}>
              <SettingsSuggest />
            </IconButton>
          </Tooltip>
        </Toolbar>
      </AppBar>

      <Box sx={{ display: 'flex', flexGrow: 1, minHeight: 0 }}>
        <Box
          component="nav"
          onMouseEnter={() => prefs.sidebar === 'hover' && setHovered(true)}
          onMouseLeave={() => prefs.sidebar === 'hover' && setHovered(false)}
          sx={{ width: { md: railWidth + (floating ? 24 : 0) }, flexShrink: { md: 0 } }}
        >
          {mobile ? (
            <Drawer variant="temporary" open={mobileOpen} onClose={() => setMobileOpen(false)} ModalProps={{ keepMounted: true }}
              sx={{ '& .MuiDrawer-paper': { width: DRAWER_W, borderRight: 0 } }}>
              {drawer}
            </Drawer>
          ) : (
            <Drawer variant="permanent" sx={{ '& .MuiDrawer-paper': sidebarPaperSx }}>
              {drawer}
            </Drawer>
          )}
        </Box>

        <Box component="main" sx={{ flexGrow: 1, width: '100%', minWidth: 0 }}>
          <Box sx={{
            px: { xs: 2, sm: 3, md: prefs.container === 'fluid' ? 4 : 3 },
            py: { xs: 2, md: 3 },
            maxWidth: containerMax === '100%' ? 'none' : containerMax,
            mx: 'auto', width: '100%',
          }}>
            <Stack spacing={0} sx={{ height: '100%' }}>
              <Outlet />
            </Stack>
          </Box>
        </Box>
      </Box>

      <CustomizePanel open={customizeOpen} onClose={() => setCustomizeOpen(false)} />
    </Box>
  )
}
