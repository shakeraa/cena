// =============================================================================
// Cena Platform -- Student Emulator Service
// Simulates student interactions with the actor system via NATS.
//
// Usage: dotnet run -- [--students=1000] [--speed=10] [--duration=60]
//                      [--seed=42] [--nats=nats://localhost:4222]
// =============================================================================

using Cena.Emulator;

await EmulatorEngine.RunAsync(args);
