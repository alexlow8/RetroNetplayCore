#include <iostream>
#include <cmath>
#include <string>
#include <vector>

// Standard Libretro function pointer definitions
typedef bool (*retro_environment_t)(unsigned cmd, void* data);
typedef void (*retro_video_refresh_t)(const void* data, unsigned width, unsigned height, size_t pitch);
typedef void (*retro_audio_sample_t)(int16_t left, int16_t right);
typedef size_t(*retro_audio_sample_batch_t)(const int16_t* data, size_t frames);
typedef void (*retro_input_poll_t)(void);
typedef int16_t(*retro_input_state_t)(unsigned port, unsigned device, unsigned index, unsigned id);

static retro_environment_t environ_cb = nullptr;
static retro_video_refresh_t video_cb = nullptr;
static retro_audio_sample_t audio_cb = nullptr;
static retro_audio_sample_batch_t audio_batch_cb = nullptr;
static retro_input_poll_t input_poll_cb = nullptr;
static retro_input_state_t input_state_cb = nullptr;

const unsigned RETRO_DEVICE_JOYPAD = 1;
const unsigned RETRO_DEVICE_ANALOG = 2;

// Libretro Button IDs for Mapping Matrix
const char* BUTTON_NAMES[] = { "B", "Y", "Select", "Start", "Up", "Down", "Left", "Right", "A", "X", "L", "R", "L2", "R2", "L3", "R3" };

extern "C" {

    __declspec(dllexport) void retro_set_environment(retro_environment_t cb) {
        environ_cb = cb;
    }

    __declspec(dllexport) void retro_set_video_refresh(retro_video_refresh_t cb) {
        video_cb = cb;
    }

    __declspec(dllexport) void retro_set_audio_sample(retro_audio_sample_t cb) {
        audio_cb = cb;
    }

    __declspec(dllexport) void retro_set_audio_sample_batch(retro_audio_sample_batch_t cb) {
        audio_batch_cb = cb;
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

        static uint32_t total_frames = 0;
        total_frames++;

        // --- 1. AUDIO CALLBACK SIMULATION ---
        // Simulates 1 frame of stereo audio (735 samples at 44.1kHz / 60FPS)
        // Emits a gentle 440Hz sine wave tone so you can verify your network buffer transit
        if (audio_batch_cb) {
            const size_t samples_per_frame = 735;
            int16_t audio_buffer[samples_per_frame * 2]; // Interleaved Stereo L/R
            static float sample_phase = 0.0f;

            for (size_t i = 0; i < samples_per_frame; i++) {
                int16_t tone = (int16_t)(sinf(sample_phase) * 2000.0f);
                audio_buffer[i * 2] = tone;     // Left
                audio_buffer[i * 2 + 1] = tone; // Right
                sample_phase += 2.0f * 3.14159265f * 440.0f / 44100.0f;
            }
            audio_batch_cb(audio_buffer, samples_per_frame);
        }

        if (!input_state_cb) return;

        // --- 2. EXPLICIT MONITORING FOR INTERACTION ALERTS ---
        bool p1_guide_pressed = (input_state_cb(0, 1, 0, 16) != 0);
        bool p2_guide_pressed = (input_state_cb(1, 1, 0, 16) != 0);

        if (p1_guide_pressed || p2_guide_pressed) {
            std::cout << "\n========================================================";
            std::cout << "\n⚡ [GUIDE ALERT] - FRAME TICK: " << total_frames;
            std::cout << "\n========================================================\n";

            if (p1_guide_pressed) {
                std::cout << "\033[92m[P1 HARDWARE SIGNALLING] >>> CENTER MODE/GUIDE BUTTON CLICKED <<<\033[0m\n";
            }
            if (p2_guide_pressed) {
                std::cout << "\033[38;5;208m[P2 HARDWARE SIGNALLING] >>> CENTER MODE/GUIDE BUTTON CLICKED <<<\033[0m\n";
            }
            std::cout << "========================================================\n\n";
        }

        // --- 3. STANDARD DUAL-PLAYER RUNTIME MATRIX LOGGING ---
        for (unsigned port = 0; port < 2; port++) {
            std::string pressed_buttons = "";
            bool active = false;

            for (unsigned i = 0; i < 17; i++) {
                if (input_state_cb(port, 1, 0, i) != 0) {
                    if (i == 12 || i == 13 || i == 16) continue;
                    pressed_buttons += "[" + std::string(BUTTON_NAMES[i]) + "] ";
                    active = true;
                }
            }

            int16_t lx = input_state_cb(port, 2, 0, 0);
            int16_t ly = input_state_cb(port, 2, 0, 1);
            int16_t rx = input_state_cb(port, 2, 1, 0);
            int16_t ry = input_state_cb(port, 2, 1, 1);

            int16_t l_trig = input_state_cb(port, 1, 0, 12);
            int16_t r_trig = input_state_cb(port, 1, 0, 13);

            bool stick_moved = (abs(lx) > 1000 || abs(ly) > 1000 || abs(rx) > 1000 || abs(ry) > 1000);
            bool triggers_pulled = (l_trig > 5 || r_trig > 5);

            if (stick_moved || triggers_pulled) active = true;

            if (active) {
                if (port == 0) {
                    std::cout << "\033[96m[P1 LOCAL]  Frame: " << total_frames
                        << " | Buttons: " << (pressed_buttons.empty() ? "None " : pressed_buttons)
                        << "| LS: (" << lx << "," << ly << ") "
                        << "| RS: (" << rx << "," << ry << ") "
                        << "| Triggers: L: " << l_trig << " / R: " << r_trig << "\033[0m\n";
                }
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
