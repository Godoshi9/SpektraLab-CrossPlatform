#pragma once
#include "gpu/windows_memory_budget.hpp"
#include <algorithm>
#include <cmath>
#include <cstdint>

namespace sl {
// Admission is an estimate, never a replacement for the per-allocation guard.
// Use at most half of remaining commit/RAM, reserve another 128 MiB for
// decoding/UI transfers, and budget 256 bytes per working pixel. A square
// long-edge bound is conservative for landscape/portrait photographs.
inline int import_edge(uint64_t available_commit, uint64_t available_ram, int maximum) {
    constexpr uint64_t overhead = 128ull << 20, per_pixel = 256;
    const auto reserve = spk::gpu::memory::reserve_bytes;
    if (available_commit <= reserve) return 0;
    const uint64_t budget = std::min((available_commit - reserve) / 2, available_ram / 2);
    if (budget <= overhead) return 0;
    const auto pixels = (budget - overhead) / per_pixel;
    if (pixels < 256 * 256) return 0;
    return std::min(maximum, int(std::min(4096., std::sqrt(double(pixels)))));
}
}
