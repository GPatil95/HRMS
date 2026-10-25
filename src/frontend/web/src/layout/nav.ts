export interface NavItem {
  label: string
  route?: string
  disabled?: boolean
}

export interface NavSection {
  title: string
  items: NavItem[]
}

// Platform admin navigation. Only items backed by a real platform API are enabled;
// the rest render disabled so nothing fake is implied.
export const platformNav: NavSection[] = [
  { title: 'Dashboard', items: [{ label: 'Overview', route: '/admin' }] },
  {
    title: 'Tenant Management',
    items: [
      { label: 'All Tenants', route: '/admin/tenants' },
      { label: 'Create Tenant', route: '/admin/tenants/new' },
      { label: 'Provisioning / Operations', route: '/admin/operations' },
    ],
  },
  {
    title: 'Platform',
    items: [
      { label: 'Modules', route: '/admin/modules' },
      { label: 'Plans', route: '/admin/plans' },
      { label: 'Features', disabled: true },
    ],
  },
  {
    title: 'Security',
    items: [
      { label: 'Platform Users', disabled: true },
      { label: 'Roles & Permissions', disabled: true },
      { label: 'Audit Logs', route: '/admin/audit' },
      { label: 'Support Access', route: '/admin/support-access' },
    ],
  },
  {
    title: 'Infrastructure',
    items: [
      { label: 'Database Clusters', disabled: true },
      { label: 'System Health', route: '/admin/health' },
    ],
  },
  { title: 'Administration', items: [{ label: 'Settings', disabled: true }] },
]
