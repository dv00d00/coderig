# Decide whether `bus` and `echo` are distinct effect families or one alias

**Status:** todo · **Triage:** needs-info · **Found:** 2026-09-01 while auditing the file lens ·
**Family:** rules / product vocabulary

## Problem

The built-in rules expose `bus`, while the MedDBase analysis rules rename the relevant providers to `echo`.
A file query can therefore disclose both family names even when they describe the same product concept. This
is not a renderer defect: the shared lens accurately reports the effective selector vocabulary it receives.

## Decision required

Choose one of these domain meanings before editing rules:

- `bus` and `echo` are distinct operational effects and need explicit provider membership boundaries; or
- `echo` is the MedDBase spelling of `bus`, in which case one canonical family plus a migration/alias policy is
  required for existing stores, rules, caches, CLI output, and saved web state.

Calibrate the provider sets on the MedDBase rules/store before choosing. A synthetic fixture cannot establish
whether two real provider populations mean the same thing.

## Testing expectations

- Rule-loading tests pin the chosen canonical family and provider membership.
- A synthetic derive/file-lens fixture proves providers do not double-report across both names.
- Real-store counts are captured before and after; any family rename is disclosed as an output migration.

## Out of scope

Changing family names in a text renderer. The renderer must remain vocabulary-agnostic.
