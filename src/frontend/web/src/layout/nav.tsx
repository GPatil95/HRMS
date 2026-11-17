import {
  AddBusiness,
  Business,
  CloudQueue,
  Dashboard,
  Description,
  Folder,
  Groups,
  HealthAndSafety,
  Inventory,
  Key,
  ListAlt,
  Payments,
  Shield,
  Sync,
  Tune,
  VpnKey,
  Workspaces,
} from '@mui/icons-material'
import type { ReactElement } from 'react'

export interface NavItem {
  label: string
  route?: string
  disabled?: boolean
  icon: ReactElement
}

export interface NavSection {
  title: string
  items: NavItem[]
}

// Platform admin navigation. Only items backed by a real platform API are enabled;
// the rest render disabled so nothing fake is implied.
export const platformNav: NavSection[] = [
  { title: 'Dashboard', items: [{ label: 'Overview', route: '/admin', icon: <Dashboard /> }] },
  {
    title: 'Tenant Management',
    items: [
      { label: 'All Tenants', route: '/admin/tenants', icon: <Business /> },
      { label: 'Create Tenant', route: '/admin/tenants/new', icon: <AddBusiness /> },
      { label: 'Provisioning / Operations', route: '/admin/operations', icon: <Sync /> },
    ],
  },
  {
    title: 'Platform',
    items: [
      { label: 'Modules', route: '/admin/modules', icon: <Inventory /> },
      { label: 'Plans', route: '/admin/plans', icon: <Payments /> },
      { label: 'Features', route: '/admin/features', disabled: true, icon: <Tune /> },
    ],
  },
  {
    title: 'Security',
    items: [
      { label: 'Platform Users', route: '/admin/users', disabled: true, icon: <Groups /> },
      { label: 'Roles & Permissions', route: '/admin/roles', disabled: true, icon: <VpnKey /> },
      { label: 'Audit Logs', route: '/admin/audit', icon: <ListAlt /> },
      { label: 'Support Access', route: '/admin/support-access', icon: <Shield /> },
    ],
  },
  {
    title: 'Infrastructure',
    items: [
      { label: 'Database Clusters', route: '/admin/clusters', disabled: true, icon: <Workspaces /> },
      { label: 'System Health', route: '/admin/health', icon: <HealthAndSafety /> },
    ],
  },
  { title: 'Administration', items: [{ label: 'Settings', route: '/admin/settings', disabled: true, icon: <Key /> }] },
]

export function breadcrumbFor(pathname: string): string[] {
  if (pathname === '/admin' || pathname === '/admin/') return ['Dashboard', 'Overview']
  if (pathname.startsWith('/admin/tenants/new')) return ['Tenant Management', 'Create Tenant']
  if (pathname.startsWith('/admin/tenants/')) return ['Tenant Management', 'All Tenants', 'Details']
  for (const s of platformNav) {
    const hit = s.items.find((i) => i.route && i.route !== '/admin' && (pathname === i.route || pathname.startsWith(`${i.route}/`)))
    if (hit) return [s.title, hit.label]
  }
  return []
}

// Icons reused for tenant pages that have no nav icon of their own.
export const icons = { Business, CloudQueue, Description, Folder, HealthAndSafety, Inventory, Payments }
