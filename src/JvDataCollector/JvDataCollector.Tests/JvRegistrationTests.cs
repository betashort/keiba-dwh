namespace JvDataCollector.Tests;

public class JvRegistrationTests
{
    [Fact]
    public void ImageBitnessFollowsThePeMachine()
    {
        var dir = Directory.CreateTempSubdirectory("jvreg");
        try
        {
            var i386 = Path.Combine(dir.FullName, "i386.dll");
            var amd64 = Path.Combine(dir.FullName, "amd64.dll");
            File.WriteAllBytes(i386, Image(0x014C));
            File.WriteAllBytes(amd64, Image(0x8664));
            Assert.False(JvRegistration.Is64BitImage(i386));
            Assert.True(JvRegistration.Is64BitImage(amd64));
            Assert.False(JvRegistration.Is64BitImage(Path.Combine(dir.FullName, "missing.dll")));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    private static byte[] Image(ushort machine)
    {
        var bytes = new byte[0x46];
        bytes[0] = 0x4D;
        bytes[1] = 0x5A;
        bytes[0x3C] = 0x40;
        bytes[0x40] = 0x50;
        bytes[0x41] = 0x45;
        bytes[0x44] = (byte)(machine & 0xFF);
        bytes[0x45] = (byte)(machine >> 8);
        return bytes;
    }
}
