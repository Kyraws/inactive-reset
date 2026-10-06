#include "pit_resolver.h"
#include <fstream>
#include <iterator>
#include <cstdio>
#include <cstring>
#include <vector>

int main(int argc, char** argv) {
    const bool probe = argc == 3 && strcmp(argv[2], "--probe") == 0;
    if (argc != 2 && !probe) return 2;
    std::ifstream file(argv[1], std::ios::binary);
    std::vector<uint8_t> image(std::istreambuf_iterator<char>{file}, {});
    PitSite site{};
    if (!resolve_pit(image.data(), image.size(), site)) {
        puts("no unique validated path");
        return 3;
    }
    printf("penalty=%08llX message=%08llX flag=%08llX gate=%08llX return=%08llX\n",
           static_cast<unsigned long long>(site.function), static_cast<unsigned long long>(site.message),
           static_cast<unsigned long long>(site.flag), static_cast<unsigned long long>(site.decision_function),
           static_cast<unsigned long long>(site.speed_call_return));
    const bool d9 = site.function == 0x00CF4860 && site.message == 0x017C78C0 &&
                    site.flag == 0x01E10A38 && site.decision_function == 0x00CEAEA0 &&
                    site.speed_call_return == 0x00CE59FA;
    const bool next = site.function == 0x00CF4C80 && site.message == 0x017C8E10 &&
                      site.flag == 0x01E11B38 && site.decision_function == 0x00CEB2C0 &&
                      site.speed_call_return == 0x00CE5E1A;
    const bool current = site.function == 0x00CF7470 && site.message == 0x017CAEA0 &&
                         site.flag == 0x01E13E68 && site.decision_function == 0x00CED950 &&
                         site.speed_call_return == 0x00CE84AA;
    if (!probe && !d9 && !next && !current) return 4;
    image[site.speed_call_return - 5] = 0;
    if (resolve_pit(image.data(), image.size(), site)) return 5;
    image[site.speed_call_return - 5] = 0xe8;
    image[site.message] = 0;
    if (resolve_pit(image.data(), image.size(), site)) return 6;
    return 0;
}
