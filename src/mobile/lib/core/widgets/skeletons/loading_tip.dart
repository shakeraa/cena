// =============================================================================
// Cena Adaptive Learning Platform — Loading Tips (MOB-039)
// =============================================================================
//
// Educational "Did you know..." facts displayed below skeleton screens during
// longer loading waits (> 2 seconds). A random tip is selected each time the
// widget is built.
// =============================================================================

import 'dart:math';

import 'package:flutter/material.dart';

import '../../config/app_config.dart';

/// A "Did you know?" learning tip shown during extended loading states.
///
/// The tip only appears after [delay] (default 2 seconds). Before that,
/// the widget renders as an empty [SizedBox]. Each instance randomly
/// selects a tip from [_tips] using a seeded [Random].
class LoadingTip extends StatefulWidget {
  const LoadingTip({
    super.key,
    this.delay = const Duration(seconds: 2),
  });

  /// How long to wait before showing the tip.
  final Duration delay;

  @override
  State<LoadingTip> createState() => _LoadingTipState();
}

class _LoadingTipState extends State<LoadingTip> {
  bool _visible = false;
  late final int _tipIndex;

  @override
  void initState() {
    super.initState();
    _tipIndex = Random().nextInt(_tips.length);
    Future.delayed(widget.delay, () {
      if (mounted) setState(() => _visible = true);
    });
  }

  @override
  Widget build(BuildContext context) {
    if (!_visible) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final tip = _tips[_tipIndex];

    return AnimatedOpacity(
      opacity: _visible ? 1.0 : 0.0,
      duration: const Duration(milliseconds: 400),
      child: Container(
        margin: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.md,
          vertical: SpacingTokens.sm,
        ),
        padding: const EdgeInsets.all(SpacingTokens.md),
        decoration: BoxDecoration(
          color: theme.colorScheme.primaryContainer.withValues(alpha: 0.3),
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          border: Border.all(
            color: theme.colorScheme.primary.withValues(alpha: 0.2),
          ),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(
              Icons.lightbulb_outline_rounded,
              size: 20,
              color: theme.colorScheme.primary,
            ),
            const SizedBox(width: SpacingTokens.sm),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    tip.en,
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.onSurface,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                  if (tip.he != null) ...[
                    const SizedBox(height: SpacingTokens.xs),
                    Text(
                      tip.he!,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                      textDirection: TextDirection.rtl,
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// A learning tip with English text and optional Hebrew translation.
class _LearningTip {
  const _LearningTip(this.en, {this.he});
  final String en;
  final String? he;
}

/// Collection of educational tips shown during loading states.
const List<_LearningTip> _tips = [
  _LearningTip(
    'Spaced repetition helps you remember 90% more after 30 days.',
    he: 'חזרה מרווחת עוזרת לזכור 90% יותר אחרי 30 יום.',
  ),
  _LearningTip(
    'Testing yourself is more effective than re-reading notes.',
    he: 'בחינה עצמית יעילה יותר מקריאה חוזרת של סיכומים.',
  ),
  _LearningTip(
    'Sleep consolidates memories — studying before bed helps retention.',
    he: 'שינה מחזקת זיכרון — לימוד לפני השינה משפר שימור.',
  ),
  _LearningTip(
    'Interleaving topics (mixing subjects) improves problem-solving skills.',
    he: 'שילוב נושאים (ערבוב מקצועות) משפר כישורי פתרון בעיות.',
  ),
  _LearningTip(
    'The "testing effect": retrieving information strengthens memory more than reviewing it.',
    he: '"אפקט הבחינה": שליפת מידע מחזקת זיכרון יותר מחזרה עליו.',
  ),
  _LearningTip(
    'Breaking study sessions into 25-minute blocks (Pomodoro) boosts focus.',
    he: 'חלוקת זמן לימוד לבלוקים של 25 דקות (פומודורו) משפרת ריכוז.',
  ),
  _LearningTip(
    'Teaching a concept to someone else is one of the best ways to learn it.',
    he: 'ללמד מישהו אחר הוא אחת הדרכים הטובות ביותר ללמוד.',
  ),
  _LearningTip(
    'Your brain forms new neural pathways every time you practice a skill.',
    he: 'המוח יוצר מסלולים עצביים חדשים בכל פעם שאתה מתרגל מיומנות.',
  ),
  _LearningTip(
    'Mistakes activate more brain regions than correct answers — embrace errors!',
    he: 'טעויות מפעילות יותר אזורים במוח מתשובות נכונות — חבקו טעויות!',
  ),
  _LearningTip(
    'Exercise before studying increases blood flow to the brain and improves focus.',
    he: 'פעילות גופנית לפני לימוד מגבירה זרימת דם למוח ומשפרת ריכוז.',
  ),
  _LearningTip(
    'Handwriting notes activates deeper processing than typing them.',
    he: 'כתיבת סיכומים ביד מפעילה עיבוד עמוק יותר מהקלדה.',
  ),
  _LearningTip(
    'Connecting new concepts to what you already know creates stronger memories.',
    he: 'חיבור מושגים חדשים למה שאתה כבר יודע יוצר זיכרונות חזקים יותר.',
  ),
  _LearningTip(
    'The brain can focus intensely for about 90 minutes before needing a break.',
    he: 'המוח יכול להתרכז לכ-90 דקות לפני שצריך הפסקה.',
  ),
  _LearningTip(
    'Visualization: picturing a concept in your mind improves understanding.',
    he: 'ויזואליזציה: דמיון של מושג בראש משפר הבנה.',
  ),
  _LearningTip(
    'Drinking water helps cognition — even mild dehydration affects focus.',
    he: 'שתיית מים עוזרת לחשיבה — גם התייבשות קלה פוגעת בריכוז.',
  ),
  _LearningTip(
    'Elaborative interrogation — asking "why?" and "how?" — deepens understanding.',
    he: 'שאילת "למה?" ו"איך?" מעמיקה הבנה.',
  ),
  _LearningTip(
    'Consistent daily practice beats occasional cramming sessions.',
    he: 'תרגול יומי עקבי מנצח מפגשי למידה אינטנסיביים מדי פעם.',
  ),
  _LearningTip(
    'Dual coding: combining words and images helps memory encoding.',
    he: 'קידוד כפול: שילוב מילים ותמונות עוזר לקידוד זיכרון.',
  ),
  _LearningTip(
    'Growth mindset: believing you can improve actually helps you improve.',
    he: 'חשיבה צמיחתית: האמונה שאפשר להשתפר באמת עוזרת להשתפר.',
  ),
  _LearningTip(
    'Background music without lyrics can improve concentration while studying.',
    he: 'מוזיקת רקע בלי מילים יכולה לשפר ריכוז בזמן לימוד.',
  ),
  _LearningTip(
    'The "generation effect": creating your own examples helps you remember better.',
    he: '"אפקט היצירה": יצירת דוגמאות משלך עוזרת לזכור טוב יותר.',
  ),
  _LearningTip(
    'Reviewing material within 24 hours of learning it dramatically reduces forgetting.',
    he: 'חזרה על חומר תוך 24 שעות מהלמידה מפחיתה שכחה בצורה דרמטית.',
  ),
];
