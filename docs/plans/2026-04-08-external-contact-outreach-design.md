# External Contact Outreach ("Osloveni")

## Problem

Organizers have contacts (email addresses) of people who have never attended Ovcina but expressed interest. There is no way to store these contacts or send them emails from the app.

## Solution

A standalone admin page at `/admin/osloveni` with a simple contact list and bulk email composer.

## Data Model

```
ExternalContact
- Id (int, PK, auto-increment)
- Email (string, max 320, required, unique index)
- CreatedAtUtc (DateTime, required)
```

One table, hard-delete only (no soft-delete). No relationship to Person or ApplicationUser.

## Admin Page: `/admin/osloveni`

### Add Contacts

- Single email input + "Pridat" button
- Textarea for bulk paste (one email per line) + "Importovat" button
- Deduplicates on insert (skip existing emails, report count)

### Contact List

- Table: email | date added | delete button
- Total count displayed
- No pagination needed (expected <500 contacts)

### Compose & Send

- Subject input field
- HTML body textarea
- "Odeslat vsem" button with confirmation dialog ("Opravdu odeslat X emailu?")
- Sends via existing Microsoft Graph shared mailbox (ovcina@ovcina.cz)
- One email per recipient (not CC/BCC bulk)
- No delivery tracking, fire-and-forget

## Service: `ExternalContactService`

- `AddAsync(string email)` - single add, normalize to lowercase, skip if exists
- `ImportAsync(IEnumerable<string> emails)` - bulk add, returns (added, skipped) counts
- `GetAllAsync()` - list all contacts
- `DeleteAsync(int id)` - hard delete
- `SendToAllAsync(string subject, string htmlBody)` - load all contacts, send via Graph

## Navigation

Add "Osloveni" link to admin sidebar/nav, restricted to Admin role.

## Out of Scope

- Delivery tracking, open/click analytics
- Email templates or personalization
- Scheduled sending
- Contact segmentation or tagging
