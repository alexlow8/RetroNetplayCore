using System;
using System.Threading;
using System.Runtime.InteropServices;
using RetroNetplayCore.Emulator;
using RetroNetplayCore.Synchronization;
using RetroNetplayCore.Configuration;
using RetroNetplayCore.Packets;

namespace RetroNetplayCore
{
    internal partial class Program
    {
        // Windows timer precision functions
        [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static partial int TimeBeginPeriod(uint uPeriod);

        [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static partial int TimeEndPeriod(uint uPeriod);

        static void Main(string[] args)
        {
            Console.WriteLine("=== Modular 4-Player High Performance Multiplexing Engine ===");

            // Boost timer precision to 1ms (default is 15.6ms on many systems)
            // This allows our 60 FPS frame loop to stay accurate
            _ = TimeBeginPeriod(1);

            NetworkThreadManager? networkManager = null;

            try
            {
                // ========================================
                // 1. LOAD CONFIGURATION
                // ========================================
                // The launcher wrote engine_config.json with all peer info and server coordinates.
                // No command-line arguments needed anymore!
                EngineConfig config = EngineConfig.Load("engine_config.json");

                Console.WriteLine($"📋 Configuration loaded:");
                Console.WriteLine($"   Mode: {(config.IsHost ? "HOST" : "GUEST")}");
                Console.WriteLine($"   Core: {config.CorePath}");
                Console.WriteLine($"   ROM: {config.RomPath}");
                Console.WriteLine($"   Server: {config.ServerAddress}:{config.ServerPort}");
                Console.WriteLine($"   Room: {config.RoomCode}");

                // ========================================
                // 2. INITIALIZE SUBSYSTEMS
                // ========================================
                // Set up input handling, emulator, and networking in parallel

                // Input manager reads from local game controller
                InputManager input = new(0);
                Console.WriteLine("🎮 Input Manager: Ready");

                // Network thread manager handles all I/O
                // We start it BEFORE loading the emulator so it's ready when we need it
                networkManager = new NetworkThreadManager(config, 0); // Player 0 is always local
                networkManager.Start();
                Console.WriteLine("🌐 Network Manager: Started");

                // ========================================
                // 3. LOAD EMULATOR CORE
                // ========================================
                // This loads the .dll and prepares for frame loop
                EmulatorManager emulator = new();
                Console.WriteLine($"⚡ Loading emulator core: {config.CorePath}");
                emulator.Start(config.CorePath, config.RomPath);

                Console.WriteLine($"\n🎮 ✅ All systems operational. Starting frame loop...\n");

                // ========================================
                // 4. FRAME LOOP (THE HEART OF NETPLAY)
                // ========================================
                // This loop runs at precisely 60 FPS
                // It MUST remain allocation-free to avoid GC pauses
                // All network I/O happens on the background thread

                long ticksPerFrame = TimeSpan.TicksPerSecond / 60; // 166,666 ticks for 60 FPS
                long nextFrameTick = DateTime.UtcNow.Ticks;
                uint frameCounter = 0;

                while (true)
                {
                    long currentTicks = DateTime.UtcNow.Ticks;

                    // Wait for next frame time (60 FPS means ~16.67ms per frame)
                    if (currentTicks >= nextFrameTick)
                    {
                        // --- STEP 1: Capture local controller input ---
                        ClientInputPacket localInput = input.CaptureFrame(frameCounter);

                        // --- STEP 2: Get synchronized frame state from all peers ---
                        // This is where network inputs are merged
                        // NetworkThreadManager is constantly filling this buffer in background
                        HostFramePacket masterFrame = networkManager.GetFrameInputs(frameCounter);

                        // --- STEP 3: Publish our inputs so peers can receive them ---
                        // This just queues the send on the background thread (non-blocking)
                        networkManager.PublishLocalInput(frameCounter, ref localInput);

                        // --- STEP 4: Run one frame of emulation with merged inputs ---
                        emulator.UpdateFrameStates(ref masterFrame);

                        frameCounter++;
                        nextFrameTick += ticksPerFrame;
                    }
                    else
                    {
                        // Not time for next frame yet, yield CPU to other threads
                        Thread.Yield();
                    }
                }
            }
            catch (Exception fatalError)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n💥 ENGINE FATAL ERROR:");
                Console.WriteLine($"Message: {fatalError.Message}");
                Console.WriteLine($"Stack Trace: {fatalError.StackTrace}");
                Console.ResetColor();

                Console.WriteLine("\nPress any key to close this terminal...");
                Console.ReadKey();
            }
            finally
            {
                // Cleanup
                networkManager?.Dispose();
                _ = TimeEndPeriod(1);
            }
        }
    }
}

