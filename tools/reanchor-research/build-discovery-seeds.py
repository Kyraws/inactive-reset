"""Build instruction anchors for fields not covered by structural resolvers.

Developer tool only; the shipped application reads the resulting JSON using .NET.
Requires capstone. Run with a known mapped image and its offset profile.
"""
import json
import struct
import sys
from collections import defaultdict
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_GRP_JUMP, CS_GRP_CALL
from capstone.x86 import X86_REG_RIP, X86_OP_MEM

source, profile_path, output, *checks = sys.argv[1:]
image = Path(source).read_bytes()
profile = json.loads(Path(profile_path).read_text(encoding="utf-8-sig"))
pe = struct.unpack_from("<I", image, 0x3c)[0]
count, optional = struct.unpack_from("<H", image, pe + 6)[0], struct.unpack_from("<H", image, pe + 20)[0]
sections = []
for i in range(count):
    at = pe + 24 + optional + i * 40
    size, start = struct.unpack_from("<II", image, at + 8)
    if struct.unpack_from("<I", image, at + 36)[0] & 0x20000000:
        sections.append((start, start + size))

fields = profile["containers"]["offsets"]
names = ["vehCachedPose", "vehMotionState", "vehGear", "countLapFlag", "lapNumber", "sector", "restOffsetPrimary", "restOffsetSecondary"]
targets = defaultdict(list)
for name in names:
    targets[int(fields[name]["off"], 16)].append(name)
decoder = Cs(CS_ARCH_X86, CS_MODE_64)
decoder.detail = True
decoder.skipdata = True
candidates = defaultdict(list)
for start, end in sections:
    # Search operand values first; decode only their small instruction windows.
    for value, field_names in targets.items():
        needle = struct.pack("<I", value)
        pos = image.find(needle, start, end)
        while pos >= 0:
            for site in range(max(start, pos - 5), pos):
                insns = list(decoder.disasm(image[site:site + 64], site))
                if not insns or insns[0].id == 0:
                    continue
                first = insns[0]
                if "restOffsetSecondary" in field_names and first.mnemonic != "addss":
                    continue
                if first.disp_size != 4 or site + first.disp_offset != pos or not any(o.type == X86_OP_MEM and o.mem.base != X86_REG_RIP and o.mem.disp == value for o in first.operands):
                    continue
                pattern = []
                length = 0
                for insn in insns:
                    if insn.id == 0:
                        break
                    masked = list(insn.bytes)
                    if insn.disp_size:
                        masked[insn.disp_offset:insn.disp_offset + insn.disp_size] = [None] * insn.disp_size
                    if insn.group(CS_GRP_JUMP) or insn.group(CS_GRP_CALL):
                        masked[insn.imm_offset:insn.imm_offset + insn.imm_size] = [None] * insn.imm_size
                    pattern.extend(masked)
                    length += insn.size
                    if length >= 40:
                        break
                if length < 32:
                    continue
                for name in field_names:
                    candidates[name].append((site, first.disp_offset, pattern))
            pos = image.find(needle, pos + 4, end)

import re
images = [image] + [Path(p).read_bytes() for p in checks]
result = {}
for name in names:
    anchors = []
    sites = []
    values = [set() for _ in images]
    for site, operand, pattern in candidates[name]:
        if any(abs(site - previous) < 64 for previous in sites):
            continue
        regex = re.compile(b"".join(b"." if b is None else re.escape(bytes([b])) for b in pattern), re.DOTALL)
        hits = [list(regex.finditer(data)) for data in images]
        if any(len(h) != 1 for h in hits):
            continue
        resolved = [struct.unpack_from("<I", data, h[0].start() + operand)[0] for data, h in zip(images, hits)]
        if any(v < 0x20 or v > 0x50000 for v in resolved):
            continue
        anchors.append({"pattern": " ".join("??" if b is None else f"{b:02X}" for b in pattern), "operand": operand})
        sites.append(site)
        for vs, v in zip(values, resolved):
            vs.add(v)
        if len(anchors) == 4:
            break
    if len(anchors) < (1 if name == "restOffsetSecondary" else 2) or any(len(vs) != 1 for vs in values):
        raise SystemExit(f"{name}: insufficient agreeing independent anchors ({len(anchors)}, {values})")
    result[name] = {"type": fields[name]["type"], "base": fields[name].get("base", "container"), "anchors": anchors}
    print(name, [f"0x{next(iter(v)):X}" for v in values])
Path(output).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
