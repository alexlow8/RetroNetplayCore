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

    struct retro_system_info {
        const char* library_name;
        const char* library_version;
        const char* valid_extensions;
        bool need_fullpath;
        bool block_extract;
    };

    __declspec(dllexport) void retro_get_system_info(struct retro_system_info* info) {
        if (info) {
            info->library_name = "Mock Engine Core";
            info->library_version = "v1.0.0";
            info->valid_extensions = "n64|z64";
            info->need_fullpath = true;
            info->block_extract = false;
        }
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
        if (input_poll_cb) {
            input_poll_cb();
        }

        if (input_state_cb) {
            // Sample digital buttons
            bool p1_A = input_state_cb(0, RETRO_DEVICE_JOYPAD, 0, BUTTON_A) != 0;
            bool p1_B = input_state_cb(0, RETRO_DEVICE_JOYPAD, 0, BUTTON_B) != 0;

            // Sample Analogue Sticks (Device Type = 2)
            // Index 0 (Left Stick), Axis 0 (X), Axis 1 (Y)
            int16_t p1_LX = input_state_cb(0, 2, 0, 0);
            int16_t p1_LY = input_state_cb(0, 2, 0, 1);

            // Print telemetry if buttons are pressed OR if sticks move outside a tiny noise threshold
            if (p1_A || p1_B || abs(p1_LX) > 1000 || abs(p1_LY) > 1000) {
                std::cout << "[Mock Core Frame Tick] "
                    << "P1 Buttons: [A:" << (p1_A ? "X" : "_") << " B:" << (p1_B ? "X" : "_") << "] | "
                    << "P1 Left Stick: (" << p1_LX << ", " << p1_LY << ")\n";
            }
        }
    }
}
