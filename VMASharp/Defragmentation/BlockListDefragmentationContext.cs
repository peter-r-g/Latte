using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace VMASharp.Defragmentation
{
	internal class BlockListDefragmentationContext
	{

		public readonly List<BlockDefragmentationContext> blockContexts = new();
		public readonly List<DefragmentationMove> DefragMoves = new();

		public int DefragMovesProcessed, DefragMovedCommitted;
		public bool HasDefragmentationPlanned;
		public bool MutexLocked;
		public Result Result;


		public BlockListDefragmentationContext( VulkanMemoryAllocator allocator, VulkanMemoryPool? customPool, BlockList list, uint currentFrame )
		{
		}

		public VulkanMemoryPool? CustomPool { get; }

		public BlockList BlockList { get; }

		public DefragmentationAlgorithm Algorithm { get; }
	}
}
