#pragma once
#include <stdint.h>
#ifdef _WIN32
#define SL_API __declspec(dllexport)
#else
#define SL_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
// One handle, one serial worker. Strings and pixel pointers are borrowed until
// the next call. No platform allocator crosses this boundary.
SL_API void* sl_create(const char* resources);
SL_API void sl_destroy(void* handle);
SL_API const char* sl_error(void* handle);
SL_API const char* sl_catalog(void* handle);
// Trim unused GPU memory, then choose a safe long edge before host decoding.
// Returns 0 with sl_error when there is not enough budget even for 256 px.
SL_API int sl_prepare_import(void* handle, int maximum_edge);
SL_API int sl_open_argb(void* handle, const uint32_t* pixels, int w, int h, const char* settings);
SL_API int sl_open_raw(void* handle, const char* path, int max_edge, const char* settings);
SL_API int sl_render(void* handle, const char* settings);
SL_API const uint32_t* sl_pixels(void* handle, int original);
SL_API int sl_width(void* handle);
SL_API int sl_height(void* handle);
SL_API double sl_elapsed(void* handle);
#ifdef __cplusplus
}
#endif
