# FIGURE-006: Admin — figure editor in question edit page with live preview

## Goal
Extend `src/admin/full-version/src/pages/apps/questions/edit/[id].vue` so authors can create, edit, and preview any of the 4 figure spec types. Must be fast, non-painful, and enforce `aria-label` on save.

## Depends on
- FIGURE-002 (schema + DTOs)
- FIGURE-003 (`<QuestionFigure>` reusable in admin)
- FIGURE-005 (physics service endpoint)

## Work to do
1. New tab/section in the question edit page: "Figure"
2. Type picker: None | Function plot | Geometry | Physics diagram | Raster
3. Form fields per type:
   - **FunctionPlot**: expression (with live LaTeX preview), x/y range sliders, markers list (x, label)
   - **Geometry**: JSXGraph JSON editor (Monaco or equivalent), with "insert common construction" presets (perpendicular bisector, angle bisector, circle-through-3-points, etc.)
   - **Physics**: body type dropdown, parameters form (angle, mu, masses, …), force checklist (gravity, normal, friction, applied, tension, …), coordinate-frame toggle
   - **Raster**: upload + paste-URL + crop
4. Live preview pane (re-uses `<QuestionFigure>` from FIGURE-003) — updates on every change, debounced 200ms
5. `aria-label` field is **required**. Save button disabled if empty. Show character count.
6. "Generate description" button → calls AI (see FIGURE-008) to propose an `aria-label` from the spec; author approves or edits.
7. For Physics specs: "Preview with service" button → POSTs to `/api/admin/figures/physics` and shows the actual server-generated SVG (not the client renderer), so author sees exactly what students will see.
8. Save wires through the existing question update mutation (from FIGURE-002).

## Non-negotiables
- Cannot save a figure without `aria-label`
- Cannot save without previewing at least once (prevents broken specs reaching the bank)
- Dark-mode + RTL of the admin UI must not break

## DoD
- All 4 types authorable end-to-end
- Author can go Question Bank → edit question → add figure → save → open student view and see the figure
- Unit tests on the form validation (aria-label required, parameter ranges, etc.)

## Reporting
Complete with branch and screencast URL (or screenshots) of authoring each of the 4 types.
