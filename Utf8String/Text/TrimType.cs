namespace System.Text;

[Flags]
public enum TrimType : byte
{
	Head = 1,
	Tail = 2,
	Both = Head | Tail,
}