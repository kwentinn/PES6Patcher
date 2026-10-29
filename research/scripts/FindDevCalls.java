// List functions that call a given IDirect3DDevice8 vtable slot and that use the
// device global, then decompile them.
// Args: output file, vtable offset (hex), device global (hex), max functions to decompile.
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.util.*;

public class FindDevCalls extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        PrintWriter out = new PrintWriter(new FileWriter(args[0]));
        String slot = "+ 0x" + args[1].toLowerCase() + "]";
        Address dev = toAddr(Long.parseLong(args[2], 16));
        int max = Integer.parseInt(args[3]);

        Set<Function> usesDev = new HashSet<>();
        for (Reference r : getReferencesTo(dev)) {
            Function f = getFunctionContaining(r.getFromAddress());
            if (f != null) usesDev.add(f);
        }

        Map<Function, List<Address>> hits = new LinkedHashMap<>();
        InstructionIterator it = currentProgram.getListing().getInstructions(true);
        while (it.hasNext()) {
            Instruction ins = it.next();
            if (!ins.getMnemonicString().equals("CALL")) continue;
            String s = ins.toString();
            if (!s.endsWith(slot)) continue;
            Function f = getFunctionContaining(ins.getAddress());
            if (f == null || !usesDev.contains(f)) continue;
            hits.computeIfAbsent(f, k -> new ArrayList<>()).add(ins.getAddress());
        }

        for (Map.Entry<Function, List<Address>> e : hits.entrySet())
            out.println(e.getKey().getName() + " @ " + e.getKey().getEntryPoint() + " calls at " + e.getValue());

        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        int n = 0;
        for (Function f : hits.keySet()) {
            if (n++ >= max) break;
            DecompileResults res = dec.decompileFunction(f, 60, monitor);
            out.println("=== " + f.getName() + " @ " + f.getEntryPoint());
            out.println(res.decompileCompleted() ? res.getDecompiledFunction().getC() : "<decompile failed>");
        }
        out.close();
    }
}
