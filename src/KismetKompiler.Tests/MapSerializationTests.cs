using UAssetAPI;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.UnrealTypes;

namespace KismetKompiler.Tests;

[TestClass]
public sealed class MapSerializationTests
{
    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4)]
    public void SetMapWritesPairCount(int pairs)
    {
        using var stream = new MemoryStream();
        using var writer = new AssetBinaryWriter(stream, new UAsset(EngineVersion.VER_UE4_27));
        var expression = new EX_SetMap
        {
            MapProperty = new EX_NoObject(),
            Elements = Enumerable.Range(0, pairs * 2)
                .Select(n => (KismetExpression)new EX_IntConst { Value = n }).ToArray(),
        };
        expression.Write(writer);
        writer.Flush();
        // EX_NoObject is one byte; the next four bytes encode entry pairs.
        Assert.AreEqual(pairs, BitConverter.ToInt32(stream.ToArray(), 1));
    }

    [TestMethod]
    public void SetMapRejectsAnUnpairedKey()
    {
        using var stream = new MemoryStream();
        using var writer = new AssetBinaryWriter(stream, new UAsset(EngineVersion.VER_UE4_27));
        var expression = new EX_SetMap
        {
            MapProperty = new EX_NoObject(),
            Elements = new KismetExpression[] { new EX_IntConst { Value = 1 } },
        };
        Assert.ThrowsException<InvalidOperationException>(() => expression.Write(writer));
    }
}
