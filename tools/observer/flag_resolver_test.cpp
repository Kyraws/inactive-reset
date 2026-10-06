#include "flag_resolver.h"
#include <fstream>
#include <iterator>
#include <vector>

int main(int argc, char** argv) {
    if (argc != 2) return 2;
    std::ifstream file(argv[1], std::ios::binary);
    std::vector<uint8_t> image(std::istreambuf_iterator<char>{file}, {});
    FlagSite site{};
    if (!resolve_flag(image.data(), image.size(), site)) return 3;
    if (site.function != 0x00D1C270 || site.reader != 0x00D1D3A6 || site.value != 0x01E10A38) return 4;
    image[site.reader] = 0;
    if (resolve_flag(image.data(), image.size(), site)) return 5;
    return 0;
}
