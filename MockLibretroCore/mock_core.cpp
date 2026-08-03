#include <iostream>
#include <cmath>
#include <string>

// Standard Libretro function pointer definitions
typedef void (*retro_video_refresh_t)(const void* data, unsigned width, unsigned height, size_t pitch);
typedef void (*retro_input_poll_t)(void);
typedef int16_t(*retro_input_state_t)(unsigned port, unsigned device, unsigned index, unsigned id);

static retro_video_refresh_t video_cb = nullptr;
static retro_input_poll_t input_poll_cb = nullptr;
static retro_input_state_t input_state_cb = nullptr;

const unsigned RETRO_DEVICE_JOYPAD = 1;
const unsigned RETRO_DEVICE_ANALOG = 2;

// Libretro Button IDs for Mapping Matrix
const char* BUTTON_NAMES[] = { "B", "Y", "Select", "Start", "Up", "Down", "Left", "Right", "A", "X", "L", "R", "L2", "R2", "L3", "R3" };

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
        if (input_poll_cb) input_poll_cb();
        if (!input_state_cb) return;

        static uint32_t total_frames = 0;
        total_frames++;

        // Process both player slots explicitly
        for (unsigned port = 0; port < 2; port++) {
            std::string pressed_buttons = "";
            bool active = false;

            // 1. Gather all 16 digital buttons
            for (unsigned i = 0; i < 16; i++) {
                if (input_state_cb(port, RETRO_DEVICE_JOYPAD, 0, i) != 0) {
                    // If it's a trigger slot, skip appending to digital string text since we print raw values below
                    if (i == 12 || i == 13) continue;
                    pressed_buttons += "[" + std::string(BUTTON_NAMES[i]) + "] ";
                    active = true;
                }
            }

            // 2. Extract Analogue Sticks
            int16_t lx = input_state_cb(port, RETRO_DEVICE_ANALOG, 0, 0);
            int16_t ly = input_state_cb(port, RETRO_DEVICE_ANALOG, 0, 1);
            int16_t rx = input_state_cb(port, RETRO_DEVICE_ANALOG, 1, 0);
            int16_t ry = input_state_cb(port, RETRO_DEVICE_ANALOG, 1, 1);

            // 3. Extract Raw Analogue Trigger Depths
            int16_t l_trig = input_state_cb(port, RETRO_DEVICE_JOYPAD, 0, 12); // L2 Depth
            int16_t r_trig = input_state_cb(port, RETRO_DEVICE_JOYPAD, 0, 13); // R2 Depth

            // Threshold check for active state output
            bool stick_moved = (abs(lx) > 1000 || abs(ly) > 1000 || abs(rx) > 1000 || abs(ry) > 1000);
            bool triggers_pulled = (l_trig > 5 || r_trig > 5); // Exclude minor trace sensor noise

            if (stick_moved || triggers_pulled) active = true;

            // 4. Output lines matching your custom color profiles
            if (active) {
                // Player 1 line -> Cyan
                if (port == 0) {
                    std::cout << "\033[96m[P1 LOCAL]  Frame: " << total_frames
                        << " | Buttons: " << (pressed_buttons.empty() ? "None " : pressed_buttons)
                        << "| LS: (" << lx << "," << ly << ") "
                        << "| RS: (" << rx << "," << ry << ") "
                        << "| Triggers: L: " << l_trig << " / R: " << r_trig << "\033[0m\n";
                }
                // Player 2 line -> Gold/Yellow
                else if (port == 1) {
                    std::cout << "\033[93m[P2 REMOTE] Frame: " << total_frames
                        << " | Buttons: " << (pressed_buttons.empty() ? "None " : pressed_buttons)
                        << "| LS: (" << lx << "," << ly << ") "
                        << "| RS: (" << rx << "," << ry << ") "
                        << "| Triggers: L: " << l_trig << " / R: " << r_trig << "\033[0m\n";
                }
            }
        }
    }
}