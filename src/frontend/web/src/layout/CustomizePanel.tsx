import {
  Box, Divider, Drawer, IconButton, ToggleButton, ToggleButtonGroup, Tooltip, Typography, alpha, useTheme,
} from '@mui/material'
import { Brightness4, Check, Close, DarkMode, LightMode, SettingsSuggest } from '@mui/icons-material'
import type { ReactNode } from 'react'
import { accentKeys, accents, useThemePrefs, type Accent, type ContainerWidth, type NavStyle, type NavbarMode, type SidebarMode, type ThemeMode } from '../theme/tokens'

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Box sx={{ mb: 3 }}>
      <Typography variant="overline" sx={{ color: 'text.secondary', fontSize: 10.5, letterSpacing: 1.2, fontWeight: 700, display: 'block', mb: 1 }}>
        {title}
      </Typography>
      {children}
    </Box>
  )
}

function Options<T extends string>({ value, onChange, items }: { value: T; onChange: (v: T) => void; items: { value: T; label: string; icon?: ReactNode }[] }) {
  return (
    <ToggleButtonGroup
      exclusive fullWidth size="small" value={value}
      onChange={(_, v) => { if (v) onChange(v as T) }}
      sx={{ '& .MuiToggleButton-root': { textTransform: 'none', fontWeight: 600, fontSize: 12, py: 0.75, gap: 0.75 } }}
    >
      {items.map((i) => <ToggleButton key={i.value} value={i.value}>{i.icon}{i.label}</ToggleButton>)}
    </ToggleButtonGroup>
  )
}

export function CustomizePanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const theme = useTheme()
  const prefs = useThemePrefs()
  return (
    <Drawer anchor="right" open={open} onClose={onClose} slotProps={{ paper: { sx: { width: 320 } } }}>
      <Box sx={{ px: 3, py: 2, display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Box>
          <Typography variant="subtitle1">Customize</Typography>
          <Typography variant="caption" color="text.secondary">Saved to this browser</Typography>
        </Box>
        <IconButton onClick={onClose} aria-label="Close customization" size="small"><Close /></IconButton>
      </Box>
      <Divider />
      <Box sx={{ px: 3, py: 3, overflowY: 'auto' }}>
        <Section title="Appearance">
          <Options<ThemeMode>
            value={prefs.mode} onChange={prefs.setMode}
            items={[
              { value: 'light', label: 'Light', icon: <LightMode sx={{ fontSize: 16 }} /> },
              { value: 'dark', label: 'Dark', icon: <DarkMode sx={{ fontSize: 16 }} /> },
              { value: 'system', label: 'Auto', icon: <SettingsSuggest sx={{ fontSize: 16 }} /> },
            ]}
          />
        </Section>

        <Section title="Accent color">
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 1 }}>
            {accentKeys.map((k: Accent) => (
              <Tooltip key={k} title={accents[k].label}>
                <IconButton
                  onClick={() => prefs.setAccent(k)} aria-label={`Accent ${accents[k].label}`}
                  sx={{
                    width: '100%', aspectRatio: '1', borderRadius: 2, bgcolor: accents[k].main,
                    border: prefs.accent === k ? `2px solid ${theme.palette.text.primary}` : '2px solid transparent',
                    '&:hover': { bgcolor: accents[k].dark },
                  }}
                >
                  {prefs.accent === k && <Check sx={{ color: '#fff', fontSize: 18 }} />}
                </IconButton>
              </Tooltip>
            ))}
          </Box>
        </Section>

        <Section title="Sidebar">
          <Options<SidebarMode>
            value={prefs.sidebar} onChange={prefs.setSidebar}
            items={[
              { value: 'expanded', label: 'Expanded' },
              { value: 'folded', label: 'Folded' },
              { value: 'hover', label: 'On hover' },
            ]}
          />
        </Section>

        <Section title="Navigation style">
          <Options<NavStyle>
            value={prefs.navStyle} onChange={prefs.setNavStyle}
            items={[{ value: 'attached', label: 'Attached' }, { value: 'floating', label: 'Floating' }]}
          />
        </Section>

        <Section title="Navbar">
          <Options<NavbarMode>
            value={prefs.navbar} onChange={prefs.setNavbar}
            items={[{ value: 'sticky', label: 'Sticky' }, { value: 'static', label: 'Static' }]}
          />
        </Section>

        <Section title="Container">
          <Options<ContainerWidth>
            value={prefs.container} onChange={prefs.setContainer}
            items={[
              { value: 'default', label: 'Default' },
              { value: 'boxed', label: 'Boxed' },
              { value: 'fluid', label: 'Fluid' },
            ]}
          />
        </Section>

        <Divider sx={{ my: 2 }} />
        <Typography variant="caption" color="text.secondary">
          Appearance options apply instantly and persist across sessions on this device.
        </Typography>
      </Box>
    </Drawer>
  )
}

export function CustomizeButton({ onClick }: { onClick: () => void }) {
  const theme = useTheme()
  return (
    <Tooltip title="Customize appearance">
      <IconButton onClick={onClick} aria-label="Customize appearance" sx={{ color: 'text.secondary' }}>
        <Brightness4 sx={{ color: alpha(theme.palette.text.primary, 0) }} style={{ display: 'none' }} />
        <SettingsSuggest />
      </IconButton>
    </Tooltip>
  )
}
