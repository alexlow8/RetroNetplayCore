using RetroNetplayCore.Packets;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RetroNetplayCore
{
    public unsafe partial class InputManager
    {
        // Direct kernel link bypassing managed wrappers
        [LibraryImport("xinput1_4.dll", EntryPoint = "#100")]
        private static partial int XInputGetStateEx(int userIndex, ref XINPUT_STATE_EX pState);

        // Native layout matching Windows XInput memory exactly
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE_EX
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        private const ushort XINPUT_GAMEPAD_GUIDE = 0x0400;
        private const int AnalogDeadzone = 7849;
        private const byte TriggerDeadzone = 30;

        private readonly int _playerIndex;
        private bool _wasGuidePressedLastFrame;
        private bool _isConnected;

        public bool IsConnected => _isConnected;

        public InputManager(int playerIndex = 0)
        {
            _playerIndex = playerIndex;
            _isConnected = VerifyConnection();
        }

        private bool VerifyConnection()
        {
            XINPUT_STATE_EX stateEx = default;
            // If the device returns 0 (ERROR_SUCCESS), it is actively connected
            return XInputGetStateEx(_playerIndex, ref stateEx) == 0;
        }

        /// <summary>
        /// Captures local physical hardware states directly over the stack as a lean ClientInputPacket.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ClientInputPacket CaptureFrame(uint frameId)
        {
            ClientInputPacket packet = new() { FrameId = frameId };

            XINPUT_STATE_EX stateEx = default;
            int result = XInputGetStateEx(_playerIndex, ref stateEx);

            if (result != 0)
            {
                _isConnected = false;
                return packet;
            }

            _isConnected = true;
            XINPUT_GAMEPAD gamepad = stateEx.Gamepad;
            ushort rawButtons = gamepad.wButtons;

            // 1. Process Mode/Guide Button state via Ordinal #100
            bool isGuidePressed = (rawButtons & XINPUT_GAMEPAD_GUIDE) != 0;
            if (isGuidePressed && !_wasGuidePressedLastFrame)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n🔄 [Mode Click] System Mode Shift Triggered on Frame {frameId}!");
                Console.ResetColor();
            }
            _wasGuidePressedLastFrame = isGuidePressed;

            // 2. Build the packet structure with absolute zero allocations
            packet.Buttons = rawButtons;

            // Raw Deadzone Bitwise Branching (Inline optimized)
            packet.LeftStickX = Math.Abs((int)gamepad.sThumbLX) < AnalogDeadzone ? (short)0 : gamepad.sThumbLX;
            packet.LeftStickY = Math.Abs((int)gamepad.sThumbLY) < AnalogDeadzone ? (short)0 : gamepad.sThumbLY;
            packet.RightStickX = Math.Abs((int)gamepad.sThumbRX) < AnalogDeadzone ? (short)0 : gamepad.sThumbRX;
            packet.RightStickY = Math.Abs((int)gamepad.sThumbRY) < AnalogDeadzone ? (short)0 : gamepad.sThumbRY;

            packet.LeftTrigger = gamepad.bLeftTrigger < TriggerDeadzone ? (byte)0 : gamepad.bLeftTrigger;
            packet.RightTrigger = gamepad.bRightTrigger < TriggerDeadzone ? (byte)0 : gamepad.bRightTrigger;

            return packet;
        }

        /// <summary>
        /// Helper utility: Extracts inputs for any player from a unified network struct packet.
        /// Extracted structure can be handed off directly to your Libretro environment loop.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PlayerInputState ExtractPlayerState(ref HostFramePacket masterPacket, int playerSlot)
        {
            return playerSlot switch
            {
                0 => masterPacket.Player0,
                1 => masterPacket.Player1,
                2 => masterPacket.Player2,
                3 => masterPacket.Player3,
                _ => default,
            };
        }
    }
}
