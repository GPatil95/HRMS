import { createContext, useContext } from 'react'

export type ThemeMode = 'light' | 'dark' | 'system'
export type Accent = 'blue' | 'indigo' | 'purple' | 'green' | 'teal' | 'orange' | 'red'
export type SidebarMode = 'expanded' | 'folded' | 'hover'
export type ContainerWidth = 'default' | 'fluid' | 'boxed'
export type NavbarMode = 'sticky' | 'static'
export type NavStyle = 'attached' | 'floating'

export const accents: Record<Accent, { label: string; main: string; dark: string; light: string }> = {
  blue: { label: 'Blue', main: '#206BC4', dark: '#1A5AA8', light: '#8FBBEC' },
  indigo: { label: 'Indigo', main: '#4F46E5', dark: '#4338CA', light: '#A5B4FC' },
  purple: { label: 'Purple', main: '#7C3AED', dark: '#6D28D9', light: '#C4B5FD' },
  green: { label: 'Green', main: '#2FB344', dark: '#269B38', light: '#8FE3A0' },
  teal: { label: 'Teal', main: '#0CA678', dark: '#0A8F68', light: '#7ADEBB' },
  orange: { label: 'Orange', main: '#F76707', dark: '#D95A06', light: '#FBB77F' },
  red: { label: 'Red', main: '#D63939', dark: '#B82E2E', light: '#F4A3A3' },
}

export const accentKeys = Object.keys(accents) as Accent[]

export const MODE_KEY = 'hrms.theme.mode'
export const ACCENT_KEY = 'hrms.theme.accent'
export const SIDEBAR_KEY = 'hrms.layout.sidebar'
export const CONTAINER_KEY = 'hrms.layout.container'
export const NAVBAR_KEY = 'hrms.layout.navbar'
export const NAVSTYLE_KEY = 'hrms.layout.navstyle'

export interface ThemePrefs {
  mode: ThemeMode
  accent: Accent
  sidebar: SidebarMode
  container: ContainerWidth
  navbar: NavbarMode
  navStyle: NavStyle
  setMode: (m: ThemeMode) => void
  setAccent: (a: Accent) => void
  setSidebar: (s: SidebarMode) => void
  setContainer: (c: ContainerWidth) => void
  setNavbar: (n: NavbarMode) => void
  setNavStyle: (n: NavStyle) => void
}

export const PrefsContext = createContext<ThemePrefs | null>(null)

function read<T extends string>(key: string, valid: readonly T[], fallback: T): T {
  const v = localStorage.getItem(key) as T | null
  return v && valid.includes(v) ? v : fallback
}

export function readMode(): ThemeMode {
  return read(MODE_KEY, ['light', 'dark', 'system'] as const, 'system')
}

export function readAccent(): Accent {
  return read(ACCENT_KEY, accentKeys, 'blue')
}

export function readSidebar(): SidebarMode {
  return read(SIDEBAR_KEY, ['expanded', 'folded', 'hover'] as const, 'expanded')
}

export function readContainer(): ContainerWidth {
  return read(CONTAINER_KEY, ['default', 'fluid', 'boxed'] as const, 'default')
}

export function readNavbar(): NavbarMode {
  return read(NAVBAR_KEY, ['sticky', 'static'] as const, 'sticky')
}

export function readNavStyle(): NavStyle {
  return read(NAVSTYLE_KEY, ['attached', 'floating'] as const, 'attached')
}

export function useThemePrefs(): ThemePrefs {
  const ctx = useContext(PrefsContext)
  if (!ctx) throw new Error('useThemePrefs must be used inside AppThemeProvider')
  return ctx
}
