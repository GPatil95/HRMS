import { createContext, useContext } from 'react'

export type ThemeMode = 'light' | 'dark' | 'system'
export type Accent = 'blue' | 'indigo' | 'purple' | 'green' | 'teal' | 'orange'

export const accents: Record<Accent, { label: string; main: string; dark: string; light: string }> = {
  blue: { label: 'Blue', main: '#2563EB', dark: '#1D4ED8', light: '#93C5FD' },
  indigo: { label: 'Indigo', main: '#4F46E5', dark: '#4338CA', light: '#A5B4FC' },
  purple: { label: 'Purple', main: '#7C3AED', dark: '#6D28D9', light: '#C4B5FD' },
  green: { label: 'Green', main: '#059669', dark: '#047857', light: '#6EE7B7' },
  teal: { label: 'Teal', main: '#0D9488', dark: '#0F766E', light: '#5EEAD4' },
  orange: { label: 'Orange', main: '#EA580C', dark: '#C2410C', light: '#FDBA74' },
}

export const accentKeys = Object.keys(accents) as Accent[]

export const MODE_KEY = 'hrms.theme.mode'
export const ACCENT_KEY = 'hrms.theme.accent'

export interface ThemePrefs {
  mode: ThemeMode
  accent: Accent
  setMode: (m: ThemeMode) => void
  setAccent: (a: Accent) => void
}

export const PrefsContext = createContext<ThemePrefs | null>(null)

export function readMode(): ThemeMode {
  const v = localStorage.getItem(MODE_KEY)
  return v === 'light' || v === 'dark' || v === 'system' ? v : 'system'
}

export function readAccent(): Accent {
  const v = localStorage.getItem(ACCENT_KEY) as Accent | null
  return v && accents[v] ? v : 'blue'
}

export function useThemePrefs(): ThemePrefs {
  const ctx = useContext(PrefsContext)
  if (!ctx) throw new Error('useThemePrefs must be used inside AppThemeProvider')
  return ctx
}
