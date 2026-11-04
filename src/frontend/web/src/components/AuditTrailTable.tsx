import { ExpandMore } from '@mui/icons-material'
import {
  Box, Chip, Collapse, IconButton, Table, TableBody, TableCell, TableHead, TableRow, Typography,
} from '@mui/material'
import { Fragment, useState } from 'react'

export interface AuditFieldChange {
  field: string
  oldValue?: string | null
  newValue?: string | null
  oldLabel?: string | null
  newLabel?: string | null
  masked: boolean
}

export interface AuditEntry {
  id: string
  occurredAt: string
  moduleCode: string
  entityType: string
  entityId: string
  entityLabel?: string | null
  action: string
  actorType: string
  actorId?: string | null
  actorDisplay?: string | null
  source: string
  reason?: string | null
  ipAddress?: string | null
  correlationId?: string | null
  fields: AuditFieldChange[]
}

const short = (id: string) => (id.length > 8 ? `${id.slice(0, 8)}…` : id)

function FieldValue({ value, label, masked }: { value?: string | null; label?: string | null; masked: boolean }) {
  if (masked) return <Typography variant="body2" color="text.secondary">••••••</Typography>
  if (value == null) return <Typography variant="body2" color="text.disabled">—</Typography>
  return (
    <Box>
      {label && <Typography variant="body2" sx={{ fontWeight: 500 }}>{label}</Typography>}
      <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12 }} color={label ? 'text.secondary' : 'text.primary'}>
        {label ? short(value) : value}
      </Typography>
    </Box>
  )
}

const ACTION_COLOR: Record<string, 'success' | 'warning' | 'error' | 'default'> = {
  create: 'success', update: 'warning', delete: 'error',
}

function Row({ entry }: { entry: AuditEntry }) {
  const [open, setOpen] = useState(false)
  return (
    <Fragment>
      <TableRow hover onClick={() => setOpen(!open)} sx={{ cursor: 'pointer' }}>
        <TableCell padding="checkbox">
          <IconButton size="small" aria-label="expand" sx={{ transform: open ? 'rotate(180deg)' : 'none', transition: 'transform .15s' }}>
            <ExpandMore fontSize="small" />
          </IconButton>
        </TableCell>
        <TableCell sx={{ whiteSpace: 'nowrap' }}><Typography variant="body2">{new Date(entry.occurredAt).toLocaleString()}</Typography></TableCell>
        <TableCell><Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{entry.moduleCode}</Typography></TableCell>
        <TableCell><Chip size="small" label={entry.action} color={ACTION_COLOR[entry.action] ?? 'default'} variant="outlined" /></TableCell>
        <TableCell>
          <Typography variant="body2" sx={{ fontWeight: 500 }}>{entry.entityType}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ fontSize: 12 }}>
            {entry.entityLabel ?? short(entry.entityId)}
          </Typography>
        </TableCell>
        <TableCell>{entry.actorDisplay ?? entry.actorType}</TableCell>
        <TableCell><Typography variant="body2" color="text.secondary">{entry.source}</Typography></TableCell>
      </TableRow>
      <TableRow>
        <TableCell colSpan={7} sx={{ p: 0, borderBottom: open ? undefined : 0 }}>
          <Collapse in={open} unmountOnExit>
            <Box sx={{ px: 4, py: 2, bgcolor: 'action.hover' }}>
              {(entry.reason || entry.ipAddress) && (
                <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 1 }}>
                  {entry.reason ? `Reason: ${entry.reason} · ` : ''}{entry.ipAddress ? `IP ${entry.ipAddress}` : ''}
                </Typography>
              )}
              {entry.fields.length === 0 ? (
                <Typography variant="body2" color="text.secondary">No field-level changes recorded.</Typography>
              ) : (
                <Table size="small">
                  <TableHead>
                    <TableRow><TableCell>Field</TableCell><TableCell>Old value</TableCell><TableCell>New value</TableCell></TableRow>
                  </TableHead>
                  <TableBody>
                    {entry.fields.map((f) => (
                      <TableRow key={f.field}>
                        <TableCell sx={{ width: 220 }}><Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: 12.5 }}>{f.field}</Typography></TableCell>
                        <TableCell><FieldValue value={f.oldValue} label={f.oldLabel} masked={f.masked} /></TableCell>
                        <TableCell><FieldValue value={f.newValue} label={f.newLabel} masked={f.masked} /></TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </Box>
          </Collapse>
        </TableCell>
      </TableRow>
    </Fragment>
  )
}

export function AuditTrailTable({ entries }: { entries: AuditEntry[] }) {
  return (
    <Table size="small">
      <TableHead>
        <TableRow>
          <TableCell padding="checkbox" />
          <TableCell>When</TableCell><TableCell>Module</TableCell><TableCell>Action</TableCell>
          <TableCell>Record</TableCell><TableCell>Actor</TableCell><TableCell>Source</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {entries.map((e) => <Row key={e.id} entry={e} />)}
      </TableBody>
    </Table>
  )
}
