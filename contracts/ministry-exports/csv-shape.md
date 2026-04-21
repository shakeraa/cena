# Ministry reporting export — CSV shape

**Sibling of** [`schema.json`](schema.json) (JSON format).
**Canonical spec doc**: [`docs/api/ministry-reporting-export-spec.md`](../../docs/api/ministry-reporting-export-spec.md) (prr-235).
**Status**: spec-only at Launch. Implementation lands Q3.

---

## Encoding

- UTF-8 with BOM (`0xEF 0xBB 0xBF` prefix). Windows Excel opens Hebrew /
  Arabic correctly only with the BOM.
- Line ending `\r\n` per RFC 4180.
- Cells containing comma, newline, CR, or double-quote are wrapped in
  double-quotes; embedded double-quotes are doubled (`"Some ""quoted"" text"`).
- Null values are emitted as an empty cell (not the string `"null"`).

## Header row

Line 1 is the header. Columns in declared order — consumers must not rely
on column-name matching alone. Schema is versioned: when the shape
changes, the schema_version in the sibling JSON export bumps, and CSV
consumers should re-fetch the shape doc.

## Column order (schema version 1.0)

| Pos | Column name | Notes |
|----:|---|---|
| 1  | `student_id` | Tenant-local stable id. |
| 2  | `enrollment_id` | ADR-0001 identifier. |
| 3  | `exam_code` | Catalog PK. |
| 4  | `ministry_subject_code` | May be empty for non-Ministry catalogs. |
| 5  | `ministry_question_paper_codes` | Semicolon-joined (e.g. `035581;035582`). Empty for SAT/PET. |
| 6  | `track` | May be empty. |
| 7  | `sitting_code` | Canonical form. |
| 8  | `academic_year` | Hebrew or Gregorian. |
| 9  | `season` | `Summer`\|`Winter`. |
| 10 | `moed` | `A`\|`B`\|`C`\|`Special`. |
| 11 | `source` | `Student`\|`Classroom`\|`Tenant`\|`Migration`. |
| 12 | `assigned_by_id` | User id. |
| 13 | `weekly_hours` | 1..40. |
| 14 | `reason_tag` | Enum or empty. |
| 15 | `created_at` | ISO-8601. |
| 16 | `archived_at` | ISO-8601 or empty. |
| 17 | `export_readiness` | `Exportable`\|`RequiresConsent`. |

## Example

```csv
student_id,enrollment_id,exam_code,ministry_subject_code,ministry_question_paper_codes,track,sitting_code,academic_year,season,moed,source,assigned_by_id,weekly_hours,reason_tag,created_at,archived_at,export_readiness
stu-abc,enr-xyz,BAGRUT_MATH_5U,035,035581;035582;035583,5U,"תשפ""ו/Summer/A","תשפ""ו",Summer,A,Classroom,teacher-001,6,Retake,2026-01-15T09:00:00Z,,Exportable
stu-def,enr-xyz,PET_QUANTITATIVE,,,,"2026/Summer/A",2026,Summer,A,Student,stu-def,4,NewSubject,2026-02-10T14:22:00Z,,RequiresConsent
```

## Row filter

A row is included when:

- Its `ExamTarget` matches `enrollmentId` (request parameter).
- The `include` parameter says `all` OR the target is active
  (`archived_at` is empty).
- Either `source ∈ {Classroom, Tenant}`, OR the student has consented to
  school sharing (see [spec §5](../../docs/api/ministry-reporting-export-spec.md#5-source-filter-rule--export-readiness-flag)).

## Stability guarantees

- Column order is stable within a schema version.
- Adding a column (with a backward-compatible default) bumps the schema
  version from `1.x` → `1.x+1`.
- Removing a column or reordering columns bumps to `2.0`.
- `ministry_question_paper_codes` separator (`;`) is stable; we never
  switch to comma because comma would require quoting on every row.
