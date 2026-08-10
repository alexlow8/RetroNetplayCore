using System;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RetroNetplayCore.Emulator;
using RetroNetplayCore.Synchronization;
using RetroNetplayCore.Configuration;
using RetroNetplayCore.Packets;
using System.Net.Sockets;

namespace RetroNetplayCore
{
    internal partial class Program
    {
        [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static partial int TimeBeginPeriod(uint uPeriod);

        [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static partial int TimeEndPeriod(uint uPeriod);

        static void Main(string[] args)
        {
            Console.WriteLine("=== Modular 4-Player High Performance Multiplexing Engine ===");
            _ = TimeBeginPeriod(1);

            try
            {
                long inheritedSocketValue = -1;

                // 1. Scan command-line parameters to locate the inherited handle token
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--inherited-socket" && i + 1 < args.Length)
                    {
                        if (!long.TryParse(args[i + 1], out inheritedSocketValue))
                        {
                            inheritedSocketValue = -1; // Force fallback value on bad parse data
                        }
                        break;
                    }
                }

                if (inheritedSocketValue == -1)
                {
                    throw new InvalidOperationException("💥 Boot Error: No inherited socket handle was provided by the launcher setup instance.");
                }

                // 2. Wrap the raw handle in a secure operating system handle wrapper
                IntPtr socketHandlePtr = new(inheritedSocketValue);
                // setting 'ownsHandle: true' means when this Core app terminates, the OS automatically drops the port.
                SafeSocketHandle safeSocketWrapper = new(socketHandlePtr, ownsHandle: true);

                // 3. Load configurations from the disk file engine mapping config
                EngineConfig config = EngineConfig.Load("engine_config.json");

                // 4. instance initialization for the hardware input subsystem
                InputManager input = new(0); // Hardware slot 0 local controller binding

                // 4. Initialize Network Link using our handle injection constructor
                NetworkLink network = new(safeSocketWrapper, config.IsHost, config.HostAddress, config.HostPort);

                if (config.IsHost)
                {
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
                    Console.WriteLine($"🌐 Running in CLIENT mode. Targeting host: {config.HostAddress}:{config.HostPort}");
                }

                // 5. Setup Synchronization Strategies
                INetplaySyncEngine syncEngine = config.IsHost
                    ? new HostSyncEngine(network, config.ActiveSlots)
                    : new ClientSyncEngine(network);

                // 6. Launch Emulator Ecosystem Context
                EmulatorManager emulator = new();
                Console.WriteLine($"\n⚡ Subsystems Active. Spinning up Emulator Core...");
                emulator.Start(config.CorePath, config.RomPath);

                Console.WriteLine($"\n🎮 Core Booted. Operational loop active. Press Ctrl+C to terminate.\n");

                long ticksPerFrame = TimeSpan.TicksPerSecond / 60;
                long nextFrameTick = DateTime.UtcNow.Ticks;
                uint frameCounter = 0;
                HostFramePacket masterFrame = default;

                // 7. High-Precision Execution Frame Loop
                while (true)
                {
                    long currentTicks = DateTime.UtcNow.Ticks;

                    if (currentTicks >= nextFrameTick)
                    {
                        ClientInputPacket localInput = input.CaptureFrame(frameCounter);

                        syncEngine.ProcessFrameSync(frameCounter, ref localInput, ref masterFrame);
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
