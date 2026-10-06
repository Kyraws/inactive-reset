#include "spot_resolver.h"
#include <fstream>
#include <iterator>
#include <vector>

int main(int argc, char** argv) {
    if (argc != 2) return 2;
    std::ifstream file(argv[1], std::ios::binary);
    std::vector<uint8_t> image(std::istreambuf_iterator<char>{file}, {});
    SpotSite site{};
    if (!resolve_spot(image.data(), image.size(), site)) return 3;
    if (site.function != 0x00A908C0 || site.count != 0x01DFA618 || site.table != 0x01DFA5D8) return 4;
    image[site.function] = 0;
    if (resolve_spot(image.data(), image.size(), site)) return 5;
    return 0;
}
