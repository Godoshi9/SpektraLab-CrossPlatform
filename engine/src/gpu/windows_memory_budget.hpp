#pragma once
#include <cstdint>

// The full upstream desktop keeps its original 3 GiB reserve. A host that
// bounds its working image before decoding may choose a smaller build-time
// reserve, but must always leave at least 1 GiB for Windows and recovery.
#ifndef SPK_WINDOWS_COMMIT_RESERVE_MIB
#define SPK_WINDOWS_COMMIT_RESERVE_MIB 3072
#endif
namespace spk::gpu::memory {
inline constexpr uint64_t reserve_bytes = uint64_t(SPK_WINDOWS_COMMIT_RESERVE_MIB) << 20;
static_assert(SPK_WINDOWS_COMMIT_RESERVE_MIB >= 1024);
inline constexpr bool allows(uint64_t available, uint64_t requested, uint64_t reserve = reserve_bytes) {
    return available > reserve && requested <= available - reserve;
}
}
