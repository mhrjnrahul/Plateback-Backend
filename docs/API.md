# PlateBack API reference

Version 1. Every endpoint below is under `/api/v1`, for example `http://localhost:5080/api/v1/listings`.

The same API is described as OpenAPI 3 in [`openapi.json`](openapi.json), and while the API runs it can be explored and called from the browser at `http://localhost:5080/scalar` (the raw document is at `/openapi/v1.json`).

Examples are real responses from the demo data (shortened where marked with `...`), except the upload URL, which is illustrative.

## Contents

- [Conventions](#conventions)
- [Errors](#errors)
- [Auth](#auth) · [Users](#users) · [Organizations](#organizations) · [Listings](#listings) · [Reservations](#reservations) · [Notifications](#notifications) · [Stats](#stats) · [Uploads](#uploads) · [Admin](#admin) · [Health](#health)
- [Enumerations](#enumerations)

## Conventions

| Topic | Rule |
| --- | --- |
| Format | JSON, `camelCase` field names, UTF-8. Send `Content-Type: application/json` with a body. |
| Ids | GUID strings, for example `"37781d7a-00f3-3b32-a267-c5401d529f32"`. A path id that is not a GUID matches no route (404). |
| Times | ISO 8601 with offset, stored and returned in UTC: `"2026-10-17T07:00:38+00:00"`. Requests may use any offset. |
| Money | Whole Nepali rupees as integers. `price` is the selling price (0 means free); `originalPrice` is the MRP. |
| Enums | Sent and returned as their names, for example `"SoldOut"`. See [Enumerations](#enumerations). |
| Paging | List endpoints take `page` (from 1, default 1) and `pageSize` (default 20, maximum 50) and return `{ items, page, pageSize, total }`. |
| Authentication | `Authorization: Bearer <accessToken>`. Tokens last 15 minutes; get a new one from `POST /auth/refresh`. |
| Refresh token | Never in a response body. It is set as the `plateback_refresh` cookie: httpOnly, Secure, SameSite=Strict, path `/api/v1/auth`, 7 days, rotated on every use. |
| Correlation id | Send `X-Correlation-Id` to tag a request; it is echoed back and written to the logs. Otherwise the trace id is used. |
| Rate limits | Auth endpoints: 20 requests a minute per IP address. `POST /reservations/verify`: 30 a minute per user. Over the limit: `429 RATE_LIMITED` with `Retry-After`. |

Access levels used below: **Public** (no token), **Signed in** (any valid token), **Member** (owner or staff of that business, checked in the database on every request), **Owner**, **Admin**.

## Errors

Every error, including 401, 403, 404 and 429, is an RFC 9457 ProblemDetails body with content type `application/problem+json` and a machine-readable `code`:

```json
{
  "type": "urn:plateback:error:validation-error",
  "title": "Validation failed",
  "status": 400,
  "code": "VALIDATION_ERROR",
  "errors": { "quantity": ["Choose between 1 and 2."] },
  "traceId": "0HNPC2FTA0F25:00000001"
}
```

`detail` is a sentence a person can read; `errors` (only on `VALIDATION_ERROR`) maps each request field to its messages; `traceId` finds the request in the logs.

| Status | Code | When |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | A field is missing or invalid, the JSON cannot be read, or a business rule about the input is broken (for example a pickup window shorter than 15 minutes). |
| 401 | `UNAUTHENTICATED` | No token, an invalid token, wrong email or password, or an ended session on `/auth/refresh`. |
| 401 | `TOKEN_EXPIRED` | The access token is older than 15 minutes. Call `/auth/refresh`. |
| 403 | `FORBIDDEN` | Signed in, but the account cannot do this (for example a customer calling an admin endpoint). |
| 403 | `NOT_ORG_MEMBER` | Not a member of the business named in the URL. |
| 403 | `ORG_NOT_VERIFIED` | The business is not verified yet, so it cannot publish. |
| 403 | `ACCOUNT_SUSPENDED` | The account is suspended. |
| 404 | `NOT_FOUND` | No such resource, or one the caller is not allowed to know about (reservations of other people, unverified businesses). |
| 409 | `SOLD_OUT` | Not enough stock left, usually because someone else reserved it first. |
| 409 | `LIMIT_REACHED` | The customer already holds this listing, or their maximum of active reservations. |
| 409 | `WINDOW_CLOSED` | The listing or reservation is no longer open (ended, cancelled or a no-show). |
| 409 | `ALREADY_COLLECTED` | Cancelling a reservation that was collected. |
| 409 | `CODE_ALREADY_USED` | A pickup code that was already used. |
| 409 | `INVALID_STATE` | Not allowed in the current state (verifying a verified business, removing the last owner). New since the Week 1 design. |
| 409 | `CONFLICT` | Someone changed the same row at the same moment (row version check). Reload and retry. New since the Week 1 design. |
| 429 | `RATE_LIMITED` | Too many requests, or the account is locked for 15 minutes after five wrong passwords. |
| 500 | `INTERNAL_ERROR` | Unexpected. No internal details are returned; quote the `traceId`. |

## Auth

### POST /auth/register

Public, rate limited. Creates a customer account, signs it in and sets the refresh cookie.

| Field | Type | Rules |
| --- | --- | --- |
| `displayName` | string | 2 to 60 characters |
| `email` | string | valid email, unique (case does not matter) |
| `password` | string | at least 8 characters |

```json
{ "displayName": "Asha Karki", "email": "asha@example.com", "password": "longenough" }
```

**201 Created**, same body as login. **Errors:** 400 `VALIDATION_ERROR` (including "An account with this email already exists."), 429.

### POST /auth/login

Public, rate limited.

```json
{ "email": "maya@example.com", "password": "password123" }
```

**200 OK**

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5...",
  "expiresIn": 900,
  "user": {
    "id": "b5014ecd-f6fd-36a5-8d87-926ea48eb100",
    "email": "maya@example.com",
    "displayName": "Maya Rai",
    "role": "Customer",
    "reliabilityScore": 90,
    "isSuspended": false,
    "memberships": []
  }
}
```

**Errors:** 401 `UNAUTHENTICATED` (wrong email or password, same message for both), 403 `ACCOUNT_SUSPENDED`, 429 `RATE_LIMITED` (five wrong passwords lock the account for 15 minutes, or the IP limit).

### POST /auth/refresh

Uses the `plateback_refresh` cookie; no body. Rotates the refresh token (new cookie) and returns a new access token, same body as login.

Presenting a refresh token that was already rotated means it was copied, so every session of that account is ended. **Errors:** 401 `UNAUTHENTICATED` (missing, expired, revoked or reused token; the cookie is cleared).

### POST /auth/logout

Revokes the refresh token in the cookie and clears the cookie. **204 No Content**, also when there was no cookie.

## Users

### GET /users/me

Signed in. The profile, reliability score and business memberships. Body as `user` in the login response; for a business owner:

```json
"memberships": [
  { "organizationId": "2fef9290-5bec-3976-83a7-6a257eba20b5", "organizationName": "Sunrise Bakery", "role": "Owner" }
]
```

### PATCH /users/me

Signed in. `{ "displayName": "Maya R." }` (2 to 60 characters). **200 OK** with the updated profile.

## Organizations

The organization object:

```json
{
  "id": "2fef9290-5bec-3976-83a7-6a257eba20b5",
  "name": "Sunrise Bakery",
  "type": "Bakery",
  "address": "Thamel Rd 12, Kathmandu",
  "latitude": 27.7154,
  "longitude": 85.3123,
  "timeZone": "Asia/Kathmandu",
  "status": "Verified",
  "createdAt": "2026-08-18T06:00:38+00:00",
  "verifiedAt": "2026-08-18T06:00:38+00:00",
  "rejectionReason": null
}
```

### POST /organizations

Signed in (not suspended). Registers a business; it starts `Pending` and the caller becomes its `Owner`. Admins are notified.

| Field | Type | Rules |
| --- | --- | --- |
| `name` | string | 2 to 100 characters |
| `type` | string | `Bakery`, `Cafe`, `Restaurant`, `Grocer` or `Other` |
| `address` | string | 5 to 200 characters |
| `latitude`, `longitude` | number | required, valid coordinates |
| `phone` | string | optional, up to 30 characters, only admins see it |

**201 Created** with the organization. **Errors:** 400, 401, 403 `ACCOUNT_SUSPENDED`.

### GET /organizations/mine

Signed in. The businesses the caller belongs to, as an array of organizations.

### GET /organizations/{id}

Public for verified businesses; members and admins also see pending, rejected or suspended ones. Anyone else gets **404**.

### PATCH /organizations/{id}

Owner. Same body as POST. **200 OK** with the organization. **Errors:** 400, 403 `FORBIDDEN` (staff), 403 `NOT_ORG_MEMBER`, 404.

### GET /organizations/{id}/members

Owner. `[{ "userId", "displayName", "email", "role", "joinedAt" }]`, owners first.

### POST /organizations/{id}/members

Owner. `{ "email": "sita@example.com" }` adds an existing account as `Staff`. **200 OK** with the updated team. **Errors:** 400 (no account with that email), 403, 409 `INVALID_STATE` (already a member).

### DELETE /organizations/{id}/members/{userId}

Owner. **204 No Content.** The person loses access on their next request, even with the same token. **Errors:** 403, 404, 409 `INVALID_STATE` (the last owner).

### GET /organizations/{id}/stats

Member. Dashboard numbers. "Today" and "this month" follow the business's time zone; the series is the last 14 days, oldest first.

```json
{
  "activeListings": 3,
  "portionsReserved": 4,
  "collectedToday": 2,
  "mealsRescuedThisMonth": 4,
  "mealsRescuedByDay": [
    { "date": "2026-10-16", "count": 0 },
    { "date": "2026-10-17", "count": 2 }
  ]
}
```

(`mealsRescuedByDay` always has 14 entries; two are shown.)

### GET /organizations/{id}/listings

Member. Query: `status` (optional, a [listing status](#enumerations)), `page`, `pageSize`. A page of [listing summaries](#get-listings): open listings first (soonest pickup), then past ones (most recent first).

### POST /organizations/{id}/listings

Member of a **verified** business. Publishes a listing.

| Field | Type | Rules |
| --- | --- | --- |
| `title` | string | 3 to 80 characters |
| `category` | string | `Bakery`, `Meals`, `Groceries`, `Beverages` or `Other` |
| `description` | string | optional, up to 500 characters |
| `quantityTotal` | integer | 1 to 200 |
| `originalPrice` | integer | MRP in rupees, 0 or more |
| `price` | integer | 0 to `originalPrice` (0 means free) |
| `pickupStart`, `pickupEnd` | date-time | the window lasts 15 minutes to 12 hours and ends in the future |
| `address` | string | 1 to 200 characters |
| `latitude`, `longitude` | number | optional; default to the business's location |
| `allergenNotes` | string | 3 to 300 characters; write "None known" if there are none |
| `imageUrl` | string | optional; the `url` from [POST /uploads/images](#post-uploadsimages) (or another http/https URL) |

```json
{
  "title": "Banana bread",
  "category": "Bakery",
  "description": "Two loaves.",
  "quantityTotal": 5,
  "originalPrice": 300,
  "price": 120,
  "pickupStart": "2026-10-17T18:00:00+05:45",
  "pickupEnd": "2026-10-17T20:00:00+05:45",
  "address": "Thamel Rd 12, Kathmandu",
  "allergenNotes": "Contains gluten and eggs."
}
```

**201 Created** with the [listing detail](#get-listingsid) and a `Location` header. **Errors:** 400 (for example `pickupEnd`: "Pickup must last between 15 minutes and 12 hours."), 403 `ORG_NOT_VERIFIED`, 403 `NOT_ORG_MEMBER`, 404.

### GET /organizations/{id}/reservations

Member. Today's counter queue: reservations whose pickup ends today or later, waiting ones first, then collected, no-shows and cancelled. Query: `listingId`, `status`, `page`, `pageSize`. Items are reservations plus `customerName` and `listingTitle` (see [POST /reservations/verify](#post-reservationsverify)).

## Listings

### GET /listings

Public. Live listings (Active or SoldOut, window not ended) from verified businesses.

| Query | Type | Meaning |
| --- | --- | --- |
| `lat`, `lng` | number | search location; send both or neither |
| `radiusKm` | number | default 5, maximum 25; only with a location |
| `category` | string | a listing category |
| `maxPrice` | integer | price at most this (0 finds free food) |
| `pickupFrom`, `pickupTo` | date-time | the pickup window overlaps this range |
| `q` | string | text in the title, the business name or the category |
| `sort` | string | `distance` (default with a location), `pickup` (default without) or `price` |
| `page`, `pageSize` | integer | paging |

Sold-out listings always come last. With a location, `distanceKm` is the distance in kilometres (one decimal); otherwise it is null.

`GET /api/v1/listings?lat=27.7154&lng=85.3123&pageSize=1`

```json
{
  "items": [
    {
      "id": "c7d99bc5-ef83-3309-9f92-8dfb436d79a1",
      "title": "Croissant trio",
      "category": "Bakery",
      "organization": { "id": "2fef9290-5bec-3976-83a7-6a257eba20b5", "name": "Sunrise Bakery", "isVerified": true },
      "price": 150,
      "originalPrice": 360,
      "quantityTotal": 12,
      "quantityAvailable": 8,
      "pickupStart": "2026-10-17T06:30:38+00:00",
      "pickupEnd": "2026-10-17T08:30:38+00:00",
      "status": "Active",
      "distanceKm": 0,
      "imageUrl": null
    }
  ],
  "page": 1,
  "pageSize": 1,
  "total": 9
}
```

**Errors:** 400 (unknown `sort` or `category`, only one of `lat`/`lng`).

### GET /listings/{id}

Public, including ended listings (for reservation history). The summary fields plus the details:

```json
{
  "id": "37781d7a-00f3-3b32-a267-c5401d529f32",
  "title": "Mixed pastry box",
  "...": "summary fields as above",
  "description": "A mix of croissants, danishes and rolls baked today. Contents vary. Best eaten within 24 hours.",
  "allergenNotes": "Contains gluten, milk and eggs; may contain nuts.",
  "address": "Thamel Rd 12, Kathmandu",
  "latitude": 27.7154,
  "longitude": 85.3123,
  "maxPerUser": 2
}
```

**Errors:** 404.

### PUT /listings/{id}

Member, live listings only. Same body as creating one. Quantity cannot drop below the units already reserved; stock is recalculated from the new total. **200 OK** with the listing. **Errors:** 400 (for example `quantityTotal`: "Quantity must be 7-200 (7 already reserved)."), 403, 404, 409 `INVALID_STATE` (ended or cancelled), 409 `CONFLICT` (a reservation changed the stock while you edited).

### POST /listings/{id}/cancel

Member. Cancels the listing; its active reservations are cancelled without any penalty and the customers are notified. **200 OK** with the listing (`status: "Cancelled"`). **Errors:** 403, 404, 409 `INVALID_STATE`.

## Reservations

The reservation object:

```json
{
  "id": "01a14872-caa9-71de-9dc3-69853923347b",
  "listingId": "f3965c70-d73e-3951-a094-1a409f59b249",
  "quantity": 1,
  "status": "Reserved",
  "pickupCode": "PA8R-TVN7",
  "reservedAt": "2026-10-17T06:00:44+00:00",
  "expiresAt": "2026-10-17T07:30:38+00:00",
  "collectedAt": null,
  "cancelledAt": null,
  "lateCancel": false
}
```

`expiresAt` is the end of the pickup window; a reservation still `Reserved` after it becomes `NoShow` (-20 reliability).

### POST /listings/{id}/reservations

Signed in (customers; not suspended). `{ "quantity": 1 }` (1 or 2). **201 Created** with the reservation and its pickup code.

Rules, checked inside one database transaction:

- at most 2 units per listing and one active reservation per listing per customer;
- at most 3 active reservations, or 1 when reliability is below 60;
- the stock is taken by a single conditional UPDATE, so two customers can never get the same last unit.

**Errors:** 400 `VALIDATION_ERROR`, 403 `ACCOUNT_SUSPENDED`, 404, 409 `SOLD_OUT`, 409 `LIMIT_REACHED`, 409 `WINDOW_CLOSED`.

```json
{
  "type": "urn:plateback:error:limit-reached",
  "title": "Already reserved",
  "status": 409,
  "detail": "You already have a reservation for this listing.",
  "code": "LIMIT_REACHED",
  "traceId": "0HNPC2FTA0F24:00000001"
}
```

### GET /reservations/mine

Signed in. Query: `status` = `active` (default; soonest pickup first) or `history` (collected, cancelled and no-shows; most recent first), `page`, `pageSize`. Each item is a reservation plus the listing details a customer needs:

```json
"listing": {
  "title": "Veg thali set",
  "category": "Meals",
  "price": 150,
  "originalPrice": 400,
  "pickupStart": "2026-10-17T05:30:38+00:00",
  "pickupEnd": "2026-10-17T07:30:38+00:00",
  "address": "Jhamsikhel, Lalitpur",
  "latitude": 27.6795,
  "longitude": 85.3088,
  "imageUrl": null,
  "organizationId": "3f8ac49a-0d35-3152-a01c-190a1b0d0acb",
  "organizationName": "Green Leaf Cafe"
}
```

### GET /reservations/{id}

The customer, or a member of the listing's business. Same shape as an item of `/reservations/mine`. Anyone else gets **404**.

### POST /reservations/{id}/cancel

The customer. Free before the pickup window opens; after that `lateCancel` is true and reliability drops by 5. The units go back to the listing while it is open. **200 OK** with the reservation. **Errors:** 404, 409 `ALREADY_COLLECTED`, 409 `WINDOW_CLOSED`, 409 `CONFLICT` (a parallel cancel of the same reservation won).

### POST /reservations/verify

Member, rate limited per user. Staff type the customer's code; case, spaces and the dash do not matter.

```json
{ "pickupCode": "k7m2 9qxa" }
```

**200 OK**: the reservation, now `Collected`, plus who and what:

```json
{
  "id": "6432b15a-b8cf-30ff-ba51-787179ac3e96",
  "listingId": "37781d7a-00f3-3b32-a267-c5401d529f32",
  "quantity": 1,
  "status": "Collected",
  "pickupCode": "K7M2-9QXA",
  "reservedAt": "2026-10-17T04:00:38+00:00",
  "expiresAt": "2026-10-17T09:00:38+00:00",
  "collectedAt": "2026-10-17T06:00:45+00:00",
  "cancelledAt": null,
  "lateCancel": false,
  "customerName": "Maya Rai",
  "listingTitle": "Mixed pastry box"
}
```

The customer gains 2 reliability. **Errors:** 404 `NOT_FOUND` (unknown code, or a code of another business: both look the same), 409 `CODE_ALREADY_USED`, 409 `WINDOW_CLOSED` (cancelled or no-show), 429.

## Notifications

Notifications are written to an outbox in the same transaction as the change they describe and delivered by a background job within about 10 seconds.

### GET /notifications

Signed in. Newest first, with the unread count for the bell.

```json
{
  "items": [
    {
      "id": "01a14870-dac0-7434-a203-066bfd49c823",
      "type": "ReservationConfirmed",
      "title": "Reservation confirmed",
      "body": "Mixed pastry box from Sunrise Bakery. Show code K7M2-9QXA at pickup.",
      "isRead": false,
      "createdAt": "2026-10-17T05:58:38+00:00",
      "data": { "reservationId": "6432b15a-b8cf-30ff-ba51-787179ac3e96" }
    }
  ],
  "page": 1,
  "pageSize": 1,
  "total": 5,
  "unreadCount": 3
}
```

`data` holds deep-link targets (`reservationId`, `listingId`, `organizationId`).

### POST /notifications/{id}/read and POST /notifications/read-all

Signed in. **204 No Content.** **Errors:** 404 (not your notification).

## Stats

### GET /stats/impact

Public, cached for 5 minutes. Counted from real data only.

```json
{ "mealsRescued": 8, "partnerBusinesses": 6, "activeListings": 10 }
```

## Uploads

### POST /uploads/images

Signed in. New since the Week 1 design (added for listing photos in Week 2). `multipart/form-data` with one field, `file`: JPEG, PNG or WebP, up to 10 MB. The type is decided from the file's first bytes, so a renamed text file is rejected. Files get a random name and are served by the API.

```bash
curl -X POST http://localhost:5080/api/v1/uploads/images \
  -H "Authorization: Bearer $TOKEN" -F "file=@bread.jpg"
```

**201 Created**

```json
{ "url": "/uploads/01a14873b0c27f2b9d4e6a1c5f0e2d3a.jpg" }
```

Use `url` as the listing's `imageUrl`. It is a path on the API's host; the frontend's dev server proxies `/uploads` to the API. **Errors:** 400 `VALIDATION_ERROR` on `file` ("Use a JPEG, PNG or WebP photo.", "The photo must be 10 MB or smaller."), 401, 413.

## Admin

All admin endpoints need the `Admin` role; anyone else gets **403 `FORBIDDEN`**.

### GET /admin/organizations

Query: `status` (Pending, Verified, Rejected, Suspended), `page`, `pageSize`. Oldest first, with the owner and phone:

```json
{
  "items": [
    {
      "id": "fbe8d892-89d1-3118-8b68-20bf8e9d02fd",
      "name": "Durbar Sweets",
      "type": "Bakery",
      "address": "Taumadhi, Bhaktapur",
      "latitude": 27.6714,
      "longitude": 85.4298,
      "timeZone": "Asia/Kathmandu",
      "status": "Pending",
      "createdAt": "2026-10-16T04:00:38+00:00",
      "verifiedAt": null,
      "rejectionReason": null,
      "phone": "+977 98XXXXXXXX",
      "ownerName": "Suman Thapa",
      "ownerEmail": "suman@durbarsweets.example"
    }
  ],
  "page": 1,
  "pageSize": 1,
  "total": 3
}
```

### POST /admin/organizations/{id}/verify

No body. Verifies a pending or rejected business, or lifts a suspension. **200 OK** with the review item. The owners are notified. **Errors:** 404, 409 `INVALID_STATE` (already verified).

### POST /admin/organizations/{id}/reject and /suspend

`{ "reason": "Could not confirm the business address." }`. The reason is required and shown to the owner. Reject only applies to pending businesses, suspend only to verified ones; suspending also cancels the business's live listings and their reservations. **200 OK** with the review item. **Errors:** 400 on `reason`, 404, 409 `INVALID_STATE`.

### GET /admin/users

Query: `q` (part of the name, or an exact email), `page`, `pageSize`. Items: `{ id, email, displayName, role, reliabilityScore, isSuspended, suspensionReason, createdAt }`.

### POST /admin/users/{id}/suspend and /restore

Suspend takes `{ "reason": "..." }` (required) and ends the account's sessions; it can no longer log in or reserve. Restore takes no body. **204 No Content.** **Errors:** 400, 404, 409 `INVALID_STATE` (admins cannot be suspended).

### POST /admin/listings/{id}/remove

`{ "reason": "..." }`. Takes a listing down, cancels its reservations and notifies the business. **204 No Content.** **Errors:** 400, 404, 409 `INVALID_STATE`.

### GET /admin/audit-logs

Query: `entityType` (for example `Reservation`), `entityId`, `actorId`, `page`, `pageSize`. Newest first. Items: `{ id, actorId, action, entityType, entityId, details, occurredAt }`; `actorId` is null for the background jobs.

## Health

### GET /health

Outside `/api/v1`, public. **200** `{ "status": "Healthy", "database": "Connected" }`, or **503** when the database cannot be reached.

## Enumerations

| Name | Values |
| --- | --- |
| UserRole | `Customer`, `Admin` |
| MemberRole | `Owner`, `Staff` |
| OrganizationStatus | `Pending`, `Verified`, `Rejected`, `Suspended` |
| BusinessType | `Bakery`, `Cafe`, `Restaurant`, `Grocer`, `Other` |
| ListingCategory | `Bakery`, `Meals`, `Groceries`, `Beverages`, `Other` |
| ListingStatus | `Active`, `SoldOut`, `Expired`, `Cancelled` |
| ReservationStatus | `Reserved`, `Collected`, `Cancelled`, `NoShow` |
| NotificationType | `ReservationConfirmed`, `ReservationCancelled`, `ReservationCollected`, `ReservationNoShow`, `PickupWindowOpening`, `ListingCancelled`, `NewListingNearby`, `OrganizationVerified`, `OrganizationRejected` |
