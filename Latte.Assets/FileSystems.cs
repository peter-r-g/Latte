using System.IO;
using Zio;
using Zio.FileSystems;

namespace Latte.Assets;

public static class FileSystems
{
	public static IFileSystem System { get; private set; } = new PhysicalFileSystem();

	public static IFileSystem Program { get; private set; } = new SubFileSystem(
		System,
		System.ConvertPathFromInternal( Directory.GetCurrentDirectory() ) );

	public static IFileSystem Assets => InternalAssets;
	internal static AggregateFileSystem InternalAssets { get; private set; } = new( Program );
}
