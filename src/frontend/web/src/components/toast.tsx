import { Alert, AlertTitle, Box, Button, Grow, Snackbar, alpha, useTheme } from '@mui/material'
import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react'

type Severity = 'success' | 'error' | 'warning' | 'info'

interface Toast {
  msg: string
  title?: string
  severity: Severity
  action?: { label: string; onClick: () => void }
}

const titles: Record<Severity, string> = {
  success: 'Done',
  error: 'Something went wrong',
  warning: 'Heads up',
  info: 'Note',
}

const ToastContext = createContext<(msg: string, severity?: Severity, opts?: { title?: string; action?: Toast['action'] }) => void>(() => {})

export function ToastProvider({ children }: { children: ReactNode }) {
  const theme = useTheme()
  const [toast, setToast] = useState<Toast | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const dismiss = useCallback(() => {
    if (timer.current) clearTimeout(timer.current)
    setToast(null)
  }, [])

  const show = useCallback((msg: string, severity: Severity = 'success', opts?: { title?: string; action?: Toast['action'] }) => {
    if (timer.current) clearTimeout(timer.current)
    setToast({ msg, severity, title: opts?.title, action: opts?.action })
    timer.current = setTimeout(() => setToast(null), severity === 'error' ? 8000 : 5000)
  }, [])

  return (
    <ToastContext.Provider value={show}>
      {children}
      {toast && (
        <Snackbar open slots={{ transition: Grow }} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}>
          <Alert
            severity={toast.severity} variant="filled" onClose={dismiss}
            action={toast.action ? (
              <Button color="inherit" size="small" sx={{ fontWeight: 700 }}
                onClick={() => { toast.action?.onClick(); dismiss() }}>
                {toast.action.label}
              </Button>
            ) : undefined}
            sx={{
              borderRadius: 2, minWidth: 300, maxWidth: 420, alignItems: 'center',
              boxShadow: theme.palette.mode === 'dark' ? `0 8px 24px ${alpha('#000', 0.5)}` : `0 8px 24px ${alpha('#1D2939', 0.18)}`,
            }}
          >
            <AlertTitle sx={{ mb: 0.25, fontWeight: 700 }}>{toast.title ?? titles[toast.severity]}</AlertTitle>
            <Box component="span" sx={{ fontSize: 13 }}>{toast.msg}</Box>
          </Alert>
        </Snackbar>
      )}
    </ToastContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useToast() {
  return useContext(ToastContext)
}
