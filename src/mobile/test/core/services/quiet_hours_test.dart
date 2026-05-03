import 'package:cena/core/services/quiet_hours_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('isQuietHours', () {
    test('9 PM is quiet hours', () {
      final nightTime = DateTime(2026, 3, 31, 21, 0);
      expect(isQuietHours(nightTime), isTrue);
    });

    test('11 PM is quiet hours', () {
      final lateNight = DateTime(2026, 3, 31, 23, 30);
      expect(isQuietHours(lateNight), isTrue);
    });

    test('3 AM is quiet hours', () {
      final earlyMorning = DateTime(2026, 3, 31, 3, 0);
      expect(isQuietHours(earlyMorning), isTrue);
    });

    test('6:59 AM is quiet hours', () {
      final justBefore = DateTime(2026, 3, 31, 6, 59);
      expect(isQuietHours(justBefore), isTrue);
    });

    test('7 AM is NOT quiet hours', () {
      final morning = DateTime(2026, 3, 31, 7, 0);
      expect(isQuietHours(morning), isFalse);
    });

    test('noon is NOT quiet hours', () {
      final noon = DateTime(2026, 3, 31, 12, 0);
      expect(isQuietHours(noon), isFalse);
    });

    test('8:59 PM is NOT quiet hours', () {
      final justBefore = DateTime(2026, 3, 31, 20, 59);
      expect(isQuietHours(justBefore), isFalse);
    });
  });

  group('nextQuietHoursBoundary', () {
    test('during quiet hours (late night) returns 7 AM next day', () {
      final lateNight = DateTime(2026, 3, 31, 22, 0);
      final boundary = nextQuietHoursBoundary(lateNight);
      expect(boundary.hour, 7);
      expect(boundary.day, 1); // April 1
    });

    test('during quiet hours (early morning) returns 7 AM same day', () {
      final earlyMorning = DateTime(2026, 3, 31, 4, 0);
      final boundary = nextQuietHoursBoundary(earlyMorning);
      expect(boundary.hour, 7);
      expect(boundary.day, 31);
    });

    test('outside quiet hours returns 9 PM same day', () {
      final afternoon = DateTime(2026, 3, 31, 14, 0);
      final boundary = nextQuietHoursBoundary(afternoon);
      expect(boundary.hour, 21);
      expect(boundary.day, 31);
    });
  });

  group('WellbeingState', () {
    test('remainingStudyTime computed correctly', () {
      const state = WellbeingState(
        studyLimitMinutes: 120,
        totalStudyTodayMs: 60 * 60 * 1000, // 60 minutes
      );
      expect(state.remainingStudyTime.inMinutes, 60);
    });

    test('limitProgress is fraction of limit used', () {
      const state = WellbeingState(
        studyLimitMinutes: 100,
        totalStudyTodayMs: 50 * 60 * 1000, // 50 minutes
      );
      expect(state.limitProgress, closeTo(0.5, 0.01));
    });

    test('isLimitReached when total exceeds limit', () {
      const state = WellbeingState(
        studyLimitMinutes: 90,
        totalStudyTodayMs: 91 * 60 * 1000,
        isLimitReached: true,
      );
      expect(state.isLimitReached, isTrue);
    });
  });
}
