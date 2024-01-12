using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace VMASharp.Metadata
{
	internal sealed class BlockMetadata_Generic : IBlockMetadata
	{

		private readonly List<LinkedListNode<Suballocation>> freeSuballocationsBySize = new();

		private readonly LinkedList<Suballocation> suballocations = new();

		private int freeCount;

		public BlockMetadata_Generic( long blockSize )
		{
			Size = blockSize;
			freeCount = 1;
			SumFreeSize = blockSize;

			Debug.Assert( blockSize > Helpers.MinFreeSuballocationSizeToRegister );

			var node = suballocations.AddLast( new Suballocation( 0, blockSize ) );

			freeSuballocationsBySize.Add( node );
		}

		public long Size { get; }

		public int AllocationCount => suballocations.Count - freeCount;

		public long SumFreeSize { get; private set; }

		public long UnusedRangeSizeMax
		{
			get
			{
				var count = freeSuballocationsBySize.Count;

				if ( count != 0 )
				{
					return freeSuballocationsBySize[count - 1].ValueRef.Size;
				}

				return 0;
			}
		}

		public bool IsEmpty => suballocations.Count == 1 && freeCount == 1;

		public void Alloc( in AllocationRequest request, SuballocationType type, long allocSize, BlockAllocation allocation )
		{
			Debug.Assert( request.Type == AllocationRequestType.Normal );
			Debug.Assert( request.Item != null );

			if ( request.Item is not LinkedListNode<Suballocation> requestNode )
			{
				throw new InvalidOperationException();
			}

			Debug.Assert( ReferenceEquals( requestNode.List, suballocations ) );

			ref var suballoc = ref requestNode.ValueRef;

			Debug.Assert( suballoc.Type == SuballocationType.Free );
			Debug.Assert( request.Offset >= suballoc.Offset );

			var paddingBegin = request.Offset - suballoc.Offset;

			Debug.Assert( suballoc.Size >= paddingBegin + allocSize );

			var paddingEnd = suballoc.Size - paddingBegin - allocSize;

			UnregisterFreeSuballocation( requestNode );

			suballoc.Offset = request.Offset;
			suballoc.Size = allocSize;
			suballoc.Type = type;
			suballoc.Allocation = allocation;

			if ( paddingEnd > 0 )
			{
				var newNode = suballocations.AddAfter( requestNode, new Suballocation( request.Offset + allocSize, paddingEnd ) );
				RegisterFreeSuballocation( newNode );
			}

			if ( paddingBegin > 0 )
			{
				var newNode = suballocations.AddBefore( requestNode, new Suballocation( request.Offset - paddingBegin, paddingBegin ) );
				RegisterFreeSuballocation( newNode );
			}

			if ( paddingBegin > 0 )
			{
				if ( paddingEnd > 0 )
				{
					freeCount += 1;
				}
			}
			else if ( paddingEnd <= 0 )
			{
				freeCount -= 1;
			}

			SumFreeSize -= allocSize;
		}

		public void CheckCorruption( nuint blockDataPointer )
		{
			throw new NotImplementedException();
		}

		public bool TryCreateAllocationRequest( in AllocationContext context, out AllocationRequest request )
		{
			request = default;

			request.Type = AllocationRequestType.Normal;

			if ( context.CanMakeOtherLost == false && SumFreeSize < context.AllocationSize + 2 * Helpers.DebugMargin )
			{
				return false;
			}

			var contextCopy = context;
			contextCopy.CanMakeOtherLost = false;

			var freeSuballocCount = freeSuballocationsBySize.Count;
			if ( freeSuballocCount > 0 )
			{
				if ( context.Strategy == AllocationStrategyFlags.BestFit )
				{
					var index = freeSuballocationsBySize.FindIndex( context.AllocationSize + 2 * Helpers.DebugMargin, ( node, size ) => node.ValueRef.Size >= size );

					for ( ; index < freeSuballocCount; ++index )
					{
						var suballocNode = freeSuballocationsBySize[index];

						if ( CheckAllocation( in contextCopy, suballocNode, ref request ) )
						{
							request.Item = suballocNode;
							return true;
						}
					}
				}
				else if ( context.Strategy == Helpers.InternalAllocationStrategy_MinOffset )
				{
					for ( var node = suballocations.First; node != null; node = node.Next )
					{
						if ( node.Value.Type == SuballocationType.Free
							&& CheckAllocation( in contextCopy, node, ref request ) )
						{
							request.Item = node;
							return true;
						}
					}
				}
				else //Worst Fit, First Fit
				{
					for ( var i = freeSuballocCount; i >= 0; --i )
					{
						var item = freeSuballocationsBySize[i];

						if ( CheckAllocation( in contextCopy, item, ref request ) )
						{
							request.Item = item;
							return true;
						}
					}
				}
			}

			if ( context.CanMakeOtherLost )
			{
				var found = false;
				AllocationRequest tmpRequest = default;

				for ( var tNode = suballocations.First; tNode != null; tNode = tNode.Next )
				{
					if ( CheckAllocation( in context, tNode, ref tmpRequest ) )
					{
						if ( context.Strategy == AllocationStrategyFlags.FirstFit )
						{
							request = tmpRequest;
							request.Item = tNode;
							break;
						}

						if ( !found || tmpRequest.CalcCost() < request.CalcCost() )
						{
							request = tmpRequest;
							request.Item = tNode;
							found = true;
						}
					}
				}

				return found;
			}

			return false;
		}

		public void Free( BlockAllocation allocation )
		{
			for ( var node = suballocations.First; node != null; node = node.Next )
			{
				ref var suballoc = ref node.ValueRef;

				if ( ReferenceEquals( suballoc.Allocation, allocation ) )
				{
					FreeSuballocation( node );
					return;
				}
			}

			throw new InvalidOperationException( "Allocation not found!" );
		}

		public void FreeAtOffset( long offset )
		{
			for ( var node = suballocations.First; node != null; node = node.Next )
			{
				ref var suballoc = ref node.ValueRef;

				if ( suballoc.Offset == offset )
				{
					FreeSuballocation( node );
					return;
				}
			}

			throw new InvalidOperationException( "Allocation not found!" );
		}

		public int MakeAllocationsLost( int currentFrame, int frameInUseCount )
		{
			var lost = 0;

			for ( var node = suballocations.First; node != null; node = node.Next )
			{
				ref var suballoc = ref node.ValueRef;

				if ( suballoc.Type != SuballocationType.Free &&
					suballoc.Allocation!.CanBecomeLost &&
					suballoc.Allocation.MakeLost( currentFrame, frameInUseCount ) )
				{
					node = FreeSuballocation( node );
					lost += 1;
				}
			}

			return lost;
		}

		public bool MakeRequestedAllocationsLost( int currentFrame, int frameInUseCount, ref AllocationRequest request )
		{
			if ( request.Type != AllocationRequestType.Normal )
			{
				throw new ArgumentException( "Allocation Request Type was not normal" );
			}

			var tNode = request.Item as LinkedListNode<Suballocation> ?? throw new InvalidOperationException();

			while ( request.ItemsToMakeLostCount > 0 )
			{
				if ( tNode.ValueRef.Type == SuballocationType.Free )
				{
					tNode = tNode.Next;
				}

				Debug.Assert( tNode != null );

				ref var suballoc = ref tNode.ValueRef;

				Debug.Assert( suballoc.Allocation != null );
				Debug.Assert( suballoc.Allocation.CanBecomeLost );

				if ( suballoc.Allocation.MakeLost( currentFrame, frameInUseCount ) )
				{
					request.Item = tNode = FreeSuballocation( tNode );
					request.ItemsToMakeLostCount -= 1;
				}
				else
				{
					return false;
				}
			}

			Debug.Assert( request.Item != null );
			Debug.Assert( Unsafe.As<LinkedListNode<Suballocation>>( request.Item ).ValueRef.Type == SuballocationType.Free );

			return true;
		}

		public void Validate()
		{
			Helpers.Validate( suballocations.Count > 0 );

			long calculatedOffset = 0, calculatedSumFreeSize = 0;
			int calculatedFreeCount = 0, freeSuballocationsToRegister = 0;

			var prevFree = false;

			foreach ( var subAlloc in suballocations )
			{
				Helpers.Validate( subAlloc.Offset == calculatedOffset );

				var currFree = subAlloc.Type == SuballocationType.Free;

				if ( currFree )
				{
					Helpers.Validate( !prevFree );
					Helpers.Validate( subAlloc.Allocation == null );

					calculatedSumFreeSize += subAlloc.Size;
					calculatedFreeCount += 1;

					if ( subAlloc.Size >= Helpers.MinFreeSuballocationSizeToRegister )
					{
						freeSuballocationsToRegister += 1;
					}

					Helpers.Validate( subAlloc.Size >= Helpers.DebugMargin );
				}
				else
				{
					Helpers.Validate( subAlloc.Allocation != null );
					Helpers.Validate( subAlloc.Allocation!.Offset == subAlloc.Offset );
					Helpers.Validate( subAlloc.Allocation.Size == subAlloc.Size );
					Helpers.Validate( Helpers.DebugMargin == 0 || prevFree );
				}

				calculatedOffset += subAlloc.Size;
				prevFree = currFree;
			}

			Helpers.Validate( freeSuballocationsBySize.Count == freeSuballocationsToRegister );

			ValidateFreeSuballocationList();

			Helpers.Validate( calculatedOffset == Size );
			Helpers.Validate( calculatedSumFreeSize == SumFreeSize );
			Helpers.Validate( calculatedFreeCount == freeCount );
		}

		public void CalcAllocationStatInfo( out StatInfo outInfo )
		{
			outInfo = default;

			outInfo.BlockCount = 1;

			var rangeCount = suballocations.Count;
			outInfo.AllocationCount = rangeCount - freeCount;
			outInfo.UnusedRangeCount = freeCount;

			outInfo.UnusedBytes = SumFreeSize;
			outInfo.UsedBytes = Size - outInfo.UnusedBytes;

			outInfo.AllocationSizeMin = long.MaxValue;
			outInfo.AllocationSizeMax = 0;
			outInfo.UnusedRangeSizeMin = long.MaxValue;
			outInfo.UnusedRangeSizeMax = 0;

			for ( var node = suballocations.First; node != null; node = node.Next )
			{
				ref var item = ref node.ValueRef;

				if ( item.Type != SuballocationType.Free )
				{
					if ( item.Size < outInfo.AllocationSizeMin )
						outInfo.AllocationSizeMin = item.Size;

					if ( item.Size > outInfo.AllocationSizeMax )
						outInfo.AllocationSizeMax = item.Size;
				}
				else
				{
					if ( item.Size < outInfo.UnusedRangeSizeMin )
						outInfo.UnusedRangeSizeMin = item.Size;

					if ( item.Size > outInfo.UnusedRangeSizeMax )
						outInfo.UnusedRangeSizeMax = item.Size;
				}
			}
		}

		public void AddPoolStats( ref PoolStats stats )
		{
			var rangeCount = suballocations.Count;

			stats.Size += Size;

			stats.UnusedSize += SumFreeSize;

			stats.AllocationCount += rangeCount - freeCount;

			stats.UnusedRangeCount += freeCount;

			var tmp = UnusedRangeSizeMax;

			if ( tmp > stats.UnusedRangeSizeMax )
				stats.UnusedRangeSizeMax = tmp;
		}

		[Conditional( "DEBUG" )]
		private void ValidateFreeSuballocationList()
		{
			long lastSize = 0;

			for ( int i = 0, count = freeSuballocationsBySize.Count; i < count; ++i )
			{
				var node = freeSuballocationsBySize[i];

				Helpers.Validate( node.ValueRef.Type == SuballocationType.Free );
				Helpers.Validate( node.ValueRef.Size >= Helpers.MinFreeSuballocationSizeToRegister );
				Helpers.Validate( node.ValueRef.Size >= lastSize );

				lastSize = node.ValueRef.Size;
			}
		}

		private bool CheckAllocation( in AllocationContext context, LinkedListNode<Suballocation> node, ref AllocationRequest request )
		{
			if ( context.AllocationSize <= 0 )
			{
				throw new ArgumentOutOfRangeException( nameof( context.AllocationSize ) );
			}

			if ( context.SuballocationType == SuballocationType.Free )
			{
				throw new ArgumentException( "Invalid Allocation Type", nameof( context.SuballocationType ) );
			}

			if ( context.BufferImageGranularity <= 0 )
			{
				throw new ArgumentOutOfRangeException( nameof( context.BufferImageGranularity ) );
			}

			request.ItemsToMakeLostCount = 0;
			request.SumFreeSize = 0;
			request.SumItemSize = 0;

			ref var suballocItem = ref node.ValueRef;

			if ( context.CanMakeOtherLost )
			{
				if ( suballocItem.Type == SuballocationType.Free )
				{
					request.SumFreeSize = suballocItem.Size;
				}
				else
				{
					if ( suballocItem.Allocation.CanBecomeLost && suballocItem.Allocation.LastUseFrameIndex + context.FrameInUseCount < context.CurrentFrame )
					{
						request.ItemsToMakeLostCount += 1;
						request.SumItemSize = suballocItem.Size;
					}
					else
					{
						return false;
					}
				}

				if ( Size - suballocItem.Offset < context.AllocationSize )
				{
					return false;
				}

				var offset = Helpers.DebugMargin > 0 ? suballocItem.Offset + Helpers.DebugMargin : suballocItem.Offset;

				request.Offset = Helpers.AlignUp( offset, context.AllocationAlignment );

				AccountForBackwardGranularityConflict( node, context.BufferImageGranularity, context.SuballocationType, ref request );

				if ( request.Offset >= suballocItem.Offset + suballocItem.Size )
				{
					return false;
				}

				var paddingBegin = request.Offset - suballocItem.Offset;
				var requiredEndMargin = Helpers.DebugMargin;
				var totalSize = paddingBegin + context.AllocationSize + requiredEndMargin;

				if ( suballocItem.Offset + totalSize > Size )
				{
					return false;
				}

				var prevNode = node;

				if ( totalSize > suballocItem.Size )
				{
					var remainingSize = totalSize - suballocItem.Size;
					while ( remainingSize > 0 )
					{
						if ( prevNode.Next == null )
						{
							return false;
						}

						prevNode = prevNode.Next;

						ref var tmpSuballoc = ref prevNode.ValueRef; //TODO: Make better reference to this variable

						if ( prevNode.ValueRef.Type == SuballocationType.Free )
						{
							request.SumFreeSize += prevNode.ValueRef.Size;
						}
						else
						{
							Debug.Assert( prevNode.ValueRef.Allocation != null );

							if ( tmpSuballoc.Allocation.CanBecomeLost && tmpSuballoc.Allocation.LastUseFrameIndex + context.FrameInUseCount < context.CurrentFrame )
							{
								request.ItemsToMakeLostCount += 1;

								request.SumItemSize += tmpSuballoc.Size;
							}
							else
							{
								return false;
							}
						}

						remainingSize = tmpSuballoc.Size < remainingSize ? remainingSize - tmpSuballoc.Size : 0;
					}
				}

				if ( context.BufferImageGranularity > 1 )
				{
					var nextNode = prevNode.Next;

					while ( nextNode != null )
					{
						ref var nextItem = ref nextNode.ValueRef;

						if ( Helpers.BlocksOnSamePage( request.Offset, context.AllocationSize, nextItem.Offset, context.BufferImageGranularity ) )
						{
							if ( Helpers.IsBufferImageGranularityConflict( context.SuballocationType, nextItem.Type ) )
							{
								Debug.Assert( nextItem.Allocation != null );

								if ( nextItem.Allocation.CanBecomeLost && nextItem.Allocation.LastUseFrameIndex + context.FrameInUseCount < context.CurrentFrame )
								{
									request.ItemsToMakeLostCount += 1;
								}
								else
								{
									return false;
								}
							}
						}
						else
						{
							break;
						}

						nextNode = nextNode.Next;
					}
				}
			}
			else
			{
				request.SumFreeSize = suballocItem.Size;

				if ( suballocItem.Size < context.AllocationSize )
				{
					return false;
				}

				var offset = suballocItem.Offset;

				if ( Helpers.DebugMargin > 0 )
				{
					offset += Helpers.DebugMargin;
				}

				request.Offset = Helpers.AlignUp( offset, context.AllocationAlignment );

				AccountForBackwardGranularityConflict( node, context.BufferImageGranularity, context.SuballocationType, ref request );

				long paddingBegin = request.Offset - suballocItem.Offset, requiredEndMargin = Helpers.DebugMargin;

				if ( paddingBegin + context.AllocationSize + requiredEndMargin > suballocItem.Size )
				{
					return false;
				}

				if ( context.BufferImageGranularity > 1 )
				{
					var nextNode = node.Next;

					while ( nextNode != null )
					{
						ref var nextItem = ref nextNode.ValueRef;

						if ( Helpers.BlocksOnSamePage( request.Offset, context.AllocationSize, nextItem.Offset, context.BufferImageGranularity ) )
						{
							if ( Helpers.IsBufferImageGranularityConflict( context.SuballocationType, nextItem.Type ) )
							{
								return false;
							}
						}
						else
						{
							break;
						}

						nextNode = nextNode.Next;
					}
				}
			}

			return true;

			static void AccountForBackwardGranularityConflict( LinkedListNode<Suballocation> node, long granularity, SuballocationType suballocType, ref AllocationRequest request )
			{
				if ( granularity == 1 )
				{
					return;
				}

				var prevNode = node;

				while ( prevNode.Previous != null )
				{
					prevNode = prevNode.Previous;

					ref var prevAlloc = ref prevNode.ValueRef;

					if ( Helpers.BlocksOnSamePage( prevAlloc.Offset, prevAlloc.Size, request.Offset, granularity ) )
					{
						if ( Helpers.IsBufferImageGranularityConflict( prevAlloc.Type, suballocType ) )
						{
							request.Offset = Helpers.AlignUp( request.Offset, granularity );
							break;
						}
					}
					else
					{
						break;
					}
				}
			}
		}

		private void MergeFreeWithNext( LinkedListNode<Suballocation> node )
		{
			Debug.Assert( node != null );
			Debug.Assert( ReferenceEquals( node.List, suballocations ) );
			Debug.Assert( node.ValueRef.Type == SuballocationType.Free );

			var nextNode = node.Next;

			Debug.Assert( nextNode != null );
			Debug.Assert( nextNode.ValueRef.Type == SuballocationType.Free );

			ref Suballocation item = ref node.ValueRef, nextItem = ref nextNode.ValueRef;

			item.Size += nextItem.Size;
			freeCount -= 1;
			suballocations.Remove( nextNode );
		}

		private LinkedListNode<Suballocation> FreeSuballocation( LinkedListNode<Suballocation> item )
		{
			ref var suballoc = ref item.ValueRef;

			suballoc.Type = SuballocationType.Free;
			suballoc.Allocation = null;

			freeCount += 1;
			SumFreeSize += suballoc.Size;

			var nextItem = item.Next;
			var prevItem = item.Previous;

			if ( nextItem != null && nextItem.ValueRef.Type == SuballocationType.Free )
			{
				UnregisterFreeSuballocation( nextItem );
				MergeFreeWithNext( item );
			}

			if ( prevItem != null && prevItem.ValueRef.Type == SuballocationType.Free )
			{
				UnregisterFreeSuballocation( prevItem );
				MergeFreeWithNext( prevItem );
				RegisterFreeSuballocation( prevItem );
				return prevItem;
			}

			RegisterFreeSuballocation( item );
			return item;
		}

		private void RegisterFreeSuballocation( LinkedListNode<Suballocation> item )
		{
			Debug.Assert( item.ValueRef.Type == SuballocationType.Free );
			Debug.Assert( item.ValueRef.Size > 0 );

			ValidateFreeSuballocationList();

			if ( item.Value.Size >= Helpers.MinFreeSuballocationSizeToRegister )
			{
				if ( freeSuballocationsBySize.Count == 0 )
				{
					freeSuballocationsBySize.Add( item );
				}
				else
				{
					freeSuballocationsBySize.InsertSorted( item, Helpers.SuballocationNodeItemSizeLess );
				}
			}

			ValidateFreeSuballocationList();
		}

		private void UnregisterFreeSuballocation( LinkedListNode<Suballocation> item )
		{
			Debug.Assert( item.ValueRef.Type == SuballocationType.Free );
			Debug.Assert( item.ValueRef.Size > 0 );

			if ( item.ValueRef.Size >= Helpers.MinFreeSuballocationSizeToRegister )
			{
				var index = freeSuballocationsBySize.BinarySearch_Leftmost( item, Helpers.SuballocationNodeItemSizeLess );

				Debug.Assert( index >= 0 );

				while ( index < freeSuballocationsBySize.Count )
				{
					var tmp = freeSuballocationsBySize[index];

					if ( ReferenceEquals( tmp, item ) )
					{
						break;
					}

					if ( tmp.ValueRef.Size != item.ValueRef.Size )
					{
						throw new InvalidOperationException( "Suballocation Not Found" );
					}

					index += 1;
				}

				freeSuballocationsBySize.RemoveAt( index );

				ValidateFreeSuballocationList();
			}
		}

		public bool IsBufferImageGranularityConflictPossible( long bufferImageGranularity, ref SuballocationType type )
		{
			if ( bufferImageGranularity == 1 || IsEmpty )
			{
				return false;
			}

			var minAlignment = long.MaxValue;
			var typeConflict = false;

			for ( var node = suballocations.First; node != null; node = node.Next )
			{
				ref var suballoc = ref node.ValueRef;

				var thisType = suballoc.Type;

				if ( thisType != SuballocationType.Free )
				{
					minAlignment = Math.Min( minAlignment, suballoc.Allocation.Alignment );

					if ( Helpers.IsBufferImageGranularityConflict( type, thisType ) )
					{
						typeConflict = true;
					}

					type = thisType;
				}
			}

			return typeConflict || minAlignment >= bufferImageGranularity;
		}
	}
}
