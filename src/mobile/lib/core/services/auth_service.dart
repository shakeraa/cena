// =============================================================================
// Cena Adaptive Learning Platform — Auth Service
// Firebase Authentication with Google, Apple, and Phone providers.
// Matches the admin dashboard's auth pattern with custom claims extraction.
// =============================================================================

import 'dart:io' show Platform;

import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_sign_in/google_sign_in.dart';
import 'package:logger/logger.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

final _logger = Logger(
  printer: PrettyPrinter(methodCount: 0, printTime: true),
);

// ---------------------------------------------------------------------------
// Custom Claims Model — mirrors admin's CenaUser claims
// ---------------------------------------------------------------------------

/// User role matching backend CenaClaimsTransformer.
enum CenaRole {
  student,
  moderator,
  admin,
  superAdmin;

  static CenaRole fromString(String? value) {
    switch (value?.toUpperCase()) {
      case 'MODERATOR':
        return CenaRole.moderator;
      case 'ADMIN':
        return CenaRole.admin;
      case 'SUPER_ADMIN':
        return CenaRole.superAdmin;
      default:
        return CenaRole.student;
    }
  }
}

/// Authenticated user with Firebase custom claims.
/// Maps to the backend's JWT claims: role, school_id, locale, plan.
class CenaAuthUser {
  const CenaAuthUser({
    required this.uid,
    required this.role,
    this.email,
    this.displayName,
    this.photoURL,
    this.phoneNumber,
    this.schoolId,
    this.locale = 'he',
    this.plan = 'free',
  });

  final String uid;
  final String? email;
  final String? displayName;
  final String? photoURL;
  final String? phoneNumber;
  final CenaRole role;
  final String? schoolId;
  final String locale;
  final String plan;

  /// Extract CenaAuthUser from a Firebase User + fresh ID token claims.
  static Future<CenaAuthUser> fromFirebaseUser(User firebaseUser) async {
    final tokenResult = await firebaseUser.getIdTokenResult(true);
    final claims = tokenResult.claims ?? {};

    return CenaAuthUser(
      uid: firebaseUser.uid,
      email: firebaseUser.email,
      displayName: firebaseUser.displayName,
      photoURL: firebaseUser.photoURL,
      phoneNumber: firebaseUser.phoneNumber,
      role: CenaRole.fromString(claims['role'] as String?),
      schoolId: claims['school_id'] as String?,
      locale: (claims['locale'] as String?) ?? 'he',
      plan: (claims['plan'] as String?) ?? 'free',
    );
  }

  /// Get the fresh Firebase ID token for API calls.
  Future<String?> getIdToken() async {
    return FirebaseAuth.instance.currentUser?.getIdToken();
  }
}

// ---------------------------------------------------------------------------
// Auth State
// ---------------------------------------------------------------------------

/// Authentication state machine.
sealed class AuthState {
  const AuthState();
}

class AuthInitial extends AuthState {
  const AuthInitial();
}

class AuthLoading extends AuthState {
  const AuthLoading();
}

class AuthAuthenticated extends AuthState {
  const AuthAuthenticated(this.user);
  final CenaAuthUser user;
}

class AuthError extends AuthState {
  const AuthError(this.message);
  final String message;
}

// ---------------------------------------------------------------------------
// Auth Service
// ---------------------------------------------------------------------------

/// Firebase Authentication service supporting Google, Apple, and Phone sign-in.
///
/// Best practices for mobile:
/// - Google Sign-In: lowest friction, works on both platforms
/// - Apple Sign-In: required by App Store when offering social login
/// - Phone Auth: kept for Israeli market (SMS with +972)
///
/// After authentication, extracts custom claims (role, school_id, locale, plan)
/// matching the backend's CenaClaimsTransformer pattern.
class AuthService {
  AuthService({FirebaseAuth? auth, GoogleSignIn? googleSignIn})
      : _auth = auth ?? FirebaseAuth.instance,
        _googleSignIn = googleSignIn ?? GoogleSignIn();

  final FirebaseAuth _auth;
  final GoogleSignIn _googleSignIn;

  /// Stream of auth state changes. Emits null on sign-out.
  Stream<User?> get authStateChanges => _auth.authStateChanges();

  /// Current Firebase user, if any.
  User? get currentUser => _auth.currentUser;

  /// Whether the user is currently signed in.
  bool get isSignedIn => _auth.currentUser != null;

  // ---- Google Sign-In ----

  /// Sign in with Google. Works on both iOS and Android.
  Future<CenaAuthUser> signInWithGoogle() async {
    final googleUser = await _googleSignIn.signIn();
    if (googleUser == null) {
      throw FirebaseAuthException(
        code: 'sign-in-cancelled',
        message: 'Google sign-in was cancelled',
      );
    }

    final googleAuth = await googleUser.authentication;
    final credential = GoogleAuthProvider.credential(
      accessToken: googleAuth.accessToken,
      idToken: googleAuth.idToken,
    );

    final userCredential = await _auth.signInWithCredential(credential);
    final user = userCredential.user;
    if (user == null) {
      throw FirebaseAuthException(
        code: 'null-user',
        message: 'Firebase returned null user after Google sign-in',
      );
    }

    _logger.i('Google sign-in successful: ${user.email}');
    return CenaAuthUser.fromFirebaseUser(user);
  }

  // ---- Apple Sign-In ----

  /// Sign in with Apple. Required by App Store guidelines.
  /// On Android, falls back to OAuth provider flow.
  Future<CenaAuthUser> signInWithApple() async {
    final UserCredential userCredential;

    if (Platform.isIOS) {
      // Native Apple Sign-In on iOS
      final appleCredential = await SignInWithApple.getAppleIDCredential(
        scopes: [
          AppleIDAuthorizationScopes.email,
          AppleIDAuthorizationScopes.fullName,
        ],
      );

      final oauthCredential = OAuthProvider('apple.com').credential(
        idToken: appleCredential.identityToken,
        accessToken: appleCredential.authorizationCode,
      );

      userCredential = await _auth.signInWithCredential(oauthCredential);

      // Apple only sends name on first sign-in — persist it.
      final displayName = [
        appleCredential.givenName,
        appleCredential.familyName,
      ].whereType<String>().where((n) => n.isNotEmpty).join(' ');

      if (displayName.isNotEmpty &&
          (userCredential.user?.displayName == null ||
              userCredential.user!.displayName!.isEmpty)) {
        await userCredential.user?.updateDisplayName(displayName);
      }
    } else {
      // Android — use Firebase OAuthProvider flow
      final provider = OAuthProvider('apple.com')
        ..addScope('email')
        ..addScope('name');
      userCredential = await _auth.signInWithProvider(provider);
    }

    final user = userCredential.user;
    if (user == null) {
      throw FirebaseAuthException(
        code: 'null-user',
        message: 'Firebase returned null user after Apple sign-in',
      );
    }

    _logger.i('Apple sign-in successful: ${user.email}');
    return CenaAuthUser.fromFirebaseUser(user);
  }

  // ---- Phone Auth ----

  /// Start phone authentication by sending SMS code.
  /// Returns the verification ID needed for [verifyPhoneCode].
  Future<String> sendPhoneVerificationCode({
    required String phoneNumber,
    required Duration timeout,
  }) async {
    String? verificationId;
    String? errorMessage;

    await _auth.verifyPhoneNumber(
      phoneNumber: phoneNumber,
      timeout: timeout,
      verificationCompleted: (PhoneAuthCredential credential) async {
        // Auto-retrieval on Android — sign in immediately
        await _auth.signInWithCredential(credential);
      },
      verificationFailed: (FirebaseAuthException e) {
        errorMessage = e.message ?? 'Phone verification failed';
        _logger.e('Phone verification failed', error: e);
      },
      codeSent: (String vId, int? resendToken) {
        verificationId = vId;
      },
      codeAutoRetrievalTimeout: (String vId) {
        verificationId ??= vId;
      },
    );

    if (errorMessage != null) {
      throw FirebaseAuthException(
        code: 'verification-failed',
        message: errorMessage!,
      );
    }

    // Allow time for the callback to fire
    await Future.delayed(const Duration(milliseconds: 500));

    if (verificationId == null) {
      throw FirebaseAuthException(
        code: 'no-verification-id',
        message: 'Failed to obtain verification ID',
      );
    }

    return verificationId!;
  }

  /// Verify the SMS code and complete phone sign-in.
  Future<CenaAuthUser> verifyPhoneCode({
    required String verificationId,
    required String smsCode,
  }) async {
    final credential = PhoneAuthProvider.credential(
      verificationId: verificationId,
      smsCode: smsCode,
    );

    final userCredential = await _auth.signInWithCredential(credential);
    final user = userCredential.user;
    if (user == null) {
      throw FirebaseAuthException(
        code: 'null-user',
        message: 'Firebase returned null user after phone sign-in',
      );
    }

    _logger.i('Phone sign-in successful: ${user.phoneNumber}');
    return CenaAuthUser.fromFirebaseUser(user);
  }

  // ---- Sign Out ----

  /// Sign out from all providers.
  Future<void> signOut() async {
    await Future.wait([
      _auth.signOut(),
      _googleSignIn.signOut(),
    ]);
    _logger.i('User signed out');
  }

  // ---- Token Management ----

  /// Get a fresh ID token for API calls. Force-refreshes if [forceRefresh].
  Future<String?> getIdToken({bool forceRefresh = false}) async {
    return _auth.currentUser?.getIdToken(forceRefresh);
  }

  /// Map Firebase error codes to user-friendly messages.
  static String mapAuthError(String code) {
    switch (code) {
      case 'sign-in-cancelled':
        return 'Sign-in was cancelled';
      case 'account-exists-with-different-credential':
        return 'An account already exists with a different sign-in method';
      case 'invalid-credential':
        return 'Invalid credentials. Please try again.';
      case 'user-disabled':
        return 'This account has been disabled';
      case 'too-many-requests':
        return 'Too many attempts. Please try again later.';
      case 'network-request-failed':
        return 'Network error. Please check your connection.';
      case 'invalid-verification-code':
        return 'Invalid verification code. Please try again.';
      case 'session-expired':
        return 'Verification session expired. Please request a new code.';
      default:
        return 'Authentication failed. Please try again.';
    }
  }
}

// ---------------------------------------------------------------------------
// Riverpod Providers
// ---------------------------------------------------------------------------

/// Singleton auth service provider.
final authServiceProvider = Provider<AuthService>((ref) {
  return AuthService();
});

/// Stream of Firebase auth state changes.
final authStateProvider = StreamProvider<User?>((ref) {
  return ref.watch(authServiceProvider).authStateChanges;
});

/// Auth state notifier for the UI. Tracks loading/error states.
final authNotifierProvider =
    StateNotifierProvider<AuthNotifier, AuthState>((ref) {
  return AuthNotifier(ref.watch(authServiceProvider));
});

/// StateNotifier managing auth state transitions.
class AuthNotifier extends StateNotifier<AuthState> {
  AuthNotifier(this._authService) : super(const AuthInitial()) {
    // Check if already signed in
    if (_authService.isSignedIn) {
      _restoreSession();
    }
  }

  final AuthService _authService;

  Future<void> _restoreSession() async {
    state = const AuthLoading();
    try {
      final user = _authService.currentUser;
      if (user != null) {
        final cenaUser = await CenaAuthUser.fromFirebaseUser(user);
        state = AuthAuthenticated(cenaUser);
      } else {
        state = const AuthInitial();
      }
    } catch (e) {
      _logger.w('Session restore failed', error: e);
      state = const AuthInitial();
    }
  }

  Future<void> signInWithGoogle() async {
    state = const AuthLoading();
    try {
      final user = await _authService.signInWithGoogle();
      state = AuthAuthenticated(user);
    } on FirebaseAuthException catch (e) {
      state = AuthError(AuthService.mapAuthError(e.code));
    } catch (e) {
      state = AuthError('Google sign-in failed: ${e.toString()}');
    }
  }

  Future<void> signInWithApple() async {
    state = const AuthLoading();
    try {
      final user = await _authService.signInWithApple();
      state = AuthAuthenticated(user);
    } on FirebaseAuthException catch (e) {
      state = AuthError(AuthService.mapAuthError(e.code));
    } catch (e) {
      state = AuthError('Apple sign-in failed: ${e.toString()}');
    }
  }

  Future<void> sendPhoneCode(String phoneNumber) async {
    state = const AuthLoading();
    try {
      await _authService.sendPhoneVerificationCode(
        phoneNumber: phoneNumber,
        timeout: const Duration(seconds: 60),
      );
      // Stay in loading state — UI transitions to code entry
      state = const AuthInitial();
    } on FirebaseAuthException catch (e) {
      state = AuthError(AuthService.mapAuthError(e.code));
    } catch (e) {
      state = AuthError('Failed to send verification code');
    }
  }

  Future<void> verifyPhoneCode({
    required String verificationId,
    required String smsCode,
  }) async {
    state = const AuthLoading();
    try {
      final user = await _authService.verifyPhoneCode(
        verificationId: verificationId,
        smsCode: smsCode,
      );
      state = AuthAuthenticated(user);
    } on FirebaseAuthException catch (e) {
      state = AuthError(AuthService.mapAuthError(e.code));
    } catch (e) {
      state = AuthError('Verification failed');
    }
  }

  Future<void> signOut() async {
    try {
      await _authService.signOut();
    } finally {
      state = const AuthInitial();
    }
  }

  void clearError() {
    if (state is AuthError) {
      state = const AuthInitial();
    }
  }
}
