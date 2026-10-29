// Decompile every function that references the given addresses.
// Args: output file, then hex addresses (e.g. B7DAE0).
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.util.*;

public class DumpRefs extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        PrintWriter out = new PrintWriter(new FileWriter(args[0]));
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        Set<Function> done = new HashSet<>();
        for (int i = 1; i < args.length; i++) {
            Address target = toAddr(Long.parseLong(args[i], 16));
            out.println("##### refs to " + target);
            for (Reference r : getReferencesTo(target)) {
                Function f = getFunctionContaining(r.getFromAddress());
                out.println("  from " + r.getFromAddress() + " in " + (f == null ? "?" : f.getName() + " @ " + f.getEntryPoint()));
                if (f == null || !done.add(f)) continue;
                DecompileResults res = dec.decompileFunction(f, 60, monitor);
                out.println("=== " + f.getName() + " @ " + f.getEntryPoint());
                out.println(res.decompileCompleted() ? res.getDecompiledFunction().getC() : "<decompile failed>");
            }
        }
        out.close();
    }
}
