# Listonic API notes

Unofficial, reverse-engineered behaviour of `https://api.listonic.com/api` as used by this server.
Use [`Listonic.Sandbox`](../src/Listonic.Sandbox) to re-check anything here — it dumps every exchange in
full. Last verified with a real account on 2026-11-27 (`login-probe`, `roundtrip`).

## Authentication

- `POST /loginextended?provider=password&autoMerge=1&autoDestruct=1`, form-encoded
  `username`, `password`, `client_id=listonicv2`, `client_secret`, `redirect_uri`, plus a static
  `clientauthorization: Bearer …` header (the app's client credentials, public).
- Success: OAuth-style JSON `access_token`, `token_type: Bearer`, `expires_in: 86399` (24 h), `refresh_token`.
  The server caches the access token per account and simply logs in again on a `401`.
- **Rejected login** — wrong password, unknown email and blank fields all return
  `HTTP 400 {"Message":"Unauthorized token. Token is invalid"}`. Any `400` except an OAuth client/grant
  error is therefore treated as bad credentials.
- Accounts created with Google/Facebook sign-in have no Listonic password, so they can't use
  this server's password login.

## Lists

| Call | Notes |
|---|---|
| `GET /lists?includeShares=&archive=&includeItems=` | `ItemsCount`, `CheckedItemsCount`, `IsOwner` (0/1), `Shares[]` (the owner counts as one share, so `Shares.Count > 1` = shared) |
| `GET /lists/{id}` | single list. Unknown id → `404` |
| `POST /lists` `{ "Name": … }` | returns the created list with its id |
| `PATCH /lists/{id}` `{ "Id": …, "Name": … }` | rename |
| `DELETE /lists/{id}` | soft delete — moves to the trash |

## Items

| Call | Notes |
|---|---|
| `GET /lists/{id}/items` | `Checked` is `0`/`1`; `Amount` is a **string** (may use `,` or `.` and spaces); `Unit` is free text |
| `POST /lists/{id}/items` `{ name, amount, unit, … }` | `201` with the item (`Id`, `Amount` as string, `Checked: 0`, `CategoryId` auto-assigned, `Creator`) and a `Location` header |
| `PATCH /lists/{id}/items/{itemId}` | partial update; omitted fields stay as they are; `amount` sent as a string. Unknown item → `404` |
| `DELETE /lists/{id}/items/{itemId}` | permanent. **Unknown item → `200` with no body** (silent) — `delete_items` checks ids against the list first |

Encodings are handled by `ZeroOneBoolConverter` and `DecimalStringConverter`.

## Other

- `GET /categoriesv2` — product categories (not modelled yet; dump with `dump-categories`).
- `GET /account/userinfo` — basic account info.
- Errors use RFC 7807 problem details, e.g. `404 {"title":"The specified resource was not found.",
  "detail":"Entity \"ShoppingList\" (999999999) was not found.", …}`. The tool layer maps `404` → "not found",
  `403` → "no access", other `4xx` → "rejected the request".
- **`GET /lists/{unknown}/items` returns `200 []`**, not 404 — `get_items` re-checks `GET /lists/{id}` when
  it gets an empty list, so a wrong id isn't reported as "List is empty".

## Client-side rate limit

All calls (login included) go through one token bucket: burst 15, then one request every 0.5 s.
Requests over the limit wait in a queue; they never fail.
