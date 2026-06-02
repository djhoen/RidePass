import { APIRequestContext, expect } from '@playwright/test'
import { ADMIN } from '../helpers'

// The SPA talks to the API at VITE_API_ENDPOINT. In dev that's http://localhost:5070/api
// (Kestrel also serves https://localhost:7293/api). Override with E2E_API_BASE if yours differs.
export const API_BASE = process.env.E2E_API_BASE || 'http://localhost:5070/api'

// The tenant is resolved from the subdomain in production, but the dev API has no
// subdomain on localhost, so TenantResolutionMiddleware falls back to this header
// (see webapi/Middleware/TenantResolutionMiddleware.cs). The SPA sends it on every
// request; we replicate that for direct API setup calls.
export const TENANT = process.env.E2E_TENANT || 'acme'

// Where auth.setup.ts saves the reusable signed-in session. Lives here (a non-test
// module) so playwright.config.ts can import it without pulling in a test file.
export const ADMIN_STATE = 'tests/.auth/admin.json'

export function tenantHeaders(token?: string): Record<string, string> {
  const h: Record<string, string> = { 'X-Tenant-Subdomain': TENANT }
  if (token) h['Authorization'] = `Bearer ${token}`
  return h
}

/**
 * Authenticate against the real login endpoint and return the JWT + identity.
 * Used both by auth.setup.ts (to seed a reusable browser session) and by specs
 * that set up state through the API.
 */
export async function loginViaApi(
  request: APIRequestContext,
  email = ADMIN.email,
  password = ADMIN.password,
): Promise<{ token: string; userId: string; role: string }> {
  const res = await request.post(`${API_BASE}/User/Login`, {
    data: { email, password },
    headers: tenantHeaders(),
  })
  expect(res.ok(), `Login failed (${res.status()}): ${await res.text()}`).toBeTruthy()
  const body = await res.json()
  const d = body.data
  return { token: d.token, userId: d.userId, role: d.role }
}
