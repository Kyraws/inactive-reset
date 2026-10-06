#include "pit_lookup_resolver.h"
#include <fstream>
#include <iterator>
#include <vector>
#include <cstdio>
#include <cstring>

int main(int argc, char** argv) {
    if (argc != 2) return 2;
    std::ifstream file(argv[1], std::ios::binary);
    std::vector<uint8_t> image(std::istreambuf_iterator<char>{file}, {});
    PitLookupSite site{};
    if (!resolve_pit_lookup(image.data(), image.size(), site)) {
        fprintf(stderr, "pit lookup not unique or unvalidated: first=%llx\n", static_cast<unsigned long long>(site.function));
        return 3;
    }
    if (site.function != 0x00A90C70 || site.selector != 0x01DFA61C || site.table != 0x01DFA590) return 4;
    const uint8_t first = image[site.function];
    image[site.function] = 0;
    if (resolve_pit_lookup(image.data(), image.size(), site)) return 5;
    image[site.function] = first;
    constexpr size_t duplicate = 0x00A91600;
    memcpy(image.data() + duplicate, image.data() + site.function, 0x234);
    auto relocate = [&](size_t offset, size_t displacement, size_t length, uintptr_t destination) {
        const int32_t relative = static_cast<int32_t>(destination - (duplicate + offset + length));
        memcpy(image.data() + duplicate + offset + displacement, &relative, 4);
    };
    relocate(0x1d, 2, 7, site.selector);
    relocate(0x26, 3, 7, site.table + 0x18);
    relocate(0x73, 3, 7, site.table);
    relocate(0x22d, 3, 7, site.table);
    if (resolve_pit_lookup(image.data(), image.size(), site)) return 6;
    return 0;
}
