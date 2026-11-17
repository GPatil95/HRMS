import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, authBase, post, setCsrf, type Me } from '../api/client'

interface AuthState {
  me: Me | null | undefined
  refresh: () => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null | undefined>(undefined)
  const refresh = useCallback(async () => {
    try {
      setMe(await api<Me>(`${authBase}/me`))
    } catch {
      setMe(null)
    }
  }, [])
  const logout = useCallback(async () => {
    await post(`${authBase}/logout`).catch(() => undefined)
    setCsrf(null)
    setMe(null)
  }, [])
  useEffect(() => {
    void refresh()
  }, [refresh])
  return <AuthContext.Provider value={{ me, refresh, logout }}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth outside AuthProvider')
  return ctx
}
