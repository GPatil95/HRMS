# Documentation Review Checklist — per milestone

Run this checklist before milestone sign-off. Every item must be ✅ or the
milestone documentation is not complete.

## Content

- [ ] Module manual updated (or created) for every module the milestone touched.
- [ ] Every screen the milestone added/changed has a real screenshot from the
      running application — no mockups, no placeholders left as "implemented".
- [ ] Field-by-field explanations exist for every form.
- [ ] At least one worked example with expected results.
- [ ] Validation rules and error messages documented match what the app shows.
- [ ] Troubleshooting section lists the top 3+ real failure modes.
- [ ] FAQ has at least 5 entries.
- [ ] Pending features are marked `Status: PENDING`, not described as working.

## Accuracy

- [ ] Permission codes in the manual match the code (`*.Permissions`).
- [ ] Menu paths / routes in the manual match `App.tsx` and the sidebar.
- [ ] Steps were followed end-to-end on a clean tenant and produce the shown
      result.
- [ ] Screenshots contain only demo data — no real personal information.
- [ ] `Applies to` version matches the milestone; `Last updated` is current.

## Role coverage

- [ ] Each role that touches the module has its access and workflow explained
      (what they can see, what they cannot).
- [ ] Data-scope effects documented (what a scoped vs tenant-wide user sees).

## Training material

- [ ] Video script updated/added under `training/scripts/` and indexed in
      `training/video-index.md`.
- [ ] Script includes demo data list, timed steps, narration, duration.
- [ ] Recording status is honest: `Script ready` ≠ `Recorded`.

## Setup docs

- [ ] Implementation guide updated if the milestone added setup steps
      (configuration, integrations, migrations, permissions).
- [ ] Go-live checklist updated if the milestone added an operational step.

## Technical

- [ ] Manual committed on the milestone branch (docs ship with the code).
- [ ] Markdown lints clean; images render (relative paths, correct case).
- [ ] Cross-links to related manuals and the implementation guide work.
