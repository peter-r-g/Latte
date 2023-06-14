using Latte.Windowing.Assets;
using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using EngineMesh = Latte.Windowing.Assets.Mesh;
using AssimpMesh = Silk.NET.Assimp.Mesh;

namespace Latte.Windowing.Backend;

internal sealed unsafe class ModelParser
{
	private Dictionary<Vertex, uint> VertexMap { get; } = new();
	private ImmutableArray<EngineMesh>.Builder Meshes { get; } = ImmutableArray.CreateBuilder<EngineMesh>();
	private ImmutableArray<Vertex>.Builder Vertices { get; } = ImmutableArray.CreateBuilder<Vertex>();
	private ImmutableArray<uint>.Builder Indices { get; } = ImmutableArray.CreateBuilder<uint>();

	public static Model FromPath( string path )
	{
		var scene = Apis.Assimp.ImportFile( path, (uint)PostProcessPreset.TargetRealTimeMaximumQuality );
		if ( scene is null || scene->MFlags == Assimp.SceneFlagsIncomplete || scene->MRootNode is null )
		{
			Apis.Assimp.ReleaseImport( scene );
			throw new ArgumentException( Apis.Assimp.GetErrorStringS(), nameof( path ) );
		}

		var builder = new ModelParser();
		builder.ProcessNode( scene->MRootNode, scene );
		Apis.Assimp.ReleaseImport( scene );

		builder.Meshes.Capacity = builder.Meshes.Count;
		return new Model( builder.Meshes.MoveToImmutable() );
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
			var vertex = new Vertex
			{
				Position = mesh->MVertices[i]
			};

			if ( mesh->MTextureCoords[0] is not null )
			{
				var textureCoordinates3d = mesh->MTextureCoords[0][i];
				// Y needs to be flipped for Vulkan.
				vertex.TextureCoordinates = new Vector2( textureCoordinates3d.X, 1 - textureCoordinates3d.Y );
			}

			Vertices.Add( vertex );
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
