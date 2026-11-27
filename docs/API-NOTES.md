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
| `GET /lists?includeShares=&archive=&includeItems=` | `ItemsCount`, `CheckedItemsCount`, `IsOwner` (0/1), `Shares[]` — a new private list has exactly one share (the owner), so `Shares.Count > 1` = shared |
| `GET /lists/{id}` | single list. Unknown id → `404` |
| `POST /lists` `{ "Name": … }` | returns the created list with its id |
| `PATCH /lists/{id}` `{ "Id": …, "Name": … }` | rename. **Unknown id → `200` (silent)** — `rename_list` checks `GET /lists/{id}` first |
| `DELETE /lists/{id}` | moves the list to the app's trash. Afterwards the API returns `404` for it and it isn't listed even with `archive=true`. Unknown id → `404` |

## Items

| Call | Notes |
|---|---|
| `GET /lists/{id}/items` | `Checked` is `0`/`1`; `Amount` is a **string** (may use `,` or `.` and spaces) — `""` when added without a quantity, `"0"` after clearing; `Unit` is free text. Names keep Unicode (Polish letters) |
| `POST /lists/{id}/items` `{ name, amount, unit, … }` | `201` with the item (`Id`, `Amount` as string, `Checked: 0`, `CategoryId` auto-assigned, `Creator`) and a `Location` header |
| `PATCH /lists/{id}/items/{itemId}` | partial update; omitted fields stay as they are; `amount` sent as a string; `amount 0` + `unit ""` clears the quantity. Unknown item → `404` |
| `DELETE /lists/{id}/items/{itemId}` | permanent. **Unknown item → `200` with no body** (silent) — `delete_items` checks ids against the list first |

Encodings are handled by `ZeroOneBoolConverter` and `DecimalStringConverter`.

## Other

- `GET /categoriesv2` — flat list of product categories (26 on a Polish account, names in English, e.g.
  `Dairy & eggs`, `Bakery`, `Other`): `Id`, `Name`, `IconId`, `Order`, `AutoOrder`, `Color`, `ColorDark`,
  `Standard`. Not modelled yet (future categories feature; dump with `dump-categories`).
- **Category assignment** (probed 2026-11-27 with `category-assign`): the server always fills `CategoryId` on
  add, from the name alone — Polish and English both work (`mleko`/`milk` → Dairy & eggs, `szampon` → Personal
  care, `kombucha` → Beverages). Unrecognised names (typos like `mlekko`, nonsense) get `1` = **Other**; nothing
  is ever left empty. Every assigned id was in `/categoriesv2`, and re-reading returns the same ids. A future
  categories tool should read `/categoriesv2` at runtime (not hard-code ids), so categories Listonic adds later
  show up automatically. This is **trusted, not contract-tested**: a categories feature can rely on the server
  assigning a category to every item.
- `GET /account/userinfo` — basic account info.
- Errors use RFC 7807 problem details, e.g. `404 {"title":"The specified resource was not found.",
  "detail":"Entity \"ShoppingList\" (999999999) was not found.", …}`. The tool layer maps `404` → "not found",
  `403` → "no access", other `4xx` → "rejected the request".
- **`GET /lists/{unknown}/items` returns `200 []`**, not 404 — `get_items` re-checks `GET /lists/{id}` when
  it gets an empty list, so a wrong id isn't reported as "List is empty".

- An invalid or expired bearer token gets `401` — the only status that triggers the re-login.

## Contracts guarded by live tests

Each behaviour the code depends on has a `[Category("LiveApi")]` test (run nightly against the latest
release by *Verify Listonic API contract compatibility*). Live test classes are `[NotInParallel]` on one key,
all `[test] ` lists are swept once when the run starts (crash recovery) and after each class.

| Contract | Code that relies on it | Test |
|---|---|---|
| Login with form + client credentials → Bearer token, ≥ 1 h | auth handler, OAuth sign-in | `Login_Valid_IsOk_WithDayLongToken` |
| Wrong password / unknown email → 400 "Unauthorized token" | `ListonicLogin.Classify` | `Login_WrongPassword_…`, `Login_UnknownEmail_…` |
| Invalid token → 401 | re-login in `ListonicAuthHandler` | `InvalidBearerToken_Is401` |
| Unknown list: GET 404, items 200 [], rename 200, delete 404 | `get_items`, `rename_list`, `delete_list`, error mapping | `GetList_UnknownId_…`, `GetItems_UnknownList_…`, `RenameList_UnknownId_…`, `DeleteList_UnknownId_…` |
| Unknown item: PATCH 404, DELETE silent 200 | `edit_items`, `delete_items` | `UnknownItem_PatchIs404_DeleteIsSilent200` |
| Deleted list is gone from the API | `delete_list` wording | `DeletedList_IsGoneFromApi_EvenWithArchive` |
| New list: Id + Name in response, one share, owner | `create_list`, `get_lists` private/shared | `NewList_IsPrivate_OneShare_Owned_WithName` |
| Counts and shares populated | `get_lists` unchecked count | `GetLists_WithIncludeShares_PopulatesCountsAndShares` |
| Add returns id, amount as string, unchecked | `add_items` | `AddItem_ReturnsFullItem_…`, `AddItem_*` |
| No amount = "", clear = "0"/"", Unicode names | `FormatItem`, `edit_items` | `ItemQuantity_…` |
| Partial PATCH keeps other fields; check/rename/amount/unit | `edit_items` | `UpdateItem_*` |
| Delete / rename / create / get list | list tools | `DeleteList_*`, `RenameList_*`, `CreateList_*`, `GetList_*` |

## Client-side rate limit

All calls (login included) go through one token bucket: by default burst 15, then one request every 0.5 s
(`RateLimit__TokenLimit` / `RateLimit__ReplenishmentPeriod`).
Requests over the limit wait in a queue; they never fail.
