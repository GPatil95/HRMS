import { Alert, Snackbar } from '@mui/material'
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'

const ToastContext = createContext<(msg: string, severity?: 'success' | 'error' | 'info') => void>(() => {})

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<{ msg: string; severity: 'success' | 'error' | 'info' } | null>(null)
  const show = useCallback((msg: string, severity: 'success' | 'error' | 'info' = 'success') => setToast({ msg, severity }), [])
  return (
    <ToastContext.Provider value={show}>
      {children}
      {toast && (
        <Snackbar open autoHideDuration={5000} onClose={() => setToast(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
          <Alert severity={toast.severity} variant="filled" onClose={() => setToast(null)}>{toast.msg}</Alert>
        </Snackbar>
      )}
    </ToastContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useToast() {
  return useContext(ToastContext)
}
