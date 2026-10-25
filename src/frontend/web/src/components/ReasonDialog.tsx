import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, TextField } from '@mui/material'
import { useState, type ReactNode } from 'react'

/** Confirmation dialog requiring an audit reason — every platform mutation records one. */
export function ReasonDialog({ open, title, description, confirmLabel = 'Confirm', extra, onClose, onConfirm }: {
  open: boolean
  title: string
  description: string
  confirmLabel?: string
  extra?: ReactNode
  onClose: () => void
  onConfirm: (reason: string) => void
}) {
  const [reason, setReason] = useState('')
  const valid = reason.trim().length >= 3
  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>{description}</DialogContentText>
        {extra}
        <TextField
          label="Reason (recorded in audit log)" value={reason} required fullWidth
          onChange={(e) => setReason(e.target.value)} sx={{ mt: 1 }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!valid} onClick={() => { onConfirm(reason.trim()); setReason('') }}>{confirmLabel}</Button>
      </DialogActions>
    </Dialog>
  )
}
