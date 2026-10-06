#include "final_transform_resolver.h"
#include <fstream>
#include <iterator>
#include <vector>

int main(int argc, char** argv) {
    if (argc != 2) return 2;
    std::ifstream file(argv[1], std::ios::binary);
    std::vector<uint8_t> image(std::istreambuf_iterator<char>{file}, {});
    uintptr_t target = 0, caller = 0;
    if (!resolve_final_transform(image.data(), image.size(), target, caller) ||
        !((target == 0x00A908C0 && caller == 0x00D148B9) ||
          (target == 0x00A90A40 && caller == 0x00D14CF9))) return 3;
    uintptr_t indexed = 0, indexed_caller = 0;
    if (!resolve_indexed_transform(image.data(), image.size(), indexed, indexed_caller) ||
        !((indexed == 0x00D31570 && indexed_caller == 0x00D1482D) ||
          (indexed == 0x00D319B0 && indexed_caller == 0x00D14C6D))) return 5;
    image[indexed] ^= 1;
    if (resolve_indexed_transform(image.data(), image.size(), indexed, indexed_caller)) return 6;
    image[indexed] ^= 1;
    image[target] ^= 1;
    if (resolve_final_transform(image.data(), image.size(), target, caller)) return 4;
    return 0;
}
