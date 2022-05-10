namespace Utf8String.UnitTests;

public class Construction
{
	[Fact]
	public void InvalidData()
	{
		Assert.Throws<ArgumentException>(() => new System.Utf8String(stackalloc char[] { "😀"[0], 'h' }));
	}
}