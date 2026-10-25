import { CssBaseline, ThemeProvider, alpha, createTheme, useMediaQuery, type Theme } from '@mui/material'
import { useMemo, useState, type ReactNode } from 'react'
import {
  ACCENT_KEY, CONTAINER_KEY, MODE_KEY, NAVBAR_KEY, NAVSTYLE_KEY, PrefsContext, SIDEBAR_KEY,
  accents, readAccent, readContainer, readMode, readNavbar, readNavStyle, readSidebar,
  type Accent, type ContainerWidth, type NavStyle, type NavbarMode, type SidebarMode, type ThemeMode,
} from './tokens'

function buildTheme(mode: 'light' | 'dark', accent: Accent): Theme {
  const a = accents[accent]
  const dark = mode === 'dark'
  const border = dark ? 'rgba(148, 163, 184, 0.15)' : 'rgba(29, 41, 57, 0.10)'
  const borderSoft = dark ? 'rgba(148, 163, 184, 0.09)' : 'rgba(29, 41, 57, 0.06)'
  return createTheme({
    palette: {
      mode,
      primary: { main: a.main, dark: a.dark, light: a.light, contrastText: '#FFFFFF' },
      background: dark
        ? { default: '#0F1319', paper: '#151B24' }
        : { default: '#F5F7FB', paper: '#FFFFFF' },
      text: dark
        ? { primary: '#E6E9EF', secondary: '#98A2B3', disabled: '#667085' }
        : { primary: '#1D2939', secondary: '#667085', disabled: '#98A2B3' },
      success: { main: dark ? '#4ADE80' : '#2FB344' },
      warning: { main: dark ? '#FBBF24' : '#F59E0B' },
      error: { main: dark ? '#F87171' : '#D63939' },
      info: { main: dark ? '#60A5FA' : '#4299E1' },
      divider: borderSoft,
    },
    shape: { borderRadius: 8 },
    typography: {
      fontFamily: '"Inter", "Segoe UI", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", Arial, sans-serif',
      fontSize: 13.5,
      button: { textTransform: 'none', fontWeight: 600 },
      h4: { fontWeight: 700, fontSize: '1.5rem' },
      h5: { fontWeight: 650, fontSize: '1.25rem' },
      h6: { fontWeight: 650, fontSize: '1.05rem' },
      subtitle1: { fontWeight: 600, fontSize: '0.9rem' },
      subtitle2: { fontWeight: 600, fontSize: '0.8rem' },
      body2: { fontSize: '0.8125rem' },
      caption: { fontSize: '0.72rem' },
    },
    transitions: {
      duration: { shortest: 150, shorter: 200, short: 250 },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          '@media (prefers-reduced-motion: reduce)': {
            '*, *::before, *::after': {
              animationDuration: '0.01ms !important',
              animationIterationCount: '1 !important',
              transitionDuration: '0.01ms !important',
            },
          },
          '::selection': { backgroundColor: alpha(a.main, 0.2) },
        },
      },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: { borderRadius: 6, fontWeight: 600 },
          outlined: { borderColor: border, color: dark ? '#E6E9EF' : '#344054' },
        },
      },
      MuiCard: { styleOverrides: { root: { backgroundImage: 'none' } } },
      MuiPaper: {
        styleOverrides: {
          root: { backgroundImage: 'none' },
          outlined: {
            borderColor: border,
            boxShadow: dark ? '0 1px 2px rgba(0,0,0,0.3)' : '0 1px 2px rgba(29,41,57,0.04)',
          },
        },
      },
      MuiTableCell: {
        styleOverrides: {
          head: {
            fontWeight: 600,
            fontSize: 11,
            letterSpacing: 0.5,
            textTransform: 'uppercase',
            color: dark ? '#98A2B3' : '#667085',
            borderBottom: `1px solid ${border}`,
            background: dark ? 'rgba(148,163,184,0.03)' : '#FBFCFE',
            paddingTop: 10, paddingBottom: 10,
          },
          root: {
            borderBottom: `1px solid ${borderSoft}`,
            paddingTop: 10, paddingBottom: 10,
          },
        },
      },
      MuiTableRow: {
        styleOverrides: {
          root: {
            transition: 'background-color 150ms ease',
            '&.MuiTableRow-hover:hover': {
              backgroundColor: dark ? 'rgba(148,163,184,0.05)' : 'rgba(32,107,196,0.03)',
            },
          },
        },
      },
      MuiChip: { styleOverrides: { root: { fontWeight: 600, borderRadius: 6 } } },
      MuiListItemButton: {
        styleOverrides: {
          root: { borderRadius: 6, transition: 'background-color 150ms ease, color 150ms ease' },
        },
      },
      MuiDialog: { styleOverrides: { paper: { borderRadius: 10 } } },
      MuiPopover: { styleOverrides: { paper: { borderRadius: 10, border: `1px solid ${borderSoft}` } } },
      MuiMenu: { styleOverrides: { paper: { borderRadius: 10 } } },
      MuiDrawer: { styleOverrides: { paper: { backgroundImage: 'none' } } },
      MuiAlert: { styleOverrides: { root: { borderRadius: 8 } } },
      MuiTooltip: {
        styleOverrides: {
          tooltip: { borderRadius: 6, fontWeight: 500, fontSize: 11.5 },
        },
      },
      MuiTextField: {
        defaultProps: { size: 'small' },
      },
      MuiOutlinedInput: {
        styleOverrides: {
          root: { borderRadius: 6 },
          notchedOutline: { borderColor: border },
        },
      },
      MuiSwitch: {
        styleOverrides: {
          switchBase: { '&.Mui-checked + .MuiSwitch-track': { opacity: 1 } },
        },
      },
    },
  })
}

export function AppThemeProvider({ children }: { children: ReactNode }) {
  const prefersDark = useMediaQuery('(prefers-color-scheme: dark)')
  const [mode, setModeState] = useState<ThemeMode>(readMode)
  const [accent, setAccentState] = useState<Accent>(readAccent)
  const [sidebar, setSidebarState] = useState<SidebarMode>(readSidebar)
  const [container, setContainerState] = useState<ContainerWidth>(readContainer)
  const [navbar, setNavbarState] = useState<NavbarMode>(readNavbar)
  const [navStyle, setNavStyleState] = useState<NavStyle>(readNavStyle)

  const setMode = (v: ThemeMode) => { setModeState(v); localStorage.setItem(MODE_KEY, v) }
  const setAccent = (v: Accent) => { setAccentState(v); localStorage.setItem(ACCENT_KEY, v) }
  const setSidebar = (v: SidebarMode) => { setSidebarState(v); localStorage.setItem(SIDEBAR_KEY, v) }
  const setContainer = (v: ContainerWidth) => { setContainerState(v); localStorage.setItem(CONTAINER_KEY, v) }
  const setNavbar = (v: NavbarMode) => { setNavbarState(v); localStorage.setItem(NAVBAR_KEY, v) }
  const setNavStyle = (v: NavStyle) => { setNavStyleState(v); localStorage.setItem(NAVSTYLE_KEY, v) }

  const resolved = mode === 'system' ? (prefersDark ? 'dark' : 'light') : mode
  const theme = useMemo(() => buildTheme(resolved, accent), [resolved, accent])
  const prefs = useMemo(
    () => ({ mode, accent, sidebar, container, navbar, navStyle, setMode, setAccent, setSidebar, setContainer, setNavbar, setNavStyle }),
    [mode, accent, sidebar, container, navbar, navStyle],
  )

  return (
    <PrefsContext.Provider value={prefs}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
      </ThemeProvider>
    </PrefsContext.Provider>
  )
}
