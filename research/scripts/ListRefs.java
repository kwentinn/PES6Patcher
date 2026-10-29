// One line per reference to each given address: from-address, instruction bytes,
// instruction text, containing function.
// Args: output file, then hex addresses.
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;

public class ListRefs extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        PrintWriter out = new PrintWriter(new FileWriter(args[0]));
        for (int i = 1; i < args.length; i++) {
            Address target = toAddr(Long.parseLong(args[i], 16));
            for (Reference r : getReferencesTo(target)) {
                Address from = r.getFromAddress();
                Instruction ins = getInstructionAt(from);
                Function f = getFunctionContaining(from);
                StringBuilder hex = new StringBuilder();
                if (ins != null) for (byte b : ins.getBytes()) hex.append(String.format("%02X", b));
                out.println(target + "\t" + from + "\t" + hex + "\t" + (ins == null ? "?" : ins.toString())
                        + "\t" + (f == null ? "?" : f.getName()));
            }
        }
        out.close();
    }
}
