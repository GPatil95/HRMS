import { Alert, Box, Button, Paper, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Component, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, api, isPlatform, itemsOf, type Paged } from '../api/client'

type Row = Record<string, unknown>

export function QueryError({ error }: { error: unknown }) {
  if (error instanceof ApiError && error.status === 403) return <ForbiddenPage />
  return (
    <Alert severity="error">
      {error instanceof ApiError ? error.message : 'Unexpected error.'}
      {error instanceof ApiError && error.correlationId && <> (reference: {error.correlationId})</>}
    </Alert>
  )
}

function DataPage({ title, url, columns }: { title: string; url: string; columns: string[] }) {
  const q = useQuery({ queryKey: [url], queryFn: () => api<Paged<Row>>(url) })
  return (
    <Box>
      <Typography variant="h5" gutterBottom>{title}</Typography>
      {q.error ? <QueryError error={q.error} /> : (
        <Paper>
          <Table size="small">
            <TableHead><TableRow>{columns.map((c) => <TableCell key={c}>{c}</TableCell>)}</TableRow></TableHead>
            <TableBody>
              {itemsOf(q.data).map((r, i) => (
                <TableRow key={String(r.id ?? i)}>{columns.map((c) => <TableCell key={c}>{String(r[c] ?? '')}</TableCell>)}</TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
    </Box>
  )
}

export const UsersPage = () => <DataPage title="Users" url="/api/v1/users" columns={['email', 'displayName', 'status']} />
export const TenantsPage = () => <DataPage title="Tenants" url="/api/v1/platform/tenants" columns={['code', 'name', 'status']} />

export function HomePage() {
  const q = useQuery({ queryKey: ['dashboard'], queryFn: () => api<Row>('/api/v1/dashboard'), enabled: !isPlatform })
  return (
    <Box>
      <Typography variant="h5" gutterBottom>{isPlatform ? 'Platform overview' : 'Dashboard'}</Typography>
      {q.error && <QueryError error={q.error} />}
      {Array.isArray(q.data?.widgets) && (
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 2 }}>
          {(q.data.widgets as Row[]).map((w, i) => (
            <Paper key={String(w.code ?? i)} sx={{ p: 2 }}><Typography variant="subtitle1">{String(w.title ?? w.code)}</Typography></Paper>
          ))}
        </Box>
      )}
    </Box>
  )
}

export function NotFoundPage() {
  return (
    <Box sx={{ p: 4 }}>
      <Typography variant="h4" gutterBottom>Page not found</Typography>
      <Button component={Link} to={isPlatform ? '/admin' : '/'}>Go home</Button>
    </Box>
  )
}

export function ForbiddenPage() {
  return (
    <Box sx={{ p: 4 }}>
      <Typography variant="h4" gutterBottom>Access denied</Typography>
      <Typography>You do not have permission to view this page, or the module is not enabled for your organisation.</Typography>
    </Box>
  )
}

export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  render() {
    return this.state.failed ? (
      <Box sx={{ p: 4 }}><Typography variant="h4">Something went wrong</Typography><Button onClick={() => location.reload()}>Reload</Button></Box>
    ) : this.props.children
  }
}
