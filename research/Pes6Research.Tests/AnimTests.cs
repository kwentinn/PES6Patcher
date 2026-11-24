using System.Numerics;
using Pes6Research;

namespace Pes6Research.Tests;

public class AnimTests
{
    // A type-2 descriptor as the game keeps it: 56-byte header, keys of 164 bytes, then the key table (count first).
    static byte[] Descriptor(int keys, uint flags = 0x20000002)
    {
        var size = keys * AnimFormat.KeySize + 28; // from anim (descriptor + 0x1C) to the table, plus the table
        var d = new byte[AnimFormat.HeaderSize + size];
        BitConverter.GetBytes(flags).CopyTo(d, 0x20);
        BitConverter.GetBytes(AnimFormat.KeySize * keys + 0x1C).CopyTo(d, 0x2C);
        BitConverter.GetBytes(keys).CopyTo(d, AnimFormat.HeaderSize + keys * AnimFormat.KeySize);
        for (var k = 0; k < keys; k++)
        {
            var at = AnimFormat.HeaderSize + k * AnimFormat.KeySize;
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
        BitConverter.GetBytes((short)-16384).CopyTo(d, AnimFormat.HeaderSize + AnimFormat.KeySize + 20 + 4 * 8 + 2); // key 1, bone 5, y

        var clip = AnimFormat.Decode(d, out _)!;

        Assert.Equal(3, clip.Keys);
        Assert.Equal(new Vector3(20, -450, 0), clip.Root[2]);
        Assert.Equal(1f, clip.Rotations[0][0].W);
        Assert.Equal(-16384 / 32767f, clip.Rotations[1][5].Y);
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
