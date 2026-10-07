"""Call-graph anchoring: find a recompiled function through the functions that
CALL it.

getSpotTransform has no strings of its own, so it cannot be string-anchored.
But 26 sites call it. For each caller that can still be located in the new
image by masked signature, decode the call at the same relative offset and read
off the callee. Then take the majority vote, exactly as RemapData does.
"""
import re, struct
from capstone import *
from capstone.x86 import X86_OP_MEM, X86_REG_RIP

old = open(r"G:\LMU_Plugin\analysis\dumps\LMU_runtime_44C3EE9C.bin", "rb").read()
new = open(r"G:\LMU_Plugin\analysis\dumps\LMU_runtime_C40C815F.bin", "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_64); md.detail = True

TARGETS = {
    "getSpotTransform":        0x00AB80C0,
    "getSpotTransformSibling": 0x00AB8460,
    "applyVehicleTransform":   0x00F86AB0,
}


def callers(buf, target):
    out = []
    for m in re.finditer(rb"\xe8", buf):
        i = m.start()
        if i + 5 > len(buf):
            continue
        if i + 5 + struct.unpack_from("<i", buf, i + 1)[0] == target:
            out.append(i)
    return out


def func_start(buf, inside, back=0x1400):
    for off in range(inside, max(0, inside - back), -1):
        if buf[off - 1] in (0xCC, 0x90) and buf[off] in (0x40, 0x48, 0x4C, 0x55, 0x53, 0x56, 0x57):
            return off
    return None


def masked_regex(buf, rva, length):
    """Pattern with RIP displacements AND branch rel32s wildcarded."""
    code = buf[rva:rva + length]
    mask = [True] * length
    for ins in md.disasm(code, rva):
        off = ins.address - rva
        if off >= length:
            break
        for op in ins.operands:
            if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                for b in range(off + ins.disp_offset, min(off + ins.disp_offset + ins.disp_size, length)):
                    mask[b] = False
        if ins.imm_size == 4 and (ins.mnemonic == "call" or ins.mnemonic.startswith("j")):
            for b in range(off + ins.imm_offset, min(off + ins.imm_offset + 4, length)):
                mask[b] = False
    if sum(mask) < 8:
        return None
    return b"".join(re.escape(bytes([code[i]])) if mask[i] else b"." for i in range(length))


for name, target in TARGETS.items():
    sites = callers(old, target)
    votes = {}
    used = 0
    for site in sites:
        start = func_start(old, site)
        if start is None:
            continue
        offset = site - start
        for length in (128, 96, 64):
            if offset + 5 > length:
                continue
            rx = masked_regex(old, start, length)
            if rx is None:
                continue
            hits = [m.start() for _, m in zip(range(2), re.finditer(rx, new, re.DOTALL))]
            if len(hits) != 1:
                continue
            call_site = hits[0] + offset
            if new[call_site] != 0xE8:
                continue
            callee = call_site + 5 + struct.unpack_from("<i", new, call_site + 1)[0]
            votes[callee] = votes.get(callee, 0) + 1
            used += 1
            break
    print(f"{name:24} old {target:#010x}  callers {len(sites):>3}  usable {used:>3}")
    for addr, n in sorted(votes.items(), key=lambda x: -x[1])[:4]:
        print(f"    -> {addr:#010x}  {n} vote(s)   delta {addr - target:+#x}")
    if not votes:
        print("    -> no caller could be located")
