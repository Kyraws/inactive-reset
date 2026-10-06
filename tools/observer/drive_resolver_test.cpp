#include "drive_resolver.h"
#include <fstream>
#include <iterator>
#include <vector>
#include <cstring>

int main(int argc, char** argv) {
    if (argc != 2) return 2;
    std::ifstream file(argv[1], std::ios::binary);
    std::vector<uint8_t> image(std::istreambuf_iterator<char>{file}, {});
    DriveSite site{};
    if (!resolve_drive(image.data(), image.size(), site) ||
        !((site.function == 0x00D14440 && site.count == 0x01DFA608 && site.table == 0x01DFA578) ||
          (site.function == 0x00D14880 && site.count == 0x01DFB708 && site.table == 0x01DFB678))) return 3;
    const auto byte = image[site.function];
    image[site.function] = 0;
    if (resolve_drive(image.data(), image.size(), site)) return 4;
    image[site.function] = byte;
    const size_t duplicate = site.function + 0x1000;
    memcpy(image.data() + duplicate, image.data() + site.function, 0x480);
    auto relocate = [&](size_t offset, size_t displacement, size_t length, uintptr_t target) {
        const int32_t relative = static_cast<int32_t>(target - (duplicate + offset + length));
        memcpy(image.data() + duplicate + offset + displacement, &relative, 4);
    };
    relocate(0x340, 3, 7, site.table);
    relocate(0x3f6, 2, 6, site.count);
    relocate(0x417, 3, 7, site.table);
    relocate(0x474, 1, 5, site.function == 0x00D14440 ? 0x00A908C0 : 0x00A90A40);
    if (resolve_drive(image.data(), image.size(), site)) return 5;
    return 0;
}
