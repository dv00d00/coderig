# Quoted-query resource attribution — recover table-touch without phantom calls

## The gap the v5 expression-tree gate exposed

`InExpressionTree` (schema v5) correctly stops quoted references from deriving invocation effects — a
nav getter in `where p.Nav.X == y` is a SQL join, not a call. But the join DOES touch that table on
every execution of the query, and when the query executes inside a real loop the touch is amplified
×N. Pre-v5 that table-touch was "covered" by accident (the phantom getter effect); post-v5 it is
invisible, because the query's own EXECUTION effect is resource-anonymous (the coarse LinqMetaData
ctor rule, resource = declaring_type).

Measured on MedDBase (2026-08-04, OTEL oracle of 79 runtime-amplified tables, identical mapper both
sides): v4 71/79 combined coverage → v5 66/79. Of the 5 lost tables, 2 (RESERVATION, BANK_HOLIDAY)
were fake coverage via monadic-FP anchors — good riddance — but APPOINTMENT_TYPE_SERVICE_MODULE was a
REAL foreach anchor whose witness was a quoted nav-getter: true table-touch, wrong mechanism. This
item recovers that class honestly.

## Design sketch

One executing query = ONE effect, carrying the RESOURCE SET of the tables its expression references:

- Extraction: for a reference with `InExpressionTree=true`, additionally record the SITE of the
  enclosing query/lambda root (span or line of the outermost quoted node), so quoted refs GROUP.
- Derive: a new rule arm ("query_projection"?) matches the query's execution site (the LinqMetaData
  datasource access / `GetEnumerator` / the ctor rule that exists today) and resolves its resource(s)
  from the grouped quoted refs' target types (entity-typed getters → entities). Multi-resource
  effects OR one effect per referenced entity — one per entity is simpler and matches the oracle's
  per-table grain.
- This also de-anonymizes the ~2k coarse `llblgen_linq_query_context_coarse` sites and supersedes the
  blocked "LinqMetaData ctor type_argument" extraction fix (the ctor has no type argument to read;
  the quoted refs are the real signal).

Keep the amplification/anchor semantics unchanged: the effect sits at the query execution site, so a
query in a real loop anchors normally; nothing per-row is claimed.

## Status

Postponed for review (high-effort: extraction change + schema bump + reindex, new derive arm).
Depends on nothing; supersedes the `type_argument`-for-ctors idea recorded earlier.
