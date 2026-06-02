import { APIRequestContext, expect } from '@playwright/test'
import { API_BASE, tenantHeaders } from './api'

// These helpers follow one rule: reuse what's already in the tenant, and only
// create or adjust data when nothing suitable exists. That keeps the suite
// runnable without re-seeding, and stops it from piling up junk every run.

const HOUR = 3600 * 1000
const DAY = 24 * HOUR

type EventLite = {
  id: string
  title: string
  startsAtUtc: string
  endsAtUtc: string
  eligiblePasses?: { id: string; isActive: boolean }[]
  [k: string]: any
}

async function listEvents(request: APIRequestContext): Promise<EventLite[]> {
  const fromUtc = new Date(Date.now() - 30 * DAY).toISOString()
  const toUtc = new Date(Date.now() + 365 * DAY).toISOString()
  const res = await request.get(`${API_BASE}/Event`, {
    params: { fromUtc, toUtc },
    headers: tenantHeaders(),
  })
  expect(res.ok(), `List events failed (${res.status()})`).toBeTruthy()
  return (await res.json()).data ?? []
}

const isPurchasable = (e: EventLite) => (e.eligiblePasses ?? []).some(p => p.isActive)

/**
 * Return an event that starts in the future and has at least one active pass, so
 * the public BuyPass flow has something to sell. Strategy, in order:
 *   1. Reuse a future purchasable event if one exists.
 *   2. Otherwise bump a past purchasable event's dates into the future (PUT).
 *   3. Otherwise create one from the first event type + active pass products.
 * No seeding required, and re-running just reuses what it made last time.
 */
export async function ensureFuturePurchasableEvent(
  request: APIRequestContext,
  token: string,
): Promise<EventLite> {
  const events = await listEvents(request)
  const soon = Date.now() + 2 * HOUR

  const future = events.find(e => isPurchasable(e) && new Date(e.startsAtUtc).getTime() > soon)
  if (future) return future

  const stale = events.find(isPurchasable)
  if (stale) return bumpEventDates(request, token, stale)

  return createPurchasableEvent(request, token)
}

/** Move an event's start/end into the future, preserving its pass eligibility. */
async function bumpEventDates(
  request: APIRequestContext,
  token: string,
  e: EventLite,
): Promise<EventLite> {
  const startsAtUtc = new Date(Date.now() + 7 * DAY).toISOString()
  const endsAtUtc = new Date(Date.now() + 7 * DAY + 4 * HOUR).toISOString()
  const res = await request.put(`${API_BASE}/Event/${e.id}`, {
    headers: tenantHeaders(token),
    data: {
      eventTypeId: e.eventTypeId,
      title: e.title,
      description: e.description ?? null,
      startsAtUtc,
      endsAtUtc,
      allDay: false,
      capacity: e.capacity ?? 50,
      locationLabel: e.locationLabel ?? null,
      status: 'scheduled',
      requiresRiderWaiver: e.requiresRiderWaiver ?? false,
      requiresSpectatorWaiver: e.requiresSpectatorWaiver ?? false,
      spectatorWaiverId: e.spectatorWaiverId ?? null,
      racerWaiverId: e.racerWaiverId ?? null,
      imageUrl: e.imageUrl ?? null,
      // Resend eligibility so the PUT does not clear it.
      eligiblePassProductIds: (e.eligiblePasses ?? []).map(p => p.id),
    },
  })
  expect(res.ok(), `Bump event dates failed (${res.status()}): ${await res.text()}`).toBeTruthy()
  return { ...e, startsAtUtc, endsAtUtc }
}

async function createPurchasableEvent(
  request: APIRequestContext,
  token: string,
): Promise<EventLite> {
  const types = await request.get(`${API_BASE}/EventType`, { headers: tenantHeaders(token) })
  expect(types.ok(), `List event types failed (${types.status()})`).toBeTruthy()
  const eventTypeId = (await types.json()).data?.[0]?.id
  expect(eventTypeId, 'No event type exists to attach the event to').toBeTruthy()

  const products = await request.get(`${API_BASE}/PassProduct`, { headers: tenantHeaders(token) })
  expect(products.ok(), `List pass products failed (${products.status()})`).toBeTruthy()
  const productIds: string[] = ((await products.json()).data ?? []).map((p: any) => p.id)
  expect(productIds.length, 'No active pass products to make the event purchasable').toBeGreaterThan(0)

  const startsAtUtc = new Date(Date.now() + 7 * DAY).toISOString()
  const endsAtUtc = new Date(Date.now() + 7 * DAY + 4 * HOUR).toISOString()
  const res = await request.post(`${API_BASE}/Event`, {
    headers: tenantHeaders(token),
    data: {
      eventTypeId,
      title: '[e2e] Purchasable event',
      description: 'Created by the e2e suite so the buy flow always has a target. [e2e]',
      startsAtUtc,
      endsAtUtc,
      allDay: false,
      capacity: 50,
      locationLabel: 'E2E',
      status: 'scheduled',
      requiresRiderWaiver: false,
      requiresSpectatorWaiver: false,
      eligiblePassProductIds: productIds,
    },
  })
  expect(res.ok(), `Create event failed (${res.status()}): ${await res.text()}`).toBeTruthy()
  // POST returns the created event; re-list to get the eligiblePasses shape.
  const id = (await res.json()).data?.id
  const created = (await listEvents(request)).find(e => e.id === id)
  return created ?? { id, title: '[e2e] Purchasable event', startsAtUtc, endsAtUtc }
}

/**
 * Return an active coupon with the given code, creating it if missing. Code match
 * is case-insensitive to mirror how coupon codes are compared server-side.
 */
export async function ensureCoupon(
  request: APIRequestContext,
  token: string,
  code: string,
  percentOff = 10,
) {
  const list = await request.get(`${API_BASE}/Coupon`, { headers: tenantHeaders(token) })
  expect(list.ok(), `List coupons failed (${list.status()})`).toBeTruthy()
  const existing = ((await list.json()).data ?? []).find(
    (c: any) => c.code?.toLowerCase() === code.toLowerCase(),
  )
  if (existing) return existing

  const res = await request.post(`${API_BASE}/Coupon`, {
    headers: tenantHeaders(token),
    data: {
      code,
      description: '[e2e] reusable test coupon',
      discountKind: 'percent',
      discountValue: percentOff * 100, // bps: 10% -> 1000
      applicableScope: 'all',
      applicableEventId: null,
      validFromUtc: null,
      validToUtc: null,
      maxTotalUses: null,
      maxUsesPerUser: null,
      isActive: true,
    },
  })
  expect(res.ok(), `Create coupon failed (${res.status()}): ${await res.text()}`).toBeTruthy()
  return (await res.json()).data
}

/** Best-effort cleanup used by self-cleaning tests; ignores already-gone rows. */
export async function deleteEvent(request: APIRequestContext, token: string, id: string) {
  await request.delete(`${API_BASE}/Event/${id}`, { headers: tenantHeaders(token) }).catch(() => {})
}
