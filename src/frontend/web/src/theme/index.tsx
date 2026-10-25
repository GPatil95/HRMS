import { CssBaseline, ThemeProvider, createTheme, useMediaQuery, type Theme } from '@mui/material'
import { useMemo, useState, type ReactNode } from 'react'
import { ACCENT_KEY, MODE_KEY, PrefsContext, accents, readAccent, readMode, type Accent, type ThemeMode } from './tokens'

function buildTheme(mode: 'light' | 'dark', accent: Accent): Theme {
  const a = accents[accent]
  const dark = mode === 'dark'
  return createTheme({
    palette: {
      mode,
      primary: { main: a.main, dark: a.dark, light: a.light, contrastText: '#FFFFFF' },
      background: dark
        ? { default: '#0C0E14', paper: '#12151F' }
        : { default: '#F6F7F9', paper: '#FFFFFF' },
      divider: dark ? 'rgba(148, 163, 184, 0.14)' : 'rgba(15, 23, 42, 0.09)',
    },
    shape: { borderRadius: 10 },
    typography: {
      fontFamily: '"Inter", "Segoe UI", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", Arial, sans-serif',
      button: { textTransform: 'none', fontWeight: 600 },
      h5: { fontWeight: 650 },
      h6: { fontWeight: 650 },
      subtitle1: { fontWeight: 600 },
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
        },
      },
      MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { borderRadius: 8 } } },
      MuiPaper: {
        styleOverrides: {
          outlined: { borderColor: dark ? 'rgba(148,163,184,0.16)' : 'rgba(15,23,42,0.10)' },
        },
      },
      MuiTableCell: {
        styleOverrides: {
          head: {
            fontWeight: 600,
            fontSize: 11.5,
            letterSpacing: 0.6,
            textTransform: 'uppercase',
            color: dark ? '#94A3B8' : '#64748B',
            borderBottom: `1px solid ${dark ? 'rgba(148,163,184,0.16)' : 'rgba(15,23,42,0.10)'}`,
            background: dark ? 'rgba(148,163,184,0.04)' : 'rgba(15,23,42,0.02)',
          },
          root: { borderBottom: `1px solid ${dark ? 'rgba(148,163,184,0.10)' : 'rgba(15,23,42,0.07)'}` },
        },
      },
      MuiTableRow: {
        styleOverrides: {
          root: {
            transition: 'background-color 120ms ease',
            '&.MuiTableRow-hover:hover': {
              backgroundColor: dark ? 'rgba(148,163,184,0.06)' : 'rgba(15,23,42,0.03)',
            },
          },
        },
      },
      MuiChip: { styleOverrides: { root: { fontWeight: 600, borderRadius: 6 } } },
      MuiListItemButton: {
        styleOverrides: {
          root: { borderRadius: 8, transition: 'background-color 120ms ease, color 120ms ease' },
        },
      },
      MuiDialog: { styleOverrides: { paper: { borderRadius: 14, backgroundImage: 'none' } } },
      MuiMenu: { styleOverrides: { paper: { backgroundImage: 'none' } } },
      MuiDrawer: { styleOverrides: { paper: { backgroundImage: 'none' } } },
      MuiAlert: { styleOverrides: { root: { borderRadius: 10 } } },
      MuiTooltip: { styleOverrides: { tooltip: { borderRadius: 6, fontWeight: 500 } } },
    },
  })
}

export function AppThemeProvider({ children }: { children: ReactNode }) {
  const prefersDark = useMediaQuery('(prefers-color-scheme: dark)')
  const [mode, setModeState] = useState<ThemeMode>(readMode)
  const [accent, setAccentState] = useState<Accent>(readAccent)

  const setMode = (m: ThemeMode) => { setModeState(m); localStorage.setItem(MODE_KEY, m) }
  const setAccent = (a: Accent) => { setAccentState(a); localStorage.setItem(ACCENT_KEY, a) }

  const resolved = mode === 'system' ? (prefersDark ? 'dark' : 'light') : mode
  const theme = useMemo(() => buildTheme(resolved, accent), [resolved, accent])
  const prefs = useMemo(() => ({ mode, accent, setMode, setAccent }), [mode, accent])

  return (
    <PrefsContext.Provider value={prefs}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
      </ThemeProvider>
    </PrefsContext.Provider>
  )
}
