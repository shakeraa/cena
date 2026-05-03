// =============================================================================
// Cena Adaptive Learning Platform — Sound Design System (MOB-051)
// =============================================================================
//
// Centralized sound effect and ambient audio management.
//
// Audio files are expected at assets/sounds/<name>.mp3.
// This service manages the logical layer — actual playback requires adding
// an audio plugin (e.g. audioplayers or just_audio) and replacing the
// _playSoundAsset stub with real playback calls.
//
// Required audio assets (create or source these files):
//   assets/sounds/correct_chime.mp3
//   assets/sounds/wrong_gentle.mp3
//   assets/sounds/streak_milestone.mp3
//   assets/sounds/badge_unlock.mp3
//   assets/sounds/level_up.mp3
//   assets/sounds/quest_complete.mp3
//   assets/sounds/hint_reveal.mp3
//   assets/sounds/session_start.mp3
//   assets/sounds/session_complete.mp3
//   assets/sounds/nav_tap.mp3
//   assets/sounds/xp_count.mp3
//   assets/sounds/timer_warning.mp3
// =============================================================================

/// Sound effects available in the app.
enum CenaSound {
  correctChime('assets/sounds/correct_chime.mp3'),
  wrongGentle('assets/sounds/wrong_gentle.mp3'),
  streakMilestone('assets/sounds/streak_milestone.mp3'),
  badgeUnlock('assets/sounds/badge_unlock.mp3'),
  levelUp('assets/sounds/level_up.mp3'),
  questComplete('assets/sounds/quest_complete.mp3'),
  hintReveal('assets/sounds/hint_reveal.mp3'),
  sessionStart('assets/sounds/session_start.mp3'),
  sessionComplete('assets/sounds/session_complete.mp3'),
  navTap('assets/sounds/nav_tap.mp3'),
  xpCount('assets/sounds/xp_count.mp3'),
  timerWarning('assets/sounds/timer_warning.mp3');

  const CenaSound(this.assetPath);

  /// Path to the audio asset file.
  final String assetPath;
}

/// Ambient sound options for study sessions.
enum AmbientSound {
  lofiBeats('assets/sounds/ambient_lofi.mp3'),
  rainSounds('assets/sounds/ambient_rain.mp3'),
  libraryAmbience('assets/sounds/ambient_library.mp3'),
  silence(null);

  const AmbientSound(this.assetPath);
  final String? assetPath;
}

/// User-configurable sound preferences.
class SoundSettings {
  const SoundSettings({
    this.masterEnabled = false,
    this.effectsEnabled = true,
    this.ambientEnabled = false,
    this.hapticsEnabled = true,
  });

  /// Master toggle — when false, all sound is muted.
  final bool masterEnabled;

  /// Whether sound effects (chimes, alerts) are enabled.
  final bool effectsEnabled;

  /// Whether ambient background audio is enabled.
  final bool ambientEnabled;

  /// Whether haptic feedback accompanies sounds.
  final bool hapticsEnabled;

  /// Whether any effects should actually play.
  bool get shouldPlayEffects => masterEnabled && effectsEnabled;

  /// Whether ambient audio should play.
  bool get shouldPlayAmbient => masterEnabled && ambientEnabled;

  SoundSettings copyWith({
    bool? masterEnabled,
    bool? effectsEnabled,
    bool? ambientEnabled,
    bool? hapticsEnabled,
  }) {
    return SoundSettings(
      masterEnabled: masterEnabled ?? this.masterEnabled,
      effectsEnabled: effectsEnabled ?? this.effectsEnabled,
      ambientEnabled: ambientEnabled ?? this.ambientEnabled,
      hapticsEnabled: hapticsEnabled ?? this.hapticsEnabled,
    );
  }
}

/// Centralized sound system for Cena.
///
/// Manages sound effect playback, respects user preferences and silent mode,
/// and enforces a maximum of 3 concurrent sounds to prevent audio clutter.
///
/// **Integration note:** This service provides the complete API surface.
/// To enable actual audio playback, add an audio plugin (e.g. `just_audio`
/// or `audioplayers`) to pubspec.yaml and implement [_playSoundAsset] and
/// [_playAmbientAsset]. The current implementation tracks state correctly
/// but does not produce audible output without a playback backend.
class CenaSoundSystem {
  CenaSoundSystem({
    SoundSettings? settings,
  }) : _settings = settings ?? const SoundSettings();

  SoundSettings _settings;

  /// Current sound settings.
  SoundSettings get settings => _settings;

  /// Update sound settings.
  void updateSettings(SoundSettings settings) {
    _settings = settings;
    if (!settings.shouldPlayAmbient) {
      stopAmbient();
    }
  }

  /// The last sound effect that was played, for testing verification.
  CenaSound? lastPlayedSound;

  /// The currently active ambient sound.
  AmbientSound? _currentAmbient;
  AmbientSound? get currentAmbient => _currentAmbient;

  /// Tracks the number of concurrently playing sound effects.
  int _activeSoundCount = 0;

  /// Maximum number of concurrent sound effects.
  static const int _maxConcurrentSounds = 3;

  /// Set of asset paths that have been pre-loaded.
  final Set<String> _preloadedAssets = {};

  /// Pre-load all sound effect assets for instant playback.
  ///
  /// Call this during app initialization. With a real audio plugin,
  /// this would load audio buffers into memory.
  Future<void> preloadAll() async {
    for (final sound in CenaSound.values) {
      _preloadedAssets.add(sound.assetPath);
    }
    // With a real audio plugin, each asset would be decoded and cached here.
    // Example: await _audioPool.load(sound.assetPath);
  }

  /// Play a sound effect.
  ///
  /// Respects [SoundSettings.shouldPlayEffects] and the concurrent sound
  /// limit. Returns immediately if sounds are disabled or the limit is
  /// reached.
  Future<void> play(CenaSound sound) async {
    if (!_settings.shouldPlayEffects) return;
    if (_activeSoundCount >= _maxConcurrentSounds) return;

    lastPlayedSound = sound;
    _activeSoundCount++;

    try {
      await _playSoundAsset(sound.assetPath);
    } finally {
      _activeSoundCount = (_activeSoundCount - 1).clamp(0, _maxConcurrentSounds);
    }
  }

  /// Start playing ambient background audio.
  ///
  /// Stops any currently playing ambient sound first. [AmbientSound.silence]
  /// stops ambient audio without starting a new track.
  Future<void> startAmbient(AmbientSound ambient) async {
    stopAmbient();

    if (ambient == AmbientSound.silence) {
      _currentAmbient = AmbientSound.silence;
      return;
    }

    if (!_settings.shouldPlayAmbient) return;

    _currentAmbient = ambient;
    if (ambient.assetPath != null) {
      await _playAmbientAsset(ambient.assetPath!);
    }
  }

  /// Stop the currently playing ambient sound.
  void stopAmbient() {
    _currentAmbient = null;
    // With a real audio plugin: _ambientPlayer?.stop();
  }

  /// Whether a specific sound asset has been pre-loaded.
  bool isPreloaded(CenaSound sound) =>
      _preloadedAssets.contains(sound.assetPath);

  /// Dispose of all audio resources.
  void dispose() {
    stopAmbient();
    _preloadedAssets.clear();
    _activeSoundCount = 0;
  }

  // ---------------------------------------------------------------------------
  // Private playback stubs
  // ---------------------------------------------------------------------------

  /// Play a sound effect asset.
  ///
  /// Replace this with real audio playback when an audio plugin is added.
  /// Example with just_audio:
  ///   final player = AudioPlayer();
  ///   await player.setAsset(assetPath);
  ///   await player.play();
  ///   await player.dispose();
  Future<void> _playSoundAsset(String assetPath) async {
    // Simulated playback duration based on sound type.
    // With a real plugin this would play the actual audio file.
    // The method is async to maintain the correct API contract.
  }

  /// Play an ambient audio asset on loop.
  ///
  /// Replace this with real audio playback when an audio plugin is added.
  /// Example with just_audio:
  ///   _ambientPlayer = AudioPlayer();
  ///   await _ambientPlayer.setAsset(assetPath);
  ///   await _ambientPlayer.setLoopMode(LoopMode.one);
  ///   await _ambientPlayer.play();
  Future<void> _playAmbientAsset(String assetPath) async {
    // With a real plugin this would start looping playback.
  }
}
