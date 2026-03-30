// =============================================================================
// Cena Adaptive Learning Platform — Answer Input
// Adaptive input control for MCQ, free-text, numeric, and proof questions.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../core/config/app_config.dart';
import '../../../core/models/domain_models.dart';

/// Provides the submit and skip controls for the active question.
///
/// For MCQ questions [selectedOptionIndex] must be non-null to enable submit.
/// For text-based types the internal [TextEditingController] drives enablement.
/// Calls [onSubmit] with the string answer and elapsed milliseconds, and
/// [onSkip] with an optional reason string.
class AnswerInput extends StatefulWidget {
  const AnswerInput({
    super.key,
    required this.questionType,
    required this.onSubmit,
    required this.onSkip,
    this.selectedOptionIndex,
    this.isSubmitting = false,
  });

  final QuestionType questionType;

  /// Index of selected MCQ option (null when nothing selected).
  final int? selectedOptionIndex;

  /// Called with (answer string, timeSpentMs) when submit is tapped.
  final void Function(String answer, int timeSpentMs) onSubmit;

  /// Called with optional reason when skip is confirmed.
  final void Function(String? reason) onSkip;

  /// True while the server is evaluating the answer.
  final bool isSubmitting;

  @override
  State<AnswerInput> createState() => _AnswerInputState();
}

class _AnswerInputState extends State<AnswerInput> {
  final _controller = TextEditingController();
  final _numericController = TextEditingController();
  String _selectedUnit = '';
  late final DateTime _questionStartTime;

  @override
  void initState() {
    super.initState();
    _questionStartTime = DateTime.now();
    _controller.addListener(_onTextChanged);
    _numericController.addListener(_onTextChanged);
  }

  @override
  void dispose() {
    _controller
      ..removeListener(_onTextChanged)
      ..dispose();
    _numericController
      ..removeListener(_onTextChanged)
      ..dispose();
    super.dispose();
  }

  void _onTextChanged() => setState(() {});

  bool get _canSubmit {
    if (widget.isSubmitting) return false;
    switch (widget.questionType) {
      case QuestionType.multipleChoice:
        return widget.selectedOptionIndex != null;
      case QuestionType.numeric:
        return _numericController.text.trim().isNotEmpty;
      case QuestionType.freeText:
      case QuestionType.proof:
        return _controller.text.trim().isNotEmpty;
      case QuestionType.diagram:
        // Diagram answers via drawn canvas are not yet supported;
        // the submit button remains enabled to allow free-text annotations.
        return _controller.text.trim().isNotEmpty;
    }
  }

  String get _answerString {
    switch (widget.questionType) {
      case QuestionType.multipleChoice:
        return '${widget.selectedOptionIndex}';
      case QuestionType.numeric:
        final value = _numericController.text.trim();
        return _selectedUnit.isEmpty ? value : '$value $_selectedUnit';
      case QuestionType.freeText:
      case QuestionType.proof:
      case QuestionType.diagram:
        return _controller.text.trim();
    }
  }

  int get _timeSpentMs =>
      DateTime.now().difference(_questionStartTime).inMilliseconds;

  void _submit() {
    if (!_canSubmit) return;
    widget.onSubmit(_answerString, _timeSpentMs);
  }

  Future<void> _confirmSkip() async {
    final reason = await showDialog<String>(
      context: context,
      builder: (_) => const _SkipDialog(),
    );
    // reason is null when dismissed without confirming; 'skip' means confirmed
    if (reason != null) {
      widget.onSkip(reason == 'skip' ? null : reason);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isMcq = widget.questionType == QuestionType.multipleChoice;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // Text input — shown for non-MCQ types
        if (!isMcq) ...[
          _buildTextInput(theme, colorScheme),
          const SizedBox(height: SpacingTokens.md),
        ],

        // Action row: Skip (secondary) + Submit (primary)
        Row(
          children: [
            OutlinedButton.icon(
              onPressed: widget.isSubmitting ? null : _confirmSkip,
              icon: const Icon(Icons.skip_next_rounded, size: 18),
              label: const Text('דלג'),
              style: OutlinedButton.styleFrom(
                foregroundColor: colorScheme.onSurfaceVariant,
                padding: const EdgeInsets.symmetric(
                  horizontal: SpacingTokens.md,
                  vertical: SpacingTokens.sm,
                ),
              ),
            ),
            const SizedBox(width: SpacingTokens.sm),
            Expanded(
              child: FilledButton.icon(
                onPressed: _canSubmit ? _submit : null,
                icon: widget.isSubmitting
                    ? SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(
                          strokeWidth: 2,
                          color: colorScheme.onPrimary,
                        ),
                      )
                    : const Icon(Icons.send_rounded, size: 18),
                label: Text(widget.isSubmitting ? 'בודק...' : 'שלח תשובה'),
                style: FilledButton.styleFrom(
                  padding: const EdgeInsets.symmetric(
                    vertical: SpacingTokens.sm,
                  ),
                ),
              ),
            ),
          ],
        ),
      ],
    );
  }

  Widget _buildTextInput(ThemeData theme, ColorScheme colorScheme) {
    if (widget.questionType == QuestionType.numeric) {
      return _NumericInput(
        controller: _numericController,
        selectedUnit: _selectedUnit,
        onUnitChanged: (u) => setState(() => _selectedUnit = u),
        isEnabled: !widget.isSubmitting,
      );
    }

    final isProof = widget.questionType == QuestionType.proof;

    return TextField(
      controller: _controller,
      enabled: !widget.isSubmitting,
      maxLines: isProof ? null : 4,
      minLines: isProof ? 5 : 2,
      textDirection: Directionality.of(context),
      keyboardType:
          isProof ? TextInputType.multiline : TextInputType.text,
      textInputAction:
          isProof ? TextInputAction.newline : TextInputAction.done,
      decoration: InputDecoration(
        hintText: isProof ? 'כתוב את הוכחתך כאן...' : 'כתוב את תשובתך כאן...',
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
        ),
        filled: true,
        fillColor: colorScheme.surfaceContainerLowest,
      ),
      style: theme.textTheme.bodyLarge,
    );
  }
}

// ---------------------------------------------------------------------------
// Numeric input with unit selector
// ---------------------------------------------------------------------------

class _NumericInput extends StatelessWidget {
  const _NumericInput({
    required this.controller,
    required this.selectedUnit,
    required this.onUnitChanged,
    required this.isEnabled,
  });

  final TextEditingController controller;
  final String selectedUnit;
  final ValueChanged<String> onUnitChanged;
  final bool isEnabled;

  static const List<String> _units = [
    '',
    'm',
    'cm',
    'km',
    'kg',
    'g',
    's',
    'ms',
    'N',
    'J',
    'W',
    'Pa',
    'Hz',
    'mol',
    'K',
    '°C',
    'm/s',
    'm/s²',
  ];

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          flex: 3,
          child: TextField(
            controller: controller,
            enabled: isEnabled,
            keyboardType:
                const TextInputType.numberWithOptions(decimal: true, signed: true),
            inputFormatters: [
              FilteringTextInputFormatter.allow(
                RegExp(r'^-?\d*\.?\d*$'),
              ),
            ],
            textAlign: TextAlign.center,
            decoration: InputDecoration(
              hintText: '0.0',
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(RadiusTokens.lg),
              ),
              filled: true,
              fillColor: colorScheme.surfaceContainerLowest,
            ),
          ),
        ),
        const SizedBox(width: SpacingTokens.sm),
        Expanded(
          flex: 2,
          child: DropdownButtonFormField<String>(
            value: selectedUnit,
            isExpanded: true,
            decoration: InputDecoration(
              labelText: 'יחידה',
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(RadiusTokens.lg),
              ),
              filled: true,
              fillColor: colorScheme.surfaceContainerLowest,
            ),
            items: _units
                .map((u) => DropdownMenuItem(
                      value: u,
                      child: Text(u.isEmpty ? 'ללא' : u),
                    ))
                .toList(),
            onChanged: isEnabled
                ? (v) => onUnitChanged(v ?? '')
                : null,
          ),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Skip confirmation dialog
// ---------------------------------------------------------------------------

class _SkipDialog extends StatelessWidget {
  const _SkipDialog();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    return AlertDialog(
      title: const Text('דלג על שאלה?'),
      content: const Text(
        'דילוג על שאלה לא ייחשב כטעות, אך גם לא יתרום לשיפור השליטה שלך.',
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(null),
          child: const Text('ביטול'),
        ),
        FilledButton(
          style: FilledButton.styleFrom(
            backgroundColor: colorScheme.secondary,
          ),
          onPressed: () => Navigator.of(context).pop('skip'),
          child: const Text('דלג'),
        ),
      ],
    );
  }
}
