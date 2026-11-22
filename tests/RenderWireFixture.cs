using System.Globalization;
using System.Numerics;
using LumaWall.Contracts;

internal static class RenderWireFixture
{
    // Wire 37 carries all visual stages followed by the two reserved stage IDs.
    public static int OrderOffset(string[] fields)
    {
        var stages=EffectOrder.Default.Select(kind=>BitOperations.TrailingZeroCount((uint)kind)
            .ToString(CultureInfo.InvariantCulture)).ToHashSet();
        int count=stages.Count+2;
        for(int pos=1; pos+count<fields.Length; pos++)
        {
            if(fields[pos]==count.ToString(CultureInfo.InvariantCulture)
                &&fields[pos+count-1]=="2"&&fields[pos+count]=="22"
                &&stages.SetEquals(fields.Skip(pos+1).Take(stages.Count))) return pos;
        }
        throw new InvalidDataException("Missing visual stage order in render fixture.");
    }

    public static string[] Legacy25(string wire)
    {
        var fields=wire.Split(' ');
        var parameters=fields[..OrderOffset(fields)].ToList();
        int maskCountIndex=211+int.Parse(parameters[210],CultureInfo.InvariantCulture);
        int firstMaskLink=maskCountIndex+1+13*int.Parse(parameters[maskCountIndex],CultureInfo.InvariantCulture);
        // The first 21 mask links existed in wire 25. The current format has 25.
        parameters.RemoveRange(firstMaskLink+21,4);
        parameters[0]="25";
        return parameters.ToArray();
    }
}
