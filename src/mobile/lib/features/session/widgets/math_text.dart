// =============================================================================
// Cena Adaptive Learning Platform — Math Text Widget
// Renders mixed plain text + LaTeX using flutter_math_fork.
// Supports $...$ (inline) and $$...$$ (display) math delimiters.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_math_fork/flutter_math.dart';

import '../../../core/config/app_config.dart';

/// A segment of parsed content — either plain text or LaTeX math.
class _Segment {
  const _Segment.text(this.content)
      : isDisplay = false,
        isMath = false;

  const _Segment.inlineMath(this.content)
      : isDisplay = false,
        isMath = true;

  const _Segment.displayMath(this.content)
      : isDisplay = true,
        isMath = true;

  final String content;
  final bool isMath;
  final bool isDisplay;
}

/// Renders a string containing mixed plain text and LaTeX math.
///
/// - `$...$`  delimiters produce inline math (rendered in-line with text).
/// - `$$...$$` delimiters produce display math (centered on its own line).
/// - Plain text segments are rendered with the current theme's [bodyLarge] style.
/// - LaTeX parse errors fall back gracefully to raw text in monospace.
/// - RTL layout is supported: the widget respects the ambient [Directionality].
///
/// Usage:
/// ```dart
/// MathText(content: r'Solve $x^2 + 3x = 0$ for $x$.')
/// ```
class MathText extends StatelessWidget {
  const MathText({
    super.key,
    required this.content,
    this.textStyle,
    this.mathColor,
    this.mathBackground,
  });

  /// The raw string potentially containing `$...$` or `$$...$$` delimiters.
  final String content;

  /// Override the default text style for plain text segments.
  final TextStyle? textStyle;

  /// Color for rendered math expressions. Falls back to
  /// [SubjectColorTokens.mathPrimary] then [ColorScheme.primary].
  final Color? mathColor;

  /// Background color for rendered math expressions. Falls back to
  /// [SubjectColorTokens.mathBackground] then transparent.
  final Color? mathBackground;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final isRtl = Directionality.of(context) == TextDirection.rtl;
    final segments = _parse(content);

    final resolvedTextStyle = textStyle ?? theme.textTheme.bodyLarge;
    final resolvedMathColor = mathColor ?? SubjectColorTokens.mathPrimary;
    final resolvedMathBg = mathBackground ?? SubjectColorTokens.mathBackground;

    // If there are no display-math segments, render everything inline
    // using a Wrap so that inline math flows naturally with text.
    final hasDisplay = segments.any((s) => s.isDisplay);

    if (!hasDisplay) {
      return Directionality(
        textDirection: isRtl ? TextDirection.rtl : TextDirection.ltr,
        child: Wrap(
          crossAxisAlignment: WrapCrossAlignment.center,
          textDirection: isRtl ? TextDirection.rtl : TextDirection.ltr,
          children: segments.map((seg) {
            if (seg.isMath) {
              return _buildInlineMath(
                seg.content,
                resolvedTextStyle,
                resolvedMathColor,
                resolvedMathBg,
              );
            }
            return Text(seg.content, style: resolvedTextStyle);
          }).toList(),
        ),
      );
    }

    // When display math is present, build a Column with mixed widgets.
    return Directionality(
      textDirection: isRtl ? TextDirection.rtl : TextDirection.ltr,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: _buildColumnChildren(
          segments,
          resolvedTextStyle,
          resolvedMathColor,
          resolvedMathBg,
          isRtl,
        ),
      ),
    );
  }

  /// Builds children for the Column layout (when display math exists).
  List<Widget> _buildColumnChildren(
    List<_Segment> segments,
    TextStyle? textStyle,
    Color mathColor,
    Color mathBg,
    bool isRtl,
  ) {
    final children = <Widget>[];

    // Accumulate consecutive inline segments into a Wrap row.
    var inlineBuffer = <Widget>[];

    void flushInlineBuffer() {
      if (inlineBuffer.isNotEmpty) {
        children.add(Wrap(
          crossAxisAlignment: WrapCrossAlignment.center,
          textDirection: isRtl ? TextDirection.rtl : TextDirection.ltr,
          children: inlineBuffer,
        ));
        inlineBuffer = [];
      }
    }

    for (final seg in segments) {
      if (seg.isDisplay) {
        flushInlineBuffer();
        children.add(Padding(
          padding: const EdgeInsets.symmetric(vertical: SpacingTokens.sm),
          child: Center(
            child: _buildDisplayMath(seg.content, textStyle, mathColor, mathBg),
          ),
        ));
      } else if (seg.isMath) {
        inlineBuffer.add(
          _buildInlineMath(seg.content, textStyle, mathColor, mathBg),
        );
      } else {
        inlineBuffer.add(Text(seg.content, style: textStyle));
      }
    }

    flushInlineBuffer();
    return children;
  }

  /// Renders an inline LaTeX expression. On parse error, falls back to
  /// monospace styled raw text.
  Widget _buildInlineMath(
    String tex,
    TextStyle? baseStyle,
    Color mathColor,
    Color mathBg,
  ) {
    final fontSize = baseStyle?.fontSize ?? TypographyTokens.bodyLarge;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 2),
      decoration: BoxDecoration(
        color: mathBg.withValues(alpha: 0.3),
        borderRadius: BorderRadius.circular(RadiusTokens.sm),
      ),
      child: Math.tex(
        tex,
        textStyle: TextStyle(
          fontSize: fontSize,
          color: mathColor,
        ),
        onErrorFallback: (err) => Text(
          '\$$tex\$',
          style: baseStyle?.copyWith(
            fontFamily: TypographyTokens.monoFontFamily,
            color: mathColor,
          ),
        ),
      ),
    );
  }

  /// Renders a display (block-level) LaTeX expression, centered.
  Widget _buildDisplayMath(
    String tex,
    TextStyle? baseStyle,
    Color mathColor,
    Color mathBg,
  ) {
    final fontSize = (baseStyle?.fontSize ?? TypographyTokens.bodyLarge) * 1.2;

    return Container(
      padding: const EdgeInsets.all(SpacingTokens.sm),
      decoration: BoxDecoration(
        color: mathBg.withValues(alpha: 0.2),
        borderRadius: BorderRadius.circular(RadiusTokens.md),
      ),
      child: Math.tex(
        tex,
        textStyle: TextStyle(
          fontSize: fontSize,
          color: mathColor,
        ),
        mathStyle: MathStyle.display,
        onErrorFallback: (err) => Text(
          '\$\$$tex\$\$',
          style: baseStyle?.copyWith(
            fontFamily: TypographyTokens.monoFontFamily,
            color: mathColor,
          ),
          textAlign: TextAlign.center,
        ),
      ),
    );
  }

  /// Parses a content string into a list of [_Segment]s.
  ///
  /// `$$...$$` is matched first (display math), then `$...$` (inline math).
  /// Everything else becomes plain text segments.
  static List<_Segment> _parse(String input) {
    final segments = <_Segment>[];

    // Combined regex: display math ($$...$$) takes priority over inline ($...$).
    // Using non-greedy matching so nested delimiters don't over-match.
    final regex = RegExp(r'\$\$(.+?)\$\$|\$(.+?)\$', dotAll: true);

    int lastEnd = 0;

    for (final match in regex.allMatches(input)) {
      // Add any plain text before this match.
      if (match.start > lastEnd) {
        final text = input.substring(lastEnd, match.start);
        if (text.isNotEmpty) {
          segments.add(_Segment.text(text));
        }
      }

      if (match.group(1) != null) {
        // Display math ($$...$$)
        segments.add(_Segment.displayMath(match.group(1)!.trim()));
      } else if (match.group(2) != null) {
        // Inline math ($...$)
        segments.add(_Segment.inlineMath(match.group(2)!.trim()));
      }

      lastEnd = match.end;
    }

    // Trailing plain text.
    if (lastEnd < input.length) {
      final text = input.substring(lastEnd);
      if (text.isNotEmpty) {
        segments.add(_Segment.text(text));
      }
    }

    return segments;
  }
}
