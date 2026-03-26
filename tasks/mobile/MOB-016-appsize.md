# MOB-016: APK Optimization — Split-Per-ABI, Font Subset, Dep Review

**Priority:** P2 — blocks efficient distribution
**Blocked by:** All other MOB tasks
**Estimated effort:** 1 day
**Contract:** None (performance optimization)

---

## Subtasks

### MOB-016.1: Split APK Per ABI
- [ ] `flutter build apk --split-per-abi` for arm64-v8a, armeabi-v7a, x86_64
- [ ] Each APK < 20MB target
- [ ] App bundle for Play Store distribution

### MOB-016.2: Font Subsetting + Asset Review
- [ ] Subset fonts: only include used glyphs for Hebrew, Arabic, Latin
- [ ] Remove unused assets (placeholder images, dev-only resources)
- [ ] Tree-shake icons: only import used Material icons
- [ ] Dependency review: remove unused packages from pubspec.yaml

**Test:**
```bash
flutter build apk --split-per-abi --analyze-size
# Assert: arm64 APK < 20MB
```

---

## Definition of Done
- [ ] APK < 20MB per ABI
- [ ] Unused dependencies removed
- [ ] PR reviewed by architect
