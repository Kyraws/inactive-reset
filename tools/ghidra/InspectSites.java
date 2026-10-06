// Inspect a small set of known LMU runtime RVAs in a mapped module dump.
// @category LMU

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import java.io.File;
import java.io.PrintWriter;

public class InspectSites extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        try (PrintWriter out = new PrintWriter(new File(args[0]), "UTF-8")) {
            out.println("program=" + currentProgram.getName());
            out.println("imageBase=" + currentProgram.getImageBase());
            for (int i = 1; i < args.length; i++) {
                Address start = currentProgram.getAddressFactory().getAddress(args[i]);
                out.println("\n## " + args[i]);
                if (start == null || !currentProgram.getMemory().contains(start)) {
                    out.println("outside image");
                    continue;
                }
                Function function = currentProgram.getFunctionManager().getFunctionContaining(start);
                out.println("function=" + (function == null ? "unidentified" : function.getEntryPoint()));
                disassemble(start);
                Instruction instruction = currentProgram.getListing().getInstructionAt(start);
                Instruction previous = instruction;
                for (int n = 0; n < 12 && previous != null; n++) {
                    previous = previous.getPrevious();
                }
                while (previous != null && instruction != null && previous.getAddress().compareTo(instruction.getAddress()) < 0) {
                    out.println(previous.getAddress() + "  " + previous);
                    previous = previous.getNext();
                }
                for (int n = 0; n < 48 && instruction != null; n++) {
                    out.print(instruction.getAddress() + "  " + instruction);
                    for (Reference ref : instruction.getReferencesFrom()) {
                        out.print("  -> " + ref.getToAddress() + " (" + ref.getReferenceType() + ")");
                    }
                    out.println();
                    instruction = instruction.getNext();
                }
            }
        }
    }
}
