/** Thin fetch wrapper: cookie session (BFF), CSRF header on unsafe methods, RFC 7807 problem errors with correlation id. */
export const isPlatform = window.location.hostname.startsWith('admin.')
export const authBase = isPlatform ? '/api/v1/platform/auth' : '/api/v1/auth'

let csrfToken: string | null = null

export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly correlationId?: string

  constructor(status: number, title: string, code?: string, correlationId?: string) {
    super(title)
    this.status = status
    this.code = code
    this.correlationId = correlationId
  }
}

export function setCsrf(token: string | null) {
  csrfToken = token
}

async function ensureCsrf(): Promise<string> {
  if (!csrfToken) {
    const r = await fetch(`${authBase}/csrf`, { credentials: 'include' })
    csrfToken = ((await r.json()) as { token: string }).token
  }
  return csrfToken
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const method = (init.method ?? 'GET').toUpperCase()
  const headers = new Headers(init.headers)
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) headers.set('X-CSRF-TOKEN', await ensureCsrf())

  const r = await fetch(path, { ...init, method, headers, credentials: 'include' })
  if (!r.ok) {
    let p: Record<string, unknown> = {}
    try {
      p = (await r.json()) as Record<string, unknown>
    } catch {
      /* non-JSON error body */
    }
    if (p.code === 'csrf.invalid') setCsrf(null)
    throw new ApiError(r.status, (p.title as string) ?? r.statusText, p.code as string | undefined,
      (p.correlationId as string) ?? r.headers.get('X-Correlation-Id') ?? undefined)
  }
  if (r.status === 204) return undefined as T
  return ((r.headers.get('content-type') ?? '').includes('json') ? await r.json() : await r.text()) as T
}

export const post = <T>(path: string, body?: unknown) =>
  api<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })

export type Paged<T> = T[] | { items: T[]; total?: number }
export const itemsOf = <T,>(p: Paged<T> | undefined): T[] => (!p ? [] : Array.isArray(p) ? p : p.items)

export interface NavigationItem {
  code: string
  title: string
  route?: string | null
  icon?: string | null
  order: number
  moduleCode: string
  children: NavigationItem[]
}

export interface Me {
  userId: string
  email: string
  displayName: string
}
