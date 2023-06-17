using Latte.Windowing.Backend.Vulkan;
using Silk.NET.Vulkan;
using System;
using System.Collections.Immutable;

namespace Latte.Windowing.Assets;

/// <summary>
/// Represents a mesh of a 3D model.
/// </summary>
public sealed class Mesh
{
	/// <summary>
	/// The vertices of the mesh.
	/// </summary>
	public ImmutableArray<Vertex> Vertices { get; }
	/// <summary>
	/// The indices of the mesh.
	/// </summary>
	public ImmutableArray<uint> Indices { get; }

	/// <summary>
	/// The vertex buffer that stores the <see cref="Vertices"/> on the GPU.
	/// </summary>
	internal GPUBuffer<Vertex>? VulkanVertexBuffer { get; private set; }
	/// <summary>
	/// The index buffer that stores the <see cref="Indices"/> on the GPU.
	/// </summary>
	internal GPUBuffer<uint>? VulkanIndexBuffer { get; private set; }

	/// <summary>
	/// Flags that represent what renderers have initialized this meshes renderer specific data.
	/// </summary>
	internal RenderingBackend InitializedFlags { get; private set; }

	/// <summary>
	/// Initializes a new instance of <see cref="Mesh"/>.
	/// </summary>
	/// <param name="vertices">The vertices of the mesh.</param>
	/// <param name="indices">The indices of the mesh.</param>
	public Mesh( in ImmutableArray<Vertex> vertices, in ImmutableArray<uint> indices )
	{
		Vertices = vertices;
		Indices = indices;
	}

	/// <summary>
	/// Initializes a new instance of <see cref="Mesh"/>.
	/// </summary>
	/// <param name="vertices">The vertices of the mesh.</param>
	public Mesh( in ImmutableArray<Vertex> vertices )
	{
		Vertices = vertices;
		Indices = ImmutableArray<uint>.Empty;
	}

	/// <summary>
	/// Initializes renderer specific data on a mesh.
	/// </summary>
	/// <param name="backend">The renderer to initialize for.</param>
	/// <exception cref="InvalidOperationException">Thrown when the mesh has already been initialized for the renderer.</exception>
	public void Initialize( IRenderingBackend backend )
	{
		if ( backend is not VulkanBackend vulkanBackend )
			return;

		if ( InitializedFlags.HasFlag( RenderingBackend.Vulkan ) )
			throw new InvalidOperationException( "This mesh has already been initialized for usage in Vulkan" );

		VulkanVertexBuffer = new GPUBuffer<Vertex>( vulkanBackend, Vertices.AsSpan(), BufferUsageFlags.VertexBufferBit );
		if ( Indices.Length > 0 )
			VulkanIndexBuffer = new GPUBuffer<uint>( vulkanBackend, Indices.AsSpan(), BufferUsageFlags.IndexBufferBit );
		InitializedFlags |= RenderingBackend.Vulkan;
	}
}
