using RetroNetplayCore.Emulator; // Bring in our newly decoupled emulator namespace
using System.Runtime.InteropServices;
using System.Text.Json;

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
                // --- REPLACE YOUR CHOSEN CONFIGURATION BLOCK INSIDE PROGRAM.CS WITH THIS ---
                int localPort = 8000;
                int targetPort = 8001;
                string corePath = @"Emulators\MockLibretroCore.dll";
                string romPath = "dummy.z64";

                string configPath = "engine_config.json";
                if (File.Exists(configPath))
                {
                    try
                    {
                        string json = File.ReadAllText(configPath);
                        // Simple unmanaged-safe light parsing loop or quick JSON parse
                        using (JsonDocument doc = JsonDocument.Parse(json))
                        {
                            JsonElement root = doc.RootElement;
                            localPort = root.GetProperty("LocalPort").GetInt32();
                            targetPort = root.GetProperty("FriendPort").GetInt32();
                            corePath = root.GetProperty("CorePath").GetString() ?? corePath;
                            romPath = root.GetProperty("RomPath").GetString() ?? romPath;
                        }
                        Console.WriteLine("📥 Settings successfully loaded from engine_config.json");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Config parse error: {ex.Message}. Reverting to baseline simulation parameters.");
                    }
                }
                else
                {
                    Console.WriteLine("💡 No engine_config.json detected. Executing with default pipeline configurations.");
                }

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
