import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, TextField } from '@mui/material'
import { useState, type ReactNode } from 'react'

/** Confirmation dialog requiring an audit reason — every platform mutation records one. */
export function ReasonDialog({ open, title, description, confirmLabel = 'Confirm', extra, danger, busy, onClose, onConfirm }: {
  open: boolean
  title: string
  description: string
  confirmLabel?: string
  extra?: ReactNode
  danger?: boolean
  busy?: boolean
  onClose: () => void
  onConfirm: (reason: string) => void
}) {
  const [reason, setReason] = useState('')
  const valid = reason.trim().length >= 3
  const close = () => { setReason(''); onClose() }
  return (
    <Dialog open={open} onClose={busy ? undefined : close} maxWidth="xs" fullWidth>
      <DialogTitle sx={{ fontWeight: 600 }}>{title}</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>{description}</DialogContentText>
        {extra}
        <TextField
          label="Reason (recorded in audit log)" value={reason} required fullWidth
          onChange={(e) => setReason(e.target.value)}
          helperText={valid ? ' ' : 'Required — at least 3 characters'}
          sx={{ mt: 1 }}
        />
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        <Button onClick={close} disabled={busy}>Cancel</Button>
        <Button variant="contained" color={danger ? 'error' : 'primary'} disabled={!valid || busy}
          onClick={() => { onConfirm(reason.trim()); setReason('') }}>
          {busy ? 'Working…' : confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
