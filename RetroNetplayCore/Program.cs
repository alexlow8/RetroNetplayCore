using System;
using System.Runtime.InteropServices;
using System.Threading;
using RetroNetplayCore.Emulator; // Bring in our newly decoupled emulator namespace

namespace RetroNetplayCore
{
    internal class Program
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uPeriod);

        static void Main(string[] args)
        {
            TimeBeginPeriod(1);
            Console.WriteLine("=== Modular 16-Byte High Performance Engine ===");

            try
            {
                // 1. Gather configuration details
                Console.Write("Enter your LOCAL port (e.g., 8000): ");
                int localPort = int.Parse(Console.ReadLine() ?? "8000");

                Console.Write("Enter your FRIEND'S port (e.g., 8001): ");
                int targetPort = int.Parse(Console.ReadLine() ?? "8001");

                // --- NEW EMULATOR CONFIGURATION ---
                Console.Write("Enter absolute path to Libretro Core DLL: ");
                string corePath = Console.ReadLine() ?? "mupen64plus_next_libretro.dll";

                Console.Write("Enter absolute path to Game ROM: ");
                string romPath = Console.ReadLine() ?? "";

                // 2. Initialize our clean decoupled components
                InputManager input = new InputManager(0);
                NetworkLink network = new NetworkLink(localPort, targetPort);

                // Initialize the orchestration layer
                EmulatorManager emulator = new EmulatorManager();

                Console.WriteLine($"\n⚡ Subsystems Active. Spinning up Emulator Core...");

                // Boot up the native dynamic library loader and pin the ROM path
                emulator.Start(corePath, romPath);

                Console.WriteLine($"\n🎮 Core Booted. Loopback streaming initialized...");
                Console.WriteLine("Press Ctrl+C to terminate.\n");

                long ticksPerFrame = TimeSpan.TicksPerSecond / 60;
                long nextFrameTick = DateTime.UtcNow.Ticks;
                uint localFrameCounter = 0;

                UniversalInputPacket remotePacket = default;

                while (true)
                {
                    long currentTicks = DateTime.UtcNow.Ticks;

                    if (currentTicks >= nextFrameTick)
                    {
                        // A. Capture local physical inputs from hardware via Ordinal #100
                        UniversalInputPacket localPacket = input.CaptureFrame(localFrameCounter);

                        // B. Blast them to our friend instantly over the raw UDP socket
                        network.SendPacket(ref localPacket);

                        // C. Check if our friend sent their network input packet
                        // If no new packet arrived, it falls back to the previous frame's cached data
                        network.ReceivePacket(ref remotePacket);

                        // D. Feed both input tracks into the emulator and run the unmanaged 60Hz tick
                        emulator.UpdateFrameStates(localPacket, remotePacket);

                        // Optional: Print telemetry for verification
                        if (remotePacket.Buttons != 0 || localPacket.Buttons != 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"[Frame {localFrameCounter}] Core Ticked. Local Buttons: {(XInputButtonFlags)localPacket.Buttons} | Remote Buttons: {(XInputButtonFlags)remotePacket.Buttons}");
                            Console.ResetColor();
                        }

                        localFrameCounter++;
                        nextFrameTick += ticksPerFrame;
                    }

                    Thread.Yield();
                }
            }
            catch (Exception fatalError)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n💥 ENGINE FATAL ERROR ACCRUED!");
                Console.WriteLine($"Message: {fatalError.Message}");
                Console.WriteLine($"Trace: {fatalError.StackTrace}");
                Console.ResetColor();

                Console.WriteLine("\nPress any key to close this terminal...");
                Console.ReadKey();
            }
            finally
            {
                TimeEndPeriod(1);
            }
        }
    }
}
