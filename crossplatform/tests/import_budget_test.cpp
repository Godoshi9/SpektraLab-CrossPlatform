#include "import_budget.hpp"
#include <cstdio>
#include <stdexcept>
int main() {
    try {
        constexpr uint64_t gib = 1ull << 30;
        auto check = [](bool ok) { if (!ok) throw std::runtime_error("memory admission regression"); };
        // Reproduces the user's less-than-3-GiB state without consuming RAM.
        const auto edge = sl::import_edge(5*gib/2, 2*gib, 4096);
        check(edge >= 256 && edge < 4096);
        check(spk::gpu::memory::allows(5*gib/2, 64ull << 20));
        check(!spk::gpu::memory::allows(5*gib/2, 64ull << 20, 3*gib));
        check(!spk::gpu::memory::allows(gib, 1));
        check(!spk::gpu::memory::allows(gib-1, UINT64_MAX));
        check(spk::gpu::memory::allows(gib+100, 100));
        check(!spk::gpu::memory::allows(gib+100, 101));
        check(sl::import_edge(gib, 16*gib, 4096) == 0);
        check(sl::import_edge(16*gib, 128ull << 20, 4096) == 0);
        check(sl::import_edge(16*gib, 16*gib, 4096) == 4096);
        check(sl::import_edge(16*gib, 16*gib, 512) == 512);
        std::printf("PASS: constrained import edge=%d, reserve retained, no underflow\n", edge);
        return 0;
    } catch(const std::exception& e) { std::fprintf(stderr,"%s\n",e.what()); return 1; }
}
