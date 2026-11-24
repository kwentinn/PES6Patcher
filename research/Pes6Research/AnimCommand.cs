using System.Globalization;
using System.Numerics;
using System.Text;

namespace Pes6Research;

/// <summary>
/// Exports the player animations: every descriptor of the animation table as raw bytes, and as BVH when its format is the decoded one
/// (type 2: int16 quaternions, root position in floats, 164-byte keys). See the vault note "Animations".
/// </summary>
static class AnimCommand
{
    public const string Usage = "anims -Out dir [-First 0] [-Last 1554]          every animation of the running game: .bin, .bvh, .events.csv, index.csv\n" +
                                "anims -File anim_0003.bin [-Out dir]             a saved .bin to .bvh (+ .events.csv) without the game";

    const uint TablePointer = 0xBD7A50, Descriptors = 0x178C, FrameCounts = 0x2FD8;
    const int TableSize = (int)((FrameCounts - Descriptors) / 4); // 1555 ids

    public static int Run(Options o, string processName)
    {
        var file = o.String("File");
        var outDir = Path.GetFullPath(o.String("Out") ?? (file != null ? Path.GetDirectoryName(Path.GetFullPath(file))! : "anims"));
        var first = o.Int("First", 0); var last = o.Int("Last", TableSize - 1);
        o.CheckAllUsed();
        Directory.CreateDirectory(outDir);

        if (file != null)
        {
            var clip = AnimFormat.Decode(File.ReadAllBytes(file), out var why);
            if (clip == null) throw new ToolException($"{file}: {why}");
            var bvh = Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + ".bvh");
            File.WriteAllText(bvh, AnimFormat.ToBvh(clip));
            var events = AnimFormat.Events(File.ReadAllBytes(file));
            if (events.Count > 0) File.WriteAllText(Path.ChangeExtension(bvh, ".events.csv"), AnimFormat.EventsCsv(events, clip.Duration));
            Console.WriteLine($"{clip.Keys} keys, {AnimFormat.Heights(clip)} -> {bvh}");
            return 0;
        }

        using var game = GameProcess.Open(processName);
        var (written, bvhs) = Export(game, outDir, Math.Max(0, first), Math.Min(TableSize - 1, last), Console.Out);
        Console.WriteLine($"{written} animations, {bvhs} as BVH -> {outDir}");
        return 0;
    }

    /// <summary>Writes anim_NNNN.bin (+ .bvh when decoded) for each id with a descriptor, and index.csv.</summary>
    public static (int Written, int Bvhs) Export(IGameMemory m, string outDir, int first, int last, TextWriter log)
    {
        var table = BitConverter.ToUInt32(m.Read(TablePointer, 4));
        if (table == 0) throw new ToolException("no animation table: start a match first");
        var pointers = m.Read(table + Descriptors, TableSize * 4);
        var frames = m.Read(table + FrameCounts, TableSize * 4);
        var index = new StringBuilder("id,descriptor,frames,keys,duration,key_spacing,anim_offset,event_tracks,flags,loop,bvh,heights\n");
        int written = 0, bvhs = 0;
        for (var id = first; id <= last; id++)
        {
            var at = BitConverter.ToUInt32(pointers, id * 4);
            if (at == 0) continue;
            var length = AnimFormat.Length(m, at);
            if (length <= 0) { log.WriteLine($"id {id}: no animation block at 0x{at:X}, skipped"); continue; }
            var bytes = m.ReadOrZero(at, length);
            var name = Path.Combine(outDir, $"anim_{id:D4}");
            File.WriteAllBytes(name + ".bin", bytes);
            written++;
            var clip = AnimFormat.Decode(bytes, out var why);
            if (clip != null) { File.WriteAllText(name + ".bvh", AnimFormat.ToBvh(clip)); bvhs++; }
            var events = AnimFormat.Events(bytes);
            if (clip != null && events.Count > 0) File.WriteAllText(name + ".events.csv", AnimFormat.EventsCsv(events, clip.Duration));
            var animAt = BitConverter.ToInt32(bytes, AnimFormat.AnimOffset);
            index.Append(CultureInfo.InvariantCulture,
                $"{id},0x{at:X8},{BitConverter.ToInt32(frames, id * 4)},{clip?.Keys.ToString() ?? ""},{clip?.Duration.ToString("F4", CultureInfo.InvariantCulture) ?? ""},{(clip != null ? AnimFormat.Spacing(clip) : "")}," +
                $"0x{animAt:X},{events.Count},0x{BitConverter.ToUInt32(bytes, animAt + 4):X8},{bytes[0] & 1},{(clip != null ? "yes" : why)},{(clip != null ? AnimFormat.Heights(clip) : "")}\n");
        }
        File.WriteAllText(Path.Combine(outDir, "index.csv"), index.ToString());
        return (written, bvhs);
    }
}

/// <summary>A decoded animation: per key, its time (seconds, from 0), the root position and one rotation per bone (x, y, z, w).</summary>
sealed record AnimClip(int Keys, float[] Times, Vector3[] Root, Quaternion[][] Rotations)
{
    public float Duration => Times[^1];
}

/// <summary>
/// The descriptor format, the 19-bone skeleton and the BVH writer. A descriptor starts with 0x1C bytes whose word at 0x18 is the offset
/// of the animation block (0x1C, or more when an event section sits in between); the block: +4 flags, +8 duration (seconds),
/// +0x10 offset of the key table, the keys from +0x1C; the key table: count, then (time, offset) per key, where entry j holds the time
/// of key j + 1 (key 0 is at 0, the last entry goes back to 0). The spacing may change inside one animation (2 frames, then 4).
/// </summary>
static class AnimFormat
{
    public const int AnimOffset = 0x18, FirstKey = 0x1C, KeySize = 164, Bones = 19, Fps = 30;

    // Parent and rest offset of each bone, in key order (read in a match on 2026-11-24; one player, heights may vary).
    public static readonly int[] Parent = [-1, 0, 1, 1, 2, 3, 0, 6, 6, 7, 8, 6, 4, 5, 9, 10, 11, 14, 15];
    public static readonly Vector3[] Offset =
    [
        new(0, 0, 0), new(0, 0, 0), new(82.4f, 0, 37.6f), new(82.4f, 0, -37.6f), new(161.92f, 0, 0), new(161.92f, 0, 0),
        new(66.56f, 0, 0), new(72.96f, 0, 0), new(72.96f, 0, 0), new(80.8f, 0, 0), new(80.8f, 0, 0), new(112.96f, -11.2f, 0),
        new(177.76f, 14.72f, 0), new(177.76f, 14.72f, 0), new(124.96f, 0, 0), new(124.96f, 0, 0), new(29.76f, 0.32f, 0),
        new(91.52f, 0, 0), new(91.52f, 0, 0),
    ];
    // Right and left measured on 2026-11-24: in the game's world (x to the screen's right, y down, z toward the camera: left-handed),
    // the knees bend forward, and the bones with the +z hip are on the player's right in 1008 animations out of 1059.
    public static readonly string[] Names =
    [
        "root", "pelvis", "hip_r", "hip_l", "knee_r", "knee_l", "spine", "clavicle_r", "clavicle_l", "shoulder_r", "shoulder_l",
        "neck", "ankle_r", "ankle_l", "elbow_r", "elbow_l", "head", "wrist_r", "wrist_l",
    ];

    /// <summary>The whole descriptor's length (up to the end of its key table), read from the game; 0 when it does not look like one.</summary>
    public static int Length(IGameMemory m, uint at)
    {
        var anim = BitConverter.ToInt32(m.ReadOrZero(at + AnimOffset, 4));
        if (anim < FirstKey || anim > 1 << 16) return 0;
        var size = BitConverter.ToInt32(m.ReadOrZero(at + (uint)anim + 0x10, 4));
        if (size < FirstKey || size > 1 << 20) return 0;
        var keys = BitConverter.ToInt32(m.ReadOrZero(at + (uint)(anim + size), 4));
        return keys is < 1 or > 1 << 12 ? 0 : anim + size + 4 + 8 * keys;
    }

    /// <summary>The keys of a type-2 descriptor, or null with the reason when the format is another one.</summary>
    public static AnimClip? Decode(byte[] d, out string why)
    {
        why = "";
        if (d.Length < FirstKey + 4) { why = "too short"; return null; }
        var anim = BitConverter.ToInt32(d, AnimOffset);
        if (anim < FirstKey || anim + 0x14 > d.Length) { why = $"animation block at 0x{anim:X}"; return null; }
        var flags = BitConverter.ToUInt32(d, anim + 4);
        var table = anim + BitConverter.ToInt32(d, anim + 0x10);
        if (flags >> 28 != 2 || (flags & 2) == 0 || (flags & 8) != 0) { why = $"type {flags >> 28} flags 0x{flags:X}"; return null; }
        if (table < anim + FirstKey || table + 4 > d.Length) { why = "key table outside"; return null; }
        var keys = BitConverter.ToInt32(d, table);
        if (keys < 1 || anim + FirstKey + keys * KeySize != table) { why = $"{keys} keys do not fill {table - anim - FirstKey} bytes"; return null; }
        var times = new float[keys];
        for (var k = 1; k < keys; k++)
        {
            times[k] = BitConverter.ToSingle(d, table + 4 + 8 * (k - 1));
            if (!(times[k] > times[k - 1])) { why = $"key {k} at {times[k]} s is not after key {k - 1}"; return null; }
        }

        var root = new Vector3[keys]; var rot = new Quaternion[keys][];
        for (var k = 0; k < keys; k++)
        {
            var at = anim + FirstKey + k * KeySize;
            rot[k] = new Quaternion[Bones];
            rot[k][0] = Quat(d, at);
            root[k] = new(BitConverter.ToSingle(d, at + 8), BitConverter.ToSingle(d, at + 12), BitConverter.ToSingle(d, at + 16));
            for (var b = 1; b < Bones; b++) rot[k][b] = Quat(d, at + 20 + (b - 1) * 8);
        }
        return new AnimClip(keys, times, root, rot);
    }

    // The event tracks (2026-11-24): the u16 at 0x16 is the offset of the section (0 when none): track count, 8, then the offset of each
    // track from the section. A track: 0x29843918, kind 4 (curves, table 0xD3CF44), channel count, length in frames, then per channel the
    // offset of its curve from the track. A curve: point count, then u16 points: frame (bits 0-10, 60 per second), value (bits 11-15).
    // The game (0x8B5740) plays tracks 0, 1, 2 into the morph weights (value / 32) of model parts 0x10, 0x11, 0x12: face (15 channels;
    // channel 8 blinks), right hand, left hand (9 channels each; channel 4 opens the hand of a raised arm). Tracks 3 and 4 are not played.
    public static readonly string[] TrackNames = ["face", "hand_r", "hand_l", "track3", "track4"];

    /// <summary>The curves of the event section, per track then per channel: (frame, value 0..31) points; empty when there is none.</summary>
    public static List<(int Frame, int Value)[][]> Events(byte[] d)
    {
        var tracks = new List<(int Frame, int Value)[][]>();
        int at = d.Length > 0x18 ? BitConverter.ToUInt16(d, 0x16) : 0;
        if (at == 0 || at + 8 > d.Length) return tracks;
        var count = BitConverter.ToInt32(d, at);
        for (var t = 0; t < count && t < TrackNames.Length; t++)
        {
            var ts = at + BitConverter.ToInt32(d, at + 8 + 4 * t);
            if (d[ts + 4] != 4) break; // the only curve kind the game knows
            var channels = new (int, int)[d[ts + 5]][];
            for (var c = 0; c < channels.Length; c++)
            {
                var curve = ts + BitConverter.ToUInt16(d, ts + 8 + 2 * c);
                channels[c] = Enumerable.Range(0, BitConverter.ToUInt16(d, curve))
                    .Select(i => BitConverter.ToUInt16(d, curve + 2 + 2 * i)).Select(w => (w & 0x7FF, w >> 11)).ToArray();
            }
            tracks.Add(channels);
        }
        return tracks;
    }

    /// <summary>
    /// The weights frame by frame as the game computes them (0x8D1CD0, one step per frame at 60 per second, from 0): toward the first point,
    /// linear between points, then the last value. CSV at 30 frames per second, like the BVH: frame, then one column per channel.
    /// </summary>
    public static string EventsCsv(List<(int Frame, int Value)[][]> tracks, float duration)
    {
        var s = new StringBuilder("frame");
        for (var t = 0; t < tracks.Count; t++) for (var c = 0; c < tracks[t].Length; c++) s.Append(CultureInfo.InvariantCulture, $",{TrackNames[t]}_{c}");
        s.Append('\n');
        var weights = tracks.Select(tr => new float[tr.Length]).ToArray();
        var last = (int)MathF.Round(duration * 60);
        for (var f = 0; f <= last; f++)
        {
            for (var t = 0; t < tracks.Count; t++)
                for (var c = 0; c < tracks[t].Length; c++) weights[t][c] = CurveAt(tracks[t][c], f, weights[t][c]);
            if (f % 2 != 0) continue;
            s.Append(f.ToString(CultureInfo.InvariantCulture));
            foreach (var w in weights.SelectMany(x => x)) s.Append(',').Append(w.ToString("F3", CultureInfo.InvariantCulture));
            s.Append('\n');
        }
        return s.ToString();
    }

    /// <summary>0x8D1CD0: the weight of a curve at a frame, given the weight of the previous frame.</summary>
    public static float CurveAt((int Frame, int Value)[] p, int frame, float previous)
    {
        if (p.Length == 0) return 0;
        if (frame < p[0].Frame) return (p[0].Value / 32f - previous) / (p[0].Frame - frame) + previous;
        if (frame >= p[^1].Frame) return p[^1].Value / 32f;
        var i = 0;
        while (p[i + 1].Frame < frame) i++;
        var f = (frame - p[i].Frame) / (float)(p[i + 1].Frame - p[i].Frame);
        return (p[i + 1].Value * f + (1 - f) * p[i].Value) / 32f;
    }

    /// <summary>The key spacing in frames at 60 per second: "2" when even, "2-4" when it changes.</summary>
    public static string Spacing(AnimClip c)
    {
        if (c.Keys < 2) return "";
        var gaps = Enumerable.Range(1, c.Keys - 1).Select(k => (int)MathF.Round((c.Times[k] - c.Times[k - 1]) * 60)).ToList();
        return gaps.Min() == gaps.Max() ? $"{gaps.Min()}" : $"{gaps.Min()}-{gaps.Max()}";
    }

    /// <summary>The root and the rotations at time t: linear between the two keys around it, spherical for the rotations.</summary>
    public static (Vector3 Root, Quaternion[] Rotations) Sample(AnimClip c, float t)
    {
        var k = 0;
        while (k + 2 < c.Keys && c.Times[k + 1] < t) k++;
        if (c.Keys == 1 || t <= c.Times[0]) return (c.Root[0], c.Rotations[0]);
        if (t >= c.Times[^1]) return (c.Root[^1], c.Rotations[^1]);
        var f = (t - c.Times[k]) / (c.Times[k + 1] - c.Times[k]);
        var rot = new Quaternion[Bones];
        for (var b = 0; b < Bones; b++)
            rot[b] = Quaternion.Slerp(Quaternion.Normalize(c.Rotations[k][b]), Quaternion.Normalize(c.Rotations[k + 1][b]), f);
        return (Vector3.Lerp(c.Root[k], c.Root[k + 1], f), rot);
    }

    static Quaternion Quat(byte[] d, int at)
    {
        float C(int i) => BitConverter.ToInt16(d, at + 2 * i) / 32767f;
        return new Quaternion(C(0), C(1), C(2), C(3));
    }

    /// <summary>World positions of the bones at one key (game axes: y grows downward).</summary>
    public static Vector3[] Pose(AnimClip c, int key)
    {
        var pos = new Vector3[Bones]; var rot = new Quaternion[Bones];
        for (var b = 0; b < Bones; b++)
        {
            var q = Quaternion.Normalize(c.Rotations[key][b]);
            if (Parent[b] < 0) { pos[b] = c.Root[key]; rot[b] = q; continue; }
            var p = Parent[b];
            pos[b] = pos[p] + Vector3.Transform(Offset[b], rot[p]);
            rot[b] = rot[p] * q; // child rotation applied first, then the parent's
        }
        return pos;
    }

    /// <summary>Heights above the floor (y = 0) over the keys, a check of the rotation convention: ankles low..high, then the head.</summary>
    public static string Heights(AnimClip c)
    {
        float lo = float.MaxValue, hi = float.MinValue, headLo = float.MaxValue, headHi = float.MinValue;
        for (var k = 0; k < c.Keys; k++)
        {
            var p = Pose(c, k);
            foreach (var b in new[] { 12, 13 }) { lo = Math.Min(lo, -p[b].Y); hi = Math.Max(hi, -p[b].Y); }
            headLo = Math.Min(headLo, -p[16].Y); headHi = Math.Max(headHi, -p[16].Y);
        }
        return string.Create(CultureInfo.InvariantCulture, $"ankles {lo:F0}..{hi:F0} head {headLo:F0}..{headHi:F0}");
    }

    /// <summary>
    /// BVH at 30 frames per second (the keys resampled: their spacing varies), right-handed with Y up: the game's world is left-handed with y down, so it is mirrored in y
    /// (positions and offsets (x, −y, z), rotations (−x, y, −z, w)); the player looks as on screen, his right leg on his right.
    /// </summary>
    public static string ToBvh(AnimClip c)
    {
        var s = new StringBuilder("HIERARCHY\n");
        var order = new List<int>();
        void Joint(int b, int depth)
        {
            var pad = new string('\t', depth);
            order.Add(b);
            s.Append(pad).Append(b == 0 ? "ROOT " : "JOINT ").Append(Names[b]).Append('\n').Append(pad).Append("{\n");
            s.Append(pad).Append('\t').Append("OFFSET ").Append(V(Mirror(Offset[b]))).Append('\n');
            s.Append(pad).Append('\t').Append(b == 0 ? "CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation\n" : "CHANNELS 3 Zrotation Xrotation Yrotation\n");
            var children = Enumerable.Range(0, Bones).Where(x => Parent[x] == b).ToList();
            foreach (var child in children) Joint(child, depth + 1);
            if (children.Count == 0) s.Append(pad).Append("\tEnd Site\n").Append(pad).Append("\t{\n").Append(pad).Append("\t\tOFFSET 20 0 0\n").Append(pad).Append("\t}\n");
            s.Append(pad).Append("}\n");
        }
        Joint(0, 0);

        var count = (int)MathF.Round(c.Duration * Fps) + 1;
        s.Append(CultureInfo.InvariantCulture, $"MOTION\nFrames: {count}\nFrame Time: {1.0 / Fps:F6}\n");
        for (var i = 0; i < count; i++)
        {
            var (root, rotations) = Sample(c, i / (float)Fps);
            var line = new List<string>();
            foreach (var b in order)
            {
                var q = Mirror(Quaternion.Normalize(rotations[b]));
                if (b == 0) line.Add(V(Mirror(root)));
                var (z, x, y) = EulerZxy(q);
                line.Add(F(z) + " " + F(x) + " " + F(y));
            }
            s.Append(string.Join(' ', line)).Append('\n');
        }
        return s.ToString();
    }

    /// <summary>Angles in degrees with q = Rz(z) · Rx(x) · Ry(y), the order BVH applies "Zrotation Xrotation Yrotation".</summary>
    public static (float Z, float X, float Y) EulerZxy(Quaternion q)
    {
        var m = Matrix4x4.CreateFromQuaternion(q); // row-vector matrix: m.Mij is the column-vector matrix's [j][i]
        float M(int r, int col) => (r, col) switch
        {
            (0, 1) => m.M21, (1, 1) => m.M22, (2, 0) => m.M13, (2, 1) => m.M23, (2, 2) => m.M33, _ => throw new ArgumentException(),
        };
        var x = MathF.Asin(Math.Clamp(M(2, 1), -1f, 1f));
        var z = MathF.Atan2(-M(0, 1), M(1, 1));
        var y = MathF.Atan2(-M(2, 0), M(2, 2));
        const float deg = 180f / MathF.PI;
        return (z * deg, x * deg, y * deg);
    }

    public static Vector3 Mirror(Vector3 v) => new(v.X, -v.Y, v.Z);
    public static Quaternion Mirror(Quaternion q) => new(-q.X, q.Y, -q.Z, q.W); // the same rotation seen in the y mirror
    static string F(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => F(v.X) + " " + F(v.Y) + " " + F(v.Z);
}
