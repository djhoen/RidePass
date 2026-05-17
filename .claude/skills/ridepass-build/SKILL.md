---
name: ridepass-build
description: Run RidePass's three build verifiers — dotnet build for Services, dotnet build for webapi, and vue-tsc --noEmit for vueapp — and report only real errors. Filters known project-wide noise (vite import.meta.env, MSBuild file-lock retries, pre-existing typing gaps in unrelated views) so the report contains only new findings worth acting on.
---

# RidePass Build Check

Sanity-check that backend and frontend type-check cleanly after edits. Invoke before reporting any task done that touched `.cs`, `.vue`, or `.ts` files.

## Steps

Run all three commands. The two .NET builds are independent (build them in parallel via multiple Bash calls in one message). Frontend type-check runs from the vueapp directory.

1. `dotnet build C:\Users\djhoe\source\repos\RidePass\Services\Services.csproj`
2. `dotnet build C:\Users\djhoe\source\repos\RidePass\webapi\webapi.csproj`
3. From `C:\Users\djhoe\source\repos\RidePass\vueapp`: `npx vue-tsc --noEmit`

## What to filter

These are **noise** — do not report them as findings.

### .NET build noise
- `MSB3026` / `MSB3027` — file-copy lock retries when a webapi process is holding `Services.dll`. Not a code error. Mention separately if it occurs ("a webapi process is locking the build output — kill it and rebuild") but don't count as a build failure.
- Anything starting with `warning ` — only `error CS####` lines matter.

### vue-tsc noise (project-wide and pre-existing)
- `error TS2339: Property 'env' does not exist on type 'ImportMeta'` — present in ~25 service / store files because vite types aren't wired through tsconfig. Filter all instances.
- `src/views/Admin/CustomerDetail.vue` — `Property 'phone' does not exist on type` (pre-existing).
- `src/views/BuyPass.vue` — `Property 'requiresWaiver' does not exist` (pre-existing — column was renamed to `requiresRiderWaiver` / `requiresSpectatorWaiver`).
- `src/views/User/Membership.vue` — `Property 'requiredFor*' does not exist` (pre-existing).
- `src/views/User/MyPasses.vue:134` — `'string | null' is not assignable` in the unrelated extras render block (pre-existing).

If new errors of similar shape appear in **newly-edited files**, those ARE real findings — don't auto-filter by message text alone, also check the file path against the diff.

## Reporting

- Clean: `Build clean — 0 errors.`
- Findings: list as `file:line — message`, grouped by build. Don't try to fix; just report. Fixes are a separate call.
- File-lock note (if any): mention as a separate one-liner so the user knows to kill stray processes before rebuilding.

## Don't

- Don't auto-fix errors. Reporting is the job.
- Don't run `npm run build` or full release builds — `vue-tsc --noEmit` is the type check we want; release builds are slower and not what this skill is for.
