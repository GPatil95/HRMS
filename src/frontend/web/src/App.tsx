import { Box, CircularProgress } from '@mui/material'
import type { ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { isPlatform } from './api/client'
import { useAuth } from './auth/AuthProvider'
import { ComingSoonPage } from './components/ui'
import { AppShell } from './layout/AppShell'
import { OverviewPage } from './pages/admin/OverviewPage'
import { AuditPage } from './pages/admin/AuditPage'
import { ModulesPage, PlansPage } from './pages/admin/CatalogPages'
import { CreateTenantPage } from './pages/admin/CreateTenantPage'
import { HealthPage } from './pages/admin/HealthPage'
import { OperationsPage } from './pages/admin/OperationsPage'
import { SupportAccessPage } from './pages/admin/SupportAccessPage'
import { TenantDetailPage } from './pages/admin/TenantDetailPage'
import { TenantsPage } from './pages/admin/TenantsPage'
import { HomePage, NotFoundPage, TenantsPage as LegacyTenants, UsersPage } from './pages/AppPages'
import { ForgotPasswordPage, LoginPage, SetPasswordPage } from './pages/AuthPages'

void LegacyTenants

function RequireSession({ children }: { children: ReactNode }) {
  const { me } = useAuth()
  if (me === undefined) return <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '100vh' }}><CircularProgress /></Box>
  return me ? children : <Navigate to={isPlatform ? '/admin/login' : '/login'} replace />
}

export default function App() {
  const p = isPlatform ? '/admin' : ''
  return (
    <Routes>
      <Route path={`${p}/login`} element={<LoginPage />} />
      <Route path={`${p}/forgot-password`} element={<ForgotPasswordPage />} />
      <Route path={`${p}/reset-password`} element={<SetPasswordPage title="Choose a new password" />} />
      <Route path={`${p}/accept-invite`} element={<SetPasswordPage title="Accept invitation" />} />
      <Route element={<RequireSession><AppShell /></RequireSession>}>
        {isPlatform ? (
          <>
            <Route path="/admin" element={<OverviewPage />} />
            <Route path="/admin/tenants" element={<TenantsPage />} />
            <Route path="/admin/tenants/new" element={<CreateTenantPage />} />
            <Route path="/admin/tenants/:id" element={<TenantDetailPage />} />
            <Route path="/admin/operations" element={<OperationsPage />} />
            <Route path="/admin/modules" element={<ModulesPage />} />
            <Route path="/admin/plans" element={<PlansPage />} />
            <Route path="/admin/audit" element={<AuditPage />} />
            <Route path="/admin/support-access" element={<SupportAccessPage />} />
            <Route path="/admin/health" element={<HealthPage />} />
            <Route path="/admin/features" element={<ComingSoonPage title="Features" />} />
            <Route path="/admin/users" element={<ComingSoonPage title="Platform users" />} />
            <Route path="/admin/roles" element={<ComingSoonPage title="Roles & permissions" />} />
            <Route path="/admin/clusters" element={<ComingSoonPage title="Database clusters" />} />
            <Route path="/admin/settings" element={<ComingSoonPage title="Settings" />} />
            <Route path="/" element={<Navigate to="/admin" replace />} />
          </>
        ) : (
          <>
            <Route path="/" element={<HomePage />} />
            <Route path="/users" element={<UsersPage />} />
          </>
        )}
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}
