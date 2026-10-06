// Export bounded, read-only evidence for data readers in a mapped LMU dump.
// Arguments: output.json "label|dataRva|readerRva" ...
// @category LMU

import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.Reference;
import java.io.FileWriter;

public class ExportReaderEvidence extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 2) throw new IllegalArgumentException("output and at least one reader required");

        JsonObject report = new JsonObject();
        report.addProperty("schema", 1);
        report.addProperty("program", currentProgram.getName());
        report.addProperty("inputSha256", currentProgram.getExecutableSHA256());
        report.addProperty("imageBase", currentProgram.getImageBase().toString());
        report.addProperty("scope", "seed instructions only; not whole-program xrefs or runtime proof");
        JsonArray sites = new JsonArray();
        report.add("sites", sites);

        for (int i = 1; i < args.length; i++) {
            String[] fields = args[i].contains("|") ? args[i].split("\\|", -1) : args[i].split("~", -1);
            if (fields.length != 3) throw new IllegalArgumentException("expected label|dataRva|readerRva: " + args[i]);
            JsonObject site = new JsonObject();
            site.addProperty("label", fields[0]);
            site.addProperty("dataRva", fields[1]);
            site.addProperty("readerRva", fields[2]);
            sites.add(site);

            if (fields[2].equals("-")) {
                site.addProperty("status", "unresolved");
                continue;
            }
            Address data = fields[1].equals("-") ? null : currentProgram.getAddressFactory().getAddress(fields[1]);
            Address reader = currentProgram.getAddressFactory().getAddress(fields[2]);

            if ((data != null && !currentProgram.getMemory().contains(data)) || reader == null
                    || !currentProgram.getMemory().contains(reader)) {
                site.addProperty("status", "outside-image");
                continue;
            }

            disassemble(reader);
            Instruction instruction = currentProgram.getListing().getInstructionAt(reader);
            if (instruction == null) {
                site.addProperty("status", "undecoded");
                continue;
            }

            site.addProperty("instruction", instruction.toString());
            site.addProperty("bytes", bytes(instruction.getBytes()));
            JsonArray refs = new JsonArray();
            boolean matched = false;
            for (Reference ref : instruction.getReferencesFrom()) {
                JsonObject item = new JsonObject();
                item.addProperty("targetRva", ref.getToAddress().toString());
                item.addProperty("type", ref.getReferenceType().toString());
                refs.add(item);
                if (data != null && ref.getToAddress().equals(data)) matched = true;
            }
            site.add("references", refs);
            site.addProperty("status", data == null ? "context-only" :
                matched ? "direct-reference" : "no-direct-reference");
            if (data == null) {
                JsonArray window = new JsonArray();
                Instruction next = instruction;
                for (int n = 0; n < 16 && next != null
                        && next.getAddress().subtract(reader) < 96; n++, next = next.getNext()) {
                    JsonObject line = new JsonObject();
                    line.addProperty("rva", next.getAddress().toString());
                    line.addProperty("instruction", next.toString());
                    window.add(line);
                }
                site.add("window", window);
            }
        }

        try (FileWriter out = new FileWriter(args[0])) {
            new GsonBuilder().setPrettyPrinting().create().toJson(report, out);
        }
    }

    private static String bytes(byte[] data) {
        StringBuilder result = new StringBuilder();
        for (byte value : data) result.append(String.format("%02X", value & 0xff));
        return result.toString();
    }
}
