# Manual Person Merge via Search — Design

**Date:** 2026-04-11
**Status:** Approved

## Problem

The person detail page shows auto-detected merge candidates (same birth year + name/email/phone match), but organizers need to manually merge people who don't match the conservative detection criteria — e.g. same person registered with different birth years across submissions.

## Solution

Add a search box on the person detail page (`/organizace/osoby/{id}`) that lets organizers find any person by name, email, or phone and merge them into the current person.

## UI Flow

1. Search box labeled "Hledat osobu ke slouceni — jmeno, e-mail nebo telefon" in the right panel near existing merge candidates
2. Results appear below — matching persons (excluding current person and soft-deleted), showing: name, birth year, phone, registration count, last seen game/date
3. Organizer clicks "Sloucit" on a result
4. Confirmation: "Sloucit **{duplicate name}** (rocnik {year}, {N} ucast/i) do **{canonical name}** (rocnik {year}, {N} ucast/i)? Registrace, postavy a kontaktni udaje budou prevedeny. Tato akce nelze vratit."
5. On confirm: calls existing `PeopleReviewService.MergeAsync(canonicalId: current person, duplicateId: selected person)`
6. Page refreshes with updated data

## Technical Details

- **No new backend** — reuses existing `PeopleReviewService.MergeAsync` and `PersonIdentityNormalizer`
- **Search** — same pattern as `People.razor`: case/diacritic-insensitive via `PersonIdentityNormalizer`, matches name, email, phone
- **Merge direction** — always into the person being viewed (canonical = current)
- **Confirmation required** — destructive action, soft-deletes the duplicate
- **Files to modify** — `PersonDetail.razor` only (possibly extract search query to `PeopleReviewService` if not already there)

## Non-Goals

- Merge direction choice (always into current person)
- Undo/reverse merge
- Batch merge
