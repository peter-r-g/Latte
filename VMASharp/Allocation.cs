#pragma warning disable CA1063

using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VMASharp
{
	/// <summary>
	///     The object containing details on a suballocation of Vulkan Memory
	/// </summary>
	public abstract unsafe class Allocation : IDisposable
	{
		protected long alignment;
		private int lastUseFrameIndex;
		private bool LostOrDisposed;
		protected int mapCount;
		protected int memoryTypeIndex;

		protected long size;

		internal Allocation( VulkanMemoryAllocator allocator, int currentFrameIndex )
		{
			Allocator = allocator;
			lastUseFrameIndex = currentFrameIndex;
		}

		internal VulkanMemoryAllocator Allocator { get; }

		protected Vk VkApi => Allocator.VkApi;

		/// <summary>
		///     Size of this allocation, in bytes.
		///     Value never changes, unless allocation is lost.
		/// </summary>
		public long Size
		{
			get
			{
				if ( LostOrDisposed || lastUseFrameIndex == Helpers.FrameIndexLost )
				{
					return 0;
				}

				return size;
			}
		}

		/// <summary>
		///     Memory type index that this allocation is from. Value does not change.
		/// </summary>
		public int MemoryTypeIndex => memoryTypeIndex;

		/// <summary>
		///     Handle to Vulkan memory object.
		///     Same memory object can be shared by multiple allocations.
		///     It can change after call to vmaDefragment() if this allocation is passed to the function, or if allocation is lost.
		///     If the allocation is lost, it is equal to `VK_NULL_HANDLE`.
		/// </summary>
		public abstract DeviceMemory DeviceMemory { get; }

		/// <summary>
		///     Offset into deviceMemory object to the beginning of this allocation, in bytes. (deviceMemory, offset) pair is
		///     unique to this allocation.
		///     It can change after call to vmaDefragment() if this allocation is passed to the function, or if allocation is lost.
		/// </summary>
		public abstract long Offset { get; internal set; }

		internal abstract bool CanBecomeLost { get; }


		internal bool IsPersistantMapped => mapCount < 0;

		internal int LastUseFrameIndex => lastUseFrameIndex;

		internal long Alignment => alignment;

		public object? UserData { get; set; }

		/// <summary>
		///     If this allocation is mapped, returns a pointer to the mapped memory region. Returns Null otherwise.
		/// </summary>
		public abstract IntPtr MappedData { get; }

		public void Dispose()
		{
			if ( !LostOrDisposed )
			{
				Allocator.FreeMemory( this );
				LostOrDisposed = true;
			}
		}

		public Result BindBufferMemory( Buffer buffer )
		{
			Debug.Assert( Offset >= 0 );

			return Allocator.BindVulkanBuffer( buffer, DeviceMemory, Offset, null );
		}

		public Result BindBufferMemory( Buffer buffer, long allocationLocalOffset, IntPtr pNext )
		{
			return BindBufferMemory( buffer, allocationLocalOffset, (void*)pNext );
		}

		public Result BindBufferMemory( Buffer buffer, long allocationLocalOffset, void* pNext = null )
		{
			if ( (ulong)allocationLocalOffset >= (ulong)Size )
			{
				throw new ArgumentOutOfRangeException( nameof( allocationLocalOffset ) );
			}

			return Allocator.BindVulkanBuffer( buffer, DeviceMemory, Offset + allocationLocalOffset, pNext );
		}

		public Result BindImageMemory( Image image )
		{
			return Allocator.BindVulkanImage( image, DeviceMemory, Offset, null );
		}

		public Result BindImageMemory( Image image, long allocationLocalOffset, IntPtr pNext )
		{
			return BindImageMemory( image, allocationLocalOffset, (void*)pNext );
		}

		public Result BindImageMemory( Image image, long allocationLocalOffset, void* pNext = null )
		{
			if ( (ulong)allocationLocalOffset >= (ulong)Size )
			{
				throw new ArgumentOutOfRangeException( nameof( allocationLocalOffset ) );
			}

			return Allocator.BindVulkanImage( image, DeviceMemory, Offset + allocationLocalOffset, pNext );
		}

		internal bool MakeLost( int currentFrame, int frameInUseCount )
		{
			if ( !CanBecomeLost )
			{
				throw new InvalidOperationException( "Internal Exception, tried to make an allocation lost that cannot become lost." );
			}

			var localLastUseFrameIndex = lastUseFrameIndex;

			while ( true )
			{
				if ( localLastUseFrameIndex == Helpers.FrameIndexLost )
				{
					Debug.Assert( false );
					return false;
				}

				if ( localLastUseFrameIndex + frameInUseCount >= currentFrame )
				{
					return false;
				}

				var tmp = Interlocked.CompareExchange( ref lastUseFrameIndex, Helpers.FrameIndexLost, localLastUseFrameIndex );

				if ( tmp == localLastUseFrameIndex )
				{
					LostOrDisposed = true;
					return true;
				}

				localLastUseFrameIndex = tmp;
			}
		}

		public bool TouchAllocation()
		{
			if ( LostOrDisposed )
			{
				return false;
			}

			var currFrameIndexLoc = Allocator.CurrentFrameIndex;
			var lastUseFrameIndexLoc = lastUseFrameIndex;

			if ( CanBecomeLost )
			{
				while ( true )
				{
					if ( lastUseFrameIndexLoc == Helpers.FrameIndexLost )
					{
						return false;
					}

					if ( lastUseFrameIndexLoc == currFrameIndexLoc )
					{
						return true;
					}

					lastUseFrameIndexLoc = Interlocked.CompareExchange( ref lastUseFrameIndex, currFrameIndexLoc, lastUseFrameIndexLoc );
				}
			}

			while ( true )
			{
				Debug.Assert( lastUseFrameIndexLoc != Helpers.FrameIndexLost );

				if ( lastUseFrameIndexLoc == currFrameIndexLoc )
					break;

				lastUseFrameIndexLoc = Interlocked.CompareExchange( ref lastUseFrameIndex, currFrameIndexLoc, lastUseFrameIndexLoc );
			}

			return true;
		}

		/// <summary>
		///     Flushes a specified region of memory
		/// </summary>
		/// <param name="offset">Offset in this allocation</param>
		/// <param name="size">Size of region to flush</param>
		/// <returns>The result of the operation</returns>
		public Result Flush( long offset, long size )
		{
			return Allocator.FlushOrInvalidateAllocation( this, offset, size, CacheOperation.Flush );
		}

		/// <summary>
		///     Invalidates a specified region of memory
		/// </summary>
		/// <param name="offset">Offset in this allocation</param>
		/// <param name="size">Size of region to Invalidate</param>
		/// <returns>The result of the operation</returns>
		public Result Invalidate( long offset, long size )
		{
			return Allocator.FlushOrInvalidateAllocation( this, offset, size, CacheOperation.Invalidate );
		}

		public abstract IntPtr Map();

		public abstract void Unmap();

		public bool TryGetMemory<T>( out Memory<T> memory ) where T : unmanaged
		{
			if ( mapCount != 0 )
			{
				var size = checked((int)Size);

				if ( size >= sizeof( T ) )
				{
					memory = new UnmanagedMemoryManager<T>( (byte*)MappedData, size / sizeof( T ) ).Memory;

					return true;
				}
			}

			memory = Memory<T>.Empty;
			return false;
		}

		public bool TryGetSpan<T>( out Span<T> span ) where T : unmanaged
		{
			if ( mapCount != 0 )
			{
				var size = checked((int)Size);

				if ( size >= sizeof( T ) )
				{
					span = new Span<T>( (void*)MappedData, size / sizeof( T ) );

					return true;
				}
			}

			span = Span<T>.Empty;
			return false;
		}

		private sealed class UnmanagedMemoryManager<T> : MemoryManager<T> where T : unmanaged
		{
			private readonly int ElementCount;
			private readonly T* Pointer;

			public UnmanagedMemoryManager( void* ptr, int elemCount )
			{
				Pointer = (T*)ptr;
				ElementCount = elemCount;
			}

			protected override void Dispose( bool disposing )
			{
			}

			public override Span<T> GetSpan()
			{
				return new Span<T>( Pointer, ElementCount );
			}

			public override MemoryHandle Pin( int elementIndex = 0 )
			{
				return new MemoryHandle( Pointer + elementIndex );
			}

			public override void Unpin()
			{
			}
		}
	}
}
