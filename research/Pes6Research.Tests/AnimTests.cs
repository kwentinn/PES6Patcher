using System.Numerics;
using Pes6Research;

namespace Pes6Research.Tests;

public class AnimTests
{
    // A type-2 descriptor as the game keeps it: 0x1C bytes (word 0x18 = offset of the animation block), maybe an event section,
    // the block (flags, duration, offset of the key table), keys of 164 bytes, then the key table: count, then per key (time, offset)
    // where entry j holds the time of key j + 1 and the last one 0. Keys 1/30 s apart unless times are given (key 0 at 0 first).
    static byte[] Descriptor(int keys, uint flags = 0x20000002, int anim = 0x1C, float[]? times = null)
    {
        times ??= Enumerable.Range(0, keys).Select(k => k / 30f).ToArray();
        var table = AnimFormat.FirstKey + keys * AnimFormat.KeySize; // from the block
        var d = new byte[anim + table + 4 + 8 * keys];
        BitConverter.GetBytes(anim).CopyTo(d, AnimFormat.AnimOffset);
        BitConverter.GetBytes(0x20011119).CopyTo(d, anim);
        BitConverter.GetBytes(flags).CopyTo(d, anim + 4);
        BitConverter.GetBytes(times[^1]).CopyTo(d, anim + 8);
        BitConverter.GetBytes(table).CopyTo(d, anim + 0x10);
        BitConverter.GetBytes(keys).CopyTo(d, anim + table);
        for (var j = 0; j < keys; j++)
        {
            BitConverter.GetBytes(j + 1 < keys ? times[j + 1] : 0f).CopyTo(d, anim + table + 4 + 8 * j);
            BitConverter.GetBytes(AnimFormat.FirstKey + ((j + 1) % keys) * AnimFormat.KeySize).CopyTo(d, anim + table + 8 + 8 * j);
        }
        for (var k = 0; k < keys; k++)
        {
            var at = anim + AnimFormat.FirstKey + k * AnimFormat.KeySize;
            BitConverter.GetBytes((short)32767).CopyTo(d, at + 6); // root rotation w = 1
            BitConverter.GetBytes(10f * k).CopyTo(d, at + 8);
            BitConverter.GetBytes(-450f).CopyTo(d, at + 12);
            for (var b = 1; b < AnimFormat.Bones; b++) BitConverter.GetBytes((short)32767).CopyTo(d, at + 20 + (b - 1) * 8 + 6);
        }
        return d;
    }

    [Fact]
    public void Decodes_the_keys_of_a_type_2_descriptor()
    {
        var d = Descriptor(3);
        BitConverter.GetBytes((short)-16384).CopyTo(d, 0x1C + AnimFormat.FirstKey + AnimFormat.KeySize + 20 + 4 * 8 + 2); // key 1, bone 5, y

        var clip = AnimFormat.Decode(d, out _)!;

        Assert.Equal(3, clip.Keys);
        Assert.Equal(new Vector3(20, -450, 0), clip.Root[2]);
        Assert.Equal(1f, clip.Rotations[0][0].W);
        Assert.Equal(-16384 / 32767f, clip.Rotations[1][5].Y);
    }

    [Fact]
    public void Finds_the_block_after_an_event_section_and_the_key_times()
    {
        var times = Enumerable.Range(0, 18).Select(k => k / 15f).ToArray(); // as animation 1117: 18 keys at 15 Hz, 68 frames
        var clip = AnimFormat.Decode(Descriptor(18, anim: 0xD4, times: times), out var why);

        Assert.True(clip != null, why);
        Assert.Equal(18, clip.Keys);
        Assert.Equal(68 / 60f, clip.Duration, 5);
        Assert.Equal(new Vector3(170, -450, 0), clip.Root[17]);
        Assert.Equal("4", AnimFormat.Spacing(clip));
    }

    [Fact]
    public void The_bvh_is_resampled_at_30_frames_per_second_when_the_spacing_changes()
    {
        var clip = AnimFormat.Decode(Descriptor(3, times: [0, 2 / 30f, 3 / 30f]), out _)!; // as animation 48: 4 frames, then 2

        var lines = AnimFormat.ToBvh(clip).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("2-4", AnimFormat.Spacing(clip));
        Assert.Contains("Frames: 4", lines);
        Assert.Contains("Frame Time: 0.033333", lines);
        Assert.Equal(["0.0000", "5.0000", "10.0000", "20.0000"], lines[^4..].Select(l => l.Split(' ')[0])); // root x, keys at 0, 10, 20
    }

    [Fact]
    public void Reads_the_event_curves_and_weighs_them_as_the_game()
    {
        // Event section at 0x1C: 1 track, offset table at 8, track at +0x0C; the track: id, kind 4, 2 channels, 20 frames, curve offsets.
        var d = Descriptor(2, anim: 0x60);
        BitConverter.GetBytes((ushort)0x1C).CopyTo(d, 0x16);
        BitConverter.GetBytes(1).CopyTo(d, 0x1C); BitConverter.GetBytes(8).CopyTo(d, 0x20); BitConverter.GetBytes(0x0C).CopyTo(d, 0x24);
        var track = 0x28;
        BitConverter.GetBytes(0x29843918).CopyTo(d, track); d[track + 4] = 4; d[track + 5] = 2; BitConverter.GetBytes((ushort)20).CopyTo(d, track + 6);
        BitConverter.GetBytes((ushort)0x0C).CopyTo(d, track + 8); BitConverter.GetBytes((ushort)0x14).CopyTo(d, track + 10);
        ushort Point(int frame, int value) => (ushort)(frame | value << 11);
        foreach (var (o, w) in new[] { (0x0C, 3), (0x0E, Point(0, 0)), (0x10, Point(10, 16)), (0x12, Point(20, 0)), (0x14, 1), (0x16, Point(0, 31)) })
            BitConverter.GetBytes((ushort)w).CopyTo(d, track + o);

        var events = AnimFormat.Events(d);

        Assert.Single(events);
        Assert.Equal([(0, 0), (10, 16), (20, 0)], events[0][0]);
        Assert.Equal(0.25f, AnimFormat.CurveAt(events[0][0], 5, 0));
        Assert.Equal(0.5f, AnimFormat.CurveAt(events[0][0], 10, 0));
        Assert.Equal(0f, AnimFormat.CurveAt(events[0][0], 25, 0.7f));
        Assert.Equal(0.25f, AnimFormat.CurveAt([(4, 16)], 2, 0)); // toward the first point (0.5): half of the way when two frames are left
        var csv = AnimFormat.EventsCsv(events, 20 / 60f).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("frame,face_0,face_1", csv[0]);
        Assert.Equal("10,0.500,0.969", csv[6]); // frame 10 at 30 per second: the 6th row
        Assert.Empty(AnimFormat.Events(Descriptor(2)));
    }

    [Theory]
    [InlineData(0x10000002u)] // another type
    [InlineData(0x20000000u)] // float quaternions
    public void Leaves_the_other_formats_to_the_raw_file(uint flags)
    {
        Assert.Null(AnimFormat.Decode(Descriptor(2, flags), out var why));
        Assert.Contains("type", why);
    }

    [Fact]
    public void At_rest_a_bone_sits_at_the_sum_of_its_offsets()
    {
        var pose = AnimFormat.Pose(AnimFormat.Decode(Descriptor(1), out _)!, 0);

        Assert.Equal(new Vector3(82.4f + 161.92f + 177.76f, -450 + 14.72f, 37.6f), pose[12]); // root, pelvis, hip, knee, ankle
    }

    [Fact]
    public void Euler_angles_rebuild_the_rotation()
    {
        var random = new Random(6);
        for (var i = 0; i < 200; i++)
        {
            var q = Quaternion.Normalize(new Quaternion(random.NextSingle() - .5f, random.NextSingle() - .5f, random.NextSingle() - .5f, random.NextSingle() - .5f));
            var (z, x, y) = AnimFormat.EulerZxy(q);
            const float rad = MathF.PI / 180;
            var back = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, z * rad) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, x * rad) *
                       Quaternion.CreateFromAxisAngle(Vector3.UnitY, y * rad);
            Assert.True(MathF.Abs(Quaternion.Dot(q, back)) > 0.9999f, $"{q} -> {z} {x} {y}");
        }
    }

    [Fact]
    public void The_mirrored_rotation_moves_the_mirrored_point_to_the_mirrored_place()
    {
        var random = new Random(7);
        for (var i = 0; i < 100; i++)
        {
            var q = Quaternion.Normalize(new Quaternion(random.NextSingle() - .5f, random.NextSingle() - .5f, random.NextSingle() - .5f, random.NextSingle() - .5f));
            var v = new Vector3(random.NextSingle() * 100, random.NextSingle() * 100, random.NextSingle() * 100);

            var expected = AnimFormat.Mirror(Vector3.Transform(v, q));
            var actual = Vector3.Transform(AnimFormat.Mirror(v), AnimFormat.Mirror(q));

            Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"{q} {v}: {expected} != {actual}");
        }
    }

    [Fact]
    public void The_bvh_has_every_bone_and_one_line_per_key_with_y_up()
    {
        var bvh = AnimFormat.ToBvh(AnimFormat.Decode(Descriptor(2), out _)!);
        var lines = bvh.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(AnimFormat.Bones - 1, lines.Count(l => l.TrimStart().StartsWith("JOINT ")));
        Assert.Contains("Frames: 2", bvh);
        var first = lines[^2].Split(' ');
        Assert.Equal(3 + 3 * AnimFormat.Bones, first.Length);
        Assert.Equal("450.0000", first[1]); // the game's y grows downward
    }
}
