// Names the known functions and data of PES6.exe in the Ghidra project, from research/ghidra-labels.csv.
// Args: the csv file. Lines: kind,address,name,comment (kind f = function, d = data; '#' starts a comment line).
// A function that does not exist yet is created from its entry (the project is imported without analysis).
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.nio.file.*;

public class ApplyLabels extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        int done = 0, failed = 0;
        for (String line : Files.readAllLines(Paths.get(args[0]))) {
            line = line.trim();
            if (line.isEmpty() || line.startsWith("#")) continue;
            String[] f = line.split(",", 4);
            if (f.length < 3) { println("skipped: " + line); continue; }
            try {
                Address a = toAddr(Long.parseLong(f[1].trim().replace("0x", ""), 16));
                String name = f[2].trim(), comment = f.length > 3 ? f[3].trim() : "";
                if (f[0].trim().equals("f")) {
                    disassemble(a);
                    Function fn = getFunctionAt(a);
                    if (fn == null) fn = createFunction(a, name);
                    if (fn == null) throw new Exception("no function could be created");
                    fn.setName(name, SourceType.USER_DEFINED);
                    if (!comment.isEmpty()) setPlateComment(a, comment);
                } else {
                    createLabel(a, name, true);
                    if (!comment.isEmpty()) setEOLComment(a, comment);
                }
                done++;
            } catch (Exception e) {
                println("FAILED " + line + " : " + e.getMessage());
                failed++;
            }
        }
        println("labels applied: " + done + ", failed: " + failed);
    }
}
