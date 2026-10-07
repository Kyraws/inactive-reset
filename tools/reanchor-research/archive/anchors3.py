"""Which anchors survive a recompilation?

Signature matching failed on getSpotTransform because the compiler regenerated
it. Test the anchors that do not depend on the instruction bytes:

  1. RTTI class names  - literal strings, immune to codegen changes
  2. string references - the function's own string constants
  3. vtable membership - if it is virtual, its slot index is stable
"""
import re, struct
from capstone import *
from capstone.x86 import X86_OP_MEM, X86_REG_RIP

OLD = r"G:\LMU_Plugin\analysis\dumps\LMU_runtime_44C3EE9C.bin"
NEW = r"G:\LMU_Plugin\analysis\dumps\LMU_runtime_C40C815F.bin"
old = open(OLD, "rb").read()
new = open(NEW, "rb").read()

# ---- 1. RTTI ------------------------------------------------------------
rtti_old = re.findall(rb"\.\?A[VU][A-Za-z0-9_@?$]{2,120}@@", old)
rtti_new = re.findall(rb"\.\?A[VU][A-Za-z0-9_@?$]{2,120}@@", new)
print(f"RTTI type descriptors:  old {len(rtti_old):,}   new {len(rtti_new):,}")
if rtti_new:
    for s in rtti_new[:6]:
        print("   ", s.decode("ascii", "replace")[:88])

# ---- 2. strings referenced by the failing functions ---------------------
md = Cs(CS_ARCH_X86, CS_MODE_64); md.detail = True
FAILED = {
    "getSpotTransform":        0x00AB80C0,
    "getSpotTransformSibling": 0x00AB8460,
    "applyVehicleTransform":   0x00F86AB0,
    "slotRestart":             0x00D2F810,
    "assignSpotIndices":       0x00D28F20,
}

def ascii_at(buf, off, limit=90):
    out = bytearray()
    for i in range(off, min(off + limit, len(buf))):
        b = buf[i]
        if b == 0:
            break
        if 32 <= b < 127:
            out.append(b)
        else:
            return None
    return out.decode() if len(out) >= 5 else None

print("\nstring constants referenced in the first 512 bytes of each failing function:")
for name, rva in FAILED.items():
    found = []
    for ins in md.disasm(old[rva:rva + 512], rva):
        for op in ins.operands:
            if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                target = ins.address + ins.size + op.mem.disp
                if 0 <= target < len(old):
                    s = ascii_at(old, target)
                    if s:
                        found.append(s)
    uniq = list(dict.fromkeys(found))
    print(f"  {name:24} {len(uniq)} string(s): {uniq[:3] if uniq else '--'}")

# ---- 3. is it virtual? look for its address inside a pointer array ------
# Dumps store absolute VAs, so recover the old image base first: a vtable slot
# would read as base+rva. Find the base by looking for any qword whose low bits
# match a known function rva.
print("\nlooking for the old image base via a self-referencing pointer...")
probe_rva = 0x00AB80C0
bases = {}
target_low = probe_rva & 0xFFFF
for m in re.finditer(rb"[\x00-\xff]{8}", old[:0x400000]):
    pass  # too slow to brute force; use the known ASLR shape instead
# Windows x64 images load at 0x7FF6_0000_0000-ish. Search .rdata for a qword
# that ends in the function's low 16 bits and sits in a run of similar pointers.
hits = 0
for off in range(0x1D00000, 0x1E00000, 8):
    q = struct.unpack_from("<Q", old, off)[0]
    if q & 0xFFFF == target_low and 0x7FF000000000 < q < 0x7FFFFFFFFFFF:
        base = q - probe_rva
        bases[base] = bases.get(base, 0) + 1
        hits += 1
print(f"  candidate bases: {[(hex(b), c) for b, c in sorted(bases.items(), key=lambda x: -x[1])[:3]]}")
