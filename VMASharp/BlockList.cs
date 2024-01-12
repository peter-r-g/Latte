using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Silk.NET.Vulkan;
using VMASharp.Defragmentation;
using VMASharp.Metadata;

namespace VMASharp
{

	internal class BlockList : IDisposable
	{
		private const int AllocationTryCount = 32;

		private readonly List<VulkanMemoryBlock> blocks = new();
		private readonly bool explicitBlockSize;

		private readonly Func<long, IBlockMetadata> metaObjectCreate;

		private readonly int minBlockCount, maxBlockCount;
		private readonly ReaderWriterLockSlim mutex = new( LockRecursionPolicy.NoRecursion );

		private bool hasEmptyBlock;
		private uint nextBlockID;

		public BlockList( VulkanMemoryAllocator allocator, VulkanMemoryPool? pool, int memoryTypeIndex,
			long preferredBlockSize, int minBlockCount, int maxBlockCount, long bufferImageGranularity,
			int frameInUseCount, bool explicitBlockSize, Func<long, IBlockMetadata> algorithm )
		{
			Allocator = allocator;
			ParentPool = pool;
			MemoryTypeIndex = memoryTypeIndex;
			PreferredBlockSize = preferredBlockSize;
			this.minBlockCount = minBlockCount;
			this.maxBlockCount = maxBlockCount;
			BufferImageGranularity = bufferImageGranularity;
			FrameInUseCount = frameInUseCount;
			this.explicitBlockSize = explicitBlockSize;

			metaObjectCreate = algorithm;
		}

		public VulkanMemoryAllocator Allocator { get; }

		public VulkanMemoryPool? ParentPool { get; }

		public bool IsCustomPool => ParentPool != null;

		public int MemoryTypeIndex { get; }

		public long PreferredBlockSize { get; }

		public long BufferImageGranularity { get; }

		public int FrameInUseCount { get; }

		public bool IsEmpty
		{
			get
			{
				mutex.EnterReadLock();

				try
				{
					return blocks.Count == 0;
				}
				finally
				{
					mutex.ExitReadLock();
				}
			}
		}

		public bool IsCorruptedDetectionEnabled => false;

		public int BlockCount => blocks.Count;

		public VulkanMemoryBlock this[int index] => blocks[index];

		private IEnumerable<VulkanMemoryBlock> BlocksInReverse //Just gonna take advantage of C#...
		{
			get
			{
				var localList = blocks;

				for ( var index = localList.Count - 1; index >= 0; --index )
				{
					yield return localList[index];
				}
			}
		}

		public void Dispose()
		{
			foreach ( var block in blocks )
			{
				block.Dispose();
			}
		}

		public void CreateMinBlocks()
		{
			if ( blocks.Count > 0 )
			{
				throw new InvalidOperationException( "Block list not empty" );
			}

			for ( var i = 0; i < minBlockCount; ++i )
			{
				var res = CreateBlock( PreferredBlockSize, out _ );

				if ( res != Result.Success )
				{
					throw new AllocationException( "Unable to allocate device memory block", res );
				}
			}
		}

		public void GetPoolStats( out PoolStats stats )
		{
			mutex.EnterReadLock();

			try
			{
				stats = new PoolStats();
				stats.BlockCount = blocks.Count;

				foreach ( var block in blocks )
				{
					Debug.Assert( block != null );

					block.Validate();

					block.MetaData.AddPoolStats( ref stats );
				}
			}
			finally
			{
				mutex.ExitReadLock();
			}
		}

		public Allocation Allocate( int currentFrame, long size, long alignment, in AllocationCreateInfo allocInfo, SuballocationType suballocType )
		{
			mutex.EnterWriteLock();

			try
			{
				return AllocatePage( currentFrame, size, alignment, allocInfo, suballocType );
			}
			finally
			{
				mutex.ExitWriteLock();
			}
		}

		public void Free( Allocation allocation )
		{
			VulkanMemoryBlock? blockToDelete = null;

			var budgetExceeded = false;
			{
				var heapIndex = Allocator.MemoryTypeIndexToHeapIndex( MemoryTypeIndex );
				Allocator.GetBudget( heapIndex, out var budget );
				budgetExceeded = budget.Usage >= budget.Budget;
			}

			mutex.EnterWriteLock();

			try
			{
				var blockAlloc = (BlockAllocation)allocation;

				var block = blockAlloc.Block;

				//Corruption Detection TODO

				if ( allocation.IsPersistantMapped )
				{
					block.Unmap( 1 );
				}

				block.MetaData.Free( blockAlloc );

				block.Validate();

				var canDeleteBlock = blocks.Count > minBlockCount;

				if ( block.MetaData.IsEmpty )
				{
					if ( (hasEmptyBlock || budgetExceeded) && canDeleteBlock )
					{
						blockToDelete = block;
						Remove( block );
					}
				}
				else if ( hasEmptyBlock && canDeleteBlock )
				{
					block = blocks[^1];

					if ( block.MetaData.IsEmpty )
					{
						blockToDelete = block;
						blocks.RemoveAt( blocks.Count - 1 );
					}
				}

				UpdateHasEmptyBlock();
				IncrementallySortBlocks();
			}
			finally
			{
				mutex.ExitWriteLock();
			}

			if ( blockToDelete != null )
			{
				blockToDelete.Dispose();
			}
		}

		public void AddStats( Stats stats )
		{
			var memTypeIndex = MemoryTypeIndex;
			var memHeapIndex = Allocator.MemoryTypeIndexToHeapIndex( memTypeIndex );

			mutex.EnterReadLock();

			try
			{
				foreach ( var block in blocks )
				{
					Debug.Assert( block != null );
					block.Validate();

					block.MetaData.CalcAllocationStatInfo( out var info );
					StatInfo.Add( ref stats.Total, info );
					StatInfo.Add( ref stats.MemoryType[memTypeIndex], info );
					StatInfo.Add( ref stats.MemoryHeap[memHeapIndex], info );
				}
			}
			finally
			{
				mutex.ExitReadLock();
			}
		}

		/// <summary>
		/// </summary>
		/// <param name="currentFrame"></param>
		/// <returns>
		///     Lost Allocation Count
		/// </returns>
		public int MakePoolAllocationsLost( int currentFrame )
		{
			mutex.EnterWriteLock();

			try
			{
				var lostAllocationCount = 0;

				foreach ( var block in blocks )
				{
					Debug.Assert( block != null );

					lostAllocationCount += block.MetaData.MakeAllocationsLost( currentFrame, FrameInUseCount );
				}

				return lostAllocationCount;
			}
			finally
			{
				mutex.ExitWriteLock();
			}
		}

		public Result CheckCorruption()
		{
			throw new NotImplementedException();
		}

		public int CalcAllocationCount()
		{
			var res = 0;

			foreach ( var block in blocks )
			{
				res += block.MetaData.AllocationCount;
			}

			return res;
		}

		public bool IsBufferImageGranularityConflictPossible()
		{
			if ( BufferImageGranularity == 1 )
				return false;

			var lastSuballocType = SuballocationType.Free;

			foreach ( var block in blocks )
			{
				var metadata = block.MetaData as BlockMetadata_Generic;
				Debug.Assert( metadata != null );

				if ( metadata.IsBufferImageGranularityConflictPossible( BufferImageGranularity, ref lastSuballocType ) )
				{
					return true;
				}
			}

			return false;
		}

		private long CalcMaxBlockSize()
		{
			long result = 0;

			for ( var i = blocks.Count - 1; i >= 0; --i )
			{
				var blockSize = blocks[i].MetaData.Size;

				if ( result < blockSize )
				{
					result = blockSize;
				}

				if ( result >= PreferredBlockSize )
				{
					break;
				}
			}

			return result;
		}

		[SkipLocalsInit]
		private Allocation AllocatePage( int currentFrame, long size, long alignment, in AllocationCreateInfo createInfo, SuballocationType suballocType )
		{
			var canMakeOtherLost = (createInfo.Flags & AllocationCreateFlags.CanMakeOtherLost) != 0;
			var mapped = (createInfo.Flags & AllocationCreateFlags.Mapped) != 0;

			long freeMemory;

			{
				var heapIndex = Allocator.MemoryTypeIndexToHeapIndex( MemoryTypeIndex );

				Allocator.GetBudget( heapIndex, out var heapBudget );

				freeMemory = heapBudget.Usage < heapBudget.Budget ? heapBudget.Budget - heapBudget.Usage : 0;
			}

			var canFallbackToDedicated = !IsCustomPool;
			var canCreateNewBlock = (createInfo.Flags & AllocationCreateFlags.NeverAllocate) == 0 && blocks.Count < maxBlockCount && (freeMemory >= size || !canFallbackToDedicated);

			var strategy = createInfo.Strategy;

			//if (this.algorithm == (uint)PoolCreateFlags.LinearAlgorithm && this.maxBlockCount > 1)
			//{
			//    canMakeOtherLost = false;
			//}

			//if (isUpperAddress && (this.algorithm != (uint)PoolCreateFlags.LinearAlgorithm || this.maxBlockCount > 1))
			//{
			//    throw new AllocationException("Upper address allocation unavailable", Result.ErrorFeatureNotPresent);
			//}

			switch ( strategy )
			{
				case 0:
					strategy = AllocationStrategyFlags.BestFit;
					break;
				case AllocationStrategyFlags.BestFit:
				case AllocationStrategyFlags.WorstFit:
				case AllocationStrategyFlags.FirstFit:
					break;
				default:
					throw new AllocationException( "Invalid allocation strategy", Result.ErrorFeatureNotPresent );
			}

			if ( size + 2 * Helpers.DebugMargin > PreferredBlockSize )
			{
				throw new AllocationException( "Allocation size larger than block size", Result.ErrorOutOfDeviceMemory );
			}

			var context = new AllocationContext(
				currentFrame,
				FrameInUseCount,
				BufferImageGranularity,
				size,
				alignment,
				strategy,
				suballocType,
				canMakeOtherLost );

			Allocation? alloc;

			if ( !canMakeOtherLost || canCreateNewBlock )
			{
				var allocFlagsCopy = createInfo.Flags & ~AllocationCreateFlags.CanMakeOtherLost;

				if ( strategy == AllocationStrategyFlags.BestFit )
				{
					foreach ( var block in blocks )
					{
						alloc = AllocateFromBlock( block, in context, allocFlagsCopy, createInfo.UserData );

						if ( alloc != null )
						{
							//Possibly Log here
							return alloc;
						}
					}
				}
				else
				{
					foreach ( var curBlock in BlocksInReverse )
					{
						alloc = AllocateFromBlock( curBlock, in context, allocFlagsCopy, createInfo.UserData );

						if ( alloc != null )
						{
							//Possibly Log here
							return alloc;
						}
					}
				}
			}

			if ( canCreateNewBlock )
			{
				var allocFlagsCopy = createInfo.Flags & ~AllocationCreateFlags.CanMakeOtherLost;

				var newBlockSize = PreferredBlockSize;
				var newBlockSizeShift = 0;
				const int NewBlockSizeShiftMax = 3;

				if ( !explicitBlockSize )
				{
					var maxExistingBlockSize = CalcMaxBlockSize();

					for ( var i = 0; i < NewBlockSizeShiftMax; ++i )
					{
						var smallerNewBlockSize = newBlockSize / 2;
						if ( smallerNewBlockSize > maxExistingBlockSize && smallerNewBlockSize >= size * 2 )
						{
							newBlockSize = smallerNewBlockSize;
							newBlockSizeShift += 1;
						}
						else
						{
							break;
						}
					}
				}

				var newBlockIndex = 0;

				var res = newBlockSize <= freeMemory || !canFallbackToDedicated ? CreateBlock( newBlockSize, out newBlockIndex ) : Result.ErrorOutOfDeviceMemory;

				if ( !explicitBlockSize )
				{
					while ( res < 0 && newBlockSizeShift < NewBlockSizeShiftMax )
					{
						var smallerNewBlockSize = newBlockSize / 2;

						if ( smallerNewBlockSize >= size )
						{
							newBlockSize = smallerNewBlockSize;
							newBlockSizeShift += 1;
							res = newBlockSize <= freeMemory || !canFallbackToDedicated ? CreateBlock( newBlockSize, out newBlockIndex ) : Result.ErrorOutOfDeviceMemory;
						}
						else
						{
							break;
						}
					}
				}

				if ( res == Result.Success )
				{
					var block = blocks[newBlockIndex];

					alloc = AllocateFromBlock( block, in context, allocFlagsCopy, createInfo.UserData );

					if ( alloc != null )
					{
						//Possibly Log here
						return alloc;
					}
				}
			}

			if ( canMakeOtherLost )
			{
				var tryIndex = 0;

				for ( ; tryIndex < AllocationTryCount; ++tryIndex )
				{
					VulkanMemoryBlock? bestRequestBlock = null;

					Unsafe.SkipInit( out AllocationRequest bestAllocRequest );

					var bestRequestCost = long.MaxValue;

					if ( strategy == AllocationStrategyFlags.BestFit )
					{
						foreach ( var curBlock in blocks )
						{
							if ( curBlock.MetaData.TryCreateAllocationRequest( in context, out var request ) )
							{
								var currRequestCost = request.CalcCost();

								if ( bestRequestBlock == null || currRequestCost < bestRequestCost )
								{
									bestRequestBlock = curBlock;
									bestAllocRequest = request;
									bestRequestCost = currRequestCost;

									if ( bestRequestCost == 0 )
										break;
								}
							}
						}
					}
					else
					{
						foreach ( var curBlock in BlocksInReverse )
						{
							if ( curBlock.MetaData.TryCreateAllocationRequest( in context, out var request ) )
							{
								var curRequestCost = request.CalcCost();

								if ( bestRequestBlock == null || curRequestCost < bestRequestCost || strategy == AllocationStrategyFlags.FirstFit )
								{
									bestRequestBlock = curBlock;
									bestRequestCost = curRequestCost;
									bestAllocRequest = request;

									if ( bestRequestCost == 0 || strategy == AllocationStrategyFlags.FirstFit )
									{
										break;
									}
								}
							}
						}
					}

					if ( bestRequestBlock != null )
					{
						if ( mapped )
						{
							bestRequestBlock.Map( 1 );
						}

						if ( bestRequestBlock.MetaData.MakeRequestedAllocationsLost( currentFrame, FrameInUseCount, ref bestAllocRequest ) )
						{
							var talloc = new BlockAllocation( Allocator, Allocator.CurrentFrameIndex );

							bestRequestBlock.MetaData.Alloc( in bestAllocRequest, suballocType, size, talloc );

							UpdateHasEmptyBlock();

							//(allocation as BlockAllocation).InitBlockAllocation();

							try
							{
								bestRequestBlock.Validate(); //Won't be called in release builds
							}
							catch
							{
								talloc.Dispose();
								throw;
							}

							talloc.UserData = createInfo.UserData;

							Allocator.Budget.AddAllocation( Allocator.MemoryTypeIndexToHeapIndex( MemoryTypeIndex ), size );

							//Maybe put memory init and corruption detection here

							return talloc;
						}
					}
					else
					{
						break;
					}
				}

				if ( tryIndex == AllocationTryCount )
				{
					throw new AllocationException( "", Result.ErrorTooManyObjects );
				}
			}

			throw new AllocationException( "Unable to allocate memory" );
		}

		private Allocation? AllocateFromBlock( VulkanMemoryBlock block, in AllocationContext context, AllocationCreateFlags flags, object? userData )
		{
			Debug.Assert( (flags & AllocationCreateFlags.CanMakeOtherLost) == 0 );
			var mapped = (flags & AllocationCreateFlags.Mapped) != 0;

			if ( block.MetaData.TryCreateAllocationRequest( in context, out var request ) )
			{
				Debug.Assert( request.ItemsToMakeLostCount == 0 );

				if ( mapped )
				{
					block.Map( 1 );
				}

				var allocation = new BlockAllocation( Allocator, Allocator.CurrentFrameIndex );

				block.MetaData.Alloc( in request, context.SuballocationType, context.AllocationSize, allocation );

				allocation.InitBlockAllocation( block, request.Offset, context.AllocationAlignment, context.AllocationSize, MemoryTypeIndex,
					context.SuballocationType, mapped, (flags & AllocationCreateFlags.CanBecomeLost) != 0 );

				UpdateHasEmptyBlock();

				block.Validate();

				allocation.UserData = userData;

				Allocator.Budget.AddAllocation( Allocator.MemoryTypeIndexToHeapIndex( MemoryTypeIndex ), context.AllocationSize );

				return allocation;
			}

			return null;
		}

		private unsafe Result CreateBlock( long blockSize, out int newBlockIndex )
		{
			newBlockIndex = -1;

			var info = new MemoryAllocateInfo
			{
				SType = StructureType.MemoryAllocateInfo,
				MemoryTypeIndex = (uint)MemoryTypeIndex,
				AllocationSize = (ulong)blockSize
			};

			// Every standalone block can potentially contain a buffer with BufferUsageFlags.BufferUsageShaderDeviceAddressBitKhr - always enable the feature
			var allocFlagsInfo = new MemoryAllocateFlagsInfoKHR( StructureType.MemoryAllocateFlagsInfoKhr );
			if ( Allocator.UseKhrBufferDeviceAddress )
			{
				allocFlagsInfo.Flags = MemoryAllocateFlags.MemoryAllocateDeviceAddressBitKhr;
				info.PNext = &allocFlagsInfo;
			}

			var res = Allocator.AllocateVulkanMemory( in info, out var mem );

			if ( res < 0 )
			{
				return res;
			}

			var metaObject = metaObjectCreate( blockSize );

			if ( metaObject.Size != blockSize )
			{
				throw new InvalidOperationException( "Returned Metadata object reports incorrect block size" );
			}

			var block = new VulkanMemoryBlock( Allocator, ParentPool, MemoryTypeIndex, mem, nextBlockID++, metaObject );

			blocks.Add( block );

			newBlockIndex = blocks.Count - 1;

			return Result.Success;
		}

		private void FreeEmptyBlocks( ref DefragmentationStats stats )
		{
			for ( var i = blocks.Count - 1; i >= 0; --i )
			{
				var block = blocks[i];

				if ( block.MetaData.IsEmpty )
				{
					if ( blocks.Count > minBlockCount )
					{
						stats.DeviceMemoryBlocksFreed += 1;
						stats.BytesFreed += block.MetaData.Size;

						blocks.RemoveAt( i );
						block.Dispose();
					}
					else
					{
						break;
					}
				}
			}

			UpdateHasEmptyBlock();
		}

		private void UpdateHasEmptyBlock()
		{
			hasEmptyBlock = false;

			foreach ( var block in blocks )
			{
				if ( block.MetaData.IsEmpty )
				{
					hasEmptyBlock = true;
					break;
				}
			}
		}

		private void Remove( VulkanMemoryBlock block )
		{
			var res = blocks.Remove( block );
			Debug.Assert( res, "" );
		}

		private void IncrementallySortBlocks()
		{
			if ( (uint)blocks.Count > 1 )
			{
				var prevBlock = blocks[0];
				var i = 1;

				do
				{
					var curBlock = blocks[i];

					if ( prevBlock.MetaData.SumFreeSize > curBlock.MetaData.SumFreeSize )
					{
						blocks[i - 1] = curBlock;
						blocks[i] = prevBlock;
						return;
					}

					prevBlock = curBlock;
					i += 1;
				} while ( i < blocks.Count );
			}
		}

		public class DefragmentationContext
		{
			private readonly BlockList List;

			public DefragmentationContext( BlockList list )
			{
				List = list;
			}

			//public void Defragment(DefragmentationStats stats, DefragmentationFlags flags, ulong maxCpuBytesToMove, )

			//public void End(DefragmentationStats stats)

			//public uint ProcessDefragmentations(DefragmentationPassMoveInfo move, uint maxMoves)

			//public void CommitDefragmentations(DefragmentationStats stats)
		}
	}
}
