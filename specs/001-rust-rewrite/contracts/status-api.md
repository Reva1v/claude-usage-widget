# Contract: status.claude.com

Owner: `cuw-status` (decode), transport `cuw-http`. Source: INV 2B §8.

## Request

`GET https://status.claude.com/api/v2/summary.json`, header `Accept: application/json`, no
credentials, no explicit User-Agent beyond the client default, 100 s timeout, system proxy. Runs on a
worker thread; result posted to the UI thread.

## Status code mapping (`cuw-usage::http_validation::validate`)

2xx -> decode body; 401/403 -> `Unauthorized{None}`; 429 -> `RateLimited{retry_after}` (header
not read here: `None`); else `Network("HTTP <code>")`; transport error -> `Network(<message>)`.
Any error: StatusStore keeps the previous status, no Changed, nothing shown (INV 2B §8).

## Decoding (`cuw-status::decode`)

1. Not JSON or root not an object -> `MalformedResponse`.
2. For each object in `components` with string `name == "Claude Code"` (exact) and string `status`:
   map `operational`->Operational, `degraded_performance`->Degraded, `partial_outage`->PartialOutage,
   `major_outage`->MajorOutage, `under_maintenance`->Maintenance, else Unknown; first non-Unknown wins.
3. Else `status.indicator`: `none`->Operational, `minor`->Degraded, `major`->PartialOutage,
   `critical`->MajorOutage, `maintenance`->Maintenance; non-Unknown wins.
4. Else `MalformedResponse`.

Fixtures: `status/claude-code-degraded-page-none.json`, `status/claude-code-operational-page-critical.json`,
`status/indicator-only.json`, `status/neither.json`, `status/not-json.txt` (cases of StatusDecoderTests).

## Presentation

Dial label: `OK SLOW PARTIAL DOWN MAINT —`; colour Accent / Warning / Warning / Danger / Info / Dim
(alpha 0.4 for Unknown). Line: `service <word>`; silent only for (Operational, Cell mode). Dial click
opens `https://status.claude.com` and logs `- browser-open site=status-dial url=https://status.claude.com`.
