#include <iostream>

// Standard Libretro function pointer definitions matching our C# delegate contracts
typedef void (*retro_video_refresh_t)(const void* data, unsigned width, unsigned height, size_t pitch);
typedef void (*retro_input_poll_t)(void);
typedef int16_t(*retro_input_state_t)(unsigned port, unsigned device, unsigned index, unsigned id);

// Static callback references assigned by C#
static retro_video_refresh_t video_cb = nullptr;
static retro_input_poll_t input_poll_cb = nullptr;
static retro_input_state_t input_state_cb = nullptr;

// Constant matches LibretroConstants.RETRO_DEVICE_JOYPAD
const unsigned RETRO_DEVICE_JOYPAD = 1;

// Libretro Button IDs
const unsigned BUTTON_A = 8;
const unsigned BUTTON_B = 0;
const unsigned BUTTON_START = 3;

extern "C" {

    __declspec(dllexport) void retro_set_video_refresh(retro_video_refresh_t cb) {
        video_cb = cb;
    }

    __declspec(dllexport) void retro_set_input_poll(retro_input_poll_t cb) {
        input_poll_cb = cb;
    }

    __declspec(dllexport) void retro_set_input_state(retro_input_state_t cb) {
        input_state_cb = cb;
    }

    __declspec(dllexport) void retro_init(void) {
        std::cout << "[Mock Core] Native C++ Core Successfully Initialized!\n";
    }

    __declspec(dllexport) void retro_deinit(void) {
        std::cout << "[Mock Core] Native C++ Core Safely Deinitialized.\n";
    }

    // Matches our updated unmanaged pointer-based RetroGameInfo struct
    struct retro_game_info {
        const char* path;
        const void* data;
        size_t size;
        const char* meta;
    };

    __declspec(dllexport) bool retro_load_game(const struct retro_game_info* game) {
        if (game && game->path) {
            std::cout << "[Mock Core] Game loaded successfully via path: " << game->path << "\n";
            return true;
        }
        std::cout << "[Mock Core] Failed to load game path!\n";
        return false;
    }

    __declspec(dllexport) void retro_run(void) {
        // 1. Tell C# backend to update its physical input mappings
        if (input_poll_cb) {
            input_poll_cb();
        }

        // 2. Sample inputs for Player 1 (Port 0) and Player 2 (Port 1) using the C# callback
        if (input_state_cb) {
            bool p1_A = input_state_cb(0, RETRO_DEVICE_JOYPAD, 0, BUTTON_A) != 0;
            bool p1_B = input_state_cb(0, RETRO_DEVICE_JOYPAD, 0, BUTTON_B) != 0;
            bool p1_Start = input_state_cb(0, RETRO_DEVICE_JOYPAD, 0, BUTTON_START) != 0;

            bool p2_A = input_state_cb(1, RETRO_DEVICE_JOYPAD, 0, BUTTON_A) != 0;
            bool p2_B = input_state_cb(1, RETRO_DEVICE_JOYPAD, 0, BUTTON_B) != 0;

            // 3. Print out the telemetry to prove the Memory Pass works perfectly
            if (p1_A || p1_B || p1_Start || p2_A || p2_B) {
                std::cout << "[Mock Core Frame Tick] "
                    << "P1: [A:" << (p1_A ? "X" : "_") << " B:" << (p1_B ? "X" : "_") << " START:" << (p1_Start ? "X" : "_") << "] | "
                    << "P2: [A:" << (p2_A ? "X" : "_") << " B:" << (p2_B ? "X" : "_") << "]\n";
            }
        }
    }
}
