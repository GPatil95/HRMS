import '@fontsource-variable/inter'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import { AuthProvider } from './auth/AuthProvider'
import { ToastProvider } from './components/toast'
import { ErrorBoundary } from './pages/AppPages'
import { AppThemeProvider } from './theme'

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } })

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppThemeProvider>
      <ErrorBoundary>
        <QueryClientProvider client={queryClient}>
          <BrowserRouter>
            <ToastProvider>
            <AuthProvider>
              <App />
            </AuthProvider>
          </ToastProvider>
          </BrowserRouter>
        </QueryClientProvider>
      </ErrorBoundary>
    </AppThemeProvider>
  </StrictMode>,
)
