using Latte.NewRenderer.Renderer.Vulkan.Exceptions;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Latte.NewRenderer.Renderer.Vulkan;

internal sealed class VkQueue : IDisposable
{
	internal Queue Queue { get; private set; }
	internal uint QueueFamily { get; private set; }

	private int currentSubmissionId;
	private bool disposed;

	private readonly Thread workerThread;
	private readonly Queue<VkQueueSubmission> queueSubmissions = new();
	private readonly Queue<VkPresentQueueSubmission> presentQueueSubmissions = new();
	private readonly HashSet<int> completedSubmissions = [];
	private readonly object queueLock = new();
	private readonly AutoResetEvent submissionAvailableEvent = new( false );
	private readonly AutoResetEvent submissionCompletedEvent = new( false );

	internal VkQueue( Queue queue, uint queueFamily )
	{
		Queue = queue;
		QueueFamily = queueFamily;

		workerThread = new Thread( QueueMain )
		{
			IsBackground = true
		};
		workerThread.Start();
	}

	internal int Submit( SubmitInfo submitInfo, Fence fence )
	{
		ObjectDisposedException.ThrowIf( disposed, this );

		int submissionId;
		lock ( queueLock )
		{
			submissionId = currentSubmissionId++;
			queueSubmissions.Enqueue( new VkQueueSubmission
			{
				SubmissionId = submissionId,
				SubmitInfos = new SubmitInfo[] { submitInfo },
				Fence = fence
			} );
		}

		submissionAvailableEvent.Set();
		return submissionId;
	}

	internal int SubmitPresent( PresentInfoKHR presentInfo )
	{
		ObjectDisposedException.ThrowIf( disposed, this );

		int submissionId;
		lock ( queueLock )
		{
			submissionId = currentSubmissionId++;
			presentQueueSubmissions.Enqueue( new VkPresentQueueSubmission
			{
				SubmissionId = submissionId,
				PresentInfo = presentInfo
			} );
		}

		submissionAvailableEvent.Set();
		return submissionId;
	}

	internal void WaitForSubmission( int submissionId )
	{
		while ( true )
		{
			if ( !submissionCompletedEvent.WaitOne( TimeSpan.FromMicroseconds( 1 ) ) )
				continue;

			lock ( queueLock )
			{
				if ( !completedSubmissions.Contains( submissionId ) )
				{
					submissionCompletedEvent.Set();
					continue;
				}

				completedSubmissions.Remove( submissionId );
				return;
			}
		}
	}

	internal void SubmitAndWait( SubmitInfo submitInfo, Fence fence )
	{
		var submissionId = Submit( submitInfo, fence );
		WaitForSubmission( submissionId );
	}

	internal void SubmitPresentAndWait( PresentInfoKHR presentInfo )
	{
		var submissionId = SubmitPresent( presentInfo );
		WaitForSubmission( submissionId );
	}

	private void QueueMain()
	{
		while ( !disposed )
		{
			if ( !submissionAvailableEvent.WaitOne( TimeSpan.FromMicroseconds( 1 ) ) )
				continue;

			if ( !VkContext.IsInitialized )
				throw new VkException( $"{nameof( VkContext )} has not been initialized" );

			while ( queueSubmissions.Count > 0 )
			{
				VkQueueSubmission submission;
				lock ( queueLock )
					submission = queueSubmissions.Dequeue();

				Apis.Vk.QueueSubmit( Queue, submission.SubmitInfos.Span, submission.Fence );

				lock ( queueLock )
					completedSubmissions.Add( submission.SubmissionId );

				submissionCompletedEvent.Set();
			}

			while ( presentQueueSubmissions.Count > 0 )
			{
				VkPresentQueueSubmission submission;
				lock ( queueLock )
					submission = presentQueueSubmissions.Dequeue();

				if ( !VkContext.Extensions.TryGetExtension<KhrSwapchain>( out var swapchainExtension ) )
					throw new VkException( $"Failed to get {KhrSwapchain.ExtensionName} extension" );

				swapchainExtension.QueuePresent( Queue, submission.PresentInfo );

				lock ( queueLock )
					completedSubmissions.Add( submission.SubmissionId );

				submissionCompletedEvent.Set();
			}
		}
	}

	private void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		if ( disposing )
		{
		}

		disposed = true;
		Apis.Vk.QueueWaitIdle( Queue );
		workerThread.Join();
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}

	[method: SetsRequiredMembers]
	private readonly struct VkQueueSubmission( int submissionId, ReadOnlyMemory<SubmitInfo> submitInfos, Fence fence )
	{
		internal required int SubmissionId { get; init; } = submissionId;
		internal required ReadOnlyMemory<SubmitInfo> SubmitInfos { get; init; } = submitInfos;
		internal required Fence Fence { get; init; } = fence;
	}

	[method: SetsRequiredMembers]
	private readonly struct VkPresentQueueSubmission( int submissionId, PresentInfoKHR presentInfo )
	{
		internal required int SubmissionId { get; init; } = submissionId;
		internal required PresentInfoKHR PresentInfo { get; init; } = presentInfo;
	}
}
