using System;
using System.Threading;
using System.Runtime.InteropServices;
using RetroNetplayCore.Emulator;
using RetroNetplayCore.Synchronization;

namespace RetroNetplayCore
{
    internal partial class Program
    {
        [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static partial int TimeBeginPeriod(uint uPeriod);

        [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static partial int TimeEndPeriod(uint uPeriod);

        // 1. We keep 'args' as a normal name to prevent scope clashing inside the method
#pragma warning disable IDE0060 // Safely tell the compiler to silence the unused parameter warning
        static void Main(string[] args)
#pragma warning restore IDE0060
        {
            Console.WriteLine("=== Modular 4-Player High Performance Multiplexing Engine ===");

            // 2. Because '_' is not claimed by the parameter, this works perfectly as a local discard!
            _ = TimeBeginPeriod(1);

            try
            {
                // 3. Load configuration safely via decoupled engine config block
                EngineConfig config = EngineConfig.Load("engine_config.json");

                // 4. Initialize Core Subsystems based on configurations
                InputManager input = new(0); // Sample Local controller on hardware slot 0
                NetworkLink network;

                if (config.IsHost)
                {
                    network = new NetworkLink(config.LocalPort);

                    // Register all remote clients defined by your Launcher config setup
                    for (int i = 1; i < 4; i++)
                    {
                        if (config.ActiveSlots[i])
                        {
                            network.RegisterClient(i, config.ClientIPs[i], config.ClientPorts[i]);
                            Console.WriteLine($"👥 Registered Player {i + 1} at Endpoint: {config.ClientIPs[i]}:{config.ClientPorts[i]}");
                        }
                    }
                }
                else
                {
                    network = new NetworkLink(config.LocalPort, config.HostAddress, config.HostPort);
                    Console.WriteLine($"🌐 Running in CLIENT mode. Targeting host: {config.HostAddress}:{config.HostPort}");
                }

                // 5. Inject the explicit synchronization engine style based on runtime role assignment
                INetplaySyncEngine syncEngine = config.IsHost
                    ? new HostSyncEngine(network, config.ActiveSlots)
                    : new ClientSyncEngine(network);

                // 6. Fire up the Libretro environment orchestration components
                EmulatorManager emulator = new();

                Console.WriteLine($"\n⚡ Subsystems Active. Spinning up Emulator Core...");
                emulator.Start(config.CorePath, config.RomPath);

                Console.WriteLine($"\n🎮 Core Booted. Operational loop active. Press Ctrl+C to terminate.\n");

                // Timing metrics
                long ticksPerFrame = TimeSpan.TicksPerSecond / 60;
                long nextFrameTick = DateTime.UtcNow.Ticks;
                uint frameCounter = 0;
                HostFramePacket masterFrame = default;

                // 7. High-Precision Native Frame Processing Loop
                while (true)
                {
                    long currentTicks = DateTime.UtcNow.Ticks;

                    if (currentTicks >= nextFrameTick)
                    {
                        // Capture local physical inputs from hardware
                        ClientInputPacket localInput = input.CaptureFrame(frameCounter);

                        // Run Isolated Subsystem Sync Phase (Fills our masterFrame via pointer reference)
                        syncEngine.ProcessFrameSync(frameCounter, ref localInput, ref masterFrame);

                        // Pass the unified multi-player packet states down to Libretro controller ports
                        emulator.UpdateFrameStates(ref masterFrame);

                        frameCounter++;
                        nextFrameTick += ticksPerFrame;
                    }

                    Thread.Yield();
                }
            }
            catch (Exception fatalError)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n💥 ENGINE FATAL ERROR:");
                Console.WriteLine($"Message: {fatalError.Message}");
                Console.WriteLine($"Trace: {fatalError.StackTrace}");
                Console.ResetColor();

                Console.WriteLine("\nPress any key to close this terminal...");
                Console.ReadKey();
            }
            finally
            {
                _ = TimeEndPeriod(1);
            }
        }
    }
}
