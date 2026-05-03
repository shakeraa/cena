// =============================================================================
// FIND-pedagogy-004 — ChallengeOption localization unit tests
//
// Asserts the locale-keyed ChallengeOption model resolves text correctly
// via the fallback chain (locale → en → he → first available) and that
// `challengeCardSupportsLocale` refuses to render Hebrew content to
// English-locale students (i.e. the feature is hidden when locale='en'
// and at least one option lacks an English translation).
//
// Citations for the decision under test:
//   - Project user memory: feedback_language_strategy ("English primary,
//     Arabic/Hebrew secondary, Hebrew hideable outside Israel", 2026).
//   - August, D. & Shanahan, T. (Eds.) (2006). "Developing Literacy in
//     Second-Language Learners." Lawrence Erlbaum. ISBN 978-0805860788.
//     Comprehension feedback must be delivered in the learner's language
//     of instruction.
// =============================================================================

import 'package:cena/features/diagrams/models/diagram_models.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('ChallengeOption.resolveLocalized (fallback chain)', () {
    test('prefers current locale when present', () {
      final result = ChallengeOption.resolveLocalized(
        {'en': 'English text', 'he': 'טקסט עברי', 'ar': 'نص عربي'},
        'en',
      );
      expect(result, 'English text');
    });

    test('falls back from requested locale to en', () {
      final result = ChallengeOption.resolveLocalized(
        {'en': 'English text', 'he': 'טקסט עברי'},
        'fr', // no French content
      );
      expect(result, 'English text');
    });

    test('falls back from requested locale through en to he', () {
      final result = ChallengeOption.resolveLocalized(
        {'he': 'טקסט עברי'},
        'en', // no English content — en is not present, falls to he
      );
      expect(result, 'טקסט עברי');
    });

    test('returns null for null or empty map', () {
      expect(ChallengeOption.resolveLocalized(null, 'en'), isNull);
      expect(ChallengeOption.resolveLocalized(<String, String>{}, 'en'), isNull);
    });
  });

  group('ChallengeOption.fromJson (backward compat)', () {
    test('parses new locale-keyed shape', () {
      final opt = ChallengeOption.fromJson({
        'id': 'o1',
        'text': {'en': 'Ohm\'s law', 'he': 'חוק אוהם'},
        'isCorrect': true,
        'feedback': {'en': 'Right — V = I × R', 'he': 'נכון — V = I × R'},
      });
      expect(opt.id, 'o1');
      expect(opt.isCorrect, true);
      expect(opt.localizedText('en'), "Ohm's law");
      expect(opt.localizedText('he'), 'חוק אוהם');
      expect(opt.localizedFeedback('en'), 'Right — V = I × R');
    });

    test('migrates legacy textHe/feedbackHe into he-keyed map', () {
      final opt = ChallengeOption.fromJson({
        'id': 'legacy1',
        'textHe': 'שאלת בדיקה',
        'isCorrect': false,
        'feedbackHe': 'לא נכון',
      });
      expect(opt.text, {'he': 'שאלת בדיקה'});
      expect(opt.feedback, {'he': 'לא נכון'});
      expect(opt.hasEnglishText, isFalse);
    });

    test('new map shape wins over legacy fields when both present', () {
      final opt = ChallengeOption.fromJson({
        'id': 'mix',
        'text': {'en': 'New', 'he': 'חדש'},
        'textHe': 'legacy hebrew',
        'isCorrect': true,
      });
      expect(opt.localizedText('en'), 'New');
      expect(opt.localizedText('he'), 'חדש');
    });
  });

  group('challengeCardSupportsLocale (English-locale guard)', () {
    ChallengeCard buildCard(List<ChallengeOption> options) => ChallengeCard(
          id: 'c1',
          diagram: ConceptDiagram(
            id: 'd1',
            conceptId: 'concept',
            subject: 'physics',
            type: DiagramType.challengeCard,
            format: DiagramFormat.svg,
            bloomLevel: 'apply',
            assetUrl: 'https://cdn/x.svg',
            thumbnailUrl: 'https://cdn/x_thumb.svg',
            titleHe: 'כותרת',
            descriptionHe: 'תיאור',
            generationMeta: DiagramGenerationMeta(
              model: 'kimi-k2.5',
              generatedAt: DateTime.parse('2026-04-11T00:00:00Z'),
              curriculumVersion: 'v1',
              reviewStatus: DiagramReviewStatus.approved,
            ),
            cacheMeta: DiagramCacheMeta(
              s3Key: 'x',
              cdnUrl: 'https://cdn/x.svg',
              contentHash: 'abc',
              sizeBytes: 1,
              publishedAt: DateTime.parse('2026-04-11T00:00:00Z'),
            ),
          ),
          tier: ChallengeTier.beginner,
          questionHe: 'שאלה',
          answerType: ChallengeAnswerType.multipleChoice,
          options: options,
        );

    test('returns true for en locale when every option has English text', () {
      final card = buildCard([
        const ChallengeOption(
          id: 'o1',
          text: {'en': 'Option one', 'he': 'אפשרות ראשונה'},
          isCorrect: true,
        ),
        const ChallengeOption(
          id: 'o2',
          text: {'en': 'Option two', 'he': 'אפשרות שנייה'},
          isCorrect: false,
        ),
      ]);
      expect(challengeCardSupportsLocale(card, 'en'), isTrue);
    });

    test('returns false for en locale when any option is Hebrew-only', () {
      final card = buildCard([
        const ChallengeOption(
          id: 'o1',
          text: {'en': 'Option one', 'he': 'אפשרות ראשונה'},
          isCorrect: true,
        ),
        const ChallengeOption(
          id: 'o2',
          text: {'he': 'אפשרות שנייה'}, // no English — must hide card
          isCorrect: false,
        ),
      ]);
      expect(challengeCardSupportsLocale(card, 'en'), isFalse,
          reason: 'Must hide feature to avoid Hebrew leaking to English students');
    });

    test('returns true for he locale when options have Hebrew text', () {
      final card = buildCard([
        const ChallengeOption(
          id: 'o1',
          text: {'he': 'אפשרות ראשונה'},
          isCorrect: true,
        ),
      ]);
      expect(challengeCardSupportsLocale(card, 'he'), isTrue);
    });

    test('returns false for any locale when options list is empty', () {
      final card = buildCard(<ChallengeOption>[]);
      expect(challengeCardSupportsLocale(card, 'en'), isFalse);
      expect(challengeCardSupportsLocale(card, 'he'), isFalse);
    });
  });
}
