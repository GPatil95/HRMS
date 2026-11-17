import { useState, type ReactNode } from 'react'
import {
  Box, Paper, Table, TableBody, TableCell, TableContainer, TableHead,
  TablePagination, TableRow, useMediaQuery, useTheme,
} from '@mui/material'

/**
 * Shared tenant data table: sticky header, row hover, client-side pagination,
 * and an automatic mobile card layout (renderCard) under `sm`.
 */
export function DataList<T>({ columns, rows, rowKey, renderRow, renderCard, pageSize = 10, onRowClick }: {
  columns: ReactNode[]
  rows: T[]
  rowKey: (row: T) => string | number
  renderRow: (row: T) => ReactNode[]
  renderCard?: (row: T) => ReactNode
  pageSize?: number
  onRowClick?: (row: T) => void
}) {
  const theme = useTheme()
  const mobile = useMediaQuery(theme.breakpoints.down('sm'))
  const [page, setPage] = useState(0)
  const [size, setSize] = useState(pageSize)
  const paged = rows.slice(page * size, page * size + size)

  const cardMode = mobile && !!renderCard
  return (
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      {cardMode ? (
        <Box sx={{ p: 1 }}>
          {paged.map((r) => (
            <Box key={rowKey(r)} onClick={onRowClick ? () => onRowClick(r) : undefined}
              sx={{ py: 1.5, px: 1.5, borderRadius: 1.5, cursor: onRowClick ? 'pointer' : 'default', '&:hover': onRowClick ? { bgcolor: 'action.hover' } : undefined, '& + &': { borderTop: 1, borderColor: 'divider', borderRadius: 0 } }}>
              {renderCard(r)}
            </Box>
          ))}
        </Box>
      ) : (
        <TableContainer sx={{ overflowX: 'auto' }}>
          <Table size="small" stickyHeader>
            <TableHead>
              <TableRow>
                {columns.map((c, i) => (
                  <TableCell key={i} sx={{ bgcolor: 'background.paper', fontWeight: 650, fontSize: 12, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: 0.5, whiteSpace: 'nowrap' }}>{c}</TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {paged.map((r) => (
                <TableRow key={rowKey(r)} hover onClick={onRowClick ? () => onRowClick(r) : undefined}
                  sx={{ cursor: onRowClick ? 'pointer' : 'default', '&:last-child td': { borderBottom: 0 } }}>
                  {renderRow(r).map((cell, i) => <TableCell key={i} sx={{ py: 1.5 }}>{cell}</TableCell>)}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {rows.length > pageSize && (
        <TablePagination
          component="div"
          count={rows.length}
          page={page}
          onPageChange={(_, p) => setPage(p)}
          rowsPerPage={size}
          onRowsPerPageChange={(e) => { setSize(parseInt(e.target.value, 10)); setPage(0) }}
          rowsPerPageOptions={[10, 25, 50]}
          sx={{ borderTop: 1, borderColor: 'divider' }}
        />
      )}
    </Paper>
  )
}
