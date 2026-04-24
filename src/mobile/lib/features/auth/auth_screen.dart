// =============================================================================
// Cena Adaptive Learning Platform — Authentication Screen
// Multi-provider: Google, Apple, Phone (Israeli market)
// =============================================================================

import 'package:firebase_auth/firebase_auth.dart' show FirebaseAuthException;
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/router.dart';
import '../../core/services/auth_service.dart';
import '../../l10n/app_localizations.dart';

/// Authentication step for phone flow.
enum PhoneAuthStep { enterPhone, enterCode }

/// Authentication screen with Google, Apple, and Phone sign-in.
///
/// Best-practice mobile auth:
/// - Social buttons (Google + Apple) shown prominently — lowest friction
/// - Apple Sign-In on both platforms (native on iOS, OAuthProvider on Android)
/// - Phone auth available as secondary option for Israeli market (+972)
/// - Phase 1: iOS, Prod: iOS + Android
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

  bool _showPhoneAuth = false;
  PhoneAuthStep _phoneStep = PhoneAuthStep.enterPhone;
  String _verificationId = '';

  @override
  void dispose() {
    _phoneController.dispose();
    _codeController.dispose();
    _phoneFocusNode.dispose();
    _codeFocusNode.dispose();
    super.dispose();
  }

  // ---------- Social Sign-In ----------

  Future<void> _signInWithGoogle() async {
    await ref.read(authNotifierProvider.notifier).signInWithGoogle();
  }

  Future<void> _signInWithApple() async {
    await ref.read(authNotifierProvider.notifier).signInWithApple();
  }

  // ---------- Phone Auth ----------

  bool _isValidIsraeliPhone(String phone) {
    final cleaned = phone.replaceAll(RegExp(r'[\s\-()]'), '');
    if (cleaned.startsWith('+972')) {
      return RegExp(r'^\+9725\d{8}$').hasMatch(cleaned);
    }
    if (cleaned.startsWith('05')) {
      return RegExp(r'^05\d{8}$').hasMatch(cleaned);
    }
    return false;
  }

  String _normalizePhone(String phone) {
    final cleaned = phone.replaceAll(RegExp(r'[\s\-()]'), '');
    if (cleaned.startsWith('+972')) return cleaned;
    return '+972${cleaned.substring(1)}';
  }

  Future<void> _requestSmsCode() async {
    final phone = _phoneController.text.trim();
    if (!_isValidIsraeliPhone(phone)) {
      ref.read(authNotifierProvider.notifier).clearError();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
            content: Text(AppLocalizations.of(context).invalidPhone)),
      );
      return;
    }

    final normalizedPhone = _normalizePhone(phone);
    final authService = ref.read(authServiceProvider);

    try {
      _verificationId = await authService.sendPhoneVerificationCode(
        phoneNumber: normalizedPhone,
        timeout: const Duration(seconds: 60),
      );
      setState(() => _phoneStep = PhoneAuthStep.enterCode);
      _codeFocusNode.requestFocus();
    } on FirebaseAuthException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(AuthService.mapAuthError(e.code))),
        );
      }
    }
  }

  Future<void> _verifySmsCode() async {
    final code = _codeController.text.trim();
    if (code.length != 6 || !RegExp(r'^\d{6}$').hasMatch(code)) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(AppLocalizations.of(context).enterSixDigitCode)),
      );
      return;
    }

    await ref.read(authNotifierProvider.notifier).verifyPhoneCode(
          verificationId: _verificationId,
          smsCode: code,
        );
  }

  // ---------- Navigation on auth success ----------

  void _onAuthSuccess() {
    if (mounted) context.go(CenaRoutes.home);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    // Listen to auth state — navigate on success, show errors
    ref.listen<AuthState>(authNotifierProvider, (previous, next) {
      if (next is AuthAuthenticated) {
        _onAuthSuccess();
      } else if (next is AuthError) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(next.message),
            backgroundColor: colorScheme.error,
          ),
        );
      }
    });

    final authState = ref.watch(authNotifierProvider);
    final isLoading = authState is AuthLoading;
    final l = AppLocalizations.of(context);

    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Spacer(flex: 2),

              // Logo
              Icon(
                Icons.school_rounded,
                size: 80,
                color: colorScheme.primary,
              ),
              const SizedBox(height: SpacingTokens.md),
              Text(
                l.appTitle,
                style: theme.textTheme.displayMedium?.copyWith(
                  color: colorScheme.primary,
                  fontWeight: FontWeight.w800,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: SpacingTokens.sm),
              Text(
                l.yourPersonalMentor,
                style: theme.textTheme.bodyLarge?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),

              const Spacer(),

              if (isLoading)
                const Center(
                  child: Padding(
                    padding: EdgeInsets.all(SpacingTokens.lg),
                    child: CircularProgressIndicator(),
                  ),
                )
              else if (_showPhoneAuth)
                _buildPhoneAuthSection(theme, colorScheme)
              else
                _buildSocialAuthSection(theme, colorScheme),

              const Spacer(flex: 2),
            ],
          ),
        ),
      ),
    );
  }

  // ---------- Social Auth UI ----------

  Widget _buildSocialAuthSection(ThemeData theme, ColorScheme colorScheme) {
    final l = AppLocalizations.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // Google Sign-In button
        _SocialSignInButton(
          onPressed: _signInWithGoogle,
          icon: Icons.g_mobiledata_rounded,
          label: l.continueWithGoogle,
          backgroundColor: Colors.white,
          foregroundColor: Colors.black87,
        ),
        const SizedBox(height: SpacingTokens.sm),

        // Apple Sign-In — both platforms (native iOS, OAuthProvider Android)
        _SocialSignInButton(
          onPressed: _signInWithApple,
          icon: Icons.apple_rounded,
          label: l.continueWithApple,
          backgroundColor: Colors.black,
          foregroundColor: Colors.white,
        ),

        const SizedBox(height: SpacingTokens.md),

        // Divider
        Row(
          children: [
            Expanded(child: Divider(color: colorScheme.outlineVariant)),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.md),
              child: Text(
                l.or,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
            ),
            Expanded(child: Divider(color: colorScheme.outlineVariant)),
          ],
        ),

        const SizedBox(height: SpacingTokens.md),

        // Phone auth toggle
        OutlinedButton.icon(
          onPressed: () => setState(() => _showPhoneAuth = true),
          icon: const Icon(Icons.phone_outlined),
          label: Text(l.continueWithPhone),
        ),
      ],
    );
  }

  // ---------- Phone Auth UI ----------

  Widget _buildPhoneAuthSection(ThemeData theme, ColorScheme colorScheme) {
    final l = AppLocalizations.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (_phoneStep == PhoneAuthStep.enterPhone) ...[
          _PhoneInputField(
            controller: _phoneController,
            focusNode: _phoneFocusNode,
            onSubmitted: (_) => _requestSmsCode(),
          ),
          const SizedBox(height: SpacingTokens.md),
          FilledButton(
            onPressed: _requestSmsCode,
            child: Text(l.sendCode),
          ),
        ],

        if (_phoneStep == PhoneAuthStep.enterCode) ...[
          Text(
            l.enterCodeSent(_phoneController.text),
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
            child: Text(l.verify),
          ),
          const SizedBox(height: SpacingTokens.sm),
          TextButton(
            onPressed: () {
              setState(() {
                _phoneStep = PhoneAuthStep.enterPhone;
                _codeController.clear();
              });
            },
            child: Text(l.changePhoneNumber),
          ),
        ],

        const SizedBox(height: SpacingTokens.md),

        // Back to social buttons
        TextButton.icon(
          onPressed: () => setState(() {
            _showPhoneAuth = false;
            _phoneStep = PhoneAuthStep.enterPhone;
            _phoneController.clear();
            _codeController.clear();
          }),
          icon: const Icon(Icons.arrow_back, size: 16),
          label: Text(l.otherSignInOptions),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Social Sign-In Button
// ---------------------------------------------------------------------------

class _SocialSignInButton extends StatelessWidget {
  const _SocialSignInButton({
    required this.onPressed,
    required this.icon,
    required this.label,
    required this.backgroundColor,
    required this.foregroundColor,
  });

  final VoidCallback onPressed;
  final IconData icon;
  final String label;
  final Color backgroundColor;
  final Color foregroundColor;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 48,
      child: ElevatedButton.icon(
        onPressed: onPressed,
        icon: Icon(icon, color: foregroundColor, size: 24),
        label: Text(label),
        style: ElevatedButton.styleFrom(
          backgroundColor: backgroundColor,
          foregroundColor: foregroundColor,
          elevation: 1,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(RadiusTokens.md),
            side: BorderSide(color: Colors.grey.shade300),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Phone Input Field
// ---------------------------------------------------------------------------

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
    final l = AppLocalizations.of(context);
    return TextField(
      controller: controller,
      focusNode: focusNode,
      keyboardType: TextInputType.phone,
      textDirection: TextDirection.ltr,
      decoration: InputDecoration(
        labelText: l.phoneNumber,
        hintText: l.phoneHint,
        prefixText: '+972 ',
        prefixIcon: const Icon(Icons.phone_outlined),
      ),
      onSubmitted: onSubmitted,
    );
  }
}

// ---------------------------------------------------------------------------
// Code Input Field
// ---------------------------------------------------------------------------

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
      textDirection: TextDirection.ltr,
      maxLength: 6,
      textAlign: TextAlign.center,
      style: Theme.of(context).textTheme.headlineMedium?.copyWith(
            letterSpacing: 8,
            fontFamily: TypographyTokens.monoFontFamily,
          ),
      decoration: InputDecoration(
        labelText: AppLocalizations.of(context).verificationCode,
        hintText: '000000',
        counterText: '',
      ),
      onSubmitted: onSubmitted,
    );
  }
}
