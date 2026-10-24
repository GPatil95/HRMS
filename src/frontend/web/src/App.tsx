import { Box, CircularProgress } from '@mui/material'
import type { ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { isPlatform } from './api/client'
import { useAuth } from './auth/AuthProvider'
import { AppShell } from './layout/AppShell'
import { HomePage, NotFoundPage, TenantsPage, UsersPage } from './pages/AppPages'
import { ForgotPasswordPage, LoginPage, SetPasswordPage } from './pages/AuthPages'

function RequireSession({ children }: { children: ReactNode }) {
  const { me } = useAuth()
  if (me === undefined) return <Box sx={{ p: 4 }}><CircularProgress /></Box>
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
            <Route path="/admin" element={<HomePage />} />
            <Route path="/admin/tenants" element={<TenantsPage />} />
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
