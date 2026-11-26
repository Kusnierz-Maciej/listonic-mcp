# MCP tools reference

All tools take Listonic ids as strings. Tools that act on a list also take `listName` (and item tools a
`confirmName` per item): those are **not sent to the API**. They only make the client's permission prompt
readable ("delete *mleko* from *Zakupy*"), so the user can confirm the right target.

Annotations: **R** = read-only, **D** = destructive, **I** = idempotent. Clients use these hints to
auto-approve reads and to prompt before deletes.

Errors come back as a tool error with a plain message, e.g. *"Listonic couldn't find that list or item
(HTTP 404)…"* or, when the stored password no longer works, *"Listonic rejected the account's sign-in…
Reconnect this Listonic connector…"*.

## `get_lists` — R, I

Every list the account can access (own and shared).

**Output** — one line per list:

```
[123456] Zakupy (private) - 3 unchecked
[234567] Wspólna (shared) - 7 unchecked
```

## `get_items` — R, I

| Input | |
|---|---|
| `listId` | from `get_lists` |
| `listName` | the list's name, for the permission prompt |

**Output** — unchecked (still to buy) first, then checked; quantity in brackets, a zero amount hidden:

```
[ ] [9001] mleko (2 l)
[ ] [9002] masło
[x] [9003] chleb
```

`List is empty.` when there are no items.

## `add_items`

| Input | |
|---|---|
| `listId`, `listName` | target list |
| `items[]` | `{ name, amount?, unit? }` — product name only in `name` (user's language), number in `amount`, free-text `unit` (`kg`, `l`, `szt.`) |

New items start unchecked. **Output**: the added items in `get_items` format. The model is told to check
`get_items` first and untick or update an existing row instead of creating a duplicate.

## `edit_items` — I

| Input | |
|---|---|
| `listId`, `listName` | target list |
| `items[]` | `{ itemId, confirmName, isChecked?, name?, amount?, unit? }` — only the fields to change; `amount: 0` + `unit: ""` clears the quantity |

Tick off (`isChecked: true` — "I bought X"), untick, rename, change quantity. **Output**: the updated
items in `get_items` format.

## `delete_items` — D

| Input | |
|---|---|
| `listId`, `listName` | target list |
| `items[]` | `{ itemId, confirmName }` |

Permanent. **Output**: the deleted items in `get_items` format.

## `create_list`

| Input | |
|---|---|
| `name` | name of the new list |

Creates an empty, private list. **Output**: `[345678] Weekend (private) - 0 unchecked`. The model is told
to check `get_lists` first and not create a duplicate.

## `rename_list` — I

| Input | |
|---|---|
| `listId`, `listName` | the list and its **current** name |
| `name` | the new name |

**Output**: `List 123456 renamed to "Zakupy na weekend".` On a shared list everyone sees the new name.

## `delete_list` — D

| Input | |
|---|---|
| `listId`, `listName` | target list |

Moves the list to the Listonic trash (restorable in the app). On a shared list it disappears for everyone.
**Output**: `List 123456 moved to trash.` The model is told to confirm with the user first.

## Not supported

Moving items between lists (add to one + delete from the other), categories, notes on items.
