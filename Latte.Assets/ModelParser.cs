using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using EngineMesh = Latte.Assets.Mesh;
using AssimpMesh = Silk.NET.Assimp.Mesh;
using Zio;

namespace Latte.Assets;

internal sealed unsafe class ModelParser
{
	private static Assimp Assimp { get; } = Assimp.GetApi();

	private Dictionary<Vertex, uint> VertexMap { get; } = new();
	private ImmutableArray<EngineMesh>.Builder Meshes { get; } = ImmutableArray.CreateBuilder<EngineMesh>();
	private ImmutableArray<Vertex>.Builder Vertices { get; } = ImmutableArray.CreateBuilder<Vertex>();
	private ImmutableArray<uint>.Builder Indices { get; } = ImmutableArray.CreateBuilder<uint>();

	internal static Model FromPath( in UPath path )
	{
		var sceneBytes = FileSystems.Assets.ReadAllBytes( path.ToAbsolute() );
		var extension = path.GetExtensionWithDot();
		if ( extension is null )
			extension = string.Empty;
		else
			extension = extension[1..];

		fixed ( byte* sceneBytesPtr = sceneBytes )
		{
			var scene = Assimp.ImportFileFromMemory( sceneBytesPtr, (uint)sceneBytes.Length,
				(uint)PostProcessPreset.TargetRealTimeMaximumQuality, extension );
			if ( scene is null || scene->MFlags == Assimp.SceneFlagsIncomplete || scene->MRootNode is null )
			{
				Assimp.ReleaseImport( scene );
				throw new ArgumentException( Assimp.GetErrorStringS(), nameof( path ) );
			}

			var builder = new ModelParser();
			builder.ProcessNode( scene->MRootNode, scene );
			Assimp.ReleaseImport( scene );

			builder.Meshes.Capacity = builder.Meshes.Count;
			return new Model( builder.Meshes.MoveToImmutable() );
		}
	}

	private void ProcessNode( Node* node, Scene* scene )
	{
		for ( var meshI = 0; meshI < node->MNumMeshes; meshI++ )
		{
			var mesh = scene->MMeshes[node->MMeshes[meshI]];
			Meshes.Add( ProcessMesh( mesh, scene ) );
		}

		for ( var childIndex = 0; childIndex < node->MNumChildren; childIndex++ )
			ProcessNode( node->MChildren[childIndex], scene );
	}

	private EngineMesh ProcessMesh( AssimpMesh* mesh, Scene* scene )
	{
		for ( uint i = 0; i < mesh->MNumVertices; i++ )
		{
			var position = mesh->MVertices[i];
			var normal = mesh->MNormals[i];
			// FIXME: Look for a better solution.
			Vector3 color = Vector3.Zero;
			if ( mesh->MColors[0] is not null )
				color = new Vector3( mesh->MColors[0][0].X, mesh->MColors[0][0].Y, mesh->MColors[0][0].Z );
			var textureCoordinates = default( Vector2 );

			if ( mesh->MTextureCoords[0] is not null )
			{
				var textureCoordinates3d = mesh->MTextureCoords[0][i];
				// Y needs to be flipped for Vulkan.
				textureCoordinates = new Vector2( textureCoordinates3d.X, 1 - textureCoordinates3d.Y );
			}

			Vertices.Add( new Vertex( position, normal, color, textureCoordinates ) );
		}

		for ( uint i = 0; i < mesh->MNumFaces; i++ )
		{
			var face = mesh->MFaces[i];
			for ( var j = 0; j < face.MNumIndices; j++ )
				Indices.Add( face.MIndices[j] );
		}

		Vertices.Capacity = Vertices.Count;
		Indices.Capacity = Indices.Count;
		return new EngineMesh( Vertices.MoveToImmutable(), Indices.MoveToImmutable() );
	}
}
