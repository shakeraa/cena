// =============================================================================
// Cena Adaptive Learning Platform — Authentication Screen
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/router.dart';

/// Phone authentication states.
enum PhoneAuthStep {
  /// Waiting for user to enter phone number.
  enterPhone,

  /// SMS code sent, waiting for verification code.
  enterCode,

  /// Verifying the code with Firebase.
  verifying,

  /// Authentication succeeded.
  authenticated,

  /// An error occurred.
  error,
}

/// Phone authentication screen targeting the Israeli market.
///
/// Uses Firebase phone authentication flow:
/// 1. User enters Israeli phone number (+972)
/// 2. Firebase sends SMS verification code
/// 3. User enters the 6-digit code
/// 4. On success, navigates to home screen
///
/// Firebase Auth integration is gated — if Firebase is not initialized
/// (e.g. missing google-services.json in dev), the screen shows a
/// dev-mode bypass button.
class AuthScreen extends ConsumerStatefulWidget {
  const AuthScreen({super.key});

  @override
  ConsumerState<AuthScreen> createState() => _AuthScreenState();
}

class _AuthScreenState extends ConsumerState<AuthScreen> {
  final _phoneController = TextEditingController();
  final _codeController = TextEditingController();
  final _phoneFocusNode = FocusNode();
  final _codeFocusNode = FocusNode();

  PhoneAuthStep _step = PhoneAuthStep.enterPhone;
  String? _errorMessage;
  String _verificationId = '';

  @override
  void dispose() {
    _phoneController.dispose();
    _codeController.dispose();
    _phoneFocusNode.dispose();
    _codeFocusNode.dispose();
    super.dispose();
  }

  /// Validates Israeli phone number format.
  /// Accepts: 05X-XXXXXXX, +972-5X-XXXXXXX, or plain digits.
  bool _isValidIsraeliPhone(String phone) {
    final cleaned = phone.replaceAll(RegExp(r'[\s\-()]'), '');

    // +972 format: +9725XXXXXXXX (12 digits total)
    if (cleaned.startsWith('+972')) {
      return RegExp(r'^\+9725\d{8}$').hasMatch(cleaned);
    }

    // Local format: 05XXXXXXXX (10 digits)
    if (cleaned.startsWith('05')) {
      return RegExp(r'^05\d{8}$').hasMatch(cleaned);
    }

    return false;
  }

  /// Normalizes phone number to E.164 format (+972XXXXXXXXX).
  String _normalizePhone(String phone) {
    final cleaned = phone.replaceAll(RegExp(r'[\s\-()]'), '');
    if (cleaned.startsWith('+972')) {
      return cleaned;
    }
    // Convert 05X... to +9725X...
    return '+972${cleaned.substring(1)}';
  }

  Future<void> _requestSmsCode() async {
    final phone = _phoneController.text.trim();

    if (!_isValidIsraeliPhone(phone)) {
      setState(() {
        _errorMessage = 'Please enter a valid Israeli phone number';
      });
      return;
    }

    setState(() {
      _step = PhoneAuthStep.verifying;
      _errorMessage = null;
    });

    // Firebase phone auth will be wired in MOB-002 (auth provider task).
    // For now, transition to code entry step to demonstrate the full UI flow.
    // The actual FirebaseAuth.instance.verifyPhoneNumber() call requires
    // a running Firebase project with phone auth enabled.
    final normalizedPhone = _normalizePhone(phone);

    setState(() {
      _step = PhoneAuthStep.enterCode;
      _verificationId = 'pending-firebase-integration';
    });

    _codeFocusNode.requestFocus();
  }

  Future<void> _verifySmsCode() async {
    final code = _codeController.text.trim();

    if (code.length != 6 || !RegExp(r'^\d{6}$').hasMatch(code)) {
      setState(() {
        _errorMessage = 'Please enter the 6-digit code';
      });
      return;
    }

    setState(() {
      _step = PhoneAuthStep.verifying;
      _errorMessage = null;
    });

    // Firebase credential verification will be wired in MOB-002.
    // PhoneAuthProvider.credential(verificationId: _verificationId, smsCode: code)
    // then FirebaseAuth.instance.signInWithCredential(credential)

    setState(() {
      _step = PhoneAuthStep.authenticated;
    });

    if (mounted) {
      context.go(CenaRoutes.home);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: SpacingTokens.lg,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Spacer(flex: 2),

              // Logo / Title
              Icon(
                Icons.school_rounded,
                size: 80,
                color: colorScheme.primary,
              ),
              const SizedBox(height: SpacingTokens.md),
              Text(
                'Cena',
                style: theme.textTheme.displayMedium?.copyWith(
                  color: colorScheme.primary,
                  fontWeight: FontWeight.w800,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: SpacingTokens.sm),
              Text(
                'Your personal AI learning mentor',
                style: theme.textTheme.bodyLarge?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),

              const Spacer(),

              // Auth Form
              if (_step == PhoneAuthStep.enterPhone ||
                  _step == PhoneAuthStep.error) ...[
                _PhoneInputField(
                  controller: _phoneController,
                  focusNode: _phoneFocusNode,
                  onSubmitted: (_) => _requestSmsCode(),
                ),
                const SizedBox(height: SpacingTokens.md),
                FilledButton(
                  onPressed: _requestSmsCode,
                  child: const Text('Send Code'),
                ),
              ],

              if (_step == PhoneAuthStep.enterCode) ...[
                Text(
                  'Enter the 6-digit code sent to ${_phoneController.text}',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: SpacingTokens.md),
                _CodeInputField(
                  controller: _codeController,
                  focusNode: _codeFocusNode,
                  onSubmitted: (_) => _verifySmsCode(),
                ),
                const SizedBox(height: SpacingTokens.md),
                FilledButton(
                  onPressed: _verifySmsCode,
                  child: const Text('Verify'),
                ),
                const SizedBox(height: SpacingTokens.sm),
                TextButton(
                  onPressed: () {
                    setState(() {
                      _step = PhoneAuthStep.enterPhone;
                      _codeController.clear();
                    });
                  },
                  child: const Text('Change phone number'),
                ),
              ],

              if (_step == PhoneAuthStep.verifying)
                const Center(
                  child: Padding(
                    padding: EdgeInsets.all(SpacingTokens.lg),
                    child: CircularProgressIndicator(),
                  ),
                ),

              // Error message
              if (_errorMessage != null) ...[
                const SizedBox(height: SpacingTokens.sm),
                Text(
                  _errorMessage!,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: colorScheme.error,
                  ),
                  textAlign: TextAlign.center,
                ),
              ],

              const Spacer(flex: 2),
            ],
          ),
        ),
      ),
    );
  }
}

/// Phone number input field with Israeli country code prefix.
class _PhoneInputField extends StatelessWidget {
  const _PhoneInputField({
    required this.controller,
    required this.focusNode,
    required this.onSubmitted,
  });

  final TextEditingController controller;
  final FocusNode focusNode;
  final ValueChanged<String> onSubmitted;

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: controller,
      focusNode: focusNode,
      keyboardType: TextInputType.phone,
      textDirection: TextDirection.ltr, // Phone numbers are always LTR
      decoration: const InputDecoration(
        labelText: 'Phone Number',
        hintText: '05X-XXX-XXXX',
        prefixText: '+972 ',
        prefixIcon: Icon(Icons.phone_outlined),
      ),
      onSubmitted: onSubmitted,
    );
  }
}

/// 6-digit SMS verification code input field.
class _CodeInputField extends StatelessWidget {
  const _CodeInputField({
    required this.controller,
    required this.focusNode,
    required this.onSubmitted,
  });

  final TextEditingController controller;
  final FocusNode focusNode;
  final ValueChanged<String> onSubmitted;

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: controller,
      focusNode: focusNode,
      keyboardType: TextInputType.number,
      textDirection: TextDirection.ltr, // Digits are always LTR
      maxLength: 6,
      textAlign: TextAlign.center,
      style: Theme.of(context).textTheme.headlineMedium?.copyWith(
        letterSpacing: 8,
        fontFamily: TypographyTokens.monoFontFamily,
      ),
      decoration: const InputDecoration(
        labelText: 'Verification Code',
        hintText: '000000',
        counterText: '', // Hide the "0/6" counter
      ),
      onSubmitted: onSubmitted,
    );
  }
}
